using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace UnifierTSL.Publisher
{
    internal class Program
    {
        static void Main(string[] args) {
            var options = CLIHelper.ParseArguments(args);
            string rid = ResolveRuntimeIdentifier(options);
            if (options.TryGetValue("--excluded-plugins", out var excludedPlugins)) {
                excludedPlugins = [.. excludedPlugins
                    .Select(p => p.Split(',' , StringSplitOptions.RemoveEmptyEntries))
                    .SelectMany(p => p)
                    .Select(p => p.Trim())];
            }
            excludedPlugins ??= [];

            // Locate solution root early for default path calculation
            var solutionRoot = SolutionDirectoryHelper.SolutionRoot;

            // Parse output path (default: Publisher project's bin directory for compatibility)
            string outputPath = SolutionDirectoryHelper.DefaultOutputPath;
            if (options.TryGetValue("--output-path", out var outputPaths) && outputPaths.Count > 0) {
                outputPath = outputPaths[0];
                // Resolve relative paths relative to the current working directory
                if (!Path.IsPathRooted(outputPath)) {
                    outputPath = Path.Combine(Directory.GetCurrentDirectory(), outputPath);
                }
            }

            // Parse use-rid-folder flag (default: true)
            bool useRidFolder = true;
            if (options.TryGetValue("--use-rid-folder", out var useRidFolderValues) && useRidFolderValues.Count > 0) {
                if (!bool.TryParse(useRidFolderValues[0], out useRidFolder)) {
                    throw new ArgumentException("--use-rid-folder must be a boolean value (true/false).");
                }
            }

            // Parse clean-output-dir flag (default: yes)
            bool cleanOutputDir = true;
            if (options.TryGetValue("--clean-output-dir", out var cleanValues) && cleanValues.Count > 0) {
                var cleanValue = cleanValues[0].ToLower();
                if (cleanValue == "yes" || cleanValue == "true") {
                    cleanOutputDir = true;
                } else if (cleanValue == "no" || cleanValue == "false") {
                    cleanOutputDir = false;
                } else {
                    throw new ArgumentException("--clean-output-dir must be 'yes'/'no' or 'true'/'false'.");
                }
            }

            var task = Run(rid, excludedPlugins, outputPath, useRidFolder, cleanOutputDir);

            task.Wait();
            if (task.IsFaulted) throw task.Exception;
        }

        static string ResolveRuntimeIdentifier(Dictionary<string, List<string>> options) {
            if (options.TryGetValue("--rid", out var rids)) {
                if (rids.Count != 1 || string.IsNullOrWhiteSpace(rids[0])) {
                    throw new ArgumentException("--rid must be specified exactly once.");
                }
                return rids[0].Trim();
            }

            string os = GetPortableRidOsPart();
            string arch = GetPortableRidArchPart();
            return $"{os}-{arch}";
        }

        static string GetPortableRidOsPart() {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                return "win";
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                return "linux";
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                return "osx";
            }

            throw new PlatformNotSupportedException(
                $"Cannot infer RID for OS platform '{RuntimeInformation.OSDescription}'. Please specify --rid explicitly.");
        }

        static string GetPortableRidArchPart() {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm => "arm",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException(
                    $"Cannot infer RID for architecture '{RuntimeInformation.OSArchitecture}'. Please specify --rid explicitly.")
            };
        }

        static async Task Run(string rid, IReadOnlyList<string> excludedPlugins, string outputPath, bool useRidFolder, bool cleanOutputDir) {
            var package = new PackageLayoutManager(rid, outputPath, useRidFolder, cleanOutputDir);

            await package.InputAppTools(
                new AppToolsPublisher([
                    Path.Combine("UnifierTSL.ConsoleClient", "UnifierTSL.ConsoleClient.csproj"),
                ])
                .PublishApps(rid));

            await package.InputPlugins(
                new PluginsBuilder("Plugins").BuildPlugins(rid, excludedPlugins));

            await package.InputCoreProgram(
                new CoreAppBuilder(Path.Combine("UnifierTSL", "UnifierTSL.csproj")).Build(rid));
        }
    }
}
