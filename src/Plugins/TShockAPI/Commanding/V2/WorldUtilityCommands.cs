using Terraria;
using Terraria.ID;
using UnifierTSL;
using UnifierTSL.Commanding;
using UnifierTSL.Servers;

namespace TShockAPI.Commanding.V2
{
    internal enum ClearTargetKind : byte
    {
        [CommandToken("item", "items", "i")]
        Item,

        [CommandToken("npc", "npcs", "n")]
        Npc,

        [CommandToken("proj", "projectile", "projectiles", "p")]
        Projectile,
    }

    internal enum GrowKind : byte
    {
        [CommandToken("basic")]
        Basic,

        [CommandToken("sakura")]
        Sakura,

        [CommandToken("willow")]
        Willow,

        [CommandToken("boreal")]
        Boreal,

        [CommandToken("mahogany")]
        Mahogany,

        [CommandToken("ebonwood")]
        Ebonwood,

        [CommandToken("shadewood")]
        Shadewood,

        [CommandToken("pearlwood")]
        Pearlwood,

        [CommandToken("palm")]
        Palm,

        [CommandToken("corruptpalm")]
        CorruptPalm,

        [CommandToken("crimsonpalm")]
        CrimsonPalm,

        [CommandToken("hallowpalm")]
        HallowPalm,

        [CommandToken("topaz")]
        Topaz,

        [CommandToken("amethyst")]
        Amethyst,

        [CommandToken("sapphire")]
        Sapphire,

        [CommandToken("emerald")]
        Emerald,

        [CommandToken("ruby")]
        Ruby,

        [CommandToken("diamond")]
        Diamond,

        [CommandToken("amber")]
        Amber,

        [CommandToken("cactus")]
        Cactus,

        [CommandToken("herb")]
        Herb,

        [CommandToken("mushroom")]
        Mushroom,
    }

    internal static class WorldUtilityCommandHelpers
    {
        public static CommandOutcome ClearNearby(
            TSExecutionContext context,
            ServerContext server,
            ClearTargetKind kind,
            int radius) {
            var actor = context.Executor.Player;
            var serverPlayer = WorldCommandHelpers.ResolveServerPlayer(server);
            var cleared = kind switch {
                ClearTargetKind.Item => ClearItems(server, actor, radius),
                ClearTargetKind.Npc => ClearNpcs(server, actor, radius),
                ClearTargetKind.Projectile => ClearProjectiles(server, actor, radius),
                _ => 0,
            };

            return kind switch {
                ClearTargetKind.Item => BuildClearOutcome(
                    context,
                    serverPlayer,
                    cleared,
                    radius,
                    singularSelf: "You deleted {0} item within a radius of {1}.",
                    pluralSelf: "You deleted {0} items within a radius of {1}.",
                    singularBroadcast: "{0} deleted {1} item within a radius of {2}.",
                    pluralBroadcast: "{0} deleted {1} items within a radius of {2}."),
                ClearTargetKind.Npc => BuildClearOutcome(
                    context,
                    serverPlayer,
                    cleared,
                    radius,
                    singularSelf: "You deleted {0} NPC within a radius of {1}.",
                    pluralSelf: "You deleted {0} NPCs within a radius of {1}.",
                    singularBroadcast: "{0} deleted {1} NPC within a radius of {2}.",
                    pluralBroadcast: "{0} deleted {1} NPCs within a radius of {2}."),
                _ => BuildClearOutcome(
                    context,
                    serverPlayer,
                    cleared,
                    radius,
                    singularSelf: "You deleted {0} projectile within a radius of {1}.",
                    pluralSelf: "You deleted {0} projectiles within a radius of {1}.",
                    singularBroadcast: "{0} deleted {1} projectile within a radius of {2}.",
                    pluralBroadcast: "{0} deleted {1} projectiles within a radius of {2}."),
            };
        }

        public static bool ToggleAntiBuild(ServerContext server) {
            var serverPlayer = WorldCommandHelpers.ResolveServerPlayer(server);
            var settings = TShock.Config.GetServerSettings(server.Name);
            settings.DisableBuild = !settings.DisableBuild;
            serverPlayer.BCSuccessMessage(settings.DisableBuild
                ? GetString("Anti-build is now on.")
                : GetString("Anti-build is now off."));
            return settings.DisableBuild;
        }

        public static string GetWorldEvil(ServerContext server) {
            return server.WorldGen.crimson ? "crimson" : "corruption";
        }

        public static CommandOutcome BuildGrowHelp(int pageNumber) {
            var lines = CreateGrowHelpLines();
            return CommandOutcome.Page(
                pageNumber,
                lines,
                lines.Count,
                CreateGrowHelpPageSettings());
        }

        public static List<string> CreateGrowHelpLines() {
            return [
                GetString("- Default trees :"),
                GetString("     'basic', 'sakura', 'willow', 'boreal', 'mahogany', 'ebonwood', 'shadewood', 'pearlwood'."),
                GetString("- Palm trees :"),
                GetString("     'palm', 'corruptpalm', 'crimsonpalm', 'hallowpalm'."),
                GetString("- Gem trees :"),
                GetString("     'topaz', 'amethyst', 'sapphire', 'emerald', 'ruby', 'diamond', 'amber'."),
                GetString("- Misc :"),
                GetString("     'cactus', 'herb', 'mushroom'."),
            ];
        }

        public static PaginationTools.Settings CreateGrowHelpPageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Trees types & misc available to use. ({0}/{1}):"),
                FooterFormat = GetString("Type {0}grow help {{0}} for more sub-commands.", Commands.Specifier),
            };
        }

        public static CommandOutcome GrowAtLocation(TSExecutionContext context, GrowKind kind) {
            var tsPlayer = context.Player!;
            var server = context.Server!;
            var x = tsPlayer.TileX;
            var y = tsPlayer.TileY + 3;
            if (!TShock.Regions.CanBuild(server, x, y, tsPlayer)) {
                return CommandOutcome.Error(GetString("You're not allowed to change tiles here!"));
            }

            var canGrowEvil = context.Executor.HasPermission(Permissions.growevil);
            var grownName = "Fail";

            bool RejectCannotGrowEvil() {
                return canGrowEvil;
            }

            bool PrepareAreaForGrow(ushort groundType = TileID.Grass, bool evil = false) {
                if (evil && !RejectCannotGrowEvil()) {
                    return false;
                }

                for (var i = x - 2; i < x + 3; i++) {
                    server.Main.tile[i, y].active(true);
                    server.Main.tile[i, y].type = groundType;
                    server.Main.tile[i, y].wall = WallID.None;
                }

                server.Main.tile[x, y - 1].wall = WallID.None;
                return true;
            }

            bool GrowTree(ushort groundType, string displayName, bool evil = false) {
                if (!PrepareAreaForGrow(groundType, evil)) {
                    return false;
                }

                server.WorldGen.GrowTree(x, y);
                grownName = displayName;
                return true;
            }

            bool GrowTreeByType(ushort groundType, string displayName, ushort typeToPrepare = TileID.Grass, bool evil = false) {
                if (!PrepareAreaForGrow(typeToPrepare, evil)) {
                    return false;
                }

                server.WorldGen.TryGrowingTreeByType(groundType, x, y);
                grownName = displayName;
                return true;
            }

            bool GrowPalmTree(ushort sandType, ushort supportingType, string displayName, bool evil = false) {
                if (evil && !RejectCannotGrowEvil()) {
                    return false;
                }

                for (var i = x - 2; i < x + 3; i++) {
                    server.Main.tile[i, y].active(true);
                    server.Main.tile[i, y].type = sandType;
                    server.Main.tile[i, y].wall = WallID.None;
                    server.Main.tile[i, y + 1].active(true);
                    server.Main.tile[i, y + 1].type = supportingType;
                    server.Main.tile[i, y + 1].wall = WallID.None;
                }

                server.Main.tile[x, y - 1].wall = WallID.None;
                server.WorldGen.GrowPalmTree(x, y);
                grownName = displayName;
                return true;
            }

            var grew = kind switch {
                GrowKind.Basic => GrowTree(TileID.Grass, GetString("Basic Tree")),
                GrowKind.Boreal => GrowTree(TileID.SnowBlock, GetString("Boreal Tree")),
                GrowKind.Mahogany => GrowTree(TileID.JungleGrass, GetString("Rich Mahogany")),
                GrowKind.Sakura => GrowTreeByType(TileID.VanityTreeSakura, GetString("Sakura Tree")),
                GrowKind.Willow => GrowTreeByType(TileID.VanityTreeYellowWillow, GetString("Willow Tree")),
                GrowKind.Shadewood => GrowTree(TileID.CrimsonGrass, GetString("Shadewood Tree"), evil: true),
                GrowKind.Ebonwood => GrowTree(TileID.CorruptGrass, GetString("Ebonwood Tree"), evil: true),
                GrowKind.Pearlwood => GrowTree(TileID.HallowedGrass, GetString("Pearlwood Tree"), evil: true),
                GrowKind.Palm => GrowPalmTree(TileID.Sand, TileID.HardenedSand, GetString("Desert Palm")),
                GrowKind.HallowPalm => GrowPalmTree(TileID.Pearlsand, TileID.HallowHardenedSand, GetString("Hallow Palm"), evil: true),
                GrowKind.CrimsonPalm => GrowPalmTree(TileID.Crimsand, TileID.CrimsonHardenedSand, GetString("Crimson Palm"), evil: true),
                GrowKind.CorruptPalm => GrowPalmTree(TileID.Ebonsand, TileID.CorruptHardenedSand, GetString("Corruption Palm"), evil: true),
                GrowKind.Topaz => GrowTreeByType(TileID.TreeTopaz, GetString("Topaz Gemtree"), typeToPrepare: 1),
                GrowKind.Amethyst => GrowTreeByType(TileID.TreeAmethyst, GetString("Amethyst Gemtree"), typeToPrepare: 1),
                GrowKind.Sapphire => GrowTreeByType(TileID.TreeSapphire, GetString("Sapphire Gemtree"), typeToPrepare: 1),
                GrowKind.Emerald => GrowTreeByType(TileID.TreeEmerald, GetString("Emerald Gemtree"), typeToPrepare: 1),
                GrowKind.Ruby => GrowTreeByType(TileID.TreeRuby, GetString("Ruby Gemtree"), typeToPrepare: 1),
                GrowKind.Diamond => GrowTreeByType(TileID.TreeDiamond, GetString("Diamond Gemtree"), typeToPrepare: 1),
                GrowKind.Amber => GrowTreeByType(TileID.TreeAmber, GetString("Amber Gemtree"), typeToPrepare: 1),
                GrowKind.Cactus => GrowCactus(),
                GrowKind.Herb => GrowHerb(),
                GrowKind.Mushroom => GrowMushroom(),
                _ => false,
            };

            if (!grew) {
                return CommandOutcome.Error(GetString("You do not have permission to grow this tree type"));
            }

            tsPlayer.SendTileSquareCentered(x - 2, y - 20, 25);
            return CommandOutcome.Success(GetString("Tried to grow a {0}.", grownName));

            bool GrowCactus() {
                server.Main.tile[x, y].type = TileID.Sand;
                server.WorldGen.GrowCactus(x, y);
                grownName = GetString("Cactus");
                return true;
            }

            bool GrowHerb() {
                server.Main.tile[x, y].active(true);
                server.Main.tile[x, y].frameX = 36;
                server.Main.tile[x, y].type = TileID.MatureHerbs;
                server.WorldGen.GrowAlch(x, y);
                grownName = GetString("Herb");
                return true;
            }

            bool GrowMushroom() {
                if (!PrepareAreaForGrow(TileID.MushroomGrass)) {
                    return false;
                }

                server.WorldGen.GrowShroom(x, y);
                grownName = GetString("Glowing Mushroom Tree");
                return true;
            }
        }

        private static int ClearItems(ServerContext server, TSPlayer actor, int radius) {
            var cleared = 0;
            for (var i = 0; i < Main.maxItems; i++) {
                var dX = server.Main.item[i].position.X - actor.X;
                var dY = server.Main.item[i].position.Y - actor.Y;
                if (!server.Main.item[i].active || dX * dX + dY * dY > radius * radius * 256f) {
                    continue;
                }

                server.Main.item[i].TurnToAir(server);
                server.NetMessage.SendData((int)PacketTypes.SyncItemDespawn, -1, -1, null, i);
                cleared++;
            }

            return cleared;
        }

        private static int ClearNpcs(ServerContext server, TSPlayer actor, int radius) {
            var cleared = 0;
            for (var i = 0; i < Main.maxNPCs; i++) {
                var dX = server.Main.npc[i].position.X - actor.X;
                var dY = server.Main.npc[i].position.Y - actor.Y;
                if (!server.Main.npc[i].active || dX * dX + dY * dY > radius * radius * 256f) {
                    continue;
                }

                server.Main.npc[i].active = false;
                server.Main.npc[i].type = 0;
                server.NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, i);
                cleared++;
            }

            return cleared;
        }

        private static int ClearProjectiles(ServerContext server, TSPlayer actor, int radius) {
            var cleared = 0;
            for (var i = 0; i < Main.maxProjectiles; i++) {
                var proj = server.Main.projectile[i];
                var dX = proj.position.X - actor.X;
                var dY = proj.position.Y - actor.Y;
                if (!proj.active || dX * dX + dY * dY > radius * radius * 256f) {
                    continue;
                }

                proj.active = false;
                proj.type = 0;
                server.NetMessage.SendData((int)PacketTypes.KillProjectile, -1, -1, null, proj.identity, proj.owner);
                cleared++;
            }

            return cleared;
        }

        private static CommandOutcome BuildClearOutcome(
            TSExecutionContext context,
            TSServerPlayer serverPlayer,
            int cleared,
            int radius,
            string singularSelf,
            string pluralSelf,
            string singularBroadcast,
            string pluralBroadcast) {
            if (context.Silent) {
                return CommandOutcome.Success(GetPluralString(singularSelf, pluralSelf, cleared, cleared, radius));
            }

            serverPlayer.BCInfoMessage(GetPluralString(singularBroadcast, pluralBroadcast, cleared, context.Executor.Name, cleared, radius));
            return CommandOutcome.Empty;
        }
    }

    [CommandController("clear", Summary = nameof(ControllerSummary))]
    internal static class ClearCommand
    {
        private static string ControllerSummary => GetString("Clears item drops or projectiles.");
        private static string ExecuteSummary => GetString("Clears nearby entities around your current location.");
        private static string KindInvalidTokenMessage(params object?[] args) => GetString("\"{0}\" is not a valid clear option.", args);
        private static string RadiusInvalidTokenMessage(params object?[] args) => GetString("\"{0}\" is not a valid radius.", args);
        private static string RadiusOutOfRangeMessage(params object?[] args) => GetString("\"{0}\" is not a valid radius.", args);

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [DisallowRest]
        [TShockCommand(nameof(Permissions.clear), ServerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [FromAmbientContext] ServerContext server,
            [CommandParam(Name = "kind")]
            [CommandTokenEnum(InvalidTokenMessage = nameof(KindInvalidTokenMessage))]
            ClearTargetKind kind,
            [Int32Value(
                Minimum = 1,
                InvalidTokenMessage = nameof(RadiusInvalidTokenMessage),
                OutOfRangeMessage = nameof(RadiusOutOfRangeMessage))]
            int radius = 50) {
            return WorldUtilityCommandHelpers.ClearNearby(context, server, kind, radius);
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.InfoLines([
                GetString("Clear Syntax"),
                GetString($"{"clear".Color(Utils.BoldHighlight)} <{"item".Color(Utils.GreenHighlight)}|{"npc".Color(Utils.RedHighlight)}|{"projectile".Color(Utils.YellowHighlight)}> [{"radius".Color(Utils.PinkHighlight)}]"),
                GetString($"Example usage: {"clear".Color(Utils.BoldHighlight)} {"i".Color(Utils.RedHighlight)} {"10000".Color(Utils.GreenHighlight)}"),
                GetString($"Example usage: {"clear".Color(Utils.BoldHighlight)} {"item".Color(Utils.RedHighlight)} {"10000".Color(Utils.GreenHighlight)}"),
                GetString("If you do not specify a radius, it will use a default radius of 50 around your character."),
                GetString($"You can use {Commands.SilentSpecifier.Color(Utils.GreenHighlight)} instead of {Commands.Specifier.Color(Utils.RedHighlight)} to execute this command silently."),
            ]);
        }
    }

    [CommandController("antibuild", Summary = nameof(ControllerSummary))]
    internal static class AntiBuildCommand
    {
        private static string ControllerSummary => GetString("Toggles build protection.");
        private static string ExecuteSummary => GetString("Toggles anti-build for one server or all running servers.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.antibuild))]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            if (context.Server is null) {
                TShock.Config.GlobalSettings.DisableBuild = !TShock.Config.GlobalSettings.DisableBuild;
                foreach (var server in UnifiedServerCoordinator.Servers) {
                    if (!server.IsRunning) {
                        continue;
                    }

                    WorldUtilityCommandHelpers.ToggleAntiBuild(server);
                }
            }
            else {
                WorldUtilityCommandHelpers.ToggleAntiBuild(context.Server);
            }

            TShock.Config.SaveToFile();
            return CommandOutcome.Empty;
        }
    }

    [CommandController("grow", Summary = nameof(ControllerSummary))]
    internal static class GrowCommand
    {
        private static string ControllerSummary => GetString("Grows plants at your location.");
        private static string DefaultHelpSummary => GetString("Shows grow help when no plant type is provided.");
        private static string HelpSummary => GetString("Shows the grow command help page.");
        private static string PageInvalidTokenMessage(params object?[] args) => GetString("\"{0}\" is not a valid page number.", args);
        private static string ExecuteSummary => GetString("Grows a supported plant at your current location.");
        private static string KindInvalidTokenMessage => GetString("Unknown plant!");

        [CommandAction(Summary = nameof(DefaultHelpSummary))]
        [RequireUserArgumentCount(0)]
        [TShockCommand(nameof(Permissions.grow), PlayerScope = true)]
        public static CommandOutcome DefaultHelp() {
            return WorldUtilityCommandHelpers.BuildGrowHelp(pageNumber: 1);
        }

        [CommandAction("help", Summary = nameof(HelpSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.grow), PlayerScope = true)]
        public static CommandOutcome Help(
            [PageRef<GrowHelpPageSource>(
                InvalidTokenMessage = nameof(PageInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int page = 1) {
            return WorldUtilityCommandHelpers.BuildGrowHelp(page);
        }

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.grow), PlayerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "type")]
            [CommandTokenEnum(InvalidTokenMessage = nameof(KindInvalidTokenMessage))]
            GrowKind kind) {
            return WorldUtilityCommandHelpers.GrowAtLocation(context, kind);
        }

        private sealed class GrowHelpPageSource : IPageRefSource<GrowHelpPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var lines = WorldUtilityCommandHelpers.CreateGrowHelpLines();
                return TSPageRefResolver.CountPages(lines.Count, WorldUtilityCommandHelpers.CreateGrowHelpPageSettings());
            }
        }
    }

    [CommandController("forcehalloween", Summary = nameof(ControllerSummary))]
    internal static class ForceHalloweenCommand
    {
        private static string ControllerSummary => GetString("Toggles halloween mode (goodie bags, pumpkins, etc).");
        private static string ExecuteSummary => GetString("Toggles halloween mode for the current server.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.halloween), ServerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [FromAmbientContext] ServerContext server) {
            var settings = TShock.Config.GetServerSettings(server.Name);
            settings.ForceHalloween = !settings.ForceHalloween;
            server.Main.checkHalloween();

            if (context.Silent) {
                return CommandOutcome.Info(settings.ForceHalloween
                    ? GetString("Enabled halloween mode.")
                    : GetString("Disabled halloween mode."));
            }

            WorldCommandHelpers.ResolveServerPlayer(server).BCInfoMessage(settings.ForceHalloween
                ? GetString("{0} enabled halloween mode.", context.Executor.Name)
                : GetString("{0} disabled halloween mode.", context.Executor.Name));
            return CommandOutcome.Empty;
        }
    }

    [CommandController("forcexmas", Summary = nameof(ControllerSummary))]
    internal static class ForceXmasCommand
    {
        private static string ControllerSummary => GetString("Toggles christmas mode (present spawning, santa, etc).");
        private static string ExecuteSummary => GetString("Toggles christmas mode for the current server.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.xmas), ServerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [FromAmbientContext] ServerContext server) {
            var settings = TShock.Config.GetServerSettings(server.Name);
            settings.ForceXmas = !settings.ForceXmas;
            server.Main.checkXMas();

            if (context.Silent) {
                return CommandOutcome.Info(settings.ForceXmas
                    ? GetString("Enabled xmas mode.")
                    : GetString("Disabled xmas mode."));
            }

            WorldCommandHelpers.ResolveServerPlayer(server).BCInfoMessage(settings.ForceXmas
                ? GetString("{0} enabled xmas mode.", context.Executor.Name)
                : GetString("{0} disabled xmas mode.", context.Executor.Name));
            return CommandOutcome.Empty;
        }
    }

    [CommandController("hardmode", Summary = nameof(ControllerSummary))]
    internal static class HardmodeCommand
    {
        private static string ControllerSummary => GetString("Toggles the world's hardmode status.");
        private static string ExecuteSummary => GetString("Toggles hardmode for the current world.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.hardmode), ServerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] ServerContext server) {
            var settings = TShock.Config.GetServerSettings(server.Name);
            if (server.Main.hardMode) {
                server.Main.hardMode = false;
                WorldCommandHelpers.SendWorldInfo(server);
                return CommandOutcome.Success(GetString("Hardmode is now off."));
            }

            if (settings.DisableHardmode) {
                return CommandOutcome.Error(GetString("Hardmode is disabled in the server configuration file."));
            }

            server.WorldGen.StartHardmode();
            return CommandOutcome.Success(GetString("Hardmode is now on."));
        }
    }

    [CommandController("evil", Summary = nameof(ControllerSummary))]
    internal static class EvilCommand
    {
        private static string ControllerSummary => GetString("Switches the world's evil.");
        private static string ExecuteSummary => GetString("Switches the world's evil between corruption and crimson.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.switchevil), ServerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] ServerContext server) {
            server.WorldGen.crimson = !server.WorldGen.crimson;
            WorldCommandHelpers.SendWorldInfo(server);
            return CommandOutcome.Success(GetString("World evil switched to {0}.", WorldUtilityCommandHelpers.GetWorldEvil(server)));
        }
    }

    [CommandController("settle", Summary = nameof(ControllerSummary))]
    internal static class SettleCommand
    {
        private static string ControllerSummary => GetString("Forces all liquids to update immediately.");
        private static string ExecuteSummary => GetString("Starts settling liquids on the current server.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.worldsettle), ServerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] ServerContext server) {
            if (server.Liquid.panicMode) {
                return CommandOutcome.Warning(GetString("Liquids are already settling."));
            }

            server.Liquid.StartPanic();
            return CommandOutcome.Info(GetString("Settling liquids."));
        }
    }
}
