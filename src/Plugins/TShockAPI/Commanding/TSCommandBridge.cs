using UnifierTSL.Surface.Activities;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;
using System.Reflection;
using UnifierTSL.Surface.Status;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Commanding.Endpoints;

namespace TShockAPI.Commanding
{
    internal sealed record TSCommandCatalogEntry
    {
        public required string PrimaryName { get; init; }

        public ImmutableArray<string> Aliases { get; init; } = [];

        public ImmutableArray<string> Permissions { get; init; } = [];

        public string HelpText { get; init; } = string.Empty;

        public ImmutableArray<string> HelpLines { get; init; } = [];

        public bool DoLog { get; init; } = true;

        public bool AllowServer { get; init; } = true;

        public bool AllowCoord { get; init; } = true;

        public bool IsLegacy { get; init; }

        public bool CanRun(CommandExecutor executor) {
            return Permissions.Length == 0 || Permissions.Any(executor.HasPermission);
        }

        public bool MatchesName(string commandName) {
            if (string.IsNullOrWhiteSpace(commandName)) {
                return false;
            }

            var normalizedName = commandName.Trim();
            return PrimaryName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)
                || Aliases.Any(alias => alias.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static class TSCommandBridge
    {
        private readonly record struct DeclarativeRootKey(string SourceName, string RootName, Type ControllerType);

        private sealed record DeclarativeRoot(CommandRootDefinition Root, ImmutableArray<TShockEndpointActionBinding> Actions);

        public static bool TryHandleDirectCommand(CommandExecutor executor, string rawInput) {
            return TryHandleDirectCommand(executor, rawInput, IsSilentInvocation(rawInput));
        }

        public static bool TryHandleDirectCommand(CommandExecutor executor, string rawInput, bool silent) {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(rawInput);

            var endpointId = ResolveEndpointId(executor);
            var request = CreateDispatchRequest(executor, endpointId, rawInput, silent);
            var result = CommandDispatchCoordinator.DispatchAsync(request)
                .GetAwaiter()
                .GetResult();
            if (!result.Matched || result.Root is null || !TShockEndpointBindingFactory.IsTShockRoot(result.Root)) {
                return false;
            }

            AuditDispatch(executor, request, result);
            CommandSystem.GetOutcomeWriter<CommandExecutor>().Write(executor, result.Outcome ?? CommandOutcome.Empty);
            return true;
        }

        public static bool HasDeclarativeRoot(string commandName) {
            return TryFindDeclarativeRoot(commandName, out _);
        }

        public static IReadOnlyList<TSCommandCatalogEntry> GetRegisteredCommandCatalog() {
            List<TSCommandCatalogEntry> entries = [];

            foreach (var command in Commands.GetRegisteredLegacyCommands()) {
                if (command.Names.Count == 0) {
                    continue;
                }

                entries.Add(new TSCommandCatalogEntry {
                    PrimaryName = command.Name,
                    Aliases = [.. command.Names
                        .Skip(1)
                        .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)],
                    Permissions = [.. command.Permissions
                        .Where(static permission => !string.IsNullOrWhiteSpace(permission))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static permission => permission, StringComparer.OrdinalIgnoreCase)],
                    HelpText = command.HelpText ?? string.Empty,
                    HelpLines = command.HelpDesc is null ? [] : [.. command.HelpDesc],
                    DoLog = command.DoLog,
                    AllowServer = command.AllowServer,
                    AllowCoord = command.AllowCoord,
                    IsLegacy = true,
                });
            }

            foreach (var root in GetDeclarativeRoots()) {
                entries.Add(BuildCatalogEntry(root));
            }

            return entries.Count == 0
                ? []
                : [.. entries];
        }

        public static TSCommandCatalogEntry? FindRegisteredCommand(string commandName) {
            return FindRegisteredCommands(commandName).FirstOrDefault();
        }

        public static IReadOnlyList<TSCommandCatalogEntry> FindRegisteredCommands(string commandName) {
            if (string.IsNullOrWhiteSpace(commandName)) {
                return [];
            }

            var normalizedName = commandName.Trim();
            return [.. GetRegisteredCommandCatalog().Where(entry => entry.MatchesName(normalizedName))];
        }

        public static IReadOnlyList<TSCommandCatalogEntry> GetCommandsByPermission(string permission) {
            if (string.IsNullOrWhiteSpace(permission)) {
                return [];
            }

            var normalizedPermission = permission.Trim();
            return [.. GetRegisteredCommandCatalog()
                .Where(entry => entry.Permissions.Any(candidate => candidate.Equals(normalizedPermission, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(static entry => entry.PrimaryName, StringComparer.OrdinalIgnoreCase)];
        }

        public static TSExecutionContext CreateExecutionContext(CommandExecutor executor, bool silent) {
            return new TSExecutionContext(executor, silent, ResolveInvocationTarget(executor));
        }

        public static CommandInvocationTarget ResolveInvocationTarget(CommandExecutor executor) {
            if (executor.IsRest) {
                return TShockCommandInvocationTargets.Rest;
            }

            if (executor.IsClient) {
                return TShockCommandInvocationTargets.Player;
            }

            return executor.SourceServer is null
                ? CommandInvocationTarget.LauncherConsole
                : CommandInvocationTarget.ServerConsole;
        }

        public static CommandEndpointId ResolveEndpointId(CommandExecutor executor) {
            var target = ResolveInvocationTarget(executor);
            if (target.Equals(TShockCommandInvocationTargets.Player)) {
                return TShockCommandEndpoints.Player;
            }
            if (target.Equals(TShockCommandInvocationTargets.Rest)) {
                return TShockCommandEndpoints.Rest;
            }

            return TerminalCommandEndpoint.EndpointId;
        }

        public static CommandDispatchRequest CreateDispatchRequest(
            CommandExecutor executor,
            CommandEndpointId endpointId,
            string rawInput,
            bool silent) {
            var normalizedInput = NormalizeDispatchInput(endpointId, rawInput);
            return new CommandDispatchRequest {
                EndpointId = endpointId,
                ExecutionContext = CreateExecutionContext(executor, silent),
                RawInput = normalizedInput,
                CommandPrefixes = ResolveCommandPrefixes(),
            };
        }

        public static TSConsoleActivityScope BeginTerminalDispatchActivityScope(
            CommandDispatchRequest request,
            CancellationToken cancellationToken = default) {
            if (request.ExecutionContext is not TSExecutionContext executionContext
                || !CommandDispatchCoordinator.TryCreateExecutionRequest(request, out var executionRequest)) {
                return TSConsoleActivityScope.None;
            }

            var root = CommandSystem.GetEndpointCatalog().FindRoot(request.EndpointId, executionRequest.InvokedRoot);
            if (root is null || !ShouldTrackConsoleActivity(executionContext.Target, root)) {
                return TSConsoleActivityScope.None;
            }

            return TShockEndpointBindingFactory.IsTShockRoot(root)
                ? BeginSurfaceActivityScope(executionContext, root, request.RawInput, cancellationToken)
                : BeginGenericConsoleActivityScope(executionContext, root, request.RawInput, executionRequest.InvokedRoot, cancellationToken);
        }

        public static TSConsoleActivityScope BeginSurfaceActivityScope(
            TSExecutionContext context,
            CommandEndpointRootBinding root,
            string rawInput,
            CancellationToken cancellationToken = default) {
            if (!ShouldTrackConsoleActivity(context.Target, root)) {
                return TSConsoleActivityScope.None;
            }

            var metadata = BuildCatalogEntry(root);
            var activityScope = TSConsoleActivityScope.Begin(
                context.Executor,
                root.Root.RootName,
                BuildConsoleActivityMessage(metadata, rawInput),
                cancellationToken: cancellationToken);
            context.ConsoleActivity = activityScope.Activity;
            return activityScope;
        }

        private static TSConsoleActivityScope BeginGenericConsoleActivityScope(
            TSExecutionContext context,
            CommandEndpointRootBinding root,
            string rawInput,
            string invokedRoot,
            CancellationToken cancellationToken = default) {
            var activityScope = TSConsoleActivityScope.Begin(
                context.Executor,
                root.Root.RootName,
                BuildGenericConsoleActivityMessage(root.Root, rawInput, invokedRoot),
                cancellationToken: cancellationToken);
            context.ConsoleActivity = activityScope.Activity;
            return activityScope;
        }

        public static CommandDispatchResult CompleteTerminalDispatch(
            CommandExecutor executor,
            CommandDispatchRequest request,
            CommandDispatchResult result) {
            if (!result.Handled) {
                return result;
            }

            if (!result.Matched) {
                if (Commands.RequiresLegacyDispatch(executor, result.ExecutionRequest?.InvokedRoot)) {
                    Commands.HandleCommand(executor, request.RawInput);
                    return result with { Handled = true };
                }

                CommandSystem.GetOutcomeWriter<CommandExecutor>().Write(
                    executor,
                    CommandOutcome.Error(GetString("Invalid command entered. Type {0}help for a list of valid commands.", Commands.Specifier)));
                return result;
            }

            if (result.Root is not null && TShockEndpointBindingFactory.IsTShockRoot(result.Root)) {
                AuditDispatch(executor, request, result);
            }

            CommandSystem.GetOutcomeWriter<CommandExecutor>().Write(executor, result.Outcome ?? CommandOutcome.Empty);
            return result;
        }

        public static void AuditDispatch(
            CommandExecutor executor,
            CommandDispatchRequest request,
            CommandDispatchResult result) {
            if (!result.Matched || result.Root is null || !TShockEndpointBindingFactory.IsTShockRoot(result.Root)) {
                return;
            }

            var metadata = BuildCatalogEntry(result.Root);
            LogExecutionIfNeeded(
                executor,
                metadata,
                request.RawInput,
                result.ExecutionRequest?.InvokedRoot ?? metadata.PrimaryName);
        }

        public static IReadOnlyList<string> ResolveCommandPrefixes() {
            List<string> prefixes = [];
            try {
                if (!string.IsNullOrWhiteSpace(TShock.Config?.GlobalSettings.CommandSpecifier)) {
                    prefixes.Add(TShock.Config.GlobalSettings.CommandSpecifier);
                }

                if (!string.IsNullOrWhiteSpace(TShock.Config?.GlobalSettings.CommandSilentSpecifier)) {
                    prefixes.Add(TShock.Config.GlobalSettings.CommandSilentSpecifier);
                }
            }
            catch {
            }

            if (prefixes.Count == 0) {
                prefixes.Add("/");
                prefixes.Add(".");
            }

            return prefixes;
        }

        public static string NormalizeDispatchInput(CommandEndpointId endpointId, string rawInput) {
            ArgumentException.ThrowIfNullOrWhiteSpace(rawInput);

            return endpointId == TerminalCommandEndpoint.EndpointId
                ? EnsureCommandPrefix(rawInput)
                : rawInput;
        }

        public static string EnsureCommandPrefix(string rawInput) {
            var normalizedInput = rawInput?.TrimStart() ?? string.Empty;
            foreach (var prefix in ResolveCommandPrefixes().OrderByDescending(static prefix => prefix.Length)) {
                if (normalizedInput.StartsWith(prefix, StringComparison.Ordinal)) {
                    return normalizedInput;
                }
            }

            return $"{Commands.Specifier}{normalizedInput}";
        }

        public static bool IsSilentInvocation(string rawInput) {
            if (string.IsNullOrWhiteSpace(rawInput)) {
                return false;
            }

            var trimmed = rawInput.TrimStart();
            var silentPrefix = Commands.SilentSpecifier;
            return !string.IsNullOrWhiteSpace(silentPrefix)
                && trimmed.StartsWith(silentPrefix, StringComparison.Ordinal)
                && (trimmed.Length == silentPrefix.Length || !char.IsWhiteSpace(trimmed[silentPrefix.Length]));
        }

        private static bool TryFindDeclarativeRoot(string commandName, out DeclarativeRoot? root) {
            root = null;
            if (string.IsNullOrWhiteSpace(commandName)) {
                return false;
            }

            var normalizedName = commandName.Trim();
            root = GetDeclarativeRoots().FirstOrDefault(candidate =>
                candidate.Root.RootName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)
                || candidate.Root.Aliases.Any(alias => alias.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)));
            return root is not null;
        }

        private static IEnumerable<DeclarativeRoot> GetDeclarativeRoots() {
            Dictionary<DeclarativeRootKey, (CommandRootDefinition Root, Dictionary<MethodInfo, TShockEndpointActionBinding> Actions)> grouped = [];
            foreach (var root in CommandSystem.GetEndpointCatalog().Roots.Where(TShockEndpointBindingFactory.IsTShockRoot)) {
                var key = new DeclarativeRootKey(root.Root.SourceName, root.Root.RootName, root.Root.ControllerType);
                if (!grouped.TryGetValue(key, out var entry)) {
                    entry = (root.Root, []);
                    grouped[key] = entry;
                }

                foreach (var action in root.Actions.OfType<TShockEndpointActionBinding>()) {
                    entry.Actions.TryAdd(action.Action.Method, action);
                }
            }

            foreach (var (Root, Actions) in grouped.Values) {
                yield return new DeclarativeRoot(Root, [.. Actions.Values]);
            }
        }

        private static TSCommandCatalogEntry BuildCatalogEntry(CommandEndpointRootBinding root) {
            ImmutableArray<TShockEndpointActionBinding> actions = [.. root.Actions.OfType<TShockEndpointActionBinding>()];
            return BuildCatalogEntry(new DeclarativeRoot(root.Root, actions));
        }

        private static TSCommandCatalogEntry BuildCatalogEntry(DeclarativeRoot root) {
            var rootMetadata = root.Root.ControllerAttributes.OfType<TSCommandRootAttribute>().SingleOrDefault();
            ImmutableArray<string> permissions = [.. root.Actions
                .SelectMany(static action => action.Permissions)
                .Where(static permission => !string.IsNullOrWhiteSpace(permission))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static permission => permission, StringComparer.OrdinalIgnoreCase)];
            ImmutableArray<string> helpLines = rootMetadata?.HelpLines.Length > 0
                ? [.. rootMetadata.HelpLines
                    .Where(static line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => CommandAttributeText.Resolve(root.Root.ControllerType, typeof(TSCommandRootAttribute), nameof(TSCommandRootAttribute.HelpLines), line))]
                : [];

            var allowServer = root.Actions.Any(static action => !action.PlayerScope);
            var allowCoord = root.Actions.Any(static action => !action.RequiresServerContext && !action.PlayerScope);

            return new TSCommandCatalogEntry {
                PrimaryName = root.Root.RootName,
                Aliases = root.Root.Aliases,
                Permissions = permissions,
                HelpText = ResolveRootHelpText(root.Root, rootMetadata),
                HelpLines = helpLines,
                DoLog = rootMetadata?.DoLog ?? true,
                AllowServer = allowServer,
                AllowCoord = allowCoord,
                IsLegacy = false,
            };
        }

        private static string ResolveRootHelpText(
            CommandRootDefinition root,
            TSCommandRootAttribute? rootMetadata) {
            if (rootMetadata is not null && !string.IsNullOrWhiteSpace(rootMetadata.HelpText)) {
                return CommandAttributeText.Resolve(root.ControllerType, typeof(TSCommandRootAttribute), nameof(TSCommandRootAttribute.HelpText), rootMetadata.HelpText);
            }

            return string.IsNullOrWhiteSpace(root.Summary)
                ? GetParticularString("{0} is command root name", $"Declarative command root '{root.RootName}'.")
                : root.Summary;
        }

        private static void LogExecutionIfNeeded(
            CommandExecutor executor,
            TSCommandCatalogEntry metadata,
            string rawInput,
            string invokedRoot) {
            if (!metadata.DoLog) {
                executor.SendLogs(GetString("{0} executed (args omitted): {1}{2}.", executor.Name, ResolveMatchedPrefix(rawInput), invokedRoot), Color.PaleVioletRed, executor.IsClient ? executor.Player : null);
                return;
            }

            executor.SendLogs(GetString("{0} executed: {1}{2}.", executor.Name, string.Empty, rawInput), Color.PaleVioletRed, executor.IsClient ? executor.Player : null);
        }

        private static string ResolveMatchedPrefix(string rawInput) {
            foreach (var prefix in ResolveCommandPrefixes().OrderByDescending(static prefix => prefix.Length)) {
                if (rawInput.StartsWith(prefix, StringComparison.Ordinal)) {
                    return prefix;
                }
            }

            return Commands.Specifier;
        }

        private static bool ShouldTrackConsoleActivity(CommandInvocationTarget target, CommandEndpointRootBinding root) {
            if (!target.Equals(CommandInvocationTarget.LauncherConsole)
                && !target.Equals(CommandInvocationTarget.ServerConsole)) {
                return false;
            }

            if (root.Actions.Any(static action => MethodNeedsTrackedConsole(action.Action.Method))) {
                return true;
            }

            return root.Root.MismatchHandler is not null
                && MethodNeedsTrackedConsole(root.Root.MismatchHandler.Method);
        }

        private static bool MethodNeedsTrackedConsole(MethodInfo method) {
            if (method.ReturnType != typeof(CommandOutcome)) {
                return true;
            }

            return method.GetParameters().Any(static parameter =>
                parameter.ParameterType == typeof(CancellationToken)
                || parameter.ParameterType == typeof(ActivityHandle)
                || parameter.ParameterType == typeof(ICommandExecutionFeedback));
        }

        private static string BuildConsoleActivityMessage(TSCommandCatalogEntry metadata, string rawInput) {
            if (!metadata.DoLog && !string.IsNullOrWhiteSpace(metadata.HelpText)) {
                return metadata.HelpText.Trim();
            }

            var normalizedInput = rawInput.Trim();
            return normalizedInput.Length == 0
                ? GetString("Executing command.")
                : GetParticularString("{0} is raw command input", $"Executing {normalizedInput}.");
        }

        private static string BuildGenericConsoleActivityMessage(
            CommandRootDefinition root,
            string rawInput,
            string invokedRoot) {
            var normalizedInput = rawInput.Trim();
            if (normalizedInput.Length > 0) {
                return GetParticularString("{0} is raw command input", $"Executing {normalizedInput}.");
            }

            if (!string.IsNullOrWhiteSpace(root.Summary)) {
                return root.Summary.Trim();
            }

            return GetParticularString("{0} is command root name", $"Executing {invokedRoot}.");
        }
    }
}
