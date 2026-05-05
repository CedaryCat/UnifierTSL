using Atelier.Session;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atelier.Presentation.Window.Formatting {
    internal static class TypedCharacterFormatter {
        public static DraftRewrite? TryFormat(
            DraftRewrite rewrite,
            char typedChar,
            int typedIndex,
            CSharpParseOptions parseOptions) {
            try {
                var draft = rewrite.Draft;
                if (typedIndex < 0 || typedIndex >= draft.SourceText.Length) {
                    return null;
                }

                var root = CSharpSyntaxTree.ParseText(draft.SourceText, parseOptions).GetRoot();
                var typedToken = root.FindToken(typedIndex, findInsideTrivia: true);
                if (!MatchesTypedCharacter(typedToken, typedChar, typedIndex)) {
                    return null;
                }

                var formatContainer = ResolveFormatContainer(typedToken, typedChar);
                if (formatContainer is null || !CanFormatTypedCharacter(formatContainer, typedChar)) {
                    return null;
                }

                var formatEdits = RoslynFormattingWorkspace.GetFormattedTextChanges(root, formatContainer.FullSpan);
                rewrite = rewrite.ApplyBatch(
                    formatEdits,
                    DraftMarkers.MapPositionThroughSourceEdits(draft.SourceCaretIndex, formatEdits));
                rewrite = NormalizeLineEndings(
                    rewrite.Draft.SourceText,
                    rewrite.Draft.SourceCaretIndex)
                    .ApplyTo(rewrite);
                return (typedChar == ';'
                    ? NormalizeEmptyControlStatementSpacing(
                        rewrite.Draft.SourceText,
                        rewrite.Draft.SourceCaretIndex,
                        parseOptions)
                    : TextEditPlan.NoChange(rewrite.Draft.SourceText, rewrite.Draft.SourceCaretIndex))
                    .ApplyTo(rewrite);
            }
            catch {
                return null;
            }
        }

        private static bool MatchesTypedCharacter(SyntaxToken token, char typedChar, int typedIndex) {
            if (token.RawKind == 0 || typedIndex < token.SpanStart || typedIndex >= token.Span.End) {
                return false;
            }

            return typedChar switch {
                ';' => token.IsKind(SyntaxKind.SemicolonToken),
                '{' => token.IsKind(SyntaxKind.OpenBraceToken),
                _ => false,
            };
        }

        private static SyntaxNode? ResolveFormatContainer(SyntaxToken token, char typedChar) {
            return token.Parent?
                .AncestorsAndSelf()
                .FirstOrDefault(node => IsFormatContainer(node, typedChar, token.SpanStart));
        }

        private static bool CanFormatTypedCharacter(SyntaxNode container, char typedChar) {
            if (typedChar == '{' || typedChar == ';' && IsEmptyConditionControlStatement(container)) {
                return true;
            }

            return !container.ContainsDiagnostics
                && !container.DescendantTokens(descendIntoTrivia: true).Any(static token => token.IsMissing);
        }

        private static bool IsEmptyConditionControlStatement(SyntaxNode container) {
            return container switch {
                IfStatementSyntax {
                    Condition.IsMissing: true,
                    Statement: EmptyStatementSyntax { SemicolonToken.IsMissing: false }
                } => true,
                WhileStatementSyntax {
                    Condition.IsMissing: true,
                    Statement: EmptyStatementSyntax { SemicolonToken.IsMissing: false }
                } => true,
                _ => false,
            };
        }

        private static bool IsFormatContainer(SyntaxNode node, char typedChar, int tokenStart) {
            if (node.SpanStart >= tokenStart) {
                return false;
            }

            return typedChar switch {
                ';' or '{' => node is StatementSyntax
                    or MemberDeclarationSyntax
                    or AccessorDeclarationSyntax
                    or UsingDirectiveSyntax
                    or GlobalStatementSyntax
                    or SwitchLabelSyntax
                    or ExpressionSyntax
                    or BaseNamespaceDeclarationSyntax,
                _ => false,
            };
        }

        private static TextEditPlan NormalizeLineEndings(string text, int caretIndex) {
            if (string.IsNullOrEmpty(text) || text.IndexOf('\r') < 0) {
                return TextEditPlan.NoChange(text, caretIndex);
            }

            return TextEditPlan.FromEdits(
                text,
                caretIndex,
                Enumerable.Range(0, text.Length)
                    .Where(index => text[index] == '\r')
                    .Select(static index => new SourceEdit(index, 1, string.Empty)));
        }

        private static TextEditPlan NormalizeEmptyControlStatementSpacing(
            string text,
            int caretIndex,
            CSharpParseOptions parseOptions) {
            var currentText = text ?? string.Empty;
            var root = CSharpSyntaxTree.ParseText(currentText, parseOptions).GetRoot();
            var edits = root.DescendantNodes()
                .Select(node => node switch {
                    IfStatementSyntax { Statement: EmptyStatementSyntax empty } ifStatement
                        => CreateEmptyControlStatementSpacingEdit(
                            currentText,
                            ifStatement.CloseParenToken,
                            empty.SemicolonToken),
                    WhileStatementSyntax { Statement: EmptyStatementSyntax empty } whileStatement
                        => CreateEmptyControlStatementSpacingEdit(
                            currentText,
                            whileStatement.CloseParenToken,
                            empty.SemicolonToken),
                    _ => null,
                })
                .OfType<TextSpan>()
                .Select(static span => new SourceEdit(span.Start, span.Length, string.Empty));
            return TextEditPlan.FromEdits(currentText, caretIndex, edits);
        }

        private static TextSpan? CreateEmptyControlStatementSpacingEdit(
            string text,
            SyntaxToken closeParen,
            SyntaxToken semicolon) {
            if (closeParen.IsMissing || semicolon.IsMissing || semicolon.SpanStart <= closeParen.Span.End) {
                return null;
            }

            for (var index = closeParen.Span.End; index < semicolon.SpanStart; index++) {
                if (!char.IsWhiteSpace(text[index])) {
                    return null;
                }
            }

            return TextSpan.FromBounds(closeParen.Span.End, semicolon.SpanStart);
        }
    }
}
