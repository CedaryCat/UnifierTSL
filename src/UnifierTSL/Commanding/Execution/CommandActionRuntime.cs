using System.Collections.Immutable;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using UnifierTSL.Commanding.Bindings;
using UnifierTSL.Commanding.Composition;

namespace UnifierTSL.Commanding.Execution
{
    public enum CommandParamBindingSource : byte
    {
        UserToken,
        RemainingText,
        BindingRule,
        Flags,
        InvocationContext,
        MismatchContext,
        AmbientContext,
        ServerContext,
        CancellationToken,
    }

    public sealed record CommandParamBinding(
        Type ParameterType,
        CommandParamBindingSource Source,
        CommandParamModifiers Modifiers,
        bool Optional,
        object? DefaultValue,
        ImmutableArray<string> LiteralValues,
        CommandParamDefinition? Parameter,
        CommandFlagsParameterDefinition? FlagsParameter,
        CommandBindingAttribute? BindingAttribute,
        CommandParamBindingRule? BindingRule,
        bool RawRemainingText = false);

    public sealed class CommandActionRuntime
    {
        public readonly record struct BindingFailure(int ConsumedTokens, CommandOutcome? Outcome)
        {
            public bool IsExplicit => Outcome is not null;
        }

        private readonly record struct ScalarParseResult(
            bool Success,
            object? Value);

        private readonly Func<object?[], object?> compiledCall;

        public ImmutableArray<CommandParamBinding> Bindings { get; }

        public CommandActionRuntime(MethodInfo method, ImmutableArray<CommandParamBinding> bindings) {
            compiledCall = CompileCall(method);
            Bindings = bindings;
        }

        public bool TryBind(
            CommandInvocationContext context,
            CancellationToken cancellationToken,
            out object?[] boundValues,
            out BindingFailure? failure,
            out int consumedUserTokens,
            CommandMismatchContext? mismatchContext = null,
            bool allowUnconsumedUserTokens = false) {
            boundValues = new object?[Bindings.Length];
            failure = null;
            consumedUserTokens = 0;

            List<CommandBoundParameterValue> boundParameters = [];
            var success = TryBindCore(
                bindingIndex: 0,
                userIndex: 0,
                context,
                cancellationToken,
                mismatchContext,
                allowUnconsumedUserTokens,
                boundValues,
                boundParameters,
                ref failure,
                out consumedUserTokens);
            if (success) {
                return true;
            }

            boundValues = [];
            return false;
        }

        public async Task<CommandOutcome> InvokeAsync(object?[] boundValues) {
            var raw = compiledCall(boundValues);
            return await NormalizeOutcomeAsync(raw);
        }

        private static bool TryResolveInjectedValue(CommandParamBinding binding, object? candidate, out object? value) {
            if (candidate is not null && binding.ParameterType.IsInstanceOfType(candidate)) {
                value = candidate;
                return true;
            }

            if (candidate is IAmbientValueProvider ambientValueProvider
                && ambientValueProvider.TryGetAmbientValue(binding.ParameterType, out value)) {
                return true;
            }

            if (!binding.Optional) {
                value = null;
                return false;
            }

            value = binding.DefaultValue;
            return true;
        }

        private static async Task<CommandOutcome> NormalizeOutcomeAsync(object? raw) {
            return raw switch {
                null => CommandOutcome.Empty,
                CommandOutcome outcome => outcome,
                Task<CommandOutcome> pending => await pending,
                ValueTask<CommandOutcome> pending => await pending,
                _ => throw new InvalidOperationException(GetParticularString(
                    "{0} is command return type",
                    $"Unsupported command return type '{raw.GetType().FullName}'. Only CommandOutcome, Task<CommandOutcome>, and ValueTask<CommandOutcome> are supported in V2.")),
            };
        }

        private static Func<object?[], object?> CompileCall(MethodInfo method) {
            var argumentsParameter = Expression.Parameter(typeof(object[]), "arguments");

            IReadOnlyList<ParameterInfo> parameters = method.GetParameters();
            Expression[] callArguments = new Expression[parameters.Count];
            for (var i = 0; i < parameters.Count; i++) {
                Expression access = Expression.ArrayIndex(argumentsParameter, Expression.Constant(i));
                callArguments[i] = Expression.Convert(access, parameters[i].ParameterType);
            }

            Expression call = Expression.Call(instance: null, method, callArguments);
            Expression body = Expression.Convert(call, typeof(object));
            return Expression.Lambda<Func<object?[], object?>>(body, argumentsParameter).Compile();
        }

        private bool TryBindCore(
            int bindingIndex,
            int userIndex,
            CommandInvocationContext context,
            CancellationToken cancellationToken,
            CommandMismatchContext? mismatchContext,
            bool allowUnconsumedUserTokens,
            object?[] boundValues,
            List<CommandBoundParameterValue> boundParameters,
            ref BindingFailure? bestFailure,
            out int consumedUserTokens) {
            if (bindingIndex >= Bindings.Length) {
                if (allowUnconsumedUserTokens || userIndex == context.UserArguments.Length) {
                    consumedUserTokens = userIndex;
                    return true;
                }

                RegisterFailure(ref bestFailure, new BindingFailure(userIndex, Outcome: null));
                consumedUserTokens = 0;
                return false;
            }

            var binding = Bindings[bindingIndex];
            var result = ResolveBindingCandidates(
                binding,
                context,
                cancellationToken,
                mismatchContext,
                userIndex,
                [.. boundParameters]);

            if (!result.IsMatch) {
                RegisterFailure(ref bestFailure, new BindingFailure(userIndex, Outcome: null));
                consumedUserTokens = 0;
                return false;
            }

            if (!result.IsSuccess) {
                RegisterFailure(ref bestFailure, new BindingFailure(
                    userIndex + Math.Max(0, result.FailureConsumedTokens),
                    result.FailureOutcome ?? CommandOutcome.Usage(GetString("Invalid command syntax."))));
                consumedUserTokens = 0;
                return false;
            }

            foreach (var candidate in result.Candidates) {
                boundValues[bindingIndex] = candidate.Value;

                var appended = false;
                if (binding.Parameter is not null) {
                    boundParameters.Add(new CommandBoundParameterValue {
                        Parameter = binding.Parameter,
                        Value = candidate.Value,
                    });
                    appended = true;
                }

                if (TryBindCore(
                    bindingIndex + 1,
                    userIndex + candidate.ConsumedTokens,
                    context,
                    cancellationToken,
                    mismatchContext,
                    allowUnconsumedUserTokens,
                    boundValues,
                    boundParameters,
                    ref bestFailure,
                    out consumedUserTokens)) {
                    return true;
                }

                if (appended) {
                    boundParameters.RemoveAt(boundParameters.Count - 1);
                }

                boundValues[bindingIndex] = null;
            }

            if (result.FailureOutcome is not null) {
                RegisterFailure(ref bestFailure, new BindingFailure(
                    userIndex + Math.Max(0, result.FailureConsumedTokens),
                    result.FailureOutcome));
            }

            consumedUserTokens = 0;
            return false;
        }

        private CommandParamBindingResult ResolveBindingCandidates(
            CommandParamBinding binding,
            CommandInvocationContext context,
            CancellationToken cancellationToken,
            CommandMismatchContext? mismatchContext,
            int userIndex,
            ImmutableArray<CommandBoundParameterValue> boundParameters) {
            switch (binding.Source) {
                case CommandParamBindingSource.InvocationContext:
                    return CommandParamBindingResult.Success(context, consumedTokens: 0);
                case CommandParamBindingSource.MismatchContext:
                    return mismatchContext is null
                        ? CommandParamBindingResult.Mismatch()
                        : CommandParamBindingResult.Success(mismatchContext, consumedTokens: 0);
                case CommandParamBindingSource.Flags:
                    return ResolveFlagBinding(binding, context);
                case CommandParamBindingSource.ServerContext:
                    return TryResolveInjectedValue(binding, context.Server, out var serverValue)
                        ? CommandParamBindingResult.Success(serverValue, consumedTokens: 0)
                        : CommandParamBindingResult.Failure(CommandOutcome.Error(GetString("You must use this command in specific server.")));
                case CommandParamBindingSource.AmbientContext:
                    return TryResolveInjectedValue(binding, context.ExecutionContext, out var ambientValue)
                        ? CommandParamBindingResult.Success(ambientValue, consumedTokens: 0)
                        : CommandParamBindingResult.Mismatch();
                case CommandParamBindingSource.CancellationToken:
                    return CommandParamBindingResult.Success(cancellationToken, consumedTokens: 0);
                case CommandParamBindingSource.RemainingText:
                    return ResolveRemainingText(binding, context, userIndex);
                case CommandParamBindingSource.BindingRule:
                    return ResolveRuleBinding(binding, context, userIndex, boundParameters, cancellationToken);
                case CommandParamBindingSource.UserToken:
                    return ResolveUserTokenBinding(binding, context, userIndex);
                default:
                    return CommandParamBindingResult.Mismatch();
            }
        }

        private static CommandParamBindingResult ResolveRemainingText(
            CommandParamBinding binding,
            CommandInvocationContext context,
            int userIndex) {
            if (userIndex >= context.UserArguments.Length) {
                return binding.Optional
                    ? CommandParamBindingResult.Success(binding.DefaultValue, consumedTokens: 0)
                    : CommandParamBindingResult.Mismatch();
            }

            if (binding.RawRemainingText) {
                var rawRemaining = ResolveRawRemainingText(context, userIndex);
                return CommandParamBindingResult.Success(rawRemaining, context.UserArguments.Length - userIndex);
            }

            var remaining = string.Join(' ', context.UserArguments.Skip(userIndex));
            return CommandParamBindingResult.Success(remaining, context.UserArguments.Length - userIndex);
        }

        private static string ResolveRawRemainingText(CommandInvocationContext context, int userIndex) {
            if (userIndex >= context.UserArgumentTokens.Length) {
                return string.Join(' ', context.UserArguments.Skip(userIndex));
            }

            var firstToken = context.UserArgumentTokens[userIndex];
            var lastToken = context.UserArgumentTokens[^1];
            var startIndex = firstToken.Quoted
                && firstToken.StartIndex > 0
                && context.RawInput[firstToken.StartIndex - 1] == '"'
                    ? firstToken.StartIndex - 1
                    : firstToken.StartIndex;
            var endIndex = lastToken.Quoted
                && lastToken.EndIndex < context.RawInput.Length
                && context.RawInput[lastToken.EndIndex] == '"'
                    ? lastToken.EndIndex + 1
                    : lastToken.EndIndex;
            if (startIndex < 0 || endIndex > context.RawInput.Length || endIndex <= startIndex) {
                return string.Join(' ', context.UserArguments.Skip(userIndex));
            }

            return context.RawInput[startIndex..endIndex];
        }

        private static CommandParamBindingResult ResolveFlagBinding(
            CommandParamBinding binding,
            CommandInvocationContext context) {
            var value = context.FlagsValue
                ?? Enum.ToObject(binding.ParameterType, 0);
            return CommandParamBindingResult.Success(value, consumedTokens: 0);
        }

        private static CommandParamBindingResult ResolveRuleBinding(
            CommandParamBinding binding,
            CommandInvocationContext context,
            int userIndex,
            ImmutableArray<CommandBoundParameterValue> boundParameters,
            CancellationToken cancellationToken) {
            var consumesRemainingTokens = binding.BindingAttribute?.ConsumesRemainingTokens == true;
            if (userIndex >= context.UserArguments.Length && !consumesRemainingTokens) {
                return binding.Optional
                    ? CommandParamBindingResult.Success(binding.DefaultValue, consumedTokens: 0)
                    : CommandParamBindingResult.Mismatch();
            }

            if (binding.Parameter is null || binding.BindingRule is null || binding.BindingAttribute is null) {
                return CommandParamBindingResult.Mismatch();
            }

            return binding.BindingRule.Binder(
                new CommandParamBindingContext(
                    InvocationContext: context,
                    UserArguments: context.UserArguments,
                    UserIndex: userIndex,
                    Parameter: binding.Parameter,
                    BindingAttribute: binding.BindingAttribute,
                    RuntimeAttributes: binding.Parameter.RuntimeAttributes,
                    BoundParameters: boundParameters,
                    Modifiers: binding.Modifiers,
                    CancellationToken: cancellationToken),
                binding.BindingAttribute);
        }

        private static CommandParamBindingResult ResolveUserTokenBinding(
            CommandParamBinding binding,
            CommandInvocationContext context,
            int userIndex) {
            if (userIndex >= context.UserArguments.Length) {
                return binding.Optional
                    ? CommandParamBindingResult.Success(binding.DefaultValue, consumedTokens: 0)
                    : CommandParamBindingResult.Mismatch();
            }

            var raw = context.UserArguments[userIndex];
            if (binding.LiteralValues.Length > 0
                && !binding.LiteralValues.Any(candidate => candidate.Equals(raw, StringComparison.OrdinalIgnoreCase))) {
                return CommandParamBindingResult.Mismatch();
            }

            var tokenEnumAttribute = binding.Parameter?.RuntimeAttributes
                .OfType<CommandTokenEnumAttribute>()
                .SingleOrDefault();
            if (tokenEnumAttribute is not null) {
                return ResolveTokenEnumBinding(binding, context, raw, tokenEnumAttribute);
            }

            var parse = TryConvertToken(raw, binding.ParameterType);
            return parse.Success
                ? CommandParamBindingResult.Success(parse.Value, consumedTokens: 1)
                : CommandParamBindingResult.Mismatch();
        }

        private static CommandParamBindingResult ResolveTokenEnumBinding(
            CommandParamBinding binding,
            CommandInvocationContext context,
            string raw,
            CommandTokenEnumAttribute attribute) {
            var enumType = Nullable.GetUnderlyingType(binding.ParameterType) ?? binding.ParameterType;
            var spec = CommandTokenEnumSupport.GetSpec(enumType);
            if (spec.TryResolve(raw, out var resolvedValue)) {
                return CommandParamBindingResult.Success(
                    BoxEnumValue(binding.ParameterType, resolvedValue!),
                    consumedTokens: 1);
            }

            return attribute.UnrecognizedTokenBehavior switch {
                CommandTokenFallbackBehavior.UseDefault => CommandParamBindingResult.Success(binding.DefaultValue, consumedTokens: 1),
                CommandTokenFallbackBehavior.UseDefaultWithoutConsuming => CommandParamBindingResult.Success(binding.DefaultValue, consumedTokens: 0),
                _ => CommandParamBindingResult.Failure(BuildTokenEnumFailure(attribute, raw), consumedTokens: 1),
            };
        }

        private static CommandOutcome BuildTokenEnumFailure(
            CommandTokenEnumAttribute attribute,
            string raw) {
            return CommandOutcome.Error(CommandAttributeText.Invoke(
                attribute,
                nameof(CommandTokenEnumAttribute.InvalidTokenMessage),
                attribute.InvalidTokenMessage,
                raw));
        }

        private static object? BoxEnumValue(Type parameterType, object value) {
            var nullableType = Nullable.GetUnderlyingType(parameterType);
            return nullableType is null
                ? value
                : Activator.CreateInstance(parameterType, value);
        }

        private static void RegisterFailure(ref BindingFailure? current, BindingFailure candidate) {
            if (current is null) {
                current = candidate;
                return;
            }

            if (candidate.ConsumedTokens > current.Value.ConsumedTokens) {
                current = candidate;
                return;
            }

            if (candidate.ConsumedTokens == current.Value.ConsumedTokens
                && candidate.IsExplicit
                && !current.Value.IsExplicit) {
                current = candidate;
            }
        }

        private static ScalarParseResult TryConvertToken(string raw, Type parameterType) {
            var effectiveType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;

            if (effectiveType == typeof(string)) {
                return new ScalarParseResult(true, raw);
            }

            if (effectiveType == typeof(bool)) {
                if (bool.TryParse(raw, out var boolValue)) {
                    return new ScalarParseResult(true, boolValue);
                }

                switch (raw.Trim().ToLowerInvariant()) {
                    case "on":
                    case "yes":
                    case "y":
                    case "1":
                        return new ScalarParseResult(true, true);
                    case "off":
                    case "no":
                    case "n":
                    case "0":
                        return new ScalarParseResult(true, false);
                }

                return new ScalarParseResult(false, null);
            }

            if (effectiveType.IsEnum) {
                if (Enum.TryParse(effectiveType, raw, ignoreCase: true, out var enumValue)) {
                    return new ScalarParseResult(true, enumValue);
                }

                return new ScalarParseResult(false, null);
            }

            switch (Type.GetTypeCode(effectiveType)) {
                case TypeCode.Byte:
                    if (byte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var byteValue)) {
                        return new ScalarParseResult(true, byteValue);
                    }
                    break;
                case TypeCode.SByte:
                    if (sbyte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sbyteValue)) {
                        return new ScalarParseResult(true, sbyteValue);
                    }
                    break;
                case TypeCode.Int16:
                    if (short.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shortValue)) {
                        return new ScalarParseResult(true, shortValue);
                    }
                    break;
                case TypeCode.UInt16:
                    if (ushort.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ushortValue)) {
                        return new ScalarParseResult(true, ushortValue);
                    }
                    break;
                case TypeCode.Int32:
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue)) {
                        return new ScalarParseResult(true, intValue);
                    }
                    break;
                case TypeCode.UInt32:
                    if (uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uintValue)) {
                        return new ScalarParseResult(true, uintValue);
                    }
                    break;
                case TypeCode.Int64:
                    if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue)) {
                        return new ScalarParseResult(true, longValue);
                    }
                    break;
                case TypeCode.UInt64:
                    if (ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ulongValue)) {
                        return new ScalarParseResult(true, ulongValue);
                    }
                    break;
                case TypeCode.Single:
                    if (float.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var floatValue)) {
                        return new ScalarParseResult(true, floatValue);
                    }
                    break;
                case TypeCode.Double:
                    if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue)) {
                        return new ScalarParseResult(true, doubleValue);
                    }
                    break;
                case TypeCode.Decimal:
                    if (decimal.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var decimalValue)) {
                        return new ScalarParseResult(true, decimalValue);
                    }
                    break;
            }

            return new ScalarParseResult(false, null);
        }
    }
}
