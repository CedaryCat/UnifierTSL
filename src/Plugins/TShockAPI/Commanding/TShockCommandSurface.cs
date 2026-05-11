using System.Collections.Immutable;
using System.Reflection;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Bindings;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Commanding.Endpoints;
using UnifierTSL.Commanding.Execution;

namespace TShockAPI.Commanding
{
    internal sealed record TShockEndpointActionBinding : CommandEndpointActionBinding
    {
        public ImmutableArray<string> Permissions { get; init; } = [];

        public bool RequiresServerContext { get; init; }

        public bool PlayerScope { get; init; }

        public bool DisallowRest { get; init; }
    }

    internal static class TShockCommandEndpoints
    {
        public static CommandEndpointId Player { get; } = new("tshock-player");
        public static CommandEndpointId Rest { get; } = new("tshock-rest");
    }

    internal static class TShockCommandInvocationTargets
    {
        public static CommandInvocationTarget Player { get; } = new("player");
        public static CommandInvocationTarget Rest { get; } = new("rest");
    }

    internal static class TShockEndpointBindingFactory
    {
        public static bool IsTShockRoot(CommandEndpointRootBinding root) {
            return root.Actions.OfType<TShockEndpointActionBinding>().Any();
        }

        public static CommandAvailability BuildConsoleAvailability(TShockCommandAttribute attribute) {
            if (!attribute.AutoExposeToTerminal) {
                return CommandAvailability.None;
            }

            List<CommandInvocationTarget> targets = [];
            if (!attribute.PlayerScope) {
                targets.Add(CommandInvocationTarget.ServerConsole);
            }
            if (!attribute.ServerScope && !attribute.PlayerScope) {
                targets.Add(CommandInvocationTarget.LauncherConsole);
            }

            return new CommandAvailability(targets);
        }

        public static CommandEndpointActionBinding? BindAction(
            CommandEndpointId endpointId,
            CommandActionDefinition action,
            Func<TShockCommandAttribute, bool, bool> includeAction,
            Func<TShockCommandAttribute, bool, CommandAvailability> buildAvailability) {
            var attribute = action.GetActionAttribute<TShockCommandAttribute>();
            if (attribute is null) {
                return null;
            }

            ValidateScopedParameters(action, attribute);

            var disallowRest = action.Method.GetCustomAttribute<DisallowRestAttribute>(inherit: false) is not null;
            if (!includeAction(attribute, disallowRest)) {
                return null;
            }

            var availability = buildAvailability(attribute, disallowRest);
            if (availability.IsEmpty) {
                return null;
            }

            return new TShockEndpointActionBinding {
                EndpointId = endpointId,
                Action = action,
                Availability = availability,
                Permissions = ResolvePermissions(attribute),
                RequiresServerContext = attribute.ServerScope || attribute.PlayerScope,
                PlayerScope = attribute.PlayerScope,
                DisallowRest = disallowRest,
            };
        }

        private static void ValidateScopedParameters(CommandActionDefinition action, TShockCommandAttribute attribute) {
            var actionProvidesServerContext = attribute.ServerScope || attribute.PlayerScope;
            if (actionProvidesServerContext) {
                return;
            }

            var invalidParameter = action.Parameters.FirstOrDefault(static parameter =>
                (parameter.Modifiers & CommandParamModifiers.ServerScope) != 0);
            if (invalidParameter is null) {
                return;
            }

            throw new InvalidOperationException(GetParticularString(
                "{0} is command action name, {1} is parameter name",
                $"Command action '{action.Method.DeclaringType?.FullName}.{action.Method.Name}' marks parameter '{invalidParameter.Name}' with '{nameof(CommandParamModifiers)}.{nameof(CommandParamModifiers.ServerScope)}', but its TShockCommandAttribute does not enable ServerScope or PlayerScope. Parameter-level server scoping does not implicitly upgrade action availability."));
        }

        private static ImmutableArray<string> ResolvePermissions(TShockCommandAttribute attribute) {
            return [.. attribute.Permissions.Select(permission => ResolvePermissionToken(attribute, permission))];
        }

        private static string ResolvePermissionToken(TShockCommandAttribute attribute, string permissionToken) {
            var normalized = permissionToken?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized)) {
                throw new InvalidOperationException(GetString("TShock command permission tokens cannot be empty."));
            }

            if (normalized.Contains('.')) {
                return normalized;
            }

            var sourceType = attribute.PermissionFieldSource;
            if (sourceType is null) {
                return normalized;
            }

            var field = sourceType.GetField(normalized, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException(GetParticularString(
                    "{0} is permission field name, {1} is source type name",
                    $"Permission field '{normalized}' was not found on '{sourceType.FullName}'."));
            if (field.FieldType != typeof(string)) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is permission field name, {1} is source type name",
                    $"Permission field '{normalized}' on '{sourceType.FullName}' must be a string field."));
            }

            return (string?)field.GetValue(null)
                ?? throw new InvalidOperationException(GetParticularString(
                    "{0} is permission field name, {1} is source type name",
                    $"Permission field '{normalized}' on '{sourceType.FullName}' resolved to null."));
        }
    }

    internal static class TShockCommandAccess
    {
        public static CommandAccessResult EvaluateAccess(
            TShockEndpointActionBinding action,
            TSExecutionContext context) {
            if (!action.Availability.Allows(context.Target)) {
                return CommandAccessResult.Unavailable;
            }

            if (action.DisallowRest && context.Target.Equals(TShockCommandInvocationTargets.Rest)) {
                return CommandAccessResult.Unavailable;
            }

            if (action.PlayerScope && context.Player is null) {
                return CommandAccessResult.Unavailable;
            }

            if (action.RequiresServerContext && context.Server is null) {
                return CommandAccessResult.Unavailable;
            }

            if (action.Permissions.Length == 0) {
                return CommandAccessResult.Allowed;
            }

            return action.Permissions.Any(context.Executor.HasPermission)
                ? CommandAccessResult.Allowed
                : CommandAccessResult.Denied;
        }

        public static CommandOutcome? ResolveAccessFailure(
            TShockEndpointActionBinding action,
            CommandAccessResult access,
            CommandInvocationTarget target) {
            if (access == CommandAccessResult.Denied) {
                return CommandOutcome.Error(GetString("You do not have access to this command."));
            }

            if (access != CommandAccessResult.Unavailable) {
                return null;
            }

            if (action.DisallowRest && target.Equals(TShockCommandInvocationTargets.Rest)) {
                return CommandOutcome.Error(GetString("This command is not available from the current execution context."));
            }

            if (action.PlayerScope) {
                return CommandOutcome.Error(GetString("You must use this command in-game."));
            }

            if (action.RequiresServerContext) {
                return CommandOutcome.Error(GetString("You must use this command in specific server."));
            }

            return null;
        }
    }

    internal sealed class TShockCommandEndpointExecutor : ICommandEndpointExecutor
    {
        public Task<CommandOutcome> ExecuteAsync(
            CommandEndpointCatalog catalog,
            CommandEndpointRootBinding root,
            CommandExecutionRequest request,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(request);

            if (request.ExecutionContext is not TSExecutionContext context) {
                return Task.FromResult(CommandOutcome.Error(GetString("This command is not available from the current execution context.")));
            }

            return CommandEndpointDispatcher.DispatchAsync(
                root,
                request,
                new CommandEndpointExecutionAdapter {
                    EvaluateAccess = action => EvaluateAccess((TShockEndpointActionBinding)action, context),
                    ResolveAccessFailure = (action, access) => ResolveAccessFailure((TShockEndpointActionBinding)action, access, context.Target),
                },
                cancellationToken);
        }

        private static CommandAccessResult EvaluateAccess(
            TShockEndpointActionBinding action,
            TSExecutionContext context) {
            return TShockCommandAccess.EvaluateAccess(action, context);
        }

        private static CommandOutcome? ResolveAccessFailure(
            TShockEndpointActionBinding action,
            CommandAccessResult access,
            CommandInvocationTarget target) {
            return TShockCommandAccess.ResolveAccessFailure(action, access, target);
        }
    }

    public sealed class TShockPlayerCommandEndpoint : ICommandEndpoint
    {
        public CommandEndpointId Id => TShockCommandEndpoints.Player;

        public ICommandEndpointExecutor Executor { get; } = new TShockCommandEndpointExecutor();
    }

    public sealed class TShockRestCommandEndpoint : ICommandEndpoint
    {
        public CommandEndpointId Id => TShockCommandEndpoints.Rest;

        public ICommandEndpointExecutor Executor { get; } = new TShockCommandEndpointExecutor();
    }

    internal static class TShockCommandRegistration
    {
        public static void Configure(CommandRegistrationBuilder context) {
            context.AddControllerGroup<TShockCommandV2>();
            context.AddBindings(CommandCommonParameterRules.Configure);
            context.AddBindings(TSCommandParamBinders.Configure);
            context.AddEndpointBinding<TShockPlayerCommandEndpoint>(BindPlayerAction);
            context.AddEndpointBinding<TShockRestCommandEndpoint>(BindRestAction);
            context.AddEndpointBinding<TerminalCommandEndpoint>(BindTerminalAction);
            context.AddOutcomeWriter<CommandExecutor, TShockCommandOutcomeWriter>();
        }

        private static CommandEndpointActionBinding? BindPlayerAction(
            CommandEndpointId endpointId,
            CommandActionDefinition action) {
            return TShockEndpointBindingFactory.BindAction(
                endpointId,
                action,
                includeAction: static (_, _) => true,
                buildAvailability: static (_, _) => new CommandAvailability(TShockCommandInvocationTargets.Player));
        }

        private static CommandEndpointActionBinding? BindRestAction(
            CommandEndpointId endpointId,
            CommandActionDefinition action) {
            return TShockEndpointBindingFactory.BindAction(
                endpointId,
                action,
                includeAction: static (attribute, disallowRest) => !attribute.PlayerScope && !disallowRest,
                buildAvailability: static (_, _) => new CommandAvailability(TShockCommandInvocationTargets.Rest));
        }

        private static CommandEndpointActionBinding? BindTerminalAction(
            CommandEndpointId endpointId,
            CommandActionDefinition action) {
            return TShockEndpointBindingFactory.BindAction(
                endpointId,
                action,
                includeAction: static (attribute, _) => attribute.AutoExposeToTerminal,
                buildAvailability: static (attribute, _) => TShockEndpointBindingFactory.BuildConsoleAvailability(attribute));
        }
    }
}
