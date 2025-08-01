using System.Collections.Immutable;
using System.Diagnostics;

namespace UnifierTSL.Publisher
{
    public class PluginsBuilder(string RelativePluginProjectDir)
    {
        public async Task<ImmutableArray<string>> BuildPlugins() {
            var solutionDir = new DirectoryInfo(Directory.GetCurrentDirectory())
                // target framework folder
                .Parent! // configuration (Debug or Release) folder
                .Parent! // bin folder
                .Parent! // solution folder
                .Parent! // target framework folder
                .FullName;

            var pluginDir = Path.Combine(solutionDir, RelativePluginProjectDir);
            var pluginDirInfo = new DirectoryInfo(pluginDir);

            var projects = pluginDirInfo
                .GetDirectories()
                .SelectMany(d => d.GetFiles("*.csproj"))
                .ToList();

            return await Build(solutionDir, projects);
        }
        public async Task<ImmutableArray<string>> BuildPlugins(params IReadOnlyList<string> specifiedProjectNames) {
            var solutionDir = new DirectoryInfo(Directory.GetCurrentDirectory())
                // target framework folder
                .Parent! // configuration (Debug or Release) folder
                .Parent! // bin folder
                .Parent! // solution folder
                .Parent! // target framework folder
                .FullName;

            var specified = specifiedProjectNames.ToHashSet();
            var pluginDir = Path.Combine(solutionDir, RelativePluginProjectDir);
            var pluginDirInfo = new DirectoryInfo(pluginDir);

            List<FileInfo> projects = new List<FileInfo>();

            foreach (var project in pluginDirInfo.GetDirectories().SelectMany(d => d.GetFiles("*.csproj"))) {
                var name = Path.GetFileNameWithoutExtension(project.Name);
                if (!specified.Contains(name)) {
                    continue;
                }
                projects.Add(project);
            }

            return await Build(solutionDir, projects);
        }

        static async Task<ImmutableArray<string>> Build(string solutionDir, List<FileInfo> projects) {
            string[] files = new string[projects.Count * 2]; // dll and pdb

            await Task.WhenAll(projects.Select(async (project, i) => {

                var relativePath = Path.GetRelativePath(solutionDir, project.FullName);
                var projectName = Path.GetFileNameWithoutExtension(project.Name);
                var outputDir = Path.Combine("plugins-publish", projectName);

                if (Directory.Exists(outputDir)) {
                    Directory.Delete(outputDir, recursive: true);
                }
                Directory.CreateDirectory(outputDir);

                var startInfo = new ProcessStartInfo {
                    FileName = "dotnet",
                    Arguments = $"build \"{project.FullName}\" -c Release -o \"{outputDir}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                string output = await outputTask;
                string error = await errorTask;

                if (process.ExitCode != 0) {
                    throw new InvalidOperationException(
                        $"Failed to publish {relativePath}: {error}\nStdout: {output}");
                }

                files[i * 2] = Path.Combine(outputDir, projectName + ".dll");
                files[i * 2 + 1] = Path.Combine(outputDir, projectName + ".pdb");
            }));

            return [.. files];
        }
    }
}
