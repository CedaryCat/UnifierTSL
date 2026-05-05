using Atelier.Session;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atelier.Presentation.Window.Formatting {
    internal readonly record struct SemicolonCompletionResult(DraftRewrite Rewrite, int FormatIndex, bool ShouldFormat);

    internal static class SemicolonCompleter {
        private readonly record struct Target(int InsertIndex, int CaretIndex, bool HasSemicolon);

        public static bool TryComplete(
            DraftRewrite rewrite,
            int typedIndex,
            CSharpParseOptions parseOptions,
            out SemicolonCompletionResult completion) {
            completion = default;
            var draft = rewrite.Draft;
            if (typedIndex < 0 || typedIndex >= draft.SourceText.Length || draft.SourceText[typedIndex] != ';') {
                return false;
            }

            rewrite = rewrite.Apply(new SourceEdit(typedIndex, 1, string.Empty));
            var baseText = rewrite.Draft.SourceText;
            var baseLedger = rewrite.Draft.PairLedger;
            if (!TryResolveTarget(baseText, typedIndex, parseOptions, out var target)) {
                return false;
            }

            if (!target.HasSemicolon) {
                var insertIndex = ExtendAcrossTrailingClosers(baseText, typedIndex, target.InsertIndex);
                target = target with {
                    InsertIndex = insertIndex,
                    CaretIndex = insertIndex,
                };
            }

            if (target.InsertIndex < typedIndex) {
                return false;
            }

            var nextCaret = target.CaretIndex;
            var crossedPairIds = ResolveCrossedVirtualPairIds(baseLedger, typedIndex, target.InsertIndex);
            if (!target.HasSemicolon) {
                rewrite = rewrite.Apply(new SourceEdit(target.InsertIndex, 0, ";"));
                nextCaret = target.InsertIndex + 1;
            }

            completion = new SemicolonCompletionResult(
                rewrite.RemovePairs(crossedPairIds, nextCaret),
                target.InsertIndex,
                !ContainsLineBreak(baseText, typedIndex, target.InsertIndex));
            return true;
        }

        private static bool TryResolveTarget(
            string baseText,
            int caretIndex,
            CSharpParseOptions parseOptions,
            out Target target) {
            target = default;
            var text = baseText ?? string.Empty;
            var root = CSharpSyntaxTree.ParseText(text, parseOptions).GetRoot();
            var token = FindTokenForCaret(root, text, caretIndex);
            if (token.RawKind == 0 || IsCaretInsideLiteralToken(token, caretIndex)) {
                return false;
            }

            var ancestors = token.Parent?.AncestorsAndSelf() ?? [];
            if (IsCaretInControlHeader(ancestors, caretIndex)) {
                return false;
            }

            var statement = ancestors
                .OfType<StatementSyntax>()
                .FirstOrDefault(IsCompletionStatement)
                ?? ancestors
                    .OfType<GlobalStatementSyntax>()
                    .Select(static global => global.Statement)
                    .FirstOrDefault(IsCompletionStatement)
                ?? root.DescendantNodes()
                    .OfType<GlobalStatementSyntax>()
                    .Select(static global => global.Statement)
                    .FirstOrDefault(statement => IsCompletionStatement(statement)
                        && statement.FullSpan.Start <= caretIndex
                        && caretIndex <= statement.FullSpan.End);
            if (statement is null) {
                return TryResolveLexicalTarget(text, caretIndex, out target);
            }

            return TryCreateTarget(statement, text.Length, out target);
        }

        private static bool TryResolveLexicalTarget(string text, int caretIndex, out Target target) {
            target = default;
            if (!CanUseLexicalFallback(text, caretIndex)) {
                return false;
            }

            var insertIndex = ExtendAcrossTrailingClosers(text, caretIndex, caretIndex);
            if (insertIndex <= caretIndex) {
                return false;
            }

            target = new Target(insertIndex, insertIndex, false);
            return true;
        }

        private static bool CanUseLexicalFallback(string text, int caretIndex) {
            for (var index = Math.Clamp(caretIndex, 0, text.Length) - 1; index >= 0; index--) {
                if (char.IsWhiteSpace(text[index])) {
                    continue;
                }

                return text[index] is not ('{' or ';');
            }

            return false;
        }

        private static bool IsCaretInControlHeader(IEnumerable<SyntaxNode> ancestors, int caretIndex) {
            return ancestors.Any(node => node switch {
                ForStatementSyntax statement
                    => IsCaretInHeader(caretIndex, statement.OpenParenToken, statement.CloseParenToken),
                IfStatementSyntax statement
                    => IsCaretInHeader(caretIndex, statement.OpenParenToken, statement.CloseParenToken),
                WhileStatementSyntax statement
                    => IsCaretInHeader(caretIndex, statement.OpenParenToken, statement.CloseParenToken),
                SwitchStatementSyntax statement
                    => IsCaretInHeader(caretIndex, statement.OpenParenToken, statement.CloseParenToken),
                UsingStatementSyntax statement
                    => IsCaretInHeader(caretIndex, statement.OpenParenToken, statement.CloseParenToken),
                LockStatementSyntax statement
                    => IsCaretInHeader(caretIndex, statement.OpenParenToken, statement.CloseParenToken),
                FixedStatementSyntax statement
                    => IsCaretInHeader(caretIndex, statement.OpenParenToken, statement.CloseParenToken),
                _ => false,
            });
        }

        private static bool IsCaretInHeader(int caretIndex, SyntaxToken openParen, SyntaxToken closeParen) {
            return !openParen.IsMissing
                && openParen.SpanStart < caretIndex
                && (closeParen.IsMissing || caretIndex <= closeParen.SpanStart);
        }

        private static SyntaxToken FindTokenForCaret(SyntaxNode root, string text, int caretIndex) {
            if (text.Length == 0) {
                return default;
            }

            var boundedCaret = Math.Clamp(caretIndex, 0, text.Length);
            var token = root.FindToken(Math.Clamp(boundedCaret, 0, text.Length - 1), findInsideTrivia: true);
            return token.RawKind != 0 && token.SpanStart <= boundedCaret && boundedCaret <= token.Span.End
                ? token
                : root.FindToken(Math.Clamp(boundedCaret - 1, 0, text.Length - 1), findInsideTrivia: true);
        }

        private static bool IsCaretInsideLiteralToken(SyntaxToken token, int caretIndex) {
            return caretIndex > token.SpanStart
                && caretIndex < token.Span.End
                && (token.IsKind(SyntaxKind.StringLiteralToken)
                    || token.IsKind(SyntaxKind.CharacterLiteralToken));
        }

        private static bool IsCompletionStatement(StatementSyntax statement) {
            return statement is ExpressionStatementSyntax
                or LocalDeclarationStatementSyntax
                or ReturnStatementSyntax
                or ThrowStatementSyntax
                or YieldStatementSyntax
                or BreakStatementSyntax
                or ContinueStatementSyntax;
        }

        private static bool TryCreateTarget(StatementSyntax statement, int textLength, out Target target) {
            (SyntaxToken SemicolonToken, int FallbackInsertIndex)? parts = statement switch {
                ExpressionStatementSyntax expressionStatement
                    => (expressionStatement.SemicolonToken, expressionStatement.Expression.Span.End),
                LocalDeclarationStatementSyntax localDeclaration
                    => (localDeclaration.SemicolonToken, localDeclaration.Declaration.Span.End),
                ReturnStatementSyntax returnStatement
                    => (returnStatement.SemicolonToken,
                        returnStatement.Expression?.Span.End ?? returnStatement.ReturnKeyword.Span.End),
                ThrowStatementSyntax throwStatement
                    => (throwStatement.SemicolonToken,
                        throwStatement.Expression?.Span.End ?? throwStatement.ThrowKeyword.Span.End),
                YieldStatementSyntax yieldStatement
                    => (yieldStatement.SemicolonToken,
                        yieldStatement.Expression?.Span.End ?? yieldStatement.ReturnOrBreakKeyword.Span.End),
                BreakStatementSyntax breakStatement
                    => (breakStatement.SemicolonToken, breakStatement.BreakKeyword.Span.End),
                ContinueStatementSyntax continueStatement
                    => (continueStatement.SemicolonToken, continueStatement.ContinueKeyword.Span.End),
                _ => null,
            };
            if (parts is not { } resolved) {
                target = default;
                return false;
            }

            return TryCreateTarget(
                resolved.SemicolonToken,
                resolved.FallbackInsertIndex,
                textLength,
                out target);
        }

        private static bool TryCreateTarget(
            SyntaxToken semicolonToken,
            int fallbackInsertIndex,
            int textLength,
            out Target target) {
            if (!semicolonToken.IsMissing && semicolonToken.Span.Length > 0) {
                target = new Target(semicolonToken.SpanStart, semicolonToken.Span.End, true);
                return true;
            }

            var insertIndex = Math.Clamp(fallbackInsertIndex, 0, Math.Max(0, textLength));
            target = new Target(insertIndex, insertIndex, false);
            return true;
        }

        private static int ExtendAcrossTrailingClosers(string text, int caretIndex, int targetIndex) {
            var index = Math.Clamp(Math.Max(caretIndex, targetIndex), 0, text.Length);
            while (index < text.Length) {
                while (index < text.Length && char.IsWhiteSpace(text[index])) {
                    if (text[index] is '\r' or '\n') {
                        return index;
                    }

                    index++;
                }

                if (index < text.Length && text[index] is ')' or ']' or '>') {
                    index++;
                    continue;
                }

                break;
            }

            return index;
        }

        private static bool ContainsLineBreak(string text, int startIndex, int endIndex) {
            var start = Math.Clamp(Math.Min(startIndex, endIndex), 0, text.Length);
            var end = Math.Clamp(Math.Max(startIndex, endIndex), start, text.Length);
            return text.AsSpan(start, end - start).IndexOfAny('\r', '\n') >= 0;
        }

        private static IEnumerable<long> ResolveCrossedVirtualPairIds(
            VirtualPairLedger ledger,
            int sourceStartIndex,
            int sourceEndIndex) {
            var start = Math.Min(sourceStartIndex, sourceEndIndex);
            var end = Math.Max(sourceStartIndex, sourceEndIndex);
            return ledger.Entries
                .Where(entry => entry.CloserIndex >= start && entry.CloserEndIndex <= end)
                .Select(entry => entry.PairId)
                .ToArray();
        }
    }
}
