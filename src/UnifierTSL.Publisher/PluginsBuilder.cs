using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace UnifierTSL.Publisher
{
    public class PluginsBuilder(string RelativePluginProjectDir)
    {
        public ImmutableArray<string> BuildPlugins(string rid) {
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

            return Build(rid, solutionDir, projects);
        }
        public ImmutableArray<string> BuildPlugins(string rid, params IReadOnlyList<string> excludedProjectNames) {
            var solutionDir = new DirectoryInfo(Directory.GetCurrentDirectory())
                // target framework folder
                .Parent! // configuration (Debug or Release) folder
                .Parent! // bin folder
                .Parent! // solution folder
                .Parent! // target framework folder
                .FullName;

            var excluded = new HashSet<string>(excludedProjectNames, StringComparer.OrdinalIgnoreCase);
            var pluginDir = Path.Combine(solutionDir, RelativePluginProjectDir);
            var pluginDirInfo = new DirectoryInfo(pluginDir);

            List<FileInfo> projects = new List<FileInfo>();

            foreach (var project in pluginDirInfo.GetDirectories().SelectMany(d => d.GetFiles("*.csproj"))) {
                var name = Path.GetFileNameWithoutExtension(project.Name);
                if (excluded.Contains(name)) {
                    continue;
                }
                projects.Add(project);
            }

            return Build(rid, solutionDir, projects);
        }

        static ImmutableArray<string> Build(string rid, string solutionDir, List<FileInfo> projects) {
            string[] files = new string[projects.Count * 2]; // dll and pdb

            for (int i = 0; i < projects.Count; i++) {
                var project = projects[i];
                var relativePath = Path.GetRelativePath(solutionDir, project.FullName);
                var projectName = Path.GetFileNameWithoutExtension(project.Name);
                var outputDir = Path.Combine("plugins-publish", projectName);

                if (Directory.Exists(outputDir)) {
                    Directory.Delete(outputDir, recursive: true);
                }
                Directory.CreateDirectory(outputDir);

                var startInfo = new ProcessStartInfo {
                    FileName = "dotnet",
                    Arguments = $"build \"{project.FullName}\" -c Release -o \"{outputDir}\" -r {rid}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardErrorEncoding = Encoding.Default,
                    StandardOutputEncoding = Encoding.Default,
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                process.WaitForExit();

                string output = outputTask.GetAwaiter().GetResult();
                string error = errorTask.GetAwaiter().GetResult();

                if (process.ExitCode != 0) {
                    throw new InvalidOperationException(
                        $"Failed to publish {relativePath}: {error}\nStdout: {output}");
                }

                files[i * 2] = Path.Combine(outputDir, projectName + ".dll");
                files[i * 2 + 1] = Path.Combine(outputDir, projectName + ".pdb");
            }

            return [.. files];
        }
    }
}
