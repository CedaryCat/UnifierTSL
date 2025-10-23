using System;
using System.IO;

namespace UnifierTSL.Publisher
{
    public class SolutionDirectoryHelper
    {
        private const int MaxSearchDepth = 5;
        private const string projectName = nameof(UnifierTSL);

        public static readonly string SolutionRoot;
        public static readonly string DefaultOutputPath;

        const string configuration =
#if DEBUG
            "Debug";
#else
            "Release";
#endif

        static SolutionDirectoryHelper() {
            SolutionRoot = FindSolutionRoot();
            DefaultOutputPath = Path.Combine(SolutionRoot, "UnifierTSL.Publisher", "bin", configuration, DotnetSdkHelper.GetTFMString());
        }

        private static string FindSolutionRoot() {
            var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
            if (currentDir.Name is projectName) {
                return FindSolutionRootRecursive(new(Path.Combine(currentDir.FullName, "src")), 0);
            }
            return FindSolutionRootRecursive(currentDir, 0);
        }

        private static string FindSolutionRootRecursive(DirectoryInfo current, int depth) {
            if (depth > MaxSearchDepth) {
                throw new InvalidOperationException(
                    $"Could not find solution root (.sln or .slnx) within {MaxSearchDepth} levels " +
                    $"from execution directory. Started at: {Directory.GetCurrentDirectory()}");
            }

            // Check if any .sln or .slnx files exist in current directory

            if (current.GetFiles().Any(f => f.Name is (projectName + ".sln") or (projectName + ".slnx"))) {
                return current.FullName;
            }

            // Move up one directory
            if (current.Parent == null) {
                throw new InvalidOperationException(
                    $"Could not find solution root (.sln or .slnx) up to filesystem root. " +
                    $"Started at: {Directory.GetCurrentDirectory()}");
            }
            return FindSolutionRootRecursive(current.Parent, depth + 1);
        }
    }
}
