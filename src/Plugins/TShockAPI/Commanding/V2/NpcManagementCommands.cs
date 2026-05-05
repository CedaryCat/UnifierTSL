using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using TShockAPI.ConsolePrompting;
using UnifierTSL;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding;
using UnifierTSL.Servers;

namespace TShockAPI.Commanding.V2
{
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class SpawnBossPromptAttribute : CommandPromptAttribute
    {
        private static readonly string[] BossTypeCandidates = [
            "*", "all",
            "brain", "brain of cthulhu", "boc",
            "destroyer",
            "duke", "duke fishron", "fishron",
            "eater", "eater of worlds", "eow",
            "eye", "eye of cthulhu", "eoc",
            "golem",
            "king", "king slime", "ks",
            "plantera",
            "prime", "skeletron prime",
            "queen bee", "qb",
            "skeletron",
            "twins",
            "wof", "wall of flesh",
            "moon", "moon lord", "ml",
            "empress", "empress of light", "eol",
            "queen slime", "qs",
            "lunatic", "lunatic cultist", "cultist", "lc",
            "betsy",
            "flying", "flying dutchman", "dutchman",
            "mourning wood",
            "pumpking",
            "everscream",
            "santa-nk1", "santa",
            "ice queen",
            "martian", "martian saucer",
            "solar", "solar pillar",
            "nebula", "nebula pillar",
            "vortex", "vortex pillar",
            "stardust", "stardust pillar",
            "deerclops"
        ];

        public SpawnBossPromptAttribute() {
            SuggestionKindId = PromptSuggestionKindIds.Enum;
            EnumCandidates = [.. BossTypeCandidates];
            AcceptedSpecialTokens = ["*"];
        }
    }

    internal static class NpcCommandHelpers
    {
        public static TSServerPlayer ResolveServerPlayer(ServerContext server) {
            try {
                return server.GetExtension<TSServerPlayer>();
            }
            catch (IndexOutOfRangeException) {
                return new TSServerPlayer(server);
            }
        }

        public static void SetTime(ServerContext server, bool dayTime, double time) {
            server.Main.dayTime = dayTime;
            server.Main.time = time;
            if (!server.IsRunning) {
                return;
            }

            server.NetMessage.SendData((int)PacketTypes.TimeSet, -1, -1, null, dayTime ? 1 : 0, (int)time, server.Main.sunModY, server.Main.moonModY);
        }

        public static void SendNpcRename(ServerContext server, string newName, int npcIndex) {
            if (!server.IsRunning) {
                return;
            }

            server.NetMessage.SendData(56, -1, -1, NetworkText.FromLiteral(newName), npcIndex, 0f, 0f, 0f, 0);
        }

        public static void SendAnglerQuest(ServerContext server) {
            if (!server.IsRunning) {
                return;
            }

            server.NetMessage.SendAnglerQuest(-1);
        }

        public static void SpawnNpc(
            ServerContext server,
            TSServerPlayer serverPlayer,
            int type,
            string name,
            int amount,
            int startTileX,
            int startTileY,
            int tileXRange = 100,
            int tileYRange = 50) {
            if (!server.IsRunning) {
                return;
            }

            serverPlayer.SpawnNPC(type, name, amount, startTileX, startTileY, tileXRange, tileYRange);
        }
    }

    [CommandController("butcher", Summary = nameof(ControllerSummary))]
    internal static class ButcherCommand
    {
        private static string ControllerSummary => GetString("Kills hostile NPCs or NPCs of a certain type.");
        private static string ExecuteSummary => GetString("Kills hostile NPCs or active NPCs of a specific type.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.butcher), ServerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "npc")]
            [CommandPromptSemantic<TSCommandPromptParamKeys>(nameof(TSCommandPromptParamKeys.NpcRef))]
            string npcSearch = "") {
            if (string.IsNullOrWhiteSpace(npcSearch)) {
                return ExecuteCore(context, npcId: null);
            }

            var npcs = Utils.GetNPCByIdOrName(npcSearch);
            if (npcs.Count == 0) {
                return CommandOutcome.Error(GetString("\"{0}\" is not a valid server.NPC.", npcSearch));
            }

            if (npcs.Count > 1) {
                return CommandOutcome.MultipleMatches(npcs.Select(static npc => $"{npc.FullName}({npc.type})"));
            }

            return ExecuteCore(context, npcs[0].netID);
        }

        private static CommandOutcome ExecuteCore(TSExecutionContext context, int? npcId) {
            var server = context.Server!;
            var serverPlayer = NpcCommandHelpers.ResolveServerPlayer(server);

            var kills = 0;
            for (var i = 0; i < server.Main.npc.Length; i++) {
                if (server.Main.npc[i].active
                    && ((npcId is null && !server.Main.npc[i].townNPC && server.Main.npc[i].netID != NPCID.TargetDummy)
                        || server.Main.npc[i].netID == npcId)) {
                    serverPlayer.StrikeNPC(i, (int)(server.Main.npc[i].life + (server.Main.npc[i].defense * 0.6)), 0, 0);
                    kills++;
                }
            }

            if (context.Silent) {
                return CommandOutcome.Success(GetPluralString("You butchered {0} server.NPC.", "You butchered {0} NPCs.", kills, kills));
            }

            serverPlayer.BCInfoMessage(GetPluralString("{0} butchered {1} server.NPC.", "{0} butchered {1} NPCs.", kills, context.Executor.Name, kills));
            return CommandOutcome.Empty;
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.InfoLines([
                GetString("Butcher Syntax and Example"),
                GetString($"{"butcher".Color(Utils.BoldHighlight)} [{"NPC name".Color(Utils.RedHighlight)}|{"ID".Color(Utils.RedHighlight)}]"),
                GetString($"Example usage: {"butcher".Color(Utils.BoldHighlight)} {"pigron".Color(Utils.RedHighlight)}"),
                GetString("All alive NPCs (excluding town NPCs) on the server will be killed if you do not input a name or ID."),
                GetString($"To get rid of NPCs without making them drop items, use the {"clear".Color(Utils.BoldHighlight)} command instead."),
                GetString($"To execute this command silently, use {Commands.SilentSpecifier.Color(Utils.GreenHighlight)} instead of {Commands.Specifier.Color(Utils.RedHighlight)}"),
            ]);
        }
    }

    [CommandController("renamenpc", Summary = nameof(ControllerSummary))]
    internal static class RenameNpcCommand
    {
        private static string NpcIdInvalidNpcMessage => GetString("Invalid mob type!");

        private static string ControllerSummary => GetString("Renames an NPC.");
        private static string ExecuteSummary => GetString("Renames active town NPCs of the specified type.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.renamenpc), ServerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [NpcRef(nameof(NpcIdInvalidNpcMessage), Name = "npc")] int npcId,
            [CommandParam(Name = "newname")]
            string newName) {
            if (newName.Length > 200) {
                return CommandOutcome.Error(GetString("New name is too large!"));
            }

            var server = context.Server!;
            var serverPlayer = NpcCommandHelpers.ResolveServerPlayer(server);
            var requestedNpc = Utils.GetNPCById(npcId).FullName;

            var renamedCount = 0;
            for (var i = 0; i < server.Main.npc.Length; i++) {
                if (server.Main.npc[i].active && server.Main.npc[i].netID == npcId && server.Main.npc[i].townNPC) {
                    server.Main.npc[i].GivenName = newName;
                    NpcCommandHelpers.SendNpcRename(server, newName, i);
                    renamedCount++;
                }
            }

            if (renamedCount > 0) {
                serverPlayer.BCInfoMessage(GetString("{0} renamed the {1}.", context.Executor.Name, requestedNpc));
                return CommandOutcome.Empty;
            }

            return CommandOutcome.Error(GetString("Could not rename {0}!", requestedNpc));
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}renameNPC <guide, nurse, etc.> <newname>", Commands.Specifier));
        }
    }

    [CommandController("maxspawns", Summary = nameof(ControllerSummary))]
    internal static class MaxSpawnsCommand
    {
        private static string ControllerSummary => GetString("Sets the maximum number of NPCs.");
        private static string ExecuteSummary => GetString("Shows or updates the server maximum spawn count.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.maxspawns))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "max-or-default")]
            string? maxOrDefault = null) {
            var builder = CommandOutcome.CreateBuilder();
            var wroteReceipts = false;
            var token = maxOrDefault ?? string.Empty;
            var noUserArgument = maxOrDefault is null;

            if (context.Server is not null) {
                wroteReceipts |= Apply(context, context.Server, token, noUserArgument, builder);
            }
            else {
                foreach (var server in UnifiedServerCoordinator.Servers.Where(static server => server.IsRunning)) {
                    wroteReceipts |= Apply(context, server, token, noUserArgument, builder);
                }
            }

            return wroteReceipts ? builder.Build() : CommandOutcome.Empty;
        }

        private static bool Apply(
            TSExecutionContext context,
            ServerContext server,
            string maxOrDefault,
            bool noUserArgument,
            CommandOutcome.Builder builder) {
            var settings = TShock.Config.GetServerSettings(server.Name);
            var serverPlayer = NpcCommandHelpers.ResolveServerPlayer(server);

            if (noUserArgument) {
                builder.AddInfo(GetString("Current maximum spawns: {0}.", settings.DefaultMaximumSpawns));
                return true;
            }

            if (string.Equals(maxOrDefault, "default", StringComparison.CurrentCultureIgnoreCase)) {
                settings.DefaultMaximumSpawns = server.NPC.defaultMaxSpawns = 5;
                if (context.Silent) {
                    builder.AddInfo(GetString("Changed the maximum spawns to 5."));
                    return true;
                }

                serverPlayer.BCInfoMessage(GetString("{0} changed the maximum spawns to 5.", context.Executor.Name));
                return false;
            }

            if (!int.TryParse(maxOrDefault, out var maxSpawns) || maxSpawns < 0 || maxSpawns > Main.maxNPCs) {
                builder.AddWarning(GetString("Invalid maximum spawns.  Acceptable range is {0} to {1}.", 0, Main.maxNPCs));
                return true;
            }

            settings.DefaultMaximumSpawns = server.NPC.defaultMaxSpawns = maxSpawns;
            if (context.Silent) {
                builder.AddInfo(GetString("Changed the maximum spawns to {0}.", maxSpawns));
                return true;
            }

            serverPlayer.BCInfoMessage(GetString("{0} changed the maximum spawns to {1}.", context.Executor.Name, maxSpawns));
            return false;
        }
    }

    [CommandController("spawnboss", Summary = nameof(ControllerSummary))]
    [Aliases("sb")]
    internal static class SpawnBossCommand
    {
        private static string ControllerSummary => GetString("Spawns a number of bosses around you.");
        private static string ExecuteSummary => GetString("Spawns a supported boss at your location.");
        private static string ExecuteWithAmountSummary => GetString("Spawns a supported boss at your location.");
        private static string AmountInvalidTokenMessage => GetString("Invalid boss amount.");
        private static string AmountOutOfRangeMessage => GetString("Invalid boss amount.");

        private static readonly int[] AllBossNpcIds = [4, 13, 35, 50, 125, 126, 127, 134, 222, 245, 262, 266, 370, 398, 439, 636, 657];

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.spawnboss), PlayerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "boss")]
            [SpawnBossPrompt]
            string bossType) {
            return ExecuteCore(context, bossType, 1);
        }

        [CommandAction(Summary = nameof(ExecuteWithAmountSummary))]
        [TShockCommand(nameof(Permissions.spawnboss), PlayerScope = true)]
        public static CommandOutcome ExecuteWithAmount(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "boss")]
            [SpawnBossPrompt]
            string bossType,
            [Int32Value(
                Minimum = 1,
                InvalidTokenMessage = nameof(AmountInvalidTokenMessage),
                OutOfRangeMessage = nameof(AmountOutOfRangeMessage),
                Name = "amount")]
            int amount) {
            return ExecuteCore(context, bossType, amount);
        }

        private static CommandOutcome ExecuteCore(TSExecutionContext context, string bossType, int amount) {
            var server = context.Server!;
            var serverPlayer = NpcCommandHelpers.ResolveServerPlayer(server);
            var player = context.Player!;

            var failure = TrySpawnBoss(server, serverPlayer, player, bossType, amount, out var spawnName);
            if (failure is not null) {
                return failure;
            }

            if (context.Silent) {
                return CommandOutcome.Success(GetPluralString("You spawned {0} {1} time.", "You spawned {0} {1} times.", amount, spawnName, amount));
            }

            serverPlayer.BCSuccessMessage(GetPluralString("{0} spawned {1} {2} time.", "{0} spawned {1} {2} times.", amount, context.Executor.Name, spawnName, amount));
            return CommandOutcome.Empty;
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}spawnboss <boss type> [amount].", Commands.Specifier));
        }

        private static CommandOutcome? TrySpawnBoss(
            ServerContext server,
            TSServerPlayer serverPlayer,
            TSPlayer player,
            string bossType,
            int amount,
            out string spawnName) {
            spawnName = string.Empty;
            NPC npc = new();

            string SpawnSingleBoss(int npcId, string name, bool setNight = false) {
                if (setNight) {
                    NpcCommandHelpers.SetTime(server, dayTime: false, time: 0.0);
                }

                npc.SetDefaults(server, npcId);
                NpcCommandHelpers.SpawnNpc(server, serverPlayer, npc.type, npc.FullName, amount, player.TileX, player.TileY);
                return name;
            }

            switch (bossType.Trim().ToLowerInvariant()) {
                case "*":
                case "all":
                    NpcCommandHelpers.SetTime(server, dayTime: false, time: 0.0);
                    foreach (var npcId in AllBossNpcIds) {
                        npc.SetDefaults(server, npcId);
                        NpcCommandHelpers.SpawnNpc(server, serverPlayer, npc.type, npc.FullName, amount, player.TileX, player.TileY);
                    }

                    spawnName = GetString("all bosses");
                    return null;

                case "brain":
                case "brain of cthulhu":
                case "boc":
                    spawnName = SpawnSingleBoss(266, GetString("the Brain of Cthulhu"));
                    return null;

                case "destroyer":
                    spawnName = SpawnSingleBoss(134, GetString("the Destroyer"), setNight: true);
                    return null;

                case "duke":
                case "duke fishron":
                case "fishron":
                    spawnName = SpawnSingleBoss(370, GetString("Duke Fishron"));
                    return null;

                case "eater":
                case "eater of worlds":
                case "eow":
                    spawnName = SpawnSingleBoss(13, GetString("the Eater of Worlds"));
                    return null;

                case "eye":
                case "eye of cthulhu":
                case "eoc":
                    spawnName = SpawnSingleBoss(4, GetString("the Eye of Cthulhu"), setNight: true);
                    return null;

                case "golem":
                    spawnName = SpawnSingleBoss(245, GetString("the Golem"));
                    return null;

                case "king":
                case "king slime":
                case "ks":
                    spawnName = SpawnSingleBoss(50, GetString("the King Slime"));
                    return null;

                case "plantera":
                    spawnName = SpawnSingleBoss(262, GetString("Plantera"));
                    return null;

                case "prime":
                case "skeletron prime":
                    spawnName = SpawnSingleBoss(127, GetString("Skeletron Prime"), setNight: true);
                    return null;

                case "queen bee":
                case "qb":
                    spawnName = SpawnSingleBoss(222, GetString("the Queen Bee"));
                    return null;

                case "skeletron":
                    spawnName = SpawnSingleBoss(35, GetString("Skeletron"), setNight: true);
                    return null;

                case "twins":
                    NpcCommandHelpers.SetTime(server, dayTime: false, time: 0.0);
                    npc.SetDefaults(server, 125);
                    NpcCommandHelpers.SpawnNpc(server, serverPlayer, npc.type, npc.FullName, amount, player.TileX, player.TileY);
                    npc.SetDefaults(server, 126);
                    NpcCommandHelpers.SpawnNpc(server, serverPlayer, npc.type, npc.FullName, amount, player.TileX, player.TileY);
                    spawnName = GetString("the Twins");
                    return null;

                case "wof":
                case "wall of flesh":
                    if (server.Main.wofNPCIndex != -1) {
                        return CommandOutcome.Error(GetString("There is already a Wall of Flesh."));
                    }

                    if (player.Y / 16f < server.Main.maxTilesY - 205) {
                        return CommandOutcome.Error(GetString("You must spawn the Wall of Flesh in hell."));
                    }

                    server.NPC.SpawnWOF(new Vector2(player.X, player.Y));
                    spawnName = GetString("the Wall of Flesh");
                    return null;

                case "moon":
                case "moon lord":
                case "ml":
                    spawnName = SpawnSingleBoss(398, GetString("the Moon Lord"));
                    return null;

                case "empress":
                case "empress of light":
                case "eol":
                    spawnName = SpawnSingleBoss(636, GetString("the Empress of Light"));
                    return null;

                case "queen slime":
                case "qs":
                    spawnName = SpawnSingleBoss(657, GetString("the Queen Slime"));
                    return null;

                case "lunatic":
                case "lunatic cultist":
                case "cultist":
                case "lc":
                    spawnName = SpawnSingleBoss(439, GetString("the Lunatic Cultist"));
                    return null;

                case "betsy":
                    spawnName = SpawnSingleBoss(551, GetString("Betsy"));
                    return null;

                case "flying":
                case "flying dutchman":
                case "dutchman":
                    spawnName = SpawnSingleBoss(491, GetString("the Flying Dutchman"));
                    return null;

                case "mourning wood":
                    spawnName = SpawnSingleBoss(325, GetString("Mourning Wood"));
                    return null;

                case "pumpking":
                    spawnName = SpawnSingleBoss(327, GetString("the Pumpking"));
                    return null;

                case "everscream":
                    spawnName = SpawnSingleBoss(344, GetString("Everscream"));
                    return null;

                case "santa-nk1":
                case "santa":
                    spawnName = SpawnSingleBoss(346, GetString("Santa-NK1"));
                    return null;

                case "ice queen":
                    spawnName = SpawnSingleBoss(345, GetString("the Ice Queen"));
                    return null;

                case "martian":
                case "martian saucer":
                    spawnName = SpawnSingleBoss(392, GetString("a Martian Saucer"));
                    return null;

                case "solar":
                case "solar pillar":
                    spawnName = SpawnSingleBoss(517, GetString("a Solar Pillar"));
                    return null;

                case "nebula":
                case "nebula pillar":
                    spawnName = SpawnSingleBoss(507, GetString("a Nebula Pillar"));
                    return null;

                case "vortex":
                case "vortex pillar":
                    spawnName = SpawnSingleBoss(422, GetString("a Vortex Pillar"));
                    return null;

                case "stardust":
                case "stardust pillar":
                    spawnName = SpawnSingleBoss(493, GetString("a Stardust Pillar"));
                    return null;

                case "deerclops":
                    spawnName = SpawnSingleBoss(668, GetString("a Deerclops"));
                    return null;

                default:
                    return CommandOutcome.Error(GetString("Invalid boss type!"));
            }
        }
    }

    [CommandController("spawnmob", Summary = nameof(ControllerSummary))]
    [Aliases("sm")]
    internal static class SpawnMobCommand
    {
        private static string NpcIdInvalidNpcMessage => GetString("Invalid mob type!");

        private static string ControllerSummary => GetString("Spawns a number of mobs around you.");
        private static string ExecuteSummary => GetString("Spawns mobs around your current location.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.spawnmob), PlayerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [NpcRef(nameof(NpcIdInvalidNpcMessage), Name = "mob")] int npcId,
            [CommandParam(Name = "amount")]
            string amountRaw = "") {
            var amount = 1;
            if (!string.IsNullOrWhiteSpace(amountRaw) && !int.TryParse(amountRaw, out amount)) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}spawnmob <mob type> [amount].", Commands.Specifier));
            }

            var server = context.Server!;
            var serverPlayer = NpcCommandHelpers.ResolveServerPlayer(server);
            var player = context.Player!;
            amount = Math.Min(amount, Main.maxNPCs);

            var npc = Utils.GetNPCById(npcId);
            if (npc.type >= 1 && npc.type < NPCID.Count && npc.type != 113) {
                NpcCommandHelpers.SpawnNpc(server, serverPlayer, npc.netID, npc.FullName, amount, player.TileX, player.TileY, 50, 20);
                if (context.Silent) {
                    return CommandOutcome.Success(GetPluralString("Spawned {0} {1} time.", "Spawned {0} {1} times.", amount, npc.FullName, amount));
                }

                serverPlayer.BCSuccessMessage(GetPluralString("{0} has spawned {1} {2} time.", "{0} has spawned {1} {2} times.", amount, context.Executor.Name, npc.FullName, amount));
                return CommandOutcome.Empty;
            }

            if (npc.type == 113) {
                if (server.Main.wofNPCIndex != -1 || (player.Y / 16f < server.Main.maxTilesY - 205)) {
                    return CommandOutcome.Error(GetString("Unable to spawn a Wall of Flesh based on its current state or your current location."));
                }

                server.NPC.SpawnWOF(new Vector2(player.X, player.Y));
                if (context.Silent) {
                    return CommandOutcome.Success(GetString("Spawned a Wall of Flesh."));
                }

                serverPlayer.BCSuccessMessage(GetString("{0} has spawned a Wall of Flesh.", context.Executor.Name));
                return CommandOutcome.Empty;
            }

            return CommandOutcome.Error(GetString("Invalid mob type."));
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}spawnmob <mob type> [amount].", Commands.Specifier));
        }
    }

    [CommandController("spawnrate", Summary = nameof(ControllerSummary))]
    internal static class SpawnRateCommand
    {
        private static string ControllerSummary => GetString("Sets the spawn rate of NPCs.");
        private static string ExecuteSummary => GetString("Shows or updates the NPC spawn rate.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.spawnrate))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "rate-or-default")]
            string? rateOrDefault = null) {
            var builder = CommandOutcome.CreateBuilder();
            var wroteReceipts = false;
            var token = rateOrDefault ?? string.Empty;
            var noUserArgument = rateOrDefault is null;

            if (context.Server is not null) {
                wroteReceipts |= Apply(context, context.Server, token, noUserArgument, builder);
            }
            else {
                foreach (var server in UnifiedServerCoordinator.Servers.Where(static server => server.IsRunning)) {
                    wroteReceipts |= Apply(context, server, token, noUserArgument, builder);
                }
            }

            return wroteReceipts ? builder.Build() : CommandOutcome.Empty;
        }

        private static bool Apply(
            TSExecutionContext context,
            ServerContext server,
            string rateOrDefault,
            bool noUserArgument,
            CommandOutcome.Builder builder) {
            var settings = TShock.Config.GetServerSettings(server.Name);
            var serverPlayer = NpcCommandHelpers.ResolveServerPlayer(server);

            if (noUserArgument) {
                builder.AddInfo(GetString("Current spawn rate: {0}.", settings.DefaultSpawnRate));
                return true;
            }

            if (string.Equals(rateOrDefault, "default", StringComparison.CurrentCultureIgnoreCase)) {
                settings.DefaultSpawnRate = server.NPC.defaultSpawnRate = 600;
                if (context.Silent) {
                    builder.AddInfo(GetString("Changed the spawn rate to 600."));
                    return true;
                }

                serverPlayer.BCInfoMessage(GetString("{0} changed the spawn rate to 600.", context.Executor.Name));
                return false;
            }

            if (!int.TryParse(rateOrDefault, out var spawnRate) || spawnRate < 0) {
                builder.AddWarning(GetString("The spawn rate you provided is out-of-range or not a number."));
                return true;
            }

            settings.DefaultSpawnRate = server.NPC.defaultSpawnRate = spawnRate;
            if (context.Silent) {
                builder.AddInfo(GetString("Changed the spawn rate to {0}.", spawnRate));
                return true;
            }

            serverPlayer.BCInfoMessage(GetString("{0} changed the spawn rate to {1}.", context.Executor.Name, spawnRate));
            return false;
        }
    }

    [CommandController("clearangler", Summary = nameof(ControllerSummary))]
    internal static class ClearAnglerCommand
    {
        private static string ControllerSummary => GetString("Resets the list of users who have completed an angler quest that day.");
        private static string ExecuteSummary => GetString("Clears all angler completions or only one player's entry.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.clearangler), ServerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] ServerContext server,
            [CommandParam(Name = "player")]
            string? playerName = null) {
            var playerSearch = playerName ?? string.Empty;
            if (playerName is not null) {
                var removed = server.Main.anglerWhoFinishedToday.RemoveAll(name =>
                    name.Equals(playerSearch, StringComparison.OrdinalIgnoreCase));
                if (removed <= 0) {
                    return CommandOutcome.Error(GetString("Failed to find any users by that name on the list."));
                }

                foreach (var player in TShock.Players.Where(player =>
                    player is not null && player.Active && player.Name.Equals(playerSearch, StringComparison.OrdinalIgnoreCase))) {
                    player.SendData(PacketTypes.AnglerQuest, "");
                }

                return CommandOutcome.Success(GetString("Removed {0} players from the angler quest completion list for today.", removed));
            }

            server.Main.anglerWhoFinishedToday.Clear();
            NpcCommandHelpers.SendAnglerQuest(server);
            return CommandOutcome.Success(GetString("Cleared all users from the angler quest completion list for today."));
        }
    }
}
