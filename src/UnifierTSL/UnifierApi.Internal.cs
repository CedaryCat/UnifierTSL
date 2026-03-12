using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Immutable;
using Terraria.Localization;
using Terraria.Utilities;
using UnifierTSL.CLI;
using UnifierTSL.CLI.Prompting;
using UnifierTSL.CLI.Prompting.Startup;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Extensions;
using UnifierTSL.FileSystem;
using UnifierTSL.Logging;
using UnifierTSL.Logging.LogWriters;
using UnifierTSL.PluginHost;

namespace UnifierTSL
{
    public static partial class UnifierApi
    {
        private static VersionHelper? version;
        public static VersionHelper VersionHelper => version ??= new();

        public static readonly UnifiedRandom rand = new();

        #region Events
        public static EventHub EventHub { get; private set; } = null!;
        #endregion

        #region Plugins
        public static PluginOrchestrator? pluginHosts;
        public static PluginOrchestrator PluginHosts => pluginHosts ??= new();
        #endregion

        #region Parameters
        internal static int ListenPort { get; set; } = -1;
        private static string? serverPassword;
        internal static string ServerPassword => serverPassword ?? "";
        #endregion

        #region IO
        internal static readonly DirectoryWatchManager FileMonitor = new(Directory.GetCurrentDirectory());
        #endregion

        #region Logging
        private class LoggerHost : ILoggerHost
        {
            public string Name => "UnifierApi";
            public string? CurrentLogCategory { get; set; }
        }

        private static readonly ILoggerHost LogHost = new LoggerHost();
        private static readonly RoleLogger logger;
        public static RoleLogger Logger => logger;

        private static Logger logCore;
        public static Logger LogCore {
            get => logCore ??= new Logger();
        }
        #endregion

        private static bool runtimePrepared;
        private static LauncherRuntimeSettings runtimeSettings = new();
        private const string ShortPasswordChars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
        private static readonly Lock durableWriterGate = new();
        private static readonly TimeSpan durableShutdownFlushTimeout = TimeSpan.FromSeconds(3);
        private static AsyncDurableLogWriter? durableWriter;
        private static int durableProcessExitHookRegistered;

        private static readonly LauncherConfigStore rootConfigStore = new();
        private static readonly LauncherRuntimeOps runtimeOps = new();
        private static readonly LauncherConfigManager rootConfigManager = new(rootConfigStore, ReloadRootConfigFromWatch);
        internal static event Action? ConsoleAppearanceChanged;

        static UnifierApi() {
            logCore = new();
            logger = CreateLogger(LogHost);
        }

        internal static void Initialize(string[] launcherArgs) {
            PrepareRuntime(launcherArgs);
            InitializeCore();
            CompleteLauncherInitialization();
        }

        internal static void PrepareRuntime(string[] launcherArgs) {
            if (runtimePrepared) {
                return;
            }

            LauncherCliOverrides overrides = runtimeOps.ParseLauncherOverrides(launcherArgs);
            RootLauncherConfiguration loadedConfig = rootConfigStore.LoadForStartup();
            RootLauncherConfiguration effectiveConfig = runtimeOps.BuildEffectiveStartupConfiguration(
                loadedConfig,
                overrides,
                out bool configChanged);

            if (configChanged && rootConfigStore.TrySaveRootConfiguration(effectiveConfig)) {
                Logger.Info(
                    GetParticularString("{0} is root config path", $"Startup CLI overrides were persisted to '{rootConfigStore.RootConfigPath}' using the effective launcher snapshot."),
                    category: "Config");
            }

            runtimeSettings = runtimeOps.ResolveRuntimeSettingsFromConfig(effectiveConfig);
            ConfigureDurableLogging(runtimeSettings.LogMode);

            runtimePrepared = true;
        }

        internal static void InitializeCore() {
            EventHub = new();

            pluginHosts = new();
            PluginHosts.InitializeAllAsync()
                .GetAwaiter()
                .GetResult();

            ConsoleInput.ConfigureHost(
                EventHub.Launcher.InvokeCreateLauncherConsoleHost()
                ?? new TerminalLauncherConsoleHost());

            ApplyResolvedLauncherSettings();
        }

        internal static void CompleteLauncherInitialization() {
            ReadLauncherArgs();
            SyncRuntimeSettingsFromInteractiveInput();
            EventHub.Launcher.InitializedEvent.Invoke(new());
        }

        internal static ConsoleStatusSettings GetConsoleStatus() {
            return runtimeSettings.ConsoleStatus;
        }

        internal static bool UseColorfulConsoleStatus() {
            return runtimeSettings.ColorfulConsoleStatus;
        }

        internal static ConsolePromptTheme GetConsolePromptTheme() {
            return ConsolePromptTheme.Default with {
                UseVividStatusBar = runtimeSettings.ColorfulConsoleStatus,
            };
        }

        internal static void StartRootConfigMonitoring() {
            rootConfigManager.StartMonitoring();
        }

        internal static void HandleCommandLinePreRun(string[] launcherArgs) {

            static bool TrySetLang(string langArg) {
                if (int.TryParse(langArg, out int langId) && GameCulture._legacyCultures.TryGetValue(langId, out var culture)) {
                    SetLang(culture);
                    return true;
                }

                try {
                    culture = Utilities.Culture.FindBestMatch(
                        GameCulture._legacyCultures.Values,
                        CultureInfo.GetCultureInfo(langArg),
                        gc => gc.CultureInfo);
                    if (culture is not null) {
                        SetLang(culture);
                        return true;
                    }
                }
                catch (CultureNotFoundException) {
                }

                return false;
            }

            static void SetLang(GameCulture culture) {
                GameCulture.DefaultCulture = culture;
                LanguageManager.Instance.SetLanguage(culture);
                CultureInfo.CurrentCulture = culture.RedirectedCultureInfo();
                CultureInfo.CurrentUICulture = culture.RedirectedCultureInfo();
            }

            bool languageSet = false;

            if (Environment.GetEnvironmentVariable("UTSL_LANGUAGE") is string overrideLang) {
                languageSet = TrySetLang(overrideLang);
            }

            Dictionary<string, List<string>> args = Utilities.CLI.ParseArguments(launcherArgs);
            foreach (KeyValuePair<string, List<string>> arg in args) {
                string firstValue = arg.Value[0];
                switch (arg.Key) {
                    case "-culture":
                    case "-lang":
                    case "-language":
                        if (languageSet) {
                            break;
                        }

                        if (TrySetLang(firstValue)) {
                            languageSet = true;
                        }
                        break;
                }
            }

            if (LanguageManager.Instance.ActiveCulture is null) {
                var list = GameCulture._NamedCultures.Values;
                var target = CultureInfo.CurrentUICulture;
                var match =
                    Utilities.Culture.FindBestMatch(list, target, gc => gc.CultureInfo)
                    ?? Utilities.Culture.FindBestMatch(list, CultureInfo.CurrentCulture, gc => gc.CultureInfo);

                SetLang(match ?? GameCulture.DefaultCulture);
            }
        }

        private static void ConfigureDurableLogging(LogPersistenceMode requestedMode) {
            EnsureDurableProcessExitHook();
            DisposeDurableWriter(reportTimeout: false);

            if (requestedMode == LogPersistenceMode.None) {
                LogCore.HistoryEnabled = false;
                return;
            }

            string logDirectory = Path.Combine(BaseDirectory, "logs");
            try {
                IDurableLogSink sink = requestedMode switch {
                    LogPersistenceMode.Sqlite => new SqliteDurableSink(logDirectory),
                    _ => new TextDurableSink(logDirectory),
                };

                AsyncDurableLogWriter writer = new(sink);
                LogCore.HistoryEnabled = true;
                LogCore.AddWriter(writer);
                lock (durableWriterGate) {
                    durableWriter = writer;
                }
            }
            catch (Exception ex) {
                LogCore.HistoryEnabled = false;
                Logger.Error(
                    GetParticularString("{0} is log persistence mode, {1} is log directory path", $"Failed to initialize '{requestedMode}' durable log backend at '{logDirectory}'."),
                    ex: ex,
                    category: "Logging");
            }
        }

        private static void EnsureDurableProcessExitHook() {
            if (Interlocked.Exchange(ref durableProcessExitHookRegistered, 1) != 0) {
                return;
            }

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        private static void OnProcessExit(object? sender, EventArgs e) {
            DisposeDurableWriter(reportTimeout: true);
        }

        private static void DisposeDurableWriter(bool reportTimeout) {
            AsyncDurableLogWriter? writer;
            lock (durableWriterGate) {
                writer = durableWriter;
                if (writer is null) {
                    return;
                }

                durableWriter = null;
            }

            LogCore.RemoveWriter(writer);
            bool flushed = writer.TryFlushAndStop(durableShutdownFlushTimeout);
            if (reportTimeout && !flushed) {
                Console.Error.WriteLine(
                    GetString("[Warning][LogCore|DurableQueue] Durable log flush timed out during process exit. Some tail records may be lost."));
            }
        }

        private static void ApplyResolvedLauncherSettings() {
            ListenPort = runtimeSettings.ListenPort;
            serverPassword = runtimeSettings.ServerPassword;
            EventHub.Coordinator.SwitchJoinServer.Register(ResolveJoinServer, HandlerPriority.Lowest);
            runtimeOps.ApplyAutoStartServers(runtimeSettings.AutoStartServers);
        }

        private static void ResolveJoinServer(ref ValueEventNoCancelArgs<SwitchJoinServerEvent> arg) {
            if (arg.Content.Servers.Length == 0) {
                return;
            }

            switch (runtimeSettings.JoinServer) {
                case JoinServerMode.Random:
                    arg.Content.JoinServer = arg.Content.Servers[rand.Next(0, arg.Content.Servers.Length)];
                    break;

                case JoinServerMode.First:
                    arg.Content.JoinServer = arg.Content.Servers[0];
                    break;
            }
        }

        private static void SyncRuntimeSettingsFromInteractiveInput() {
            runtimeSettings = runtimeOps.SyncRuntimeSettingsFromInteractiveInput(
                runtimeSettings,
                ListenPort,
                serverPassword);
        }

        private static void ReloadRootConfigFromWatch() {
            RootLauncherConfiguration? config = rootConfigStore.TryLoadForReload();
            if (config is null) {
                return;
            }

            LauncherRuntimeSettings desired = runtimeOps.ResolveRuntimeSettingsFromConfig(config);
            runtimeSettings = runtimeOps.ApplyReloadedRuntimeSettings(
                runtimeSettings,
                desired,
                applyListenPort: port => ListenPort = port,
                applyServerPassword: password => {
                    serverPassword = password;
                    UnifiedServerCoordinator.ServerPassword = password;
                });
            ConsoleInput.RefreshAppearanceSettings();
            ConsoleAppearanceChanged?.Invoke();
        }

        private static bool IsPortBindable(int port) {
            TcpListener? probe = null;
            try {
                probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                return true;
            }
            catch {
                return false;
            }
            finally {
                try {
                    probe?.Stop();
                }
                catch {
                }
            }
        }

        private static List<int> BuildPortCandidates(int preferred = 7777, int maxCount = 8) {
            List<int> candidates = [];

            void TryAdd(int port) {
                if (!LauncherPortRules.IsValidListenPort(port)) {
                    return;
                }

                if (candidates.Contains(port)) {
                    return;
                }

                if (!IsPortBindable(port)) {
                    return;
                }

                candidates.Add(port);
            }

            TryAdd(preferred);
            if (LauncherPortRules.IsValidListenPort(ListenPort)) {
                TryAdd(ListenPort);
            }

            for (int offset = 1; candidates.Count < maxCount && offset < 300; offset++) {
                TryAdd(preferred + offset);
                if (candidates.Count >= maxCount) {
                    break;
                }
                TryAdd(preferred - offset);
            }

            if (candidates.Count == 0) {
                candidates.Add(preferred);
            }

            return candidates;
        }

        private static IReadOnlyList<ConsoleSuggestion> BuildPortSuggestionItems(string input) {
            string normalizedInput = (input ?? string.Empty).Trim();

            int preferred = 7777;
            if (int.TryParse(normalizedInput, out int parsed) && LauncherPortRules.IsValidListenPort(parsed)) {
                preferred = parsed;
            }

            List<int> candidates = BuildPortCandidates(preferred, maxCount: 10);
            return candidates
                .Select(port => new ConsoleSuggestion(
                    Value: port.ToString(),
                    Weight: CalculatePortSuggestionWeight(port, normalizedInput, preferred)))
                .OrderByDescending(static item => item.Weight)
                .ThenBy(static item => item.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int CalculatePortSuggestionWeight(int port, string normalizedInput, int preferred) {
            int weight = 0;
            string value = port.ToString();

            if (port == preferred) {
                weight += 200;
            }
            if (port == 7777) {
                weight += 120;
            }

            if (string.IsNullOrEmpty(normalizedInput)) {
                weight += 50;
            }
            else if (value.StartsWith(normalizedInput, StringComparison.OrdinalIgnoreCase)) {
                weight += 300;
            }
            else {
                weight -= 100;
            }

            if (IsPortBindable(port)) {
                weight += 30;
            }
            else {
                weight -= 500;
            }

            return weight;
        }

        private static IReadOnlyList<string> BuildPortReactiveStatus(ConsoleInputState state) {
            string input = (state.InputText ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(input)) {
                return [];
            }

            if (!int.TryParse(input, out int port)) {
                return [
                    GetParticularString("{0} is current input value", $"input '{input}' is not an integer port."),
                ];
            }

            if (!LauncherPortRules.IsValidListenPort(port)) {
                return [
                    GetParticularString("{0} is current input value", $"input '{port}' is out of range (0~65535)."),
                ];
            }

            if (!IsPortBindable(port)) {
                return [
                    GetParticularString("{0} is current input value", $"input '{port}' is occupied."),
                ];
            }

            return [
                GetParticularString("{0} is current input value", $"input '{port}' is available."),
            ];
        }

        private static string GenerateShortPassword(int length = 8) {
            char[] buffer = new char[length];
            for (int i = 0; i < buffer.Length; i++) {
                buffer[i] = ShortPasswordChars[rand.Next(ShortPasswordChars.Length)];
            }
            return new string(buffer);
        }

        private static List<string> BuildPasswordCandidates(int count = 5) {
            HashSet<string> set = new(StringComparer.Ordinal);
            while (set.Count < count) {
                set.Add(GenerateShortPassword());
            }
            return [.. set];
        }

        private static IReadOnlyList<ConsoleSuggestion> BuildPasswordSuggestionItems(string input, IReadOnlyList<string> seedCandidates) {
            string prefix = input ?? string.Empty;
            List<ConsoleSuggestion> items = [];

            if (string.IsNullOrEmpty(prefix)) {
                int rank = 0;
                foreach (string candidate in seedCandidates.Where(static value => !string.IsNullOrWhiteSpace(value))) {
                    items.Add(new ConsoleSuggestion(
                        Value: candidate,
                        Weight: 500 - (rank * 10)));
                    rank += 1;
                }

                return items;
            }

            int targetLength = Math.Clamp(Math.Max(prefix.Length + 4, 8), 8, 16);
            for (int index = 0; index < 6; index++) {
                string candidate = BuildDeterministicPasswordVariant(prefix, index, targetLength);
                items.Add(new ConsoleSuggestion(
                    Value: candidate,
                    Weight: 420 - (index * 8)));
            }

            return items;
        }

        private static string BuildDeterministicPasswordVariant(string prefix, int variantIndex, int targetLength) {
            string safePrefix = prefix ?? string.Empty;
            int length = Math.Max(safePrefix.Length, targetLength);
            int seed = HashCode.Combine(safePrefix, variantIndex, length);
            Random random = new(seed);
            StringBuilder buffer = new(safePrefix);

            while (buffer.Length < length) {
                buffer.Append(ShortPasswordChars[random.Next(ShortPasswordChars.Length)]);
            }

            return buffer.ToString();
        }

        private static IReadOnlyList<string> BuildPasswordReactiveStatus(ConsoleInputState state) {
            int length = (state.InputText ?? string.Empty).Length;
            string quality = length switch {
                <= 0 => "empty",
                <= 5 => "weak",
                <= 9 => "medium",
                _ => "strong",
            };

            return [
                GetParticularString("{0} is password text length", $"password length: {length}"),
                GetParticularString("{0} is simple password quality level", $"strength hint : {quality}"),
                GetString("empty input is allowed for no-password mode"),
            ];
        }

        private static void ReadLauncherArgs() {
            string lastPortError = string.Empty;
            while (!LauncherPortRules.IsValidListenPort(ListenPort)) {
                List<int> portCandidates = BuildPortCandidates();
                ConsolePromptSpec context = LauncherStartupPromptFactory.CreateListenPortPrompt(
                    lastPortError,
                    portCandidates,
                    state => BuildPortSuggestionItems(state.InputText),
                    BuildPortReactiveStatus);

                string input = ConsoleInput.ReadLine(context, trim: true);

                if (!int.TryParse(input, out int port)) {
                    lastPortError = GetParticularString("{0} is user input value", $"'{input}' is not an integer port.");
                    continue;
                }
                if (!LauncherPortRules.IsValidListenPort(port)) {
                    lastPortError = GetParticularString("{0} is user input value", $"'{port}' is out of range.");
                    continue;
                }
                if (!IsPortBindable(port)) {
                    lastPortError = GetParticularString("{0} is user input value", $"'{port}' is not available.");
                    continue;
                }

                ListenPort = port;
            }

            if (serverPassword is null) {
                List<string> passwordCandidates = BuildPasswordCandidates();
                ConsolePromptSpec context = LauncherStartupPromptFactory.CreateServerPasswordPrompt(
                    passwordCandidates,
                    state => BuildPasswordSuggestionItems(state.InputText, passwordCandidates),
                    BuildPasswordReactiveStatus);

                string input = ConsoleInput.ReadLine(context, trim: true);
                serverPassword = input;
            }
        }
    }
}
