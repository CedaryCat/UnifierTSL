using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Publisher
{
    /// <summary>
    /// Provides single-file publish functionality for the specified projects with optional configuration.
    /// </summary>
    /// <param name="appProjectPaths">Relative paths to the application projects</param>
    public class AppToolsPublisher(string[] appProjectPaths)
    {
        /// <summary>
        /// Starts the dotnet publish command asynchronously for each project,
        /// stores the publish output directories in a thread-safe collection,
        /// and returns the results as an ImmutableArray.
        /// </summary>
        /// <returns>An ImmutableArray containing the output files of the published applications.</returns>
        public async Task<ImmutableArray<string>> PublishApps() {

            var solutionDir = new DirectoryInfo(Directory.GetCurrentDirectory())
                         // target framework folder
                .Parent! // configuration (Debug or Release) folder
                .Parent! // bin folder
                .Parent! // solution folder
                .Parent! // target framework folder
                .FullName;

            var publishPaths = new ConcurrentBag<string>();
            var publishTasks = appProjectPaths.Select(async relativePath => {

                var projectPath = Path.Combine(solutionDir, relativePath);
                var outputDir = Path.Combine("apps-publish", Path.GetFileNameWithoutExtension(relativePath));

                if (Directory.Exists(outputDir)) { 
                    Directory.Delete(outputDir, recursive: true);
                }
                Directory.CreateDirectory(outputDir);

                var startInfo = new ProcessStartInfo {
                    FileName = "dotnet",
                    Arguments = $"publish {projectPath} -c Release -p:PublishSingleFile=true -p:SelfContained=false -o {outputDir}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0) {
                    string error = await process.StandardError.ReadToEndAsync();
                    throw new InvalidOperationException($"Failed to publish {relativePath}: {error}");
                }

                foreach (var file in Directory.GetFiles(outputDir)) {
                    publishPaths.Add(file);
                }
            });

            await Task.WhenAll(publishTasks);
            return [.. publishPaths];
        }
    }
}
