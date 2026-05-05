using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Terraria;
using Terraria.ID;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding;
using UnifierTSL.Localization.Terraria;
using UnifierTSL.Servers;

namespace UnifierTSL.Commanding.Prompting;

public sealed class CommandPromptParamKeys
{
    private CommandPromptParamKeys() { }

    public static SemanticKey PlayerRef { get; } = new("unifier.player-ref", "player");
    public static SemanticKey ServerRef { get; } = new("unifier.server-ref", "server");
    public static SemanticKey ItemRef { get; } = new("unifier.item-ref", "item");
    public static SemanticKey BuffRef { get; } = new("unifier.buff-ref", "buff");
    public static SemanticKey PrefixRef { get; } = new("unifier.prefix-ref", "prefix");
}

public static partial class CommandPromptCommonObjects
{
    private static readonly Lazy<ImmutableArray<int>> ItemMaxStacksCache = new(BuildItemMaxStacks);
    private static readonly Lazy<BuffCatalog> BuffCatalogCache = new(BuildBuffCatalogCore);
    private static readonly Lazy<PrefixCatalog> PrefixCatalogCache = new(BuildPrefixCatalogCore);
    private static readonly Lazy<SampleServer> ItemMetadataSampleServer = new(static () => new SampleServer());

    internal static IReadOnlyDictionary<SemanticKey, IParamValueExplainer> ParameterExplainers { get; } =
        new Dictionary<SemanticKey, IParamValueExplainer> {
            [CommandPromptParamKeys.PlayerRef] = new DelegateParamExplainer(
                ExplainPlayer,
                GetPlayerRevision),
            [CommandPromptParamKeys.ServerRef] = new DelegateParamExplainer(
                ExplainServer,
                GetServerRevision),
            [CommandPromptParamKeys.ItemRef] = new DelegateParamExplainer(
                ExplainItem,
                static _ => 0),
            [CommandPromptParamKeys.BuffRef] = new DelegateParamExplainer(
                ExplainBuff,
                static _ => 0),
            [CommandPromptParamKeys.PrefixRef] = new DelegateParamExplainer(
                ExplainPrefix,
                static _ => 0),
        };

    internal static IReadOnlyDictionary<SemanticKey, IParamValueCandidateProvider> ParameterCandidateProviders { get; } =
        new Dictionary<SemanticKey, IParamValueCandidateProvider> {
            [CommandPromptParamKeys.PlayerRef] = new DelegateParamCandidateProvider(
                context => GetPlayerCandidates(context.Server),
                GetPlayerCandidateRevision),
            [CommandPromptParamKeys.ServerRef] = new DelegateParamCandidateProvider(
                GetServerCandidates,
                GetServerCandidateRevision),
            [CommandPromptParamKeys.ItemRef] = new DelegateParamCandidateProvider(
                GetItemCandidates,
                static _ => 0),
            [CommandPromptParamKeys.BuffRef] = new DelegateParamCandidateProvider(
                static _ => GetBuffCandidates(),
                static _ => 0),
            [CommandPromptParamKeys.PrefixRef] = new DelegateParamCandidateProvider(
                static _ => GetPrefixCandidates(),
                static _ => 0),
        };

    public static IReadOnlyList<string> GetPlayerCandidates(ServerContext? server = null) {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in EnumeratePlayers(server)) {
            names.Add(candidate.Name);
        }

        return [.. names.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
    }

    public static IReadOnlyList<string> GetServerCandidates() {
        return [.. EnumerateRunningServers()
            .Select(static server => server.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
    }

    private static IReadOnlyList<string> GetServerCandidates(PromptParamCandidateContext context) {
        var excludedServer = context.ActiveSlot.ExcludeCurrentContextFromCandidates
            ? context.Server
            : null;

        return [.. EnumerateRunningServers()
            .Where(candidate => excludedServer is null || candidate.Server != excludedServer)
            .Select(static candidate => candidate.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
    }

    public static IReadOnlyList<string> GetItemCandidates() {
        return TerrariaItemNameLookup.GetCandidates();
    }

    private static IReadOnlyList<string> GetItemCandidates(PromptParamCandidateContext context) {
        var rawText = context.RawText ?? string.Empty;
        return rawText.Length == 0
            ? TerrariaItemNameLookup.GetCandidates()
            : TerrariaItemNameLookup.GetCandidatesByPrefix(rawText);
    }

    public static string? GetItemDisplayName(int itemId) {
        return TerrariaItemNameLookup.GetDisplayName(itemId);
    }

    internal static int? GetItemMaxStack(int itemId) {
        var maxStacks = ItemMaxStacksCache.Value;
        return itemId > 0
            && itemId < maxStacks.Length
            && maxStacks[itemId] > 0
                ? maxStacks[itemId]
                : null;
    }

    public static IReadOnlyList<int> ResolveItemIds(string search) {
        if (string.IsNullOrWhiteSpace(search)) {
            return [];
        }

        var normalizedSearch = search.Trim();
        if (TryParseItemTag(normalizedSearch, out var tagItemId)) {
            return [tagItemId];
        }

        var exactMatches = TerrariaItemNameLookup.GetIdsByExactName(normalizedSearch);
        if (exactMatches.Count > 0) {
            return exactMatches;
        }

        var startsWithMatches = TerrariaItemNameLookup.GetIdsByPrefix(normalizedSearch);
        if (startsWithMatches.Count == 1) {
            return startsWithMatches;
        }

        var containsMatches = TerrariaItemNameLookup.GetIdsByContains(normalizedSearch);
        return [.. startsWithMatches
            .Concat(containsMatches)
            .Distinct()
            .OrderBy(static id => id)];
    }

    public static IReadOnlyList<int> GetItemIdsByExactName(string search) {
        return TerrariaItemNameLookup.GetIdsByExactName(search);
    }

    public static IReadOnlyList<int> ResolveItemIdsByPrefix(string prefix) {
        return TerrariaItemNameLookup.GetIdsByPrefix(prefix);
    }

    public static IReadOnlyList<int> ResolveItemIdsByContains(string search) {
        return TerrariaItemNameLookup.GetIdsByContains(search);
    }

    public static IReadOnlyList<string> GetBuffCandidates() {
        return BuffCatalogCache.Value.Candidates;
    }

    public static string? GetBuffDisplayName(int buffId) {
        return BuffCatalogCache.Value.DisplayNamesById.TryGetValue(buffId, out var displayName)
            ? displayName
            : null;
    }

    public static IReadOnlyList<int> ResolveBuffIds(string search) {
        if (string.IsNullOrWhiteSpace(search)) {
            return [];
        }

        var catalog = BuffCatalogCache.Value;
        var normalizedSearch = search.Trim();

        var exactMatches = ResolveBuffIds(catalog, normalizedSearch, static (candidate, value) =>
            candidate.Equals(value, StringComparison.InvariantCultureIgnoreCase));
        if (exactMatches.Count > 0) {
            return exactMatches;
        }

        var startsWithMatches = ResolveBuffIds(catalog, normalizedSearch, static (candidate, value) =>
            candidate.StartsWith(value, StringComparison.InvariantCultureIgnoreCase));
        if (startsWithMatches.Count == 1) {
            return startsWithMatches;
        }

        var containsMatches = ResolveBuffIds(catalog, normalizedSearch, static (candidate, value) =>
            candidate.Contains(value, StringComparison.InvariantCultureIgnoreCase));
        return [.. startsWithMatches
            .Concat(containsMatches)
            .Distinct()
            .OrderBy(static id => id)];
    }

    public static IReadOnlyList<string> GetPrefixCandidates() {
        return PrefixCatalogCache.Value.Candidates;
    }

    public static string? GetPrefixDisplayName(int prefixId) {
        return PrefixCatalogCache.Value.DisplayNamesById.TryGetValue(prefixId, out var displayName)
            ? displayName
            : null;
    }

    public static IReadOnlyList<int> ResolvePrefixIds(string search) {
        if (string.IsNullOrWhiteSpace(search)) {
            return [];
        }

        var catalog = PrefixCatalogCache.Value;
        var normalizedSearch = search.Trim();

        if (int.TryParse(normalizedSearch, out var prefixId)
            && catalog.DisplayNamesById.ContainsKey(prefixId)) {
            return [prefixId];
        }

        var exactMatches = ResolvePrefixIds(catalog, normalizedSearch, static (candidate, value) =>
            candidate.Equals(value, StringComparison.InvariantCultureIgnoreCase));
        if (exactMatches.Count > 0) {
            return exactMatches;
        }

        var startsWithMatches = ResolvePrefixIds(catalog, normalizedSearch, static (candidate, value) =>
            candidate.StartsWith(value, StringComparison.InvariantCultureIgnoreCase));
        if (startsWithMatches.Count == 1) {
            return startsWithMatches;
        }

        var containsMatches = ResolvePrefixIds(catalog, normalizedSearch, static (candidate, value) =>
            candidate.Contains(value, StringComparison.InvariantCultureIgnoreCase));
        return [.. startsWithMatches
            .Concat(containsMatches)
            .Distinct()
            .OrderBy(static id => id)];
    }

    private static IEnumerable<PlayerCandidate> EnumeratePlayers(ServerContext? server) {
        for (var i = 0; i < UnifiedServerCoordinator.globalClients.Length; i++) {
            var client = UnifiedServerCoordinator.globalClients[i];
            if (!client.IsActive) {
                continue;
            }

            var currentServer = UnifiedServerCoordinator.GetClientCurrentlyServer(i);
            if (server is not null && currentServer != server) {
                continue;
            }

            var name = client.Name;
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }

            yield return new PlayerCandidate(i, currentServer, name.Trim());
        }
    }

    private static IEnumerable<ServerCandidate> EnumerateRunningServers() {
        var servers = UnifiedServerCoordinator.Servers;
        for (var i = 0; i < servers.Length; i++) {
            var server = servers[i];
            if (!server.IsRunning || string.IsNullOrWhiteSpace(server.Name)) {
                continue;
            }

            yield return new ServerCandidate(i + 1, server, server.Name.Trim());
        }
    }

    private static PrefixCatalog BuildPrefixCatalogCore() {
        HashSet<string> allNames = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<int, string> displayNames = [];
        Dictionary<string, HashSet<int>> idsByName = new(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < PrefixID.Count; i++) {
            AddPrefixName(i, Lang.prefix[i].Value, preferDisplayName: true);
            AddPrefixName(i, UnifierTSL.Localization.Terraria.EnglishLanguage.GetPrefixById(i), preferDisplayName: false);
        }

        return new PrefixCatalog(
            Candidates: [.. allNames.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)],
            DisplayNamesById: displayNames.ToImmutableDictionary(),
            IdsByName: idsByName.ToImmutableDictionary(
                static pair => pair.Key,
                static pair => pair.Value
                    .OrderBy(static id => id)
                    .ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase));

        void AddPrefixName(int prefixId, string? value, bool preferDisplayName) {
            if (string.IsNullOrWhiteSpace(value)) {
                return;
            }

            var normalized = value.Trim();
            allNames.Add(normalized);

            if (!idsByName.TryGetValue(normalized, out var ids)) {
                ids = [];
                idsByName[normalized] = ids;
            }

            ids.Add(prefixId);
            if (preferDisplayName || !displayNames.ContainsKey(prefixId)) {
                displayNames[prefixId] = normalized;
            }
        }
    }

    private static ImmutableArray<int> BuildItemMaxStacks() {
        var maxStacks = new int[ItemID.Count];
        var sampleServer = ItemMetadataSampleServer.Value;

        for (var i = 1; i < maxStacks.Length; i++) {
            Item item = new();
            item.netDefaults(sampleServer, i);
            maxStacks[i] = item.maxStack;
        }

        return [.. maxStacks];
    }

    private static BuffCatalog BuildBuffCatalogCore() {
        HashSet<string> allNames = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<int, string> displayNames = [];
        Dictionary<string, HashSet<int>> idsByName = new(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < BuffID.Count; i++) {
            AddBuffName(i, Lang.GetBuffName(i), preferDisplayName: true);
            AddBuffName(i, UnifierTSL.Localization.Terraria.EnglishLanguage.GetBuffNameById(i), preferDisplayName: false);
        }

        return new BuffCatalog(
            Candidates: [.. allNames.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)],
            DisplayNamesById: displayNames.ToImmutableDictionary(),
            IdsByName: idsByName.ToImmutableDictionary(
                static pair => pair.Key,
                static pair => pair.Value
                    .OrderBy(static id => id)
                    .ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase));

        void AddBuffName(int buffId, string? value, bool preferDisplayName) {
            if (string.IsNullOrWhiteSpace(value)) {
                return;
            }

            var normalized = value.Trim();
            allNames.Add(normalized);

            if (!idsByName.TryGetValue(normalized, out var ids)) {
                ids = [];
                idsByName[normalized] = ids;
            }

            ids.Add(buffId);
            if (preferDisplayName || !displayNames.ContainsKey(buffId)) {
                displayNames[buffId] = normalized;
            }
        }
    }

    private static PromptParamExplainResult ExplainPlayer(PromptParamExplainContext context) {
        var rawSearch = context.RawToken ?? string.Empty;
        var search = rawSearch.Trim();
        if (search.Length == 0) {
            return Invalid();
        }

        List<PlayerCandidate> candidates = [.. EnumeratePlayers(context.Server)];
        if (candidates.Count == 0) {
            return Invalid();
        }

        var continuationMatches = ResolveSemanticContinuationMatches(
            candidates,
            rawSearch,
            static player => player.Name);
        if (continuationMatches.Count == 1) {
            return Resolved(continuationMatches[0].Name);
        }

        if (continuationMatches.Count > 1) {
            return Ambiguous(continuationMatches.Select(static player => $"{player.Name}({player.ClientIndex})"));
        }

        var matches = FindMatchingPlayers(candidates, search);
        if (matches.Count == 0) {
            return Invalid();
        }

        if (matches.Count == 1) {
            return Resolved(matches[0].Name);
        }

        return Ambiguous(matches.Select(static player => $"{player.Name}({player.ClientIndex})"));
    }

    private static PromptParamExplainResult ExplainServer(PromptParamExplainContext context) {
        var rawSearch = context.RawToken ?? string.Empty;
        var search = rawSearch.Trim();
        if (search.Length == 0) {
            return Invalid();
        }

        List<ServerCandidate> candidates = [.. EnumerateRunningServers()];
        if (candidates.Count == 0) {
            return Invalid();
        }

        var continuationMatches = ResolveSemanticContinuationMatches(
            candidates,
            rawSearch,
            static server => server.Name);
        if (continuationMatches.Count == 1) {
            return Resolved(continuationMatches[0].Name);
        }

        if (continuationMatches.Count > 1) {
            return Ambiguous(continuationMatches.Select(static server => $"{server.Name}({server.Order})"));
        }

        var matches = FindMatchingServers(candidates, search);
        if (matches.Count == 0) {
            return Invalid();
        }

        if (matches.Count == 1) {
            return Resolved(matches[0].Name);
        }

        return Ambiguous(matches.Select(static server => $"{server.Name}({server.Order})"));
    }

    private static List<PlayerCandidate> FindMatchingPlayers(
        IReadOnlyList<PlayerCandidate> candidates,
        string search) {
        if (int.TryParse(search, out var clientIndex)) {
            List<PlayerCandidate> exactIndexMatches = [.. candidates
                .Where(player => player.ClientIndex == clientIndex)];
            if (exactIndexMatches.Count > 0) {
                return exactIndexMatches;
            }
        }

        List<PlayerCandidate> exactMatches = [.. candidates
            .Where(player => player.Name.Equals(search, StringComparison.OrdinalIgnoreCase))];
        if (exactMatches.Count > 0) {
            return exactMatches;
        }

        return [.. candidates
            .Where(player => player.Name.StartsWith(search, StringComparison.OrdinalIgnoreCase))];
    }

    private static List<ServerCandidate> FindMatchingServers(
        IReadOnlyList<ServerCandidate> candidates,
        string search) {
        if (int.TryParse(search, out var serverOrder)) {
            List<ServerCandidate> exactOrderMatches = [.. candidates
                .Where(server => server.Order == serverOrder)];
            if (exactOrderMatches.Count > 0) {
                return exactOrderMatches;
            }
        }

        List<ServerCandidate> exactMatches = [.. candidates
            .Where(server => server.Name.Equals(search, StringComparison.OrdinalIgnoreCase))];
        if (exactMatches.Count > 0) {
            return exactMatches;
        }

        return [.. candidates
            .Where(server => server.Name.StartsWith(search, StringComparison.OrdinalIgnoreCase))];
    }

    private static PromptParamExplainResult ExplainItem(PromptParamExplainContext context) {
        var rawSearch = context.RawToken ?? string.Empty;
        var search = rawSearch.Trim();
        if (search.Length == 0) {
            return Invalid();
        }

        var continuationMatches = ResolveSemanticContinuationMatches(
            TerrariaItemNameLookup.GetCandidates(),
            rawSearch,
            static candidate => candidate);
        if (continuationMatches.Count == 1) {
            return Resolved(continuationMatches[0]);
        }

        if (continuationMatches.Count > 1) {
            return Ambiguous(continuationMatches);
        }

        if (int.TryParse(search, out var itemId)
            && GetItemDisplayName(itemId) is string byIdDisplay) {
            return Resolved(byIdDisplay);
        }

        var matches = ResolveItemIds(search);
        if (matches.Count == 0) {
            return Invalid();
        }

        if (matches.Count == 1
            && GetItemDisplayName(matches[0]) is string displayName) {
            return Resolved(displayName);
        }

        return Ambiguous(matches
            .Select(GetItemDisplayName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))!);
    }

    private static PromptParamExplainResult ExplainBuff(PromptParamExplainContext context) {
        var rawSearch = context.RawToken ?? string.Empty;
        var search = rawSearch.Trim();
        if (search.Length == 0) {
            return Invalid();
        }

        var catalog = BuffCatalogCache.Value;
        var continuationMatches = ResolveSemanticContinuationMatches(
            catalog.Candidates,
            rawSearch,
            static candidate => candidate);
        if (continuationMatches.Count == 1) {
            return Resolved(continuationMatches[0]);
        }

        if (continuationMatches.Count > 1) {
            return Ambiguous(continuationMatches);
        }

        if (int.TryParse(search, out var buffId)
            && catalog.DisplayNamesById.TryGetValue(buffId, out var byIdDisplay)) {
            return Resolved(byIdDisplay);
        }

        var matches = ResolveBuffIds(search);
        if (matches.Count == 0) {
            return Invalid();
        }

        if (matches.Count == 1
            && catalog.DisplayNamesById.TryGetValue(matches[0], out var displayName)) {
            return Resolved(displayName);
        }

        return Ambiguous(matches
            .Select(id => catalog.DisplayNamesById.TryGetValue(id, out var name) ? name : null)
            .Where(static name => !string.IsNullOrWhiteSpace(name))!);
    }

    private static PromptParamExplainResult ExplainPrefix(PromptParamExplainContext context) {
        var rawSearch = context.RawToken ?? string.Empty;
        var search = rawSearch.Trim();
        if (search.Length == 0) {
            return Invalid();
        }

        var catalog = PrefixCatalogCache.Value;
        var continuationMatches = ResolveSemanticContinuationMatches(
            catalog.Candidates,
            rawSearch,
            static candidate => candidate);
        if (continuationMatches.Count == 1) {
            return Resolved(continuationMatches[0]);
        }

        if (continuationMatches.Count > 1) {
            return Ambiguous(continuationMatches);
        }

        if (int.TryParse(search, out var prefixId)
            && catalog.DisplayNamesById.TryGetValue(prefixId, out var byIdDisplay)) {
            return Resolved(byIdDisplay);
        }

        var matches = ResolvePrefixIds(search);
        if (matches.Count == 0) {
            return Invalid();
        }

        if (matches.Count == 1
            && catalog.DisplayNamesById.TryGetValue(matches[0], out var displayName)) {
            return Resolved(displayName);
        }

        return Ambiguous(matches
            .Select(id => catalog.DisplayNamesById.TryGetValue(id, out var name) ? name : null)
            .Where(static name => !string.IsNullOrWhiteSpace(name))!);
    }

    private static List<TCandidate> ResolveSemanticContinuationMatches<TCandidate>(
        IEnumerable<TCandidate> candidates,
        string rawSearch,
        Func<TCandidate, string> selector) {
        if (!HasSemanticContinuationSearch(rawSearch)) {
            return [];
        }

        return [.. candidates
            .Where(candidate => selector(candidate).StartsWith(rawSearch, StringComparison.OrdinalIgnoreCase))];
    }

    private static bool HasSemanticContinuationSearch(string rawSearch) {
        return !string.IsNullOrWhiteSpace(rawSearch)
            && rawSearch.Length > 0
            && char.IsWhiteSpace(rawSearch[^1])
            && rawSearch.Trim().Length > 0;
    }

    private static List<int> ResolveBuffIds(
        BuffCatalog catalog,
        string search,
        Func<string, string, bool> predicate) {
        return [.. catalog.IdsByName
            .Where(pair => predicate(pair.Key, search))
            .SelectMany(static pair => pair.Value)
            .Distinct()
            .OrderBy(static id => id)];
    }

    private static List<int> ResolvePrefixIds(
        PrefixCatalog catalog,
        string search,
        Func<string, string, bool> predicate) {
        return [.. catalog.IdsByName
            .Where(pair => predicate(pair.Key, search))
            .SelectMany(static pair => pair.Value)
            .Distinct()
            .OrderBy(static id => id)];
    }

    private static bool TryParseItemTag(string search, out int itemId) {
        var match = ItemTagMatch().Match(search);
        if (!match.Success
            || !int.TryParse(match.Groups["NetID"].Value, out itemId)
            || itemId < 1
            || itemId >= ItemID.Count) {
            itemId = 0;
            return false;
        }

        return true;
    }

    private static PromptParamExplainResult Resolved(string? displayText) {
        return string.IsNullOrWhiteSpace(displayText)
            ? Invalid()
            : new PromptParamExplainResult(PromptParamExplainState.Resolved, displayText.Trim());
    }

    private static PromptParamExplainResult Invalid()
        => new(PromptParamExplainState.Invalid, "invalid");

    private static PromptParamExplainResult Ambiguous(IEnumerable<string> displayValues) {
        List<string> candidates = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (var displayValue in displayValues) {
            if (string.IsNullOrWhiteSpace(displayValue)) {
                continue;
            }

            var normalized = displayValue.Trim();
            if (!seen.Add(normalized)) {
                continue;
            }

            candidates.Add(normalized);
        }

        if (candidates.Count == 0) {
            return Invalid();
        }

        if (candidates.Count == 1) {
            return Resolved(candidates[0]);
        }

        var preview = string.Join(", ", candidates.Take(3));
        if (candidates.Count > 3) {
            preview += ", ...";
        }

        return new PromptParamExplainResult(
            PromptParamExplainState.Ambiguous,
            "ambiguous: " + preview);
    }

    private static long GetPlayerRevision(PromptParamExplainContext context) {
        HashCode hash = new();
        foreach (var candidate in EnumeratePlayers(context.Server)) {
            hash.Add(candidate.ClientIndex);
            hash.Add(candidate.Name, StringComparer.OrdinalIgnoreCase);
            hash.Add(candidate.Server?.UniqueId ?? Guid.Empty);
        }

        return hash.ToHashCode();
    }

    private static long GetPlayerCandidateRevision(PromptParamCandidateContext context) {
        HashCode hash = new();
        foreach (var candidate in EnumeratePlayers(context.Server)) {
            hash.Add(candidate.ClientIndex);
            hash.Add(candidate.Name, StringComparer.OrdinalIgnoreCase);
            hash.Add(candidate.Server?.UniqueId ?? Guid.Empty);
        }

        return hash.ToHashCode();
    }

    private static long GetServerRevision(PromptParamExplainContext context) {
        _ = context;
        HashCode hash = new();
        foreach (var candidate in EnumerateRunningServers()) {
            hash.Add(candidate.Order);
            hash.Add(candidate.Name, StringComparer.OrdinalIgnoreCase);
            hash.Add(candidate.Server.UniqueId);
        }

        return hash.ToHashCode();
    }

    private static long GetServerCandidateRevision(PromptParamCandidateContext context) {
        HashCode hash = new();
        foreach (var candidate in EnumerateRunningServers()) {
            hash.Add(candidate.Order);
            hash.Add(candidate.Name, StringComparer.OrdinalIgnoreCase);
            hash.Add(candidate.Server.UniqueId);
        }

        if (context.ActiveSlot.ExcludeCurrentContextFromCandidates
            && context.Server is not null) {
            hash.Add(context.Server.UniqueId);
        }

        return hash.ToHashCode();
    }

    private readonly record struct PlayerCandidate(
        int ClientIndex,
        ServerContext? Server,
        string Name);

    private readonly record struct ServerCandidate(
        int Order,
        ServerContext Server,
        string Name);

    private sealed record BuffCatalog(
        ImmutableArray<string> Candidates,
        ImmutableDictionary<int, string> DisplayNamesById,
        ImmutableDictionary<string, ImmutableArray<int>> IdsByName);

    private sealed record PrefixCatalog(
        ImmutableArray<string> Candidates,
        ImmutableDictionary<int, string> DisplayNamesById,
        ImmutableDictionary<string, ImmutableArray<int>> IdsByName);

    private sealed class DelegateParamExplainer(
        Func<PromptParamExplainContext, PromptParamExplainResult> handler,
        Func<PromptParamExplainContext, long> revisionProvider) : IParamValueExplainer
    {
        public long GetRevision(PromptParamExplainContext context) {
            return revisionProvider(context);
        }

        public bool TryExplain(PromptParamExplainContext context, out PromptParamExplainResult result) {
            result = handler(context);
            return result.State != PromptParamExplainState.None;
        }
    }

    private sealed class DelegateParamCandidateProvider(
        Func<PromptParamCandidateContext, IReadOnlyList<string>> handler,
        Func<PromptParamCandidateContext, long> revisionProvider) : IParamValueCandidateProvider
    {
        public long GetRevision(PromptParamCandidateContext context) {
            return revisionProvider(context);
        }

        public IReadOnlyList<string> GetCandidates(PromptParamCandidateContext context) {
            return handler(context) ?? [];
        }
    }

    [GeneratedRegex(@"^\[i(?:tem)?(?:\/s(?<Stack>\d{1,4}))?(?:\/p(?<Prefix>\d{1,3}))?:(?<NetID>-?\d{1,4})\]$", RegexOptions.CultureInvariant)]
    private static partial Regex ItemTagMatch();
}
