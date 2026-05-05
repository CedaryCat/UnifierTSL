using System.Collections.Immutable;
using Terraria;
using Terraria.ID;

namespace UnifierTSL.Localization.Terraria
{
    public static class TerrariaItemNameLookup
    {
        private static readonly Lazy<ItemNameCatalog> CatalogCache = new(BuildCatalog);

        public static IReadOnlyList<string> GetCandidates() {
            return CatalogCache.Value.Candidates;
        }

        public static IReadOnlyList<string> GetCandidatesByPrefix(string prefix) {
            if (string.IsNullOrEmpty(prefix)) {
                return CatalogCache.Value.Candidates;
            }

            return ResolvePrefixCandidateSlice(CatalogCache.Value.Candidates, prefix);
        }

        public static string? GetDisplayName(int itemId) {
            return CatalogCache.Value.DisplayNamesById.TryGetValue(itemId, out var displayName)
                ? displayName
                : null;
        }

        public static IReadOnlyList<int> GetIdsByExactName(string search) {
            if (string.IsNullOrWhiteSpace(search)) {
                return [];
            }

            var normalizedSearch = search.Trim();
            return CatalogCache.Value.IdsByName.TryGetValue(normalizedSearch, out var ids)
                ? ids
                : [];
        }

        public static IReadOnlyList<int> GetIdsByPrefix(string prefix) {
            if (string.IsNullOrWhiteSpace(prefix)) {
                return [];
            }

            var normalizedPrefix = prefix.Trim();
            return ResolveIdsByNames(ResolvePrefixCandidateSlice(CatalogCache.Value.Candidates, normalizedPrefix));
        }

        public static IReadOnlyList<int> GetIdsByContains(string search) {
            if (string.IsNullOrWhiteSpace(search)) {
                return [];
            }

            var normalizedSearch = search.Trim();
            return [.. CatalogCache.Value.IdsByName
                .Where(pair => pair.Key.Contains(normalizedSearch, StringComparison.InvariantCultureIgnoreCase))
                .SelectMany(static pair => pair.Value)
                .Distinct()
                .OrderBy(static id => id)];
        }

        private static ItemNameCatalog BuildCatalog() {
            HashSet<string> allNames = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<int, string> displayNames = [];
            Dictionary<string, HashSet<int>> idsByName = new(StringComparer.OrdinalIgnoreCase);

            for (var i = 1; i < ItemID.Count; i++) {
                AddItemName(i, Lang.GetItemNameValue(i), preferDisplayName: true);
                AddItemName(i, EnglishLanguage.GetItemNameById(i), preferDisplayName: false);
            }

            return new ItemNameCatalog(
                Candidates: [.. allNames.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)],
                DisplayNamesById: displayNames.ToImmutableDictionary(),
                IdsByName: idsByName.ToImmutableDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value
                        .OrderBy(static id => id)
                        .ToImmutableArray(),
                    StringComparer.OrdinalIgnoreCase));

            void AddItemName(int itemId, string? value, bool preferDisplayName) {
                if (string.IsNullOrWhiteSpace(value)) {
                    return;
                }

                var normalized = value.Trim();
                allNames.Add(normalized);

                if (!idsByName.TryGetValue(normalized, out var ids)) {
                    ids = [];
                    idsByName[normalized] = ids;
                }

                ids.Add(itemId);
                if (preferDisplayName || !displayNames.ContainsKey(itemId)) {
                    displayNames[itemId] = normalized;
                }
            }
        }

        private static List<int> ResolveIdsByNames(IEnumerable<string> matchedNames) {
            var catalog = CatalogCache.Value;
            return [.. matchedNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(name => catalog.IdsByName.TryGetValue(name, out var ids) ? ids : [])
                .Distinct()
                .OrderBy(static id => id)];
        }

        private static IReadOnlyList<string> ResolvePrefixCandidateSlice(ImmutableArray<string> candidates, string prefix) {
            var start = FindPrefixStartIndex(candidates, prefix);
            if (start < 0) {
                return [];
            }

            var end = start;
            while (end < candidates.Length
                && candidates[end].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                end++;
            }

            return new CandidateSlice(candidates, start, end - start);
        }

        private static int FindPrefixStartIndex(ImmutableArray<string> candidates, string prefix) {
            var low = 0;
            var high = candidates.Length - 1;
            while (low <= high) {
                var mid = low + ((high - low) >> 1);
                if (StringComparer.OrdinalIgnoreCase.Compare(candidates[mid], prefix) < 0) {
                    low = mid + 1;
                }
                else {
                    high = mid - 1;
                }
            }

            return low < candidates.Length
                && candidates[low].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? low
                    : -1;
        }

        private sealed record ItemNameCatalog(
            ImmutableArray<string> Candidates,
            ImmutableDictionary<int, string> DisplayNamesById,
            ImmutableDictionary<string, ImmutableArray<int>> IdsByName);

        private sealed class CandidateSlice(ImmutableArray<string> source, int start, int length) : IReadOnlyList<string>
        {
            public int Count => length;

            public string this[int index] => source[start + index];

            public IEnumerator<string> GetEnumerator() {
                for (var index = 0; index < length; index++) {
                    yield return source[start + index];
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
                return GetEnumerator();
            }
        }
    }
}
