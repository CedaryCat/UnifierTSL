using System.Collections.Immutable;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Bindings;
using UnifierTSL.Commanding.Endpoints;

namespace UnifierTSL.Commanding.Composition
{
    internal sealed record CommandSystemRegistration(
        long Id,
        Action<CommandRegistrationBuilder> Configure);

    internal sealed record CommandSystemState
    {
        public static CommandSystemState Empty { get; } = new();

        public ImmutableArray<CommandControllerGroupRegistration> ControllerGroups { get; init; } = [];

        public ImmutableArray<Type> EndpointTypes { get; init; } = [];

        public CommandCatalog Catalog { get; init; } = new();

        public CommandEndpointCatalog EndpointCatalog { get; init; } = new();

        public ImmutableArray<CommandOutcomeWriterRegistration> OutcomeWriters { get; init; } = [];
    }

    public sealed class CommandInstallHandle : IDisposable
    {
        private readonly long registrationId;
        private int disposed;

        internal CommandInstallHandle(long registrationId) {
            this.registrationId = registrationId;
        }

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }

            CommandSystem.Uninstall(registrationId);
        }
    }

    public static class CommandSystem
    {
        private static readonly Lock SyncLock = new();
        private static ImmutableDictionary<long, CommandSystemRegistration> registrations = ImmutableDictionary<long, CommandSystemRegistration>.Empty;
        private static CommandSystemState state = CommandSystemState.Empty;
        private static long nextRegistrationId;

        public static CommandInstallHandle Install(Action<CommandRegistrationBuilder> configure) {
            ArgumentNullException.ThrowIfNull(configure);

            long registrationId;
            lock (SyncLock) {
                registrationId = checked(++nextRegistrationId);
                registrations = registrations.Add(registrationId, new CommandSystemRegistration(registrationId, configure));
                state = BuildState(registrations.Values);
            }

            return new CommandInstallHandle(registrationId);
        }

        public static CommandCatalog GetCatalog() {
            return Volatile.Read(ref state).Catalog;
        }

        public static CommandEndpointCatalog GetEndpointCatalog() {
            return Volatile.Read(ref state).EndpointCatalog;
        }

        public static ICommandOutcomeWriter<TSink> GetOutcomeWriter<TSink>() {
            var sinkType = typeof(TSink);
            foreach (var registration in Volatile.Read(ref state).OutcomeWriters) {
                if (registration.SinkType != sinkType) {
                    continue;
                }

                return (ICommandOutcomeWriter<TSink>)registration.Writer;
            }

            throw new InvalidOperationException(GetParticularString(
                "{0} is sink type name",
                $"No command outcome writer is registered for sink '{sinkType.FullName}'."));
        }

        internal static void Uninstall(long registrationId) {
            lock (SyncLock) {
                if (!registrations.ContainsKey(registrationId)) {
                    return;
                }

                registrations = registrations.Remove(registrationId);
                state = registrations.Count == 0
                    ? CommandSystemState.Empty
                    : BuildState(registrations.Values);
            }
        }

        private static CommandSystemState BuildState(IEnumerable<CommandSystemRegistration> activeRegistrations) {
            CommandRegistrationBuilder registrationBuilder = new();
            foreach (var registration in activeRegistrations.OrderBy(static registration => registration.Id)) {
                registration.Configure(registrationBuilder);
            }

            BindingOptionsBuilder bindingRegistry = new();
            foreach (var configureBindings in registrationBuilder.GetBindingConfigurators()) {
                configureBindings(bindingRegistry);
            }

            var controllerGroups = registrationBuilder.GetControllerGroups();
            var catalog = CommandSystemDiscovery.DiscoverFromControllerGroups(
                controllerGroups,
                bindingRegistry.Build());
            var endpointTypes = registrationBuilder.GetEndpoints();
            var actionBindingRules = registrationBuilder.GetActionBindingRules();
            ImmutableArray<ICommandEndpoint> endpoints = [.. endpointTypes.Select(CreateEndpoint)];
            var endpointCatalog = CommandEndpointCatalogCompiler.Compile(catalog, endpoints, actionBindingRules);

            ValidateConflicts(catalog.Roots);

            return new CommandSystemState {
                ControllerGroups = controllerGroups,
                EndpointTypes = endpointTypes,
                Catalog = catalog,
                EndpointCatalog = endpointCatalog,
                OutcomeWriters = registrationBuilder.GetOutcomeWriters(),
            };
        }

        private static ICommandEndpoint CreateEndpoint(Type endpointType) {
            if (Activator.CreateInstance(endpointType) is not ICommandEndpoint endpoint) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command endpoint type",
                    $"Command endpoint '{endpointType.FullName}' could not be constructed."));
            }

            return endpoint;
        }

        private static void ValidateConflicts(ImmutableArray<CommandRootDefinition> roots) {
            Dictionary<string, string> seenNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots) {
                foreach (var name in EnumerateNames(root)) {
                    if (seenNames.TryGetValue(name, out var existingRoot)) {
                        throw new InvalidOperationException(GetParticularString(
                            "{0} is command name, {1} is existing root name, {2} is new root name",
                            $"Command name '{name}' conflicts with already registered root '{existingRoot}' while installing '{root.RootName}' through CommandSystem."));
                    }

                    seenNames[name] = root.RootName;
                }
            }
        }

        private static IEnumerable<string> EnumerateNames(CommandRootDefinition root) {
            yield return root.RootName;
            foreach (var alias in root.Aliases) {
                yield return alias;
            }
        }
    }

}
