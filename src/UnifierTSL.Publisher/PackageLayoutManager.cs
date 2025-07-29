using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Publisher
{
    public class PackageLayoutManager
    {
        public readonly string PublishPath;
        public readonly string LibraryPath;
        public readonly string RuntimesPath;
        public readonly string AppPath;
        public readonly string PluginsPath;

        public PackageLayoutManager() {
            PublishPath = "utsl-publish";
            LibraryPath = Path.Combine(PublishPath, "libs");
            RuntimesPath = Path.Combine(PublishPath, "runtimes");
            AppPath = Path.Combine(PublishPath, "app");
            PluginsPath = Path.Combine(PublishPath, "plugins");
            
            if (Directory.Exists(PublishPath)) {
                Directory.Delete(PublishPath, recursive: true);
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

        public async Task InputCoreProgram(CoreAppBuilderResult result) { 
            var fileName = Path.GetFileName(result.OutputExecutable);
            var pdbName = Path.GetFileName(result.PdbFile);

            await FileHelpers.SafeCopy(result.OutputExecutable, Path.Combine(PublishPath, fileName));
            await FileHelpers.SafeCopy(result.PdbFile, Path.Combine(PublishPath, pdbName));

            var copyTasks = result.OtherDependencyDlls.Select(async dep => {
                var fileName = Path.GetFileName(dep);
                var destPath = Path.Combine(LibraryPath, fileName);
                await FileHelpers.SafeCopy(dep, destPath);
            });

            await Task.WhenAll(copyTasks);
        }
    }
}
