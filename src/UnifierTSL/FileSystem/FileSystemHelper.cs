using System.Runtime.InteropServices;

namespace UnifierTSL.FileSystem
{
    public static class FileSystemHelper
    {
        public static string GetExecutableExtension() => (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
    }
}
