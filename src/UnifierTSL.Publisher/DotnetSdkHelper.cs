using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UnifierTSL.Publisher
{
    public static class DotnetSdkHelper
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
          
            string appHostFileName = "apphost" + FileHelpers.ExecutableExtension(rid);
          
            string apphostPath = Path.Combine(matchedSdkPath, "AppHostTemplate", appHostFileName);

            if (!File.Exists(apphostPath))
                throw new FileNotFoundException($"{appHostFileName} not found at path: {apphostPath}");

            return apphostPath;
        }

        private static Version GetCurrentRuntimeVersion() {
            return Environment.Version; 
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
}

