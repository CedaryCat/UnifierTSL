using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text.Json;
using UnifierTSL.Logging;
using UnifierTSL.PluginService.Dependencies;
using UnifierTSL.Reflection.Metadata;

namespace UnifierTSL.PluginService.Loading
{
    internal class PluginDependenicesManager : ILoggerHost
    {
        private static readonly JsonSerializerOptions serializerOptions = new() { WriteIndented = true };

        private readonly Logger logCore;
        private readonly RoleLogger Logger;
        private readonly DirectoryInfo directory;
        private readonly IReadOnlyList<PluginTypeInfo> pluginInfos;

        public string Name => "PluginDepMgr";
        public string? CurrentLogCategory => null;

        public PluginDependenicesManager(Logger logger, DirectoryInfo directory, IReadOnlyList<PluginTypeInfo> pluginInfos) {
            this.directory = directory;
            this.pluginInfos = pluginInfos;
            logCore = logger;
            Logger = UnifierApi.CreateLogger(this, logger);
        }

        class PluginEntry
        {
            public required string Name { get; set; }
            public required Version Version { get; set; }
        }
        static class ManagedDependenicy {
            class DependenicyEntry
            {
                public required string FilePathRelativeToBinDir { get; set; }
                public required Version Version { get; set; }
                public required List<PluginEntry> DependentPlugins { get; set; }
            }
            class DependenciesConfiguration
            {
                public required Dictionary<string, DependenicyEntry> Dependencies { get; set; } 
            }
            static DependenciesConfiguration LoadDependenicesConfig(DirectoryInfo binDirectory) {
                var configPath = Path.Combine(binDirectory.FullName, "dependencies.json");
                if (!File.Exists(configPath)) {
                    return new DependenciesConfiguration { Dependencies = [] };
                }
                return JsonSerializer.Deserialize<DependenciesConfiguration>(File.ReadAllText(configPath))!;
            }
            static void NormalizeDependenicesConfig(RoleLogger logger, DependenciesConfiguration configuration, DirectoryInfo binDirectory) {
                foreach (var kv in configuration.Dependencies.ToArray()) {
                    var dependencyName = kv.Key;
                    var dependency = kv.Value;

                    // If the dependencies file does not exist in the bin directory, remove it from the configuration.
                    if (!File.Exists(Path.Combine(binDirectory.FullName, dependency.FilePathRelativeToBinDir))) {
                        configuration.Dependencies.Remove(dependencyName);
                        logger.Debug(
                            category: "ManagedConfNormalize", 
                            message: "The dependencies file does not exist in the bin directory, removing from the configuration.");
                    }

                    string? name;
                    Version? version;
                    using (var stream = File.OpenRead(Path.Combine(binDirectory.FullName, dependency.FilePathRelativeToBinDir))) {
                        // Try to read the assembly's identity (name and version) from the file.
                        if (!MetadataBlobHelpers.TryReadAssemblyIdentity(stream, out name, out version)) {
                            continue;
                        }
                    }

                    // If the assembly name in the file does not match the expected dependencies name,
                    // it likely means the file has been modified in a breaking way,
                    // and any original dependent plugins can no longer reliably depend on it.
                    if (dependencyName != name) {
                        dependency.DependentPlugins = [];
                        logger.Warning(
                            category: "ManagedConfNormalize",
                            message: $"The assembly name ({name}) in the file does not match the expected dependencies name ({dependencyName}).");
                    }
                }
            }
            public static void UpdateDependencies(
                RoleLogger logger,
                DirectoryInfo binDirectory,
                IReadOnlyList<(PluginTypeInfo Info, IReadOnlyList<PluginDependency> Dependencies)> pluginInfos,
                out IReadOnlyList<PluginTypeInfo> failedPlugins) {

                var failed = new List<PluginTypeInfo>();

                var prevConfig = LoadDependenicesConfig(binDirectory);
                NormalizeDependenicesConfig(logger, prevConfig, binDirectory);

                // Mapping from the target file path to all plugin-provided managed dependencies for that path
                var pathToDependencyMap = new Dictionary<string, List<(PluginTypeInfo Provider, PluginDependency Dependency)>>();

                foreach (var (pluginInfo, dependencies) in pluginInfos) {
                    var managedDependencies = dependencies
                        .Where(dep => dep is not NativeEmbeddedDependency)
                        .ToArray();

                    if (managedDependencies.Length == 0)
                        continue;

                    foreach (var dependency in managedDependencies) {
                        var fullPath = Path.Combine(binDirectory.FullName, dependency.ExpectedPath);

                        if (!pathToDependencyMap.TryGetValue(fullPath, out var list)) {
                            list = [];
                            pathToDependencyMap[fullPath] = list;
                        }

                        list.Add((pluginInfo, dependency));
                    }
                }

                var currentConfig = new DependenciesConfiguration {
                    Dependencies = []
                };

                foreach (var (path, dependencyProviders) in pathToDependencyMap) {
                    // Order dependencies by version descending and pick the highest
                    foreach (var (provider, dependency) in dependencyProviders.OrderByDescending(x => x.Dependency.Version)) {
                        var extractor = dependency.LibraryExtractor;

                        try {
                            bool needUpdate = !File.Exists(path) ||
                                (prevConfig.Dependencies.TryGetValue(extractor.LibraryName, out var existingEntry) &&
                                 existingEntry.Version < extractor.Version);

                            if (needUpdate) {
                                using var source = extractor.Extract();
                                using var destination = Utilities.IO.SafeFileCreate(path);
                                source.CopyTo(destination);

                                logger.Debug(
                                    category: "UpdateManagedDeps",
                                    message: $"Updated managed dependencies '{extractor.LibraryName}' to version '{extractor.Version}'.");
                            }
                        }
                        catch (Exception ex) {
                            failed.Add(provider);
                            logger.LogHandledExceptionWithMetadata(
                                category: "LoadManagedDeps",
                                message: $"Failed to load managed dependencies of plugin {provider.Name}.",
                                ex: ex,
                                metadata: [
                                    new("PluginFile", provider.PluginType.Assembly.Location)
                                ]);
                            continue;
                        }

                        // Set or update dependencies info
                        currentConfig.Dependencies[path] = new DependenicyEntry {
                            FilePathRelativeToBinDir = dependency.ExpectedPath,
                            Version = dependency.Version,
                            DependentPlugins = [.. dependencyProviders.Select(x => new PluginEntry {
                                Name = x.Provider.Metadata.Name,
                                Version = x.Dependency.Version
                            })]
                        };

                        break; // Only keep the highest versioned one
                    }
                }

                binDirectory.Create();

                // Cleanup unused dependencies
                SafeDependencyClean(logger, binDirectory, prevConfig, currentConfig);

                failedPlugins = failed;

                // Write updated config to disk
                var configPath = Path.Combine(binDirectory.FullName, "dependencies.json");
                File.WriteAllText(configPath, JsonSerializer.Serialize(currentConfig, serializerOptions));
            }
            static void SafeDependencyClean(RoleLogger logger, DirectoryInfo binDirectory, DependenciesConfiguration prevConfig, DependenciesConfiguration currentConfig) {
                foreach (var oldDependenicyPair in prevConfig.Dependencies) {
                    if (!currentConfig.Dependencies.ContainsKey(oldDependenicyPair.Key)) {
                        try {
                            File.Delete(Path.Combine(binDirectory.FullName, oldDependenicyPair.Value.FilePathRelativeToBinDir));
                            logger.Debug(
                                category: "ManagedDepsCleanUp",
                                message: $"Deleted unused dependencies file '{Path.Combine("bin", oldDependenicyPair.Value.FilePathRelativeToBinDir)}'");
                        }
                        catch (IOException ex) when (IgnoreThisIOError(ex)) {
                            logger.Warning(
                                category: "ManagedDepsCleanUp",
                                message: $"Failed to delete unused dependencies file '{Path.Combine("runtimes", oldDependenicyPair.Value.FilePathRelativeToBinDir)}'.\r\n" +
                                $"It is likely that the file is in use by another process. you can try to delete it manually.");
                        }
                        catch {
                            throw;
                        }
                    }
                }
                Utilities.IO.RemoveEmptyDirectories(binDirectory.FullName, false);
            }
        }
        static class NativeDependenicy {
            class DependenicyEntry
            {
                public required string LibraryNameWithouExtension { get; set; }
                public required string FilePathRelativeToRuntimesDir { get; set; }
                public required Version Version { get; set; }
                public required List<PluginEntry> DependentPlugins { get; set; }
            }
            class DependenciesConfiguration
            {
                /// <summary>
                /// Indicates whether to enable aggressive dependencies cleanup.
                /// When set to <c>true</c>, any file not listed in the curFormatter dependencies map will be deleted 
                /// from the dependencies directory, which results in a cleaner state but may unintentionally 
                /// remove manually added files.
                /// When set to <c>false</c>, the cleanup process will only remove files associated with 
                /// previously known dependencies that have been explicitly removed, preserving any 
                /// manually added or unknown files.
                /// </summary>
                public bool EnableAggressiveCleanUp { get; set; } = false;

                /// <summary>
                /// path -> Dependenicy
                /// </summary>
                public required Dictionary<string, DependenicyEntry> Dependencies { get; set; } 
            }
            static DependenciesConfiguration LoadDependenicesConfig(DirectoryInfo runtimesFolder) {
                var configPath = Path.Combine(runtimesFolder.FullName, "dependencies.json");
                if (!File.Exists(configPath)) {
                    return new DependenciesConfiguration { Dependencies = [] };
                }
                return JsonSerializer.Deserialize<DependenciesConfiguration>(File.ReadAllText(configPath))!;
            }
            static void NormalizeDependenicesConfig(DependenciesConfiguration configuration, DirectoryInfo runtimesFolder) {
                foreach (var kv in configuration.Dependencies.ToArray()) {
                    var dependency = kv.Value;

                    // If the dependencies file does not exist in the runtimes directory, remove it from the configuration.
                    if (!File.Exists(Path.Combine(runtimesFolder.FullName, dependency.FilePathRelativeToRuntimesDir))) {
                        configuration.Dependencies.Remove(kv.Key);
                    }
                }
            }
            public static void UpdateDependencies(
                RoleLogger logger,
                DirectoryInfo runtimesDirectory,
                IReadOnlyList<(PluginTypeInfo Info, IReadOnlyList<PluginDependency> Dependencies)> pluginInfos,
                out IReadOnlyList<PluginTypeInfo> failedPlugins) {

                var failed = new List<PluginTypeInfo>();

                var prevConfig = LoadDependenicesConfig(runtimesDirectory);
                NormalizeDependenicesConfig(prevConfig, runtimesDirectory);

                // Mapping from the target file path to all plugin-provided native dependencies for that path
                var pathToDependencyMap = new Dictionary<string, List<(PluginTypeInfo Provider, NativeEmbeddedDependency Dependency)>>();

                foreach (var (pluginInfo, dependencies) in pluginInfos) {
                    var nativeDependencies = dependencies
                        .OfType<NativeEmbeddedDependency>()
                        .ToArray();

                    if (nativeDependencies.Length == 0)
                        continue;

                    foreach (var dependency in nativeDependencies) {
                        var fullPath = Path.Combine(runtimesDirectory.FullName, dependency.ExpectedPath);

                        if (!pathToDependencyMap.TryGetValue(fullPath, out var list)) {
                            list = [];
                            pathToDependencyMap[fullPath] = list;
                        }

                        list.Add((pluginInfo, dependency));
                    }
                }

                var currentConfig = new DependenciesConfiguration {
                    Dependencies = []
                };

                foreach (var (path, dependencyProviders) in pathToDependencyMap) {
                    // Order dependencies by version descending and pick the highest
                    foreach (var (provider, dependency) in dependencyProviders.OrderByDescending(x => x.Dependency.Version)) {
                        var extractor = dependency.LibraryExtractor;

                        try {
                            bool needUpdate = !File.Exists(path) ||
                                (prevConfig.Dependencies.TryGetValue(extractor.LibraryName, out var existingEntry) &&
                                 existingEntry.Version < extractor.Version);

                            if (needUpdate) {
                                using var source = extractor.Extract();
                                using var destination = Utilities.IO.SafeFileCreate(path);
                                source.CopyTo(destination);
                            }
                        }
                        catch {
                            failed.Add(provider);
                            continue;
                        }

                        // Set or update dependencies info
                        currentConfig.Dependencies[path] = new DependenicyEntry {
                            LibraryNameWithouExtension = dependency.Name,
                            FilePathRelativeToRuntimesDir = dependency.ExpectedPath,
                            Version = dependency.Version,
                            DependentPlugins = [.. dependencyProviders.Select(x => new PluginEntry {
                                Name = x.Provider.Metadata.Name,
                                Version = x.Dependency.Version
                            })]
                        };

                        break; // Only keep the highest versioned one
                    }
                }

                runtimesDirectory.Create();

                // Cleanup unused dependencies
                if (prevConfig.EnableAggressiveCleanUp) {
                    currentConfig.EnableAggressiveCleanUp = true;
                    AggressiveDependencyClean(logger, runtimesDirectory, currentConfig);
                }
                else {
                    currentConfig.EnableAggressiveCleanUp = false;
                    SafeDependencyClean(logger, runtimesDirectory, prevConfig, currentConfig);
                }

                failedPlugins = failed;

                // Write updated config to disk
                var configPath = Path.Combine(runtimesDirectory.FullName, "dependencies.json");
                File.WriteAllText(configPath, JsonSerializer.Serialize(currentConfig, serializerOptions));
            }


            static void AggressiveDependencyClean(RoleLogger logger, DirectoryInfo runtimesFolder, DependenciesConfiguration currentConfig) {
                foreach (var file in runtimesFolder.GetFiles("*.*", SearchOption.AllDirectories)) {
                    if (file.FullName == Path.Combine(runtimesFolder.FullName, "dependencies.json")) {
                        continue;
                    }

                    if (currentConfig.Dependencies.Values.Any(x => Path.Combine(runtimesFolder.FullName, x.FilePathRelativeToRuntimesDir) == file.FullName)) {
                        continue;
                    }

                    var deletePath = Path.GetRelativePath(runtimesFolder.Parent!.FullName, file.FullName);
                    try {
                        File.Delete(file.FullName);
                        logger.Debug(
                            category: "NativeDepsCleanUp",
                            message: $"Deleted unused dependencies file '{deletePath}'");
                    } 
                    catch (IOException ex) when (IgnoreThisIOError(ex)) {
                        logger.Warning(
                            category: "NativeDepsCleanUp",
                            message: $"Failed to delete unused dependencies file '{deletePath}'.\r\n" +
                            $"It is likely that the file is in use by another process. you can try to delete it manually.");
                    }
                    catch {
                        throw;
                    }
                }
                Utilities.IO.RemoveEmptyDirectories(runtimesFolder.FullName, false);
            }
            static void SafeDependencyClean(RoleLogger logger, DirectoryInfo runtimesFolder, DependenciesConfiguration prevConfig, DependenciesConfiguration currentConfig) {
                foreach (var oldDependenicyPair in prevConfig.Dependencies) {
                    if (!currentConfig.Dependencies.ContainsKey(oldDependenicyPair.Key)) {
                        try {
                            File.Delete(Path.Combine(runtimesFolder.FullName, oldDependenicyPair.Value.FilePathRelativeToRuntimesDir));
                            logger.Debug(
                                category: "NativeDepsCleanUp",
                                message: $"Deleted unused dependencies file '{Path.Combine("bin", oldDependenicyPair.Value.FilePathRelativeToRuntimesDir)}'");
                        }
                        catch (IOException ex) when (IgnoreThisIOError(ex)) {
                            logger.Warning(
                                category: "NativeDepsCleanUp",
                                message: $"Failed to delete unused dependencies file '{Path.Combine("runtimes", oldDependenicyPair.Value.FilePathRelativeToRuntimesDir)}'.\r\n" +
                                $"It is likely that the file is in use by another process. you can try to delete it manually.");
                        }
                        catch {
                            throw;
                        }
                    }
                }
                Utilities.IO.RemoveEmptyDirectories(runtimesFolder.FullName, false);
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

        public PluginsLoadContext HandleDependencies(out IReadOnlyList<PluginContainer> validPlugins, out IReadOnlyList<PluginTypeInfo> failedPlugins) {
            List<PluginTypeInfo> failed = [];
            List<(PluginTypeInfo info, IReadOnlyList<PluginDependency>)> inputWithDependencies = [];
            foreach (var pluginInfo in pluginInfos) {
                try {
                    var dependencies = pluginInfo.Metadata.DependenciesProvider?.GetDependencies() ?? [];
                    inputWithDependencies.Add((pluginInfo, dependencies));
                }
                catch {
                    failed.Add(pluginInfo);
                }
            }

            var runtimesDirectory = new DirectoryInfo(Path.Combine(directory.FullName, "runtimes"));
            var binDirectory = new DirectoryInfo(Path.Combine(directory.FullName, "bin"));

            NativeDependenicy.UpdateDependencies(Logger, runtimesDirectory, inputWithDependencies, out var nativeFailedPlugins);
            failed.AddRange(nativeFailedPlugins);

            ManagedDependenicy.UpdateDependencies(Logger, binDirectory, inputWithDependencies, out var managedFailedPlugins);
            failed.AddRange(managedFailedPlugins);

            // SetManangedLibraryResolver(binDirectory);
            
            SetNativeLibraryResolver(failed, inputWithDependencies, runtimesDirectory);

            List<PluginContainer> valids = [];
            foreach (var (pluginInfo, dependencies) in inputWithDependencies) {
                if (failed.Contains(pluginInfo)) {
                    continue;
                }
                try {
                    var plugin = (IPlugin)(Activator.CreateInstance(pluginInfo.PluginType) ?? throw new InvalidOperationException($"Unable to create instance of {pluginInfo.PluginType}"));
                    valids.Add(new PluginContainer(pluginInfo.Metadata, dependencies, plugin));
                }
                catch (Exception ex) {
                    failed.Add(pluginInfo);
                    Logger.LogHandledExceptionWithMetadata(
                        category: "CreatePluginInstance",
                        message: $"Failed to create plugin instance of {pluginInfo.PluginType.FullName}.",
                        ex: ex,
                        metadata: [new("PluginFile", pluginInfo.PluginType.Assembly.Location)]);
                }
            }

            validPlugins = valids;
            failedPlugins = failed;
            return new PluginsLoadContext(logCore, valids);
        }

        private static void SetNativeLibraryResolver(List<PluginTypeInfo> failed, List<(PluginTypeInfo info, IReadOnlyList<PluginDependency>)> inputWithDependencies, DirectoryInfo runtimesDirectory) {
            Dictionary<Assembly, Dictionary<string, List<NativeEmbeddedDependency>>> assemblyToNativeDeps = [];
            foreach (var (pluginInfo, dependencies) in inputWithDependencies) {
                if (failed.Contains(pluginInfo)) {
                    continue;
                }

                var assembly = pluginInfo.PluginType.Assembly;
                if (!assemblyToNativeDeps.TryGetValue(assembly, out var dict)) {
                    assemblyToNativeDeps[assembly] = dict = [];
                }

                foreach (var dependency in dependencies.OfType<NativeEmbeddedDependency>()) {
                    if (!dict.TryGetValue(dependency.Name, out var list)) {
                        dict[dependency.Name] = list = [];
                    }
                    list.Add(dependency);
                }
            }

            foreach (var (assembly, dict) in assemblyToNativeDeps) {
                NativeLibrary.SetDllImportResolver(assembly, (libraryName, assembly, searchPath) => {
                    if (!assemblyToNativeDeps.TryGetValue(assembly, out var dict)) {
                        return nint.Zero;
                    }

                    if (!dict.TryGetValue(libraryName, out var dependencies)) {
                        return nint.Zero;
                    }

                    foreach (var dependency in dependencies) {
                        if (!NativeLibrary.TryLoad(Path.Combine(runtimesDirectory.FullName, dependency.ExpectedPath), out var handle)) {
                            continue;
                        }

                        return handle;
                    }

                    return nint.Zero;
                });
            }
        }
    }
}
