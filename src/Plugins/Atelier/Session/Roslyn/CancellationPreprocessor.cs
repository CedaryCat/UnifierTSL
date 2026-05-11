using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atelier.Session.Roslyn
{
    internal sealed class CancellationPreprocessor : IPreprocessorPass
    {
        private const string GuardTypeName = "global::Atelier.Session.Context.CancellationGuard";
        private const string TokenLocalPrefix = "__atelierCancellation_";

        public CompilationUnitSyntax Rewrite(CompilationUnitSyntax root, SemanticModel semanticModel, out bool changed) {
            var tokenLocalName = TokenLocalPrefix + Guid.NewGuid().ToString("N");
            var rewriter = new Rewriter(semanticModel, tokenLocalName);
            var rewrittenRoot = (CompilationUnitSyntax)rewriter.Visit(root)!;
            if (!rewriter.Changed) {
                changed = false;
                return root;
            }

            if (rewriter.RequiresSessionTokenLocal) {
                rewrittenRoot = InsertSessionTokenLocal(rewrittenRoot, tokenLocalName);
            }

            changed = true;
            return rewrittenRoot;
        }

        private static CompilationUnitSyntax InsertSessionTokenLocal(CompilationUnitSyntax root, string tokenLocalName) {
            var tokenDeclaration = SyntaxFactory.GlobalStatement(SyntaxFactory.ParseStatement($"var {tokenLocalName} = Cancellation;"));
            return root.WithMembers(root.Members.Insert(0, tokenDeclaration));
        }

        private sealed class Rewriter(SemanticModel semanticModel, string tokenLocalName) : PreprocessorRewriter(semanticModel)
        {
            private readonly string tokenLocalName = string.IsNullOrWhiteSpace(tokenLocalName)
                ? throw new ArgumentException(GetString("Token local name is required."), nameof(tokenLocalName))
                : tokenLocalName;

            public bool RequiresSessionTokenLocal { get; private set; }

            public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node) {
                var rewrittenNode = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
                rewrittenNode = TryWrapEscapedDelegate(node, rewrittenNode);
                return TryAppendCancellationToken(node, rewrittenNode);
            }

            public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) {
                var rewrittenNode = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!;
                if (!CanUseSessionToken(node)
                    || !TryGetBoundSymbol<IMethodSymbol>(node, out var constructor)
                    || node.ArgumentList is null
                    || rewrittenNode.ArgumentList is null) {
                    return rewrittenNode;
                }

                var containingTypeName = GetFullTypeName(constructor.ContainingType);
                return containingTypeName is "global::System.Threading.Thread" or "global::System.Threading.Timer"
                    ? TryWrapArgument(node.ArgumentList.Arguments, rewrittenNode.ArgumentList.Arguments, 0, "WrapDetached", constructor, rewrittenNode)
                    : rewrittenNode;
            }

            public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node) {
                var rewrittenNode = (AssignmentExpressionSyntax)base.VisitAssignmentExpression(node)!;
                if (node.IsKind(SyntaxKind.AddAssignmentExpression)
                    && CanUseSessionToken(node)
                    && node.Right is AnonymousFunctionExpressionSyntax
                    && TryGetBoundSymbol<IEventSymbol>(node.Left, out var eventSymbol)
                    && GetFullTypeName(eventSymbol.ContainingType) == "global::System.Timers.Timer"
                    && eventSymbol.Name == "Elapsed") {
                    MarkChanged();
                    RequiresSessionTokenLocal = true;
                    return rewrittenNode.WithRight(CreateGuardedDelegate(rewrittenNode.Right, eventSymbol.Type, "WrapDetached"));
                }

                return rewrittenNode;
            }

            public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node) {
                var rewrittenNode = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
                return rewrittenNode.WithStatement(InjectCancellationCheckIfNeeded(node, rewrittenNode.Statement));
            }

            public override SyntaxNode? VisitForStatement(ForStatementSyntax node) {
                var rewrittenNode = (ForStatementSyntax)base.VisitForStatement(node)!;
                return rewrittenNode.WithStatement(InjectCancellationCheckIfNeeded(node, rewrittenNode.Statement));
            }

            public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node) {
                var rewrittenNode = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
                return rewrittenNode.WithStatement(InjectCancellationCheckIfNeeded(node, rewrittenNode.Statement));
            }

            public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node) {
                var rewrittenNode = (ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(node)!;
                return rewrittenNode.WithStatement(InjectCancellationCheckIfNeeded(node, rewrittenNode.Statement));
            }

            public override SyntaxNode? VisitDoStatement(DoStatementSyntax node) {
                var rewrittenNode = (DoStatementSyntax)base.VisitDoStatement(node)!;
                return rewrittenNode.WithStatement(InjectCancellationCheckIfNeeded(node, rewrittenNode.Statement));
            }

            private InvocationExpressionSyntax TryWrapEscapedDelegate(InvocationExpressionSyntax node, InvocationExpressionSyntax rewrittenNode) {
                if (!CanUseSessionToken(node)
                    || !TryGetBoundSymbol<IMethodSymbol>(node, out var method)) {
                    return rewrittenNode;
                }

                var containingTypeName = GetFullTypeName(method.ContainingType);
                var guardMethod = containingTypeName switch {
                    "global::System.Threading.Tasks.Task" when method.Name == "Run" => "WrapTask",
                    "global::System.Threading.Tasks.TaskFactory" when method.Name == "StartNew" => "WrapTask",
                    "global::System.Threading.ThreadPool" when method.Name is "QueueUserWorkItem" or "UnsafeQueueUserWorkItem" => "WrapDetached",
                    _ => null,
                };
                return guardMethod is null
                    ? rewrittenNode
                    : TryWrapArgument(node.ArgumentList.Arguments, rewrittenNode.ArgumentList.Arguments, 0, guardMethod, method, rewrittenNode);
            }

            private TNode TryWrapArgument<TNode>(
                SeparatedSyntaxList<ArgumentSyntax> originalArguments,
                SeparatedSyntaxList<ArgumentSyntax> rewrittenArguments,
                int argumentIndex,
                string guardMethod,
                IMethodSymbol method,
                TNode rewrittenNode)
                where TNode : SyntaxNode {
                if (argumentIndex >= originalArguments.Count
                    || argumentIndex >= rewrittenArguments.Count
                    || !originalArguments[argumentIndex].RefKindKeyword.IsKind(SyntaxKind.None)
                    || !TryGetParameter(method, originalArguments[argumentIndex], argumentIndex, out var parameter)
                    || parameter.Type.TypeKind != TypeKind.Delegate) {
                    return rewrittenNode;
                }

                var rewrittenArgument = rewrittenArguments[argumentIndex];
                var guardedExpression = CreateGuardedDelegate(rewrittenArgument.Expression, parameter.Type, guardMethod);
                var updatedArguments = rewrittenArguments.Replace(rewrittenArgument, rewrittenArgument.WithExpression(guardedExpression));
                MarkChanged();
                RequiresSessionTokenLocal = true;
                return rewrittenNode switch {
                    InvocationExpressionSyntax invocation => (TNode)(SyntaxNode)invocation.WithArgumentList(invocation.ArgumentList.WithArguments(updatedArguments)),
                    ObjectCreationExpressionSyntax creation when creation.ArgumentList is not null => (TNode)(SyntaxNode)creation.WithArgumentList(creation.ArgumentList.WithArguments(updatedArguments)),
                    _ => rewrittenNode,
                };
            }

            private InvocationExpressionSyntax TryAppendCancellationToken(InvocationExpressionSyntax node, InvocationExpressionSyntax rewrittenNode) {
                if (!CanUseSessionToken(node)
                    || IsWithinNameOf(node)
                    || !TryGetBoundSymbol<IMethodSymbol>(node, out var method)
                    || method.Parameters.Any(static parameter => IsCancellationToken(parameter.Type))
                    || HasCancellationTokenArgument(node)
                    || TryFindTrailingCancellationOverload(method) is not { } overload) {
                    return rewrittenNode;
                }

                var tokenArgument = SyntaxFactory.Argument(CreateTokenExpression());
                if (rewrittenNode.ArgumentList.Arguments.Any(static argument => argument.NameColon is not null)
                    && overload.Parameters[^1].Name is { Length: > 0 } parameterName) {
                    tokenArgument = tokenArgument.WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(parameterName)));
                }

                MarkChanged();
                RequiresSessionTokenLocal = true;
                return rewrittenNode.WithArgumentList(rewrittenNode.ArgumentList.WithArguments(rewrittenNode.ArgumentList.Arguments.Add(tokenArgument)));
            }

            private IMethodSymbol? TryFindTrailingCancellationOverload(IMethodSymbol method) {
                if (method.ReducedFrom is not null) {
                    return null;
                }

                var originalMethod = method.OriginalDefinition;
                if (originalMethod.Parameters.Any(static parameter => IsCancellationToken(parameter.Type))) {
                    return null;
                }

                var matches = originalMethod.ContainingType.GetMembers(originalMethod.Name)
                    .OfType<IMethodSymbol>()
                    .Where(candidate => candidate.MethodKind == originalMethod.MethodKind
                        && candidate.IsStatic == originalMethod.IsStatic
                        && candidate.TypeParameters.Length == originalMethod.TypeParameters.Length
                        && candidate.Parameters.Length == originalMethod.Parameters.Length + 1
                        && IsCancellationToken(candidate.Parameters[^1].Type)
                        && PrefixParametersMatch(originalMethod, candidate))
                    .Take(2)
                    .ToArray();
                return matches.Length == 1 ? matches[0] : null;
            }

            private bool HasCancellationTokenArgument(InvocationExpressionSyntax node) {
                return node.ArgumentList.Arguments.Any(argument =>
                    argument.NameColon?.Name.Identifier.ValueText.Contains("cancellation", StringComparison.OrdinalIgnoreCase) == true
                    || IsCancellationToken(SemanticModel.GetTypeInfo(argument.Expression).ConvertedType));
            }

            private StatementSyntax InjectCancellationCheckIfNeeded(SyntaxNode owner, StatementSyntax statement) {
                return CanUseSessionToken(owner) ? InjectCancellationCheck(statement) : statement;
            }

            private StatementSyntax InjectCancellationCheck(StatementSyntax statement) {
                MarkChanged();
                RequiresSessionTokenLocal = true;
                var checkStatement = SyntaxFactory.ParseStatement($"{tokenLocalName}.ThrowIfCancellationRequested();");
                if (statement is BlockSyntax block) {
                    return block.WithStatements(block.Statements.Insert(0, checkStatement));
                }

                return SyntaxFactory.Block(checkStatement, statement.WithoutLeadingTrivia()).WithTriviaFrom(statement);
            }

            private ExpressionSyntax CreateGuardedDelegate(ExpressionSyntax expression, ITypeSymbol delegateType, string guardMethod) {
                var delegateExpression = expression is AnonymousFunctionExpressionSyntax
                    ? SyntaxFactory.ParenthesizedExpression(expression.WithoutTrivia())
                    : expression.WithoutTrivia();
                return SyntaxFactory.InvocationExpression(
                        SyntaxFactory.ParseExpression($"{GuardTypeName}.{guardMethod}"),
                        SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList([
                            SyntaxFactory.Argument(CreateTokenExpression()),
                            SyntaxFactory.Argument(SyntaxFactory.CastExpression(
                                SyntaxFactory.ParseTypeName(delegateType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                                delegateExpression)),
                        ])))
                    .WithTriviaFrom(expression);
            }

            private static bool TryGetParameter(IMethodSymbol method, ArgumentSyntax argument, int argumentIndex, out IParameterSymbol parameter) {
                parameter = argument.NameColon is { Name.Identifier.ValueText: { Length: > 0 } parameterName }
                    ? method.Parameters.FirstOrDefault(candidate => candidate.Name == parameterName)!
                    : argumentIndex < method.Parameters.Length ? method.Parameters[argumentIndex] : null!;
                return parameter is not null;
            }

            private static bool PrefixParametersMatch(IMethodSymbol source, IMethodSymbol candidate) {
                return source.Parameters.Zip(candidate.Parameters).All(pair =>
                    pair.First.RefKind == pair.Second.RefKind
                    && SymbolEqualityComparer.Default.Equals(pair.First.Type, pair.Second.Type));
            }

            private static bool CanUseSessionToken(SyntaxNode node) =>
                node.AncestorsAndSelf().Any(static ancestor => ancestor is GlobalStatementSyntax);

            private static bool IsCancellationToken(ITypeSymbol? type) =>
                type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Threading.CancellationToken";

            private ExpressionSyntax CreateTokenExpression() => SyntaxFactory.IdentifierName(tokenLocalName);
        }
    }
}
