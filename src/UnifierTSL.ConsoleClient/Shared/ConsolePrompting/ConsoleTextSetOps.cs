namespace UnifierTSL.ConsoleClient.Shared.ConsolePrompting
{
    public static class ConsoleTextSetOps
    {
        public static IReadOnlyList<string> DistinctPreserveOrder(IEnumerable<string> values) {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            List<string> output = [];

            foreach (string value in values) {
                if (string.IsNullOrWhiteSpace(value)) {
                    continue;
                }

                if (!seen.Add(value)) {
                    continue;
                }

                output.Add(value);
            }

            return output;
        }

        public static IReadOnlyList<string> DistinctAndSort(IEnumerable<string> values) {
            return [.. values
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)];
        }
    }
}
