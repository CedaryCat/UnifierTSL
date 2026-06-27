using System.Runtime.InteropServices;

namespace UnifierTSL.Publisher;

internal class Program
{
    static void Main(string[] args) {
        var options = Utils.CLI.ParseArguments(args);
        var rid = ResolveRuntimeIdentifier(options);
        var excludedPlugins = ParseListOption(options, "--excluded-plugins");
        var buildParts = ParseBuildParts(options);

        // Locate solution root early for default path calculation
        var solutionRoot = Utils.Solution.SolutionRoot;

        // Parse output path (default: Publisher project's bin directory for compatibility)
        var outputPath = Utils.Solution.DefaultOutputPath;
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

        var task = Run(rid, excludedPlugins, buildParts, outputPath, useRidFolder, cleanOutputDir);

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

    static IReadOnlyList<string> ParseListOption(Dictionary<string, List<string>> options, string name) {
        return options.TryGetValue(name, out var values)
            ? [.. values
                .SelectMany(p => p.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)]
            : [];
    }

    static PublisherBuildPart ParseBuildParts(Dictionary<string, List<string>> options) {
        var buildParts = PublisherBuildPart.All;
        var skippedParts = ParseListOption(options, "--skip-build");
        foreach (var skippedPart in skippedParts) {
            buildParts &= ~ParseBuildPart(skippedPart);
        }

        if (options.ContainsKey("--skip-build") && skippedParts.Count == 0) {
            throw new ArgumentException("--skip-build must name at least one section.");
        }
        if (buildParts is PublisherBuildPart.None) {
            throw new ArgumentException("--skip-build cannot skip every publisher section.");
        }

        return buildParts;
    }

    static PublisherBuildPart ParseBuildPart(string value) {
        return value.ToLowerInvariant() switch {
            "plugin" or "plugins" => PublisherBuildPart.Plugins,
            "app" or "apps" or "body" or "main" => PublisherBuildPart.App,
            "core" or "core-program" or "program" => PublisherBuildPart.CoreProgram,
            "app-tools" or "tools" or "console" or "console-client" => PublisherBuildPart.AppTools,
            _ => throw new ArgumentException(
                $"Unknown --skip-build section '{value}'. Expected plugins, app, core, or app-tools.")
        };
    }

    static async Task Run(
        string rid,
        IReadOnlyList<string> excludedPlugins,
        PublisherBuildPart buildParts,
        string outputPath,
        bool useRidFolder,
        bool cleanOutputDir) {

        var package = new PackageLayoutManager(rid, outputPath, useRidFolder, cleanOutputDir);

        if ((buildParts & PublisherBuildPart.AppTools) != PublisherBuildPart.None) {
            await package.InputAppTools(
                new AppToolsPublisher([
                    Path.Combine("UnifierTSL.ConsoleClient", "UnifierTSL.ConsoleClient.csproj"),
                ])
                .PublishApps(rid));
        }

        if ((buildParts & PublisherBuildPart.Plugins) != PublisherBuildPart.None) {
            await package.InputPlugins(
                new PluginsBuilder("Plugins").BuildPlugins(rid, excludedPlugins));
        }

        if ((buildParts & PublisherBuildPart.CoreProgram) != PublisherBuildPart.None) {
            await package.InputCoreProgram(
                new CoreAppBuilder(Path.Combine("UnifierTSL", "UnifierTSL.csproj")).Build(rid));
        }
    }
}

[Flags]
internal enum PublisherBuildPart
{
    None = 0,
    AppTools = 1,
    Plugins = 2,
    CoreProgram = 4,
    App = AppTools | CoreProgram,
    All = App | Plugins
}
