using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace UnifierTSL.PluginService.Dependencies
{
    public class RidGraph
    {
        public static readonly RidGraph Instance = new();
        public Dictionary<string, string[]> RIDParents { get; } = [];

        public RidGraph() {
            using var stream = typeof(RidGraph).Assembly.GetManifestResourceStream("UnifierTSL.PluginService.Dependencies.RuntimeIdentifierGraph.json")!;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            LoadRidGraph(json);
        }

        private void LoadRidGraph(string json) {
            using var doc = JsonDocument.Parse(json);
            foreach (var ridNode in doc.RootElement.GetProperty("runtimes").EnumerateObject()) {
                var rid = ridNode.Name;
                var imports = ridNode.Value.GetProperty("#import").EnumerateArray()
                    .Select(e => e.GetString()!)
                    .ToArray();
                RIDParents[rid] = imports;
            }
        }

        public IEnumerable<string> ExpandRuntimeIdentifier(string rid) {
            var visited = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(rid);

            while (queue.Count > 0) {
                var current = queue.Dequeue();
                if (!visited.Add(current)) continue;

                yield return current;

                if (RIDParents.TryGetValue(current, out var parents)) {
                    foreach (var parent in parents) {
                        if (!visited.Contains(parent)) {
                            queue.Enqueue(parent);
                        }
                    }
                }
            }
        }
    }
}
