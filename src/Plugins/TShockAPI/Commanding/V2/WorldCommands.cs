using UnifierTSL;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding;
using UnifierTSL.Servers;

namespace TShockAPI.Commanding.V2
{
    internal static class WorldCommandHelpers
    {
        public static TSServerPlayer ResolveServerPlayer(ServerContext server) {
            try {
                return server.GetExtension<TSServerPlayer>();
            }
            catch (IndexOutOfRangeException) {
                return new TSServerPlayer(server);
            }
        }

        public static void SendWorldInfo(ServerContext server) {
            if (!server.IsRunning) {
                return;
            }

            server.NetMessage.SendData((int)PacketTypes.WorldInfo, -1, -1);
        }
    }

    [CommandController("worldmode", Summary = nameof(ControllerSummary))]
    [Aliases("gamemode")]
    internal static class WorldModeCommand
    {
        private static string ControllerSummary => GetString("Changes the world mode.");
        private static string ExecuteSummary => GetString("Changes the current world's game mode.");

        private static readonly IReadOnlyDictionary<string, int> WorldModes =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
                ["normal"] = 0,
                ["expert"] = 1,
                ["master"] = 2,
                ["journey"] = 3,
                ["creative"] = 3,
            };

        private static readonly string[] WorldModeDisplayNames = ["normal", "expert", "master", "journey"];

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.toggleexpert), ServerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] ServerContext server,
            [CommandParam(Name = "mode")]
            [CommandPrompt(
                SuggestionKindId = PromptSuggestionKindIds.Enum,
                EnumCandidates = ["normal", "expert", "master", "journey", "creative", "0", "1", "2", "3"])]
            string mode) {
            if (int.TryParse(mode, out var parsedMode)) {
                if (parsedMode < 0 || parsedMode >= WorldModeDisplayNames.Length) {
                    return CommandOutcome.Error(GetString("Invalid world mode. Valid world modes: {0}", string.Join(", ", WorldModes.Keys)));
                }

                server.Main.GameMode = parsedMode;
                WorldCommandHelpers.SendWorldInfo(server);
                return CommandOutcome.Success(GetString("World mode set to {0}.", WorldModeDisplayNames[parsedMode]));
            }

            if (!WorldModes.TryGetValue(mode, out var resolvedMode)) {
                return CommandOutcome.Error(GetString("Invalid mode world mode. Valid modes: {0}", string.Join(", ", WorldModes.Keys)));
            }

            server.Main.GameMode = resolvedMode;
            WorldCommandHelpers.SendWorldInfo(server);
            return CommandOutcome.Success(GetString("World mode set to {0}.", WorldModeDisplayNames[resolvedMode]));
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.ErrorBuilder(GetString("Invalid syntax. Proper syntax: {0}worldmode <mode>.", Commands.Specifier))
                .AddError(GetString("Valid world modes: {0}", string.Join(", ", WorldModes.Keys)))
                .Build();
        }
    }

    [CommandController("worldevent", Summary = nameof(ControllerSummary))]
    internal static class WorldEventCommand
    {
        private static string ControllerSummary => GetString("Enables starting and stopping various world events.");
        private static string MeteorSummary => GetString("Triggers a meteor.");
        private static string FullMoonSummary => GetString("Starts a full moon event.");
        private static string BloodMoonSummary => GetString("Starts or stops the blood moon event.");
        private static string EclipseSummary => GetString("Starts or stops an eclipse.");
        private static string InvasionUsageSummary => GetString("Shows invasion usage when no invasion type is provided.");
        private static string StopInvasionActionSummary => GetString("Stops the current invasion when one is active.");
        private static string StartStandardInvasionSummary => GetString("Starts a supported invasion.");
        private static string InvasionTypeInvalidTokenMessage => GetString("Invalid invasion type. Valid invasion types: goblins, snowmen, pirates, pumpkinmoon, frostmoon, martians.");
        private static string StartPumpkinMoonSummary => GetString("Starts the pumpkin moon event.");
        private static string WaveInvalidTokenMessage => GetString("Invalid pumpkin moon event wave.");
        private static string WaveOutOfRangeMessage => GetString("Invalid pumpkin moon event wave.");
        private static string StartFrostMoonSummary => GetString("Starts the frost moon event.");
        private static string WaveInvalidTokenMessage2 => GetString("Invalid frost moon event wave.");
        private static string WaveOutOfRangeMessage2 => GetString("Invalid frost moon event wave.");
        private static string SandstormSummary => GetString("Starts or stops a sandstorm.");
        private static string RainSummary => GetString("Starts or stops rain, slime rain, or coin rain.");
        private static string LanternsNightSummary => GetString("Starts or stops lantern night.");
        private static string MeteorShowerSummary => GetString("Starts or stops a meteor shower.");

        internal enum RainVariant : byte
        {
            [CommandToken("normal")]
            Normal,

            [CommandToken("slime")]
            Slime,

            [CommandToken("coin")]
            Coin,
        }

        internal enum StandardInvasionKind : byte
        {
            [CommandToken("goblin", "goblins")]
            Goblins,

            [CommandToken("snowman", "snowmen")]
            Snowmen,

            [CommandToken("pirate", "pirates")]
            Pirates,

            [CommandToken("martian", "martians")]
            Martians,
        }

        private static readonly string[] ValidEvents = [
            "meteor",
            "fullmoon",
            "bloodmoon",
            "eclipse",
            "invasion",
            "sandstorm",
            "rain",
            "lanternsnight",
            "meteorshower",
        ];

        private static readonly string[] ValidInvasions = [
            "goblins",
            "snowmen",
            "pirates",
            "pumpkinmoon",
            "frostmoon",
            "martians",
        ];

        [CommandAction("meteor", Summary = nameof(MeteorSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageevents), ServerScope = true)]
        public static CommandOutcome Meteor([FromAmbientContext] TSExecutionContext context) =>
            RequireEventPermissions(
                context,
                "meteor",
                [Permissions.dropmeteor, Permissions.managemeteorevent],
                static ctx => DropMeteor(ctx));

        [CommandAction("fullmoon", Summary = nameof(FullMoonSummary))]
        [CommandActionAlias("full moon")]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageevents), ServerScope = true)]
        public static CommandOutcome FullMoon([FromAmbientContext] TSExecutionContext context) =>
            RequireEventPermissions(
                context,
                "fullmoon",
                [Permissions.fullmoon, Permissions.managefullmoonevent],
                static ctx => StartFullMoon(ctx));

        [CommandAction("bloodmoon", Summary = nameof(BloodMoonSummary))]
        [CommandActionAlias("blood moon")]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageevents), ServerScope = true)]
        public static CommandOutcome BloodMoon([FromAmbientContext] TSExecutionContext context) =>
            RequireEventPermissions(
                context,
                "bloodmoon",
                [Permissions.bloodmoon, Permissions.managebloodmoonevent],
                static ctx => SetBloodMoon(ctx));

        [CommandAction("eclipse", Summary = nameof(EclipseSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageevents), ServerScope = true)]
        public static CommandOutcome Eclipse([FromAmbientContext] TSExecutionContext context) =>
            RequireEventPermissions(
                context,
                "eclipse",
                [Permissions.eclipse, Permissions.manageeclipseevent],
                static ctx => SetEclipse(ctx));

        [CommandAction("invasion", Summary = nameof(InvasionUsageSummary))]
        [CommandActionAlias("invade")]
        [RequireInvasionActivityPreBind(active: false)]
        [RequireUserArgumentCount(0)]
        [TShockCommand(nameof(Permissions.manageevents), ServerScope = true)]
        public static CommandOutcome InvasionUsage([FromAmbientContext] TSExecutionContext context) =>
            RequireEventPermissions(
                context,
                "invasion",
                [Permissions.invade, Permissions.manageinvasionevent],
                static _ => BuildInvasionUsageOutcome());

        [CommandAction("invasion", Summary = nameof(StopInvasionActionSummary))]
        [CommandActionAlias("invade")]
        [RequireInvasionActivityPreBind(active: true)]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageevents), ServerScope = true)]
        public static CommandOutcome StopInvasionAction([FromAmbientContext] TSExecutionContext context) =>
            RequireEventPermissions(
                context,
                "invasion",
                [Permissions.invade, Permissions.manageinvasionevent],
                static ctx => StopInvasion(ctx));

        [CommandAction("invasion", Summary = nameof(StartStandardInvasionSummary))]
        [CommandActionAlias("invade")]
        [RequireInvasionActivityPreBind(active: false)]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageevents), ServerScope = true)]
        public static CommandOutcome StartStandardInvasion(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "type")]
            [CommandTokenEnum(InvalidTokenMessage = nameof(InvasionTypeInvalidTokenMessage))]
            StandardInvasionKind invasionType) =>
            RequireEventPermissions(
                context,
                "invasion",
                [Permissions.invade, Permissions.manageinvasionevent],
                ctx => LaunchStandardInvasion(ctx, invasionType));

        [CommandAction("invasion", Summary = nameof(StartPumpkinMoonSummary))]
        [CommandActionAlias("invade")]
        [RequireInvasionActivityPreBind(active: false)]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageevents), ServerScope = true)]
        public static CommandOutcome StartPumpkinMoon(
            [FromAmbientContext] TSExecutionContext context,
            [CommandLiteral("pumpkin", "pumpkinmoon")]
            [CommandParam(Name = "type")]
            string invasionType,
            [Int32Value(
                Name = "wave",
                Minimum = 1,
                InvalidTokenMessage = nameof(WaveInvalidTokenMessage),
                OutOfRangeMessage = nameof(WaveOutOfRangeMessage))]
            int wave = 1) =>
            RequireEventPermissions(
                context,
                "invasion",
                [Permissions.invade, Permissions.manageinvasionevent],
                ctx => StartPumpkinMoon(ctx, wave));

        [CommandAction("invasion", Summary = nameof(StartFrostMoonSummary))]
        [CommandActionAlias("invade")]
        [RequireInvasionActivityPreBind(active: false)]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageevents), ServerScope = true)]
        public static CommandOutcome StartFrostMoon(
            [FromAmbientContext] TSExecutionContext context,
            [CommandLiteral("frost", "frostmoon")]
            [CommandParam(Name = "type")]
            string invasionType,
            [Int32Value(
                Name = "wave",
                Minimum = 1,
                InvalidTokenMessage = nameof(WaveInvalidTokenMessage2),
                OutOfRangeMessage = nameof(WaveOutOfRangeMessage2))]
            int wave = 1) =>
            RequireEventPermissions(
                context,
                "invasion",
                [Permissions.invade, Permissions.manageinvasionevent],
                ctx => StartFrostMoon(ctx, wave));

        [CommandAction("sandstorm", Summary = nameof(SandstormSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageevents), ServerScope = true)]
        public static CommandOutcome Sandstorm([FromAmbientContext] TSExecutionContext context) =>
            RequireEventPermissions(
                context,
                "sandstorm",
                [Permissions.sandstorm, Permissions.managesandstormevent],
                static ctx => SetSandstorm(ctx));

        [CommandAction("rain", Summary = nameof(RainSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageevents), ServerScope = true)]
        public static CommandOutcome Rain(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "type")]
            [CommandTokenEnum(UnrecognizedTokenBehavior = CommandTokenFallbackBehavior.UseDefaultWithoutConsuming)]
            RainVariant variant = RainVariant.Normal) =>
            RequireEventPermissions(
                context,
                "rain",
                [Permissions.rain, Permissions.managerainevent],
                ctx => SetRain(ctx, variant));

        [CommandAction("lanternsnight", Summary = nameof(LanternsNightSummary))]
        [CommandActionAlias("lanterns")]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageevents), ServerScope = true)]
        public static CommandOutcome LanternsNight([FromAmbientContext] TSExecutionContext context) =>
            RequireEventPermissions(
                context,
                "lanternsnight",
                [Permissions.managelanternsnightevent],
                static ctx => SetLanternsNight(ctx));

        [CommandAction("meteorshower", Summary = nameof(MeteorShowerSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageevents), ServerScope = true)]
        public static CommandOutcome MeteorShower([FromAmbientContext] TSExecutionContext context) =>
            RequireEventPermissions(
                context,
                "meteorshower",
                [Permissions.managemeteorshowerevent],
                static ctx => SetMeteorShower(ctx));

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() => BuildUsageOutcome();

        private static CommandOutcome RequireEventPermissions(
            TSExecutionContext context,
            string requestedEvent,
            IReadOnlyList<string> permissions,
            Func<TSExecutionContext, CommandOutcome> onAllowed) {
            if (permissions.Any(context.Executor.HasPermission)) {
                return onAllowed(context);
            }

            return CommandOutcome.Error(GetString("You do not have permission to start the {0} event.", requestedEvent));
        }

        private static CommandOutcome DropMeteor(TSExecutionContext context) {
            var server = context.Server!;
            var serverPlayer = WorldCommandHelpers.ResolveServerPlayer(server);

            server.WorldGen.spawnMeteor = false;
            server.WorldGen.dropMeteor();
            if (context.Silent) {
                return CommandOutcome.Info(GetString("A meteor has been triggered."));
            }

            serverPlayer.BCInfoMessage(GetString("{0} triggered a meteor.", context.Executor.Name));
            return CommandOutcome.Empty;
        }

        private static CommandOutcome StartFullMoon(TSExecutionContext context) {
            var server = context.Server!;
            var serverPlayer = WorldCommandHelpers.ResolveServerPlayer(server);

            serverPlayer.SetFullMoon();
            if (context.Silent) {
                return CommandOutcome.Info(GetString("Started a full moon event."));
            }

            serverPlayer.BCInfoMessage(GetString("{0} started a full moon event.", context.Executor.Name));
            return CommandOutcome.Empty;
        }

        private static CommandOutcome SetBloodMoon(TSExecutionContext context) {
            var server = context.Server!;
            var serverPlayer = WorldCommandHelpers.ResolveServerPlayer(server);

            serverPlayer.SetBloodMoon(!server.Main.bloodMoon);
            if (context.Silent) {
                return server.Main.bloodMoon
                    ? CommandOutcome.Info(GetString("Started a blood moon event."))
                    : CommandOutcome.Info(GetString("Stopped the current blood moon event."));
            }

            serverPlayer.BCInfoMessage(server.Main.bloodMoon
                ? GetString("{0} started a blood moon event.", context.Executor.Name)
                : GetString("{0} stopped the current blood moon.", context.Executor.Name));
            return CommandOutcome.Empty;
        }

        private static CommandOutcome SetEclipse(TSExecutionContext context) {
            var server = context.Server!;
            var serverPlayer = WorldCommandHelpers.ResolveServerPlayer(server);

            serverPlayer.SetEclipse(!server.Main.eclipse);
            if (context.Silent) {
                return server.Main.eclipse
                    ? CommandOutcome.Info(GetString("Started an eclipse."))
                    : CommandOutcome.Info(GetString("Stopped an eclipse."));
            }

            serverPlayer.BCInfoMessage(server.Main.eclipse
                ? GetString("{0} started an eclipse.", context.Executor.Name)
                : GetString("{0} stopped an eclipse.", context.Executor.Name));
            return CommandOutcome.Empty;
        }

        private static CommandOutcome LaunchStandardInvasion(TSExecutionContext context, StandardInvasionKind invasionType) {
            var server = context.Server!;
            var serverPlayer = WorldCommandHelpers.ResolveServerPlayer(server);

            switch (invasionType) {
                case StandardInvasionKind.Goblins:
                    serverPlayer.BCInfoMessage(GetString("{0} has started a goblin army invasion.", context.Executor.Name));
                    Utils.StartInvasion(server, 1);
                    return CommandOutcome.Empty;

                case StandardInvasionKind.Snowmen:
                    serverPlayer.BCInfoMessage(GetString("{0} has started a snow legion invasion.", context.Executor.Name));
                    Utils.StartInvasion(server, 2);
                    return CommandOutcome.Empty;

                case StandardInvasionKind.Pirates:
                    serverPlayer.BCInfoMessage(GetString("{0} has started a pirate invasion.", context.Executor.Name));
                    Utils.StartInvasion(server, 3);
                    return CommandOutcome.Empty;

                case StandardInvasionKind.Martians:
                    serverPlayer.BCInfoMessage(GetString("{0} has started a martian invasion.", context.Executor.Name));
                    Utils.StartInvasion(server, 4);
                    return CommandOutcome.Empty;

                default:
                    return CommandOutcome.Error(GetString("Invalid invasion type. Valid invasion types: {0}.", string.Join(", ", ValidInvasions)));
            }
        }

        private static CommandOutcome StartPumpkinMoon(TSExecutionContext context, int wave) {
            var server = context.Server!;
            var serverPlayer = WorldCommandHelpers.ResolveServerPlayer(server);

            serverPlayer.SetPumpkinMoon(true);
            server.Main.bloodMoon = false;
            server.NPC.waveKills = 0f;
            server.NPC.waveNumber = wave;
            serverPlayer.BCInfoMessage(GetString("{0} started the pumpkin moon at wave {1}!", context.Executor.Name, wave));
            return CommandOutcome.Empty;
        }

        private static CommandOutcome StartFrostMoon(TSExecutionContext context, int wave) {
            var server = context.Server!;
            var serverPlayer = WorldCommandHelpers.ResolveServerPlayer(server);

            serverPlayer.SetFrostMoon(true);
            server.Main.bloodMoon = false;
            server.NPC.waveKills = 0f;
            server.NPC.waveNumber = wave;
            serverPlayer.BCInfoMessage(GetString("{0} started the frost moon at wave {1}!", context.Executor.Name, wave));
            return CommandOutcome.Empty;
        }

        private static CommandOutcome StopInvasion(TSExecutionContext context) {
            var server = context.Server!;
            var serverPlayer = WorldCommandHelpers.ResolveServerPlayer(server);

            if (server.DD2Event.Ongoing) {
                server.DD2Event.StopInvasion();
                serverPlayer.BCInfoMessage(GetString("{0} has ended the Old One's Army event.", context.Executor.Name));
                return CommandOutcome.Empty;
            }

            serverPlayer.BCInfoMessage(GetString("{0} has ended the current invasion event.", context.Executor.Name));
            server.Main.invasionSize = 0;
            return CommandOutcome.Empty;
        }

        private static CommandOutcome SetSandstorm(TSExecutionContext context) {
            var server = context.Server!;
            var serverPlayer = WorldCommandHelpers.ResolveServerPlayer(server);

            if (server.Sandstorm.Happening) {
                server.Sandstorm.StopSandstorm();
                serverPlayer.BCInfoMessage(GetString("{0} stopped the current sandstorm event.", context.Executor.Name));
            }
            else {
                server.Sandstorm.StartSandstorm();
                serverPlayer.BCInfoMessage(GetString("{0} started a sandstorm event.", context.Executor.Name));
            }

            return CommandOutcome.Empty;
        }

        private static CommandOutcome SetRain(TSExecutionContext context, RainVariant variant) {
            var server = context.Server!;
            var serverPlayer = WorldCommandHelpers.ResolveServerPlayer(server);

            switch (variant) {
                case RainVariant.Slime:
                    if (server.Main.raining) {
                        return CommandOutcome.Error(GetString("Slime rain cannot be activated during normal rain. Stop the normal rainstorm and try again."));
                    }

                    if (server.Main.slimeRain) {
                        server.Main.StopSlimeRain(false);
                        WorldCommandHelpers.SendWorldInfo(server);
                        serverPlayer.BCInfoMessage(GetString("{0} ended the slime rain.", context.Executor.Name));
                    }
                    else {
                        server.Main.StartSlimeRain(false);
                        WorldCommandHelpers.SendWorldInfo(server);
                        serverPlayer.BCInfoMessage(GetString("{0} caused it to rain slime.", context.Executor.Name));
                    }

                    return CommandOutcome.Empty;

                case RainVariant.Coin:
                    if (server.Main.coinRain != 0) {
                        server.Main.StopRain();
                        WorldCommandHelpers.SendWorldInfo(server);
                        serverPlayer.BCInfoMessage(GetString("{0} ended the coin rain.", context.Executor.Name));
                    }
                    else {
                        server.Main.StartRain(garenteeCoinRain: true);
                        WorldCommandHelpers.SendWorldInfo(server);
                        serverPlayer.BCInfoMessage(GetString("{0} caused it to coin rain.", context.Executor.Name));
                    }

                    return CommandOutcome.Empty;

                default:
                    if (server.Main.raining) {
                        server.Main.StopRain();
                        WorldCommandHelpers.SendWorldInfo(server);
                        serverPlayer.BCInfoMessage(GetString("{0} ended the rain.", context.Executor.Name));
                    }
                    else {
                        server.Main.StartRain();
                        WorldCommandHelpers.SendWorldInfo(server);
                        serverPlayer.BCInfoMessage(GetString("{0} caused it to rain.", context.Executor.Name));
                    }

                    return BuildRainHintOutcome();
            }
        }

        private static CommandOutcome SetMeteorShower(TSExecutionContext context) {
            var server = context.Server!;
            var serverPlayer = WorldCommandHelpers.ResolveServerPlayer(server);

            if (server.WorldGen.meteorShowerCount > 0) {
                server.WorldGen.meteorShowerCount = 0;
                serverPlayer.BCInfoMessage(GetString("{0} stopped the meteor shower.", context.Executor.Name));
            }
            else {
                server.WorldGen.StartMeteorShower();
                serverPlayer.BCInfoMessage(GetString("{0} started a meteor shower.", context.Executor.Name));
            }

            return CommandOutcome.Empty;
        }

        private static CommandOutcome SetLanternsNight(TSExecutionContext context) {
            var server = context.Server!;
            var serverPlayer = WorldCommandHelpers.ResolveServerPlayer(server);

            server.LanternNight.ToggleManualLanterns();

            if (context.Silent) {
                return server.LanternNight.LanternsUp
                    ? CommandOutcome.Success(GetString("Lanterns are now up."))
                    : CommandOutcome.Success(GetString("Lanterns are now down."));
            }

            serverPlayer.BCInfoMessage(server.LanternNight.LanternsUp
                ? GetString("{0} started a lantern night.", context.Executor.Name)
                : GetString("{0} stopped the lantern night.", context.Executor.Name));
            return CommandOutcome.Empty;
        }

        private static CommandOutcome BuildInvasionUsageOutcome() {
            return CommandOutcome.ErrorBuilder(GetString("Invalid syntax. Proper syntax:  {0}worldevent invasion [invasion type] [invasion wave].", Commands.Specifier))
                .AddError(GetString("Valid invasion types: {0}.", string.Join(", ", ValidInvasions)))
                .Build();
        }

        private static CommandOutcome BuildRainHintOutcome() {
            return CommandOutcome.InfoBuilder(GetString("Use \"{0}worldevent rain slime\" to start slime rain!", Commands.Specifier))
                .AddInfo(GetString("Use \"{0}worldevent rain coin\" to start coin rain!", Commands.Specifier))
                .Build();
        }

        private static CommandOutcome BuildUsageOutcome() {
            return CommandOutcome.ErrorBuilder(GetString("Invalid syntax. Proper syntax: {0}worldevent <event type>.", Commands.Specifier))
                .AddError(GetString("Valid event types: {0}.", string.Join(", ", ValidEvents)))
                .AddError(GetString("Valid invasion types if spawning an invasion: {0}.", string.Join(", ", ValidInvasions)))
                .Build();
        }
    }

    [CommandController("worldinfo", Summary = nameof(ControllerSummary))]
    internal static class WorldInfoCommand
    {
        private static string ControllerSummary => GetString("Shows information about the current world.");
        private static string ExecuteSummary => GetString("Shows information about the current world.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.worldinfo), ServerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] ServerContext server) {
            return CommandOutcome.InfoLines([
                GetString("Information about the currently running world"),
                GetString("Name: {0}", server.Name),
                GetString("Size: {0}x{1}", server.Main.maxTilesX, server.Main.maxTilesY),
                GetString("ID: {0}", server.Main.worldID),
                GetString("Seed: {0}", server.Main.ActiveWorldFileData.Seed),
                GetString("Mode: {0}", server.Main.GameMode),
                GetString("Path: {0}", server.Main.worldPathName),
            ]);
        }

    }

    [CommandController("setspawn", Summary = nameof(ControllerSummary))]
    internal static class SetSpawnCommand
    {
        private static string ControllerSummary => GetString("Sets the world's spawn point to your location.");
        private static string ExecuteSummary => GetString("Sets the world's spawn point to your location.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.worldspawn), PlayerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            var tsPlayer = context.Player!;
            var server = tsPlayer.GetCurrentServer();
            server.Main.spawnTileX = tsPlayer.TileX + 1;
            server.Main.spawnTileY = tsPlayer.TileY + 3;
            SaveManager.SaveWorld(server, wait: false);
            return CommandOutcome.Success(GetString("Spawn has now been set at your location."));
        }

    }

    [CommandController("setdungeon", Summary = nameof(ControllerSummary))]
    internal static class SetDungeonCommand
    {
        private static string ControllerSummary => GetString("Sets the dungeon's position to your location.");
        private static string ExecuteSummary => GetString("Sets the dungeon's position to your location.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.dungeonposition), PlayerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            var tsPlayer = context.Player!;
            var server = tsPlayer.GetCurrentServer();
            server.Main.dungeonX = tsPlayer.TileX + 1;
            server.Main.dungeonY = tsPlayer.TileY + 3;
            SaveManager.SaveWorld(server, wait: false);
            return CommandOutcome.Success(GetString("The dungeon's position has now been set at your location."));
        }

    }

    [CommandController("protectspawn", Summary = nameof(ControllerSummary))]
    internal static class ProtectSpawnCommand
    {
        private static string ControllerSummary => GetString("Toggles spawn protection.");
        private static string ExecuteSummary => GetString("Toggles spawn protection.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.editspawn), ServerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] ServerContext server) {
            var settings = TShock.Config.GetServerSettings(server.Name);
            settings.SpawnProtection = !settings.SpawnProtection;
            TShock.Config.SaveToFile();

            WorldCommandHelpers.ResolveServerPlayer(server).BCSuccessMessage(
                settings.SpawnProtection
                    ? GetString("Spawn is now protected.")
                    : GetString("Spawn is now open."));
            return CommandOutcome.Empty;
        }

    }

    [CommandController("save", Summary = nameof(ControllerSummary))]
    internal static class SaveCommand
    {
        private static string ControllerSummary => GetString("Saves the world file.");
        private static string ExecuteSummary => GetString("Saves the world file.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.worldsave))]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            if (context.Server is not null) {
                SaveServer(context.Server);
                return CommandOutcome.Empty;
            }

            foreach (var server in UnifiedServerCoordinator.Servers) {
                if (server.IsRunning) {
                    SaveServer(server);
                }
            }

            return CommandOutcome.Empty;
        }

        private static void SaveServer(ServerContext server) {
            SaveManager.SaveWorld(server, wait: false);
            foreach (var tsPlayer in TShock.Players) {
                if (tsPlayer is null || tsPlayer.GetCurrentServer() != server) {
                    continue;
                }

                tsPlayer.SaveServerCharacter();
            }
        }
    }
}
