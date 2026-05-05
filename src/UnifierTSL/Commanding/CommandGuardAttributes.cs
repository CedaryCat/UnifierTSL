using System.Globalization;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding.Prompting;

namespace UnifierTSL.Commanding
{
    /// <summary>
    /// Restricts an action to invocations whose user-argument count falls within the declared
    /// inclusive range. When outside the range, dispatch skips the action and continues
    /// evaluating other overloads or the root mismatch handler.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class RequireUserArgumentCountAttribute : PreBindGuardAttribute, ICommandPromptRouteGuardSource
    {
        public int Minimum { get; }

        public int Maximum { get; }

        public RequireUserArgumentCountAttribute(int exactCount)
            : this(exactCount, exactCount) { }

        public RequireUserArgumentCountAttribute(int minimum, int maximum) {
            ArgumentOutOfRangeException.ThrowIfNegative(minimum);
            ArgumentOutOfRangeException.ThrowIfNegative(maximum);
            if (maximum < minimum) {
                throw new ArgumentOutOfRangeException(nameof(maximum), maximum, GetString("Maximum user-argument count must be greater than or equal to the minimum."));
            }

            Minimum = minimum;
            Maximum = maximum;
        }

        public override CommandGuardResult Evaluate(CommandInvocationContext context) {
            var userArgumentCount = context.UserArguments.Length;
            return userArgumentCount >= Minimum && userArgumentCount <= Maximum
                ? CommandGuardResult.Continue()
                : CommandGuardResult.SkipAction();
        }

        public ICommandPromptRouteGuard CreatePromptRouteGuard() {
            var minimum = Minimum;
            var maximum = Maximum;
            return CommandPromptRouteGuard.Create(
                key: $"user-argument-count:min={minimum.ToString(CultureInfo.InvariantCulture)},max={maximum.ToString(CultureInfo.InvariantCulture)}",
                label: "arg count",
                evaluate: context => {
                    var count = context.UserArguments.Length;
                    if (count > maximum) {
                        return PromptRouteGuardState.Deny;
                    }

                    if (count < minimum) {
                        return PromptRouteGuardState.Unknown;
                    }

                    return PromptRouteGuardState.Allow;
                });
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class RequireNumericUserArgumentAttribute : PreBindGuardAttribute, ICommandPromptRouteGuardSource
    {
        public int ArgumentIndex { get; }

        public RequireNumericUserArgumentAttribute(int argumentIndex = 0) {
            ArgumentOutOfRangeException.ThrowIfNegative(argumentIndex);
            ArgumentIndex = argumentIndex;
        }

        public override CommandGuardResult Evaluate(CommandInvocationContext context) {
            return HasIntegerUserArgument(context)
                ? CommandGuardResult.Continue()
                : CommandGuardResult.SkipAction();
        }

        public ICommandPromptRouteGuard CreatePromptRouteGuard() {
            var argumentIndex = ArgumentIndex;
            return CommandPromptRouteGuard.Create(
                key: $"user-argument-integer:index={argumentIndex.ToString(CultureInfo.InvariantCulture)},polarity=allow",
                label: "integer",
                evaluate: context => CommandPromptGuardStateResolver.ResolveIntegerGuardState(context, argumentIndex, allowOnMatch: true));
        }

        private bool HasIntegerUserArgument(CommandInvocationContext context) {
            return context.UserArguments.Length > ArgumentIndex
                && int.TryParse(context.UserArguments[ArgumentIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class RejectNumericUserArgumentAttribute : PreBindGuardAttribute, ICommandPromptRouteGuardSource
    {
        public int ArgumentIndex { get; }

        public RejectNumericUserArgumentAttribute(int argumentIndex = 0) {
            ArgumentOutOfRangeException.ThrowIfNegative(argumentIndex);
            ArgumentIndex = argumentIndex;
        }

        public override CommandGuardResult Evaluate(CommandInvocationContext context) {
            return HasIntegerUserArgument(context)
                ? CommandGuardResult.SkipAction()
                : CommandGuardResult.Continue();
        }

        public ICommandPromptRouteGuard CreatePromptRouteGuard() {
            var argumentIndex = ArgumentIndex;
            return CommandPromptRouteGuard.Create(
                key: $"user-argument-integer:index={argumentIndex.ToString(CultureInfo.InvariantCulture)},polarity=deny",
                label: "integer",
                evaluate: context => CommandPromptGuardStateResolver.ResolveIntegerGuardState(context, argumentIndex, allowOnMatch: false));
        }

        private bool HasIntegerUserArgument(CommandInvocationContext context) {
            return context.UserArguments.Length > ArgumentIndex
                && int.TryParse(context.UserArguments[ArgumentIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }
    }

    internal static class CommandPromptGuardStateResolver
    {
        public static PromptRouteGuardState ResolveIntegerGuardState(
            CommandPromptRouteGuardContext context,
            int argumentIndex,
            bool allowOnMatch) {
            if (context.UserArguments.Length <= argumentIndex) {
                return PromptRouteGuardState.Unknown;
            }

            var token = context.UserArguments[argumentIndex].Value.Trim();
            if (token.Length == 0) {
                return PromptRouteGuardState.Unknown;
            }

            var matches = int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            if (allowOnMatch) {
                return matches ? PromptRouteGuardState.Allow : PromptRouteGuardState.Deny;
            }

            return matches ? PromptRouteGuardState.Deny : PromptRouteGuardState.Allow;
        }
    }
}
