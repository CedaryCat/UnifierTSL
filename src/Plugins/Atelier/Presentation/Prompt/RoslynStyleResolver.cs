using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Tags;

namespace Atelier.Presentation.Prompt;

internal static class RoslynStyleResolver
{
    private static readonly (string StyleId, string[] Tags)[] TaggedTextStyleGroups = [
        (PromptStyles.SyntaxError, [WellKnownTags.Warning, WellKnownTags.Error, TextTags.ErrorType]),
        (PromptStyles.SyntaxControlKeyword, ["ControlKeyword"]),
        (PromptStyles.SyntaxKeyword, [WellKnownTags.Keyword, TextTags.Keyword]),
        (PromptStyles.SyntaxOperator, [TextTags.Operator]),
        (PromptStyles.SyntaxMethod, [WellKnownTags.Method, WellKnownTags.ExtensionMethod, TextTags.Method, TextTags.ExtensionMethod]),
        (PromptStyles.SyntaxStruct, [WellKnownTags.Structure, TextTags.RecordStruct, TextTags.Struct, TextTags.RecordStruct]),
        (PromptStyles.SyntaxInterface, [WellKnownTags.Interface, TextTags.Interface]),
        (PromptStyles.SyntaxEnum, [WellKnownTags.Enum, TextTags.Enum]),
        (PromptStyles.SyntaxTypeParameter, [WellKnownTags.TypeParameter, TextTags.TypeParameter]),
        (PromptStyles.SyntaxType, [WellKnownTags.Class, WellKnownTags.Delegate, WellKnownTags.Module,
            TextTags.Class, TextTags.Delegate, TextTags.Record, TextTags.Module]),
        (PromptStyles.SyntaxMember, [WellKnownTags.Property, WellKnownTags.Field, WellKnownTags.Event, WellKnownTags.Namespace,
            WellKnownTags.EnumMember, WellKnownTags.Constant, WellKnownTags.Label,
            TextTags.Property, TextTags.Field, TextTags.Event, TextTags.Namespace, TextTags.EnumMember, TextTags.Constant, TextTags.Label]),
        (PromptStyles.SyntaxString, [TextTags.StringLiteral]),
        (PromptStyles.SyntaxNumber, [TextTags.NumericLiteral]),
        (PromptStyles.SyntaxValue, [WellKnownTags.Local, WellKnownTags.Parameter, TextTags.Local, TextTags.Parameter, TextTags.RangeVariable]),
    ];
    private static readonly (string StyleId, string[] Tags)[] CompletionTagStyleGroups = [
        (PromptStyles.SyntaxError, [WellKnownTags.Warning, "StatusWarning", WellKnownTags.Error]),
        (PromptStyles.SyntaxMethod, [WellKnownTags.Method, WellKnownTags.ExtensionMethod]),
        (PromptStyles.SyntaxModifier, [WellKnownTags.Snippet]),
        (PromptStyles.SyntaxStruct, [WellKnownTags.Structure, TextTags.RecordStruct]),
        (PromptStyles.SyntaxInterface, [WellKnownTags.Interface]),
        (PromptStyles.SyntaxEnum, [WellKnownTags.Enum]),
        (PromptStyles.SyntaxTypeParameter, [WellKnownTags.TypeParameter]),
        (PromptStyles.SyntaxType, [WellKnownTags.Class, WellKnownTags.Delegate, TextTags.Record, WellKnownTags.Module]),
        (PromptStyles.SyntaxMember, [WellKnownTags.Property, WellKnownTags.Field, WellKnownTags.Event,
            WellKnownTags.EnumMember, WellKnownTags.Constant, WellKnownTags.Namespace, WellKnownTags.Label]),
        (PromptStyles.SyntaxValue, [WellKnownTags.Local, WellKnownTags.Parameter]),
        (PromptStyles.SyntaxOperator, [WellKnownTags.Operator]),
        (PromptStyles.SyntaxKeyword, [WellKnownTags.Intrinsic]),
    ];

    public static string ResolveClassificationStyleId(string? classificationType) {
        return classificationType switch {
            ClassificationTypeNames.ControlKeyword => PromptStyles.SyntaxControlKeyword,
            ClassificationTypeNames.Keyword => PromptStyles.SyntaxKeyword,
            ClassificationTypeNames.Comment => PromptStyles.SyntaxComment,
            ClassificationTypeNames.MethodName or ClassificationTypeNames.ExtensionMethodName => PromptStyles.SyntaxMethod,
            ClassificationTypeNames.StructName or ClassificationTypeNames.RecordStructName => PromptStyles.SyntaxStruct,
            ClassificationTypeNames.InterfaceName => PromptStyles.SyntaxInterface,
            ClassificationTypeNames.EnumName => PromptStyles.SyntaxEnum,
            ClassificationTypeNames.TypeParameterName => PromptStyles.SyntaxTypeParameter,
            ClassificationTypeNames.ClassName or ClassificationTypeNames.RecordClassName
                or ClassificationTypeNames.DelegateName or ClassificationTypeNames.ModuleName => PromptStyles.SyntaxType,
            ClassificationTypeNames.FieldName or ClassificationTypeNames.EventName or ClassificationTypeNames.NamespaceName
                or ClassificationTypeNames.PropertyName or ClassificationTypeNames.EnumMemberName or ClassificationTypeNames.ConstantName
                or ClassificationTypeNames.Identifier or ClassificationTypeNames.LabelName => PromptStyles.SyntaxMember,
            ClassificationTypeNames.StringLiteral or ClassificationTypeNames.VerbatimStringLiteral => PromptStyles.SyntaxString,
            ClassificationTypeNames.NumericLiteral => PromptStyles.SyntaxNumber,
            ClassificationTypeNames.Operator or ClassificationTypeNames.OperatorOverloaded => PromptStyles.SyntaxOperator,
            ClassificationTypeNames.LocalName or ClassificationTypeNames.ParameterName => PromptStyles.SyntaxValue,
            _ => string.Empty,
        };
    }

    public static string ResolveTaggedTextStyleId(string? tag) {
        return string.IsNullOrWhiteSpace(tag)
            ? string.Empty
            : ResolveTagStyle([tag], TaggedTextStyleGroups, StringComparer.OrdinalIgnoreCase);
    }

    public static string ResolveSymbolDisplayPartStyleId(SymbolDisplayPartKind kind) {
        return kind switch {
            SymbolDisplayPartKind.ErrorTypeName => PromptStyles.SyntaxError,
            SymbolDisplayPartKind.Keyword => PromptStyles.SyntaxKeyword,
            SymbolDisplayPartKind.Operator => PromptStyles.SyntaxOperator,
            SymbolDisplayPartKind.MethodName or SymbolDisplayPartKind.ExtensionMethodName => PromptStyles.SyntaxMethod,
            SymbolDisplayPartKind.StructName or SymbolDisplayPartKind.RecordStructName => PromptStyles.SyntaxStruct,
            SymbolDisplayPartKind.InterfaceName => PromptStyles.SyntaxInterface,
            SymbolDisplayPartKind.EnumName => PromptStyles.SyntaxEnum,
            SymbolDisplayPartKind.TypeParameterName => PromptStyles.SyntaxTypeParameter,
            SymbolDisplayPartKind.ClassName or SymbolDisplayPartKind.DelegateName
                or SymbolDisplayPartKind.ModuleName or SymbolDisplayPartKind.RecordClassName => PromptStyles.SyntaxType,
            SymbolDisplayPartKind.AliasName or SymbolDisplayPartKind.FieldName or SymbolDisplayPartKind.EventName
                or SymbolDisplayPartKind.NamespaceName or SymbolDisplayPartKind.PropertyName or SymbolDisplayPartKind.EnumMemberName
                or SymbolDisplayPartKind.ConstantName or SymbolDisplayPartKind.LabelName => PromptStyles.SyntaxMember,
            SymbolDisplayPartKind.StringLiteral => PromptStyles.SyntaxString,
            SymbolDisplayPartKind.NumericLiteral => PromptStyles.SyntaxNumber,
            SymbolDisplayPartKind.LocalName or SymbolDisplayPartKind.ParameterName or SymbolDisplayPartKind.RangeVariableName => PromptStyles.SyntaxValue,
            _ => string.Empty,
        };
    }

    public static string ResolveCompletionDisplayStyleId(IEnumerable<string>? tags, string? displayText) {
        var itemTags = tags?.ToArray() ?? [];
        if (itemTags.Contains(WellKnownTags.Keyword, StringComparer.Ordinal)) {
            return PromptStyles.SyntaxKeyword;
        }

        var styleId = ResolveTagStyle(itemTags, CompletionTagStyleGroups, StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(styleId)) {
            return styleId;
        }

        if (IsKeyword(displayText)) {
            return PromptStyles.SyntaxKeyword;
        }

        return PromptStyles.SyntaxValue;
    }

    private static bool IsKeyword(string? text) {
        return !string.IsNullOrWhiteSpace(text)
            && (SyntaxFacts.GetKeywordKind(text) != SyntaxKind.None
                || SyntaxFacts.GetContextualKeywordKind(text) != SyntaxKind.None);
    }

    private static string ResolveTagStyle(
        IEnumerable<string> tags,
        IEnumerable<(string StyleId, string[] Tags)> styleGroups,
        StringComparer comparer) {
        foreach (var (styleId, candidates) in styleGroups) {
            if (tags.Any(tag => candidates.Contains(tag, comparer))) {
                return styleId;
            }
        }

        return string.Empty;
    }

}
