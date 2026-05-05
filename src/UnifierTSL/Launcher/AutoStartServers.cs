using System.Diagnostics.CodeAnalysis;
using UnifierTSL.Servers;

namespace UnifierTSL.Launcher
{
    internal static class AutoStartServers
    {
        private const string ConfigSourceName = "config";
        private const string CliSourceName = "cli";

        private static readonly IReadOnlyList<CliBinding> cliBindings = [
            new CliBinding(
                ["-autostart", "-addserver", "-server"],
                static (_, values, overrides, _) => {
                    overrides.HasAutoStartServers = true;
                    foreach (string serverArgs in values) {
                        if (TryParseOverride(serverArgs, out AutoStartServerConfiguration? server)) {
                            overrides.AutoStartServers.Add(server);
                        }
                    }
                }),
            new CliBinding(
                ["-servermerge", "--server-merge", "--auto-start-merge"],
                static (_, values, overrides, _) => {
                    string firstValue = values[0];
                    if (LauncherSettingValues.TryParseAutoStartServerMergeMode(firstValue, out AutoStartServerMergeMode mergeMode)) {
                        overrides.AutoStartServersMergeMode = mergeMode;
                    }
                    else {
                        UnifierApi.Logger.Warning(
                            GetParticularString("{0} is user input value for server merge mode", $"Invalid server merge mode: {firstValue}"),
                            category: LauncherCategories.Launcher);
                        UnifierApi.Logger.Warning(
                            GetString("Expected value: replace (default), overwrite, or append."),
                            category: LauncherCategories.Launcher);
                    }
                }),
        ];

        public static ILauncherSettingSpec Spec { get; } = new AutoStartServerSettingSpec();

        public static void ApplyAll(IEnumerable<AutoStartServerConfiguration> servers) {
            foreach (AutoStartServerConfiguration server in servers) {
                Start(server);
            }
        }

        private static List<AutoStartServerConfiguration> Merge(
            List<AutoStartServerConfiguration>? existingServers,
            List<AutoStartServerConfiguration> cliServers,
            AutoStartServerMergeMode mergeMode) {

            List<AutoStartMergeCandidate> candidates = [];
            int order = 0;

            switch (mergeMode) {
                case AutoStartServerMergeMode.AddIfMissing:
                    AddCandidates(existingServers, ConfigSourceName, 2, ref order, candidates);
                    AddCandidates(cliServers, CliSourceName, 1, ref order, candidates);
                    break;

                case AutoStartServerMergeMode.OverwriteByName:
                    AddCandidates(existingServers, ConfigSourceName, 1, ref order, candidates);
                    AddCandidates(cliServers, CliSourceName, 2, ref order, candidates);
                    break;

                default:
                    AddCandidates(cliServers, CliSourceName, 2, ref order, candidates);
                    break;
            }

            List<AutoStartMergeCandidate?> byName = ResolveNameConflicts(candidates, mergeMode);
            return ResolveWorldConflicts(byName, mergeMode);
        }

        private static void ApplyDiffs(
            List<AutoStartServerConfiguration> current,
            List<AutoStartServerConfiguration> desired) {

            Dictionary<string, AutoStartServerConfiguration> currentByName = BuildLookup(current, GetString("current runtime snapshot"));
            Dictionary<string, AutoStartServerConfiguration> desiredByName = BuildLookup(desired, GetString("reloaded config snapshot"));

            foreach (KeyValuePair<string, AutoStartServerConfiguration> entry in desiredByName) {
                if (currentByName.Remove(entry.Key, out AutoStartServerConfiguration? existing)) {
                    if (!DefinitionsEquivalent(existing, entry.Value)) {
                        UnifierApi.Logger.Warning(
                            GetParticularString("{0} is server name", $"Server '{entry.Value.Name}' was updated in root config, but existing server definitions are not hot-applied in phase 1. Restart or an explicit lifecycle API is required."),
                            category: LauncherCategories.Config);
                    }

                    continue;
                }

                Start(entry.Value);
            }

            foreach (AutoStartServerConfiguration removed in currentByName.Values) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is server name", $"Server '{removed.Name}' was removed from root config, but server removal is not hot-applied in phase 1. Restart or an explicit lifecycle API is required."),
                    category: LauncherCategories.Config);
            }
        }

        private static List<AutoStartServerConfiguration> CloneList(List<AutoStartServerConfiguration>? source) {
            List<AutoStartServerConfiguration> cloned = [];
            if (source is null) {
                return cloned;
            }

            foreach (AutoStartServerConfiguration server in source) {
                cloned.Add(Clone(server));
            }

            return cloned;
        }

        private static AutoStartServerConfiguration Clone(AutoStartServerConfiguration source) {
            return new AutoStartServerConfiguration {
                Name = source.Name ?? "",
                WorldName = source.WorldName ?? "",
                Seed = source.Seed ?? "",
                Difficulty = source.Difficulty ?? "master",
                Size = source.Size ?? "large",
                Evil = source.Evil ?? "random",
            };
        }

        private static bool ListEquivalent(
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
                if (!DefinitionsEquivalent(left![i], right![i])) {
                    return false;
                }
            }

            return true;
        }

        private static bool DefinitionsEquivalent(
            AutoStartServerConfiguration left,
            AutoStartServerConfiguration right) {

            return LauncherSettingValues.OrdinalIgnoreCaseEquals(LauncherSettingValues.NormalizeAutoStartServerName(left.Name), LauncherSettingValues.NormalizeAutoStartServerName(right.Name))
                && LauncherSettingValues.OrdinalIgnoreCaseEquals(LauncherSettingValues.TrimOrEmpty(left.WorldName), LauncherSettingValues.TrimOrEmpty(right.WorldName))
                && LauncherSettingValues.OrdinalEquals(LauncherSettingValues.TrimOrEmpty(left.Seed), LauncherSettingValues.TrimOrEmpty(right.Seed))
                && LauncherSettingValues.OrdinalIgnoreCaseEquals(LauncherSettingValues.TrimOrEmpty(left.Difficulty), LauncherSettingValues.TrimOrEmpty(right.Difficulty))
                && LauncherSettingValues.OrdinalIgnoreCaseEquals(LauncherSettingValues.TrimOrEmpty(left.Size), LauncherSettingValues.TrimOrEmpty(right.Size))
                && LauncherSettingValues.OrdinalIgnoreCaseEquals(LauncherSettingValues.TrimOrEmpty(left.Evil), LauncherSettingValues.TrimOrEmpty(right.Evil));
        }

        private static bool TryParseOverride(string serverArgs, [NotNullWhen(true)] out AutoStartServerConfiguration? result) {
            if (!Utilities.CLI.TryParseSubArguments(serverArgs, out Dictionary<string, string>? parsed)) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is server argument string", $"Invalid server argument: {serverArgs}"),
                    category: LauncherCategories.Launcher);
                UnifierApi.Logger.Warning(
                    GetString("Expected format: -server name:<name> worldname:<worldname> gamemode:<value> size:<value> evil:<value> seed:<value>"),
                    category: LauncherCategories.Launcher);

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

        private static void Start(AutoStartServerConfiguration config) {
            string serverName = config.Name?.Trim() ?? "";
            string worldName = config.WorldName?.Trim() ?? "";
            string seed = config.Seed?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(worldName)) {
                UnifierApi.Logger.Warning(GetString("Parameter 'worldName' is required."), category: LauncherCategories.Launcher);
                return;
            }

            if (string.IsNullOrWhiteSpace(serverName)) {
                UnifierApi.Logger.Warning(GetString("Parameter 'name' is required."), category: LauncherCategories.Launcher);
                return;
            }

            if (UnifiedServerCoordinator.Servers.Any(s => s.Name == serverName)) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is server name", $"Server name '{serverName}' is already in use"),
                    category: LauncherCategories.Launcher);
                return;
            }

            ServerContext? worldConflict = UnifiedServerCoordinator.Servers.FirstOrDefault(s => s.Main.worldName == worldName);
            if (worldConflict is not null) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is world name, {1} is server name", $"World name '{worldName}' is already in use by server '{worldConflict.Name}'"),
                    category: LauncherCategories.Launcher);
                return;
            }

            if (!LauncherSettingValues.TryResolveDifficulty(config.Difficulty, out int difficulty)) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is user input value for server difficulty", $"Invalid server difficulty: {config.Difficulty}"),
                    category: LauncherCategories.Launcher);
                UnifierApi.Logger.Warning(
                    GetString("Expected value: an integer between 0 and 3 (inclusive), or one of the strings: normal, expert, master, creative"),
                    category: LauncherCategories.Launcher);
                return;
            }

            if (!LauncherSettingValues.TryResolveSize(config.Size, out int size)) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is user input value for server size", $"Invalid server size: {config.Size}"),
                    category: LauncherCategories.Launcher);
                UnifierApi.Logger.Warning(
                    GetString("Expected value: an integer between 1 and 3 (inclusive), or one of the strings: small, medium, large"),
                    category: LauncherCategories.Launcher);
                return;
            }

            if (!LauncherSettingValues.TryResolveEvil(config.Evil, out int evil)) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is user input value for server evil (world corruption/crimson type)", $"Invalid server evil: {config.Evil}"),
                    category: LauncherCategories.Launcher);
                UnifierApi.Logger.Warning(
                    GetString("Expected value: an integer between 0 and 2 (inclusive), or one of the strings: random, corruption, crimson"),
                    category: LauncherCategories.Launcher);
                return;
            }

            ServerContext server = new(serverName, IWorldDataProvider.GenerateOrLoadExisting(worldName, size, difficulty, evil, seed));
            server.Run([]);
            UnifiedServerCoordinator.AddServer(server);
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
                    Server: Clone(server),
                    Source: sourceName,
                    Priority: priority,
                    Order: order++));
            }
        }

        private static List<AutoStartMergeCandidate?> ResolveNameConflicts(
            List<AutoStartMergeCandidate> candidates,
            AutoStartServerMergeMode mergeMode) {

            List<AutoStartMergeCandidate?> selected = [];
            Dictionary<string, int> indexByName = new(StringComparer.OrdinalIgnoreCase);

            foreach (AutoStartMergeCandidate candidate in candidates) {
                string key = LauncherSettingValues.NormalizeAutoStartServerName(candidate.Server.Name);
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

        private static List<AutoStartServerConfiguration> ResolveWorldConflicts(
            List<AutoStartMergeCandidate?> selected,
            AutoStartServerMergeMode mergeMode) {

            Dictionary<string, int> worldOwner = new(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < selected.Count; i++) {
                AutoStartMergeCandidate? candidateMaybe = selected[i];
                if (!candidateMaybe.HasValue) {
                    continue;
                }

                AutoStartMergeCandidate candidate = candidateMaybe.Value;
                string worldKey = LauncherSettingValues.NormalizeWorldName(candidate.Server.WorldName);
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

        private static void WarnNameConflictIgnored(
            in AutoStartMergeCandidate ignored,
            in AutoStartMergeCandidate kept,
            AutoStartServerMergeMode mergeMode) {

            UnifierApi.Logger.Warning(
                GetParticularString("{0} is ignored server name, {1} is kept source, {2} is merge mode",
                    $"Auto-start server '{ignored.Server.Name}' from {ignored.Source} was ignored because a higher-priority entry already exists (kept source: {kept.Source}, merge mode: {LauncherSettingValues.DescribeAutoStartServerMergeMode(mergeMode)})."),
                category: LauncherCategories.Launcher);
        }

        private static void WarnWorldConflictIgnored(
            in AutoStartMergeCandidate ignored,
            in AutoStartMergeCandidate kept,
            AutoStartServerMergeMode mergeMode) {

            UnifierApi.Logger.Warning(
                GetParticularString("{0} is ignored server name, {1} is world name, {2} is kept server name, {3} is merge mode",
                    $"Auto-start server '{ignored.Server.Name}' (world: '{ignored.Server.WorldName}') was ignored due to world conflict with higher-priority server '{kept.Server.Name}' (merge mode: {LauncherSettingValues.DescribeAutoStartServerMergeMode(mergeMode)})."),
                category: LauncherCategories.Launcher);
        }

        private static bool IsHigherPriority(
            scoped in AutoStartMergeCandidate left,
            scoped in AutoStartMergeCandidate right) {

            if (left.Priority != right.Priority) {
                return left.Priority > right.Priority;
            }

            return left.Order < right.Order;
        }

        private static Dictionary<string, AutoStartServerConfiguration> BuildLookup(
            IEnumerable<AutoStartServerConfiguration> source,
            string sourceName) {

            Dictionary<string, AutoStartServerConfiguration> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (AutoStartServerConfiguration server in source) {
                string key = LauncherSettingValues.NormalizeAutoStartServerName(server.Name);
                if (!result.TryAdd(key, server)) {
                    UnifierApi.Logger.Warning(
                        GetParticularString("{0} is server name, {1} is config snapshot source", $"Duplicate autoStartServers entry '{server.Name}' was found in the {sourceName}. Later duplicates are ignored for hot reload diffing."),
                        category: LauncherCategories.Config);
                }
            }

            return result;
        }

        private sealed class AutoStartServerSettingSpec : ILauncherSettingSpec
        {
            public IReadOnlyList<CliBinding> CliBindings => cliBindings;

            public void CopyConfig(RootLauncherConfiguration source, RootLauncherConfiguration destination) {
                destination.Launcher.AutoStartServers = CloneList(source.Launcher?.AutoStartServers);
            }

            public bool ConfigEquivalent(RootLauncherConfiguration left, RootLauncherConfiguration right) {
                return ListEquivalent(left.Launcher?.AutoStartServers, right.Launcher?.AutoStartServers);
            }

            public void ApplyOverride(RootLauncherConfiguration config, LauncherCliOverrides overrides) {
                if (overrides.HasAutoStartServers) {
                    AutoStartServerMergeMode mergeMode = overrides.AutoStartServersMergeMode ?? AutoStartServerMergeMode.ReplaceAll;
                    config.Launcher.AutoStartServers = Merge(
                        existingServers: config.Launcher.AutoStartServers,
                        cliServers: overrides.AutoStartServers,
                        mergeMode: mergeMode);
                }
                else if (overrides.AutoStartServersMergeMode.HasValue) {
                    UnifierApi.Logger.Warning(
                        GetString("'-servermerge/--server-merge' was provided without any '-server' arguments. The merge mode was ignored."),
                        category: LauncherCategories.Launcher);
                }
            }

            public void ApplyConfiguredValue(
                RootLauncherConfiguration config,
                LauncherRuntimeSettings.Builder builder) {
                builder.AutoStartServers = CloneList(config.Launcher.AutoStartServers);
            }

            public void ApplyInteractiveInput(
                LauncherRuntimeSettings.Builder builder,
                InteractiveInput input) {
            }

            public void ApplyReload(
                LauncherRuntimeSettings.Builder builder,
                LauncherRuntimeSettings current,
                LauncherRuntimeSettings desired,
                ReloadContext context) {

                ApplyDiffs(current.AutoStartServers, desired.AutoStartServers);
                builder.AutoStartServers = CloneList(desired.AutoStartServers);
            }
        }
    }
}
