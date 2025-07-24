using System.Text.Json;
using UnifierTSL.PluginService.Dependencies;
using UnifierTSL.Reflection.Metadata;

namespace UnifierTSL.PluginService.Loading
{
    internal class PluginDependenicesManager(DirectoryInfo directory, IReadOnlyList<PluginTypeInfo> pluginInfos)
    {
        private static readonly JsonSerializerOptions serializerOptions = new() { WriteIndented = true };
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
            static DependenciesConfiguration LoadDependenicesConfig(DirectoryInfo binFolder) {
                var configPath = Path.Combine(binFolder.FullName, "dependencies.json");
                if (!File.Exists(configPath)) {
                    return new DependenciesConfiguration { Dependencies = [] };
                }
                return JsonSerializer.Deserialize<DependenciesConfiguration>(File.ReadAllText(configPath))!;
            }
            static void NormalizeDependenicesConfig(DependenciesConfiguration configuration, DirectoryInfo binFolder) {
                foreach (var kv in configuration.Dependencies.ToArray()) {
                    var dependencyName = kv.Key;
                    var dependency = kv.Value;

                    // If the dependency file does not exist in the bin directory, remove it from the configuration.
                    if (!File.Exists(Path.Combine(binFolder.FullName, dependency.FilePathRelativeToBinDir))) {
                        configuration.Dependencies.Remove(dependencyName);
                    }

                    string? name;
                    Version? version;
                    using (var stream = File.OpenRead(Path.Combine(binFolder.FullName, dependency.FilePathRelativeToBinDir))) {
                        // Try to read the assembly's identity (name and version) from the file.
                        if (!MetadataBlobHelpers.TryReadAssemblyIdentity(stream, out name, out version)) {
                            continue;
                        }
                    }

                    // If the assembly name in the file does not match the expected dependency name,
                    // it likely means the file has been modified in a breaking way,
                    // and any original dependent plugins can no longer reliably depend on it.
                    if (dependencyName != name) {
                        dependency.DependentPlugins = [];
                    }
                }
            }
            public static void UpdateDependencies(
                DirectoryInfo binDirectory,
                IReadOnlyList<(PluginTypeInfo Info, IReadOnlyList<PluginDependency> Dependencies)> pluginInfos,
                out IReadOnlyList<PluginTypeInfo> failedPlugins) {

                var failed = new List<PluginTypeInfo>();

                var prevConfig = LoadDependenicesConfig(binDirectory);
                NormalizeDependenicesConfig(prevConfig, binDirectory);

                // Mapping from the target file path to all plugin-provided native dependencies for that path
                var pathToDependencyMap = new Dictionary<string, List<(PluginTypeInfo Provider, NativeEmbeddedDependency Dependency)>>();

                foreach (var (pluginInfo, dependencies) in pluginInfos) {
                    var nativeDependencies = dependencies
                        .OfType<NativeEmbeddedDependency>()
                        .ToArray();

                    if (nativeDependencies.Length == 0)
                        continue;

                    foreach (var dependency in nativeDependencies) {
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
                            }
                        }
                        catch {
                            failed.Add(provider);
                            continue;
                        }

                        // Set or update dependency info
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

                // Cleanup unused dependencies
                SafeDependencyClean(binDirectory, prevConfig, currentConfig);

                failedPlugins = failed;

                // Write updated config to disk
                var configPath = Path.Combine(binDirectory.FullName, "dependencies.json");
                File.WriteAllText(configPath, JsonSerializer.Serialize(currentConfig, serializerOptions));
            }
            static void SafeDependencyClean(DirectoryInfo runtimesFolder, DependenciesConfiguration prevConfig, DependenciesConfiguration currentConfig) {
                foreach (var oldDependenicyPair in prevConfig.Dependencies) {
                    if (!currentConfig.Dependencies.ContainsKey(oldDependenicyPair.Key)) {
                        try {
                            File.Delete(Path.Combine(runtimesFolder.FullName, oldDependenicyPair.Value.FilePathRelativeToBinDir));
                        }
                        catch (IOException ex) when (IgnoreThisIOError(ex)) { }
                        catch {
                            throw;
                        }
                    }
                }
                Utilities.IO.RemoveEmptyDirectories(runtimesFolder.FullName);
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
                /// Indicates whether to enable aggressive dependency cleanup.
                /// When set to <c>true</c>, any file not listed in the curFormatter dependency map will be deleted 
                /// from the dependency directory, which results in a cleaner state but may unintentionally 
                /// remove manually added files.
                /// When set to <c>false</c>, the cleanup process will only remove files associated with 
                /// previously known dependencies that have been explicitly removed, preserving any 
                /// manually added or unknown files.
                /// </summary>
                public bool EnableAggressiveCleanUp { get; set; } = true;

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

                    // If the dependency file does not exist in the runtimes directory, remove it from the configuration.
                    if (!File.Exists(Path.Combine(runtimesFolder.FullName, dependency.FilePathRelativeToRuntimesDir))) {
                        configuration.Dependencies.Remove(kv.Key);
                    }
                }
            }
            public static void UpdateDependencies(
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

                        // Set or update dependency info
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

                // Cleanup unused dependencies
                if (prevConfig.EnableAggressiveCleanUp) {
                    currentConfig.EnableAggressiveCleanUp = true;
                    AggressiveDependencyClean(runtimesDirectory, currentConfig);
                }
                else {
                    currentConfig.EnableAggressiveCleanUp = false;
                    SafeDependencyClean(runtimesDirectory, prevConfig, currentConfig);
                }

                failedPlugins = failed;

                // Write updated config to disk
                var configPath = Path.Combine(runtimesDirectory.FullName, "dependencies.json");
                File.WriteAllText(configPath, JsonSerializer.Serialize(currentConfig, serializerOptions));
            }


            static void AggressiveDependencyClean(DirectoryInfo runtimesFolder, DependenciesConfiguration currentConfig) {
                foreach (var file in runtimesFolder.GetFiles("*.*", SearchOption.AllDirectories)) {
                    if (file.FullName == Path.Combine(runtimesFolder.FullName, "dependencies.json")) {
                        continue;
                    }

                    if (currentConfig.Dependencies.Values.Any(x => Path.Combine(runtimesFolder.FullName, x.FilePathRelativeToRuntimesDir) == file.FullName)) {
                        continue;
                    }
                    
                    try {
                        File.Delete(file.FullName);
                    } 
                    catch (IOException ex) when (IgnoreThisIOError(ex)) { }
                    catch {
                        throw;
                    }
                }
                Utilities.IO.RemoveEmptyDirectories(runtimesFolder.FullName);
            }
            static void SafeDependencyClean(DirectoryInfo runtimesFolder, DependenciesConfiguration prevConfig, DependenciesConfiguration currentConfig) {
                foreach (var oldDependenicyPair in prevConfig.Dependencies) {
                    if (!currentConfig.Dependencies.ContainsKey(oldDependenicyPair.Key)) {
                        try {
                            File.Delete(Path.Combine(runtimesFolder.FullName, oldDependenicyPair.Value.FilePathRelativeToRuntimesDir));
                        }
                        catch (IOException ex) when (IgnoreThisIOError(ex)) { }
                        catch {
                            throw;
                        }
                    }
                }
                Utilities.IO.RemoveEmptyDirectories(runtimesFolder.FullName);
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

            NativeDependenicy.UpdateDependencies(new DirectoryInfo(Path.Combine(directory.FullName, "runtimes")), inputWithDependencies, out var nativeFailedPlugins);
            failed.AddRange(nativeFailedPlugins);

            ManagedDependenicy.UpdateDependencies(new DirectoryInfo(Path.Combine(directory.FullName, "bin")), inputWithDependencies, out var managedFailedPlugins);
            failed.AddRange(managedFailedPlugins);

            List<PluginContainer> valids = [];
            foreach (var (pluginInfo, dependencies) in inputWithDependencies) {
                if (failed.Contains(pluginInfo)) {
                    continue;
                }
                try {
                    var plugin = (IPlugin)(Activator.CreateInstance(pluginInfo.PluginType) ?? throw new InvalidOperationException($"Unable to create instance of {pluginInfo.PluginType}"));
                    valids.Add(new PluginContainer(pluginInfo.Metadata, dependencies, plugin));
                }
                catch {
                    failed.Add(pluginInfo);
                }
            }

            validPlugins = valids;
            failedPlugins = failed;
            return new PluginsLoadContext(valids);
        }
    }
}
