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

            HostWriter.CreateAppHost(
                appHostSourceFilePath: appHostTemplate,
                appHostDestinationFilePath: executable,
                appBinaryFilePath: Path.Combine("lib", $"{projectName}.dll"),
                windowsGraphicalUserInterface: false);

            return new CoreAppBuilderResult(
                OutputExecutable: executable,
                PdbFile: pdbPath,
                RuntimesPath: Path.Combine(buildDir, "runtimes"),
                I18nPath: Path.Combine(buildDir, "i18n"),
                OtherDependencyDlls: [..dependencies, ..dependenciesPdb, runtimeConfigPath, depsJsonPath]
            );
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
