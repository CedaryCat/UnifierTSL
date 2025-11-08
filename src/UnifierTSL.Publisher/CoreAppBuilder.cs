using Microsoft.NET.HostModel.AppHost;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace UnifierTSL.Publisher
{
    public class CoreAppBuilder(string relativeProjectPath)
    {
        /// <summary>
        /// Builds the project using `dotnet build` and then packages App.dll,
        /// App.runtimeconfig.json, and App.deps.json into a self-contained executable
        /// using Microsoft.NET.HostModel.
        /// </summary>
        /// <returns>Result containing paths to the packaged executable and dependencies.</returns>
        public CoreAppBuilderResult Build(string rid) {
            var solutionDir = SolutionDirectoryHelper.SolutionRoot;
            var projectPath = Path.Combine(solutionDir, relativeProjectPath);
            var projectName = Path.GetFileNameWithoutExtension(relativeProjectPath);
            var projectDir = Path.GetDirectoryName(projectPath)!;

            var publishDir = Path.Combine(SolutionDirectoryHelper.DefaultOutputPath, "core-publish", projectName);

            Directory.CreateDirectory(publishDir);

            // Step 1: Run dotnet build
            var buildProcess = Process.Start(new ProcessStartInfo {
                FileName = "dotnet",
                Arguments = $"build \"{projectPath}\" -c Release",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.Default,
                StandardOutputEncoding = Encoding.Default,
            }) ??
            throw new Exception("Failed to start dotnet build process.");

            Task<string> outputTask = buildProcess.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = buildProcess.StandardError.ReadToEndAsync();
            buildProcess.WaitForExit();
            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            if (buildProcess.ExitCode != 0) {
                throw new Exception($"Build failed: {error}");
            }

            // Step 2: Determine the target framework folder name from the built output
            var buildDir = Path.Combine(projectDir, "bin", "Release", DotnetSdkHelper.GetTFMString());

            // Step 3: Find main outputs
            var dllPath = Path.Combine(buildDir, $"{projectName}.dll");
            var runtimeConfigPath = Path.Combine(buildDir, $"{projectName}.runtimeconfig.json");
            var depsJsonPath = Path.Combine(buildDir, $"{projectName}.deps.json");
            var pdbPath = Path.Combine(buildDir, $"{projectName}.pdb");

            // Step 4: Copy dependencies
            var dependencies = Directory.GetFiles(buildDir, "*.dll")
                .Select(f => Path.Combine(buildDir, f))
                .ToArray();
            var dependenciesPdb = Directory.GetFiles(buildDir, "*.pdb")
                .Select(f => Path.Combine(buildDir, f))
                .ToArray();

            // Step 5: Generate executable using AppHost
            var appHostTemplate = DotnetSdkHelper.GetBestMatchedAppHostPath(rid);

            var executable = Path.Combine(publishDir, projectName + FileHelpers.ExecutableExtension(rid));

            var i18nPath = Path.Combine(buildDir, "i18n");

            EnsureI18nPathAsync(i18nPath).GetAwaiter().GetResult();

            HostWriter.CreateAppHost(
                appHostSourceFilePath: appHostTemplate,
                appHostDestinationFilePath: executable,
                appBinaryFilePath: Path.Combine("lib", $"{projectName}.dll"),
                windowsGraphicalUserInterface: false);

            return new CoreAppBuilderResult(
                OutputExecutable: executable,
                PdbFile: pdbPath,
                RuntimesPath: Path.Combine(buildDir, "runtimes"),
                I18nPath: i18nPath,
                OtherDependencyDlls: [..dependencies, ..dependenciesPdb, runtimeConfigPath, depsJsonPath]
            );
        }

        private async Task EnsureI18nPathAsync(string i18nOutputDir) {
            if (Directory.Exists(i18nOutputDir)) {
                return;
            }

            var solutionDir = SolutionDirectoryHelper.SolutionRoot;
            var i18nSourceDir = Path.Combine(solutionDir, "..", "i18n");

            var poFiles = Directory.GetFiles(i18nSourceDir, "*.po", SearchOption.AllDirectories);
            // Generate MO files
            Directory.CreateDirectory(i18nOutputDir);

            foreach (var poFile in poFiles) {
                var relativePath = Path.GetRelativePath(i18nSourceDir, poFile);
                var outputMoFile = Path.Combine(i18nOutputDir, Path.ChangeExtension(relativePath, ".mo"));

                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(outputMoFile);
                if (outputDir != null) {
                    Directory.CreateDirectory(outputDir);
                }
                await GenerateMOFileAsync(poFile, outputMoFile);
            }
        }

        static async Task<bool> GenerateMOFileAsync(string poFilePath, string moFilePath) {
            try {
                var psi = new ProcessStartInfo {
                    FileName = "msgfmt",
                    Arguments = $"-o \"{moFilePath}\" \"{poFilePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardErrorEncoding = Encoding.UTF8,
                    StandardOutputEncoding = Encoding.UTF8,
                };

                using var process = Process.Start(psi);

                if (process == null) {
                    Console.Error.WriteLine($"Failed to start msgfmt process for {Path.GetFileName(poFilePath)}");
                    Environment.Exit(1);
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                process.WaitForExit();

                if (process.ExitCode != 0) {
                    Console.Error.WriteLine($"ERROR: msgfmt failed for {Path.GetFileName(poFilePath)} Error: {error}");
                    Environment.Exit(1);
                }

                return true;
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"Failed to generate MO file for {Path.GetFileName(poFilePath)}: {ex.Message}");
                Environment.Exit(1);
            }
            return false;
        }
    }

    public record CoreAppBuilderResult(
        string OutputExecutable,
        string PdbFile,
        string RuntimesPath, 
        string I18nPath,
        string[] OtherDependencyDlls
    );
}
