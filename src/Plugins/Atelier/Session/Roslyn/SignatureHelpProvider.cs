using Atelier.Presentation.Prompt;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using UnifierTSL.Surface.Prompting.Model;

namespace Atelier.Session.Roslyn
{
    internal static class SignatureHelpProvider
    {
        public static async Task<SignatureHelpInfo> GetSignatureHelpAsync(
            Document document,
            SyntheticDocument syntheticDocument,
            CancellationToken cancellationToken) {
            try {
                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (root is null || semanticModel is null) {
                    return SignatureHelpInfo.Empty;
                }

                return TryResolveSignatureHelpContext(
                    semanticModel,
                    root,
                    syntheticDocument.Text,
                    syntheticDocument.SyntheticCaretIndex,
                    cancellationToken,
                    out var context)
                    ? CreateSignatureHelpInfo(context, cancellationToken)
                    : SignatureHelpInfo.Empty;
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch {
                return SignatureHelpInfo.Empty;
            }
        }

        private static bool TryResolveSignatureHelpContext(
            SemanticModel semanticModel,
            SyntaxNode root,
            string text,
            int position,
            CancellationToken cancellationToken,
            out SignatureHelpContext context) {
            if (TryResolveOpenGenericSignatureHelpContext(semanticModel, root, text, position, cancellationToken, out context)) {
                return true;
            }

            foreach (var argList in SignatureHelpLocation.EnumerateActiveArgumentLists(text, root, position)) {
                var candidates = GetSignatureHelpCandidates(semanticModel, argList.Parent, cancellationToken);
                if (!candidates.IsDefaultOrEmpty) {
                    context = new SignatureHelpContext(
                        candidates,
                        SignatureHelpLocation.GetArgumentListParameterIndex(argList, position),
                        GetSelectedSignatureSymbol(semanticModel, argList.Parent, cancellationToken));
                    return true;
                }
            }

            context = default;
            return false;
        }

        private static bool TryResolveOpenGenericSignatureHelpContext(
            SemanticModel semanticModel,
            SyntaxNode root,
            string text,
            int position,
            CancellationToken cancellationToken,
            out SignatureHelpContext context) {
            if (!SignatureHelpLocation.TryResolveOpenGenericLocation(root, text, position, out var binaryExpression, out var identifierName)) {
                context = default;
                return false;
            }

            var candidates = CreateGenericSignatureHelpCandidates(GetOpenGenericCandidates(
                semanticModel,
                binaryExpression,
                identifierName,
                cancellationToken));
            if (candidates.IsDefaultOrEmpty) {
                context = default;
                return false;
            }

            context = new SignatureHelpContext(
                candidates,
                SignatureHelpLocation.GetOpenGenericParameterIndex(binaryExpression, text, position),
                null);
            return true;
        }

        private static SignatureHelpInfo CreateSignatureHelpInfo(SignatureHelpContext context, CancellationToken cancellationToken) {
            if (context.Candidates.IsDefaultOrEmpty) {
                return SignatureHelpInfo.Empty;
            }

            var items = ImmutableArray.CreateBuilder<SignatureHelpItem>(context.Candidates.Length);
            var activeIndex = -1;
            foreach (var candidate in context.Candidates) {
                var item = new SignatureHelpItem(
                    GetSignatureItemId(candidate),
                    BuildSignatureSummary(candidate),
                    BuildSignatureSections(candidate, context.ActiveParameterIndex, cancellationToken));
                items.Add(item);
                if (activeIndex < 0 && SymbolMatches(candidate.Symbol, context.SelectedSymbol)) {
                    activeIndex = items.Count - 1;
                }
            }

            if (items.Count == 0) {
                return SignatureHelpInfo.Empty;
            }

            if (activeIndex < 0) {
                activeIndex = ResolveActiveSignatureIndex(context.Candidates, context.ActiveParameterIndex);
            }

            activeIndex = Math.Clamp(activeIndex, 0, items.Count - 1);
            return new SignatureHelpInfo(items.ToImmutable(), items[activeIndex].Id, activeIndex);
        }

        private static int ResolveActiveSignatureIndex(ImmutableArray<SignatureHelpCandidate> candidates, int activeParameterIndex) {
            for (var index = 0; index < candidates.Length; index++) {
                if (AcceptsParameterIndex(candidates[index], activeParameterIndex)) {
                    return index;
                }
            }

            return 0;
        }

        private static bool AcceptsParameterIndex(SignatureHelpCandidate candidate, int activeParameterIndex) {
            return candidate.Parameters.IsDefaultOrEmpty
                ? activeParameterIndex <= 0
                : activeParameterIndex < candidate.Parameters.Length
                    || candidate.IsVariadic && activeParameterIndex >= candidate.Parameters.Length - 1;
        }

        private static PromptStyledText BuildSignatureSummary(SignatureHelpCandidate candidate) {
            StringBuilder builder = new();
            List<PromptHighlightSpan> highlights = [];
            foreach (var part in candidate.Symbol.ToDisplayParts(SymbolDisplayFormat.MinimallyQualifiedFormat)) {
                var text = part.ToString();
                if (text.Length == 0) {
                    continue;
                }

                var partStartIndex = builder.Length;
                builder.Append(text);
                var styleId = RoslynStyleResolver.ResolveSymbolDisplayPartStyleId(part.Kind);
                if (!string.IsNullOrWhiteSpace(styleId)) {
                    highlights.Add(new PromptHighlightSpan(partStartIndex, text.Length, styleId));
                }
            }

            return new PromptStyledText {
                Text = builder.ToString(),
                Highlights = [.. highlights],
            };
        }

        private static ImmutableArray<SignatureHelpSection> BuildSignatureSections(
            SignatureHelpCandidate candidate,
            int activeParameterIndex,
            CancellationToken cancellationToken) {
            var documentation = GetSignatureDocumentation(candidate.Symbol, cancellationToken);
            var sections = ImmutableArray.CreateBuilder<SignatureHelpSection>();
            AddSection("Info", documentation.Summary);

            var activeParameter = ResolveActiveSignatureParameter(candidate, activeParameterIndex);
            if (activeParameter is not null) {
                var parameterDocs = candidate.ParameterKind switch {
                    SignatureHelpParameterKind.Type => documentation.GetTypeParameterText(activeParameter.Name),
                    _ => documentation.GetParameterText(activeParameter.Name),
                };
                AddSection(activeParameter.Name, parameterDocs);
            }

            AddSection("Returns", documentation.Returns);
            return sections.ToImmutable();

            void AddSection(string title, string text) {
                var lines = CreateStyledTextLines(text);
                if (!lines.IsDefaultOrEmpty) {
                    sections.Add(new SignatureHelpSection(title, lines));
                }
            }
        }

        private static ImmutableArray<PromptStyledText> CreateStyledTextLines(string text) {
            if (string.IsNullOrWhiteSpace(text)) {
                return [];
            }

            return [.. text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(static line => line.Trim())
                .Where(static line => line.Length > 0)
                .Select(line => new PromptStyledText { Text = line })];
        }

        private static ImmutableArray<SignatureHelpCandidate> GetSignatureHelpCandidates(
            SemanticModel semanticModel,
            SyntaxNode? node,
            CancellationToken cancellationToken) {
            return node switch {
                InvocationExpressionSyntax invocation => CreateValueSignatureHelpCandidates(semanticModel.GetMemberGroup(invocation.Expression, cancellationToken)),
                ObjectCreationExpressionSyntax objectCreation => CreateValueSignatureHelpCandidates(semanticModel.GetMemberGroup(objectCreation, cancellationToken)),
                ImplicitObjectCreationExpressionSyntax implicitObjectCreation => CreateValueSignatureHelpCandidates(semanticModel.GetMemberGroup(implicitObjectCreation, cancellationToken)),
                ElementAccessExpressionSyntax elementAccess => CreateValueSignatureHelpCandidates(semanticModel.GetIndexerGroup(elementAccess.Expression, cancellationToken)),
                ConstructorInitializerSyntax constructorInitializer => CreateValueSignatureHelpCandidates(GetConstructorInitializerMembers(semanticModel, constructorInitializer, cancellationToken)),
                GenericNameSyntax genericName => CreateGenericSignatureHelpCandidates(GetMemberGroupGeneric(semanticModel, genericName, cancellationToken)),
                _ => [],
            };
        }

        private static ImmutableArray<SignatureHelpCandidate> CreateValueSignatureHelpCandidates(IEnumerable<ISymbol> symbols) {
            var candidates = ImmutableArray.CreateBuilder<SignatureHelpCandidate>();
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (var symbol in symbols) {
                switch (symbol) {
                    case IMethodSymbol method when ids.Add(GetSignatureCandidateKey(method)):
                        candidates.Add(new SignatureHelpCandidate(
                            method,
                            [.. method.Parameters.Cast<ISymbol>()],
                            SignatureHelpParameterKind.Value,
                            method.Parameters.Length > 0 && method.Parameters[^1].IsParams));
                        break;
                    case IPropertySymbol { IsIndexer: true } property when ids.Add(GetSignatureCandidateKey(property)):
                        candidates.Add(new SignatureHelpCandidate(
                            property,
                            [.. property.Parameters.Cast<ISymbol>()],
                            SignatureHelpParameterKind.Value,
                            property.Parameters.Length > 0 && property.Parameters[^1].IsParams));
                        break;
                }
            }

            return SortSignatureHelpCandidates(candidates);
        }

        private static ImmutableArray<SignatureHelpCandidate> CreateGenericSignatureHelpCandidates(IEnumerable<ISymbol> symbols) {
            var candidates = ImmutableArray.CreateBuilder<SignatureHelpCandidate>();
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (var symbol in symbols) {
                switch (symbol) {
                    case IMethodSymbol { IsGenericMethod: true } method when ids.Add(GetSignatureCandidateKey(method)):
                        candidates.Add(new SignatureHelpCandidate(
                            method,
                            [.. method.TypeParameters.Cast<ISymbol>()],
                            SignatureHelpParameterKind.Type,
                            false));
                        break;
                    case INamedTypeSymbol { IsGenericType: true } type when ids.Add(GetSignatureCandidateKey(type)):
                        candidates.Add(new SignatureHelpCandidate(
                            type,
                            [.. type.TypeParameters.Cast<ISymbol>()],
                            SignatureHelpParameterKind.Type,
                            false));
                        break;
                }
            }

            return SortSignatureHelpCandidates(candidates);
        }

        private static ISymbol[] GetOpenGenericCandidates(
            SemanticModel semanticModel,
            BinaryExpressionSyntax binaryExpression,
            IdentifierNameSyntax identifierName,
            CancellationToken cancellationToken) {
            if (binaryExpression.Left is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == identifierName) {
                return LookupGenericMethodsAndTypes(
                    semanticModel,
                    memberAccess.Name.SpanStart,
                    identifierName.Identifier.ValueText,
                    memberAccess.Expression,
                    cancellationToken);
            }

            var node = binaryExpression.Left.Parent;
            while (node is QualifiedNameSyntax) {
                node = node.Parent;
            }

            if (node is ObjectCreationExpressionSyntax objectCreation) {
                return LookupGenericObjectCreationTypes(semanticModel, objectCreation.Type, identifierName, cancellationToken);
            }

            return LookupGenericMethodsAndTypes(
                semanticModel,
                identifierName.SpanStart,
                identifierName.Identifier.ValueText,
                cancellationToken: cancellationToken);
        }

        private static ISymbol[] LookupGenericObjectCreationTypes(
            SemanticModel semanticModel,
            TypeSyntax typeSyntax,
            SimpleNameSyntax typeName,
            CancellationToken cancellationToken) {
            if (typeSyntax is SimpleNameSyntax directName && directName != typeName) {
                return [];
            }

            INamespaceSymbol? typeNamespace = null;
            if (typeSyntax is QualifiedNameSyntax qualifiedName) {
                if (!ReferenceEquals(qualifiedName.Right, typeName)) {
                    return [];
                }

                typeNamespace = semanticModel.GetSymbolInfo(qualifiedName.Left, cancellationToken).Symbol as INamespaceSymbol;
            }

            if (typeSyntax is not SimpleNameSyntax && typeSyntax is not QualifiedNameSyntax) {
                return [];
            }

            return LookupGenericTypes(semanticModel, typeSyntax.SpanStart, typeName.Identifier.ValueText, typeNamespace);
        }

        private static ImmutableArray<SignatureHelpCandidate> SortSignatureHelpCandidates(ImmutableArray<SignatureHelpCandidate>.Builder candidates) {
            return [.. candidates
                .OrderBy(candidate => candidate.ParameterKind)
                .ThenBy(candidate => candidate.Parameters.Length)
                .ThenBy(candidate => candidate.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), StringComparer.Ordinal)];
        }

        private static IEnumerable<ISymbol> GetConstructorInitializerMembers(
            SemanticModel semanticModel,
            ConstructorInitializerSyntax constructorInitializer,
            CancellationToken cancellationToken) {
            if (constructorInitializer.Parent is null
                || semanticModel.GetDeclaredSymbol(constructorInitializer.Parent, cancellationToken) is not IMethodSymbol method) {
                return [];
            }

            return constructorInitializer.ThisOrBaseKeyword.Kind() switch {
                SyntaxKind.BaseKeyword when method.ContainingType.BaseType is { } baseType
                    => baseType.InstanceConstructors.Where(ctor => semanticModel.IsAccessible(constructorInitializer.SpanStart, ctor)),
                SyntaxKind.ThisKeyword
                    => method.ContainingType.InstanceConstructors.Where(ctor => semanticModel.IsAccessible(constructorInitializer.SpanStart, ctor)),
                _ => [],
            };
        }

        private static ISymbol[] GetMemberGroupGeneric(SemanticModel semanticModel, GenericNameSyntax genericName, CancellationToken cancellationToken) {
            SyntaxNode? node = genericName.Parent;
            while (node is QualifiedNameSyntax) {
                node = node.Parent;
            }

            if (node is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == genericName) {
                return LookupGenericMethodsAndTypes(
                    semanticModel,
                    memberAccess.Name.SpanStart,
                    genericName.Identifier.ValueText,
                    memberAccess.Expression,
                    cancellationToken);
            }

            if (node is ObjectCreationExpressionSyntax objectCreation) {
                return LookupGenericObjectCreationTypes(semanticModel, objectCreation.Type, genericName, cancellationToken);
            }

            return LookupGenericMethodsAndTypes(
                semanticModel,
                genericName.SpanStart,
                genericName.Identifier.ValueText,
                cancellationToken: cancellationToken);
        }

        private static ISymbol[] LookupGenericMethodsAndTypes(
            SemanticModel semanticModel,
            int location,
            string name,
            SyntaxNode? containerTypeSyntax = null,
            CancellationToken cancellationToken = default) {
            var containerType = containerTypeSyntax is null
                ? null
                : semanticModel.GetTypeInfo(containerTypeSyntax, cancellationToken).Type;
            return [.. semanticModel
                .LookupSymbols(location, containerType, name)
                .Where(static symbol => symbol is IMethodSymbol { IsGenericMethod: true } or INamedTypeSymbol { IsGenericType: true })];
        }

        private static ISymbol[] LookupGenericTypes(
            SemanticModel semanticModel,
            int location,
            string name,
            INamespaceSymbol? typeNamespace) {
            return [.. semanticModel
                .LookupNamespacesAndTypes(location, name: name)
                .OfType<INamedTypeSymbol>()
                .Where(type => type.IsGenericType && IsSubnamespace(type.ContainingNamespace, typeNamespace))];
        }

        private static bool IsSubnamespace(INamespaceSymbol? @namespace, INamespaceSymbol? subnamespace) {
            while (true) {
                if (subnamespace is null) {
                    return true;
                }

                if (@namespace is null || @namespace.Name != subnamespace.Name) {
                    return false;
                }

                @namespace = @namespace.ContainingNamespace;
                subnamespace = subnamespace.ContainingNamespace;
            }
        }

        private static ISymbol? GetSelectedSignatureSymbol(SemanticModel semanticModel, SyntaxNode? node, CancellationToken cancellationToken) {
            return node switch {
                InvocationExpressionSyntax invocation => semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol,
                ObjectCreationExpressionSyntax objectCreation => semanticModel.GetSymbolInfo(objectCreation, cancellationToken).Symbol,
                ImplicitObjectCreationExpressionSyntax implicitObjectCreation => semanticModel.GetSymbolInfo(implicitObjectCreation, cancellationToken).Symbol,
                ElementAccessExpressionSyntax elementAccess => semanticModel.GetSymbolInfo(elementAccess, cancellationToken).Symbol,
                ConstructorInitializerSyntax constructorInitializer => semanticModel.GetSymbolInfo(constructorInitializer, cancellationToken).Symbol,
                GenericNameSyntax genericName => semanticModel.GetSymbolInfo(genericName, cancellationToken).Symbol,
                _ => null,
            };
        }

        private static string GetSignatureItemId(SignatureHelpCandidate candidate) {
            return $"atelier.signature:{candidate.ParameterKind}:{GetSignatureCandidateKey(candidate.Symbol)}";
        }

        private static string GetSignatureCandidateKey(ISymbol symbol) {
            var baseSymbol = symbol.OriginalDefinition;
            return baseSymbol.GetDocumentationCommentId()
                ?? baseSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        private static bool SymbolMatches(ISymbol symbol, ISymbol? selectedSymbol) {
            return selectedSymbol is not null
                && (SymbolEqualityComparer.Default.Equals(symbol, selectedSymbol)
                    || SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, selectedSymbol.OriginalDefinition));
        }

        private static ISymbol? ResolveActiveSignatureParameter(SignatureHelpCandidate candidate, int activeParameterIndex) {
            return candidate.Parameters.IsDefaultOrEmpty
                ? null
                : candidate.Parameters[candidate.IsVariadic && activeParameterIndex >= candidate.Parameters.Length
                    ? candidate.Parameters.Length - 1
                    : Math.Clamp(activeParameterIndex, 0, candidate.Parameters.Length - 1)];
        }

        private static SignatureDocumentation GetSignatureDocumentation(ISymbol symbol, CancellationToken cancellationToken) {
            var xml = symbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(xml) || TryParseDocumentationXml(xml) is not { } root) {
                return SignatureDocumentation.Empty;
            }

            return new SignatureDocumentation(
                GetDocumentationElementText(root, "summary"),
                GetDocumentationElementText(root, "returns"),
                GetNamedDocumentationTexts(root, "param"),
                GetNamedDocumentationTexts(root, "typeparam"));
        }

        private static XElement? TryParseDocumentationXml(string xml) {
            try {
                return XElement.Parse(xml);
            }
            catch {
            }

            try {
                return XElement.Parse("<root>" + xml + "</root>");
            }
            catch {
                return null;
            }
        }

        private static string GetDocumentationElementText(XElement root, string elementName) {
            return NormalizeDocumentationText(string.Join(
                "\n",
                root.Elements().Where(element => element.Name.LocalName == elementName).Select(RenderDocumentationText)));
        }

        private static ImmutableDictionary<string, string> GetNamedDocumentationTexts(XElement root, string elementName) {
            return root.Elements()
                .Where(element => element.Name.LocalName == elementName)
                .Select(element => (Name: element.Attribute("name")?.Value?.Trim(), Text: NormalizeDocumentationText(RenderDocumentationText(element))))
                .Where(static pair => !string.IsNullOrWhiteSpace(pair.Name) && pair.Text.Length > 0)
                .ToImmutableDictionary(pair => pair.Name!, pair => pair.Text, StringComparer.Ordinal);
        }

        private static string RenderDocumentationText(XElement element) {
            return string.Concat(element.Nodes().Select(RenderDocumentationNode));
        }

        private static string RenderDocumentationNode(XNode node) {
            return node switch {
                XText textNode => textNode.Value,
                XElement elementNode => elementNode.Name.LocalName switch {
                    "see" or "seealso" => RenderSeeElement(elementNode),
                    "paramref" or "typeparamref" => elementNode.Attribute("name")?.Value ?? string.Empty,
                    "para" => "\n" + RenderDocumentationText(elementNode) + "\n",
                    "br" => "\n",
                    "list" => RenderDocumentationList(elementNode),
                    _ => RenderDocumentationText(elementNode),
                },
                _ => string.Empty,
            };
        }

        private static string RenderSeeElement(XElement element) {
            if (!string.IsNullOrWhiteSpace(element.Value)) {
                return element.Value;
            }

            var cref = element.Attribute("cref")?.Value;
            if (!string.IsNullOrWhiteSpace(cref)) {
                var colonIndex = cref.IndexOf(':');
                return colonIndex >= 0 && colonIndex < cref.Length - 1
                    ? cref[(colonIndex + 1)..]
                    : cref;
            }

            return element.Attribute("langword")?.Value ?? string.Empty;
        }

        private static string RenderDocumentationList(XElement element) {
            var items = element.Elements().Where(item => item.Name.LocalName == "item").ToArray();
            if (items.Length == 0) {
                return RenderDocumentationText(element);
            }

            var ordered = string.Equals(element.Attribute("type")?.Value, "number", StringComparison.OrdinalIgnoreCase);
            return string.Join('\n', items.Select((item, index) =>
                (ordered ? $"{index + 1}. " : "- ") + RenderDocumentationText(item)));
        }

        private static string NormalizeDocumentationText(string text) {
            if (string.IsNullOrWhiteSpace(text)) {
                return string.Empty;
            }

            return string.Join(
                "\n",
                text.Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n')
                    .Select(static line => string.Join(" ", line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
                    .Where(static line => line.Length > 0));
        }

        private readonly record struct SignatureHelpContext(
            ImmutableArray<SignatureHelpCandidate> Candidates,
            int ActiveParameterIndex,
            ISymbol? SelectedSymbol);

        private readonly record struct SignatureHelpCandidate(
            ISymbol Symbol,
            ImmutableArray<ISymbol> Parameters,
            SignatureHelpParameterKind ParameterKind,
            bool IsVariadic);

        private readonly record struct SignatureDocumentation(
            string Summary,
            string Returns,
            ImmutableDictionary<string, string> Parameters,
            ImmutableDictionary<string, string> TypeParameters) {
            public static SignatureDocumentation Empty { get; } = new(string.Empty, string.Empty, ImmutableDictionary<string, string>.Empty, ImmutableDictionary<string, string>.Empty);

            public string GetParameterText(string name) {
                return Parameters.TryGetValue(name, out var text) ? text : string.Empty;
            }

            public string GetTypeParameterText(string name) {
                return TypeParameters.TryGetValue(name, out var text) ? text : string.Empty;
            }
        }

        private enum SignatureHelpParameterKind : byte {
            Value,
            Type,
        }
    }
}
