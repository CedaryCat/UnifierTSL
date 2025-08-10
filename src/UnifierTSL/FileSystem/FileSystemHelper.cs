using System.Runtime.InteropServices;

namespace UnifierTSL.FileSystem
{
    public static class FileSystemHelper
    {
        public static string GetExecutableExtension() => (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
        public static string GetDynamicLibraryExtension() => 
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)? ".dll" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux)? ".so" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)? ".dylib" :
            throw new PlatformNotSupportedException("Unsupported OS platform");
        public static string GetDynamicLibraryPrefix() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "" : "lib";
        public static string GetDynamicLibraryFileName(string name, bool withPrefix = true) => $"{(withPrefix ? GetDynamicLibraryPrefix() : "")}{name}{GetDynamicLibraryExtension()}";
    }
}
