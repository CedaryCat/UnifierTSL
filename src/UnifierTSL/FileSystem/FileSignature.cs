using System.Security.Cryptography;

namespace UnifierTSL.FileSystem
{
    public record FileSignature(string FilePath, string Hash, DateTime LastWriteTimeUtc) {
        public string FilePath { get; init; } = new FileInfo(FilePath).FullName;
        public string RelativePath => Path.GetRelativePath(Directory.GetCurrentDirectory(), FilePath);
        public static FileSignature Generate(string filePath) {
            using var sha256 = SHA256.Create();
            byte[] hashBytes;

            using (var stream = File.OpenRead(filePath)) {
                hashBytes = sha256.ComputeHash(stream);
            }

            string hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            DateTime lastWriteTime = File.GetLastWriteTimeUtc(filePath);

            return new FileSignature(filePath, hashString, lastWriteTime);
        }
        public bool QuickEquals(string filePath) {
            var lastWriteTime = File.GetLastWriteTimeUtc(filePath);
            return string.Equals(FilePath, filePath, StringComparison.OrdinalIgnoreCase)
                && LastWriteTimeUtc == lastWriteTime;
        }

        public bool ExactEquals(string filePath) {
            var lastWriteTime = File.GetLastWriteTimeUtc(filePath);
            if (LastWriteTimeUtc != lastWriteTime)
                return false;

            string hash = ComputeHash(filePath);
            return Hash == hash;
        }

        public bool ContentEquals(string filePath) {
            string hash = ComputeHash(filePath);
            return Hash == hash;
        }
        public static string ComputeHash(string filePath) {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hashBytes = sha256.ComputeHash(stream);
            return Convert.ToHexStringLower(hashBytes);
        }
    }
}
