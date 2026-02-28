using System.Runtime.InteropServices;

namespace UnifierTSL.FileSystem
{
    public static class FileSystemHelper
    {
        public static string GetExecutableExtension() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "" :
            throw new PlatformNotSupportedException("Unsupported OS platform");
        public static string GetLibraryExtension() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? ".so" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" :
            throw new PlatformNotSupportedException("Unsupported OS platform");
        public static bool FileIsInUse(IOException ex) {
            int HR_FILE_IN_USE = unchecked((int)0x80070020); // ERROR_SHARING_VIOLATION
            int HR_LOCK_VIOLATION = unchecked((int)0x80070021); // ERROR_LOCK_VIOLATION

            return ex.HResult == HR_FILE_IN_USE || ex.HResult == HR_LOCK_VIOLATION;
        }
    }
}
