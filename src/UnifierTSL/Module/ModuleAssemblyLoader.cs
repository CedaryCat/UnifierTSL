using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using UnifierTSL.Logging;
using UnifierTSL.Module.Dependencies;
using UnifierTSL.Reflection.Metadata;

namespace UnifierTSL.Module
{
    public abstract class ModuleAssemblyLoader : ILoggerHost
    {
        private readonly string loadDirectory;
        protected RoleLogger Logger { get; init; }

        public abstract string Name { get; }
        public abstract string CurrentLogCategory { get; set; }

        public ModuleAssemblyLoader(string modulesDirectory) {
            this.loadDirectory = modulesDirectory;
            Logger = UnifierApi.CreateLogger(this);
        }

        public abstract ModuleLoadContext CreateLoadContext();

        private void PreloadModules() {
            var modulesDir = new DirectoryInfo(loadDirectory);
            modulesDir.Create();

            foreach (var dll in Directory.GetFiles(loadDirectory, "*.dll")) {
                using (var stream = File.OpenRead(dll)) {
                    if (!MetadataBlobHelpers.HasCustomAttribute(stream, nameof(ModuleDependenciesAttribute<>))) {
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(dll);
                    var moduleDir = Path.Combine(loadDirectory, name);
                    Directory.CreateDirectory(moduleDir);

                    using var moved = File.Create(Path.Combine(moduleDir, name + ".dll"));
                    stream.CopyTo(moved);
                }
                File.Delete(dll);
            }
        }
        private ImmutableArray<string> GetModulesPaths() {
            List<string> modules = [];

            foreach (var dll in Directory.GetFiles(loadDirectory, "*.dll")) {
                if (!MetadataBlobHelpers.IsManagedAssembly(dll)) {
                    continue;
                }

                modules.Add(dll);
            }

            var modulesDir = new DirectoryInfo(loadDirectory);
            foreach (var directory in modulesDir.GetDirectories()) {
                var dll = Path.Combine(loadDirectory, directory.Name, directory.Name + ".dll");
                if (!File.Exists(dll) || !MetadataBlobHelpers.IsManagedAssembly(dll)) {
                    continue;
                }

                modules.Add(dll);
            }

            return [.. modules];
        }
        record ModuleInfo(AssemblyLoadContext Context, Assembly Assembly, IDependencyProvider? Dependencies);
        public ImmutableArray<ModuleAssemblyInfo> Load() {
            List<ModuleAssemblyInfo> modules = [];

            PreloadModules();

            foreach (var dll in GetModulesPaths()) {
                var context = CreateLoadContext();
                var asm = context.LoadFromAssemblyPath(dll);
                var dependencyAttr = asm.GetCustomAttribute<ModuleDependenciesAttribute>();
                var dependenciesProvider = dependencyAttr?.DependenciesProvider;

                var info = new ModuleInfo(context, asm, dependenciesProvider);

                if (!UpdateDependencies(dll, info, out var dependencies)) { 
                    continue;
                }

                modules.Add(new ModuleAssemblyInfo(context, asm, dependencies));
            }

            return [.. modules];
        }
        static DependenciesConfiguration LoadDependenicesConfig(string pluginDirectory) {
            var configPath = Path.Combine(pluginDirectory, "dependencies.json");
            if (!File.Exists(configPath)) {
                return new DependenciesConfiguration { Dependencies = [] };
            }
            return JsonSerializer.Deserialize<DependenciesConfiguration>(File.ReadAllText(configPath))!;
        }
        void NormalizeDependenicesConfig(DependenciesConfiguration configuration, string pluginDirectory) {
            foreach (var pair in configuration.Dependencies.ToArray()) {
                var relativeFilePath = pair.Key;
                var dependency = pair.Value;

                if (!File.Exists(Path.Combine(pluginDirectory, relativeFilePath))) {
                    configuration.Dependencies.Remove(relativeFilePath);
                    Logger.Debug(
                        category: "ConfNormalize",
                        message: "The dependencies file does not exist in the bin pluginDirInfo, removing from the configuration.");
                }

                if (!dependency.IsNativeLibrary) {
                    string? name;
                    Version? version;
                    using (var stream = File.OpenRead(Path.Combine(pluginDirectory, relativeFilePath))) {
                        // Try to read the assembly's identity (name and version) from the file.
                        if (!MetadataBlobHelpers.TryReadAssemblyIdentity(stream, out name, out version)) {
                            continue;
                        }
                    }

                    // If the assembly name in the file does not match the expected dependencies name,
                    // it likely means the file has been modified in a breaking way,
                    // and any original dependent plugins can no longer reliably depend on it.
                    if (dependency.Name != name) {
                        configuration.Dependencies.Remove(relativeFilePath);
                        Logger.Warning(
                            category: "ConfNormalize",
                            message: $"The assembly name ({name}) in the file does not match the expected dependencies name ({dependency.Name}).");
                    }
                }
            }
        }
        /// <summary>
        /// Ignore exceptions caused by external file access issues (e.g., file is in use or locked by another process).
        /// These are not programming errors and should not cause the application to crash.
        /// </summary>
        /// <param name="ex"></param>
        /// <returns></returns>
        static bool IgnoreThisIOError(IOException ex) {
            int HR_FILE_IN_USE = unchecked((int)0x80070020); // ERROR_SHARING_VIOLATION
            int HR_LOCK_VIOLATION = unchecked((int)0x80070021); // ERROR_LOCK_VIOLATION

            return ex.HResult == HR_FILE_IN_USE || ex.HResult == HR_LOCK_VIOLATION;
        }
        void AggressiveDependencyClean(DirectoryInfo pluginDirInfo, DependenciesConfiguration currentConfig) {
            foreach (var file in pluginDirInfo.GetFiles("*.*", SearchOption.AllDirectories)) {
                if (file.FullName == Path.Combine(pluginDirInfo.FullName, "dependencies.json")) {
                    continue;
                }

                if (currentConfig.Dependencies.Keys.Any(path => Path.Combine(pluginDirInfo.FullName, path) == file.FullName)) {
                    continue;
                }

                var deletePath = Path.GetRelativePath(pluginDirInfo.Parent!.FullName, file.FullName);
                try {
                    File.Delete(file.FullName);
                    Logger.Debug(
                        category: "DepsCleanUp",
                        message: $"Deleted unused dependencies file '{deletePath}'");
                }
                catch (IOException ex) when (IgnoreThisIOError(ex)) {
                    Logger.Warning(
                        category: "DepsCleanUp",
                        message: $"Failed to delete unused dependencies file '{deletePath}'.\r\n" +
                        $"It is likely that the file is in use by another process. you can try to delete it manually.");
                }
                catch {
                    throw;
                }
            }
            Utilities.IO.RemoveEmptyDirectories(pluginDirInfo.FullName, false);
        }
        void SafeDependencyClean(DirectoryInfo pluginDirInfo, DependenciesConfiguration prevConfig, DependenciesConfiguration currentConfig) {
            foreach (var oldDependenicyPair in prevConfig.Dependencies) {
                if (!currentConfig.Dependencies.ContainsKey(oldDependenicyPair.Key)) {
                    try {
                        File.Delete(Path.Combine(pluginDirInfo.FullName, oldDependenicyPair.Key));
                        Logger.Debug(
                            category: "DepsCleanUp",
                            message: $"Deleted unused dependencies file '{Path.Combine("bin", oldDependenicyPair.Key)}'");
                    }
                    catch (IOException ex) when (IgnoreThisIOError(ex)) {
                        Logger.Warning(
                            category: "DepsCleanUp",
                            message: $"Failed to delete unused dependencies file '{Path.Combine("runtimes", oldDependenicyPair.Key)}'.\r\n" +
                            $"It is likely that the file is in use by another process. you can try to delete it manually.");
                    }
                    catch {
                        throw;
                    }
                }
            }
            Utilities.IO.RemoveEmptyDirectories(pluginDirInfo.FullName, false);
        }

        bool UpdateDependencies(string dll, ModuleInfo info, out ImmutableArray<ModuleDependency> dependencies) {
            dependencies = [];

            if (string.IsNullOrEmpty(dll)) {
                return false;
            }

            if (info.Dependencies is null) {
                return true;
            }

            var name = Path.GetFileNameWithoutExtension(dll);
            var pluginDir = Path.GetDirectoryName(dll)!;
            var pluginDirInfo = new DirectoryInfo(pluginDir);

            if (pluginDirInfo.Name != name) {
                Logger.Warning(
                    category: "UpdateDeps",
                    message: "Module with dependencies must be in the same pluginDirInfo as the module to store dependencies.\r\n" +
                            $"Module File: {dll}");

                return false;
            }

            pluginDirInfo.Create();

            var prevConfig = LoadDependenicesConfig(pluginDir);
            NormalizeDependenicesConfig(prevConfig, pluginDir);

            try {
                dependencies = info.Dependencies.GetDependencies()?.ToImmutableArray() ?? [];
            }
            catch (Exception ex) {
                Logger.LogHandledExceptionWithMetadata(
                    category: "ExtractDeps",
                    message: $"Failed to extract dependencies info of module '{dll}'.",
                    ex: ex,
                    metadata: [new("ModuleFile", dll)]);

                return false;
            }

            var currentConfig = new DependenciesConfiguration {
                EnableAggressiveCleanUp = prevConfig.EnableAggressiveCleanUp,
                Dependencies = []
            };

            try {
                foreach (var dependency in dependencies) {
                    var relativeDepPath = dependency.ExpectedPath;
                    bool update = false;

                    if (!prevConfig.Dependencies.TryGetValue(relativeDepPath, out var existingDependency)) {
                        update = true;
                    }
                    else if (dependency.Name != existingDependency.Name) {
                        update = true;
                    }
                    else if (dependency.Version != existingDependency.Version) {
                        update = true;
                    }

                    if (update) {
                        using var source = dependency.LibraryExtractor.Extract();
                        using var destination = Utilities.IO.SafeFileCreate(Path.Combine(pluginDir, relativeDepPath));
                        source.CopyTo(destination);
                    }

                    currentConfig.Dependencies[relativeDepPath] = new DependencyConfEntry {
                        Name = dependency.Name,
                        IsNativeLibrary = dependency.Kind is DependencyKind.NativeLibrary,
                        Version = dependency.Version,
                    };
                }
            }
            catch (Exception ex) {
                Logger.LogHandledExceptionWithMetadata(
                    category: "ExtractDeps",
                    message: $"Failed to extract dependencies files of module '{dll}'.",
                    ex: ex,
                    metadata: [new("ModuleFile", dll)]);

                return false;
            }

            if (prevConfig.EnableAggressiveCleanUp) {
                currentConfig.EnableAggressiveCleanUp = true;
                AggressiveDependencyClean(pluginDirInfo, currentConfig);
            }
            else {
                currentConfig.EnableAggressiveCleanUp = false;
                SafeDependencyClean(pluginDirInfo, prevConfig, currentConfig);
            }

            // Write updated config to disk
            var configPath = Path.Combine(pluginDirInfo.FullName, "dependencies.json");
            File.WriteAllText(configPath, JsonSerializer.Serialize(currentConfig, serializerOptions));

            return true;
        }

        private static readonly JsonSerializerOptions serializerOptions = new() { WriteIndented = true };
    }
}
