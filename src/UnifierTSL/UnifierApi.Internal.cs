using System.Globalization;
using Terraria.Localization;
using Terraria.Utilities;
using UnifierTSL.CLI;
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
        private static readonly Lock durableWriterGate = new();
        private static readonly TimeSpan durableShutdownFlushTimeout = TimeSpan.FromSeconds(3);
        private static AsyncDurableLogWriter? durableWriter;
        private static int durableProcessExitHookRegistered;

        private static readonly LauncherConfigStore rootConfigStore = new();
        private static readonly LauncherRuntimeOps runtimeOps = new();
        private static readonly LauncherConfigManager rootConfigManager = new(rootConfigStore, ReloadRootConfigFromWatch);

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

            ApplyResolvedLauncherSettings();
        }

        internal static void CompleteLauncherInitialization() {
            ReadLauncherArgs();
            SyncRuntimeSettingsFromInteractiveInput();
            EventHub.Launcher.InitializedEvent.Invoke(new());
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

                culture = GameCulture._legacyCultures.Values.SingleOrDefault(c => c.Name == langArg);
                if (culture is not null) {
                    SetLang(culture);
                    return true;
                }

                return false;
            }

            static void SetLang(GameCulture culture) {
                GameCulture.DefaultCulture = culture;
                LanguageManager.Instance.SetLanguage(culture);
                CultureInfo.CurrentCulture = culture.RedirectedCultureInfo();
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
                LogCore.AttachHistoryWriter(writer, writer);
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
        }

        private static void ReadLauncherArgs() {
            while (!LauncherPortRules.IsValidListenPort(ListenPort)) {
                if (int.TryParse(ConsoleInput.ReadLine(GetString("Enter the port to listen on: ")), out int port)) {
                    ListenPort = port;
                }
            }

            serverPassword ??= ConsoleInput.ReadLine(GetString("Enter the server password: "))?.Trim() ?? "";
        }
    }
}
