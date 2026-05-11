using System.Collections.Immutable;

namespace UnifierTSL.Surface.Prompting.Semantics
{
    internal static class PromptSuggestionOps
    {
        public static readonly IComparer<string> DisplayTextComparer = Comparer<string>.Create(CompareDisplayText);

        public static ImmutableArray<PromptSuggestion> Normalize(IEnumerable<PromptSuggestion> source) {
            return [.. source
                .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
                .GroupBy(static item => item.Value, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.OrderByDescending(static item => item.Weight).First())
                .OrderByDescending(static item => item.Weight)
                .ThenBy(static item => item.Value, DisplayTextComparer)];
        }

        public static IReadOnlyList<PromptSuggestion> OrderUniqueByWeight(IEnumerable<PromptSuggestion> source) {
            return [.. Normalize(source)];
        }

        private static int CompareDisplayText(string? left, string? right) {
            left ??= string.Empty;
            right ??= string.Empty;

            if (left.Length == 0 || right.Length == 0) {
                return StringComparer.OrdinalIgnoreCase.Compare(left, right);
            }

            var commonPrefixLength = ResolveCommonPrefixLength(left, right);
            if (commonPrefixLength > 0) {
                return left.Length.CompareTo(right.Length);
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }

        private static int ResolveCommonPrefixLength(string left, string right) {
            var commonLength = Math.Min(left.Length, right.Length);
            var index = 0;
            while (index < commonLength
                && char.ToUpperInvariant(left[index]) == char.ToUpperInvariant(right[index])) {
                index++;
            }

            return index;
        }
    }
}
