using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UnifierTSL.Publisher
{
    public static class DotnetSdkHelper
    {
        public static string GetBestMatchedAppHostPath() {
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
            string apphostPath = Path.Combine(matchedSdkPath, "AppHostTemplate", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "apphost.exe" : "apphost");

            if (!File.Exists(apphostPath))
                throw new FileNotFoundException($"apphost not found at path: {apphostPath}");

            return apphostPath;
        }

        private static Version GetCurrentRuntimeVersion() {
            return Environment.Version; 
        }

        private static string GetDotnetSdkBasePath() {
            string basePath;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "sdk");
            else
                basePath = "/usr/share/dotnet/sdk";

            return basePath;
        }
    }
}

