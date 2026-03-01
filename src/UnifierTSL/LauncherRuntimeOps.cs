using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Terraria.GameContent.UI.States;
using UnifierTSL.Servers;

namespace UnifierTSL
{
    internal sealed class LauncherRuntimeOps
    {
        public LauncherCliOverrides ParseLauncherOverrides(string[] launcherArgs) {
            LauncherCliOverrides overrides = new();
            Dictionary<string, List<string>> args = Utilities.CLI.ParseArguments(launcherArgs);
            bool joinServerConfigured = false;

            foreach (KeyValuePair<string, List<string>> arg in args) {
                string firstValue = arg.Value[0];
                switch (arg.Key) {
                    case "-listen":
                    case "-port":
                        if (int.TryParse(firstValue, out int port)) {
                            overrides.ListenPort = port;
                        }
                        else {
                            UnifierApi.Logger.Warning(
                                GetParticularString("{0} is user input value for port number", $"Invalid port number specified: {firstValue}"),
                                category: "Launcher");
                        }
                        break;

                    case "-password":
                        overrides.ServerPassword = firstValue;
                        break;

                    case "-autostart":
                    case "-addserver":
                    case "-server":
                        overrides.HasAutoStartServers = true;
                        foreach (string serverArgs in arg.Value) {
                            if (TryParseAutoStartServerOverride(serverArgs, out AutoStartServerConfiguration? server)) {
                                overrides.AutoStartServers.Add(server);
                            }
                        }
                        break;

                    case "-servermerge":
                    case "--server-merge":
                    case "--auto-start-merge":
                        if (TryParseAutoStartServerMergeMode(firstValue, out AutoStartServerMergeMode mergeMode)) {
                            overrides.AutoStartServersMergeMode = mergeMode;
                        }
                        else {
                            UnifierApi.Logger.Warning(
                                GetParticularString("{0} is user input value for server merge mode", $"Invalid server merge mode: {firstValue}"),
                                category: "Launcher");
                            UnifierApi.Logger.Warning(
                                GetString("Expected value: replace (default), overwrite, or append."),
                                category: "Launcher");
                        }
                        break;

                    case "-joinserver":
                        if (joinServerConfigured) {
                            break;
                        }

                        if (TryParseJoinServerMode(firstValue, out JoinServerMode joinMode)) {
                            overrides.JoinServer = joinMode;
                            joinServerConfigured = true;
                        }
                        else {
                            UnifierApi.Logger.Warning(
                                GetParticularString("{0} is user input value for join server mode", $"Invalid join server mode: {firstValue}"),
                                category: "Launcher");
                        }
                        break;

                    case "-logmode":
                    case "--log-mode":
                        if (TryParseLogMode(firstValue, out LogPersistenceMode mode)) {
                            overrides.LogMode = mode;
                        }
                        else {
                            UnifierApi.Logger.Warning(
                                GetParticularString("{0} is user input value for log mode", $"Invalid log mode specified: {firstValue}"),
                                category: "Logging");
                        }
                        break;
                }
            }

            return overrides;
        }

        public RootLauncherConfiguration BuildEffectiveStartupConfiguration(
            RootLauncherConfiguration config,
            LauncherCliOverrides overrides,
            out bool configChanged) {

            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(overrides);

            RootLauncherConfiguration effective = CloneRootConfiguration(config);

            if (overrides.ListenPort.HasValue) {
                effective.Launcher.ListenPort = overrides.ListenPort.Value;
            }

            if (overrides.ServerPassword is not null) {
                effective.Launcher.ServerPassword = overrides.ServerPassword;
            }

            if (overrides.JoinServer.HasValue) {
                effective.Launcher.JoinServer = DescribeJoinServerMode(overrides.JoinServer.Value);
            }

            if (overrides.LogMode.HasValue) {
                effective.Logging.Mode = DescribeLogMode(overrides.LogMode.Value);
            }

            if (overrides.HasAutoStartServers) {
                AutoStartServerMergeMode mergeMode = overrides.AutoStartServersMergeMode ?? AutoStartServerMergeMode.ReplaceAll;
                effective.Launcher.AutoStartServers = MergeAutoStartServers(
                    existingServers: effective.Launcher.AutoStartServers,
                    cliServers: overrides.AutoStartServers,
                    mergeMode: mergeMode);
            }
            else if (overrides.AutoStartServersMergeMode.HasValue) {
                UnifierApi.Logger.Warning(
                    GetString("'-servermerge/--server-merge' was provided without any '-server' arguments. The merge mode was ignored."),
                    category: "Launcher");
            }

            configChanged = !RootLauncherConfigurationEquivalent(config, effective);
            return effective;
        }

        public LauncherRuntimeSettings ResolveRuntimeSettingsFromConfig(RootLauncherConfiguration config) {
            return new LauncherRuntimeSettings {
                LogMode = ResolveConfiguredLogMode(config.Logging.Mode),
                ListenPort = config.Launcher.ListenPort,
                ServerPassword = config.Launcher.ServerPassword,
                JoinServer = ResolveConfiguredJoinServerMode(config.Launcher.JoinServer),
                AutoStartServers = CloneAutoStartServers(config.Launcher.AutoStartServers),
            };
        }

        public LauncherRuntimeSettings ApplyReloadedRuntimeSettings(
            LauncherRuntimeSettings current,
            LauncherRuntimeSettings desired,
            Action<int> applyListenPort,
            Action<string> applyServerPassword) {

            ArgumentNullException.ThrowIfNull(applyListenPort);
            ArgumentNullException.ThrowIfNull(applyServerPassword);

            int appliedListenPort = current.ListenPort;

            if (current.LogMode != desired.LogMode) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is current logging mode, {1} is desired logging mode", $"logging.mode changed from '{DescribeLogMode(current.LogMode)}' to '{DescribeLogMode(desired.LogMode)}'. Restart is required before this setting takes effect."),
                    category: "Config");
            }

            if (current.ListenPort != desired.ListenPort) {
                if (!LauncherPortRules.IsValidListenPort(desired.ListenPort)) {
                    UnifierApi.Logger.Warning(
                        GetParticularString("{0} is current listen port, {1} is desired listen port, {2} is active listen port", $"launcher.listenPort changed from '{DescribeListenPort(current.ListenPort)}' to '{DescribeListenPort(desired.ListenPort)}', but the new value is invalid. Keeping port {UnifiedServerCoordinator.ListenPort}."),
                        category: "Config");
                }
                else if (UnifiedServerCoordinator.RebindListener(desired.ListenPort)) {
                    applyListenPort(desired.ListenPort);
                    appliedListenPort = desired.ListenPort;

                    UnifierApi.Logger.Info(
                        GetParticularString("{0} is current listen port, {1} is desired listen port", $"launcher.listenPort changed from '{DescribeListenPort(current.ListenPort)}' to '{DescribeListenPort(desired.ListenPort)}'. The active listener has been rebound."),
                        category: "Config");
                }
                else {
                    UnifierApi.Logger.Warning(
                        GetParticularString("{0} is current listen port, {1} is desired listen port, {2} is active listen port", $"launcher.listenPort changed from '{DescribeListenPort(current.ListenPort)}' to '{DescribeListenPort(desired.ListenPort)}', but rebinding failed. Keeping port {UnifiedServerCoordinator.ListenPort}."),
                        category: "Config");
                }
            }
            else {
                appliedListenPort = desired.ListenPort;
            }

            string currentPassword = current.ServerPassword ?? "";
            string desiredPassword = desired.ServerPassword ?? "";
            if (!OrdinalEquals(currentPassword, desiredPassword)) {
                applyServerPassword(desiredPassword);
                UnifierApi.Logger.Info(
                    GetString("launcher.serverPassword changed. New connections will use the updated password policy."),
                    category: "Config");
            }

            if (current.JoinServer != desired.JoinServer) {
                UnifierApi.Logger.Info(
                    GetParticularString("{0} is current join server mode, {1} is desired join server mode", $"launcher.joinServer changed from '{DescribeJoinServerMode(current.JoinServer)}' to '{DescribeJoinServerMode(desired.JoinServer)}'. Future joins will use the updated policy."),
                    category: "Config");
            }

            ApplyAutoStartServerDiffs(current.AutoStartServers, desired.AutoStartServers);
            return new LauncherRuntimeSettings {
                LogMode = current.LogMode,
                ListenPort = appliedListenPort,
                ServerPassword = desiredPassword,
                JoinServer = desired.JoinServer,
                AutoStartServers = CloneAutoStartServers(desired.AutoStartServers),
            };
        }

        public void ApplyAutoStartServers(IEnumerable<AutoStartServerConfiguration> servers) {
            foreach (AutoStartServerConfiguration server in servers) {
                AutoStartServer(server);
            }
        }

        public LauncherRuntimeSettings SyncRuntimeSettingsFromInteractiveInput(
            LauncherRuntimeSettings current,
            int listenPort,
            string? serverPassword) {

            return new LauncherRuntimeSettings {
                LogMode = current.LogMode,
                ListenPort = listenPort,
                ServerPassword = serverPassword,
                JoinServer = current.JoinServer,
                AutoStartServers = CloneAutoStartServers(current.AutoStartServers),
            };
        }

        private List<AutoStartServerConfiguration> MergeAutoStartServers(
            List<AutoStartServerConfiguration>? existingServers,
            List<AutoStartServerConfiguration> cliServers,
            AutoStartServerMergeMode mergeMode) {

            List<AutoStartMergeCandidate> candidates = [];
            int order = 0;

            switch (mergeMode) {
                case AutoStartServerMergeMode.AddIfMissing:
                    AddCandidates(existingServers, "config", 2, ref order, candidates);
                    AddCandidates(cliServers, "cli", 1, ref order, candidates);
                    break;

                case AutoStartServerMergeMode.OverwriteByName:
                    AddCandidates(existingServers, "config", 1, ref order, candidates);
                    AddCandidates(cliServers, "cli", 2, ref order, candidates);
                    break;

                default:
                    AddCandidates(cliServers, "cli", 2, ref order, candidates);
                    break;
            }

            List<AutoStartMergeCandidate?> byName = ResolveNameConflicts(candidates, mergeMode);
            return ResolveWorldConflicts(byName, mergeMode);
        }

        private static void AddCandidates(
            List<AutoStartServerConfiguration>? source,
            string sourceName,
            int priority,
            ref int order,
            List<AutoStartMergeCandidate> destination) {

            if (source is null) {
                return;
            }

            foreach (AutoStartServerConfiguration server in source) {
                destination.Add(new AutoStartMergeCandidate(
                    Server: CloneAutoStartServer(server),
                    Source: sourceName,
                    Priority: priority,
                    Order: order++));
            }
        }

        private List<AutoStartMergeCandidate?> ResolveNameConflicts(
            List<AutoStartMergeCandidate> candidates,
            AutoStartServerMergeMode mergeMode) {

            List<AutoStartMergeCandidate?> selected = [];
            Dictionary<string, int> indexByName = new(StringComparer.OrdinalIgnoreCase);

            foreach (AutoStartMergeCandidate candidate in candidates) {
                string key = NormalizeAutoStartServerName(candidate.Server.Name);
                if (!indexByName.TryAdd(key, selected.Count)) {
                    int existingIndex = indexByName[key];
                    AutoStartMergeCandidate existing = selected[existingIndex]!.Value;
                    if (IsHigherPriority(in candidate, in existing)) {
                        WarnNameConflictIgnored(existing, candidate, mergeMode);
                        selected[existingIndex] = candidate;
                    }
                    else {
                        WarnNameConflictIgnored(candidate, existing, mergeMode);
                    }

                    continue;
                }

                selected.Add(candidate);
            }

            return selected;
        }

        private List<AutoStartServerConfiguration> ResolveWorldConflicts(
            List<AutoStartMergeCandidate?> selected,
            AutoStartServerMergeMode mergeMode) {

            Dictionary<string, int> worldOwner = new(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < selected.Count; i++) {
                AutoStartMergeCandidate? candidateMaybe = selected[i];
                if (!candidateMaybe.HasValue) {
                    continue;
                }

                AutoStartMergeCandidate candidate = candidateMaybe.Value;
                string worldKey = NormalizeWorldName(candidate.Server.WorldName);
                if (worldKey.Length == 0) {
                    continue;
                }

                if (!worldOwner.TryAdd(worldKey, i)) {
                    int existingIndex = worldOwner[worldKey];
                    if (!selected[existingIndex].HasValue) {
                        selected[existingIndex] = candidate;
                        selected[i] = null;
                        worldOwner[worldKey] = existingIndex;
                        continue;
                    }

                    AutoStartMergeCandidate existing = selected[existingIndex]!.Value;
                    if (IsHigherPriority(in candidate, in existing)) {
                        WarnWorldConflictIgnored(existing, candidate, mergeMode);
                        selected[existingIndex] = candidate;
                        selected[i] = null;
                        worldOwner[worldKey] = existingIndex;
                    }
                    else {
                        WarnWorldConflictIgnored(candidate, existing, mergeMode);
                        selected[i] = null;
                    }
                }
            }

            List<AutoStartServerConfiguration> merged = [];
            foreach (AutoStartMergeCandidate? item in selected) {
                if (item.HasValue) {
                    merged.Add(item.Value.Server);
                }
            }

            return merged;
        }

        private void WarnNameConflictIgnored(
            in AutoStartMergeCandidate ignored,
            in AutoStartMergeCandidate kept,
            AutoStartServerMergeMode mergeMode) {

            UnifierApi.Logger.Warning(
                GetParticularString("{0} is ignored server name, {1} is kept source, {2} is merge mode",
                    $"Auto-start server '{ignored.Server.Name}' from {ignored.Source} was ignored because a higher-priority entry already exists (kept source: {kept.Source}, merge mode: {DescribeAutoStartServerMergeMode(mergeMode)})."),
                category: "Launcher");
        }

        private void WarnWorldConflictIgnored(
            in AutoStartMergeCandidate ignored,
            in AutoStartMergeCandidate kept,
            AutoStartServerMergeMode mergeMode) {

            UnifierApi.Logger.Warning(
                GetParticularString("{0} is ignored server name, {1} is world name, {2} is kept server name, {3} is merge mode",
                    $"Auto-start server '{ignored.Server.Name}' (world: '{ignored.Server.WorldName}') was ignored due to world conflict with higher-priority server '{kept.Server.Name}' (merge mode: {DescribeAutoStartServerMergeMode(mergeMode)})."),
                category: "Launcher");
        }

        private static bool IsHigherPriority(
            scoped in AutoStartMergeCandidate left,
            scoped in AutoStartMergeCandidate right) {

            if (left.Priority != right.Priority) {
                return left.Priority > right.Priority;
            }

            return left.Order < right.Order;
        }

        private static RootLauncherConfiguration CloneRootConfiguration(RootLauncherConfiguration source) {
            return new RootLauncherConfiguration {
                Logging = new LoggingConfiguration {
                    Mode = source.Logging?.Mode ?? "txt",
                },
                Launcher = new LauncherConfiguration {
                    ListenPort = source.Launcher?.ListenPort ?? -1,
                    ServerPassword = source.Launcher?.ServerPassword,
                    JoinServer = source.Launcher?.JoinServer ?? "none",
                    AutoStartServers = CloneAutoStartServers(source.Launcher?.AutoStartServers),
                },
            };
        }

        private static AutoStartServerConfiguration CloneAutoStartServer(AutoStartServerConfiguration source) {
            return new AutoStartServerConfiguration {
                Name = source.Name ?? "",
                WorldName = source.WorldName ?? "",
                Seed = source.Seed ?? "",
                Difficulty = source.Difficulty ?? "master",
                Size = source.Size ?? "large",
                Evil = source.Evil ?? "random",
            };
        }

        private static bool RootLauncherConfigurationEquivalent(
            RootLauncherConfiguration left,
            RootLauncherConfiguration right) {

            if (!OrdinalEquals(left.Logging?.Mode, right.Logging?.Mode)) {
                return false;
            }

            LauncherConfiguration leftLauncher = left.Launcher ?? new LauncherConfiguration();
            LauncherConfiguration rightLauncher = right.Launcher ?? new LauncherConfiguration();

            if (leftLauncher.ListenPort != rightLauncher.ListenPort) {
                return false;
            }

            if (!OrdinalEquals(leftLauncher.ServerPassword ?? "", rightLauncher.ServerPassword ?? "")) {
                return false;
            }

            if (!OrdinalIgnoreCaseEquals(leftLauncher.JoinServer, rightLauncher.JoinServer)) {
                return false;
            }

            return AutoStartServerListEquivalent(leftLauncher.AutoStartServers, rightLauncher.AutoStartServers);
        }

        private static bool AutoStartServerListEquivalent(
            List<AutoStartServerConfiguration>? left,
            List<AutoStartServerConfiguration>? right) {

            int leftCount = left?.Count ?? 0;
            int rightCount = right?.Count ?? 0;
            if (leftCount != rightCount) {
                return false;
            }

            if (leftCount == 0) {
                return true;
            }

            for (int i = 0; i < leftCount; i++) {
                if (!AutoStartServerDefinitionsEquivalent(left![i], right![i])) {
                    return false;
                }
            }

            return true;
        }

        private static string NormalizeWorldName(string? value) {
            return value?.Trim() ?? "";
        }

        private void ApplyAutoStartServerDiffs(
            List<AutoStartServerConfiguration> current,
            List<AutoStartServerConfiguration> desired) {

            Dictionary<string, AutoStartServerConfiguration> currentByName = BuildAutoStartServerLookup(current, GetString("current runtime snapshot"));
            Dictionary<string, AutoStartServerConfiguration> desiredByName = BuildAutoStartServerLookup(desired, GetString("reloaded config snapshot"));

            foreach (KeyValuePair<string, AutoStartServerConfiguration> entry in desiredByName) {
                if (currentByName.Remove(entry.Key, out AutoStartServerConfiguration? existing)) {
                    if (!AutoStartServerDefinitionsEquivalent(existing, entry.Value)) {
                        UnifierApi.Logger.Warning(
                            GetParticularString("{0} is server name", $"Server '{entry.Value.Name}' was updated in root config, but existing server definitions are not hot-applied in phase 1. Restart or an explicit lifecycle API is required."),
                            category: "Config");
                    }

                    continue;
                }

                AutoStartServer(entry.Value);
            }

            foreach (AutoStartServerConfiguration removed in currentByName.Values) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is server name", $"Server '{removed.Name}' was removed from root config, but server removal is not hot-applied in phase 1. Restart or an explicit lifecycle API is required."),
                    category: "Config");
            }
        }

        private Dictionary<string, AutoStartServerConfiguration> BuildAutoStartServerLookup(
            IEnumerable<AutoStartServerConfiguration> source,
            string sourceName) {

            Dictionary<string, AutoStartServerConfiguration> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (AutoStartServerConfiguration server in source) {
                string key = NormalizeAutoStartServerName(server.Name);
                if (!result.TryAdd(key, server)) {
                    UnifierApi.Logger.Warning(
                        GetParticularString("{0} is server name, {1} is config snapshot source", $"Duplicate autoStartServers entry '{server.Name}' was found in the {sourceName}. Later duplicates are ignored for hot reload diffing."),
                        category: "Config");
                }
            }

            return result;
        }

        private static string NormalizeAutoStartServerName(string? value) {
            return value?.Trim() ?? "";
        }

        private static string TrimOrEmpty(string? value) {
            return value?.Trim() ?? "";
        }

        private static bool OrdinalEquals(string? left, string? right) {
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        private static bool OrdinalIgnoreCaseEquals(string? left, string? right) {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool AutoStartServerDefinitionsEquivalent(
            AutoStartServerConfiguration left,
            AutoStartServerConfiguration right) {

            return OrdinalIgnoreCaseEquals(NormalizeAutoStartServerName(left.Name), NormalizeAutoStartServerName(right.Name))
                && OrdinalIgnoreCaseEquals(TrimOrEmpty(left.WorldName), TrimOrEmpty(right.WorldName))
                && OrdinalEquals(TrimOrEmpty(left.Seed), TrimOrEmpty(right.Seed))
                && OrdinalIgnoreCaseEquals(TrimOrEmpty(left.Difficulty), TrimOrEmpty(right.Difficulty))
                && OrdinalIgnoreCaseEquals(TrimOrEmpty(left.Size), TrimOrEmpty(right.Size))
                && OrdinalIgnoreCaseEquals(TrimOrEmpty(left.Evil), TrimOrEmpty(right.Evil));
        }

        private static string DescribeJoinServerMode(JoinServerMode mode) {
            return mode switch {
                JoinServerMode.Random => "random",
                JoinServerMode.First => "first",
                _ => "none",
            };
        }

        private static string DescribeLogMode(LogPersistenceMode mode) {
            return mode switch {
                LogPersistenceMode.None => "none",
                LogPersistenceMode.Sqlite => "sqlite",
                _ => "txt",
            };
        }

        private static string DescribeAutoStartServerMergeMode(AutoStartServerMergeMode mode) {
            return mode switch {
                AutoStartServerMergeMode.OverwriteByName => "overwrite",
                AutoStartServerMergeMode.AddIfMissing => "append",
                _ => "replace",
            };
        }

        private static string DescribeListenPort(int port) {
            return LauncherPortRules.IsValidListenPort(port)
                ? port.ToString(CultureInfo.InvariantCulture)
                : "unset";
        }

        private static bool TryParseAutoStartServerOverride(string serverArgs, [NotNullWhen(true)] out AutoStartServerConfiguration? result) {
            if (!Utilities.CLI.TryParseSubArguments(serverArgs, out Dictionary<string, string>? parsed)) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is server argument string", $"Invalid server argument: {serverArgs}"),
                    category: "Launcher");
                UnifierApi.Logger.Warning(
                    GetString("Expected format: -server name:<name> worldname:<worldname> gamemode:<value> size:<value> evil:<value> seed:<value>"),
                    category: "Launcher");

                result = null;
                return false;
            }

            AutoStartServerConfiguration server = new();
            foreach (KeyValuePair<string, string> serverArg in parsed) {
                switch (serverArg.Key) {
                    case "name":
                        server.Name = serverArg.Value.Trim();
                        break;

                    case "worldname":
                        server.WorldName = serverArg.Value.Trim();
                        break;

                    case "seed":
                        server.Seed = serverArg.Value.Trim();
                        break;

                    case "gamemode":
                    case "difficulty":
                        server.Difficulty = serverArg.Value.Trim();
                        break;

                    case "size":
                        server.Size = serverArg.Value.Trim();
                        break;

                    case "evil":
                        server.Evil = serverArg.Value.Trim();
                        break;
                }
            }

            result = server;
            return true;
        }

        private void AutoStartServer(AutoStartServerConfiguration config) {
            string serverName = config.Name?.Trim() ?? "";
            string worldName = config.WorldName?.Trim() ?? "";
            string seed = config.Seed?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(worldName)) {
                UnifierApi.Logger.Warning(GetString("Parameter 'worldName' is required."), category: "Launcher");
                return;
            }

            if (string.IsNullOrWhiteSpace(serverName)) {
                UnifierApi.Logger.Warning(GetString("Parameter 'name' is required."), category: "Launcher");
                return;
            }

            if (UnifiedServerCoordinator.Servers.Any(s => s.Name == serverName)) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is server name", $"Server name '{serverName}' is already in use"),
                    category: "Launcher");
                return;
            }

            ServerContext? nameConflict = UnifiedServerCoordinator.Servers.FirstOrDefault(s => s.Main.worldName == worldName);
            if (nameConflict is not null) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is world name, {1} is server name", $"World name '{worldName}' is already in use by server '{nameConflict.Name}'"),
                    category: "Launcher");
                return;
            }

            if (!TryResolveDifficulty(config.Difficulty, out int difficulty)) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is user input value for server difficulty", $"Invalid server difficulty: {config.Difficulty}"),
                    category: "Launcher");
                UnifierApi.Logger.Warning(
                    GetString("Expected value: an integer between 0 and 3 (inclusive), or one of the strings: normal, expert, master, creative"),
                    category: "Launcher");
                return;
            }

            if (!TryResolveSize(config.Size, out int size)) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is user input value for server size", $"Invalid server size: {config.Size}"),
                    category: "Launcher");
                UnifierApi.Logger.Warning(
                    GetString("Expected value: an integer between 1 and 3 (inclusive), or one of the strings: small, medium, large"),
                    category: "Launcher");
                return;
            }

            if (!TryResolveEvil(config.Evil, out int evil)) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is user input value for server evil (world corruption/crimson type)", $"Invalid server evil: {config.Evil}"),
                    category: "Launcher");
                UnifierApi.Logger.Warning(
                    GetString("Expected value: an integer between 0 and 2 (inclusive), or one of the strings: random, corruption, crimson"),
                    category: "Launcher");
                return;
            }

            ServerContext server = new(serverName, IWorldDataProvider.GenerateOrLoadExisting(worldName, size, difficulty, evil, seed));
            Task.Run(() => server.Program.LaunchGame([]));
            UnifiedServerCoordinator.AddServer(server);
        }

        private static bool TryResolveDifficulty(string? value, out int difficulty) {
            string text = value?.Trim() ?? "";
            if (int.TryParse(text, out difficulty) && difficulty >= 0 && difficulty <= 3) {
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldDifficultyId.Normal)) || text == "n") {
                difficulty = 0;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldDifficultyId.Expert)) || text == "e") {
                difficulty = 1;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldDifficultyId.Master)) || text == "m") {
                difficulty = 2;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldDifficultyId.Creative)) || text == "c") {
                difficulty = 3;
                return true;
            }

            difficulty = 0;
            return false;
        }

        private static bool TryResolveSize(string? value, out int size) {
            string text = value?.Trim() ?? "";
            if (int.TryParse(text, out size) && size >= 1 && size <= 3) {
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldSizeId.Small)) || text == "s") {
                size = 1;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldSizeId.Medium)) || text == "m") {
                size = 2;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldSizeId.Large)) || text == "l") {
                size = 3;
                return true;
            }

            size = 0;
            return false;
        }

        private static bool TryResolveEvil(string? value, out int evil) {
            string text = value?.Trim() ?? "";
            if (int.TryParse(text, out evil) && evil >= 0 && evil <= 2) {
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldEvilId.Random))) {
                evil = 0;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldEvilId.Corruption))) {
                evil = 1;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldEvilId.Crimson))) {
                evil = 2;
                return true;
            }

            evil = 0;
            return false;
        }

        private static bool TryParseAutoStartServerMergeMode(string? value, out AutoStartServerMergeMode mode) {
            string text = value?.Trim() ?? "";
            if (OrdinalIgnoreCaseEquals(text, "replace") || OrdinalIgnoreCaseEquals(text, "clean")) {
                mode = AutoStartServerMergeMode.ReplaceAll;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, "overwrite") || OrdinalIgnoreCaseEquals(text, "name")) {
                mode = AutoStartServerMergeMode.OverwriteByName;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, "append") || OrdinalIgnoreCaseEquals(text, "add")) {
                mode = AutoStartServerMergeMode.AddIfMissing;
                return true;
            }

            mode = AutoStartServerMergeMode.ReplaceAll;
            return false;
        }

        private static bool TryParseJoinServerMode(string? value, out JoinServerMode joinMode) {
            string text = value?.Trim() ?? "";
            if (text.Length == 0 || OrdinalIgnoreCaseEquals(text, "none")) {
                joinMode = JoinServerMode.None;
                return true;
            }

            if (text is "random" or "rnd" or "r") {
                joinMode = JoinServerMode.Random;
                return true;
            }

            if (text is "first" or "f") {
                joinMode = JoinServerMode.First;
                return true;
            }

            joinMode = JoinServerMode.None;
            return false;
        }

        private static JoinServerMode ResolveConfiguredJoinServerMode(string? value) {
            if (TryParseJoinServerMode(value, out JoinServerMode mode)) {
                return mode;
            }

            UnifierApi.Logger.Warning(
                GetParticularString("{0} is configured joinServer value", $"Invalid joinServer setting '{value}'. Falling back to 'none'."),
                category: "Config");
            return JoinServerMode.None;
        }

        private static bool TryParseLogMode(string? value, out LogPersistenceMode mode) {
            string text = value?.Trim() ?? "";
            if (OrdinalIgnoreCaseEquals(text, "txt")) {
                mode = LogPersistenceMode.Txt;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, "none")) {
                mode = LogPersistenceMode.None;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, "sqlite")) {
                mode = LogPersistenceMode.Sqlite;
                return true;
            }

            mode = LogPersistenceMode.Txt;
            return false;
        }

        private static LogPersistenceMode ResolveConfiguredLogMode(string? value) {
            if (TryParseLogMode(value, out LogPersistenceMode mode)) {
                return mode;
            }

            UnifierApi.Logger.Warning(
                GetParticularString("{0} is configured logging.mode value", $"Invalid logging.mode setting '{value}'. Falling back to 'txt'."),
                category: "Config");
            return LogPersistenceMode.Txt;
        }

        private readonly record struct AutoStartMergeCandidate(
            AutoStartServerConfiguration Server,
            string Source,
            int Priority,
            int Order);

        private static List<AutoStartServerConfiguration> CloneAutoStartServers(List<AutoStartServerConfiguration>? source) {
            List<AutoStartServerConfiguration> cloned = [];
            if (source is null) {
                return cloned;
            }

            foreach (AutoStartServerConfiguration server in source) {
                cloned.Add(CloneAutoStartServer(server));
            }

            return cloned;
        }
    }
}
