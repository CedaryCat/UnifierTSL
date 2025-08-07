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
        public CoreAppBuilderResult Build() {
            var targetFrameworkDir = new DirectoryInfo(Directory.GetCurrentDirectory());
            var solutionDir = targetFrameworkDir
                .Parent! // configuration (Debug/Release)
                .Parent! // bin
                .Parent! // project root
                .Parent! // solution root
                .FullName;

            var projectPath = Path.Combine(solutionDir, relativeProjectPath);
            var projectName = Path.GetFileNameWithoutExtension(relativeProjectPath);

            var projectDir = Path.GetDirectoryName(projectPath)!;
            var buildDir = Path.Combine(projectDir, "bin", "Release", targetFrameworkDir.Name);

            var publishDir = Path.Combine("core-publish", projectName);

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

            // Step 2: Find main outputs
            var dllPath = Path.Combine(buildDir, $"{projectName}.dll");
            var runtimeConfigPath = Path.Combine(buildDir, $"{projectName}.runtimeconfig.json");
            var depsJsonPath = Path.Combine(buildDir, $"{projectName}.deps.json");
            var pdbPath = Path.Combine(buildDir, $"{projectName}.pdb");

            // Step 3: Copy dependencies
            var dependencies = Directory.GetFiles(buildDir, "*.dll")
                .Select(f => Path.Combine(buildDir, f))
                .ToArray();
            var dependenciesPdb = Directory.GetFiles(buildDir, "*.pdb")
                .Select(f => Path.Combine(buildDir, f))
                .ToArray();

            // Step 4: Generate executable using AppHost
            var appHostTemplate = DotnetSdkHelper.GetBestMatchedAppHostPath();

            var outputExe = Path.Combine(publishDir, $"{projectName}.exe");

            HostWriter.CreateAppHost(
                appHostSourceFilePath: appHostTemplate,
                appHostDestinationFilePath: outputExe,
                appBinaryFilePath: Path.Combine("lib", $"{projectName}.dll"),
                windowsGraphicalUserInterface: false);

            return new CoreAppBuilderResult(
                OutputExecutable: outputExe,
                PdbFile: pdbPath,
                RuntimesPath: Path.Combine(buildDir, "runtimes"),
                OtherDependencyDlls: [..dependencies, ..dependenciesPdb, runtimeConfigPath, depsJsonPath]
            );
        }
    }

    public record CoreAppBuilderResult(
        string OutputExecutable,
        string PdbFile,
        string RuntimesPath,
        string[] OtherDependencyDlls
    );
}
