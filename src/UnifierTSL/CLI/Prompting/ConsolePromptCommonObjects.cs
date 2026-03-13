using System.Collections.Immutable;
using Terraria;
using Terraria.ID;
using UnifierTSL.Servers;

namespace UnifierTSL.CLI.Prompting;

public static class ConsolePromptCommonParameterSemanticKeys
{
    public const string PlayerRef = "unifier.player-ref";
    public const string ItemRef = "unifier.item-ref";
}

public static class ConsolePromptCommonObjects
{
    private static readonly Lazy<ItemCatalog> ItemCatalogCache = new(BuildItemCatalogCore);

    internal static IReadOnlyDictionary<string, IConsoleParameterValueExplainer> ParameterExplainers { get; } =
        new Dictionary<string, IConsoleParameterValueExplainer>(StringComparer.Ordinal) {
            [ConsolePromptCommonParameterSemanticKeys.PlayerRef] = new DelegateConsoleParameterValueExplainer(ExplainPlayer),
            [ConsolePromptCommonParameterSemanticKeys.ItemRef] = new DelegateConsoleParameterValueExplainer(ExplainItem),
        };

    public static IReadOnlyList<string> GetPlayerCandidates(ServerContext? server = null)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (PlayerCandidate candidate in EnumeratePlayers(server)) {
            names.Add(candidate.Name);
        }

        return [.. names.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
    }

    public static IReadOnlyList<string> GetServerCandidates()
    {
        return [.. UnifiedServerCoordinator.Servers
            .Where(static server => server.IsRunning)
            .Select(static server => server.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
    }

    public static IReadOnlyList<string> GetItemCandidates()
    {
        return ItemCatalogCache.Value.Candidates;
    }

    private static IEnumerable<PlayerCandidate> EnumeratePlayers(ServerContext? server)
    {
        for (int i = 0; i < UnifiedServerCoordinator.globalClients.Length; i++) {
            var client = UnifiedServerCoordinator.globalClients[i];
            if (!client.IsActive) {
                continue;
            }

            ServerContext? currentServer = UnifiedServerCoordinator.GetClientCurrentlyServer(i);
            if (server is not null && currentServer != server) {
                continue;
            }

            string? name = client.Name;
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }

            yield return new PlayerCandidate(i, currentServer, name.Trim());
        }
    }

    private static ItemCatalog BuildItemCatalogCore()
    {
        HashSet<string> allNames = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<int, string> displayNames = [];
        Dictionary<string, HashSet<int>> idsByName = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < ItemID.Count; i++) {
            AddItemName(i, Lang.GetItemNameValue(i), preferDisplayName: true);
            AddItemName(i, UnifierTSL.Localization.Terraria.EnglishLanguage.GetItemNameById(i), preferDisplayName: false);
        }

        return new ItemCatalog(
            Candidates: [.. allNames.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)],
            DisplayNamesById: displayNames.ToImmutableDictionary(),
            IdsByName: idsByName.ToImmutableDictionary(
                static pair => pair.Key,
                static pair => pair.Value
                    .OrderBy(static id => id)
                    .ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase));

        void AddItemName(int itemId, string? value, bool preferDisplayName)
        {
            if (string.IsNullOrWhiteSpace(value)) {
                return;
            }

            string normalized = value.Trim();
            allNames.Add(normalized);

            if (!idsByName.TryGetValue(normalized, out HashSet<int>? ids)) {
                ids = new HashSet<int>();
                idsByName[normalized] = ids;
            }

            ids.Add(itemId);
            if (preferDisplayName || !displayNames.ContainsKey(itemId)) {
                displayNames[itemId] = normalized;
            }
        }
    }

    private static ConsoleParameterExplainResult ExplainPlayer(ConsoleParameterExplainContext context)
    {
        string search = context.RawToken?.Trim() ?? string.Empty;
        if (search.Length == 0) {
            return Invalid();
        }

        List<PlayerCandidate> candidates = [.. EnumeratePlayers(context.Server)];
        if (candidates.Count == 0) {
            return Invalid();
        }

        List<PlayerCandidate> matches = FindMatchingPlayers(candidates, search);
        if (matches.Count == 0) {
            return Invalid();
        }

        if (matches.Count == 1) {
            return Resolved(matches[0].Name);
        }

        return Ambiguous(matches.Select(static player => $"{player.Name}({player.ClientIndex})"));
    }

    private static List<PlayerCandidate> FindMatchingPlayers(
        IReadOnlyList<PlayerCandidate> candidates,
        string search)
    {
        if (int.TryParse(search, out int clientIndex)) {
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

    private static ConsoleParameterExplainResult ExplainItem(ConsoleParameterExplainContext context)
    {
        ItemCatalog catalog = ItemCatalogCache.Value;
        string search = context.RawToken?.Trim() ?? string.Empty;
        if (search.Length == 0) {
            return Invalid();
        }

        if (int.TryParse(search, out int itemId)
            && catalog.DisplayNamesById.TryGetValue(itemId, out string? byIdDisplay)) {
            return Resolved(byIdDisplay);
        }

        List<int> exactMatches = ResolveItemIds(catalog, search, exact: true);
        if (exactMatches.Count == 0) {
            exactMatches = ResolveItemIds(catalog, search, exact: false);
        }

        if (exactMatches.Count == 0) {
            return Invalid();
        }

        if (exactMatches.Count == 1
            && catalog.DisplayNamesById.TryGetValue(exactMatches[0], out string? displayName)) {
            return Resolved(displayName);
        }

        return Ambiguous(exactMatches
            .Select(id => catalog.DisplayNamesById.TryGetValue(id, out string? name) ? name : null)
            .Where(static name => !string.IsNullOrWhiteSpace(name))!);
    }

    private static List<int> ResolveItemIds(ItemCatalog catalog, string search, bool exact)
    {
        IEnumerable<KeyValuePair<string, ImmutableArray<int>>> matches = exact
            ? catalog.IdsByName.Where(pair => pair.Key.Equals(search, StringComparison.OrdinalIgnoreCase))
            : catalog.IdsByName.Where(pair => pair.Key.StartsWith(search, StringComparison.OrdinalIgnoreCase));

        return [.. matches
            .SelectMany(static pair => pair.Value)
            .Distinct()
            .OrderBy(static id => id)];
    }

    private static ConsoleParameterExplainResult Resolved(string? displayText)
    {
        return string.IsNullOrWhiteSpace(displayText)
            ? Invalid()
            : new ConsoleParameterExplainResult(ConsoleParameterExplainState.Resolved, displayText.Trim());
    }

    private static ConsoleParameterExplainResult Invalid()
        => new(ConsoleParameterExplainState.Invalid, "invalid");

    private static ConsoleParameterExplainResult Ambiguous(IEnumerable<string> displayValues)
    {
        List<string> candidates = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string? displayValue in displayValues) {
            if (string.IsNullOrWhiteSpace(displayValue)) {
                continue;
            }

            string normalized = displayValue.Trim();
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

        string preview = string.Join(", ", candidates.Take(3));
        if (candidates.Count > 3) {
            preview += ", ...";
        }

        return new ConsoleParameterExplainResult(
            ConsoleParameterExplainState.Ambiguous,
            "ambiguous: " + preview);
    }

    private readonly record struct PlayerCandidate(
        int ClientIndex,
        ServerContext? Server,
        string Name);

    private sealed record ItemCatalog(
        ImmutableArray<string> Candidates,
        ImmutableDictionary<int, string> DisplayNamesById,
        ImmutableDictionary<string, ImmutableArray<int>> IdsByName);

    private sealed class DelegateConsoleParameterValueExplainer(
        Func<ConsoleParameterExplainContext, ConsoleParameterExplainResult> handler) : IConsoleParameterValueExplainer
    {
        public bool TryExplain(ConsoleParameterExplainContext context, out ConsoleParameterExplainResult result)
        {
            result = handler(context);
            return result.State != ConsoleParameterExplainState.None;
        }
    }
}
