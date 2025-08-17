using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using UnifierTSL.FileSystem;
using UnifierTSL.Logging;

namespace UnifierTSL.Module.Dependencies
{
    public class DependenciesConfiguration
    {
        readonly RoleLogger Logger;
        public readonly DependenciesSetting Setting;

        public DependenciesConfiguration(RoleLogger logger, DependenciesSetting setting) {
            Logger = logger;
            Setting = setting;
        }

        public static DependenciesSetting LoadDependenicesConfig(string moduleDirectory) {
            var configPath = Path.Combine(moduleDirectory, "dependencies.json");
            if (!File.Exists(configPath)) {
                return new DependenciesSetting { Dependencies = [] };
            }
            return JsonSerializer.Deserialize<DependenciesSetting>(File.ReadAllText(configPath))!;
        }

        public static bool TryLoadDependenicesConfig(string moduleDirectory, [NotNullWhen(true)] out DependenciesSetting? config) {
            var configPath = Path.Combine(moduleDirectory, "dependencies.json");
            if (!File.Exists(configPath)) {
                config = null;
                return false;
            }
            config = JsonSerializer.Deserialize<DependenciesSetting>(File.ReadAllText(configPath))!;
            return true;
        }

        private static readonly JsonSerializerOptions serializerOptions = new() { WriteIndented = true };
        public void Save(string moduleDirectory) { 
            var configPath = Path.Combine(moduleDirectory, "dependencies.json");
            File.WriteAllText(configPath, JsonSerializer.Serialize(Setting, serializerOptions));
        }

        public void NormalizeDependenicesConfig(string moduleDirectory) {
            foreach (var depEntry in Setting.Dependencies.Values.ToArray()) {

                foreach (var item in depEntry.Manifests) {
                    if (!File.Exists(Path.Combine(moduleDirectory, item.FilePath))) {
                        Setting.Dependencies.Remove(depEntry.Name);
                        Logger.Debug(
                            category: "ConfNormalize",
                            message: "The dependencies file missing, removing from the recorded configuration for update.");
                        break;
                    }
                }

                //if (!dependency.IsNativeLibrary) {
                //    string? name;
                //    Version? version;
                //    using (var stream = File.OpenRead(Path.Combine(moduleDirectory, relativeFilePath))) {
                //        // Try to read the assembly's identity (name and version) from the file.
                //        if (!MetadataBlobHelpers.TryReadAssemblyIdentity(stream, out name, out version)) {
                //            continue;
                //        }
                //    }

                //    // If the assembly name in the file does not match the expected dependencies name,
                //    // it likely means the file has been modified in a breaking way,
                //    // and any original dependent plugins can no longer reliably depend on it.
                //    if (dependency.Name != name) {
                //        configuration.Dependencies.Remove(relativeFilePath);
                //        Logger.Warning(
                //            category: "ConfNormalize",
                //            message: $"The assembly name ({name}) in the file does not match the expected dependencies name ({dependency.Name}).");
                //    }
                //}
            }
        }
        public void AggressiveDependencyClean(string moduleDirectory) {
            var moduleDirInfo = new DirectoryInfo(moduleDirectory);
            foreach (var file in moduleDirInfo.GetFiles("*.*", SearchOption.AllDirectories)) {
                if (file.FullName == Path.Combine(moduleDirInfo.FullName, "dependencies.json")) {
                    continue;
                }

                if (Setting.Dependencies.Keys.Any(path => Path.Combine(moduleDirInfo.FullName, path) == file.FullName)) {
                    continue;
                }

                var deletePath = Path.GetRelativePath(moduleDirInfo.Parent!.FullName, file.FullName);
                try {
                    File.Delete(file.FullName);
                    Logger.Debug(
                        category: "DepsCleanUp",
                        message: $"Deleted unused dependencies file '{deletePath}'");
                }
                catch (IOException ex) when (FileSystemHelper.FileIsInUse(ex)) {
                    Logger.Warning(
                        category: "DepsCleanUp",
                        message: $"Failed to delete unused dependencies file '{deletePath}'.\r\n" +
                        $"It is likely that the file is in use by another process. you can try to delete it manually.");
                }
                catch {
                    throw;
                }
            }
            Utilities.IO.RemoveEmptyDirectories(moduleDirInfo.FullName, false);
        }

        public void SafeDependencyClean(string moduleDirectory, DependenciesSetting previous) {
            var moduleDirInfo = new DirectoryInfo(moduleDirectory);
            foreach (var oldDependenicyPair in previous.Dependencies) {
                if (!Setting.Dependencies.ContainsKey(oldDependenicyPair.Key)) {
                    try {
                        File.Delete(Path.Combine(moduleDirInfo.FullName, oldDependenicyPair.Key));
                        Logger.Debug(
                            category: "DepsCleanUp",
                            message: $"Deleted unused dependencies file '{Path.Combine("bin", oldDependenicyPair.Key)}'");
                    }
                    catch (IOException ex) when (FileSystemHelper.FileIsInUse(ex)) {
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
            Utilities.IO.RemoveEmptyDirectories(moduleDirInfo.FullName, false);
        }

        public void SpecificDependencyClean(string moduleDirectory, DependenciesSetting previous) {
            if (Setting.EnableAggressiveCleanUp) {
                AggressiveDependencyClean(moduleDirectory);
            }
            else {
                SafeDependencyClean(moduleDirectory, previous);
            }
        }
    }
}
