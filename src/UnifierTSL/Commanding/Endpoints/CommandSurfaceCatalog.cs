using System.Collections.Immutable;
using System.Reflection;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Commanding.Prompting;
using UnifierTSL.Servers;

namespace UnifierTSL.Commanding.Endpoints
{
    public readonly record struct CommandEndpointId
    {
        public CommandEndpointId(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                throw new ArgumentException(GetString("Command endpoint id must not be empty."), nameof(value));
            }

            Value = value.Trim();
        }

        public string Value { get; }

        public override string ToString() {
            return Value;
        }
    }

    public interface ICommandEndpoint
    {
        CommandEndpointId Id { get; }

        ICommandEndpointExecutor Executor { get; }

        ICommandEndpointPresentation? Presentation => null;
    }

    public delegate CommandEndpointActionBinding? CommandEndpointActionBinder(
        CommandEndpointId endpointId,
        CommandActionDefinition action);

    public interface ICommandEndpointExecutor
    {
        Task<CommandOutcome> ExecuteAsync(
            CommandEndpointCatalog catalog,
            CommandEndpointRootBinding root,
            CommandExecutionRequest request,
            CancellationToken cancellationToken = default);
    }

    public interface ICommandEndpointPresentation
    {
        IReadOnlyList<PromptAlternativeSpec> BuildPromptAlternatives(
            CommandEndpointCatalog catalog,
            ServerContext? server);
    }

    public sealed record CommandEndpointRootBinding
    {
        public required CommandEndpointId EndpointId { get; init; }

        public required CommandRootDefinition Root { get; init; }

        public required ImmutableArray<CommandEndpointActionBinding> Actions { get; init; }
    }

    public abstract record CommandEndpointActionBinding
    {
        public required CommandEndpointId EndpointId { get; init; }

        public required CommandActionDefinition Action { get; init; }

        public required CommandAvailability Availability { get; init; }
    }

    internal sealed record CommandEndpointActionBindingRule
    {
        public required Type EndpointType { get; init; }

        public required CommandEndpointActionBinder BindAction { get; init; }
    }

    public sealed record CommandEndpointCatalog
    {
        public ImmutableArray<ICommandEndpoint> Endpoints { get; init; } = [];

        public ImmutableArray<CommandEndpointRootBinding> Roots { get; init; } = [];

        public ICommandEndpoint? FindEndpoint(CommandEndpointId endpointId) {
            return Endpoints.FirstOrDefault(endpoint => endpoint.Id.Equals(endpointId));
        }

        public ImmutableArray<CommandEndpointRootBinding> GetRoots(CommandEndpointId endpointId) {
            return [.. Roots.Where(root => root.EndpointId.Equals(endpointId))];
        }

        public CommandEndpointRootBinding? FindRoot(CommandEndpointId endpointId, string invokedRoot) {
            if (string.IsNullOrWhiteSpace(invokedRoot)) {
                return null;
            }

            var normalized = invokedRoot.Trim();
            return Roots.FirstOrDefault(root =>
                root.EndpointId.Equals(endpointId)
                && (root.Root.RootName.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                    || root.Root.Aliases.Any(alias => alias.Equals(normalized, StringComparison.OrdinalIgnoreCase))));
        }
    }

    internal static class CommandEndpointCatalogCompiler
    {
        private readonly record struct RootKey(
            CommandEndpointId EndpointId,
            string SourceName,
            string RootName,
            Type ControllerType);

        public static CommandEndpointCatalog Compile(
            CommandCatalog catalog,
            ImmutableArray<ICommandEndpoint> endpoints,
            ImmutableArray<CommandEndpointActionBindingRule> actionBindingRules) {
            Dictionary<CommandEndpointId, ICommandEndpoint> registeredEndpoints = [];
            Dictionary<Type, ICommandEndpoint> registeredEndpointTypes = [];
            foreach (var endpoint in endpoints) {
                if (!registeredEndpoints.TryAdd(endpoint.Id, endpoint)) {
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is command endpoint id",
                        $"Command endpoint '{endpoint.Id}' is registered more than once."));
                }

                registeredEndpointTypes.Add(endpoint.GetType(), endpoint);
            }

            List<CommandEndpointRootBinding> roots = [];
            Dictionary<RootKey, int> rootIndexes = [];
            foreach (var rule in actionBindingRules) {
                if (!registeredEndpointTypes.TryGetValue(rule.EndpointType, out var endpoint)) {
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is command endpoint type",
                        $"Command endpoint action binding rule targets endpoint type '{rule.EndpointType.FullName}', but no endpoint definition of that type is registered."));
                }

                foreach (var root in BindRule(catalog, endpoint.Id, rule)) {
                    ValidateRuleRoot(endpoint.Id, root);
                    MergeRuleRoot(root, roots, rootIndexes);
                }
            }

            return new CommandEndpointCatalog {
                Endpoints = endpoints,
                Roots = [.. roots],
            };
        }

        private static IEnumerable<CommandEndpointRootBinding> BindRule(
            CommandCatalog catalog,
            CommandEndpointId endpointId,
            CommandEndpointActionBindingRule rule) {
            foreach (var root in catalog.Roots) {
                ImmutableArray<CommandEndpointActionBinding> actions = [.. root.Actions
                    .Select(action => rule.BindAction(endpointId, action))
                    .Where(static action => action is not null)!];
                if (actions.Length == 0) {
                    continue;
                }

                yield return new CommandEndpointRootBinding {
                    EndpointId = endpointId,
                    Root = root,
                    Actions = actions,
                };
            }
        }

        private static void MergeRuleRoot(
            CommandEndpointRootBinding root,
            List<CommandEndpointRootBinding> roots,
            Dictionary<RootKey, int> rootIndexes) {
            var key = new RootKey(
                root.EndpointId,
                root.Root.SourceName,
                root.Root.RootName,
                root.Root.ControllerType);
            if (!rootIndexes.TryGetValue(key, out var index)) {
                roots.Add(root);
                rootIndexes[key] = roots.Count - 1;
                return;
            }

            var existing = roots[index];
            ValidateDuplicateActions(existing, root);
            roots[index] = existing with {
                Actions = [.. existing.Actions, .. root.Actions],
            };
        }

        private static void ValidateRuleRoot(CommandEndpointId endpointId, CommandEndpointRootBinding root) {
            if (!root.EndpointId.Equals(endpointId)) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is rule endpoint id, {1} is binding endpoint id, {2} is command root name",
                    $"Command endpoint action binding rule for '{endpointId}' returned root binding '{root.Root.RootName}' targeting '{root.EndpointId}'."));
            }

            foreach (var action in root.Actions) {
                if (!action.EndpointId.Equals(endpointId)) {
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is rule endpoint id, {1} is action endpoint id, {2} is command action name",
                        $"Command endpoint action binding rule for '{endpointId}' returned action binding '{action.Action.Method.DeclaringType?.FullName}.{action.Action.Method.Name}' targeting '{action.EndpointId}'."));
                }
            }
        }

        private static void ValidateDuplicateActions(
            CommandEndpointRootBinding existing,
            CommandEndpointRootBinding incoming) {
            HashSet<MethodInfo> existingMethods = [.. existing.Actions.Select(static action => action.Action.Method)];
            foreach (var action in incoming.Actions) {
                if (existingMethods.Add(action.Action.Method)) {
                    continue;
                }

                throw new InvalidOperationException(GetParticularString(
                    "{0} is command endpoint id, {1} is command action name",
                    $"Command endpoint '{existing.EndpointId}' received duplicate bindings for action '{action.Action.Method.DeclaringType?.FullName}.{action.Action.Method.Name}'."));
            }
        }
    }

    internal sealed record TerminalCommandEndpointActionBinding : CommandEndpointActionBinding;

    public sealed class TerminalCommandEndpoint : ICommandEndpoint
    {
        private static readonly CommandEndpointId EndpointIdValue = new("terminal");

        public static CommandEndpointId EndpointId => EndpointIdValue;

        internal static UnavailableActionVisibility UnavailableActionVisibility { get; set; } = UnavailableActionVisibility.HideUnavailable;

        public CommandEndpointId Id => EndpointIdValue;

        public ICommandEndpointExecutor Executor { get; } = new ExecutorImpl();

        public ICommandEndpointPresentation Presentation { get; } = new PresentationImpl();

        internal static CommandEndpointActionBinding? BindAction(
            CommandEndpointId endpointId,
            CommandActionDefinition action) {
            var attribute = action.GetActionAttribute<TerminalCommandAttribute>();
            if (attribute is null) {
                return null;
            }

            return new TerminalCommandEndpointActionBinding {
                EndpointId = endpointId,
                Action = action,
                Availability = CommandAvailability.Terminal(
                    allowLauncherConsole: attribute.AllowLauncherConsole,
                    allowServerConsole: attribute.AllowServerConsole),
            };
        }

        private sealed class ExecutorImpl : ICommandEndpointExecutor
        {
            public Task<CommandOutcome> ExecuteAsync(
                CommandEndpointCatalog catalog,
                CommandEndpointRootBinding root,
                CommandExecutionRequest request,
                CancellationToken cancellationToken = default) {

                var target = request.ExecutionContext.Target;
                return CommandEndpointDispatcher.DispatchAsync(
                    root,
                    request,
                    new CommandEndpointExecutionAdapter {
                        EvaluateAccess = action => action.Availability.Allows(target)
                            ? CommandAccessResult.Allowed
                            : CommandAccessResult.Unavailable,
                    },
                    cancellationToken);
            }
        }

        private sealed class PresentationImpl : ICommandEndpointPresentation
        {
            public IReadOnlyList<PromptAlternativeSpec> BuildPromptAlternatives(
                CommandEndpointCatalog catalog,
                ServerContext? server) {
                var target = server is null
                    ? CommandInvocationTarget.LauncherConsole
                    : CommandInvocationTarget.ServerConsole;
                var visibility = UnavailableActionVisibility;
                List<PromptAlternativeSpec> alternatives = [];

                foreach (var root in catalog.GetRoots(EndpointIdValue)) {
                    List<CommandActionDefinition> visibleActions = [];

                    foreach (var action in root.Actions) {
                        var available = action.Availability.Allows(target);
                        if (!available && visibility == UnavailableActionVisibility.HideUnavailable) {
                            continue;
                        }

                        visibleActions.Add(action.Action);
                    }

                    if (visibleActions.Count == 0) {
                        continue;
                    }

                    alternatives.AddRange(CommandPromptProjection.BuildAlternatives(EndpointIdValue, root.Root, visibleActions));
                }

                return alternatives;
            }
        }
    }

}
