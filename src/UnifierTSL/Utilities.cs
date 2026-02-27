using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Terraria;

namespace UnifierTSL
{
    public static class Utilities
    {
        public static class CLI
        {
            /// <summary>
            /// --key1 value1 --key2 value2
            /// </summary>
            /// <param name="args"></param>
            /// <returns></returns>
            public static Dictionary<string, List<string>> ParseArguments(string[] args) {
                string? key = null;
                string value = "";
                Dictionary<string, List<string>> dictionary = [];
                for (int i = 0; i < args.Length; i++) {
                    if (args[i].Length == 0) {
                        continue;
                    }

                    if (args[i][0] == '-' || args[i][0] == '+') {
                        if (key is not null) {
                            if (!dictionary.TryGetValue(key.ToLower(), out List<string>? values)) {
                                dictionary.Add(key.ToLower(), values = []);
                            }
                            values.Add(value);
                        }

                        key = args[i];
                        value = "";
                    }
                    else {
                        if (value != "") {
                            value += " ";
                        }

                        value += args[i];
                    }
                }

                if (key is not null) {
                    if (!dictionary.TryGetValue(key.ToLower(), out List<string>? values)) {
                        dictionary.Add(key.ToLower(), values = []);
                    }
                    values.Add(value);
                }

                return dictionary;
            }
            /// <summary>
            /// --key1 sub1:val1 sub2:val2 --key2 sub3:val3
            /// </summary>
            /// <param name="input"></param>
            /// <param name="result"></param>
            /// <returns></returns>
            public static bool TryParseSubArguments(string input, [NotNullWhen(true)] out Dictionary<string, string>? result) {
                result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                StringBuilder sb = new();
                string? currentKey = null;
                bool inQuotes = false;
                bool readingValue = false;
                bool hasColon = false;

                for (int i = 0; i <= input.Length; i++) {
                    char c = i < input.Length ? input[i] : ' '; // simulate trailing whitespace

                    if (c == '"' && (i == 0 || input[i - 1] != '\\')) {
                        inQuotes = !inQuotes;
                        continue;
                    }

                    if (!inQuotes && c == ':') {
                        if (!readingValue) {
                            currentKey = sb.ToString().Trim();
                            sb.Clear();
                            readingValue = true;
                            hasColon = true;
                            continue;
                        }
                        else if (currentKey is not null) {
                            StringBuilder keySb = new();
                            for (int j = sb.Length - 1; j >= 0; j--) {
                                if (char.IsWhiteSpace(sb[j])) {
                                    break;
                                }
                                keySb.Insert(0, sb[j]);
                                sb.Remove(j, 1);
                            }
                            result[currentKey] = sb.ToString().Trim();
                            currentKey = keySb.ToString().Trim();
                            sb.Clear();
                            readingValue = true;
                            hasColon = true;
                            continue;
                        }
                    }

                    if (!inQuotes && char.IsWhiteSpace(c)) {
                        if (sb.Length == 0 && !readingValue) continue;
                    }

                    sb.Append(c);
                }

                if (currentKey is not null && sb.Length > 0) {
                    result[currentKey] = sb.ToString().Trim();
                }

                if (!hasColon) {
                    result = null;
                    return false;
                }

                return true;
            }
        }
        public static class IO
        {
            public static async Task<bool> SafeCopyAsync(string src, string dest) {
                string destDir = Path.GetDirectoryName(dest)!;
                Directory.CreateDirectory(destDir);
                using FileStream srcStr = File.OpenRead(src);
                try {
                    using FileStream destStr = File.Create(dest);
                    await srcStr.CopyToAsync(destStr);
                }
                catch {
                    return false;
                }
                return true;
            }
            public static bool SafeCopy(string src, string dest) {
                string destDir = Path.GetDirectoryName(dest)!;
                Directory.CreateDirectory(destDir);
                using FileStream srcStr = File.OpenRead(src);
                try {
                    using FileStream destStr = File.Create(dest);
                    srcStr.CopyTo(destStr);
                }
                catch {
                    return false;
                }
                return true;
            }
            public static FileStream? SafeFileCreate(string path, out Exception? exception) {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }

                exception = null;
                try {
                    return File.Create(path);
                }
                catch (Exception ex) {
                    exception = ex;
                    return null;
                }
            }

            public static int RemoveEmptyDirectories(string path, bool includeRoot = true) {
                if (!Directory.Exists(path)) {
                    return 0;
                }
                int removedCount = 0;
                foreach (string dir in Directory.GetDirectories(path)) {
                    removedCount += RemoveEmptyDirectories(dir);
                }
                if (includeRoot && Directory.GetFiles(path).Length == 0 && Directory.GetDirectories(path).Length == 0) {
                    try {
                        Directory.Delete(path);
                        removedCount++;
                    }
                    catch {
                    }
                }

                return removedCount;
            }
            public static string GetWorldPathFromName(string worldName, bool allowDuplicate = false) {
                static string SanitizeFileName(string name) {
                    char[] invalidChars = Path.GetInvalidFileNameChars();
                    StringBuilder sb = new(name.Length);
                    foreach (char c in name) {
                        if (invalidChars.Contains(c))
                            sb.Append('-');
                        else if (c == ' ')
                            sb.Append('_');
                        else
                            sb.Append(c);
                    }
                    return sb
                        .Replace(".", "_")
                        .Replace("*", "_")
                        .ToString();
                }

                string sanitized = SanitizeFileName(worldName);
                string worldDir = Main.WorldPath;
                string fullPath = Path.Combine(worldDir, sanitized + ".wld");

                if (Path.GetFullPath(fullPath).StartsWith("\\\\.\\", StringComparison.Ordinal)) {
                    sanitized += "_";
                    fullPath = Path.Combine(worldDir, sanitized + ".wld");
                }

                if (!allowDuplicate && File.Exists(fullPath)) {
                    int i = 2;
                    string candidatePath;
                    do {
                        candidatePath = Path.Combine(worldDir, $"{sanitized}{i}.wld");
                        i++;
                    }
                    while (File.Exists(candidatePath));

                    fullPath = candidatePath;
                }

                return fullPath;
            }
        }
        public static class Culture {
            private static List<CultureInfo> BuildChain(CultureInfo c) {
                var list = new List<CultureInfo>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                while (c != null) {
                    if (!seen.Add(c.Name)) break;

                    list.Add(c);

                    if (Equals(c, CultureInfo.InvariantCulture) || Equals(c.Parent, c))
                        break;

                    c = c.Parent;
                }

                return list;
            }

            public static T? FindBestMatch<T>(
                IEnumerable<T> candidates,
                CultureInfo target,
                Func<T, CultureInfo?> getCulture,
                bool allowInvariantIntersection = false)
                where T : class {
                var targetChain = BuildChain(target);

                var byName = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in candidates) {
                    var ci = getCulture(item);
                    if (ci == null) continue;
                    if (!byName.ContainsKey(ci.Name))
                        byName[ci.Name] = item;
                }

                foreach (var c in targetChain) {
                    if (byName.TryGetValue(c.Name, out var hit))
                        return hit;
                }

                var targetIndex = targetChain
                    .Select((c, i) => new { c.Name, i })
                    .ToDictionary(x => x.Name, x => x.i, StringComparer.OrdinalIgnoreCase);

                T? best = null;
                int bestScore = int.MaxValue;

                foreach (var item in candidates) {
                    var ci = getCulture(item);
                    if (ci == null) continue;

                    var candChain = BuildChain(ci);

                    for (int j = 0; j < candChain.Count; j++) {
                        var name = candChain[j].Name;

                        if (!allowInvariantIntersection && name.Length == 0)
                            continue;

                        if (!targetIndex.TryGetValue(name, out int i))
                            continue;

                        int score = i * 100 + j;

                        if (score < bestScore ||
                            (score == bestScore &&
                             getCulture(item)!.IsNeutralCulture == false &&
                             (best == null || getCulture(best)!.IsNeutralCulture == true))) {
                            bestScore = score;
                            best = item;
                        }
                        break;
                    }
                }

                return best;
            }
        }
        public static class Crypto
        {
            public static string Sha512Hex(ReadOnlySpan<byte> data) {
                Span<byte> hash = stackalloc byte[64];
                SHA512.HashData(data, hash);
                return Convert.ToHexString(hash);
            }
            public static string Sha512Hex(string text, Encoding encoding) {
                ArgumentNullException.ThrowIfNull(text);
                ArgumentNullException.ThrowIfNull(encoding);

                var bytes = encoding.GetBytes(text);
                return Sha512Hex(bytes);
            }
        }
    }
}
