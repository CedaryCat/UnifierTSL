using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace UnifierTSL.Publisher
{
    public class PackageLayoutManager
    {
        public readonly string RID;
        public readonly string PublishPath;
        public readonly string LibraryPath;
        public readonly string RuntimesPath;
        public readonly string AppPath;
        public readonly string PluginsPath;
        public readonly string I18nPath;

        public PackageLayoutManager(string rid, string outputPath = ".", bool useRidFolder = true, bool cleanOutputDir = true) {
            RID = rid;

            PublishPath = useRidFolder ? Path.Combine(outputPath, "utsl-" + RID) : outputPath;

            LibraryPath = Path.Combine(PublishPath, "lib");
            RuntimesPath = Path.Combine(PublishPath, "runtimes");
            AppPath = Path.Combine(PublishPath, "app");
            PluginsPath = Path.Combine(PublishPath, "plugins");
            I18nPath = Path.Combine(PublishPath, "i18n");

            // Handle directory cleanup based on cleanOutputDir flag
            if (Directory.Exists(PublishPath)) {
                if (cleanOutputDir) {
                    // Delete entire directory contents recursively with proper error handling
                    // First delete all files, then delete empty directories to handle file locking gracefully
                    DeleteDirectoryContents(PublishPath);
                }
                // If cleanOutputDir is false, do nothing - preserve existing files
                // FileHelpers.SafeCopy will create or overwrite files as needed
            }

            Directory.CreateDirectory(PublishPath);
            Directory.CreateDirectory(LibraryPath);
            Directory.CreateDirectory(RuntimesPath);
            Directory.CreateDirectory(AppPath);
            Directory.CreateDirectory(PluginsPath);
            Directory.CreateDirectory(I18nPath);
        }

        public async Task InputAppTools(ImmutableArray<string> appPaths) {

            var copyTasks = appPaths.Select(async app =>
            {
                var fileName = Path.GetFileName(app);
                var destPath = Path.Combine(AppPath, fileName);
                await FileHelpers.SafeCopy(app, destPath);
            });

            await Task.WhenAll(copyTasks);
        }

        public async Task InputPlugins(ImmutableArray<string> pluginFiles) {

            var copyTasks = pluginFiles.Select(async file => {
                var fileName = Path.GetFileName(file);
                var destPath = Path.Combine(PluginsPath, fileName);
                await FileHelpers.SafeCopy(file, destPath);
            });

            await Task.WhenAll(copyTasks);
        }

        public async Task InputCoreProgram(CoreAppBuilderResult result) {
            var fileName = Path.GetFileName(result.OutputExecutable);
            var pdbName = Path.GetFileName(result.PdbFile);

            await FileHelpers.SafeCopy(result.OutputExecutable, Path.Combine(PublishPath, fileName));
            await FileHelpers.SafeCopy(result.PdbFile, Path.Combine(PublishPath, pdbName));

            await Task.WhenAll(result.OtherDependencyDlls.Select(async dep => {
                var fileName = Path.GetFileName(dep);
                var destPath = Path.Combine(LibraryPath, fileName);
                await FileHelpers.SafeCopy(dep, destPath);
            }));

            await MoveMOFiles(result.I18nPath);

            var sourceRidDir = new DirectoryInfo(Path.Combine(result.RuntimesPath, RID));
            if (sourceRidDir.Exists) {
                var nativeDir = new DirectoryInfo(Path.Combine(sourceRidDir.FullName, "native"));
                if (nativeDir.Exists) {
                    await Task.WhenAll(nativeDir.GetFiles().Select(async file => {
                        var fileName = Path.GetFileName(file.FullName);
                        var destPath = Path.Combine(RuntimesPath, RID, "native", fileName);
                        await FileHelpers.SafeCopy(file.FullName, destPath);
                    }));
                }
                var libDir = new DirectoryInfo(Path.Combine(sourceRidDir.FullName, "lib"));
                if (libDir.Exists) {
                    await Task.WhenAll(libDir.GetFiles("*", SearchOption.AllDirectories).Select(async file => {
                        var fileName = Path.GetFileName(file.FullName);
                        var destPath = Path.Combine(LibraryPath, fileName);
                        await FileHelpers.SafeCopy(file.FullName, destPath);
                    }));
                }
            }
        }

        async Task MoveMOFiles(string sourceI18nPath) {
            var moFiles = Directory.GetFiles(sourceI18nPath, "*.mo", SearchOption.AllDirectories);
            var copyTasks = moFiles.Select(async moFile => {
                // Get the relative path from the source i18n directory
                var relativePath = Path.GetRelativePath(sourceI18nPath, moFile);
                var destPath = Path.Combine(I18nPath, relativePath);
                await FileHelpers.SafeCopy(moFile, destPath);
            });

            await Task.WhenAll(copyTasks);
        }

        /// <summary>
        /// Recursively deletes all contents of a directory, handling file locking gracefully.
        /// Deletes files first, then attempts to delete empty directories.
        /// </summary>
        static void DeleteDirectoryContents(string dirPath) {
            var dirInfo = new DirectoryInfo(dirPath);
            if (!dirInfo.Exists) return;

            // Delete all files in the directory
            foreach (var file in dirInfo.GetFiles()) {
                try {
                    file.Delete();
                } catch {
                    // Ignore errors when deleting individual files
                    // This allows the operation to continue even if some files are locked
                }
            }

            // Recursively delete subdirectories
            foreach (var subDir in dirInfo.GetDirectories()) {
                DeleteDirectoryContents(subDir.FullName);
                try {
                    subDir.Delete();
                } catch {
                    // Ignore errors when deleting individual directories
                    // This allows the operation to continue even if some directories can't be deleted
                }
            }
        }
    }
}
