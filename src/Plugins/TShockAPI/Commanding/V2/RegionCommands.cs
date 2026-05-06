using Microsoft.Xna.Framework;
using Terraria;
using TShockAPI.DB;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding;
using UnifierTSL.Servers;

namespace TShockAPI.Commanding.V2
{
    [Flags]
    internal enum RegionInfoFlags
    {
        None = 0,
        [CommandFlag("-d")]
        DisplayBoundaries = 1 << 0,
    }

    [CommandController("region", Summary = nameof(ControllerSummary))]
    internal static class RegionCommand
    {
        private static string RegionInvalidRegionMessage(params object?[] args) => GetString("Could not find the region {0}.", args);
        private static string AccountInvalidUserAccountMessage(params object?[] args) => GetString("Player {0} not found.", args);
        private static string GroupInvalidGroupMessage(params object?[] args) => GetString("Group {0} not found.", args);
        private static string RegionInvalidRegionMessage4 => GetString("Could not find specified region");
        private static string RegionInvalidRegionMessage5(params object?[] args) => GetString("Invalid region \"{0}\".", args);
        private static string RegionInvalidRegionMessage8(params object?[] args) => GetString("Region \"{0}\" does not exist.", args);

        private static string ControllerSummary => GetString("Manages regions.");
        private static string DefaultSummary => GetString("Shows region subcommand help.");
        private static string DefaultPageSummary => GetString("Shows region subcommand help.");
        private static string PageNumberInvalidTokenMessage(params object?[] args) => GetString("\"{0}\" is not a valid page number.", args);
        private static string HelpSummary => GetString("Shows region subcommand help.");
        private static string HelpPageSummary => GetString("Shows region subcommand help.");
        private static string NameSummary => GetString("Shows the name of the region at the next edited block.");
        private static string SetSummary => GetString("Sets one of the temporary region corners.");
        private static string PointInvalidTokenMessage => GetString("Invalid region point selection. Expected 1 or 2.");
        private static string PointOutOfRangeMessage => GetString("Invalid region point selection. Expected 1 or 2.");
        private static string DefineSummary => GetString("Defines a region from the two temporary points.");
        private static string ProtectSummary => GetString("Changes whether a region is protected.");
        private static string StateInvalidTokenMessage => GetString("Invalid region protection state. Expected true/false, on/off, yes/no, y/n, or 1/0.");
        private static string DeleteSummary => GetString("Deletes a region.");
        private static string ClearSummary => GetString("clear - Clears the temporary region points.");
        private static string AllowSummary => GetString("Allows a user account to build in a region.");
        private static string RemoveSummary => GetString("Removes a user account from a region.");
        private static string AllowGroupSummary => GetString("Allows a group to build in a region.");
        private static string RemoveGroupSummary => GetString("Removes a group from a region.");
        private static string ListSummary => GetString("Lists all regions in the current world.");
        private static string InfoSummary => GetString("Shows information about a region.");
        private static string SetZSummary => GetString("z <name> <#> - Sets the z-order of the region.");
        private static string ZInvalidTokenMessage => GetString("Invalid region z index.");
        private static string ResizeSummary => GetString("Resizes a region.");
        private static string AmountInvalidTokenMessage => GetString("Invalid region resize amount.");
        private static string ExpandSummary => GetString("Resizes a region.");
        private static string RenameSummary => GetString("Renames a region.");
        private static string TeleportSummary => GetString("Teleports you to a region's center.");

        [CommandAction(Summary = nameof(DefaultSummary))]
        [RejectNumericUserArgument]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome Default(
            [FromAmbientContext] TSExecutionContext context,
            [RemainingText] string trailing = "") => BuildHelpOutcome(context, pageNumber: 1);

        [CommandAction(Summary = nameof(DefaultPageSummary))]
        [RequireNumericUserArgument]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome DefaultPage(
            [FromAmbientContext] TSExecutionContext context,
            [PageRef<RegionHelpPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber) => BuildHelpOutcome(context, pageNumber);

        [CommandAction("help", Summary = nameof(HelpSummary))]
        [RejectNumericUserArgument]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome Help(
            [FromAmbientContext] TSExecutionContext context,
            [RemainingText] string trailing = "") => BuildHelpOutcome(context, pageNumber: 1);

        [CommandAction("help", Summary = nameof(HelpPageSummary))]
        [RequireNumericUserArgument]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome HelpPage(
            [FromAmbientContext] TSExecutionContext context,
            [PageRef<RegionHelpPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber) => BuildHelpOutcome(context, pageNumber);

        [CommandAction("name", Summary = nameof(NameSummary))]
        [TShockCommand(nameof(Permissions.manageregion), PlayerScope = true)]
        public static CommandOutcome Name(
            [FromAmbientContext] TSExecutionContext context,
            [CommandFlags<RegionNameDisplayFlags>] RegionNameDisplayFlags flags = RegionNameDisplayFlags.None) {
            var tsPlayer = context.Player!;
            tsPlayer.AwaitingName = true;
            tsPlayer.AwaitingNameFlags = flags;
            return CommandOutcome.Info(GetString("Hit a block to get the name of the region."));
        }

        [CommandAction("set", Summary = nameof(SetSummary))]
        [TShockCommand(nameof(Permissions.manageregion), PlayerScope = true)]
        public static CommandOutcome Set(
            [FromAmbientContext] TSExecutionContext context,
            [Int32Value(
                Minimum = 1,
                Maximum = 2,
                InvalidTokenMessage = nameof(PointInvalidTokenMessage),
                OutOfRangeMessage = nameof(PointOutOfRangeMessage))]
            int? point = null) {
            if (point is null) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: /region set <1/2>."));
            }

            var tsPlayer = context.Player!;
            tsPlayer.AwaitingTempPoint = point.Value;
            return CommandOutcome.Info(GetString("Hit a block to set point {0}.", point.Value));
        }

        [CommandAction("define", Summary = nameof(DefineSummary))]
        [TShockCommand(nameof(Permissions.manageregion), PlayerScope = true)]
        public static CommandOutcome Define(
            [FromAmbientContext] TSExecutionContext context,
            [RemainingText] string regionName = "") {
            var tsPlayer = context.Player!;
            if (string.IsNullOrWhiteSpace(regionName)) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}region define <name>.", Commands.Specifier));
            }

            if (tsPlayer.TempPoints.Any(static point => point == Point.Zero)) {
                return CommandOutcome.Error(GetString("Region points need to be defined first. Use /region set 1 and /region set 2."));
            }

            var server = tsPlayer.GetCurrentServer();
            var worldId = server.Main.worldID.ToString();
            var x = Math.Min(tsPlayer.TempPoints[0].X, tsPlayer.TempPoints[1].X);
            var y = Math.Min(tsPlayer.TempPoints[0].Y, tsPlayer.TempPoints[1].Y);
            var width = Math.Abs(tsPlayer.TempPoints[0].X - tsPlayer.TempPoints[1].X);
            var height = Math.Abs(tsPlayer.TempPoints[0].Y - tsPlayer.TempPoints[1].Y);

            if (!TShock.Regions.AddRegion(x, y, width, height, regionName, context.Executor.Account.Name, worldId)) {
                return CommandOutcome.Error(GetString("Region {0} already exists.", regionName));
            }

            tsPlayer.TempPoints[0] = Point.Zero;
            tsPlayer.TempPoints[1] = Point.Zero;
            return CommandOutcome.Info(GetString("Set region {0}.", regionName));
        }

        [CommandAction("protect", Summary = nameof(ProtectSummary))]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome Protect(
            [FromAmbientContext] ServerContext server,
            [RegionRef(
                nameof(RegionInvalidRegionMessage),
                LookupMode = TSLookupMatchMode.ExactOnly,
                ConsumptionMode = TSRegionConsumptionMode.SingleToken)]
            Region? region = null,
            [CommandPrompt(
                SuggestionKindId = PromptSuggestionKindIds.Enum,
                EnumCandidates = ["true", "false", "on", "off", "yes", "no", "y", "n", "1", "0"])]
            [BooleanValue(InvalidTokenMessage = nameof(StateInvalidTokenMessage))]
            bool? state = null) {
            if (region is null || state is null) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}region protect <name> <state>. Accepted values: true/false, on/off, yes/no, y/n, 1/0.", Commands.Specifier));
            }

            return TShock.Regions.SetRegionState(server.Main.worldID.ToString(), region.Name, state.Value)
                ? CommandOutcome.Info(state.Value
                    ? GetString("Marked region {0} as protected.", region.Name)
                    : GetString("Marked region {0} as unprotected.", region.Name))
                : CommandOutcome.Error(GetString("Could not find the region {0}.", region.Name));
        }

        [CommandAction("delete", Summary = nameof(DeleteSummary))]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome Delete(
            [FromAmbientContext] ServerContext server,
            [RemainingText]
            [RegionRef(nameof(RegionInvalidRegionMessage), LookupMode = TSLookupMatchMode.ExactOnly)]
            Region? region = null) {
            if (region is null) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}region delete <name>.", Commands.Specifier));
            }

            return TShock.Regions.DeleteRegion(server.Main.worldID.ToString(), region.Name)
                ? CommandOutcome.Info(GetString("Deleted region \"{0}\".", region.Name))
                : CommandOutcome.Error(GetString("Could not find the region {0}.", region.Name));
        }

        [CommandAction("clear", Summary = nameof(ClearSummary))]
        [TShockCommand(nameof(Permissions.manageregion), PlayerScope = true)]
        public static CommandOutcome Clear(
            [FromAmbientContext] TSExecutionContext context,
            [RemainingText] string _ = "") {
            var tsPlayer = context.Player!;
            tsPlayer.TempPoints[0] = Point.Zero;
            tsPlayer.TempPoints[1] = Point.Zero;
            tsPlayer.AwaitingTempPoint = 0;
            return CommandOutcome.Info(GetString("Temporary region set points have been removed."));
        }

        [CommandAction("allow", Summary = nameof(AllowSummary))]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome Allow(
            [FromAmbientContext] ServerContext server,
            [UserAccountRef(nameof(AccountInvalidUserAccountMessage), Name = "name", LookupMode = TSLookupMatchMode.ExactOnly)]
            UserAccount? account = null,
            [CommandParam(Name = "region")]
            [RemainingText] string regionName = "") {
            if (account is null || string.IsNullOrWhiteSpace(regionName)) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}region allow <name> <region>.", Commands.Specifier));
            }

            return TShock.Regions.AddNewUser(server.Main.worldID.ToString(), regionName, account.Name)
                ? CommandOutcome.Info(GetString("Added user {0} to {1}.", account.Name, regionName))
                : CommandOutcome.Error(GetString("Could not find the region {0}.", regionName));
        }

        [CommandAction("remove", Summary = nameof(RemoveSummary))]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome Remove(
            [FromAmbientContext] ServerContext server,
            [UserAccountRef(nameof(AccountInvalidUserAccountMessage), Name = "name", LookupMode = TSLookupMatchMode.ExactOnly)]
            UserAccount? account = null,
            [CommandParam(Name = "region")]
            [RemainingText] string regionName = "") {
            if (account is null || string.IsNullOrWhiteSpace(regionName)) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}region remove <name> <region>.", Commands.Specifier));
            }

            return TShock.Regions.RemoveUser(server.Main.worldID.ToString(), regionName, account.Name)
                ? CommandOutcome.Info(GetString("Removed user {0} from {1}.", account.Name, regionName))
                : CommandOutcome.Error(GetString("Could not find the region {0}.", regionName));
        }

        [CommandAction("allowg", Summary = nameof(AllowGroupSummary))]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome AllowGroup(
            [FromAmbientContext] ServerContext server,
            [GroupRef(nameof(GroupInvalidGroupMessage), Name = "group", LookupMode = TSLookupMatchMode.ExactOnly)]
            Group? group = null,
            [CommandParam(Name = "region")]
            [RemainingText] string regionName = "") {
            if (group is null || string.IsNullOrWhiteSpace(regionName)) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}region allowg <group> <region>.", Commands.Specifier));
            }

            return TShock.Regions.AllowGroup(server.Main.worldID.ToString(), regionName, group.Name)
                ? CommandOutcome.Info(GetString("Added group {0} to {1}.", group.Name, regionName))
                : CommandOutcome.Error(GetString("Could not find the region {0}.", regionName));
        }

        [CommandAction("removeg", Summary = nameof(RemoveGroupSummary))]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome RemoveGroup(
            [FromAmbientContext] ServerContext server,
            [GroupRef(nameof(GroupInvalidGroupMessage), Name = "group", LookupMode = TSLookupMatchMode.ExactOnly)]
            Group? group = null,
            [CommandParam(Name = "region")]
            [RemainingText] string regionName = "") {
            if (group is null || string.IsNullOrWhiteSpace(regionName)) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}region removeg <group> <region>.", Commands.Specifier));
            }

            return TShock.Regions.RemoveGroup(server.Main.worldID.ToString(), regionName, group.Name)
                ? CommandOutcome.Info(GetString("Removed group {0} from {1}", group.Name, regionName))
                : CommandOutcome.Error(GetString("Could not find the region {0}.", regionName));
        }

        [CommandAction("list", Summary = nameof(ListSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome List(
            [FromAmbientContext] ServerContext server,
            [PageRef<RegionListPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber = 1) {
            var lines = CreateRegionListLines(server);
            return CommandOutcome.Page(
                pageNumber,
                lines,
                lines.Count,
                CreateRegionListPageSettings());
        }

        [CommandAction("info", Summary = nameof(InfoSummary))]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome Info(
            [FromAmbientContext] TSExecutionContext context,
            [FromAmbientContext] ServerContext server,
            [RegionRef(
                nameof(RegionInvalidRegionMessage),
                LookupMode = TSLookupMatchMode.ExactOnly,
                ConsumptionMode = TSRegionConsumptionMode.SingleToken)]
            Region? region = null,
            [PageRef<RegionInfoPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber = 1,
            [CommandFlags<RegionInfoFlags>] RegionInfoFlags flags = RegionInfoFlags.None) {
            return BuildRegionInfoOutcome(
                context,
                server,
                region,
                displayBoundaries: (flags & RegionInfoFlags.DisplayBoundaries) != 0,
                pageNumber);
        }

        [CommandAction("z", Summary = nameof(SetZSummary))]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome SetZ(
            [FromAmbientContext] ServerContext server,
            [RegionRef(
                nameof(RegionInvalidRegionMessage4),
                LookupMode = TSLookupMatchMode.ExactOnly,
                ConsumptionMode = TSRegionConsumptionMode.SingleToken)]
            Region? region = null,
            [Int32Value(InvalidTokenMessage = nameof(ZInvalidTokenMessage))]
            int? z = null) {
            if (region is null || z is null) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}region z <name> <#>", Commands.Specifier));
            }

            return TShock.Regions.SetZ(server.Main.worldID.ToString(), region.Name, z.Value)
                ? CommandOutcome.Info(GetString("Region's z is now {0}", z.Value))
                : CommandOutcome.Error(GetString("Could not find specified region"));
        }

        [CommandAction("resize", Summary = nameof(ResizeSummary))]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome Resize(
            [FromAmbientContext] ServerContext server,
            [RegionRef(
                nameof(RegionInvalidRegionMessage5),
                LookupMode = TSLookupMatchMode.ExactOnly,
                ConsumptionMode = TSRegionConsumptionMode.SingleToken)]
            Region? region = null,
            TSRegionResizeDirection? direction = null,
            [Int32Value(InvalidTokenMessage = nameof(AmountInvalidTokenMessage))]
            int? amount = null) {
            return ResizeInternal(server, region, direction, amount);
        }

        [CommandAction("expand", Summary = nameof(ExpandSummary))]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome Expand(
            [FromAmbientContext] ServerContext server,
            [RegionRef(
                nameof(RegionInvalidRegionMessage5),
                LookupMode = TSLookupMatchMode.ExactOnly,
                ConsumptionMode = TSRegionConsumptionMode.SingleToken)]
            Region? region = null,
            TSRegionResizeDirection? direction = null,
            [Int32Value(InvalidTokenMessage = nameof(AmountInvalidTokenMessage))]
            int? amount = null) {
            return ResizeInternal(server, region, direction, amount);
        }

        [CommandAction("rename", Summary = nameof(RenameSummary))]
        [TShockCommand(nameof(Permissions.manageregion), ServerScope = true)]
        public static CommandOutcome Rename(
            [FromAmbientContext] ServerContext server,
            [RegionRef(
                nameof(RegionInvalidRegionMessage5),
                LookupMode = TSLookupMatchMode.ExactOnly,
                ConsumptionMode = TSRegionConsumptionMode.SingleToken)]
            Region? region = null,
            string newName = "") {
            if (region is null || string.IsNullOrWhiteSpace(newName)) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}region rename <region> <new name>", Commands.Specifier));
            }

            if (region.Name.Equals(newName, StringComparison.Ordinal)) {
                return CommandOutcome.Error(GetString("Error: both names are the same."));
            }

            var worldId = server.Main.worldID.ToString();
            var existingRegion = TShock.Regions.GetRegionByName(newName, worldId);
            if (existingRegion is not null) {
                return CommandOutcome.Error(GetString("Region \"{0}\" already exists.", newName));
            }

            return TShock.Regions.RenameRegion(worldId, region.Name, newName)
                ? CommandOutcome.Info(GetString("Region renamed successfully!"))
                : CommandOutcome.Error(GetString("Failed to rename the region."));
        }

        [CommandAction("tp", Summary = nameof(TeleportSummary))]
        [TShockCommand(nameof(Permissions.manageregion), PlayerScope = true)]
        public static CommandOutcome Teleport(
            [FromAmbientContext] TSExecutionContext context,
            [RemainingText]
            [RegionRef(nameof(RegionInvalidRegionMessage8), LookupMode = TSLookupMatchMode.ExactOnly)]
            Region? region = null) {
            if (!context.Executor.HasPermission(Permissions.tp)) {
                return CommandOutcome.Error(GetString("You do not have permission to teleport."));
            }

            if (region is null) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}region tp <region>.", Commands.Specifier));
            }

            context.Player!.TeleportCentered(region.Area.Center.ToWorldCoordinates());
            return CommandOutcome.Empty;
        }

        private static CommandOutcome BuildHelpOutcome(TSExecutionContext context, int pageNumber) {
            var lines = CreateHelpLines(context);
            return CommandOutcome.Page(
                pageNumber,
                lines,
                lines.Count,
                CreateHelpPageSettings());
        }

        private static List<string> CreateHelpLines(TSExecutionContext? context) {
            List<string> lines = [
                GetString("set <1/2> - Sets the temporary region points."),
                GetString("clear - Clears the temporary region points."),
                GetString("define <name> - Defines the region with the given name."),
                GetString("delete <name> - Deletes the given region."),
                GetString("name [-u][-z][-p] - Shows the name of the region at the given point."),
                GetString("rename <region> <new name> - Renames the given region."),
                GetString("list - Lists all regions."),
                GetString("resize <region> <u/d/l/r> <amount> - Resizes a region."),
                GetString("allow <user> <region> - Allows a user to a region."),
                GetString("remove <user> <region> - Removes a user from a region."),
                GetString("allowg <group> <region> - Allows a user group to a region."),
                GetString("removeg <group> <region> - Removes a user group from a region."),
                GetString("info <region> [-d] - Displays several information about the given region."),
                GetString("protect <name> <state> - Sets whether the tiles inside the region are protected or not. Accepted values: true/false, on/off, yes/no, y/n, 1/0."),
                GetString("z <name> <#> - Sets the z-order of the region."),
            ];
            if (context?.Executor.HasPermission(Permissions.tp) == true) {
                lines.Add(GetString("tp <region> - Teleports you to the given region's center."));
            }

            return lines;
        }

        private static CommandOutcome BuildRegionInfoOutcome(
            TSExecutionContext context,
            ServerContext server,
            Region? region,
            bool displayBoundaries,
            int pageNumber) {
            if (region is null) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}region info <region> [-d] [page].", Commands.Specifier));
            }

            if (displayBoundaries) {
                if (context.Player is null) {
                    return CommandOutcome.Error(GetString("You must use this command in-game."));
                }

                ShowRegionBoundaries(server, context.Player, region.Area);
            }

            var lines = BuildRegionInfoLines(region);
            var builder = CommandOutcome.CreateBuilder();
            builder.AddPage(
                pageNumber,
                lines,
                lines.Count,
                CreateRegionInfoPageSettings(region.Name));
            return builder.Build();
        }

        private static List<string> BuildRegionInfoLines(Region region) {
            List<string> lines = [
                GetString("X: {0}; Y: {1}; W: {2}; H: {3}, Z: {4}", region.Area.X, region.Area.Y, region.Area.Width, region.Area.Height, region.Z),
                GetString("Region owner: {0}.", region.Owner),
                GetString("Protected: {0}.", region.DisableBuild),
            ];

            if (region.AllowedIDs.Count > 0) {
                var sharedUsers = region.AllowedIDs.Select(static userId => {
                    var account = TShock.UserAccounts.GetUserAccountByID(userId);
                    return account is not null
                        ? account.Name
                        : string.Concat("{ID: ", userId, "}");
                });
                var extraLines = PaginationTools.BuildLinesFromTerms(sharedUsers.Distinct());
                extraLines[0] = GetString("Shared with: ") + extraLines[0];
                lines.AddRange(extraLines);
            }
            else {
                lines.Add(GetString("Region is not shared with any users."));
            }

            if (region.AllowedGroups.Count > 0) {
                var extraLines = PaginationTools.BuildLinesFromTerms(region.AllowedGroups.Distinct());
                extraLines[0] = GetString("Shared with groups: ") + extraLines[0];
                lines.AddRange(extraLines);
            }
            else {
                lines.Add(GetString("Region is not shared with any groups."));
            }

            return lines;
        }

        private static CommandOutcome ResizeInternal(
            ServerContext server,
            Region? region,
            TSRegionResizeDirection? direction,
            int? amount) {
            if (region is null || direction is null || amount is null) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}region resize <region> <u/d/l/r> <amount>", Commands.Specifier));
            }

            return TShock.Regions.ResizeRegion(
                server.Main.worldID.ToString(),
                region.Name,
                amount.Value,
                ResolveDirectionValue(direction.Value))
                ? CommandOutcome.Info(GetString("Region Resized Successfully!"))
                : CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}region resize <region> <u/d/l/r> <amount>", Commands.Specifier));
        }

        private static int ResolveDirectionValue(TSRegionResizeDirection direction) {
            return direction switch {
                TSRegionResizeDirection.Up => 0,
                TSRegionResizeDirection.Right => 1,
                TSRegionResizeDirection.Down => 2,
                TSRegionResizeDirection.Left => 3,
                _ => -1,
            };
        }

        private static void ShowRegionBoundaries(ServerContext server, TSPlayer player, Rectangle regionArea) {
            foreach (var boundaryPoint in Utils.EnumerateRegionBoundaries(regionArea)) {
                if (((boundaryPoint.X + boundaryPoint.Y) & 1) != 0) {
                    continue;
                }

                var tile = server.Main.tile[boundaryPoint.X, boundaryPoint.Y];
                var oldWireState = tile.wire();
                tile.wire(true);

                try {
                    player.SendTileSquareCentered(boundaryPoint.X, boundaryPoint.Y, 1);
                }
                finally {
                    tile.wire(oldWireState);
                }
            }

            Timer boundaryHideTimer = null!;
            boundaryHideTimer = new Timer(_ => {
                foreach (var boundaryPoint in Utils.EnumerateRegionBoundaries(regionArea)) {
                    if (((boundaryPoint.X + boundaryPoint.Y) & 1) == 0) {
                        player.SendTileSquareCentered(boundaryPoint.X, boundaryPoint.Y, 1);
                    }
                }

                boundaryHideTimer.Dispose();
            }, null, 5000, Timeout.Infinite);
        }

        private sealed class RegionHelpPageSource : IPageRefSource<RegionHelpPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var lines = CreateHelpLines(context.InvocationContext?.ExecutionContext as TSExecutionContext);
                return TSPageRefResolver.CountPages(lines.Count, CreateHelpPageSettings());
            }
        }

        private sealed class RegionListPageSource : IPageRefSource<RegionListPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                if (context.Server is null) {
                    return null;
                }

                var lines = CreateRegionListLines(context.Server);
                return lines.Count == 0
                    ? null
                    : TSPageRefResolver.CountPages(lines.Count, CreateRegionListPageSettings());
            }
        }

        private sealed class RegionInfoPageSource : IPageRefSource<RegionInfoPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var server = context.Server;
                var regionName = context.InvocationContext?.UserArguments.Length > 0
                    ? context.InvocationContext.UserArguments[0]
                    : null;
                if (server is null || string.IsNullOrWhiteSpace(regionName)) {
                    return null;
                }

                var region = TShock.Regions.GetRegionByName(regionName, server.Main.worldID.ToString());
                if (region is null) {
                    return null;
                }

                var lines = BuildRegionInfoLines(region);
                return TSPageRefResolver.CountPages(lines.Count, CreateRegionInfoPageSettings(region.Name));
            }
        }

        private static PaginationTools.Settings CreateHelpPageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Available Region Sub-Commands ({{0}}/{{1}}):"),
                FooterFormat = GetString("Type {0}region {{0}} for more sub-commands.", Commands.Specifier),
            };
        }

        private static List<string> CreateRegionListLines(ServerContext server) {
            return PaginationTools.BuildLinesFromTerms(
                from region in TShock.Regions.Regions
                where region.WorldID == server.Main.worldID.ToString()
                select region.Name);
        }

        private static PaginationTools.Settings CreateRegionListPageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Regions ({0}/{1}):"),
                FooterFormat = GetString("Type {0}region list {{0}} for more.", Commands.Specifier),
                NothingToDisplayString = GetString("There are currently no regions defined."),
            };
        }

        private static PaginationTools.Settings CreateRegionInfoPageSettings(string regionName) {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Information About Region \"{0}\" ({{0}}/{{1}}):", regionName),
                FooterFormat = GetString("Type {0}region info {1} {{0}} for more information.", Commands.Specifier, regionName),
            };
        }
    }
}
