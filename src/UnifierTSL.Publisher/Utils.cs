using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace UnifierTSL.Publisher;

public static class Utils
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
            Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>();
            for (int i = 0; i < args.Length; i++) {
                if (args[i].Length == 0) {
                    continue;
                }

                if (args[i][0] == '-' || args[i][0] == '+') {
                    if (key is not null) {
                        if (!dictionary.TryGetValue(key.ToLower(), out var values)) {
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
                if (!dictionary.TryGetValue(key.ToLower(), out var values)) {
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
            var sb = new StringBuilder();
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
                        var keySb = new StringBuilder();
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

    public static class DotnetSdk
    {
        public static string GetBestMatchedAppHostPath(string rid) {
            Version currentRuntimeVersion = GetCurrentRuntimeVersion();
            string sdkBasePath = GetDotnetSdkBasePath();

            if (!Directory.Exists(sdkBasePath))
                throw new DirectoryNotFoundException($"SDK base path not found: {sdkBasePath}");

            var matchedSdkDir = Directory.GetDirectories(sdkBasePath)
                .Select(Path.GetFileName)
                .Where(static name => Version.TryParse(name, out _))
                .Select(static v => Version.Parse(v!))
                .Where(v => v.Major == currentRuntimeVersion.Major)
                .OrderByDescending(v => v)
                .FirstOrDefault() ?? throw new InvalidOperationException($"No matching SDK version found for runtime major version {currentRuntimeVersion.Major}");

            string matchedSdkPath = Path.Combine(sdkBasePath, matchedSdkDir.ToString());

            string appHostFileName = "apphost" + Utils.File.ExecutableExtension(rid);

            string apphostPath = Path.Combine(matchedSdkPath, "AppHostTemplate", appHostFileName);

            if (!System.IO.File.Exists(apphostPath))
                throw new FileNotFoundException($"{appHostFileName} not found at path: {apphostPath}");

            return apphostPath;
        }

        public static Version GetCurrentRuntimeVersion() {
            return Environment.Version;
        }
        public static string GetTFMString() {
            var v = GetCurrentRuntimeVersion();
            return $"net{v.Major}.{v.Minor}";
        }

        private static string GetDotnetSdkBasePath() {
            var currentDotnetDir = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
            var basePath = currentDotnetDir
                // version folder
                .Parent! // framework folder
                .Parent! // shared folder
                .Parent! // dotnet folder
                .FullName;
            return Path.Combine(basePath, "sdk");
        }
    }

    public static class File
    {

        public static async Task SafeCopy(string src, string dest) {
            var destDir = Path.GetDirectoryName(dest)!;
            Directory.CreateDirectory(destDir);
            using var srcStr = System.IO.File.OpenRead(src);
            using var destStr = System.IO.File.Create(dest);
            await srcStr.CopyToAsync(destStr);
        }

        public static string ExecutableExtension(string rid) {
            if (rid.StartsWith("win")) return ".exe";
            if (rid.StartsWith("osx")) return "";
            if (rid.StartsWith("linux")) return "";
            throw new NotSupportedException(rid);
        }
    }

    public static class Solution
    {
        private const int MaxSearchDepth = 5;
        private const string projectName = nameof(UnifierTSL);

        public static readonly string SolutionRoot;
        public static readonly string DefaultOutputPath;

        public const string configuration =
#if DEBUG
            "Debug";
#else
            "Release";
#endif

        static Solution() {
            SolutionRoot = FindSolutionRoot();
            DefaultOutputPath = Path.Combine(SolutionRoot, "UnifierTSL.Publisher", "bin", configuration, Utils.DotnetSdk.GetTFMString());
        }

        private static string FindSolutionRoot() {
            var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
            if (currentDir.Name is projectName) {
                return FindSolutionRootRecursive(new(Path.Combine(currentDir.FullName, "src")), 0);
            }
            return FindSolutionRootRecursive(currentDir, 0);
        }

        private static string FindSolutionRootRecursive(DirectoryInfo current, int depth) {
            if (depth > MaxSearchDepth) {
                throw new InvalidOperationException(
                    $"Could not find solution root (.sln or .slnx) within {MaxSearchDepth} levels " +
                    $"from execution directory. Started at: {Directory.GetCurrentDirectory()}");
            }

            // Check if any .sln or .slnx files exist in current directory

            if (current.GetFiles().Any(f => f.Name is (projectName + ".sln") or (projectName + ".slnx"))) {
                return current.FullName;
            }

            // Move up one directory
            if (current.Parent == null) {
                throw new InvalidOperationException(
                    $"Could not find solution root (.sln or .slnx) up to filesystem root. " +
                    $"Started at: {Directory.GetCurrentDirectory()}");
            }
            return FindSolutionRootRecursive(current.Parent, depth + 1);
        }
    }
}
