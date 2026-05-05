using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atelier.Session.Roslyn {
    internal static class SignatureHelpLocation {
        public static IEnumerable<SyntaxNode> EnumerateActiveArgumentLists(string text, SyntaxNode root, int position) {
            var node = FindNonWhitespaceNode(text, root, position);
            var argList = FindArgumentList(node);
            while (argList is not null) {
                if (IsActiveArgumentList(argList, node, position)) {
                    yield return argList;
                }

                argList = FindArgumentList(argList.Parent);
            }
        }

        public static bool IsActiveLocation(string text, SyntaxNode root, int position) {
            return EnumerateActiveArgumentLists(text, root, position).Any()
                || TryResolveOpenGenericLocation(root, text, position, out _, out _);
        }

        public static bool TryResolveOpenGenericLocation(
            SyntaxNode root,
            string text,
            int position,
            out BinaryExpressionSyntax binaryExpression,
            out IdentifierNameSyntax identifierName) {
            binaryExpression = null!;
            identifierName = null!;

            var lessThanIndex = FindOpenGenericStart(text, position);
            if (lessThanIndex < 0) {
                return false;
            }

            if (root.FindToken(lessThanIndex).Parent is not BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LessThanExpression } candidate
                || candidate.Left.DescendantNodesAndSelf().LastOrDefault() is not IdentifierNameSyntax identifier
                || position <= candidate.OperatorToken.SpanStart) {
                return false;
            }

            binaryExpression = candidate;
            identifierName = identifier;
            return true;
        }

        public static int GetOpenGenericParameterIndex(BinaryExpressionSyntax binaryExpression, string text, int position) {
            var activeParameterIndex = 0;
            for (var index = binaryExpression.OperatorToken.Span.End; index < Math.Min(position, text.Length); index++) {
                if (text[index] == ',') {
                    activeParameterIndex++;
                }
            }

            return activeParameterIndex;
        }

        public static int GetArgumentListParameterIndex(SyntaxNode argList, int position) {
            return argList switch {
                BaseArgumentListSyntax baseArgumentList => baseArgumentList.Arguments.GetSeparators().Count(separator => position > separator.SpanStart),
                TypeArgumentListSyntax typeArgumentList => typeArgumentList.Arguments.GetSeparators().Count(separator => position > separator.SpanStart),
                _ => 0,
            };
        }

        private static SyntaxNode? FindNonWhitespaceNode(string text, SyntaxNode root, int position) {
            var safePosition = Math.Clamp(position, 0, text.Length);
            var node = root.FindNode(new TextSpan(safePosition, 0), getInnermostNodeForTie: true);
            if (!node.IsKind(SyntaxKind.CompilationUnit)) {
                return node;
            }

            for (var index = safePosition - 1; index >= 0; index--) {
                if (!char.IsWhiteSpace(text[index])) {
                    return root.FindNode(new TextSpan(index, 0), getInnermostNodeForTie: true);
                }
            }

            return null;
        }

        private static SyntaxNode? FindArgumentList(SyntaxNode? node) {
            return node?.AncestorsAndSelf().FirstOrDefault(static node =>
                node is ArgumentListSyntax or BracketedArgumentListSyntax or TypeArgumentListSyntax);
        }

        private static bool IsActiveArgumentList(SyntaxNode argList, SyntaxNode? node, int position) {
            if (position <= argList.SpanStart) {
                return false;
            }

            var closeToken = GetListCloseToken(argList);
            return (closeToken.Span.Length == 0 || position < argList.Span.End)
                && !IsSuppressedByNestedImplementationContext(node, argList, position);
        }

        private static bool IsSuppressedByNestedImplementationContext(SyntaxNode? node, SyntaxNode argList, int position) {
            for (var current = node; current is not null && current != argList; current = current.Parent) {
                if (current switch {
                    LambdaExpressionSyntax lambda => IsInLambdaBody(lambda, position),
                    AnonymousMethodExpressionSyntax anonymousMethod => IsInDelimitedBody(anonymousMethod.Block.OpenBraceToken, anonymousMethod.Block.CloseBraceToken, position),
                    InitializerExpressionSyntax initializer => IsInDelimitedBody(initializer.OpenBraceToken, initializer.CloseBraceToken, position),
                    AnonymousObjectCreationExpressionSyntax anonymousObject => IsInDelimitedBody(anonymousObject.OpenBraceToken, anonymousObject.CloseBraceToken, position),
                    _ => false,
                }) {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInLambdaBody(LambdaExpressionSyntax lambda, int position) {
            return lambda switch {
                ParenthesizedLambdaExpressionSyntax parenthesized => IsInAnonymousFunctionBody(parenthesized.Block, parenthesized.ExpressionBody, position),
                SimpleLambdaExpressionSyntax simple => IsInAnonymousFunctionBody(simple.Block, simple.ExpressionBody, position),
                _ => false,
            };
        }

        private static bool IsInAnonymousFunctionBody(BlockSyntax? block, ExpressionSyntax? expressionBody, int position) {
            if (block is not null) {
                return IsInDelimitedBody(block.OpenBraceToken, block.CloseBraceToken, position);
            }

            return expressionBody?.Span.Contains(position) == true;
        }

        private static bool IsInDelimitedBody(SyntaxToken openToken, SyntaxToken closeToken, int position) {
            return !openToken.IsMissing
                && position > openToken.SpanStart
                && (closeToken.IsMissing || position <= closeToken.SpanStart);
        }

        private static SyntaxToken GetListCloseToken(SyntaxNode argList) {
            return argList switch {
                ArgumentListSyntax argumentList => argumentList.CloseParenToken,
                BracketedArgumentListSyntax bracketedArgumentList => bracketedArgumentList.CloseBracketToken,
                TypeArgumentListSyntax typeArgumentList => typeArgumentList.GreaterThanToken,
                _ => default,
            };
        }

        private static int FindOpenGenericStart(string text, int position) {
            var depth = 0;
            for (var index = Math.Min(position, text.Length) - 1; index >= 0; index--) {
                switch (text[index]) {
                    case '>':
                        depth++;
                        break;
                    case '<':
                        if (depth == 0) {
                            return index;
                        }

                        depth--;
                        break;
                }
            }

            return -1;
        }
    }
}
