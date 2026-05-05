using System.Collections.Immutable;
using System.Reflection;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding.Bindings;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Commanding.Execution;
using UnifierTSL.Commanding.Prompting;
using UnifierTSL.Servers;

namespace UnifierTSL.Commanding.Composition
{
    internal static class CommandSystemDiscovery
    {
        private sealed class TokenSequenceComparer : IEqualityComparer<ImmutableArray<string>>
        {
            public static TokenSequenceComparer Instance { get; } = new();

            public bool Equals(ImmutableArray<string> x, ImmutableArray<string> y) {
                if (x.Length != y.Length) {
                    return false;
                }

                for (var i = 0; i < x.Length; i++) {
                    if (!string.Equals(x[i], y[i], StringComparison.OrdinalIgnoreCase)) {
                        return false;
                    }
                }

                return true;
            }

            public int GetHashCode(ImmutableArray<string> obj) {
                HashCode hash = new();
                foreach (var token in obj) {
                    hash.Add(token, StringComparer.OrdinalIgnoreCase);
                }

                return hash.ToHashCode();
            }
        }

        private readonly record struct ResolvedBinding(
            CommandBindingAttribute? Attribute,
            CommandParamBindingRule? Rule,
            bool UsesExplicitAttribute);

        private readonly record struct PromptRouteData(
            PromptRouteMatchKind MatchKind,
            PromptRouteConsumptionMode ConsumptionMode,
            ImmutableArray<string> AcceptedTokens,
            int Specificity);

        public static CommandCatalog DiscoverFromControllerGroups(
            ImmutableArray<CommandControllerGroupRegistration> controllerGroups,
            CommandRegistrationOptions bindingOptions) {

            var roots = ImmutableArray.CreateBuilder<CommandRootDefinition>();
            foreach (var controllerGroup in controllerGroups) {
                roots.AddRange(DiscoverFromControllerGroup(controllerGroup, bindingOptions));
            }

            return new CommandCatalog {
                Roots = [.. roots
                    .OrderBy(static root => root.RootName, StringComparer.OrdinalIgnoreCase)],
            };
        }

        internal static ImmutableArray<CommandRootDefinition> DiscoverFromControllerGroup(
            CommandControllerGroupRegistration registration,
            CommandRegistrationOptions options) {
            var controllerGroupType = registration.ControllerGroupType;
            var sourceName = GetGroupSourceName(controllerGroupType);

            var group = controllerGroupType.GetCustomAttribute<ControllerGroupAttribute>(inherit: false)
                ?? throw new InvalidOperationException(GetParticularString(
                    "{0} is command controller group type",
                    $"Command controller group '{controllerGroupType.FullName}' is missing CommandControllerGroupAttribute."));

            ImmutableArray<Type> controllerTypes = [.. group.ControllerTypes
                .Where(static type => type is not null)
                .Distinct()
                .OrderBy(static type => type.FullName, StringComparer.Ordinal)];
            if (controllerTypes.Length == 0) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command controller group type",
                    $"Command controller group '{controllerGroupType.FullName}' does not declare any controller types."));
            }

            var roots = ImmutableArray.CreateBuilder<CommandRootDefinition>(controllerTypes.Length);
            foreach (var controllerType in controllerTypes) {
                roots.Add(DiscoverController(sourceName, controllerType, options));
            }

            return roots.ToImmutable();
        }

        private static CommandRootDefinition DiscoverController(
            string sourceName,
            Type controllerType,
            CommandRegistrationOptions options) {
            if (!controllerType.IsClass || !controllerType.IsAbstract || !controllerType.IsSealed) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command controller type",
                    $"Command controller '{controllerType.FullName}' must be declared as a static class."));
            }

            var controller = controllerType.GetCustomAttribute<CommandControllerAttribute>(inherit: false)
                ?? throw new InvalidOperationException(GetParticularString(
                    "{0} is command controller type",
                    $"Command controller '{controllerType.FullName}' is missing CommandControllerAttribute."));

            if (string.IsNullOrWhiteSpace(controller.RootName)) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command controller type",
                    $"Command controller '{controllerType.FullName}' declared an empty root name."));
            }

            ImmutableArray<MethodInfo> methods = [.. controllerType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(static method => !method.IsSpecialName)
                .OrderBy(static method => method.MetadataToken)];
            ImmutableArray<MethodInfo> actions = [.. methods
                .Where(static method => method.GetCustomAttribute<CommandActionAttribute>(inherit: false) is not null)];
            ImmutableArray<MethodInfo> mismatchHandlers = [.. methods
                .Where(static method => method.GetCustomAttribute<MismatchHandlerAttribute>(inherit: false) is not null)];

            if (actions.Length == 0) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command controller type",
                    $"Command controller '{controllerType.FullName}' does not declare any CommandAction methods."));
            }

            var instanceMethod = methods.FirstOrDefault(static method =>
                (method.GetCustomAttribute<CommandActionAttribute>(inherit: false) is not null
                    || method.GetCustomAttribute<MismatchHandlerAttribute>(inherit: false) is not null)
                && !method.IsStatic);
            if (instanceMethod is not null) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command action name, {1} is command controller type",
                    $"Command method '{instanceMethod.DeclaringType?.FullName}.{instanceMethod.Name}' is an instance method. V2 command methods must be static, and command controllers must not carry instance state."));
            }

            var invalidDualPurposeMethod = methods.FirstOrDefault(static method =>
                method.GetCustomAttribute<CommandActionAttribute>(inherit: false) is not null
                && method.GetCustomAttribute<MismatchHandlerAttribute>(inherit: false) is not null);
            if (invalidDualPurposeMethod is not null) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command method name",
                    $"Command method '{invalidDualPurposeMethod.DeclaringType?.FullName}.{invalidDualPurposeMethod.Name}' cannot be both a CommandAction and a CommandMismatchHandler."));
            }

            if (mismatchHandlers.Length > 1) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command controller type",
                    $"Command controller '{controllerType.FullName}' declares multiple CommandMismatchHandler methods. Only one root mismatch handler is allowed per controller."));
            }

            ImmutableArray<Attribute> controllerAttributes = [.. controllerType
                .GetCustomAttributes(inherit: false)
                .OfType<Attribute>()];
            var actionDefinitions = ImmutableArray.CreateBuilder<CommandActionDefinition>(actions.Length);
            foreach (var method in actions) {
                actionDefinitions.Add(DiscoverAction(method, options));
            }

            var mismatchHandler = mismatchHandlers.Length == 0
                ? null
                : DiscoverMismatchHandler(mismatchHandlers[0]);

            var aliasesAttribute = controllerType.GetCustomAttribute<AliasesAttribute>(inherit: false);
            var aliases = aliasesAttribute?.Values
                .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Select(static alias => alias.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(alias => !alias.Equals(controller.RootName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray() ?? [];

            return new CommandRootDefinition {
                SourceName = sourceName,
                RootName = controller.RootName.Trim(),
                Aliases = aliases,
                Summary = CommandAttributeText.Resolve(controllerType, typeof(CommandControllerAttribute), nameof(CommandControllerAttribute.Summary), controller.Summary),
                ControllerType = controllerType,
                ControllerAttributes = controllerAttributes,
                Actions = actionDefinitions.ToImmutable(),
                MismatchHandler = mismatchHandler,
            };
        }

        private static CommandActionDefinition DiscoverAction(
            MethodInfo method,
            CommandRegistrationOptions options) {
            var action = method.GetCustomAttribute<CommandActionAttribute>(inherit: false)
                ?? throw new InvalidOperationException();

            ValidateReturnType(method);

            var pathSegments = ParsePath(action.Path);
            var pathAliases = ParseActionAliases(method, pathSegments);
            (var parameters, var bindings, var flagsParameter) = BuildBindings(method, options);
            var ignoreTrailingArguments = method.GetCustomAttribute<IgnoreTrailingArgumentsAttribute>(inherit: false) is not null;
            ImmutableArray<PreBindGuardAttribute> preBindGuards = [.. method
                .GetCustomAttributes(inherit: false)
                .OfType<PreBindGuardAttribute>()];
            ImmutableArray<PostBindGuardAttribute> postBindGuards = [.. method
                .GetCustomAttributes(inherit: false)
                .OfType<PostBindGuardAttribute>()];
            ResolveAttributeTextMembers(method.DeclaringType!, preBindGuards);
            ResolveAttributeTextMembers(method.DeclaringType!, postBindGuards);
            ImmutableArray<Attribute> actionAttributes = [.. method
                .GetCustomAttributes(inherit: false)
                .OfType<Attribute>()];

            return new CommandActionDefinition {
                Summary = CommandAttributeText.Resolve(method.DeclaringType!, typeof(CommandActionAttribute), nameof(CommandActionAttribute.Summary), action.Summary),
                PathSegments = pathSegments,
                PathAliases = pathAliases,
                Parameters = parameters,
                FlagsParameter = flagsParameter,
                IgnoreTrailingArguments = ignoreTrailingArguments,
                Method = method,
                PreBindGuards = preBindGuards,
                PostBindGuards = postBindGuards,
                PromptRouteGuards = BuildPromptRouteGuards(actionAttributes),
                ActionAttributes = actionAttributes,
                Runtime = new CommandActionRuntime(method, bindings),
            };
        }

        private static MismatchHandlerDefinition DiscoverMismatchHandler(MethodInfo method) {
            var mismatchHandler = method.GetCustomAttribute<MismatchHandlerAttribute>(inherit: false)
                ?? throw new InvalidOperationException();
            ValidateReturnType(method);
            var bindings = BuildMismatchBindings(method);
            return new MismatchHandlerDefinition {
                Method = method,
                HandlingMode = mismatchHandler.HandlingMode,
                Runtime = new CommandActionRuntime(method, bindings),
            };
        }

        private static void ValidateReturnType(MethodInfo method) {
            var returnType = method.ReturnType;
            if (returnType == typeof(CommandOutcome)
                || returnType == typeof(Task<CommandOutcome>)
                || returnType == typeof(ValueTask<CommandOutcome>)) {
                return;
            }

            throw new InvalidOperationException(GetParticularString(
                "{0} is command action name, {1} is return type name",
                $"Command action '{method.DeclaringType?.FullName}.{method.Name}' uses unsupported return type '{returnType.FullName}'. V2 commands must return CommandOutcome, Task<CommandOutcome>, or ValueTask<CommandOutcome>."));
        }

        private static ImmutableArray<string> ParsePath(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                return [];
            }

            return [.. path
                .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static token => token.Trim())];
        }

        private static ImmutableArray<ImmutableArray<string>> ParseActionAliases(MethodInfo method, ImmutableArray<string> pathSegments) {
            return [.. method
                .GetCustomAttributes<CommandActionAliasAttribute>(inherit: false)
                .Select(static alias => NormalizeTokens(alias.Tokens.ToArray()))
                .Where(static alias => alias.Length > 0)
                .Distinct(TokenSequenceComparer.Instance)
                .Where(alias => !TokenSequenceComparer.Instance.Equals(alias, pathSegments))];
        }

        private static (ImmutableArray<CommandParamDefinition> Parameters, ImmutableArray<CommandParamBinding> Bindings, CommandFlagsParameterDefinition? FlagsParameter) BuildBindings(
            MethodInfo method,
            CommandRegistrationOptions options) {
            var declaringType = method.DeclaringType
                ?? throw new InvalidOperationException(GetParticularString(
                    "{0} is command action name",
                    $"Command action '{method.Name}' has no declaring type."));
            var parameters = method.GetParameters();
            var bindings = ImmutableArray.CreateBuilder<CommandParamBinding>(parameters.Length);
            var definitions = ImmutableArray.CreateBuilder<CommandParamDefinition>();
            var seenTailConsumingParameter = false;
            CommandFlagsParameterDefinition? flagsParameter = null;

            for (var i = 0; i < parameters.Length; i++) {
                var parameter = parameters[i];
                ImmutableArray<Attribute> runtimeAttributes = [.. parameter.GetCustomAttributes(inherit: false).OfType<Attribute>()];

                var metadata = parameter.GetCustomAttribute<CommandParamAttribute>(inherit: false);
                var flagsAttribute = parameter.GetCustomAttribute<CommandFlagsAttribute>(inherit: false);
                var explicitBindingAttribute = GetExplicitBindingAttr(parameter);
                var resolvedBinding = ResolveBinding(parameter.ParameterType, explicitBindingAttribute, options);
                var bindingAttribute = resolvedBinding.Attribute;
                var promptOverride = ResolvePromptOverride(parameter);
                var tokenEnumAttribute = parameter.GetCustomAttribute<CommandTokenEnumAttribute>(inherit: false);
                ResolveAttributeTextMembers(declaringType, tokenEnumAttribute);
                if (resolvedBinding.UsesExplicitAttribute) {
                    ResolveAttributeTextMembers(declaringType, bindingAttribute);
                }

                var literal = parameter.GetCustomAttribute<CommandLiteralAttribute>(inherit: false);
                var remainingTextAttribute = parameter.GetCustomAttribute<RemainingTextAttribute>(inherit: false);
                var hasRemainingText = remainingTextAttribute is not null;

                if (flagsAttribute is not null) {
                    if (flagsParameter is not null) {
                        throw new InvalidOperationException(GetParticularString(
                            "{0} is command action name",
                            $"Command action '{method.DeclaringType?.FullName}.{method.Name}' declares multiple CommandFlags parameters. Each action may only expose one first-class flag bag."));
                    }

                    ValidateFlagsParameter(
                        method,
                        parameter,
                        flagsAttribute,
                        metadata,
                        promptOverride,
                        literal,
                        hasRemainingText,
                        explicitBindingAttribute);

                    flagsParameter = BuildFlagsParameterDefinition(method, parameter, flagsAttribute);
                    bindings.Add(new CommandParamBinding(
                        ParameterType: parameter.ParameterType,
                        Source: CommandParamBindingSource.Flags,
                        Modifiers: CommandParamModifiers.None,
                        Optional: true,
                        DefaultValue: parameter.HasDefaultValue ? parameter.DefaultValue : null,
                        LiteralValues: [],
                        Parameter: null,
                        FlagsParameter: flagsParameter,
                        BindingAttribute: null,
                        BindingRule: null));
                    continue;
                }

                var variadicBinding = bindingAttribute?.IsVariadicParameter == true;
                var modifiers = bindingAttribute?.Modifiers ?? CommandParamModifiers.None;
                var resolvedBindingRule = resolvedBinding.Rule;
                var source = ResolveBindingSource(
                    method,
                    parameter,
                    hasRemainingText,
                    bindingAttribute,
                    resolvedBindingRule);
                var bindingRule = source == CommandParamBindingSource.BindingRule
                    ? resolvedBindingRule
                    : null;
                var optional = parameter.HasDefaultValue;
                var defaultValue = optional ? parameter.DefaultValue : null;
                // RemainingText still consumes the tail token span, but it is one logical slot.
                // Do not fold that back into Variadic or usage/prompt surfaces regress to <arg...>.
                var variadic = variadicBinding;
                var consumesRemainingTokens = hasRemainingText
                    || source == CommandParamBindingSource.RemainingText
                    || bindingAttribute?.ConsumesRemainingTokens == true;
                var literalValues = literal?.Values
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToImmutableArray() ?? [];

                if (resolvedBinding.UsesExplicitAttribute && metadata is not null) {
                    var bindingAttributeName = explicitBindingAttribute?.GetType().Name ?? nameof(CommandBindingAttribute);
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is command action name, {1} is parameter name",
                        $"Command action '{method.DeclaringType?.FullName}.{method.Name}' declares both CommandParamAttribute and binding attribute '{bindingAttributeName}' on parameter '{parameter.Name}'. Structured parameters must use the binding attribute Name property instead of CommandParamAttribute."));
                }

                if (source == CommandParamBindingSource.BindingRule && literal is not null) {
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is command action name, {1} is parameter name",
                        $"Command action '{method.DeclaringType?.FullName}.{method.Name}' declares CommandLiteralAttribute on structured parameter '{parameter.Name}'. Choose either literal token binding or a structured binding route."));
                }

                if (tokenEnumAttribute is not null) {
                    ValidateTokenEnumParameter(
                        method,
                        parameter,
                        tokenEnumAttribute,
                        bindingAttribute,
                        literal);
                }

                if (hasRemainingText) {
                    if (parameter.ParameterType != typeof(string) && bindingAttribute is null) {
                        throw new InvalidOperationException(GetParticularString(
                            "{0} is command action name, {1} is parameter name",
                            $"Command action '{method.DeclaringType?.FullName}.{method.Name}' marks '{parameter.Name}' with RemainingTextAttribute, but the parameter type is neither string nor backed by a registered binding attribute."));
                    }
                }

                if (consumesRemainingTokens) {
                    if (seenTailConsumingParameter) {
                        throw new InvalidOperationException(GetParticularString(
                            "{0} is command action name, {1} is parameter name",
                            $"Command action '{method.DeclaringType?.FullName}.{method.Name}' must place tail-consuming parameter '{parameter.Name}' last."));
                    }

                    seenTailConsumingParameter = true;
                }
                else if (seenTailConsumingParameter
                    && source is CommandParamBindingSource.UserToken
                        or CommandParamBindingSource.BindingRule) {
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is command action name, {1} is parameter name",
                        $"Command action '{method.DeclaringType?.FullName}.{method.Name}' cannot declare user-bound parameter '{parameter.Name}' after a tail-consuming parameter."));
                }

                if (parameter.ParameterType == typeof(CommandPlayerSelector)
                    && bindingAttribute is PlayerSelectorAttribute
                    && (modifiers & CommandParamModifiers.All) == 0) {
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is command action name, {1} is parameter name",
                        $"Command action '{method.DeclaringType?.FullName}.{method.Name}' must enable CommandParamModifiers.All on PlayerSelectorAttribute for parameter '{parameter.Name}'."));
                }

                if (modifiers != CommandParamModifiers.None) {
                    if (source is not CommandParamBindingSource.BindingRule || bindingRule is null) {
                        throw new InvalidOperationException(GetParticularString(
                            "{0} is command action name, {1} is parameter name",
                            $"Command action '{method.DeclaringType?.FullName}.{method.Name}' declares binding modifiers on '{parameter.Name}', but only parameters backed by a registered binding attribute may use CommandParamModifiers."));
                    }

                    var unsupported = modifiers & ~bindingRule.SupportedModifiers;
                    if (unsupported != CommandParamModifiers.None) {
                        throw new InvalidOperationException(GetParticularString(
                            "{0} is command action name, {1} is parameter name, {2} is modifier name",
                            $"Command action '{method.DeclaringType?.FullName}.{method.Name}' marks '{parameter.Name}' with unsupported modifiers '{unsupported}'."));
                    }
                }

                CommandParamDefinition? definition = null;
                if (source is CommandParamBindingSource.UserToken
                    or CommandParamBindingSource.RemainingText
                    or CommandParamBindingSource.BindingRule) {
                    var name = bindingAttribute?.Name?.Trim()
                        ?? metadata?.Name?.Trim()
                        ?? bindingRule?.DefaultName?.Trim()
                        ?? parameter.Name
                        ?? $"arg{i}";
                    definition = BuildParameterDefinition(
                        parameter,
                        name,
                        source,
                        hasRemainingText,
                        modifiers,
                        optional,
                        variadic,
                        defaultValue,
                        literalValues,
                        bindingAttribute,
                        bindingRule,
                        tokenEnumAttribute,
                        promptOverride,
                        runtimeAttributes);
                    definitions.Add(definition);
                }

                bindings.Add(new CommandParamBinding(
                    ParameterType: parameter.ParameterType,
                    Source: source,
                    Modifiers: modifiers,
                    Optional: optional,
                    DefaultValue: defaultValue,
                    LiteralValues: literalValues,
                    Parameter: definition,
                    FlagsParameter: null,
                    BindingAttribute: bindingAttribute,
                    BindingRule: bindingRule,
                    RawRemainingText: remainingTextAttribute?.Raw == true));
            }

            return (definitions.ToImmutable(), bindings.ToImmutable(), flagsParameter);
        }

        private static void ResolveAttributeTextMembers(Type declaringType, IEnumerable<Attribute> attributes) {
            foreach (var attribute in attributes) {
                ResolveAttributeTextMembers(declaringType, attribute);
            }
        }

        private static void ResolveAttributeTextMembers(Type declaringType, Attribute? attribute) {
            if (attribute is null) {
                return;
            }

            var attributeType = attribute.GetType();
            CommandAttributeText.Register(attribute, declaringType);
        }

        private static ImmutableArray<CommandParamBinding> BuildMismatchBindings(MethodInfo method) {
            var parameters = method.GetParameters();
            var bindings = ImmutableArray.CreateBuilder<CommandParamBinding>(parameters.Length);

            for (var i = 0; i < parameters.Length; i++) {
                var parameter = parameters[i];
                var source = ResolveMismatchBindingSource(method, parameter);
                var optional = parameter.HasDefaultValue;
                var defaultValue = optional ? parameter.DefaultValue : null;

                bindings.Add(new CommandParamBinding(
                    ParameterType: parameter.ParameterType,
                    Source: source,
                    Modifiers: CommandParamModifiers.None,
                    Optional: optional,
                    DefaultValue: defaultValue,
                    LiteralValues: [],
                    Parameter: null,
                    FlagsParameter: null,
                    BindingAttribute: null,
                    BindingRule: null));
            }

            return bindings.ToImmutable();
        }

        private static void ValidateFlagsParameter(
            MethodInfo method,
            ParameterInfo parameter,
            CommandFlagsAttribute flagsAttribute,
            CommandParamAttribute? metadata,
            CommandPromptAttribute? promptOverride,
            CommandLiteralAttribute? literal,
            bool hasRemainingText,
            CommandBindingAttribute? explicitBindingAttribute) {
            var hasAmbient = parameter.GetCustomAttribute<FromAmbientContextAttribute>(inherit: false) is not null;
            if (metadata is not null
                || promptOverride is not null
                || literal is not null
                || hasRemainingText
                || explicitBindingAttribute is not null
                || hasAmbient) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command action name, {1} is parameter name",
                    $"Command action '{method.DeclaringType?.FullName}.{method.Name}' declares unsupported metadata alongside CommandFlagsAttribute on parameter '{parameter.Name}'. First-class flag parameters must stand alone and cannot also be positional, literal, bound, prompt-overridden, or ambient."));
            }

            if (parameter.ParameterType != flagsAttribute.EnumType) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command action name, {1} is parameter name",
                    $"Command action '{method.DeclaringType?.FullName}.{method.Name}' marks '{parameter.Name}' with CommandFlagsAttribute for enum '{flagsAttribute.EnumType.FullName}', but the CLR parameter type is '{parameter.ParameterType.FullName}'. The parameter type must match the declared flags enum exactly."));
            }

            if (!parameter.ParameterType.IsEnum || parameter.ParameterType.GetCustomAttribute<FlagsAttribute>(inherit: false) is null) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command action name, {1} is parameter name",
                    $"Command action '{method.DeclaringType?.FullName}.{method.Name}' declares CommandFlagsAttribute on '{parameter.Name}', but the parameter type '{parameter.ParameterType.FullName}' is not a non-nullable [Flags] enum."));
            }
        }

        private static CommandFlagsParameterDefinition BuildFlagsParameterDefinition(
            MethodInfo method,
            ParameterInfo parameter,
            CommandFlagsAttribute flagsAttribute) {
            var enumType = flagsAttribute.EnumType;
            ImmutableArray<FieldInfo> fields = [.. enumType
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .OrderBy(static field => field.MetadataToken)];
            List<CommandFlagDefinition> flags = [];
            var tokenLookup =
                ImmutableDictionary.CreateBuilder<string, CommandFlagDefinition>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in fields) {
                var attribute = field.GetCustomAttribute<CommandFlagAttribute>(inherit: false);
                if (attribute is null) {
                    continue;
                }

                var tokens = NormalizeTokens(attribute.Tokens.ToArray());
                if (tokens.Length == 0) {
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is command action name, {1} is enum field name",
                        $"Command action '{method.DeclaringType?.FullName}.{method.Name}' maps flags enum member '{enumType.FullName}.{field.Name}' without any usable command tokens."));
                }

                if (tokens.Any(static token => !token.StartsWith("-", StringComparison.Ordinal) || token.Length < 2 || token.Any(char.IsWhiteSpace))) {
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is command action name, {1} is enum field name",
                        $"Command action '{method.DeclaringType?.FullName}.{method.Name}' maps flags enum member '{enumType.FullName}.{field.Name}' to an invalid command token. Flag tokens must start with '-' and must not contain whitespace."));
                }

                var value = Convert.ToUInt64(field.GetValue(null));
                if (value == 0) {
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is command action name, {1} is enum field name",
                        $"Command action '{method.DeclaringType?.FullName}.{method.Name}' maps zero-valued flags enum member '{enumType.FullName}.{field.Name}'. Zero-valued members must remain sentinel/default values and cannot be exposed as command flags."));
                }

                CommandFlagDefinition definition = new() {
                    MemberName = field.Name,
                    Value = value,
                    CanonicalToken = tokens[0],
                    Tokens = tokens,
                };

                foreach (var token in tokens) {
                    if (tokenLookup.ContainsKey(token)) {
                        throw new InvalidOperationException(GetParticularString(
                            "{0} is command action name, {1} is enum type name",
                            $"Command action '{method.DeclaringType?.FullName}.{method.Name}' declares duplicate command flag token '{token}' on enum '{enumType.FullName}'."));
                    }

                    tokenLookup[token] = definition;
                }

                flags.Add(definition);
            }

            if (flags.Count == 0) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command action name, {1} is parameter name",
                    $"Command action '{method.DeclaringType?.FullName}.{method.Name}' declares CommandFlagsAttribute on '{parameter.Name}', but enum '{enumType.FullName}' does not expose any [CommandFlag] members."));
            }

            return new CommandFlagsParameterDefinition {
                Name = parameter.Name ?? "flags",
                EnumType = enumType,
                Flags = [.. flags],
                TokenLookup = tokenLookup.ToImmutable(),
            };
        }

        private static CommandParamDefinition BuildParameterDefinition(
            ParameterInfo parameter,
            string name,
            CommandParamBindingSource source,
            bool hasRemainingText,
            CommandParamModifiers modifiers,
            bool optional,
            bool variadic,
            object? defaultValue,
            ImmutableArray<string> literalValues,
            CommandBindingAttribute? bindingAttribute,
            CommandParamBindingRule? bindingRule,
            CommandTokenEnumAttribute? tokenEnumAttribute,
            CommandPromptAttribute? promptOverride,
            ImmutableArray<Attribute> runtimeAttributes) {
            var bindingPrompt = bindingAttribute is not null && bindingRule is not null
                ? bindingRule.ResolvePrompt(bindingAttribute)
                : new CommandParamPromptData();
            var explicitEnumCandidates = NormalizeTokens(promptOverride?.EnumCandidates);
            var explicitSpecialTokens = NormalizeTokens(promptOverride?.AcceptedSpecialTokens);
            var effectiveType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            var tokenEnumCandidates = tokenEnumAttribute is not null
                ? CommandTokenEnumSupport.GetSpec(effectiveType).AllTokens
                : [];
            var route = ResolvePromptRouteData(
                parameter,
                source,
                hasRemainingText,
                bindingAttribute,
                bindingPrompt,
                literalValues,
                tokenEnumAttribute);

            var suggestionKindId = ResolveScalarSuggestionKindId(parameter.ParameterType);
            if (!string.IsNullOrWhiteSpace(bindingPrompt.SuggestionKindId)) {
                suggestionKindId = bindingPrompt.SuggestionKindId.Trim();
            }

            if (promptOverride is not null && !string.IsNullOrWhiteSpace(promptOverride.SuggestionKindId)) {
                suggestionKindId = promptOverride.SuggestionKindId.Trim();
            }

            if (string.IsNullOrWhiteSpace(suggestionKindId) && literalValues.Length > 0) {
                suggestionKindId = PromptSuggestionKindIds.Enum;
            }

            var promptSemanticOverride = promptOverride?.ResolveSemanticKey();

            return new CommandParamDefinition {
                Name = name,
                ParameterType = parameter.ParameterType,
                Modifiers = modifiers,
                Optional = optional,
                Variadic = variadic,
                DefaultValue = defaultValue,
                SemanticKey = promptSemanticOverride ?? bindingPrompt.SemanticKey,
                SuggestionKindId = suggestionKindId,
                ValidationMode = promptOverride?.HasValidationModeOverride == true
                    ? promptOverride.ValidationMode
                    : bindingPrompt.ValidationMode,
                RouteMatchKind = route.MatchKind,
                RouteConsumptionMode = route.ConsumptionMode,
                RouteAcceptedTokens = route.AcceptedTokens,
                RouteSpecificity = route.Specificity,
                EnumCandidates = explicitEnumCandidates.Length > 0
                    ? explicitEnumCandidates
                    : literalValues.Length > 0
                        ? literalValues
                        : tokenEnumCandidates.Length > 0
                            ? tokenEnumCandidates
                        : bindingPrompt.EnumCandidates.Length > 0
                            ? bindingPrompt.EnumCandidates
                            : effectiveType.IsEnum
                                ? [.. Enum.GetNames(effectiveType)]
                                : [],
                AcceptedSpecialTokens = MergeSpecialTokens(
                    bindingPrompt.AcceptedSpecialTokens,
                    explicitSpecialTokens,
                    modifiers),
                Metadata = PromptSlotMetadata.Normalize(bindingPrompt.Metadata),
                RuntimeAttributes = runtimeAttributes,
            };
        }

        private static PromptRouteData ResolvePromptRouteData(
            ParameterInfo parameter,
            CommandParamBindingSource source,
            bool hasRemainingText,
            CommandBindingAttribute? bindingAttribute,
            CommandParamPromptData bindingPrompt,
            ImmutableArray<string> literalValues,
            CommandTokenEnumAttribute? tokenEnumAttribute) {
            var effectiveType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            var consumptionMode = ResolveRouteConsumptionMode(
                source,
                hasRemainingText,
                bindingAttribute,
                bindingPrompt.RouteConsumptionMode);

            if (HasExplicitRouteProjection(bindingPrompt)) {
                return new PromptRouteData(
                    bindingPrompt.RouteMatchKind,
                    consumptionMode,
                    bindingPrompt.RouteAcceptedTokens,
                    bindingPrompt.RouteSpecificity);
            }

            if (literalValues.Length > 0) {
                return new PromptRouteData(
                    CommandPromptRoutes.ExactTokenSet,
                    consumptionMode,
                    literalValues,
                    CommandPromptRoutes.ExactTokenSetSpecificity);
            }

            if (tokenEnumAttribute is not null) {
                return new PromptRouteData(
                    CommandPromptRoutes.ExactTokenSet,
                    consumptionMode,
                    CommandTokenEnumSupport.GetSpec(effectiveType).AllTokens,
                    CommandPromptRoutes.ExactTokenSetSpecificity);
            }

            if (effectiveType.IsEnum) {
                return new PromptRouteData(
                    CommandPromptRoutes.ExactTokenSet,
                    consumptionMode,
                    [.. Enum.GetNames(effectiveType)],
                    CommandPromptRoutes.ExactTokenSetSpecificity);
            }

            if (effectiveType == typeof(string)) {
                return new PromptRouteData(
                    CommandPromptRoutes.FreeText,
                    consumptionMode,
                    [],
                    CommandPromptRoutes.FreeTextSpecificity);
            }

            if (effectiveType == typeof(bool)) {
                return new PromptRouteData(
                    CommandPromptRoutes.Boolean,
                    consumptionMode,
                    [],
                    CommandPromptRoutes.BooleanSpecificity);
            }

            if (bindingPrompt.ValidationMode == PromptSlotValidationMode.Integer || IsIntegerRouteType(effectiveType)) {
                return new PromptRouteData(
                    CommandPromptRoutes.Integer,
                    consumptionMode,
                    [],
                    CommandPromptRoutes.IntegerSpecificity);
            }

            return new PromptRouteData(
                CommandPromptRoutes.FreeText,
                consumptionMode,
                [],
                0);
        }
        private static CommandParamBindingSource ResolveBindingSource(
            MethodInfo method,
            ParameterInfo parameter,
            bool hasRemainingText,
            CommandBindingAttribute? bindingAttribute,
            CommandParamBindingRule? bindingRule) {
            var fromAmbientContext = parameter.GetCustomAttribute<FromAmbientContextAttribute>(inherit: false) is not null;
            if (fromAmbientContext && hasRemainingText) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command action name, {1} is parameter name",
                    $"Command action '{method.DeclaringType?.FullName}.{method.Name}' cannot mark '{parameter.Name}' with both FromAmbientContextAttribute and RemainingTextAttribute."));
            }

            if (parameter.ParameterType == typeof(CommandInvocationContext)) {
                return CommandParamBindingSource.InvocationContext;
            }

            if (parameter.ParameterType == typeof(CancellationToken)) {
                return CommandParamBindingSource.CancellationToken;
            }

            if (fromAmbientContext && parameter.ParameterType == typeof(ServerContext)) {
                return CommandParamBindingSource.ServerContext;
            }

            if (fromAmbientContext) {
                return CommandParamBindingSource.AmbientContext;
            }

            if (bindingAttribute is not null) {
                if (bindingRule is null) {
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is command action name, {1} is parameter name, {2} is binding attribute type, {3} is parameter type name",
                        $"Command action '{method.DeclaringType?.FullName}.{method.Name}' declares structured parameter '{parameter.Name}' with binding attribute '{bindingAttribute.GetType().FullName}', but no binding rule is registered for CLR type '{parameter.ParameterType.FullName}'."));
                }

                return CommandParamBindingSource.BindingRule;
            }

            if (hasRemainingText) {
                return CommandParamBindingSource.RemainingText;
            }

            if (IsUserTokenType(parameter.ParameterType)) {
                return CommandParamBindingSource.UserToken;
            }

            throw new InvalidOperationException(GetParticularString(
                "{0} is command action name, {1} is parameter name, {2} is parameter type name",
                $"Command action '{method.DeclaringType?.FullName}.{method.Name}' declares unsupported parameter '{parameter.Name}' of type '{parameter.ParameterType.FullName}'. Use a supported scalar token type, register a binding attribute, or mark the parameter with FromAmbientContextAttribute."));
        }

        private static CommandParamBindingSource ResolveMismatchBindingSource(
            MethodInfo method,
            ParameterInfo parameter) {
            var fromAmbientContext = parameter.GetCustomAttribute<FromAmbientContextAttribute>(inherit: false) is not null;
            var hasUserBindingAttribute =
                parameter.GetCustomAttribute<RemainingTextAttribute>(inherit: false) is not null
                || parameter.GetCustomAttribute<CommandParamAttribute>(inherit: false) is not null
                || parameter.GetCustomAttribute<CommandFlagsAttribute>(inherit: false) is not null
                || parameter.GetCustomAttributes(inherit: false).OfType<CommandBindingAttribute>().Any()
                || parameter.GetCustomAttribute<CommandLiteralAttribute>(inherit: false) is not null
                || parameter.GetCustomAttributes(inherit: false).OfType<CommandPromptAttribute>().Any();
            if (hasUserBindingAttribute) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command mismatch handler name, {1} is parameter name",
                    $"Command mismatch handler '{method.DeclaringType?.FullName}.{method.Name}' declares unsupported user-bound metadata on parameter '{parameter.Name}'. Mismatch handlers may only consume injected context values."));
            }

            if (parameter.ParameterType == typeof(CommandInvocationContext)) {
                return CommandParamBindingSource.InvocationContext;
            }

            if (parameter.ParameterType == typeof(CommandMismatchContext)) {
                return CommandParamBindingSource.MismatchContext;
            }

            if (parameter.ParameterType == typeof(CancellationToken)) {
                return CommandParamBindingSource.CancellationToken;
            }

            if (fromAmbientContext && parameter.ParameterType == typeof(ServerContext)) {
                return CommandParamBindingSource.ServerContext;
            }

            if (fromAmbientContext) {
                return CommandParamBindingSource.AmbientContext;
            }

            throw new InvalidOperationException(GetParticularString(
                "{0} is command mismatch handler name, {1} is parameter name, {2} is parameter type name",
                $"Command mismatch handler '{method.DeclaringType?.FullName}.{method.Name}' declares unsupported parameter '{parameter.Name}' of type '{parameter.ParameterType.FullName}'. Use CommandInvocationContext, CommandMismatchContext, CancellationToken, or mark an injected ambient parameter with FromAmbientContextAttribute."));
        }

        private static bool IsUserTokenType(Type parameterType) {
            var effectiveType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
            if (effectiveType == typeof(string) || effectiveType == typeof(bool)) {
                return true;
            }

            if (effectiveType.IsEnum) {
                return true;
            }

            return Type.GetTypeCode(effectiveType) is not TypeCode.Object and not TypeCode.Empty;
        }

        private static CommandBindingAttribute? GetExplicitBindingAttr(ParameterInfo parameter) {
            ImmutableArray<CommandBindingAttribute> bindingAttributes = [.. parameter
                .GetCustomAttributes(inherit: false)
                .OfType<CommandBindingAttribute>()];
            return bindingAttributes.Length switch {
                0 => null,
                1 => bindingAttributes[0],
                _ => throw new InvalidOperationException(GetParticularString(
                    "{0} is parameter name",
                    $"Command parameter '{parameter.Name}' declares multiple binding attributes. Each structured parameter may only declare one CommandBindingAttribute.")),
            };
        }

        private static ResolvedBinding ResolveBinding(
            Type parameterType,
            CommandBindingAttribute? explicitBindingAttribute,
            CommandRegistrationOptions options) {
            if (explicitBindingAttribute is not null) {
                return TryResolveBindingRule(
                    explicitBindingAttribute.GetType(),
                    parameterType,
                    options,
                    out var explicitRule)
                    ? new ResolvedBinding(explicitBindingAttribute, explicitRule, UsesExplicitAttribute: true)
                    : new ResolvedBinding(explicitBindingAttribute, null, UsesExplicitAttribute: true);
            }

            if (!options.ImplicitBindingsByType.TryGetValue(parameterType, out var registration)) {
                return new ResolvedBinding(null, null, UsesExplicitAttribute: false);
            }

            var key = BindingOptionsBuilder.CreateBindingRuleKey(
                registration.BindingAttributeType,
                parameterType);
            if (!options.ParameterBindingRules.TryGetValue(key, out var rule)) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command parameter type, {1} is command binding attribute type",
                    $"CLR parameter type '{parameterType.FullName}' declares implicit binding route '{registration.BindingAttributeType.FullName}', but no matching binding rule was registered."));
            }

            var attribute = registration.DefaultAttribute;
            return new ResolvedBinding(attribute, rule, UsesExplicitAttribute: false);
        }

        private static bool TryResolveBindingRule(
            Type bindingAttributeType,
            Type parameterType,
            CommandRegistrationOptions options,
            out CommandParamBindingRule? rule) {
            for (var candidateType = bindingAttributeType;
                candidateType is not null && typeof(CommandBindingAttribute).IsAssignableFrom(candidateType);
                candidateType = candidateType.BaseType) {
                var key = BindingOptionsBuilder.CreateBindingRuleKey(candidateType, parameterType);
                if (options.ParameterBindingRules.TryGetValue(key, out rule)) {
                    return true;
                }
            }

            rule = null;
            return false;
        }

        private static ImmutableArray<ICommandPromptRouteGuard> BuildPromptRouteGuards(
            ImmutableArray<Attribute> actionAttributes) {
            return [.. actionAttributes
                .OfType<ICommandPromptRouteGuardSource>()
                .Select(static source => source.CreatePromptRouteGuard())
                .Where(static guard => guard is not null)!];
        }

        private static ImmutableArray<string> NormalizeTokens(string[]? values) {
            return values is not { Length: > 0 }
                ? []
                : [.. values
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        private static ImmutableArray<string> MergeSpecialTokens(
            ImmutableArray<string> typeRuleTokens,
            ImmutableArray<string> explicitTokens,
            CommandParamModifiers modifiers) {
            HashSet<string> merged = new(StringComparer.OrdinalIgnoreCase);
            List<string> ordered = [];

            void AddRange(IEnumerable<string> values) {
                foreach (var value in values) {
                    if (string.IsNullOrWhiteSpace(value)) {
                        continue;
                    }

                    var normalized = value.Trim();
                    if (merged.Add(normalized)) {
                        ordered.Add(normalized);
                    }
                }
            }

            AddRange(typeRuleTokens);
            if ((modifiers & CommandParamModifiers.All) != 0) {
                AddRange(["*"]);
            }
            AddRange(explicitTokens);
            return [.. ordered];
        }

        private static bool HasExplicitRouteProjection(CommandParamPromptData prompt) {
            return prompt.RouteSpecificity != 0
                || prompt.RouteAcceptedTokens.Length > 0;
        }

        private static PromptRouteConsumptionMode ResolveRouteConsumptionMode(
            CommandParamBindingSource source,
            bool hasRemainingText,
            CommandBindingAttribute? bindingAttribute,
            PromptRouteConsumptionMode projectedMode) {
            if (projectedMode != PromptRouteConsumptionMode.SingleToken) {
                return projectedMode;
            }

            // Structured binders can still be declared as RemainingText. Prompt projection must
            // preserve that tail-owning shape instead of collapsing back to single-token editing.
            if (hasRemainingText || source == CommandParamBindingSource.RemainingText) {
                return CommandPromptRoutes.RemainingText;
            }

            return bindingAttribute?.ConsumesRemainingTokens == true
                ? CommandPromptRoutes.Variadic
                : CommandPromptRoutes.SingleToken;
        }

        private static bool IsIntegerRouteType(Type parameterType) {
            return Type.GetTypeCode(parameterType) is TypeCode.SByte
                or TypeCode.Byte
                or TypeCode.Int16
                or TypeCode.UInt16
                or TypeCode.Int32
                or TypeCode.UInt32
                or TypeCode.Int64
                or TypeCode.UInt64;
        }

        private static string ResolveScalarSuggestionKindId(Type parameterType) {
            var effectiveType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
            if (effectiveType == typeof(bool)) {
                return PromptSuggestionKindIds.Boolean;
            }

            if (effectiveType.IsEnum) {
                return PromptSuggestionKindIds.Enum;
            }

            return string.Empty;
        }

        private static string GetGroupSourceName(Type controllerGroupType) {
            var typeName = controllerGroupType.FullName ?? controllerGroupType.Name;
            var assemblyName = controllerGroupType.Assembly.GetName().Name ?? "unknown-assembly";
            return $"{assemblyName}:{typeName}";
        }

        private static CommandPromptAttribute? ResolvePromptOverride(ParameterInfo parameter) {
            CommandPromptAttribute[] promptAttributes =
                [.. parameter.GetCustomAttributes(inherit: false).OfType<CommandPromptAttribute>()];
            if (promptAttributes.Length > 1) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is parameter name, {1} is method name",
                    $"Command parameter '{parameter.Name}' on method '{parameter.Member.DeclaringType?.FullName}.{parameter.Member.Name}' declares multiple prompt override attributes. Use a single {nameof(CommandPromptAttribute)} or {nameof(CommandPromptSemanticAttribute)} on the parameter."));
            }

            return promptAttributes.SingleOrDefault();
        }

        private static void ValidateTokenEnumParameter(
            MethodInfo method,
            ParameterInfo parameter,
            CommandTokenEnumAttribute attribute,
            CommandBindingAttribute? bindingAttribute,
            CommandLiteralAttribute? literalAttribute) {
            var effectiveType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            if (!effectiveType.IsEnum) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command action name, {1} is parameter name",
                    $"Command action '{method.DeclaringType?.FullName}.{method.Name}' declares CommandTokenEnumAttribute on '{parameter.Name}', but the parameter type '{parameter.ParameterType.FullName}' is not an enum."));
            }

            if (bindingAttribute is not null) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command action name, {1} is parameter name",
                    $"Command action '{method.DeclaringType?.FullName}.{method.Name}' declares both CommandTokenEnumAttribute and binding attribute '{bindingAttribute.GetType().Name}' on '{parameter.Name}'."));
            }

            if (literalAttribute is not null) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command action name, {1} is parameter name",
                    $"Command action '{method.DeclaringType?.FullName}.{method.Name}' declares both CommandTokenEnumAttribute and CommandLiteralAttribute on '{parameter.Name}'."));
            }

            if (attribute.UnrecognizedTokenBehavior != CommandTokenFallbackBehavior.Fail && !parameter.HasDefaultValue) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command action name, {1} is parameter name",
                    $"Command action '{method.DeclaringType?.FullName}.{method.Name}' configures fallback token binding for '{parameter.Name}', but the parameter has no default value."));
            }

            if (CommandTokenEnumSupport.GetSpec(effectiveType).AllTokens.Length == 0) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command action name, {1} is parameter name, {2} is enum type name",
                    $"Command action '{method.DeclaringType?.FullName}.{method.Name}' declares CommandTokenEnumAttribute on '{parameter.Name}', but enum '{effectiveType.FullName}' does not expose any [CommandToken] members."));
            }
        }
    }
}
