using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atelier.Session.Roslyn
{
    internal sealed class ConsolePreprocessor(string redirectTargetExpression) : IPreprocessorPass
    {
        private readonly string redirectTargetExpression = string.IsNullOrWhiteSpace(redirectTargetExpression)
            ? throw new ArgumentException(GetString("Console redirect target expression is required."), nameof(redirectTargetExpression))
            : redirectTargetExpression;
        private static readonly HashSet<string> RedirectedMethods =
            ["Write", "WriteLine", "Clear", "ResetColor", "Read", "ReadLine", "ReadKey"];
        private static readonly HashSet<string> RedirectedProperties = ["ForegroundColor", "BackgroundColor"];

        public CompilationUnitSyntax Rewrite(CompilationUnitSyntax root, SemanticModel semanticModel, out bool changed) {
            var rewriter = new Rewriter(semanticModel, redirectTargetExpression);
            var rewrittenRoot = (CompilationUnitSyntax)rewriter.Visit(root)!;
            changed = rewriter.Changed;
            return changed ? rewrittenRoot : root;
        }

        private sealed class Rewriter(SemanticModel semanticModel, string redirectTargetExpression) : PreprocessorRewriter(semanticModel)
        {
            public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node) {
                var rewrittenNode = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
                if (rewrittenNode.Expression is IdentifierNameSyntax identifier
                    && RedirectedMethods.Contains(identifier.Identifier.ValueText)
                    && !IsWithinNameOf(node)
                    && BindsToSystemConsoleMember(node.Expression)) {
                    MarkChanged();
                    return rewrittenNode.WithExpression(CreateRedirectMemberAccess(identifier.Identifier.ValueText));
                }

                return rewrittenNode;
            }

            public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node) {
                var rewrittenNode = (AssignmentExpressionSyntax)base.VisitAssignmentExpression(node)!;
                if (rewrittenNode.Left is IdentifierNameSyntax identifier
                    && RedirectedProperties.Contains(identifier.Identifier.ValueText)
                    && !IsWithinNameOf(node)
                    && BindsToSystemConsoleMember(node.Left)) {
                    MarkChanged();
                    return rewrittenNode.WithLeft(CreateRedirectMemberAccess(identifier.Identifier.ValueText).WithTriviaFrom(rewrittenNode.Left));
                }

                return rewrittenNode;
            }

            public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node) {
                var rewrittenNode = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
                var memberName = rewrittenNode.Name.Identifier.ValueText;
                if ((!RedirectedMethods.Contains(memberName) && !RedirectedProperties.Contains(memberName))
                    || IsWithinNameOf(node)
                    || !BindsToSystemConsoleMember(node)) {
                    return rewrittenNode;
                }

                MarkChanged();
                return rewrittenNode.WithExpression(SyntaxFactory.ParseExpression(redirectTargetExpression).WithTriviaFrom(rewrittenNode.Expression));
            }

            public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) {
                var rewrittenNode = (IdentifierNameSyntax)base.VisitIdentifierName(node)!;
                if (!RedirectedProperties.Contains(rewrittenNode.Identifier.ValueText)
                    || IsIdentifierRewrittenByParent(node)
                    || IsWithinNameOf(node)
                    || !BindsToSystemConsoleMember(node)) {
                    return rewrittenNode;
                }

                MarkChanged();
                return CreateRedirectMemberAccess(rewrittenNode.Identifier.ValueText).WithTriviaFrom(rewrittenNode);
            }

            private bool BindsToSystemConsoleMember(SyntaxNode node) {
                return AnyBoundSymbol(node, static symbol =>
                    symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Console");
            }

            private MemberAccessExpressionSyntax CreateRedirectMemberAccess(string memberName) {
                return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParseExpression(redirectTargetExpression),
                    SyntaxFactory.IdentifierName(memberName));
            }

            private static bool IsIdentifierRewrittenByParent(IdentifierNameSyntax node) {
                return node.Parent switch {
                    MemberAccessExpressionSyntax memberAccess when memberAccess.Name == node => true,
                    InvocationExpressionSyntax invocation when invocation.Expression == node => true,
                    AssignmentExpressionSyntax assignment when assignment.Left == node => true,
                    _ => false,
                };
            }
        }
    }
}
