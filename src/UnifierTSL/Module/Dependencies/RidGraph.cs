using System.Text.Json;

namespace UnifierTSL.Module.Dependencies
{
    public class RidGraph
    {
        public static readonly RidGraph Instance = new();
        public Dictionary<string, string[]> RIDParents { get; } = [];

        public RidGraph() {
            using Stream stream = typeof(RidGraph).Assembly.GetManifestResourceStream(
                $"{typeof(RidGraph).Namespace}.RuntimeIdentifierGraph.json")!;
            using StreamReader reader = new(stream);
            string json = reader.ReadToEnd();
            LoadRidGraph(json);
        }

        private void LoadRidGraph(string json) {
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (JsonProperty ridNode in doc.RootElement.GetProperty("runtimes").EnumerateObject()) {
                string rid = ridNode.Name;
                string[] imports = ridNode.Value.GetProperty("#import").EnumerateArray()
                    .Select(e => e.GetString()!)
                    .ToArray();
                RIDParents[rid] = imports;
            }
        }

        public IEnumerable<string> ExpandRuntimeIdentifier(string rid) {
            HashSet<string> visited = [];
            Queue<string> queue = new();
            queue.Enqueue(rid);

            while (queue.Count > 0) {
                string current = queue.Dequeue();
                if (!visited.Add(current)) continue;

                yield return current;

                if (RIDParents.TryGetValue(current, out string[]? parents)) {
                    foreach (string parent in parents) {
                        if (!visited.Contains(parent)) {
                            queue.Enqueue(parent);
                        }
                    }
                }
            }
        }
    }
}
