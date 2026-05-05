using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using System.Collections.Immutable;
using System.Reflection;

namespace Atelier.Session.Execution
{
    internal sealed class ExecutionReferencePlanner
    {
        private readonly ImmutableArray<ManagedPluginReference> managedPluginReferences;

        public ExecutionReferencePlanner(ImmutableArray<ManagedPluginReference> managedPluginReferences) {
            this.managedPluginReferences = managedPluginReferences;
        }

        public ImmutableArray<string> ResolveManagedPluginKeys(Script<object?> script, CancellationToken cancellationToken = default) {

            if (managedPluginReferences.IsDefaultOrEmpty) {
                return [];
            }

            var compilation = script.GetCompilation();
            var syntaxTree = compilation.SyntaxTrees.LastOrDefault();
            if (syntaxTree is null) {
                return [];
            }

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot(cancellationToken);
            HashSet<string> keys = [];
            foreach (var node in root.DescendantNodesAndSelf()) {
                CollectAssembly(semanticModel.GetDeclaredSymbol(node, cancellationToken)?.ContainingAssembly, keys);

                var symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken);
                CollectAssembly(symbolInfo.Symbol?.ContainingAssembly, keys);
                foreach (var candidate in symbolInfo.CandidateSymbols) {
                    CollectAssembly(candidate.ContainingAssembly, keys);
                }

                var typeInfo = semanticModel.GetTypeInfo(node, cancellationToken);
                CollectAssembly(typeInfo.Type?.ContainingAssembly, keys);
                CollectAssembly(typeInfo.ConvertedType?.ContainingAssembly, keys);

                if (node is IdentifierNameSyntax identifier) {
                    CollectAliasTarget(semanticModel.GetAliasInfo(identifier, cancellationToken), keys);
                }
            }

            return [.. keys];
        }

        private void CollectAliasTarget(IAliasSymbol? alias, HashSet<string> keys) {
            if (alias?.Target is INamespaceSymbol namespaceSymbol) {
                CollectAssembly(namespaceSymbol.ContainingAssembly, keys);
            }
        }

        private void CollectAssembly(IAssemblySymbol? assemblySymbol, HashSet<string> keys) {
            if (assemblySymbol is null || assemblySymbol.IsImplicitlyDeclared) {
                return;
            }

            foreach (var reference in managedPluginReferences) {
                if (!Matches(reference.RootAssemblyName, assemblySymbol.Identity)) {
                    continue;
                }

                keys.Add(reference.StableKey);
                break;
            }
        }

        private static bool Matches(AssemblyName referenceAssemblyName, AssemblyIdentity identity) {
            if (!string.Equals(referenceAssemblyName.Name, identity.Name, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            if (referenceAssemblyName.Version is { } version && version != identity.Version) {
                return false;
            }

            var referenceToken = referenceAssemblyName.GetPublicKeyToken();
            if ((referenceToken?.Length ?? 0) == 0 && identity.PublicKeyToken.IsDefaultOrEmpty) {
                return true;
            }

            if (referenceToken is null || referenceToken.Length != identity.PublicKeyToken.Length) {
                return false;
            }

            for (var index = 0; index < referenceToken.Length; index++) {
                if (referenceToken[index] != identity.PublicKeyToken[index]) {
                    return false;
                }
            }

            return true;
        }
    }
}
