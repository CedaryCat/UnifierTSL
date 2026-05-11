using System.Collections.Immutable;
using System.Reflection;
using TShockAPI.ConsolePrompting;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Bindings;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Commanding.Endpoints;
using UnifierTSL.Commanding.Execution;
using UnifierTSL.Commanding.Prompting;
using UnifierTSL.Servers;
using System.Diagnostics.CodeAnalysis;

namespace TShockAPI.Commanding
{
    internal sealed record TSCommandRefTarget(
        string InvocationPath,
        string CanonicalPath,
        TSCommandCatalogEntry? LegacyEntry,
        CommandRootDefinition? Root,
        CommandActionDefinition? Action,
        ImmutableArray<TShockEndpointActionBinding> Bindings);

    internal static class TSCommandRefResolver
    {
        private readonly record struct DeclarativeRootKey(string SourceName, string RootName, Type ControllerType);

        private sealed record DeclarativeActionRef(
            CommandActionDefinition Action,
            ImmutableArray<TShockEndpointActionBinding> Bindings);

        private sealed record DeclarativeRootRef(
            CommandRootDefinition Root,
            ImmutableArray<DeclarativeActionRef> Actions);

        public static CommandParamBindingResult Bind(CommandParamBindingContext context, CommandRefAttribute attribute) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var remainingTokens = context.UserArguments.Skip(context.UserIndex).ToArray();
            if (attribute.Recursive) {
                var rawInvocation = ResolveRawInvocation(context.InvocationContext, context.UserIndex);
                return CommandParamBindingResult.Success(
                    FormatOutputPath(rawInvocation, context.InvocationContext.Server, remainingTokens[0], attribute.InsertPrefix),
                    remainingTokens.Length);
            }

            if (TryResolveBoundTarget(remainingTokens, attribute, out var target, out var consumedTokens)) {
                return CommandParamBindingResult.Success(
                    FormatOutputPath(target.InvocationPath, context.InvocationContext.Server, remainingTokens[0], attribute.InsertPrefix),
                    consumedTokens);
            }

            return CommandParamBindingResult.Success(
                FormatOutputPath(remainingTokens[0], context.InvocationContext.Server, remainingTokens[0], attribute.InsertPrefix),
                consumedTokens: 1);
        }

        public static PromptParamExplainResult Explain(PromptParamExplainContext context) {
            var rawText = context.RawToken ?? string.Empty;
            if (TryResolveExactTarget(rawText, context.ActiveSlot, context.Server, out var exact)) {
                return Resolved(exact.CanonicalPath);
            }

            var matches = EnumerateTargets(context.ActiveSlot)
                .Where(candidate => ResolvePromptCandidateWeight(candidate.InvocationPath, rawText, context.Server, context.ActiveSlot) is not null)
                .Select(static candidate => candidate.InvocationPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray();
            return matches.Length switch {
                0 => Invalid(),
                1 => Resolved(matches[0]),
                _ => Ambiguous(matches),
            };
        }

        public static IReadOnlyList<string> GetCandidates(PromptParamCandidateContext context) {
            var rawText = context.RawToken ?? string.Empty;
            return [.. EnumerateTargets(context.ActiveSlot)
                .Where(candidate => ResolvePromptCandidateWeight(candidate.InvocationPath, rawText, context.Server, context.ActiveSlot) is not null)
                .Select(candidate => FormatOutputPath(candidate.InvocationPath, context.Server, rawText, TSPromptSlotMetadata.InsertCommandRefPrefix(context.ActiveSlot)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static candidate => candidate, StringComparer.OrdinalIgnoreCase)];
        }

        public static int? ResolveCandidateMatchWeight(PromptParamCandidateContext context, string candidate, int baseWeight) {
            return ResolvePromptCandidateWeight(candidate, context.RawToken ?? string.Empty, context.Server, context.ActiveSlot, baseWeight);
        }

        public static bool TryResolveExactTarget(string rawText, PromptSlotSegmentSpec slot, ServerContext? server, out TSCommandRefTarget target) {
            return TryResolveExactTarget(
                rawText,
                TSPromptSlotMetadata.IsRecursiveCommandRef(slot),
                TSPromptSlotMetadata.AcceptsOptionalCommandRefPrefix(slot),
                server,
                out target);
        }

        public static bool TryResolveExactTarget(
            string rawText,
            bool recursive,
            bool acceptOptionalPrefix,
            ServerContext? server,
            out TSCommandRefTarget target) {
            var normalizedRaw = NormalizeInput(rawText, server, acceptOptionalPrefix, out _);
            if (normalizedRaw.Length == 0) {
                target = default!;
                return false;
            }

            foreach (var candidate in EnumerateTargets(recursive)) {
                if (candidate.InvocationPath.Equals(normalizedRaw, StringComparison.OrdinalIgnoreCase)) {
                    target = candidate;
                    return true;
                }
            }

            target = default!;
            return false;
        }

        public static CommandOutcome? ResolveAccessFailure(TSCommandRefTarget target, TSExecutionContext context) {
            if (target.LegacyEntry is TSCommandCatalogEntry legacyEntry) {
                return legacyEntry.CanRun(context.Executor)
                    ? null
                    : CommandOutcome.Error(GetString("You do not have access to this command."));
            }

            if (target.Bindings.Length == 0) {
                return null;
            }

            var sawDenied = false;
            foreach (var binding in target.Bindings) {
                var access = TShockCommandAccess.EvaluateAccess(binding, context);
                if (access == CommandAccessResult.Allowed) {
                    return null;
                }

                if (access == CommandAccessResult.Denied) {
                    sawDenied = true;
                }
            }

            return sawDenied
                ? CommandOutcome.Error(GetString("You do not have access to this command."))
                : CommandOutcome.Error(GetString("This command is not available from the current execution context."));
        }

        public static IReadOnlyList<string> BuildUsageLines(TSCommandRefTarget target, string commandSpecifier) {
            if (target.Root is null) {
                return [];
            }

            var actions = target.Bindings
                .Select(static binding => binding.Action)
                .DistinctBy(static action => action.Method)
                .ToArray();
            if (actions.Length == 0 && target.Action is not null) {
                actions = [target.Action];
            }

            return [.. actions
                .Select(action => BuildUsageLine(target.Root, action, commandSpecifier))
                .Where(static usage => !string.IsNullOrWhiteSpace(usage))];
        }

        public static bool TryCreateNestedPrompt(PromptParamCandidateContext context, [NotNullWhen(true)] out PromptSemanticSpec? prompt) {
            prompt = default;
            if (context.ActiveAlternative.Metadata is not CommandPromptAlternativeMetadata metadata) {
                return false;
            }

            var alternatives = BuildNestedPromptAlternatives(metadata.EndpointId, context.Server, context.RawInputText);
            if (alternatives.Count == 0) {
                return false;
            }
            prompt = BuildPromptSpec(context.Server, alternatives);
            return true;
        }

        public static bool TryResolveInvocationTarget(string rawText, TSExecutionContext context, out TSCommandRefTarget target) {
            var endpointId = TSCommandBridge.ResolveEndpointId(context.Executor);
            var request = TSCommandBridge.CreateDispatchRequest(
                context.Executor,
                endpointId,
                rawText,
                TSCommandBridge.IsSilentInvocation(rawText));
            if (!CommandDispatchCoordinator.TryCreateExecutionRequest(request, out var executionRequest)
                || !TryResolveRootTarget(endpointId, executionRequest.InvokedRoot, out target, out var rootBinding)) {
                target = default!;
                return false;
            }

            if (executionRequest.RawArguments.Length == 0 || target.LegacyEntry is not null || rootBinding is null) {
                return true;
            }

            var preview = CommandEndpointDispatcher.PreviewMatches(rootBinding, executionRequest);
            if (preview.Candidates.Length > 0
                && preview.Candidates[0].Binding is TShockEndpointActionBinding matchedBinding) {
                target = CreateActionTarget(rootBinding, rootBinding.Actions
                    .OfType<TShockEndpointActionBinding>()
                    .Where(action => action.Action.Method == matchedBinding.Action.Method));
                return true;
            }

            return TryResolvePathTarget(rootBinding, executionRequest.RawArguments, out target);
        }

        private static PromptSemanticSpec BuildPromptSpec(
            ServerContext? server,
            IReadOnlyList<PromptAlternativeSpec> alternatives) {
            return new PromptSemanticSpec {
                StatusLabel = "cmd",
                LiteralExpectationLabel = "command",
                ModifierLabel = "flag",
                OverflowExpectationLabel = "no more args",
                ActivationPrefixes = [.. ResolveRegisteredPrefixes(server)
                    .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
                    .Select(static prefix => prefix.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)],
                Alternatives = [.. alternatives
                    .Where(static alternative => alternative.Segments.Length > 0)
                    .OrderBy(static alternative => alternative.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static alternative => alternative.DisplayGroupKey, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static alternative => alternative.AlternativeId, StringComparer.OrdinalIgnoreCase)],
            };
        }

        private static List<PromptAlternativeSpec> BuildNestedPromptAlternatives(
            CommandEndpointId endpointId,
            ServerContext? server,
            string rawText) {
            var parse = CommandLineLexer.ParseCommandText(rawText, ResolveRegisteredPrefixes(server));
            if (parse.Tokens.Length == 0) {
                return BuildRootAlternatives(endpointId, TSCommandBridge.GetRegisteredCommandCatalog());
            }

            var rootName = parse.CommandName;
            if (TryFindEndpointRoot(endpointId, rootName, out var rootBinding)) {
                List<PromptAlternativeSpec> alternatives = [
                    .. BuildCatalogEntryAlternatives(endpointId, BuildCatalogEntry(rootBinding)),
                ];
                alternatives.AddRange(CommandPromptProjection.BuildAlternatives(
                    endpointId,
                    rootBinding.Root,
                    rootBinding.Actions.Select(static action => action.Action)));
                return alternatives;
            }

            if (TSCommandBridge.FindRegisteredCommand(rootName) is TSCommandCatalogEntry legacyEntry) {
                return [.. BuildCatalogEntryAlternatives(endpointId, legacyEntry)];
            }

            return BuildRootAlternatives(endpointId, TSCommandBridge.GetRegisteredCommandCatalog());
        }

        private static List<PromptAlternativeSpec> BuildRootAlternatives(
            CommandEndpointId endpointId,
            IReadOnlyList<TSCommandCatalogEntry> entries) {
            List<PromptAlternativeSpec> alternatives = [];
            foreach (var entry in entries.OrderBy(static entry => entry.PrimaryName, StringComparer.OrdinalIgnoreCase)) {
                alternatives.AddRange(BuildCatalogEntryAlternatives(endpointId, entry));
            }

            return alternatives;
        }

        private static IEnumerable<PromptAlternativeSpec> BuildCatalogEntryAlternatives(
            CommandEndpointId endpointId,
            TSCommandCatalogEntry entry) {
            foreach (var invocationName in EnumerateCatalogEntryNames(entry)) {
                yield return new PromptAlternativeSpec {
                    AlternativeId = $"command-root:{endpointId.Value}:{entry.PrimaryName}:{invocationName}",
                    DisplayGroupKey = $"command:{endpointId.Value}:{entry.PrimaryName}",
                    Title = invocationName,
                    ResultDisplayText = entry.PrimaryName,
                    Summary = string.IsNullOrWhiteSpace(entry.HelpText)
                        ? string.Join(Environment.NewLine, entry.HelpLines)
                        : entry.HelpText,
                    Metadata = new CommandPromptAlternativeMetadata {
                        EndpointId = endpointId,
                        RootName = entry.PrimaryName,
                        InvocationName = invocationName,
                        CanonicalCommand = entry.PrimaryName,
                        PathSegments = [],
                        ActionMethod = null,
                        PromptRouteGuards = [],
                    },
                    OverflowBehavior = PromptOverflowBehavior.Error,
                    Segments = [
                        new PromptLiteralSegmentSpec {
                            Name = "command",
                            Value = invocationName,
                            HighlightStyleId = PromptStyleKeys.SyntaxKeyword,
                        },
                    ],
                };
            }
        }

        private static IEnumerable<string> EnumerateCatalogEntryNames(TSCommandCatalogEntry entry) {
            yield return entry.PrimaryName;
            foreach (var alias in entry.Aliases
                .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(alias => !alias.Equals(entry.PrimaryName, StringComparison.OrdinalIgnoreCase))) {
                yield return alias;
            }
        }

        private static bool TryResolveRootTarget(
            CommandEndpointId endpointId,
            string invokedRoot,
            out TSCommandRefTarget target,
            out CommandEndpointRootBinding? rootBinding) {
            rootBinding = null;
            var catalog = CommandSystem.GetEndpointCatalog();
            var candidateRoot = catalog.FindRoot(endpointId, invokedRoot);
            if (candidateRoot is not null && TShockEndpointBindingFactory.IsTShockRoot(candidateRoot)) {
                rootBinding = candidateRoot;
                target = new TSCommandRefTarget(
                    invokedRoot,
                    candidateRoot.Root.RootName,
                    LegacyEntry: null,
                    Root: candidateRoot.Root,
                    Action: null,
                    Bindings: [.. candidateRoot.Actions.OfType<TShockEndpointActionBinding>()]);
                return true;
            }

            var legacyEntry = TSCommandBridge.FindRegisteredCommand(invokedRoot);
            if (legacyEntry is not null) {
                target = new TSCommandRefTarget(
                    invokedRoot,
                    legacyEntry.PrimaryName,
                    legacyEntry,
                    Root: null,
                    Action: null,
                    Bindings: []);
                return true;
            }

            target = default!;
            return false;
        }

        private static bool TryFindEndpointRoot(
            CommandEndpointId endpointId,
            string invokedRoot,
            out CommandEndpointRootBinding rootBinding) {
            rootBinding = CommandSystem.GetEndpointCatalog().FindRoot(endpointId, invokedRoot)!;
            return rootBinding is not null && TShockEndpointBindingFactory.IsTShockRoot(rootBinding);
        }

        private static TSCommandCatalogEntry BuildCatalogEntry(CommandEndpointRootBinding rootBinding) {
            return new TSCommandCatalogEntry {
                PrimaryName = rootBinding.Root.RootName,
                Aliases = rootBinding.Root.Aliases,
                HelpText = rootBinding.Root.Summary,
                HelpLines = [],
                IsLegacy = false,
            };
        }

        private static bool TryResolvePathTarget(
            CommandEndpointRootBinding rootBinding,
            ImmutableArray<string> rawArguments,
            out TSCommandRefTarget target) {
            List<(string CanonicalPath, ImmutableArray<TShockEndpointActionBinding> Bindings, int MatchedPathLength)> matches = [];
            foreach (var group in rootBinding.Actions
                .OfType<TShockEndpointActionBinding>()
                .GroupBy(action => string.Join(' ', action.Action.PathSegments), StringComparer.OrdinalIgnoreCase)) {
                var bindings = group.ToImmutableArray();
                var matchedPathLength = bindings
                    .Select(binding => ResolveMatchedPathLength(binding.Action, rawArguments))
                    .DefaultIfEmpty(0)
                    .Max();
                if (matchedPathLength <= 0) {
                    continue;
                }

                var canonicalPath = $"{rootBinding.Root.RootName} {string.Join(' ', bindings[0].Action.PathSegments)}".Trim();
                matches.Add((canonicalPath, bindings, matchedPathLength));
            }

            if (matches.Count == 0) {
                target = default!;
                return false;
            }

            var best = matches
                .OrderByDescending(static match => match.MatchedPathLength)
                .ThenBy(static match => match.CanonicalPath, StringComparer.OrdinalIgnoreCase)
                .First();
            target = CreateActionTarget(rootBinding, best.Bindings);
            return true;
        }

        private static int ResolveMatchedPathLength(CommandActionDefinition action, ImmutableArray<string> rawArguments) {
            return EnumerateActionPaths(action)
                .Where(path => MatchesPath(rawArguments, path))
                .Select(static path => path.Length)
                .DefaultIfEmpty(0)
                .Max();
        }

        private static bool MatchesPath(ImmutableArray<string> rawArguments, ImmutableArray<string> path) {
            if (path.Length == 0 || rawArguments.Length < path.Length) {
                return false;
            }

            for (var index = 0; index < path.Length; index++) {
                if (!rawArguments[index].Equals(path[index], StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }
            }

            return true;
        }

        private static TSCommandRefTarget CreateActionTarget(
            CommandEndpointRootBinding rootBinding,
            IEnumerable<TShockEndpointActionBinding> bindings) {
            var actionBindings = bindings.ToImmutableArray();
            var primaryAction = actionBindings[0].Action;
            var canonicalPath = $"{rootBinding.Root.RootName} {string.Join(' ', primaryAction.PathSegments)}".Trim();
            return new TSCommandRefTarget(
                canonicalPath,
                canonicalPath,
                LegacyEntry: null,
                Root: rootBinding.Root,
                Action: primaryAction,
                Bindings: actionBindings);
        }

        private static bool TryResolveBoundTarget(
            IReadOnlyList<string> tokens,
            CommandRefAttribute attribute,
            out TSCommandRefTarget target,
            out int consumedTokens) {
            target = default!;
            consumedTokens = 0;

            var normalizedTokens = NormalizeBoundTokens(tokens, attribute.AcceptOptionalPrefix);
            if (normalizedTokens.Count == 0) {
                return false;
            }

            foreach (var candidate in EnumerateTargets(attribute.Recursive)
                .OrderByDescending(static candidate => SplitSegments(candidate.InvocationPath).Count)
                .ThenBy(static candidate => candidate.InvocationPath, StringComparer.OrdinalIgnoreCase)) {
                var candidateSegments = SplitSegments(candidate.InvocationPath);
                if (candidateSegments.Count == 0 || candidateSegments.Count > normalizedTokens.Count) {
                    continue;
                }

                var matches = true;
                for (var index = 0; index < candidateSegments.Count; index++) {
                    if (!candidateSegments[index].Equals(normalizedTokens[index], StringComparison.OrdinalIgnoreCase)) {
                        matches = false;
                        break;
                    }
                }

                if (!matches) {
                    continue;
                }

                target = candidate;
                consumedTokens = candidateSegments.Count;
                return true;
            }

            return false;
        }

        private static IEnumerable<TSCommandRefTarget> EnumerateTargets(PromptSlotSegmentSpec slot) {
            return EnumerateTargets(TSPromptSlotMetadata.IsRecursiveCommandRef(slot));
        }

        private static IEnumerable<TSCommandRefTarget> EnumerateTargets(bool recursive) {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in TSCommandBridge.GetRegisteredCommandCatalog()) {
                if (seen.Add(entry.PrimaryName)) {
                    yield return new TSCommandRefTarget(entry.PrimaryName, entry.PrimaryName, entry, null, null, []);
                }

                foreach (var alias in entry.Aliases) {
                    if (seen.Add(alias)) {
                        yield return new TSCommandRefTarget(alias, entry.PrimaryName, entry, null, null, []);
                    }
                }
            }

            foreach (var root in EnumerateDeclarativeRoots()) {
                var rootBindings = root.Actions.SelectMany(static action => action.Bindings).ToImmutableArray();
                foreach (var invocationName in EnumerateRootNames(root)) {
                    if (seen.Add(invocationName)) {
                        yield return new TSCommandRefTarget(invocationName, root.Root.RootName, null, root.Root, null, rootBindings);
                    }

                    if (!recursive) {
                        continue;
                    }

                    foreach (var action in root.Actions) {
                        foreach (var actionPath in EnumerateActionPaths(action.Action)) {
                            if (actionPath.Length == 0) {
                                continue;
                            }

                            var invocationPath = $"{invocationName} {string.Join(' ', actionPath)}";
                            if (!seen.Add(invocationPath)) {
                                continue;
                            }

                            var canonicalPath = $"{root.Root.RootName} {string.Join(' ', action.Action.PathSegments)}".Trim();
                            yield return new TSCommandRefTarget(invocationPath, canonicalPath, null, root.Root, action.Action, action.Bindings);
                        }
                    }
                }
            }
        }

        private static IEnumerable<DeclarativeRootRef> EnumerateDeclarativeRoots() {
            Dictionary<DeclarativeRootKey, (CommandRootDefinition Root, Dictionary<MethodInfo, List<TShockEndpointActionBinding>> Actions)> grouped = [];
            foreach (var root in CommandSystem.GetEndpointCatalog().Roots.Where(TShockEndpointBindingFactory.IsTShockRoot)) {
                var key = new DeclarativeRootKey(root.Root.SourceName, root.Root.RootName, root.Root.ControllerType);
                if (!grouped.TryGetValue(key, out var entry)) {
                    entry = (root.Root, []);
                    grouped[key] = entry;
                }

                foreach (var action in root.Actions.OfType<TShockEndpointActionBinding>()) {
                    if (!entry.Actions.TryGetValue(action.Action.Method, out var bindings)) {
                        bindings = [];
                        entry.Actions[action.Action.Method] = bindings;
                    }

                    bindings.Add(action);
                }
            }

            foreach (var (root, actions) in grouped.Values) {
                yield return new DeclarativeRootRef(
                    root,
                    [.. actions.Values
                        .Select(static bindings => new DeclarativeActionRef(bindings[0].Action, [.. bindings]))
                        .OrderBy(static action => string.Join(' ', action.Action.PathSegments), StringComparer.OrdinalIgnoreCase)]);
            }
        }

        private static IEnumerable<string> EnumerateRootNames(DeclarativeRootRef root) {
            yield return root.Root.RootName;
            foreach (var alias in root.Root.Aliases
                .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(alias => !alias.Equals(root.Root.RootName, StringComparison.OrdinalIgnoreCase))) {
                yield return alias;
            }
        }

        private static IEnumerable<ImmutableArray<string>> EnumerateActionPaths(CommandActionDefinition action) {
            yield return action.PathSegments;
            foreach (var alias in action.PathAliases) {
                yield return alias;
            }
        }

        private static IReadOnlyList<string> NormalizeBoundTokens(IReadOnlyList<string> tokens, bool acceptOptionalPrefix) {
            if (tokens.Count == 0) {
                return [];
            }

            List<string> normalized = new(tokens.Count);
            for (var index = 0; index < tokens.Count; index++) {
                var token = tokens[index]?.Trim() ?? string.Empty;
                if (token.Length == 0) {
                    continue;
                }

                normalized.Add(index == 0 && acceptOptionalPrefix
                    ? StripRegisteredPrefix(token, server: null)
                    : token);
            }

            return normalized;
        }

        private static int? ResolvePromptCandidateWeight(
            string candidateText,
            string rawText,
            ServerContext? server,
            PromptSlotSegmentSpec slot,
            int baseWeight = 120) {
            var normalizedRaw = NormalizeInput(
                rawText,
                server,
                TSPromptSlotMetadata.AcceptsOptionalCommandRefPrefix(slot),
                out var endsWithSeparator);
            var candidateSegments = SplitSegments(StripRegisteredPrefix(candidateText, server));
            if (candidateSegments.Count == 0) {
                return null;
            }

            if (normalizedRaw.Length == 0) {
                int? emptyWeight = candidateSegments.Count == 1
                    ? baseWeight
                    : null;
                return emptyWeight;
            }

            var rawSegments = SplitSegments(normalizedRaw);
            var committedCount = endsWithSeparator
                ? rawSegments.Count
                : Math.Max(0, rawSegments.Count - 1);
            var livePrefix = endsWithSeparator || rawSegments.Count == 0
                ? string.Empty
                : rawSegments[^1];
            if (candidateSegments.Count != committedCount + 1) {
                return null;
            }

            for (var index = 0; index < committedCount; index++) {
                if (!candidateSegments[index].Equals(rawSegments[index], StringComparison.OrdinalIgnoreCase)) {
                    return null;
                }
            }

            var targetSegment = candidateSegments[^1];
            if (livePrefix.Length == 0) {
                var continuationWeight = baseWeight + 32;
                return continuationWeight;
            }

            if (!targetSegment.StartsWith(livePrefix, StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            if (targetSegment.Equals(livePrefix, StringComparison.OrdinalIgnoreCase)) {
                var exactWeight = baseWeight + 1000;
                return exactWeight;
            }

            var remainingLength = Math.Max(0, targetSegment.Length - livePrefix.Length);
            var prefixWeight = baseWeight + Math.Max(1, 256 - Math.Min(255, remainingLength));

            return prefixWeight;
        }

        private static string NormalizeInput(
            string rawText,
            ServerContext? server,
            bool acceptOptionalPrefix,
            out bool endsWithSeparator) {
            endsWithSeparator = rawText.Length > 0 && char.IsWhiteSpace(rawText[^1]);
            var normalized = rawText.Trim();
            if (normalized.Length == 0) {
                return string.Empty;
            }

            return acceptOptionalPrefix
                ? StripRegisteredPrefix(normalized, server)
                : normalized;
        }

        private static string FormatOutputPath(string invocationPath, ServerContext? server, string rawText, bool insertPrefix) {
            var normalized = StripRegisteredPrefix(invocationPath, server);
            var prefix = ResolveOutputPrefix(server, rawText, insertPrefix);
            return prefix.Length == 0
                ? normalized
                : prefix + normalized;
        }

        private static string ResolveOutputPrefix(ServerContext? server, string rawText, bool insertPrefix) {
            if (TryResolveRegisteredPrefix(rawText, server, out var prefix)) {
                return prefix;
            }

            if (!insertPrefix) {
                return string.Empty;
            }

            return ResolveRegisteredPrefixes(server).FirstOrDefault()
                ?? Commands.Specifier;
        }

        private static string StripRegisteredPrefix(string rawText, ServerContext? server) {
            return TryResolveRegisteredPrefix(rawText, server, out var prefix)
                ? rawText[prefix.Length..].TrimStart()
                : rawText?.Trim() ?? string.Empty;
        }

        private static bool TryResolveRegisteredPrefix(string rawText, ServerContext? server, out string prefix) {
            foreach (var candidate in ResolveRegisteredPrefixes(server)
                .OrderByDescending(static candidate => candidate.Length)) {
                if (rawText.StartsWith(candidate, StringComparison.Ordinal)) {
                    prefix = candidate;
                    return true;
                }
            }

            prefix = string.Empty;
            return false;
        }

        private static IReadOnlyList<string> ResolveRegisteredPrefixes(ServerContext? server) {
            var prefixes = PromptRegistry.GetRegisteredCommandPrefixes(server);
            if (prefixes.Count > 0) {
                return prefixes;
            }

            return TSCommandBridge.ResolveCommandPrefixes();
        }

        private static string ResolveRawInvocation(CommandInvocationContext context, int userIndex) {
            if (userIndex >= context.UserArgumentTokens.Length) {
                return string.Join(' ', context.UserArguments.Skip(userIndex));
            }

            var firstToken = context.UserArgumentTokens[userIndex];
            var lastToken = context.UserArgumentTokens[^1];
            var startIndex = firstToken.Quoted
                && firstToken.StartIndex > 0
                && context.RawInput[firstToken.StartIndex - 1] == '"'
                    ? firstToken.StartIndex - 1
                    : firstToken.StartIndex;
            var endIndex = lastToken.Quoted
                && lastToken.EndIndex < context.RawInput.Length
                && context.RawInput[lastToken.EndIndex] == '"'
                    ? lastToken.EndIndex + 1
                    : lastToken.EndIndex;
            if (startIndex < 0 || endIndex > context.RawInput.Length || endIndex <= startIndex) {
                return string.Join(' ', context.UserArguments.Skip(userIndex));
            }

            return context.RawInput[startIndex..endIndex];
        }

        private static IReadOnlyList<string> SplitSegments(string text) {
            return string.IsNullOrWhiteSpace(text)
                ? []
                : text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static PromptParamExplainResult Resolved(string displayText) => new(PromptParamExplainState.Resolved, displayText);

        private static PromptParamExplainResult Invalid() => new(PromptParamExplainState.Invalid, "invalid");

        private static PromptParamExplainResult Ambiguous(IEnumerable<string> displayValues) {
            var candidates = displayValues
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (candidates.Length == 0) {
                return Invalid();
            }

            if (candidates.Length == 1) {
                return Resolved(candidates[0]);
            }

            var preview = string.Join(", ", candidates.Take(3));
            if (candidates.Length > 3) {
                preview += ", ...";
            }

            return new PromptParamExplainResult(PromptParamExplainState.Ambiguous, "ambiguous: " + preview);
        }

        private static string BuildUsageLine(
            CommandRootDefinition root,
            CommandActionDefinition action,
            string commandSpecifier) {
            var subCommand = action.PathSegments.Length == 0 ? string.Empty : string.Join(' ', action.PathSegments) + " ";
            var parameters = string.Join(' ', action.Parameters.Select(FormatParameter));
            var flags = FormatFlags(action.FlagsParameter);
            var usage = $"{commandSpecifier}{root.RootName} {subCommand}{parameters}".TrimEnd();
            if (!string.IsNullOrWhiteSpace(flags)) {
                usage = string.IsNullOrWhiteSpace(parameters)
                    ? $"{usage} {flags}".TrimEnd()
                    : $"{usage} {flags}";
            }

            return string.IsNullOrWhiteSpace(action.Summary)
                ? usage
                : $"{usage} - {action.Summary}";
        }

        private static string FormatParameter(CommandParamDefinition parameter) {
            var parameterName = parameter.Name;
            if (parameter.AcceptedSpecialTokens.Length > 0) {
                parameterName = string.Join('|', [parameterName, .. parameter.AcceptedSpecialTokens]);
            }

            var token = parameter.Variadic
                ? $"<{parameterName}...>"
                : $"<{parameterName}>";
            return parameter.Optional ? $"[{token}]" : token;
        }

        private static string FormatFlags(CommandFlagsParameterDefinition? flagsParameter) {
            if (flagsParameter is null || flagsParameter.Flags.Length == 0) {
                return string.Empty;
            }

            return string.Join(' ', flagsParameter.Flags.Select(static flag => $"[{flag.CanonicalToken}]"));
        }
    }
}
