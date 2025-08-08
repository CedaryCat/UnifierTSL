using System.Runtime.InteropServices;

namespace UnifierTSL.Publisher
{
    public static class FileHelpers
    {

        public static async Task SafeCopy(string src, string dest) {
            var destDir = Path.GetDirectoryName(dest)!;
            Directory.CreateDirectory(destDir);
            using var srcStr = File.OpenRead(src);
            using var destStr = File.Create(dest);
            await srcStr.CopyToAsync(destStr);
        }
        public static string ExecutableExtension(string rid) { 
            if (rid.StartsWith("win")) return ".exe";
            if (rid.StartsWith("osx")) return "";
            if (rid.StartsWith("linux")) return "";
            throw new NotSupportedException(rid);
        }
    }
}