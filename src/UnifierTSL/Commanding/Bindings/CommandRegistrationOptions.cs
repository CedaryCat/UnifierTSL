using System.Collections.Immutable;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding.Composition;

namespace UnifierTSL.Commanding.Bindings
{
    [Flags]
    public enum CommandParamModifiers : byte
    {
        None = 0,
        All = 1 << 0,
        // Narrows this parameter's binding/search scope to the current server context.
        // Surfaces that expose server-scoped execution may validate that the owning action can
        // actually provide this context and fail fast during registration when it cannot.
        ServerScope = 1 << 1,
        ExcludeCurrentContext = 1 << 2,
    }

    public sealed record CommandParamPromptData
    {
        public string SuggestionKindId { get; init; } = string.Empty;

        public SemanticKey? SemanticKey { get; init; }

        public PromptSlotValidationMode ValidationMode { get; init; }

        public PromptRouteMatchKind RouteMatchKind { get; init; }

        public PromptRouteConsumptionMode RouteConsumptionMode { get; init; }

        public ImmutableArray<string> RouteAcceptedTokens { get; init; } = [];

        public int RouteSpecificity { get; init; }

        public ImmutableArray<string> EnumCandidates { get; init; } = [];

        public ImmutableArray<string> AcceptedSpecialTokens { get; init; } = [];

        public ImmutableArray<PromptSlotMetadataEntry> Metadata { get; init; } = [];
    }

    public sealed record CommandBoundParameterValue
    {
        public required CommandParamDefinition Parameter { get; init; }

        public required object? Value { get; init; }
    }

    public readonly record struct CommandParamBindingContext(
        CommandInvocationContext InvocationContext,
        ImmutableArray<string> UserArguments,
        int UserIndex,
        CommandParamDefinition Parameter,
        CommandBindingAttribute? BindingAttribute,
        ImmutableArray<Attribute> RuntimeAttributes,
        ImmutableArray<CommandBoundParameterValue> BoundParameters,
        CommandParamModifiers Modifiers,
        CancellationToken CancellationToken)
    {
    }

    public readonly record struct BindingCandidate(object? Value, int ConsumedTokens);

    public readonly record struct CommandParamBindingResult(
        bool IsMatch,
        ImmutableArray<BindingCandidate> Candidates,
        CommandOutcome? FailureOutcome,
        int FailureConsumedTokens)
    {
        public bool IsSuccess => IsMatch && Candidates.Length > 0;

        public int ConsumedTokens => Candidates.Length == 1 ? Candidates[0].ConsumedTokens : 0;

        public object? Value => Candidates.Length == 1 ? Candidates[0].Value : null;

        public static CommandParamBindingResult Success(
            object? value,
            int consumedTokens = 1,
            CommandOutcome? fallbackOutcome = null,
            int fallbackConsumedTokens = 0) {
            if (consumedTokens < 0) {
                throw new ArgumentOutOfRangeException(nameof(consumedTokens));
            }

            return new CommandParamBindingResult(
                IsMatch: true,
                Candidates: [new BindingCandidate(value, consumedTokens)],
                FailureOutcome: fallbackOutcome,
                FailureConsumedTokens: fallbackConsumedTokens);
        }

        public static CommandParamBindingResult SuccessMany(
            IEnumerable<BindingCandidate> candidates,
            CommandOutcome? fallbackOutcome = null,
            int fallbackConsumedTokens = 0) {
            ArgumentNullException.ThrowIfNull(candidates);

            ImmutableArray<BindingCandidate> materialized = [.. candidates];
            if (materialized.Length == 0) {
                throw new ArgumentException(GetString("At least one binding candidate is required."), nameof(candidates));
            }

            if (materialized.Any(static candidate => candidate.ConsumedTokens < 0)) {
                throw new ArgumentOutOfRangeException(nameof(candidates), GetString("Consumed token count cannot be negative."));
            }

            return new CommandParamBindingResult(
                IsMatch: true,
                Candidates: materialized,
                FailureOutcome: fallbackOutcome,
                FailureConsumedTokens: fallbackConsumedTokens);
        }

        public static CommandParamBindingResult Mismatch() => new(
            IsMatch: false,
            Candidates: [],
            FailureOutcome: null,
            FailureConsumedTokens: 0);

        public static CommandParamBindingResult Failure(CommandOutcome outcome, int consumedTokens = 0) => new(
            IsMatch: true,
            Candidates: [],
            FailureOutcome: outcome,
            FailureConsumedTokens: consumedTokens);
    }

    public delegate CommandParamBindingResult BindingRuleBinder(
        CommandParamBindingContext context,
        CommandBindingAttribute attribute);

    public sealed record CommandParamBindingRule
    {
        public required BindingRuleBinder Binder { get; init; }

        public Func<CommandBindingAttribute, CommandParamPromptData>? PromptResolver { get; init; }

        public CommandParamModifiers SupportedModifiers { get; init; } = CommandParamModifiers.None;

        public string? DefaultName { get; init; }

        public CommandParamPromptData ResolvePrompt(CommandBindingAttribute attribute) {
            ArgumentNullException.ThrowIfNull(attribute);
            return PromptResolver?.Invoke(attribute) ?? new CommandParamPromptData();
        }

        public static CommandParamBindingRule Create<TAttribute>(
            Func<CommandParamBindingContext, TAttribute, CommandParamBindingResult> binder,
            Func<TAttribute, CommandParamPromptData>? promptResolver = null,
            CommandParamModifiers supportedModifiers = CommandParamModifiers.None,
            string? defaultName = null)
            where TAttribute : CommandBindingAttribute {
            ArgumentNullException.ThrowIfNull(binder);

            return new CommandParamBindingRule {
                Binder = (context, attribute) => binder(context, (TAttribute)attribute),
                PromptResolver = promptResolver is null
                    ? null
                    : attribute => promptResolver((TAttribute)attribute),
                SupportedModifiers = supportedModifiers,
                DefaultName = defaultName,
            };
        }
    }

    internal readonly record struct BindingRuleKey(Type BindingAttributeType, Type ParameterType);

    internal readonly record struct ImplicitBindingRegistration(
        Type BindingAttributeType,
        CommandBindingAttribute DefaultAttribute);

    public interface ICommandBindingRegistry
    {
        void AddBindingRule<TAttribute, TParameter>(
            Func<CommandParamBindingContext, TAttribute, CommandParamBindingResult> binder,
            Func<TAttribute, CommandParamPromptData>? promptResolver = null,
            CommandParamModifiers supportedModifiers = CommandParamModifiers.None,
            string? defaultName = null)
            where TAttribute : CommandBindingAttribute;

        void AddBindingRule<TAttribute, TParameter>(CommandParamBindingRule rule)
            where TAttribute : CommandBindingAttribute;

        void AddImplicitBindingRule<TAttribute, TParameter>(
            Func<CommandParamBindingContext, TAttribute, CommandParamBindingResult> binder,
            Func<TAttribute, CommandParamPromptData>? promptResolver = null,
            CommandParamModifiers supportedModifiers = CommandParamModifiers.None,
            string? defaultName = null)
            where TAttribute : CommandBindingAttribute, new();

        void AddImplicitBindingRule<TAttribute, TParameter>(
            Func<CommandParamBindingContext, TAttribute, CommandParamBindingResult> binder,
            Func<TAttribute, CommandParamPromptData>? promptResolver,
            CommandParamModifiers supportedModifiers,
            string? defaultName,
            TAttribute defaultAttribute)
            where TAttribute : CommandBindingAttribute;

        void AddImplicitBindingRule<TAttribute, TParameter>(
            CommandParamBindingRule rule,
            TAttribute defaultAttribute)
            where TAttribute : CommandBindingAttribute;
    }

    public sealed record CommandRegistrationOptions
    {
        internal ImmutableDictionary<BindingRuleKey, CommandParamBindingRule> ParameterBindingRules { get; init; } =
            ImmutableDictionary<BindingRuleKey, CommandParamBindingRule>.Empty;

        internal ImmutableDictionary<Type, ImplicitBindingRegistration> ImplicitBindingsByType { get; init; } =
            ImmutableDictionary<Type, ImplicitBindingRegistration>.Empty;

        public static CommandRegistrationOptions Empty { get; } = new();
    }

    public sealed class BindingOptionsBuilder : ICommandBindingRegistry
    {
        private readonly Dictionary<BindingRuleKey, CommandParamBindingRule> parameterBindingRules = [];
        private readonly Dictionary<Type, ImplicitBindingRegistration> implicitBindingsByType = [];

        public BindingOptionsBuilder AddBindingRule<TAttribute, TParameter>(
            Func<CommandParamBindingContext, TAttribute, CommandParamBindingResult> binder,
            Func<TAttribute, CommandParamPromptData>? promptResolver = null,
            CommandParamModifiers supportedModifiers = CommandParamModifiers.None,
            string? defaultName = null)
            where TAttribute : CommandBindingAttribute {
            ArgumentNullException.ThrowIfNull(binder);

            return AddBindingRule<TAttribute, TParameter>(CommandParamBindingRule.Create(
                binder,
                promptResolver,
                supportedModifiers,
                defaultName));
        }

        public BindingOptionsBuilder AddBindingRule<TAttribute, TParameter>(CommandParamBindingRule rule)
            where TAttribute : CommandBindingAttribute {
            ArgumentNullException.ThrowIfNull(rule);

            var attributeType = typeof(TAttribute);
            var parameterType = typeof(TParameter);
            var key = CreateBindingRuleKey(attributeType, parameterType);
            if (parameterBindingRules.ContainsKey(key)) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command binding attribute type, {1} is command parameter type",
                    $"A command binding rule for binding attribute '{attributeType.FullName}' and CLR parameter type '{parameterType.FullName}' is already registered in this registration scope."));
            }

            parameterBindingRules.Add(key, rule);
            return this;
        }

        public BindingOptionsBuilder AddImplicitBindingRule<TAttribute, TParameter>(
            Func<CommandParamBindingContext, TAttribute, CommandParamBindingResult> binder,
            Func<TAttribute, CommandParamPromptData>? promptResolver = null,
            CommandParamModifiers supportedModifiers = CommandParamModifiers.None,
            string? defaultName = null)
            where TAttribute : CommandBindingAttribute, new() {
            return AddImplicitBindingRule<TAttribute, TParameter>(
                binder,
                promptResolver,
                supportedModifiers,
                defaultName,
                new TAttribute());
        }

        public BindingOptionsBuilder AddImplicitBindingRule<TAttribute, TParameter>(
            Func<CommandParamBindingContext, TAttribute, CommandParamBindingResult> binder,
            Func<TAttribute, CommandParamPromptData>? promptResolver,
            CommandParamModifiers supportedModifiers,
            string? defaultName,
            TAttribute defaultAttribute)
            where TAttribute : CommandBindingAttribute {
            ArgumentNullException.ThrowIfNull(binder);
            ArgumentNullException.ThrowIfNull(defaultAttribute);

            return AddImplicitBindingRule<TAttribute, TParameter>(
                CommandParamBindingRule.Create(
                    binder,
                    promptResolver,
                    supportedModifiers,
                    defaultName),
                defaultAttribute);
        }

        public BindingOptionsBuilder AddImplicitBindingRule<TAttribute, TParameter>(
            CommandParamBindingRule rule,
            TAttribute defaultAttribute)
            where TAttribute : CommandBindingAttribute {
            ArgumentNullException.ThrowIfNull(rule);
            ArgumentNullException.ThrowIfNull(defaultAttribute);

            AddBindingRule<TAttribute, TParameter>(rule);

            var parameterType = typeof(TParameter);
            if (implicitBindingsByType.ContainsKey(parameterType)) {
                var existing = implicitBindingsByType[parameterType];
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command parameter type, {1} is existing command binding attribute type, {2} is new command binding attribute type",
                    $"CLR parameter type '{parameterType.FullName}' already has an implicit binding route registered for binding attribute '{existing.BindingAttributeType.FullName}'. It cannot also default to '{typeof(TAttribute).FullName}' in the same registration scope."));
            }

            implicitBindingsByType.Add(
                parameterType,
                new ImplicitBindingRegistration(
                    typeof(TAttribute),
                    defaultAttribute));
            return this;
        }

        public CommandRegistrationOptions Build() {
            return new CommandRegistrationOptions {
                ParameterBindingRules = parameterBindingRules.ToImmutableDictionary(),
                ImplicitBindingsByType = implicitBindingsByType.ToImmutableDictionary(),
            };
        }

        internal static BindingRuleKey CreateBindingRuleKey(Type bindingAttributeType, Type parameterType) {
            return new BindingRuleKey(bindingAttributeType, parameterType);
        }

        void ICommandBindingRegistry.AddBindingRule<TAttribute, TParameter>(
            Func<CommandParamBindingContext, TAttribute, CommandParamBindingResult> binder,
            Func<TAttribute, CommandParamPromptData>? promptResolver,
            CommandParamModifiers supportedModifiers,
            string? defaultName) {
            AddBindingRule<TAttribute, TParameter>(binder, promptResolver, supportedModifiers, defaultName);
        }

        void ICommandBindingRegistry.AddBindingRule<TAttribute, TParameter>(CommandParamBindingRule rule) {
            AddBindingRule<TAttribute, TParameter>(rule);
        }

        void ICommandBindingRegistry.AddImplicitBindingRule<TAttribute, TParameter>(
            Func<CommandParamBindingContext, TAttribute, CommandParamBindingResult> binder,
            Func<TAttribute, CommandParamPromptData>? promptResolver,
            CommandParamModifiers supportedModifiers,
            string? defaultName) {
            AddImplicitBindingRule<TAttribute, TParameter>(binder, promptResolver, supportedModifiers, defaultName);
        }

        void ICommandBindingRegistry.AddImplicitBindingRule<TAttribute, TParameter>(
            Func<CommandParamBindingContext, TAttribute, CommandParamBindingResult> binder,
            Func<TAttribute, CommandParamPromptData>? promptResolver,
            CommandParamModifiers supportedModifiers,
            string? defaultName,
            TAttribute defaultAttribute) {
            AddImplicitBindingRule<TAttribute, TParameter>(binder, promptResolver, supportedModifiers, defaultName, defaultAttribute);
        }

        void ICommandBindingRegistry.AddImplicitBindingRule<TAttribute, TParameter>(
            CommandParamBindingRule rule,
            TAttribute defaultAttribute) {
            AddImplicitBindingRule<TAttribute, TParameter>(rule, defaultAttribute);
        }
    }

}
