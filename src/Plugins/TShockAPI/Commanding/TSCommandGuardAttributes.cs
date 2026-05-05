using System.Globalization;
using System.Reflection;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Prompting;
using UnifierTSL.Servers;

namespace TShockAPI.Commanding
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class RequirePermissionPreBindAttribute(string permissionFieldName, string denialMessage) : PreBindGuardAttribute
    {
        private readonly string permission = PermissionGuardHelpers.ResolvePermission(permissionFieldName);

        public string DenialMessage { get; } = denialMessage;

        public override CommandGuardResult Evaluate(CommandInvocationContext context) {
            if (context.ExecutionContext is not TSExecutionContext tsContext) {
                return CommandGuardResult.Continue();
            }

            return tsContext.Executor.HasPermission(permission)
                ? CommandGuardResult.Continue()
                : CommandGuardResult.Fail(CommandOutcome.Error(CommandAttributeText.Invoke(
                    this,
                    nameof(DenialMessage),
                    DenialMessage)));
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class SkipWithoutPermissionPreBindAttribute(string permissionFieldName) : PreBindGuardAttribute
    {
        private readonly string permission = PermissionGuardHelpers.ResolvePermission(permissionFieldName);

        public override CommandGuardResult Evaluate(CommandInvocationContext context) {
            if (context.ExecutionContext is not TSExecutionContext tsContext) {
                return CommandGuardResult.Continue();
            }

            return tsContext.Executor.HasPermission(permission)
                ? CommandGuardResult.Continue()
                : CommandGuardResult.SkipAction();
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class RequireUserArgumentCountSyntaxAttribute : PreBindGuardAttribute
    {
        public int Minimum { get; }

        public int Maximum { get; }

        public string SyntaxMessage { get; }

        public RequireUserArgumentCountSyntaxAttribute(int exactCount, string syntaxMessage)
            : this(exactCount, exactCount, syntaxMessage) { }

        public RequireUserArgumentCountSyntaxAttribute(int minimum, int maximum, string syntaxMessage) {
            ArgumentOutOfRangeException.ThrowIfNegative(minimum);
            ArgumentOutOfRangeException.ThrowIfNegative(maximum);
            if (maximum < minimum) {
                throw new ArgumentOutOfRangeException(nameof(maximum), maximum, "Maximum user-argument count must be greater than or equal to the minimum.");
            }

            Minimum = minimum;
            Maximum = maximum;
            SyntaxMessage = syntaxMessage?.Trim() ?? string.Empty;
            if (SyntaxMessage.Length == 0) {
                throw new ArgumentException("Syntax message cannot be empty.", nameof(syntaxMessage));
            }
        }

        public override CommandGuardResult Evaluate(CommandInvocationContext context) {
            var userArgumentCount = context.UserArguments.Length;
            return userArgumentCount >= Minimum && userArgumentCount <= Maximum
                ? CommandGuardResult.Continue()
                : CommandGuardResult.Fail(CommandOutcome.Error(CommandAttributeText.Invoke(
                    this,
                    nameof(SyntaxMessage),
                    SyntaxMessage,
                    Commands.Specifier)));
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class RequireUnmutedAttribute : PostBindGuardAttribute
    {
        public override CommandGuardResult Evaluate(CommandInvocationContext context) {
            if (context.ExecutionContext is not TSExecutionContext tsContext) {
                return CommandGuardResult.Continue();
            }

            return tsContext.Executor.Player.mute
                ? CommandGuardResult.Fail(CommandOutcome.Error(GetString("You are muted.")))
                : CommandGuardResult.Continue();
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class RequireInvasionActivityPreBindAttribute(bool active) : PreBindGuardAttribute, ICommandPromptRouteGuardSource
    {
        public bool Active { get; } = active;

        public override CommandGuardResult Evaluate(CommandInvocationContext context) {
            return ResolvePromptRouteGuardState(context.Server) == PromptRouteGuardState.Deny
                ? CommandGuardResult.SkipAction()
                : CommandGuardResult.Continue();
        }

        public ICommandPromptRouteGuard CreatePromptRouteGuard() {
            var requiredActive = Active;
            return CommandPromptRouteGuard.Create(
                key: "tshock.invasion-activity:active=" + (requiredActive ? bool.TrueString.ToLowerInvariant() : bool.FalseString.ToLowerInvariant()),
                label: "invasion",
                evaluate: context => ResolvePromptRouteGuardState(context.Server));
        }

        private PromptRouteGuardState ResolvePromptRouteGuardState(ServerContext? server) {
            if (server is null) {
                return PromptRouteGuardState.Unknown;
            }

            var isActive = server.DD2Event.Ongoing || server.Main.invasionSize > 0;
            return isActive == Active
                ? PromptRouteGuardState.Allow
                : PromptRouteGuardState.Deny;
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class RequireNonEmptyUserArgumentPreBindAttribute : PreBindGuardAttribute
    {
        public int ArgumentIndex { get; }

        public string ErrorMessage { get; }

        public RequireNonEmptyUserArgumentPreBindAttribute(int argumentIndex, string errorMessage) {
            ArgumentOutOfRangeException.ThrowIfNegative(argumentIndex);
            var normalizedErrorMessage = errorMessage?.Trim() ?? string.Empty;
            if (normalizedErrorMessage.Length == 0) {
                throw new ArgumentException("Error message cannot be empty.", nameof(errorMessage));
            }

            ArgumentIndex = argumentIndex;
            ErrorMessage = normalizedErrorMessage;
        }

        public override CommandGuardResult Evaluate(CommandInvocationContext context) {
            return context.UserArguments.Length > ArgumentIndex && context.UserArguments[ArgumentIndex].Length == 0
                ? CommandGuardResult.Fail(CommandOutcome.Error(CommandAttributeText.Invoke(
                    this,
                    nameof(ErrorMessage),
                    ErrorMessage)))
                : CommandGuardResult.Continue();
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class RequireWorldTimeFormatPreBindAttribute(int argumentIndex = 0) : PreBindGuardAttribute, ICommandPromptRouteGuardSource
    {
        public int ArgumentIndex { get; } = argumentIndex >= 0
            ? argumentIndex
            : throw new ArgumentOutOfRangeException(nameof(argumentIndex));

        public override CommandGuardResult Evaluate(CommandInvocationContext context) {
            if (context.UserArguments.Length <= ArgumentIndex) {
                return CommandGuardResult.Continue();
            }

            var token = context.UserArguments[ArgumentIndex].Trim();
            if (token.Length == 0 || TryParseWorldTime(token, out _)) {
                return CommandGuardResult.Continue();
            }

            return CommandGuardResult.Fail(CommandOutcome.Error(GetString("Invalid time string. Proper format: hh:mm, in 24-hour time.")));
        }

        public ICommandPromptRouteGuard CreatePromptRouteGuard() {
            var argumentIndex = ArgumentIndex;
            return CommandPromptRouteGuard.Create(
                key: "tshock.world-time:index=" + argumentIndex.ToString(CultureInfo.InvariantCulture),
                label: "world time",
                evaluate: context => {
                    if (context.UserArguments.Length <= argumentIndex) {
                        return PromptRouteGuardState.Unknown;
                    }

                    var token = context.UserArguments[argumentIndex].Value.Trim();
                    if (token.Length == 0) {
                        return PromptRouteGuardState.Unknown;
                    }

                    return TryParseWorldTime(token, out _)
                        ? PromptRouteGuardState.Allow
                        : PromptRouteGuardState.Deny;
                });
        }

        private static bool TryParseWorldTime(string token, out TimeOnly time) {
            time = default;

            var parts = token.Split(':');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
                || hours < 0 || hours > 23
                || minutes < 0 || minutes > 59) {
                return false;
            }

            time = new TimeOnly(hours, minutes);
            return true;
        }
    }

    internal static class PermissionGuardHelpers
    {
        public static string ResolvePermission(string permissionFieldName) {
            var normalized = permissionFieldName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized)) {
                throw new InvalidOperationException(GetString("TShock command permission tokens cannot be empty."));
            }

            if (normalized.Contains('.')) {
                return normalized;
            }

            var field = typeof(Permissions).GetField(normalized, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException(GetParticularString(
                    "{0} is permission field name",
                    $"Permission field '{normalized}' was not found on '{typeof(Permissions).FullName}'."));
            if (field.FieldType != typeof(string)) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is permission field name",
                    $"Permission field '{normalized}' on '{typeof(Permissions).FullName}' must be a string field."));
            }

            return (string?)field.GetValue(null)
                ?? throw new InvalidOperationException(GetParticularString(
                    "{0} is permission field name",
                    $"Permission field '{normalized}' on '{typeof(Permissions).FullName}' resolved to null."));
        }
    }
}
