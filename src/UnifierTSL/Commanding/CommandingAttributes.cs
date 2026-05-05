using System.Collections.Immutable;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding.Bindings;

namespace UnifierTSL.Commanding
{
    /// <summary>
    /// Declares a command root controller. V2 controllers are static grouping containers.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CommandControllerAttribute(string rootName) : Attribute
    {
        /// <summary>
        /// Gets the command root name exposed to users.
        /// </summary>
        public string RootName { get; } = rootName?.Trim() ?? string.Empty;

        /// <summary>
        /// Gets or sets the static string property name that provides the root-level summary.
        /// </summary>
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>
    /// Declares aliases for a command root.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AliasesAttribute(params string[] aliases) : Attribute
    {
        /// <summary>
        /// Gets the declared alias list.
        /// </summary>
        public ImmutableArray<string> Values { get; } = aliases is null ? [] : [.. aliases];
    }

    /// <summary>
    /// Declares the controller types owned by a single registration group.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ControllerGroupAttribute(params Type[] controllerTypes) : Attribute
    {
        /// <summary>
        /// Gets the controller types registered by this group.
        /// </summary>
        public ImmutableArray<Type> ControllerTypes { get; } = controllerTypes is null ? [] : [.. controllerTypes];
    }

    /// <summary>
    /// Declares a command action under the current controller root.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class CommandActionAttribute(string path = "") : Attribute
    {
        /// <summary>
        /// Gets the leaf path segment string. Whitespace separates subcommand tokens.
        /// </summary>
        public string Path { get; } = path?.Trim() ?? string.Empty;

        /// <summary>
        /// Gets or sets the static string property name that provides the action-level summary.
        /// </summary>
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>
    /// Declares an alternate action path token sequence for command matching and prompt surfaces.
    /// Unlike <see cref="CommandActionAttribute"/>, each constructor argument is treated as one
    /// already-tokenized segment and is not split on whitespace.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class CommandActionAliasAttribute(params string[] tokens) : Attribute
    {
        public ImmutableArray<string> Tokens { get; } = tokens is null ? [] : [.. tokens];
    }

    /// <summary>
    /// Allows an action to bind its declared user arguments and ignore any additional trailing
    /// user tokens. This affects execution matching only and does not expose a prompt parameter.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class IgnoreTrailingArgumentsAttribute : Attribute { }

    /// <summary>
    /// Declares a root-level mismatch handler invoked when no action matches the current user input.
    /// </summary>
    public enum CommandMismatchHandlingMode : byte
    {
        SyntaxOnly,
        OverrideExplicitFailure,
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class MismatchHandlerAttribute : Attribute
    {
        public CommandMismatchHandlingMode HandlingMode { get; set; } = CommandMismatchHandlingMode.SyntaxOnly;
    }

    /// <summary>
    /// Declares a synchronous action-level guard that runs after path selection but before parameter binding.
    /// Guards may allow the action to continue, skip the action, or fail early with an explicit outcome.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public abstract class PreBindGuardAttribute : Attribute
    {
        public abstract CommandGuardResult Evaluate(CommandInvocationContext context);
    }

    /// <summary>
    /// Declares a synchronous action-level guard that runs after parameter binding but before action execution.
    /// Guards may allow the action to continue, skip the action, or fail early with an explicit outcome.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public abstract class PostBindGuardAttribute : Attribute
    {
        public abstract CommandGuardResult Evaluate(CommandInvocationContext context);
    }

    /// <summary>
    /// Marks the final string parameter as receiving the remaining user input text.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class RemainingTextAttribute : Attribute
    {
        public bool Raw { get; init; }
    }

    /// <summary>
    /// Declares a first-class flag bag for the owning action. The annotated parameter must be a
    /// non-nullable enum marked with <see cref="FlagsAttribute"/>, and it does not consume a
    /// positional argument slot.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public class CommandFlagsAttribute(Type enumType) : Attribute
    {
        public Type EnumType { get; } = enumType ?? throw new ArgumentNullException(nameof(enumType));
    }

    /// <summary>
    /// Generic sugar for <see cref="CommandFlagsAttribute"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class CommandFlagsAttribute<TEnum>() : CommandFlagsAttribute(typeof(TEnum))
        where TEnum : struct, Enum;

    /// <summary>
    /// Maps a flags-enum field to one or more command-line tokens.
    /// The first token is treated as the canonical render/display token.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class CommandFlagAttribute(params string[] tokens) : Attribute
    {
        public ImmutableArray<string> Tokens { get; } = tokens is null ? [] : [.. tokens];
    }

    /// <summary>
    /// Binds the final parameter to the remaining user tokens as a preserved string array.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class RemainingArgsAttribute : CommandBindingAttribute
    {
        public override bool ConsumesRemainingTokens => true;
        public override bool IsVariadicParameter => true;
    }

    /// <summary>
    /// Marks a parameter as being supplied from the ambient invocation context rather than user tokens.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class FromAmbientContextAttribute : Attribute { }

    /// <summary>
    /// Overrides the public-facing parameter name used in help and prompt metadata.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class CommandParamAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets the public-facing parameter name used in help and prompt metadata.
        /// </summary>
        public string? Name { get; set; }
    }

    public enum ItemConsumptionMode : byte
    {
        SingleToken,
        GreedyPhrase,
    }

    public enum PrefixResolveMode : byte
    {
        Strict,
        Lenient,
    }

    public enum InvalidTokenBehavior : byte
    {
        Fail,
        UseDefault,
    }

    public enum OutOfRangeBehavior : byte
    {
        Fail,
        Clamp,
        UseDefault,
    }

    public enum CommandTokenFallbackBehavior : byte
    {
        Fail,
        UseDefault,
        UseDefaultWithoutConsuming,
    }

    public enum ItemAmountDefaultSource : byte
    {
        ParameterDefault,
        ItemMaxStack,
    }

    /// <summary>
    /// Declares a structured user-bound parameter owned by a registered binding rule.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public abstract class CommandBindingAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets the public-facing parameter name used in help and prompt metadata.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the binding modifiers understood by the owning rule.
        /// These flags affect parameter binding semantics only. Individual surfaces may impose
        /// additional validation between parameter modifiers and action availability metadata,
        /// and can fail fast during registration when the combination is invalid.
        /// </summary>
        public CommandParamModifiers Modifiers { get; set; } = CommandParamModifiers.None;

        /// <summary>
        /// Gets a value indicating whether the binding consumes all remaining user tokens.
        /// </summary>
        public virtual bool ConsumesRemainingTokens => false;

        /// <summary>
        /// Gets a value indicating whether usage and prompt surfaces should treat the parameter as
        /// a repeated logical value rather than a single tail-consuming slot.
        /// </summary>
        public virtual bool IsVariadicParameter => false;
    }

    /// <summary>
    /// Maps an enum field to one or more explicit command tokens.
    /// The first token is treated as the canonical prompt/display token.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class CommandTokenAttribute(params string[] tokens) : Attribute
    {
        public ImmutableArray<string> Tokens { get; } = tokens is null ? [] : [.. tokens];
    }

    /// <summary>
    /// Overrides scalar enum token binding to use <see cref="CommandTokenAttribute"/> values
    /// instead of CLR enum member names.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class CommandTokenEnumAttribute : Attribute
    {
        public CommandTokenFallbackBehavior UnrecognizedTokenBehavior { get; set; } = CommandTokenFallbackBehavior.Fail;

        public string InvalidTokenMessage { get; set; } = nameof(DefaultInvalidTokenMessage);

        private static string DefaultInvalidTokenMessage => GetString("Invalid command syntax.");
    }

    /// <summary>
    /// Declares that a parameter should bind through the host-neutral player selector track.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class PlayerSelectorAttribute : CommandBindingAttribute { }

    /// <summary>
    /// Declares that an integer parameter should resolve Terraria item references.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class ItemRefAttribute : CommandBindingAttribute
    {
        public ItemConsumptionMode ConsumptionMode { get; set; } = ItemConsumptionMode.SingleToken;
    }

    /// <summary>
    /// Declares that an integer parameter should resolve Terraria buff references.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class BuffRefAttribute : CommandBindingAttribute { }

    /// <summary>
    /// Declares that an integer parameter should resolve Terraria prefix references.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class PrefixRefAttribute : CommandBindingAttribute
    {
        public PrefixResolveMode ResolveMode { get; set; } = PrefixResolveMode.Strict;
    }

    /// <summary>
    /// Binds a scalar integer value with declarative validation and fallback behavior.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class Int32ValueAttribute : CommandBindingAttribute
    {
        public int Minimum { get; set; } = int.MinValue;

        public int Maximum { get; set; } = int.MaxValue;

        public InvalidTokenBehavior InvalidTokenBehavior { get; set; } = InvalidTokenBehavior.Fail;

        public OutOfRangeBehavior OutOfRangeBehavior { get; set; } = OutOfRangeBehavior.Fail;

        public string InvalidTokenMessage { get; set; } = nameof(DefaultInvalidTokenMessage);

        public string? OutOfRangeMessage { get; set; }

        private static string DefaultInvalidTokenMessage => GetString("Invalid command syntax.");
    }

    /// <summary>
    /// Binds a scalar boolean value with declarative validation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class BooleanValueAttribute : CommandBindingAttribute
    {
        public string InvalidTokenMessage { get; set; } = nameof(DefaultInvalidTokenMessage);

        private static string DefaultInvalidTokenMessage => GetString("Invalid command syntax.");
    }

    /// <summary>
    /// Applies Terraria item-stack defaults and overflow handling to a numeric amount parameter.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class ItemAmountAttribute : CommandBindingAttribute
    {
        public string? ItemParameterName { get; set; }

        public InvalidTokenBehavior InvalidTokenBehavior { get; set; } = InvalidTokenBehavior.Fail;

        public OutOfRangeBehavior OutOfRangeBehavior { get; set; } = OutOfRangeBehavior.Fail;

        public ItemAmountDefaultSource DefaultSource { get; set; } = ItemAmountDefaultSource.ParameterDefault;

        public bool TreatZeroAsDefault { get; set; }
    }

    /// <summary>
    /// Overrides prompt-facing metadata for a user-bound parameter without changing runtime binding semantics.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public class CommandPromptAttribute : Attribute
    {
        private PromptSlotValidationMode validationMode;
        private bool hasValidationModeOverride;

        /// <summary>
        /// Gets or sets the prompt suggestion kind identifier for this parameter.
        /// </summary>
        public string SuggestionKindId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets explicit enum-like candidates exposed to prompt UIs.
        /// </summary>
        public string[] EnumCandidates { get; set; } = [];

        /// <summary>
        /// Gets or sets literal special tokens accepted by the parameter, such as "*".
        /// </summary>
        public string[] AcceptedSpecialTokens { get; set; } = [];

        /// <summary>
        /// Gets or sets prompt-side validation semantics for the parameter token.
        /// </summary>
        public PromptSlotValidationMode ValidationMode {
            get => validationMode;
            set {
                validationMode = value;
                hasValidationModeOverride = true;
            }
        }

        internal bool HasValidationModeOverride => hasValidationModeOverride;

        internal virtual SemanticKey? ResolveSemanticKey() => null;
    }

    /// <summary>
    /// Binds prompt-facing semantic metadata to a key catalog member.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public class CommandPromptSemanticAttribute(Type semanticKeyCatalogType, string semanticKeyMemberName) : CommandPromptAttribute
    {
        public Type SemanticKeyCatalogType { get; } = semanticKeyCatalogType ?? throw new ArgumentNullException(nameof(semanticKeyCatalogType));

        public string SemanticKeyMemberName { get; } = string.IsNullOrWhiteSpace(semanticKeyMemberName)
            ? throw new ArgumentException(GetString("Semantic key member name must not be empty."), nameof(semanticKeyMemberName))
            : semanticKeyMemberName.Trim();

        internal override SemanticKey? ResolveSemanticKey() {
            return SemanticKeyCatalog.Resolve(SemanticKeyCatalogType, SemanticKeyMemberName);
        }
    }

    /// <summary>
    /// Generic sugar for semantic prompt metadata backed by a semantic-key catalog type.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class CommandPromptSemanticAttribute<TCatalog>(string semanticKeyMemberName)
        : CommandPromptSemanticAttribute(typeof(TCatalog), semanticKeyMemberName)
        where TCatalog : class;

    /// <summary>
    /// Restricts a parameter to one of the declared literal token values.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class CommandLiteralAttribute(params string[] values) : Attribute
    {
        /// <summary>
        /// Gets the accepted literal token values.
        /// </summary>
        public ImmutableArray<string> Values { get; } = values is null ? [] : [.. values];
    }

    /// <summary>
    /// Exposes a command action to UTSL terminal execution and prompt surfaces.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class TerminalCommandAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets a value indicating whether the action is available from the launcher console.
        /// </summary>
        public bool AllowLauncherConsole { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the action is available from per-server consoles.
        /// </summary>
        public bool AllowServerConsole { get; set; } = true;
    }
}
