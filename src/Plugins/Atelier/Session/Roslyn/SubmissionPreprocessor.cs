using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;

namespace Atelier.Session.Roslyn
{
    internal sealed class SubmissionPreprocessor(string hostOutExpression)
    {
        private readonly IPreprocessorPass[] passes = [
            new ConsolePreprocessor(hostOutExpression),
            new CancellationPreprocessor(),
        ];

        public Script<object?> RewriteScript(
            Script<object?> script,
            Func<string, Script<object?>> createScript,
            CancellationToken cancellationToken = default) {

            cancellationToken.ThrowIfCancellationRequested();
            var compilation = script.GetCompilation();
            var syntaxTree = compilation.SyntaxTrees.LastOrDefault();
            if (syntaxTree is null || syntaxTree.GetRoot(cancellationToken) is not CompilationUnitSyntax root) {
                return script;
            }

            var changed = false;
            foreach (var pass in passes) {
                var rewrittenRoot = pass.Rewrite(root, compilation.GetSemanticModel(syntaxTree), out var passChanged);
                if (!passChanged) {
                    continue;
                }

                changed = true;
                var oldTree = syntaxTree;
                root = rewrittenRoot;
                syntaxTree = CSharpSyntaxTree.Create(
                    root,
                    (CSharpParseOptions?)oldTree.Options,
                    oldTree.FilePath,
                    oldTree.Encoding);
                root = (CompilationUnitSyntax)syntaxTree.GetRoot(cancellationToken);
                compilation = compilation.ReplaceSyntaxTree(oldTree, syntaxTree);
            }

            return changed ? createScript(root.ToFullString()) : script;
        }
    }

    internal interface IPreprocessorPass
    {
        CompilationUnitSyntax Rewrite(CompilationUnitSyntax root, SemanticModel semanticModel, out bool changed);
    }

    internal abstract class PreprocessorRewriter(SemanticModel semanticModel) : CSharpSyntaxRewriter
    {
        protected SemanticModel SemanticModel { get; } = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));

        public bool Changed { get; private set; }

        protected void MarkChanged() {
            Changed = true;
        }

        protected bool AnyBoundSymbol(SyntaxNode node, Func<ISymbol, bool> predicate) {
            var symbolInfo = SemanticModel.GetSymbolInfo(node);
            return symbolInfo.Symbol is { } symbol && predicate(symbol)
                || symbolInfo.CandidateSymbols.Any(predicate);
        }

        protected bool TryGetBoundSymbol<TSymbol>(SyntaxNode node, out TSymbol symbol)
            where TSymbol : class, ISymbol {
            var symbolInfo = SemanticModel.GetSymbolInfo(node);
            symbol = symbolInfo.Symbol as TSymbol
                ?? symbolInfo.CandidateSymbols.OfType<TSymbol>().FirstOrDefault()!;
            return symbol is not null;
        }

        protected static string GetFullTypeName(ITypeSymbol? type) {
            return type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty;
        }

        protected static bool IsWithinNameOf(SyntaxNode node) {
            return node.AncestorsAndSelf().Any(static current => current.Parent is ArgumentSyntax {
                Parent: ArgumentListSyntax {
                    Parent: InvocationExpressionSyntax {
                        Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }
                    }
                }
            });
        }
    }
}
