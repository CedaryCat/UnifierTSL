using UnifierTSL.Contracts.Display;

namespace Atelier.Presentation.Prompt;

internal static class PromptStyles
{
    public const string SyntaxKeyword = "atelier.prompt.syntax.keyword";
    public const string SyntaxControlKeyword = "atelier.prompt.syntax.control-keyword";
    public const string SyntaxModifier = "atelier.prompt.syntax.modifier";
    public const string SyntaxValue = "atelier.prompt.syntax.value";
    public const string SyntaxNumber = "atelier.prompt.syntax.number";
    public const string SyntaxString = "atelier.prompt.syntax.string";
    public const string SyntaxType = "atelier.prompt.syntax.type";
    public const string SyntaxStruct = "atelier.prompt.syntax.struct";
    public const string SyntaxInterface = "atelier.prompt.syntax.interface";
    public const string SyntaxEnum = "atelier.prompt.syntax.enum";
    public const string SyntaxTypeParameter = "atelier.prompt.syntax.type-parameter";
    public const string SyntaxMethod = "atelier.prompt.syntax.method";
    public const string SyntaxMember = "atelier.prompt.syntax.member";
    public const string SyntaxOperator = "atelier.prompt.syntax.operator";
    public const string SyntaxComment = "atelier.prompt.syntax.comment";
    public const string SyntaxError = "atelier.prompt.syntax.error";
    public const string GhostKeyword = "atelier.prompt.ghost.keyword";
    public const string GhostControlKeyword = "atelier.prompt.ghost.control-keyword";
    public const string GhostModifier = "atelier.prompt.ghost.modifier";
    public const string GhostValue = "atelier.prompt.ghost.value";
    public const string GhostNumber = "atelier.prompt.ghost.number";
    public const string GhostString = "atelier.prompt.ghost.string";
    public const string GhostType = "atelier.prompt.ghost.type";
    public const string GhostStruct = "atelier.prompt.ghost.struct";
    public const string GhostInterface = "atelier.prompt.ghost.interface";
    public const string GhostEnum = "atelier.prompt.ghost.enum";
    public const string GhostTypeParameter = "atelier.prompt.ghost.type-parameter";
    public const string GhostMethod = "atelier.prompt.ghost.method";
    public const string GhostMember = "atelier.prompt.ghost.member";
    public const string GhostOperator = "atelier.prompt.ghost.operator";
    public const string GhostComment = "atelier.prompt.ghost.comment";
    public const string GhostError = "atelier.prompt.ghost.error";
    public const string PairDelimiter = "atelier.prompt.pair.delimiter";
    public const string PairDelimiterActive = "atelier.prompt.pair.delimiter.active";
    public const string PairStringDelimiter = "atelier.prompt.pair.string-delimiter";
    public const string PairStringDelimiterActive = "atelier.prompt.pair.string-delimiter.active";
    public const string CompletionSummaryText = "atelier.prompt.completion.summary.text";
    public const string CompletionSummaryKeyword = "atelier.prompt.completion.summary.keyword";
    public const string CompletionSummaryControlKeyword = "atelier.prompt.completion.summary.control-keyword";
    public const string CompletionSummaryModifier = "atelier.prompt.completion.summary.modifier";
    public const string CompletionSummaryValue = "atelier.prompt.completion.summary.value";
    public const string CompletionSummaryNumber = "atelier.prompt.completion.summary.number";
    public const string CompletionSummaryString = "atelier.prompt.completion.summary.string";
    public const string CompletionSummaryType = "atelier.prompt.completion.summary.type";
    public const string CompletionSummaryStruct = "atelier.prompt.completion.summary.struct";
    public const string CompletionSummaryInterface = "atelier.prompt.completion.summary.interface";
    public const string CompletionSummaryEnum = "atelier.prompt.completion.summary.enum";
    public const string CompletionSummaryTypeParameter = "atelier.prompt.completion.summary.type-parameter";
    public const string CompletionSummaryMethod = "atelier.prompt.completion.summary.method";
    public const string CompletionSummaryMember = "atelier.prompt.completion.summary.member";
    public const string CompletionSummaryOperator = "atelier.prompt.completion.summary.operator";
    public const string CompletionSummaryComment = "atelier.prompt.completion.summary.comment";
    public const string CompletionSummaryError = "atelier.prompt.completion.summary.error";

    public static StyleDictionary Default { get; } = new() {
        Styles = [
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.PromptLabel,
                Foreground = Rgb(255, 255, 255),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.PromptInput,
                Foreground = Rgb(255, 255, 255),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.PromptCompletionBadge,
                Foreground = Rgb(150, 150, 150),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.StatusBand,
                Foreground = Rgb(220, 220, 220),
                Background = Rgb(56, 56, 64),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.StatusTitle,
                Foreground = Rgb(220, 220, 220),
                Background = Rgb(56, 56, 64),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.StatusSummary,
                Foreground = Rgb(220, 220, 220),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.StatusDetail,
                Foreground = Rgb(150, 150, 150),
            },
            new StyledTextStyle {
                StyleId = SyntaxKeyword,
                Foreground = Rgb(86, 156, 214),
            },
            new StyledTextStyle {
                StyleId = SyntaxControlKeyword,
                Foreground = Rgb(216, 160, 223),
            },
            new StyledTextStyle {
                StyleId = SyntaxModifier,
                Foreground = Rgb(220, 220, 170),
            },
            new StyledTextStyle {
                StyleId = SyntaxValue,
                Foreground = Rgb(156, 220, 254),
            },
            new StyledTextStyle {
                StyleId = SyntaxNumber,
                Foreground = Rgb(181, 206, 168),
            },
            new StyledTextStyle {
                StyleId = SyntaxString,
                Foreground = Rgb(214, 157, 133),
            },
            new StyledTextStyle {
                StyleId = SyntaxType,
                Foreground = Rgb(78, 201, 176),
            },
            new StyledTextStyle {
                StyleId = SyntaxStruct,
                Foreground = Rgb(134, 198, 145),
            },
            new StyledTextStyle {
                StyleId = SyntaxInterface,
                Foreground = Rgb(184, 215, 163),
            },
            new StyledTextStyle {
                StyleId = SyntaxEnum,
                Foreground = Rgb(184, 215, 163),
            },
            new StyledTextStyle {
                StyleId = SyntaxTypeParameter,
                Foreground = Rgb(184, 215, 163),
            },
            new StyledTextStyle {
                StyleId = SyntaxMethod,
                Foreground = Rgb(220, 220, 170),
            },
            new StyledTextStyle {
                StyleId = SyntaxMember,
                Foreground = Rgb(220, 220, 220),
            },
            new StyledTextStyle {
                StyleId = SyntaxOperator,
                Foreground = Rgb(180, 180, 180),
            },
            new StyledTextStyle {
                StyleId = SyntaxComment,
                Foreground = Rgb(87, 166, 74),
            },
            new StyledTextStyle {
                StyleId = SyntaxError,
                Foreground = Rgb(244, 71, 71),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.InlineHint,
                Foreground = Rgb(150, 150, 150),
            },
            new StyledTextStyle {
                StyleId = GhostKeyword,
                Foreground = Rgb(150, 150, 150),
            },
            new StyledTextStyle {
                StyleId = GhostControlKeyword,
                Foreground = Rgb(150, 150, 150),
            },
            new StyledTextStyle {
                StyleId = GhostModifier,
                Foreground = Rgb(150, 150, 150),
            },
            new StyledTextStyle {
                StyleId = GhostValue,
                Foreground = Rgb(150, 150, 150),
            },
            new StyledTextStyle {
                StyleId = GhostNumber,
                Foreground = Rgb(150, 150, 150),
            },
            new StyledTextStyle {
                StyleId = GhostString,
                Foreground = Rgb(150, 150, 150),
            },
            new StyledTextStyle {
                StyleId = GhostType,
                Foreground = Rgb(150, 150, 150),
            },
            new StyledTextStyle {
                StyleId = GhostStruct,
                Foreground = Rgb(135, 155, 140),
            },
            new StyledTextStyle {
                StyleId = GhostInterface,
                Foreground = Rgb(155, 165, 140),
            },
            new StyledTextStyle {
                StyleId = GhostEnum,
                Foreground = Rgb(155, 165, 140),
            },
            new StyledTextStyle {
                StyleId = GhostTypeParameter,
                Foreground = Rgb(155, 165, 140),
            },
            new StyledTextStyle {
                StyleId = GhostMethod,
                Foreground = Rgb(150, 150, 150),
            },
            new StyledTextStyle {
                StyleId = GhostMember,
                Foreground = Rgb(150, 150, 150),
            },
            new StyledTextStyle {
                StyleId = GhostOperator,
                Foreground = Rgb(150, 150, 150),
            },
            new StyledTextStyle {
                StyleId = GhostComment,
                Foreground = Rgb(120, 120, 120),
            },
            new StyledTextStyle {
                StyleId = GhostError,
                Foreground = Rgb(180, 120, 120),
            },
            new StyledTextStyle {
                StyleId = PairDelimiter,
                Foreground = Rgb(220, 220, 220),
            },
            new StyledTextStyle {
                StyleId = PairDelimiterActive,
                Foreground = Rgb(220, 220, 220),
                TextAttributes = StyledTextAttributes.Underline,
            },
            new StyledTextStyle {
                StyleId = PairStringDelimiter,
                Foreground = Rgb(214, 157, 133),
            },
            new StyledTextStyle {
                StyleId = PairStringDelimiterActive,
                Foreground = Rgb(214, 157, 133),
                TextAttributes = StyledTextAttributes.Underline,
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryText,
                Foreground = Rgb(212, 212, 212),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryKeyword,
                Foreground = Rgb(86, 156, 214),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryControlKeyword,
                Foreground = Rgb(216, 160, 223),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryModifier,
                Foreground = Rgb(78, 201, 176),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryValue,
                Foreground = Rgb(156, 220, 254),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryNumber,
                Foreground = Rgb(181, 206, 168),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryString,
                Foreground = Rgb(214, 157, 133),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryType,
                Foreground = Rgb(78, 201, 176),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryStruct,
                Foreground = Rgb(134, 198, 145),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryInterface,
                Foreground = Rgb(184, 215, 163),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryEnum,
                Foreground = Rgb(184, 215, 163),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryTypeParameter,
                Foreground = Rgb(184, 215, 163),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryMethod,
                Foreground = Rgb(220, 220, 170),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryMember,
                Foreground = Rgb(220, 220, 220),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryOperator,
                Foreground = Rgb(180, 180, 180),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryComment,
                Foreground = Rgb(87, 166, 74),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryError,
                Foreground = Rgb(244, 71, 71),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.CompletionPopupBorder,
                Foreground = Rgb(144, 144, 144),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.CompletionPopupText,
                Foreground = Rgb(220, 220, 220),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.CompletionPopupDetail,
                Foreground = Rgb(220, 220, 220),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.CompletionItemSelected,
                Foreground = Rgb(220, 220, 220),
                Background = Rgb(60, 60, 60),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.CompletionItemSelectedMarker,
                Foreground = Rgb(86, 156, 214),
            },
        ],
    };

    public static string ResolveGhost(string syntaxStyleId) {
        return syntaxStyleId switch {
            SyntaxKeyword => GhostKeyword,
            SyntaxControlKeyword => GhostControlKeyword,
            SyntaxModifier => GhostModifier,
            SyntaxNumber => GhostNumber,
            SyntaxString => GhostString,
            SyntaxType => GhostType,
            SyntaxStruct => GhostStruct,
            SyntaxInterface => GhostInterface,
            SyntaxEnum => GhostEnum,
            SyntaxTypeParameter => GhostTypeParameter,
            SyntaxMethod => GhostMethod,
            SyntaxMember => GhostMember,
            SyntaxOperator => GhostOperator,
            SyntaxComment => GhostComment,
            SyntaxError => GhostError,
            _ => GhostValue,
        };
    }

    public static string ResolveSummary(string syntaxStyleId) {
        return syntaxStyleId switch {
            SyntaxKeyword => CompletionSummaryKeyword,
            SyntaxControlKeyword => CompletionSummaryControlKeyword,
            SyntaxModifier => CompletionSummaryModifier,
            SyntaxNumber => CompletionSummaryNumber,
            SyntaxString => CompletionSummaryString,
            SyntaxType => CompletionSummaryType,
            SyntaxStruct => CompletionSummaryStruct,
            SyntaxInterface => CompletionSummaryInterface,
            SyntaxEnum => CompletionSummaryEnum,
            SyntaxTypeParameter => CompletionSummaryTypeParameter,
            SyntaxMethod => CompletionSummaryMethod,
            SyntaxMember => CompletionSummaryMember,
            SyntaxOperator => CompletionSummaryOperator,
            SyntaxComment => CompletionSummaryComment,
            SyntaxError => CompletionSummaryError,
            SyntaxValue => CompletionSummaryValue,
            _ => CompletionSummaryText,
        };
    }

    private static StyledColorValue Rgb(byte red, byte green, byte blue) {
        return new StyledColorValue {
            Red = red,
            Green = green,
            Blue = blue,
        };
    }
}
