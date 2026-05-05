using System.Collections.Immutable;
using System.Reflection;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Bindings;
using UnifierTSL.Commanding.Execution;
using UnifierTSL.Commanding.Prompting;
using UnifierTSL.Commanding.Endpoints;

namespace UnifierTSL.Commanding.Composition
{
    public sealed record CommandCatalog
    {
        public ImmutableArray<CommandRootDefinition> Roots { get; init; } = [];
    }

    public sealed record CommandControllerGroupRegistration
    {
        public required Type ControllerGroupType { get; init; }
    }

    public sealed record CommandRootDefinition
    {
        public required string SourceName { get; init; }

        public required string RootName { get; init; }

        public ImmutableArray<string> Aliases { get; init; } = [];

        public string Summary { get; init; } = string.Empty;

        public required Type ControllerType { get; init; }

        public ImmutableArray<Attribute> ControllerAttributes { get; init; } = [];

        public required ImmutableArray<CommandActionDefinition> Actions { get; init; }

        public MismatchHandlerDefinition? MismatchHandler { get; init; }
    }

    public sealed record CommandActionDefinition
    {
        public required string Summary { get; init; }

        public required ImmutableArray<string> PathSegments { get; init; }

        public ImmutableArray<ImmutableArray<string>> PathAliases { get; init; } = [];

        public required ImmutableArray<CommandParamDefinition> Parameters { get; init; }

        public CommandFlagsParameterDefinition? FlagsParameter { get; init; }

        public bool IgnoreTrailingArguments { get; init; }

        public required MethodInfo Method { get; init; }

        public ImmutableArray<PreBindGuardAttribute> PreBindGuards { get; init; } = [];

        public ImmutableArray<PostBindGuardAttribute> PostBindGuards { get; init; } = [];

        public ImmutableArray<ICommandPromptRouteGuard> PromptRouteGuards { get; init; } = [];

        public ImmutableArray<Attribute> ActionAttributes { get; init; } = [];

        public TAttribute? GetActionAttribute<TAttribute>() where TAttribute : Attribute {
            return ActionAttributes.OfType<TAttribute>().SingleOrDefault();
        }

        internal CommandActionRuntime Runtime { get; init; } = null!;
    }

    public sealed record CommandFlagsParameterDefinition
    {
        public required string Name { get; init; }

        public required Type EnumType { get; init; }

        public required ImmutableArray<CommandFlagDefinition> Flags { get; init; }

        internal ImmutableDictionary<string, CommandFlagDefinition> TokenLookup { get; init; } =
            ImmutableDictionary<string, CommandFlagDefinition>.Empty;

        public bool TryResolve(string token, out CommandFlagDefinition flag) {
            return TokenLookup.TryGetValue(token, out flag!);
        }
    }

    public sealed record CommandFlagDefinition
    {
        public required string MemberName { get; init; }
        public required ulong Value { get; init; }
        public required string CanonicalToken { get; init; }
        public ImmutableArray<string> Tokens { get; init; } = [];
    }

    public sealed record MismatchHandlerDefinition
    {
        public required MethodInfo Method { get; init; }
        public CommandMismatchHandlingMode HandlingMode { get; init; } = CommandMismatchHandlingMode.SyntaxOnly;
        internal CommandActionRuntime Runtime { get; init; } = null!;
    }

    public sealed class CommandRegistrationBuilder
    {
        private readonly Dictionary<Type, CommandControllerGroupRegistration> controllerGroups = [];
        private readonly List<Action<ICommandBindingRegistry>> bindingConfigurators = [];
        private readonly HashSet<Action<ICommandBindingRegistry>> bindingConfiguratorSet = [];
        private readonly HashSet<Type> endpointTypes = [];
        private readonly List<CommandEndpointActionBindingRule> actionBindingRules = [];
        private readonly HashSet<EndpointBindingRuleKey> actionBindingRuleKeys = [];
        private readonly CommandOutcomeWriterRegistryBuilder outcomeWriters = new();

        internal CommandRegistrationBuilder() { }

        private readonly record struct EndpointBindingRuleKey(
            Type EndpointType,
            CommandEndpointActionBinder BindAction);

        public void AddControllerGroup<TControllerGroup>() {
            AddControllerGroup(typeof(TControllerGroup));
        }

        public void AddControllerGroup(Type controllerGroupType) {
            ArgumentNullException.ThrowIfNull(controllerGroupType);

            if (controllerGroups.TryGetValue(controllerGroupType, out var existing)) {
                return;
            }

            controllerGroups.Add(controllerGroupType, new CommandControllerGroupRegistration {
                ControllerGroupType = controllerGroupType,
            });
        }

        public void AddBindings(Action<ICommandBindingRegistry> configure) {
            ArgumentNullException.ThrowIfNull(configure);

            if (bindingConfiguratorSet.Add(configure)) {
                bindingConfigurators.Add(configure);
            }
        }

        public void AddEndpoint<TEndpoint>() where TEndpoint : ICommandEndpoint, new() {
            endpointTypes.Add(typeof(TEndpoint));
        }

        public void AddEndpointBinding<TEndpoint>(CommandEndpointActionBinder bindAction)
            where TEndpoint : ICommandEndpoint, new() {
            ArgumentNullException.ThrowIfNull(bindAction);

            var endpointType = typeof(TEndpoint);
            endpointTypes.Add(endpointType);
            if (actionBindingRuleKeys.Add(new EndpointBindingRuleKey(endpointType, bindAction))) {
                actionBindingRules.Add(new CommandEndpointActionBindingRule {
                    EndpointType = endpointType,
                    BindAction = bindAction,
                });
            }
        }

        public void AddOutcomeWriter<TSink, TWriter>() where TWriter : ICommandOutcomeWriter<TSink>, new() {
            outcomeWriters.AddWriter<TSink, TWriter>();
        }

        public void AddOutcomeWriter<TSink>(ICommandOutcomeWriter<TSink> writer) {
            outcomeWriters.AddWriter(writer);
        }

        internal ImmutableArray<CommandControllerGroupRegistration> GetControllerGroups() {
            return [.. controllerGroups.Values.OrderBy(static registration => registration.ControllerGroupType.FullName, StringComparer.Ordinal)];
        }

        internal ImmutableArray<Action<ICommandBindingRegistry>> GetBindingConfigurators() {
            return [.. bindingConfigurators];
        }

        internal ImmutableArray<Type> GetEndpoints() {
            return [.. endpointTypes.OrderBy(static type => type.FullName, StringComparer.Ordinal)];
        }

        internal ImmutableArray<CommandEndpointActionBindingRule> GetActionBindingRules() {
            return [.. actionBindingRules];
        }

        internal ImmutableArray<CommandOutcomeWriterRegistration> GetOutcomeWriters() {
            return outcomeWriters.Build();
        }
    }

    public sealed record CommandParamDefinition
    {
        public required string Name { get; init; }
        public required Type ParameterType { get; init; }
        public CommandParamModifiers Modifiers { get; init; }
        public bool Optional { get; init; }
        public bool Variadic { get; init; }
        public bool ConsumesRemainingTokens => RouteConsumptionMode is PromptRouteConsumptionMode.RemainingText or PromptRouteConsumptionMode.Variadic;
        public object? DefaultValue { get; init; }
        public SemanticKey? SemanticKey { get; init; }
        public string SuggestionKindId { get; init; } = string.Empty;
        public PromptSlotValidationMode ValidationMode { get; init; }
        public PromptRouteMatchKind RouteMatchKind { get; init; }
        public PromptRouteConsumptionMode RouteConsumptionMode { get; init; }
        public ImmutableArray<string> RouteAcceptedTokens { get; init; } = [];
        public int RouteSpecificity { get; init; }
        public ImmutableArray<string> EnumCandidates { get; init; } = [];
        public ImmutableArray<string> AcceptedSpecialTokens { get; init; } = [];
        public ImmutableArray<PromptSlotMetadataEntry> Metadata { get; init; } = [];
        public ImmutableArray<Attribute> RuntimeAttributes { get; init; } = [];
    }

}
