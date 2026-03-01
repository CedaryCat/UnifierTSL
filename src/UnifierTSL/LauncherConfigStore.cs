using System.Text.Json;

namespace UnifierTSL
{
    internal sealed class LauncherConfigStore
    {
        private static readonly JsonSerializerOptions serializerOptions = new() {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        private readonly Lock ioGate = new();
        private bool legacyRootConfigWarningIssued;

        public const string RootConfigRelativeDir = "config";
        public string RootConfigRelativePath => Path.Combine(RootConfigRelativeDir, "config.json");
        public string RootConfigPath => Path.Combine(UnifierApi.BaseDirectory, RootConfigRelativePath);
        private string LegacyRootConfigPath => Path.Combine(UnifierApi.BaseDirectory, "config.json");

        public RootLauncherConfiguration LoadForStartup() {
            WarnAboutLegacyRootConfigIfPresent();
            EnsureRootConfigDirectoryExists();

            if (!File.Exists(RootConfigPath)) {
                RootLauncherConfiguration defaults = new();
                TryWriteDefaultRootConfiguration(defaults);
                return defaults;
            }

            try {
                return DeserializeRootConfiguration(File.ReadAllText(RootConfigPath));
            }
            catch (JsonException ex) {
                UnifierApi.Logger.Error(
                    GetParticularString("{0} is root config path", $"Root config '{RootConfigPath}' is invalid and will be ignored for this run."),
                    ex: ex,
                    category: "Config");
            }
            catch (Exception ex) {
                UnifierApi.Logger.Error(
                    GetParticularString("{0} is root config path", $"Root config '{RootConfigPath}' could not be read and will be ignored for this run."),
                    ex: ex,
                    category: "Config");
            }

            return new RootLauncherConfiguration();
        }

        public RootLauncherConfiguration? TryLoadForReload() {
            EnsureRootConfigDirectoryExists();

            if (!File.Exists(RootConfigPath)) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is root config path", $"Root config '{RootConfigPath}' is missing. Keeping the current runtime settings."),
                    category: "Config");
                return null;
            }

            try {
                return DeserializeRootConfiguration(File.ReadAllText(RootConfigPath));
            }
            catch (JsonException ex) {
                UnifierApi.Logger.Error(
                    GetParticularString("{0} is root config path", $"Root config '{RootConfigPath}' is invalid. Keeping the current runtime settings."),
                    ex: ex,
                    category: "Config");
            }
            catch (Exception ex) {
                UnifierApi.Logger.Error(
                    GetParticularString("{0} is root config path", $"Root config '{RootConfigPath}' could not be read. Keeping the current runtime settings."),
                    ex: ex,
                    category: "Config");
            }

            return null;
        }

        public bool TrySaveRootConfiguration(RootLauncherConfiguration config) {
            RootLauncherConfiguration normalized = NormalizeRootConfiguration(config);
            string content = JsonSerializer.Serialize(normalized, serializerOptions);

            try {
                lock (ioGate) {
                    EnsureRootConfigDirectoryExists();
                    File.WriteAllText(RootConfigPath, content);
                }
                return true;
            }
            catch (Exception ex) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is root config path", $"Unable to persist root config to '{RootConfigPath}'."),
                    category: "Config",
                    ex: ex);
                return false;
            }
        }

        private void WarnAboutLegacyRootConfigIfPresent() {
            if (legacyRootConfigWarningIssued || !File.Exists(LegacyRootConfigPath)) {
                return;
            }

            legacyRootConfigWarningIssued = true;
            UnifierApi.Logger.Warning(
                GetParticularString("{0} is legacy root config path, {1} is active root config path", $"Legacy root config '{LegacyRootConfigPath}' is ignored. Use '{RootConfigPath}' instead."),
                category: "Config");
        }

        private void EnsureRootConfigDirectoryExists() {
            string? directory = Path.GetDirectoryName(RootConfigPath);
            if (!string.IsNullOrEmpty(directory)) {
                Directory.CreateDirectory(directory);
            }
        }

        private void TryWriteDefaultRootConfiguration(RootLauncherConfiguration config) {
            try {
                lock (ioGate) {
                    EnsureRootConfigDirectoryExists();
                    File.WriteAllText(
                        RootConfigPath,
                        JsonSerializer.Serialize(config, serializerOptions));
                }
            }
            catch (Exception ex) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is root config path", $"Unable to create default root config at '{RootConfigPath}'."),
                    category: "Config",
                    ex: ex);
            }
        }

        private static RootLauncherConfiguration NormalizeRootConfiguration(RootLauncherConfiguration? config) {
            RootLauncherConfiguration normalized = config ?? new RootLauncherConfiguration();
            normalized.Logging ??= new LoggingConfiguration();
            normalized.Launcher ??= new LauncherConfiguration();
            normalized.Launcher.AutoStartServers ??= [];
            return normalized;
        }

        private RootLauncherConfiguration DeserializeRootConfiguration(string content) {
            return NormalizeRootConfiguration(JsonSerializer.Deserialize<RootLauncherConfiguration>(
                content,
                serializerOptions));
        }
    }
}
