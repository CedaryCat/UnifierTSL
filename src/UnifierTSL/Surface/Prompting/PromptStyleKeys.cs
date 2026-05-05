using UnifierTSL.Contracts.Display;
using UnifierTSL.Surface.Status;

namespace UnifierTSL.Surface.Prompting;

public static class PromptStyleKeys
{
    public const string SyntaxKeyword = "surface.prompt.syntax.keyword";
    public const string SyntaxModifier = "surface.prompt.syntax.modifier";
    public const string SyntaxValue = "surface.prompt.syntax.value";
    public const string SyntaxError = "surface.prompt.syntax.error";
    public const string GhostKeyword = "surface.prompt.ghost.keyword";
    public const string GhostModifier = "surface.prompt.ghost.modifier";
    public const string GhostValue = "surface.prompt.ghost.value";
    public const string GhostError = "surface.prompt.ghost.error";
    public const string CompletionSummaryText = "surface.prompt.completion.summary.text";
    public const string CompletionSummaryKeyword = "surface.prompt.completion.summary.keyword";
    public const string CompletionSummaryModifier = "surface.prompt.completion.summary.modifier";
    public const string CompletionSummaryValue = "surface.prompt.completion.summary.value";
    public const string CompletionSummaryError = "surface.prompt.completion.summary.error";

    public static StyleDictionary Default => new() {
        Styles = [
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.PromptLabel,
                Foreground = Rgb(0, 255, 0),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.PromptInput,
                Foreground = Rgb(255, 255, 255),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.PromptCompletionBadge,
                Foreground = Rgb(128, 128, 128),
            },
            StatusProjectionDocumentFactory.CreateStatusBandTextStyle(),
            StatusProjectionDocumentFactory.CreateStatusTitleTextStyle(),
            StatusProjectionDocumentFactory.CreateStatusSummaryTextStyle(),
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.StatusDetail,
                Foreground = Rgb(128, 128, 128),
            },
            new StyledTextStyle {
                StyleId = SurfaceStyleCatalog.InlineHint,
                Foreground = Rgb(128, 128, 128),
            },
            new StyledTextStyle {
                StyleId = SyntaxKeyword,
                Foreground = Rgb(255, 255, 0),
            },
            new StyledTextStyle {
                StyleId = SyntaxModifier,
                Foreground = Rgb(0, 255, 255),
            },
            new StyledTextStyle {
                StyleId = SyntaxValue,
                Foreground = Rgb(255, 255, 255),
            },
            new StyledTextStyle {
                StyleId = SyntaxError,
                Foreground = Rgb(255, 0, 0),
            },
            new StyledTextStyle {
                StyleId = GhostKeyword,
                Foreground = Rgb(128, 128, 128),
            },
            new StyledTextStyle {
                StyleId = GhostModifier,
                Foreground = Rgb(128, 128, 128),
            },
            new StyledTextStyle {
                StyleId = GhostValue,
                Foreground = Rgb(128, 128, 128),
            },
            new StyledTextStyle {
                StyleId = GhostError,
                Foreground = Rgb(128, 128, 128),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryText,
                Foreground = Rgb(192, 192, 192),
                Background = Rgb(40, 40, 40),
            },
            new StyledTextStyle {
                StyleId = CompletionSummaryKeyword,
                Foreground = Rgb(86, 156, 214),
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
                StyleId = CompletionSummaryError,
                Foreground = Rgb(244, 71, 71),
                Background = Rgb(40, 40, 40),
            },
        ],
    };

    private static StyledColorValue Rgb(byte red, byte green, byte blue) {
        return new StyledColorValue {
            Red = red,
            Green = green,
            Blue = blue,
        };
    }
}
