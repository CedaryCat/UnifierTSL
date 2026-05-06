using Terraria.ID;
using TShockAPI.Localization;
using UnifierTSL.Commanding;
using static TShockAPI.Commanding.V2.RestrictionCommandHelpers;

namespace TShockAPI.Commanding.V2
{
    [CommandController("itemban", Summary = nameof(ControllerSummary))]
    internal static class ItemBanCommand
    {
        private static string AddSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}itemban add <item name>.", args);
        private static string AllowSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}itemban allow <item name> <group name>.", args);
        private static string GroupInvalidGroupMessage => GetString("Invalid group.");
        private static string DeleteSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}itemban del <item name>.", args);
        private static string DisallowSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}itemban disallow <item name> <group name>.", args);

        private static string ControllerSummary => GetString("Manages item bans.");
        private static string AddSummary => GetString("add <item> - Adds an item ban.");
        private static string AllowSummary => GetString("allow <item> <group> - Allows a group to use an item.");
        private static string DeleteSummary => GetString("del <item> - Deletes an item ban.");
        private static string DisallowSummary => GetString("disallow <item> <group> - Disallows a group from using an item.");
        private static string HelpSummary => GetString("Shows item ban subcommand help.");
        private static string PageNumberInvalidTokenMessage(params object?[] args) => GetString("\"{0}\" is not a valid page number.", args);
        private static string ListSummary => GetString("list [page] - Lists all item bans.");

        [CommandAction("add", Summary = nameof(AddSummary))]
        [RequireUserArgumentCountSyntax(1, nameof(AddSyntaxMessage))]
        [TShockCommand(nameof(Permissions.manageitem))]
        public static CommandOutcome Add(
            [TSItemRef(LookupMode = TSItemLookupMode.LegacyCommand, FailureMode = TSItemFailureMode.LegacyItemBan)]
            int itemId) {
            (var storageName, var displayName) = ResolveItemIdentity(itemId);
            TShock.ItemBans.DataModel.AddNewBan(storageName);

            // Preserve TShock's command-layer coupling between banned items and their spawned projectiles.
            if (itemId == ItemID.DirtRod) {
                TShock.ProjectileBans.AddNewBan(ProjectileID.DirtBall);
            }

            if (itemId == ItemID.Sandgun) {
                TShock.ProjectileBans.AddNewBan(ProjectileID.SandBallGun);
                TShock.ProjectileBans.AddNewBan(ProjectileID.EbonsandBallGun);
                TShock.ProjectileBans.AddNewBan(ProjectileID.PearlSandBallGun);
            }

            return CommandOutcome.Success(GetString("Banned {0}.", displayName));
        }

        [CommandAction("allow", Summary = nameof(AllowSummary))]
        [RequireUserArgumentCountSyntax(2, nameof(AllowSyntaxMessage))]
        [TShockCommand(nameof(Permissions.manageitem))]
        public static CommandOutcome Allow(
            [TSItemRef(LookupMode = TSItemLookupMode.LegacyCommand, FailureMode = TSItemFailureMode.LegacyItemBan)]
            int itemId,
            [GroupRef(nameof(GroupInvalidGroupMessage), LookupMode = TSLookupMatchMode.ExactOnly)] Group group) {
            (var storageName, var displayName) = ResolveItemIdentity(itemId);
            var ban = TShock.ItemBans.DataModel.GetItemBanByName(storageName);
            if (ban is null) {
                return CommandOutcome.Error(GetString("{0} is not banned.", displayName));
            }

            if (ban.AllowedGroups.Contains(group.Name)) {
                return CommandOutcome.Warning(GetString("{0} is already allowed to use {1}.", group.Name, displayName));
            }

            TShock.ItemBans.DataModel.AllowGroup(storageName, group.Name);
            return CommandOutcome.Success(GetString("{0} has been allowed to use {1}.", group.Name, displayName));
        }

        [CommandAction("del", Summary = nameof(DeleteSummary))]
        [RequireUserArgumentCountSyntax(1, nameof(DeleteSyntaxMessage))]
        [TShockCommand(nameof(Permissions.manageitem))]
        public static CommandOutcome Delete(
            [TSItemRef(LookupMode = TSItemLookupMode.LegacyCommand, FailureMode = TSItemFailureMode.LegacyItemBan)]
            int itemId) {
            (var storageName, var displayName) = ResolveItemIdentity(itemId);
            TShock.ItemBans.DataModel.RemoveBan(storageName);
            return CommandOutcome.Success(GetString("Unbanned {0}.", displayName));
        }

        [CommandAction("disallow", Summary = nameof(DisallowSummary))]
        [RequireUserArgumentCountSyntax(2, nameof(DisallowSyntaxMessage))]
        [TShockCommand(nameof(Permissions.manageitem))]
        public static CommandOutcome Disallow(
            [TSItemRef(LookupMode = TSItemLookupMode.LegacyCommand, FailureMode = TSItemFailureMode.LegacyItemBan)]
            int itemId,
            [GroupRef(nameof(GroupInvalidGroupMessage), LookupMode = TSLookupMatchMode.ExactOnly)] Group group) {
            (var storageName, var displayName) = ResolveItemIdentity(itemId);
            var ban = TShock.ItemBans.DataModel.GetItemBanByName(storageName);
            if (ban is null) {
                return CommandOutcome.Error(GetString("{0} is not banned.", displayName));
            }

            if (!ban.AllowedGroups.Contains(group.Name)) {
                return CommandOutcome.Warning(GetString("{0} is already disallowed to use {1}.", group.Name, displayName));
            }

            TShock.ItemBans.DataModel.RemoveGroup(storageName, group.Name);
            return CommandOutcome.Success(GetString("{0} has been disallowed to use {1}.", group.Name, displayName));
        }

        [CommandAction("help", Summary = nameof(HelpSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageitem))]
        public static CommandOutcome Help(
            [PageRef<ItemBanHelpPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber = 1) {
            var lines = CreateHelpLines();
            return CommandOutcome.Page(
                pageNumber,
                lines,
                lines.Count,
                CreateHelpPageSettings());
        }

        [CommandAction("list", Summary = nameof(ListSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageitem))]
        public static CommandOutcome List(
            [PageRef<ItemBanListPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber = 1) {
            var lines = CreateListLines();
            return CommandOutcome.Page(
                pageNumber,
                lines,
                lines.Count,
                CreateListPageSettings());
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch(
            [FromAmbientContext] TSExecutionContext context,
            CommandInvocationContext invocation) => BuildRootMismatchOutcome(
                context,
                invocation,
                Permissions.manageitem,
                static _ => Help(),
                "itemban");

        private sealed class ItemBanHelpPageSource : IPageRefSource<ItemBanHelpPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var lines = CreateHelpLines();
                return TSPageRefResolver.CountPages(lines.Count, CreateHelpPageSettings());
            }
        }

        private sealed class ItemBanListPageSource : IPageRefSource<ItemBanListPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var lines = CreateListLines();
                return lines.Count == 0
                    ? null
                    : TSPageRefResolver.CountPages(lines.Count, CreateListPageSettings());
            }
        }

        private static List<string> CreateHelpLines() {
            return [
                GetString("add <item> - Adds an item ban."),
                GetString("allow <item> <group> - Allows a group to use an item."),
                GetString("del <item> - Deletes an item ban."),
                GetString("disallow <item> <group> - Disallows a group from using an item."),
                GetString("list [page] - Lists all item bans.")
            ];
        }

        private static PaginationTools.Settings CreateHelpPageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Item Ban Sub-Commands ({{0}}/{{1}}):"),
                FooterFormat = GetString("Type {0}itemban help {{0}} for more sub-commands.", Commands.Specifier),
            };
        }

        private static List<string> CreateListLines() {
            return PaginationTools.BuildLinesFromTerms(TShock.ItemBans.DataModel.ItemBans.Select(static ban => ban.Name));
        }

        private static PaginationTools.Settings CreateListPageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Item bans ({{0}}/{{1}}):"),
                FooterFormat = GetString("Type {0}itemban list {{0}} for more.", Commands.Specifier),
                NothingToDisplayString = GetString("There are currently no banned items."),
            };
        }
    }

    [CommandController("projban", Summary = nameof(ControllerSummary))]
    internal static class ProjectileBanCommand
    {
        private static string AddSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}projban add <proj id>", args);
        private static string ProjectileIdInvalidProjectileMessage => GetString("Invalid projectile ID!");
        private static string AllowSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}projban allow <id> <group>.", args);
        private static string ProjectileIdInvalidProjectileMessage2 => GetString("Invalid projectile ID.");
        private static string GroupInvalidGroupMessage => GetString("Invalid group.");
        private static string DeleteSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}projban del <id>.", args);
        private static string DisallowSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}projban disallow <id> <group name>.", args);

        private static string ControllerSummary => GetString("Manages projectile bans.");
        private static string AddSummary => GetString("add <projectile ID> - Adds a projectile ban.");
        private static string AllowSummary => GetString("allow <projectile ID> <group> - Allows a group to use a projectile.");
        private static string DeleteSummary => GetString("del <projectile ID> - Deletes an projectile ban.");
        private static string DisallowSummary => GetString("disallow <projectile ID> <group> - Disallows a group from using a projectile.");
        private static string HelpSummary => GetString("Shows projectile ban subcommand help.");
        private static string PageNumberInvalidTokenMessage(params object?[] args) => GetString("\"{0}\" is not a valid page number.", args);
        private static string ListSummary => GetString("list [page] - Lists all projectile bans.");

        [CommandAction("add", Summary = nameof(AddSummary))]
        [RequireUserArgumentCountSyntax(1, nameof(AddSyntaxMessage))]
        [TShockCommand(nameof(Permissions.manageprojectile))]
        public static CommandOutcome Add(
            [ProjectileRef(nameof(ProjectileIdInvalidProjectileMessage))]
            short projectileId) {
            TShock.ProjectileBans.AddNewBan((short)projectileId);
            return CommandOutcome.Success(GetString("Banned projectile {0}.", projectileId));
        }

        [CommandAction("allow", Summary = nameof(AllowSummary))]
        [RequireUserArgumentCountSyntax(2, nameof(AllowSyntaxMessage))]
        [TShockCommand(nameof(Permissions.manageprojectile))]
        public static CommandOutcome Allow(
            [ProjectileRef(nameof(ProjectileIdInvalidProjectileMessage2))]
            short projectileId,
            [GroupRef(nameof(GroupInvalidGroupMessage), LookupMode = TSLookupMatchMode.ExactOnly)] Group group) {
            var ban = TShock.ProjectileBans.GetBanById((short)projectileId);
            if (ban is null) {
                return CommandOutcome.Error(GetString("Projectile {0} is not banned.", projectileId));
            }

            if (ban.AllowedGroups.Contains(group.Name)) {
                return CommandOutcome.Warning(GetString("{0} is already allowed to use projectile {1}.", group.Name, projectileId));
            }

            TShock.ProjectileBans.AllowGroup((short)projectileId, group.Name);
            return CommandOutcome.Success(GetString("{0} has been allowed to use projectile {1}.", group.Name, projectileId));
        }

        [CommandAction("del", Summary = nameof(DeleteSummary))]
        [RequireUserArgumentCountSyntax(1, nameof(DeleteSyntaxMessage))]
        [TShockCommand(nameof(Permissions.manageprojectile))]
        public static CommandOutcome Delete(
            [ProjectileRef(nameof(ProjectileIdInvalidProjectileMessage2))]
            short projectileId) {
            TShock.ProjectileBans.RemoveBan((short)projectileId);
            return CommandOutcome.Success(GetString("Unbanned projectile {0}.", projectileId));
        }

        [CommandAction("disallow", Summary = nameof(DisallowSummary))]
        [RequireUserArgumentCountSyntax(2, nameof(DisallowSyntaxMessage))]
        [TShockCommand(nameof(Permissions.manageprojectile))]
        public static CommandOutcome Disallow(
            [ProjectileRef(nameof(ProjectileIdInvalidProjectileMessage2))]
            short projectileId,
            [GroupRef(nameof(GroupInvalidGroupMessage), LookupMode = TSLookupMatchMode.ExactOnly)] Group group) {
            var ban = TShock.ProjectileBans.GetBanById((short)projectileId);
            if (ban is null) {
                return CommandOutcome.Error(GetString("Projectile {0} is not banned.", projectileId));
            }

            if (!ban.AllowedGroups.Contains(group.Name)) {
                return CommandOutcome.Warning(GetString("{0} is already prevented from using projectile {1}.", group.Name, projectileId));
            }

            TShock.ProjectileBans.RemoveGroup((short)projectileId, group.Name);
            return CommandOutcome.Success(GetString("{0} has been disallowed from using projectile {1}.", group.Name, projectileId));
        }

        [CommandAction("help", Summary = nameof(HelpSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageprojectile))]
        public static CommandOutcome Help(
            [PageRef<ProjectileBanHelpPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber = 1) {
            var lines = CreateHelpLines();
            return CommandOutcome.Page(
                pageNumber,
                lines,
                lines.Count,
                CreateHelpPageSettings());
        }

        [CommandAction("list", Summary = nameof(ListSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.manageprojectile))]
        public static CommandOutcome List(
            [PageRef<ProjectileBanListPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber = 1) {
            var lines = CreateListLines();
            return CommandOutcome.Page(
                pageNumber,
                lines,
                lines.Count,
                CreateListPageSettings());
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch(
            [FromAmbientContext] TSExecutionContext context,
            CommandInvocationContext invocation) => BuildRootMismatchOutcome(
                context,
                invocation,
                Permissions.manageprojectile,
                static _ => Help(),
                "projban");

        private sealed class ProjectileBanHelpPageSource : IPageRefSource<ProjectileBanHelpPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var lines = CreateHelpLines();
                return TSPageRefResolver.CountPages(lines.Count, CreateHelpPageSettings());
            }
        }

        private sealed class ProjectileBanListPageSource : IPageRefSource<ProjectileBanListPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var lines = CreateListLines();
                return lines.Count == 0
                    ? null
                    : TSPageRefResolver.CountPages(lines.Count, CreateListPageSettings());
            }
        }

        private static List<string> CreateHelpLines() {
            return [
                GetString("add <projectile ID> - Adds a projectile ban."),
                GetString("allow <projectile ID> <group> - Allows a group to use a projectile."),
                GetString("del <projectile ID> - Deletes an projectile ban."),
                GetString("disallow <projectile ID> <group> - Disallows a group from using a projectile."),
                GetString("list [page] - Lists all projectile bans.")
            ];
        }

        private static PaginationTools.Settings CreateHelpPageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Projectile Ban Sub-Commands ({{0}}/{{1}}):"),
                FooterFormat = GetString("Type {0}projban help {{0}} for more sub-commands.", Commands.Specifier),
            };
        }

        private static List<string> CreateListLines() {
            return PaginationTools.BuildLinesFromTerms(TShock.ProjectileBans.ProjectileBans.Select(static ban => ban.ID));
        }

        private static PaginationTools.Settings CreateListPageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Projectile bans ({{0}}/{{1}}):"),
                FooterFormat = GetString("Type {0}projban list {{0}} for more.", Commands.Specifier),
                NothingToDisplayString = GetString("There are currently no banned projectiles."),
            };
        }
    }

    [CommandController("tileban", Summary = nameof(ControllerSummary))]
    internal static class TileBanCommand
    {
        private static string AddSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}tileban add <tile id>.", args);
        private static string TileIdInvalidTileMessage => GetString("Invalid tile ID.");
        private static string AllowSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}tileban allow <id> <group>.", args);
        private static string GroupInvalidGroupMessage => GetString("Invalid group.");
        private static string DeleteSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}tileban del <id>.", args);
        private static string DisallowSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}tileban disallow <id> <group name>.", args);

        private static string ControllerSummary => GetString("Manages tile bans.");
        private static string AddSummary => GetString("add <tile ID> - Adds a tile ban.");
        private static string AllowSummary => GetString("allow <tile ID> <group> - Allows a group to place a tile.");
        private static string DeleteSummary => GetString("del <tile ID> - Deletes a tile ban.");
        private static string DisallowSummary => GetString("Disallows a group from placing a banned tile.");
        private static string HelpSummary => GetString("Shows tile ban subcommand help.");
        private static string PageNumberInvalidTokenMessage(params object?[] args) => GetString("\"{0}\" is not a valid page number.", args);
        private static string ListSummary => GetString("list [page] - Lists all tile bans.");

        [CommandAction("add", Summary = nameof(AddSummary))]
        [RequireUserArgumentCountSyntax(1, nameof(AddSyntaxMessage))]
        [TShockCommand(nameof(Permissions.managetile))]
        public static CommandOutcome Add(
            [TileRef(nameof(TileIdInvalidTileMessage))]
            short tileId) {
            TShock.TileBans.AddNewBan((short)tileId);
            return CommandOutcome.Success(GetString("Banned tile {0}.", tileId));
        }

        [CommandAction("allow", Summary = nameof(AllowSummary))]
        [RequireUserArgumentCountSyntax(2, nameof(AllowSyntaxMessage))]
        [TShockCommand(nameof(Permissions.managetile))]
        public static CommandOutcome Allow(
            [TileRef(nameof(TileIdInvalidTileMessage))]
            short tileId,
            [GroupRef(nameof(GroupInvalidGroupMessage), LookupMode = TSLookupMatchMode.ExactOnly)] Group group) {
            var ban = TShock.TileBans.GetBanById((short)tileId);
            if (ban is null) {
                return CommandOutcome.Error(GetString("Tile {0} is not banned.", tileId));
            }

            if (ban.AllowedGroups.Contains(group.Name)) {
                return CommandOutcome.Warning(GetString("{0} is already allowed to place tile {1}.", group.Name, tileId));
            }

            TShock.TileBans.AllowGroup((short)tileId, group.Name);
            return CommandOutcome.Success(GetString("{0} has been allowed to place tile {1}.", group.Name, tileId));
        }

        [CommandAction("del", Summary = nameof(DeleteSummary))]
        [RequireUserArgumentCountSyntax(1, nameof(DeleteSyntaxMessage))]
        [TShockCommand(nameof(Permissions.managetile))]
        public static CommandOutcome Delete(
            [TileRef(nameof(TileIdInvalidTileMessage))]
            short tileId) {
            TShock.TileBans.RemoveBan((short)tileId);
            return CommandOutcome.Success(GetString("Unbanned tile {0}.", tileId));
        }

        [CommandAction("disallow", Summary = nameof(DisallowSummary))]
        [RequireUserArgumentCountSyntax(2, nameof(DisallowSyntaxMessage))]
        [TShockCommand(nameof(Permissions.managetile))]
        public static CommandOutcome Disallow(
            [TileRef(nameof(TileIdInvalidTileMessage))]
            short tileId,
            [GroupRef(nameof(GroupInvalidGroupMessage), LookupMode = TSLookupMatchMode.ExactOnly)] Group group) {
            var ban = TShock.TileBans.GetBanById((short)tileId);
            if (ban is null) {
                return CommandOutcome.Error(GetString("Tile {0} is not banned.", tileId));
            }

            if (!ban.AllowedGroups.Contains(group.Name)) {
                return CommandOutcome.Warning(GetString("{0} is already prevented from placing tile {1}.", group.Name, tileId));
            }

            TShock.TileBans.RemoveGroup((short)tileId, group.Name);
            return CommandOutcome.Success(GetString("{0} has been disallowed from placing tile {1}.", group.Name, tileId));
        }

        [CommandAction("help", Summary = nameof(HelpSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.managetile))]
        public static CommandOutcome Help(
            [PageRef<TileBanHelpPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber = 1) {
            var lines = CreateHelpLines();
            return CommandOutcome.Page(
                pageNumber,
                lines,
                lines.Count,
                CreateHelpPageSettings());
        }

        [CommandAction("list", Summary = nameof(ListSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.managetile))]
        public static CommandOutcome List(
            [PageRef<TileBanListPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber = 1) {
            var lines = CreateListLines();
            return CommandOutcome.Page(
                pageNumber,
                lines,
                lines.Count,
                CreateListPageSettings());
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch(
            [FromAmbientContext] TSExecutionContext context,
            CommandInvocationContext invocation) => BuildRootMismatchOutcome(
                context,
                invocation,
                Permissions.managetile,
                static _ => Help(),
                "tileban");

        private sealed class TileBanHelpPageSource : IPageRefSource<TileBanHelpPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var lines = CreateHelpLines();
                return TSPageRefResolver.CountPages(lines.Count, CreateHelpPageSettings());
            }
        }

        private sealed class TileBanListPageSource : IPageRefSource<TileBanListPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var lines = CreateListLines();
                return lines.Count == 0
                    ? null
                    : TSPageRefResolver.CountPages(lines.Count, CreateListPageSettings());
            }
        }

        private static List<string> CreateHelpLines() {
            return [
                GetString("add <tile ID> - Adds a tile ban."),
                GetString("allow <tile ID> <group> - Allows a group to place a tile."),
                GetString("del <tile ID> - Deletes a tile ban."),
                GetString("disallow <tile ID> <group> - Disallows a group from place a tile."),
                GetString("list [page] - Lists all tile bans.")
            ];
        }

        private static PaginationTools.Settings CreateHelpPageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Tile Ban Sub-Commands ({{0}}/{{1}}):"),
                FooterFormat = GetString("Type {0}tileban help {{0}} for more sub-commands.", Commands.Specifier),
            };
        }

        private static List<string> CreateListLines() {
            return PaginationTools.BuildLinesFromTerms(TShock.TileBans.TileBans.Select(static ban => ban.ID));
        }

        private static PaginationTools.Settings CreateListPageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Tile bans ({{0}}/{{1}}):"),
                FooterFormat = GetString("Type {0}tileban list {{0}} for more.", Commands.Specifier),
                NothingToDisplayString = GetString("There are currently no banned tiles."),
            };
        }
    }

    internal static class RestrictionCommandHelpers
    {
        public static CommandOutcome BuildRootMismatchOutcome(
            TSExecutionContext context,
            CommandInvocationContext invocation,
            string permission,
            Func<CommandInvocationContext, CommandOutcome> helpFactory,
            string rootName) {
            if (!context.Executor.HasPermission(permission)) {
                return CommandOutcome.Error(GetString("You do not have access to this command."));
            }

            return invocation.UserArguments.Length == 0
                ? helpFactory(invocation)
                : CommandOutcome.Error(GetString("Invalid subcommand. Type {0}{1} help for more information on valid subcommands.", Commands.Specifier, rootName));
        }

        public static (string StorageName, string DisplayName) ResolveItemIdentity(int itemId) {
            var item = Utils.GetItemById(itemId);
            var storageName = EnglishLanguage.GetItemNameById(itemId)
                ?? throw new InvalidOperationException($"English item name is unavailable for item id {itemId}.");
            var displayName = string.IsNullOrWhiteSpace(item.Name)
                ? storageName
                : item.Name;

            return (storageName, displayName);
        }
    }
}
