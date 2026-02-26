using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using UnifierTSL.Extensions;
using UnifierTSL.FileSystem;
using UnifierTSL.Logging;
using UnifierTSL.Module.Dependencies;
using UnifierTSL.Reflection.Metadata;

namespace UnifierTSL.Module
{
    public enum ModuleSearchMode
    {
        Any,
        UpdatedOnly,
        NewOnly,
    }
    public enum ModuleLoadResult
    {
        Success = 0,
        InvalidLibrary,
        AlreadyLoaded,
        ExistingOldVersion,
        CoreModuleNotFound,
        Failed
    }
    public class ModuleAssemblyLoader : ILoggerHost
    {
        private readonly string loadDirectory;
        protected RoleLogger Logger { get; init; }

        public string Name { get; init; } = "ModuleLoader";
        public string? CurrentLogCategory => null;

        public ModuleAssemblyLoader(string modulesDirectory) {
            this.loadDirectory = modulesDirectory;
            Logger = UnifierApi.CreateLogger(this);
        }

        public ImmutableArray<ModulePreloadInfo> PreloadModules(ModuleSearchMode mode = ModuleSearchMode.NewOnly) {
            DirectoryInfo modulesDir = new(loadDirectory);
            modulesDir.Create();

            Dictionary<string, FileInfo> dlls = [];
            foreach (DirectoryInfo dir in modulesDir.GetDirectories()) {
                foreach (FileInfo dll in dir.GetFiles("*.dll")) {
                    dlls[dll.Name] = dll;
                }
            }
            foreach (FileInfo dll in modulesDir.GetFiles("*.dll")) {
                dlls[dll.Name] = dll;
            }

            List<ModulePreloadInfo> modules = [];
            foreach (FileInfo dll in dlls.Values) {
                ModulePreloadInfo? info = PreloadModule(Path.GetRelativePath(Directory.GetCurrentDirectory(), dll.FullName));
                if (info is null) {
                    continue;
                }

                if (mode == ModuleSearchMode.NewOnly) {
                    if (moduleCache.ContainsKey(info.FileSignature.FilePath)) {
                        continue;
                    }
                }
                else if (mode == ModuleSearchMode.UpdatedOnly) {
                    if (moduleCache.TryGetValue(info.FileSignature.FilePath, out LoadedModule? existing) && existing.Signature.Hash == info.FileSignature.Hash) {
                        continue;
                    }
                }
                else if (mode != ModuleSearchMode.Any) {
                    continue;
                }

                modules.Add(info);
            }

            return [.. modules];
        }
        public ModulePreloadInfo? PreloadModule(string dll) {

            if (!Directory.Exists(loadDirectory)) {
                Directory.CreateDirectory(loadDirectory);
            }

            if (!File.Exists(dll)) {
                return null;
            }

            string moduleName;
            bool isCoreModule;
            bool hasDependencies;
            bool isRequiresCoreModule = false;
            string? requiresCoreModule = null;

            using (FileStream stream = File.OpenRead(dll)) {
                using System.Reflection.PortableExecutable.PEReader? reader = MetadataBlobHelpers.GetPEReader(stream);
                if (reader == null) {
                    return null;
                }
                MetadataReader metadataReader = reader.GetMetadataReader();
                moduleName = MetadataBlobHelpers.ReadAssemblyName(metadataReader);
                isCoreModule = MetadataBlobHelpers.HasCustomAttribute(metadataReader, typeof(CoreModuleAttribute).FullName!);
                hasDependencies = MetadataBlobHelpers.HasCustomAttribute(metadataReader, typeof(ModuleDependenciesAttribute<>).FullName!);
                if (MetadataBlobHelpers.TryReadAssemblyAttributeData(metadataReader, typeof(RequiresCoreModuleAttribute).FullName!, out ParsedCustomAttribute reqCoreModuleData)) {
                    isRequiresCoreModule = true;
                    requiresCoreModule = (string?)reqCoreModuleData.ConstructorArguments[0];
                    if (string.IsNullOrWhiteSpace(requiresCoreModule)) {
                        requiresCoreModule = null;
                    }
                }
            }

            if (isRequiresCoreModule && isCoreModule) {
                Logger.Warning(
                    category: null,
                    message: GetParticularString("{0} is module file path, {1} is attribute name (RequiresCoreModuleAttribute)", $"The module '{dll}' is a core module but has a '{typeof(RequiresCoreModuleAttribute).Name}' attribute. Skipping it."));
            }

            if (isRequiresCoreModule && hasDependencies) {
                Logger.Warning(
                    category: null,
                    message: GetParticularString("{0} is module file path, {1} is attribute name (RequiresCoreModuleAttribute)", $"The module '{dll}' with a '{typeof(RequiresCoreModuleAttribute).Name}' attribute should not specify dependencies. Skipping it."));
            }

            if (isRequiresCoreModule && requiresCoreModule is null) {
                Logger.Warning(
                    category: null,
                    message: GetParticularString("{0} is module file path, {1} is attribute name (RequiresCoreModuleAttribute)", $"The module '{dll}' has a '{typeof(RequiresCoreModuleAttribute).Name}' attribute but no module name specified. Skipping it."));
            }

            string fileName = Path.GetFileName(dll);
            string newLocation;

            if (!hasDependencies && !isCoreModule && requiresCoreModule is null) {
                newLocation = Path.Combine(loadDirectory, fileName);
            }
            else {
                string moduleDir = Path.Combine(loadDirectory, (hasDependencies || isCoreModule) ? moduleName : requiresCoreModule!);
                Directory.CreateDirectory(moduleDir);
                newLocation = Path.Combine(moduleDir, Path.GetFileName(dll));
            }

            if (new FileInfo(newLocation).FullName != new FileInfo(dll).FullName) {

                using (FileStream stream = File.OpenRead(dll)) {
                    using (FileStream moved = File.OpenWrite(newLocation)) {
                        stream.CopyTo(moved);
                    }
                }
                File.SetCreationTime(newLocation, File.GetCreationTime(dll));
                File.SetLastWriteTime(newLocation, File.GetLastWriteTime(dll));
                File.SetLastAccessTime(newLocation, File.GetLastAccessTime(dll));
                File.Delete(dll);

                string pdb = Path.ChangeExtension(dll, ".pdb");
                if (File.Exists(pdb)) {
                    string newPdb = Path.ChangeExtension(newLocation, ".pdb");
                    using (FileStream stream = File.OpenRead(pdb)) {
                        using (FileStream moved = File.OpenWrite(newPdb)) {
                            stream.CopyTo(moved);
                        }
                    }
                    File.SetCreationTime(newPdb, File.GetCreationTime(pdb));
                    File.SetLastWriteTime(newPdb, File.GetLastWriteTime(pdb));
                    File.SetLastAccessTime(newPdb, File.GetLastAccessTime(pdb));
                    File.Delete(pdb);
                }
            }

            return new ModulePreloadInfo(FileSignature.Generate(newLocation), moduleName, isCoreModule, hasDependencies, requiresCoreModule);
        }

        private record ModuleInfo(AssemblyLoadContext Context, Assembly Assembly, IDependencyProvider? Dependencies);

        public void ForceUnload(LoadedModule module) {
            if (module.CoreModule is not null) {
                ForceUnload(module.CoreModule);
                return;
            }
            foreach (LoadedModule m in module.GetDependentOrder(true, false)) {
                Logger.Debug($"Unloading module {m.Signature.FilePath}");
                m.Unload();
                moduleCache.Remove(m.Signature.FilePath, out _);
            }
        }

        public bool TryGetExistingModule(string filename, [NotNullWhen(true)] out LoadedModule? module) => moduleCache.TryGetValue(filename, out module);

        public ImmutableArray<LoadedModule> Load(ModuleSearchMode mode = ModuleSearchMode.NewOnly) {
            List<LoadedModule> modules = [];

            ImmutableArray<ModulePreloadInfo> infos = PreloadModules(mode);
            ModulePreloadInfo[] independentModules = infos.Where(x => !x.IsRequiredCoreModule).ToArray();
            ModulePreloadInfo[] requiredCoreModules = infos.Where(x => x.IsRequiredCoreModule).ToArray();

            List<ModulePreloadInfo> failed = [];
            foreach (ModulePreloadInfo? info in independentModules) {
                string fullPath = info.FileSignature.FilePath;
                if (moduleCache.TryGetValue(fullPath, out LoadedModule? cached)) {
                    if (info.FileSignature.Hash != cached.Signature.Hash) {
                        failed.Add(info);
                    }
                    continue;
                }

                ModuleLoadContext context = CreateLoadContext(fullPath);
                context.ResolvingSharedAssemblyPreferred += OnResolvingPreferred;
                context.ResolvingSharedAssemblyFallback += OnResolvingFallback;
                Assembly asm = context.LoadFromStream(fullPath);
                ModuleDependenciesAttribute? dependencyAttr = asm.GetCustomAttribute<ModuleDependenciesAttribute>();
                IDependencyProvider? dependenciesProvider = dependencyAttr?.DependenciesProvider;

                ModuleInfo tmp = new(context, asm, dependenciesProvider);

                if (!UpdateDependencies(info.FileSignature.RelativePath, tmp, out ImmutableArray<ModuleDependency> dependencies)) {
                    context.Unload();
                    failed.Add(info);
                    continue;
                }

                LoadedModule loaded = new(context, asm, dependencies, info.FileSignature, null);
                modules.Add(loaded);
                moduleCache.TryAdd(fullPath, loaded);
            }

            foreach (ModulePreloadInfo? info in requiredCoreModules) {
                if (failed.Any(x => x.ModuleName == info.RequiresCoreModule)) {
                    failed.Add(info);
                    continue;
                }
                LoadedModule? coreModule = moduleCache.Values.FirstOrDefault(x => x.Assembly.GetName().Name == info.RequiresCoreModule);
                if (coreModule is null) {
                    failed.Add(info);
                    continue;
                }
                LoadedModule? match = coreModule.DependentModules.FirstOrDefault(m => m.Signature.FilePath == info.FileSignature.FilePath);
                if (match is not null) {
                    if (match.Signature.Hash == info.FileSignature.Hash) {
                        continue;
                    }
                    else {
                        failed.Add(info);
                    }
                }
                LoadedModule loaded = new(coreModule.Context, coreModule.Context.LoadFromStream(info.FileSignature.FilePath), [], info.FileSignature, coreModule);
                modules.Add(loaded);
                LoadedModule.Reference(coreModule, loaded);
                moduleCache.TryAdd(info.FileSignature.FilePath, loaded);
            }
            return [.. modules];
        }

        private Assembly? OnResolvingPreferred(AssemblyLoadContext context, AssemblyName name) {
            ImmutableArray<LoadedModule> snapshot = moduleCache.Values.ToImmutableArray();
            LoadedModule? module = snapshot.FirstOrDefault(x => x.Context == context);
            if (module is null) {
                return null;
            }
            foreach (LoadedModule? otherModule in snapshot) {
                if (otherModule == module) {
                    continue;
                }
                if (otherModule.Assembly.GetName().FullName == name.FullName) {
                    LoadedModule.Reference(otherModule, module);
                    return otherModule.Assembly;
                }
            }
            return null;
        }

        private Assembly? OnResolvingFallback(AssemblyLoadContext context, AssemblyName name) {
            ImmutableArray<LoadedModule> snapshot = moduleCache.Values.ToImmutableArray();
            LoadedModule? module = snapshot.FirstOrDefault(x => x.Context == context);

            if (module is not null) {
                foreach (LoadedModule? otherModule in snapshot) {
                    if (otherModule == module) {
                        continue;
                    }
                    if (otherModule.Assembly.GetName().Name == name.Name) {
                        LoadedModule.Reference(otherModule, module);
                        return otherModule.Assembly;
                    }
                    if (otherModule.TryProxyLoad(module, name, out Assembly? result)) {
                        return result;
                    }
                }
            }

            Assembly? defaultAsm = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(x => x.GetName().Name == name.Name);
            if (defaultAsm is not null) {
                return defaultAsm;
            }

            return null;
        }

        private ModuleLoadContext CreateLoadContext(string filePath) {
            return new ModuleLoadContext(new FileInfo(filePath));
        }

        private static readonly ConcurrentDictionary<string, LoadedModule> moduleCache = new();
        public bool TryLoadSpecific(ModulePreloadInfo preloadInfo, [NotNullWhen(true)] out LoadedModule? info, out ModuleLoadResult result) {

            string fullPath = preloadInfo.FileSignature.FilePath;
            string path = preloadInfo.FileSignature.RelativePath;

            if (moduleCache.TryGetValue(fullPath, out LoadedModule? cached)) {
                if (cached.Signature.Hash == preloadInfo.FileSignature.Hash) {
                    info = cached;
                    result = ModuleLoadResult.AlreadyLoaded;
                    return true;
                }
                info = null;
                result = ModuleLoadResult.ExistingOldVersion;
                return false;
            }

            LoadedModule loaded;

            if (preloadInfo.RequiresCoreModule is not null) {
                LoadedModule? coreModule = moduleCache.Values.FirstOrDefault(x => x.Assembly.GetName().Name == preloadInfo.RequiresCoreModule);
                if (coreModule is null) {
                    info = null;
                    result = ModuleLoadResult.CoreModuleNotFound;
                    return false;
                }
                ModuleLoadContext context = coreModule.Context;
                Assembly asm = context.LoadFromStream(fullPath);

                loaded = new LoadedModule(context, asm, [], preloadInfo.FileSignature, coreModule);
                LoadedModule.Reference(coreModule, loaded);
            }
            else {
                ModuleLoadContext context = CreateLoadContext(path);
                Assembly asm = context.LoadFromStream(fullPath);
                ModuleDependenciesAttribute? dependencyAttr = asm.GetCustomAttribute<ModuleDependenciesAttribute>();
                IDependencyProvider? dependenciesProvider = dependencyAttr?.DependenciesProvider;

                ModuleInfo tmp = new(context, asm, dependenciesProvider);
                if (!UpdateDependencies(path, tmp, out ImmutableArray<ModuleDependency> dependencies)) {
                    info = null;
                    result = ModuleLoadResult.Failed;
                    return false;
                }
                loaded = new LoadedModule(context, asm, dependencies, preloadInfo.FileSignature, null);
            }

            moduleCache.TryAdd(fullPath, loaded);
            info = loaded;
            result = ModuleLoadResult.Success;
            return true;
        }
        public bool TryLoadSpecific(string filePath, [NotNullWhen(true)] out LoadedModule? info, out ModuleLoadResult result) {
            ModulePreloadInfo? preloadInfo = PreloadModule(filePath);
            if (preloadInfo == null) {
                info = null;
                result = ModuleLoadResult.InvalidLibrary;
                return false;
            }
            return TryLoadSpecific(preloadInfo, out info, out result);
        }

        private bool UpdateDependencies(string dll, ModuleInfo info, out ImmutableArray<ModuleDependency> dependencies) {
            dependencies = [];

            if (string.IsNullOrEmpty(dll)) {
                return false;
            }

            if (info.Dependencies is null) {
                return true;
            }

            string name = Path.GetFileNameWithoutExtension(dll);
            string moduleDir = Path.GetDirectoryName(dll)!;
            DirectoryInfo moduleDirInfo = new(moduleDir);

            if (moduleDirInfo.Name != name) {
                Logger.Warning(
                    category: "UpdateDeps",
                    message: GetParticularString("{0} is module file path (absolute or relative)", $"Module with dependencies must be in the same moduleDirInfo as the module to store dependencies.\r\nModule File: {dll}"));

                return false;
            }

            moduleDirInfo.Create();

            DependenciesConfiguration prevConfig = new(Logger, DependenciesConfiguration.LoadDependenicesConfig(moduleDir));
            prevConfig.NormalizeDependenicesConfig(moduleDir);

            try {
                dependencies = info.Dependencies.GetDependencies()?.ToImmutableArray() ?? [];
            }
            catch (Exception ex) {
                Logger.LogHandledExceptionWithMetadata(
                    category: "ExtractDeps",
                    message: GetParticularString("{0} is module file path (relative path)", $"Failed to extract temporary dependencies of module '{dll}'."),
                    ex: ex,
                    metadata: [new("ModuleFile", dll)]);

                return false;
            }

            DependenciesSetting currentSetting = new() {
                EnableAggressiveCleanUp = prevConfig.Setting.EnableAggressiveCleanUp,
                Dependencies = []
            };

            try {
                /*
                 * This method updates plugin dependencies safely, handling potential file locks on Windows
                 * caused by AssemblyLoadContext. Key points:
                 *
                 * 1. Detects which dependencies are new or have updated versions compared to previous configuration.
                 * 2. Extracts library files for updated dependencies and tracks the highest version of each file.
                 * 3. Attempts to copy each dependency file to the plugin directory.
                 *    - If a file is locked (common with Windows AssemblyLoadContext), the new version is saved
                 *      using a "Name.Version.Extension" format to avoid overwriting the locked file.
                 *    - The configuration is updated to reference both the old locked file and the new versioned file,
                 *      enabling proper tracking and future cleanup.
                 * 4. Ensures all streams are safely disposed and exceptions are handled or propagated appropriately.
                 *
                 * This approach ensures that updated dependencies can be deployed even when previous assemblies
                 * are still loaded, preventing file access conflicts while maintaining an accurate configuration state.
                 */

                // Dictionary to track the highest version of each dependency file encountered
                Dictionary<string, (ModuleDependency dependency, LibraryEntry item)> highestVersion = [];

                foreach (ModuleDependency dependency in dependencies) {
                    bool update = false;

                    // Check if this dependency is new or has changed since the previous configuration
                    if (!prevConfig.Setting.Dependencies.TryGetValue(dependency.Name, out DependencyRecord? existingDependency)) {
                        update = true;
                    }
                    else if (dependency.Name != existingDependency.Name) {
                        update = true;
                    }
                    else if (dependency.Version != existingDependency.Version) {
                        update = true;
                    }

                    if (update) {
                        // Extract the library files for the current dependency
                        ImmutableArray<LibraryEntry> items = dependency.LibraryExtractor.Extract(Logger);

                        foreach (LibraryEntry item in items) {
                            (ModuleDependency dependency, LibraryEntry item) group = (dependency, item);

                            // Keep track of the highest version for each file path
                            if (!highestVersion.TryAdd(item.FilePath, group)) {
                                if (highestVersion[item.FilePath].item.Version < item.Version) {
                                    highestVersion[item.FilePath] = group;
                                }
                            }
                        }

                        // Update the current configuration with the new dependency info
                        currentSetting.Dependencies[dependency.Name] = new DependencyRecord {
                            Name = dependency.Name,
                            Version = dependency.Version,
                            Manifests = [.. items.Select(x => new DependencyItem(x.FilePath, x.Version))]
                        };
                    }
                    else {
                        // If no update is needed, retain the previous configuration
                        currentSetting.Dependencies[dependency.Name] = prevConfig.Setting.Dependencies[dependency.Name];
                    }
                }

                // Copy the highest version of each dependency file to the plugin directory
                foreach (KeyValuePair<string, (ModuleDependency dependency, LibraryEntry item)> pair in highestVersion) {
                    string relativeDepPath = pair.Key;
                    (ModuleDependency dependency, LibraryEntry item) = pair.Value;

                    using Stream source = item.Stream.Value;
                    FileStream? destination = null;

                    try {
                        // Attempt to create the destination file safely
                        destination = Utilities.IO.SafeFileCreate(Path.Combine(moduleDir, relativeDepPath), out Exception? ex);

                        if (destination is not null) {
                            // Copy the dependency file to the destination if no conflicts occurred
                            source.CopyTo(destination);
                        }
                        else {
                            // If file creation failed, check if it's due to file being locked by Windows (common with AssemblyLoadContext)
                            if (prevConfig.Setting.Dependencies.TryGetValue(relativeDepPath, out DependencyRecord? prevDependencyConf)
                                && prevDependencyConf.Manifests.Any(x => x.FilePath == item.FilePath)
                                && ex is IOException ioEx && FileSystemHelper.FileIsInUse(ioEx)) {

                                DependencyItem prevItem = prevDependencyConf.Manifests.First(x => x.FilePath == item.FilePath);
                                // Generate a new path including the version number to avoid file lock conflicts
                                string newPath = Path.ChangeExtension(item.FilePath, $"{item.Version}{Path.GetExtension(item.FilePath)}");

                                DependencyItem? currentItem = prevDependencyConf.Manifests.FirstOrDefault(x => x.FilePath == newPath && x.Version == item.Version);
                                if (currentItem is null) {
                                    currentItem = new DependencyItem(newPath, item.Version);

                                    destination = Utilities.IO.SafeFileCreate(Path.Combine(moduleDir, newPath), out ex);
                                    if (destination is null) {
                                        if (ex is not null) {
                                            throw ex;
                                        }
                                        else {
                                            throw new Exception($"Failed to create file '{Path.Combine(moduleDir, newPath)}'");
                                        }
                                    }
                                    // Copy the file content to the new versioned file
                                    source.CopyTo(destination);
                                }
                                // Adjust current configuration:
                                // 1. Remove the old manifest entry for the locked file
                                // 2. Add the old dependency entry for tracking
                                // 3. Add the new versioned dependency entry
                                currentSetting.Dependencies[dependency.Name].Manifests.RemoveAll(x => x.FilePath == item.FilePath);
                                prevItem.Obsolete = true;
                                currentSetting.Dependencies[dependency.Name].Manifests.Add(prevItem);
                                currentSetting.Dependencies[dependency.Name].Manifests.Add(currentItem);

                            }
                            else if (ex is not null) {
                                // Propagate other exceptions
                                throw ex;
                            }
                            else {
                                throw new Exception($"Failed to create file '{Path.Combine(moduleDir, relativeDepPath)}'");
                            }
                        }
                    }
                    finally {
                        destination?.Dispose();
                    }
                }
            }
            catch (Exception ex) {
                Logger.LogHandledExceptionWithMetadata(
                    category: "ExtractDeps",
                    message: GetParticularString("{0} is module file path (relative path)", $"Failed to extract dependencies files of module '{dll}'."),
                    ex: ex,
                    metadata: [new("ModuleFile", dll)]);

                return false;
            }

            currentSetting.EnableAggressiveCleanUp = prevConfig.Setting.EnableAggressiveCleanUp;
            DependenciesConfiguration currentConfig = new(Logger, currentSetting);
            currentConfig.SpecificDependencyClean(moduleDir, prevConfig.Setting);
            currentConfig.Save(moduleDir);

            return true;
        }
    }
}
