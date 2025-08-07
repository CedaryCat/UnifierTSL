using System.Collections.Immutable;

namespace UnifierTSL.Publisher
{
    public class PackageLayoutManager
    {
        static readonly string[] runtimeIdentifiers = ["win-x64", "linux-arm", "linux-arm64", "linux-x64", "osx-x64"];
        public readonly string RID;
        public readonly string PublishPath;
        public readonly string LibraryPath;
        public readonly string RuntimesPath;
        public readonly string AppPath;
        public readonly string PluginsPath;
        public static ImmutableArray<PackageLayoutManager> CreateSupportPackages() => [.. runtimeIdentifiers.Select(rid => new PackageLayoutManager(rid))];
        private PackageLayoutManager(string rid) {
            RID = rid;
            PublishPath = "utsl-" + rid;
            LibraryPath = Path.Combine(PublishPath, "lib");
            RuntimesPath = Path.Combine(PublishPath, "runtimes");
            AppPath = Path.Combine(PublishPath, "app");
            PluginsPath = Path.Combine(PublishPath, "plugins");
            
            if (Directory.Exists(PublishPath)) {
                foreach (var file in Directory.GetFiles(PublishPath)) { 
                    File.Delete(file);
                }
                foreach (var dir in Directory.GetDirectories(PublishPath)) { 
                    Directory.Delete(dir, recursive: true);
                }
            }
            Directory.CreateDirectory(PublishPath);

            Directory.CreateDirectory(LibraryPath);
            Directory.CreateDirectory(RuntimesPath);
            Directory.CreateDirectory(AppPath);
            Directory.CreateDirectory(PluginsPath);
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
    }
    public static class PackageLayoutManagerExt {
        public static async Task InputAppTools(this IEnumerable<PackageLayoutManager> packages, ImmutableArray<string> appPaths) {
            foreach (var package in packages) {
                await package.InputAppTools(appPaths);
            }
        }
        public static async Task InputPlugins(this IEnumerable<PackageLayoutManager> packages, ImmutableArray<string> pluginFiles) {
            foreach (var package in packages) {
                await package.InputPlugins(pluginFiles);
            }
        }
        public static async Task InputCoreProgram(this IEnumerable<PackageLayoutManager> packages, CoreAppBuilderResult result) {
            foreach (var package in packages) {
                await package.InputCoreProgram(result);
            }
        }
    }
}
