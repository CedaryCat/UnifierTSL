using TShockAPI.ConsolePrompting;
using TShockAPI.DB;
using UnifierTSL.Commanding;
using UnifierTSL.Logging;

namespace TShockAPI.Commanding.V2
{
    [CommandController("group", Summary = nameof(ControllerSummary))]
    internal static class GroupCommand
    {
        private static string AddPermissionsToAllSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}group addperm <group name> <permissions...>.", args);
        private static string AddPermissionsSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}group addperm <group name> <permissions...>.", args);
        private static string GroupInvalidGroupMessage(params object?[] args) => GetString("No such group \"{0}\".", args);
        private static string ColorSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}group color <group name> [new color(000,000,000)].", args);
        private static string DeletePermissionsFromAllSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}group delperm <group name> <permissions...>.", args);
        private static string DeletePermissionsSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}group delperm <group name> <permissions...>.", args);

        private static string ControllerSummary => GetString("Manages TShock permission groups.");
        private static string AddSummary => GetString("Adds a new group.");
        private static string AddPermissionsToAllSummary => GetString("Adds permissions to a group.");
        private static string AddPermissionsSummary => GetString("Adds permissions to a group.");
        private static string HelpSummary => GetString("Shows group subcommand help.");
        private static string PageNumberInvalidTokenMessage(params object?[] args) => GetString("\"{0}\" is not a valid page number.", args);
        private static string ParentSummary => GetString("Reads or changes a group's parent.");
        private static string SuffixSummary => GetString("Reads or changes a group's suffix.");
        private static string PrefixSummary => GetString("Reads or changes a group's prefix.");
        private static string ColorSummary => GetString("Reads or changes a group's chat color.");
        private static string RenameSummary => GetString("Renames a group.");
        private static string DeleteSummary => GetString("Deletes a group.");
        private static string DeletePermissionsFromAllSummary => GetString("Removes permissions from a group.");
        private static string DeletePermissionsSummary => GetString("Removes permissions from a group.");
        private static string ListSummary => GetString("Lists groups.");
        private static string ListPermissionsSummary => GetString("Lists a group's permissions.");

        // Keep management actions declarative: `RemainingArgs` preserves legacy token arrays,
        // and exact-only refs keep manager-backed names off the prefix-binding path.
        [CommandAction("add", Summary = nameof(AddSummary))]
        [TShockCommand(nameof(Permissions.managegroup))]
        public static CommandOutcome Add(
            [CommandParam(Name = "group")]
            string groupName = "",
            [RemainingArgs(Name = "permissions")]
            string[]? permissions = null) {

            if (string.IsNullOrWhiteSpace(groupName)) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}group add <group name> [permissions].", Commands.Specifier));
            }

            permissions ??= [];

            try {
                TShock.Groups.AddGroup(groupName, null, string.Join(",", permissions), Group.defaultChatColor);
                return CommandOutcome.Success(GetString("Group {0} was added successfully.", groupName));
            }
            catch (GroupExistsException) {
                return CommandOutcome.Error(GetString("A group with the same name already exists."));
            }
            catch (GroupManagerException ex) {
                return CommandOutcome.Error(ex.ToString());
            }
        }

        [CommandAction("addperm", Summary = nameof(AddPermissionsToAllSummary))]
        [RequireUserArgumentCountSyntax(2, int.MaxValue, nameof(AddPermissionsToAllSyntaxMessage))]
        [TShockCommand(nameof(Permissions.managegroup))]
        public static CommandOutcome AddPermissionsToAll(
            [CommandPromptSemantic<TSCommandPromptParamKeys>(nameof(TSCommandPromptParamKeys.GroupRef))]
            [CommandLiteral("*")]
            [CommandParam(Name = "group")]
            string allGroups,
            [RemainingArgs(Name = "permissions")]
            params string[] permissions) {
            List<string> permissionTerms = [.. permissions];
            if (permissionTerms.Count == 0) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}group addperm <group name> <permissions...>.", Commands.Specifier));
            }

            foreach (var group in TShock.Groups) {
                TShock.Groups.AddPermissions(group.Name, permissionTerms);
            }

            return CommandOutcome.Success(GetString("The permissions have been added to all of the groups in the system."));
        }

        [CommandAction("addperm", Summary = nameof(AddPermissionsSummary))]
        [RequireUserArgumentCountSyntax(2, int.MaxValue, nameof(AddPermissionsSyntaxMessage))]
        [TShockCommand(nameof(Permissions.managegroup))]
        public static CommandOutcome AddPermissions(
            [GroupRef(nameof(GroupInvalidGroupMessage), Name = "group", LookupMode = TSLookupMatchMode.ExactOnly)]
            Group? group = null,
            [RemainingArgs(Name = "permissions")]
            params string[] permissions) {
            List<string> permissionTerms = [.. permissions];
            if (group is null || permissionTerms.Count == 0) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}group addperm <group name> <permissions...>.", Commands.Specifier));
            }

            try {
                var response = TShock.Groups.AddPermissions(group.Name, permissionTerms);
                return response.Length > 0
                    ? CommandOutcome.Success(response)
                    : CommandOutcome.Empty;
            }
            catch (GroupManagerException ex) {
                return CommandOutcome.Error(ex.ToString());
            }
        }

        [CommandAction("help", Summary = nameof(HelpSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.managegroup))]
        public static CommandOutcome Help(
            [PageRef<GroupHelpPageSource>(
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

        [CommandAction("parent", Summary = nameof(ParentSummary))]
        [TShockCommand(nameof(Permissions.managegroup))]
        public static CommandOutcome Parent(
            [GroupRef(nameof(GroupInvalidGroupMessage), LookupMode = TSLookupMatchMode.ExactOnly)]
            Group? group = null,
            [RemainingText] string? newParentGroupName = null) {

            if (group is null) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}group parent <group name> [new parent group name].", Commands.Specifier));
            }

            if (newParentGroupName is null) {
                return group.Parent is not null
                    ? CommandOutcome.Success(GetString("Parent of \"{0}\" is \"{1}\".", group.Name, group.Parent.Name))
                    : CommandOutcome.Success(GetString("Group \"{0}\" has no parent.", group.Name));
            }

            if (!string.IsNullOrWhiteSpace(newParentGroupName) && !TShock.Groups.GroupExists(newParentGroupName)) {
                return CommandOutcome.Error(GetString("No such group \"{0}\".", newParentGroupName));
            }

            try {
                TShock.Groups.UpdateGroup(group.Name, newParentGroupName, group.Permissions, group.ChatColor, group.Suffix, group.Prefix);
                return !string.IsNullOrWhiteSpace(newParentGroupName)
                    ? CommandOutcome.Success(GetString("Parent of group \"{0}\" set to \"{1}\".", group.Name, newParentGroupName))
                    : CommandOutcome.Success(GetString("Removed parent of group \"{0}\".", group.Name));
            }
            catch (GroupManagerException ex) {
                return CommandOutcome.Error(ex.Message);
            }
        }

        [CommandAction("suffix", Summary = nameof(SuffixSummary))]
        [TShockCommand(nameof(Permissions.managegroup))]
        public static CommandOutcome Suffix(
            [GroupRef(nameof(GroupInvalidGroupMessage), LookupMode = TSLookupMatchMode.ExactOnly)]
            Group? group = null,
            [RemainingText] string? newSuffix = null) {

            if (group is null) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}group suffix <group name> [new suffix].", Commands.Specifier));
            }

            if (newSuffix is null) {
                return !string.IsNullOrWhiteSpace(group.Suffix)
                    ? CommandOutcome.Success(GetString("Suffix of \"{0}\" is \"{1}\".", group.Name, group.Suffix))
                    : CommandOutcome.Success(GetString("Group \"{0}\" has no suffix.", group.Name));
            }

            try {
                TShock.Groups.UpdateGroup(group.Name, group.ParentName, group.Permissions, group.ChatColor, newSuffix, group.Prefix);
                return !string.IsNullOrWhiteSpace(newSuffix)
                    ? CommandOutcome.Success(GetString("Suffix of group \"{0}\" set to \"{1}\".", group.Name, newSuffix))
                    : CommandOutcome.Success(GetString("Removed suffix of group \"{0}\".", group.Name));
            }
            catch (GroupManagerException ex) {
                return CommandOutcome.Error(ex.Message);
            }
        }

        [CommandAction("prefix", Summary = nameof(PrefixSummary))]
        [TShockCommand(nameof(Permissions.managegroup))]
        public static CommandOutcome Prefix(
            [GroupRef(nameof(GroupInvalidGroupMessage), LookupMode = TSLookupMatchMode.ExactOnly)]
            Group? group = null,
            [RemainingText] string? newPrefix = null) {

            if (group is null) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}group prefix <group name> [new prefix].", Commands.Specifier));
            }

            if (newPrefix is null) {
                return !string.IsNullOrWhiteSpace(group.Prefix)
                    ? CommandOutcome.Success(GetString("Prefix of \"{0}\" is \"{1}\".", group.Name, group.Prefix))
                    : CommandOutcome.Success(GetString("Group \"{0}\" has no prefix.", group.Name));
            }

            try {
                TShock.Groups.UpdateGroup(group.Name, group.ParentName, group.Permissions, group.ChatColor, group.Suffix, newPrefix);
                return !string.IsNullOrWhiteSpace(newPrefix)
                    ? CommandOutcome.Success(GetString("Prefix of group \"{0}\" set to \"{1}\".", group.Name, newPrefix))
                    : CommandOutcome.Success(GetString("Removed prefix of group \"{0}\".", group.Name));
            }
            catch (GroupManagerException ex) {
                return CommandOutcome.Error(ex.Message);
            }
        }

        [CommandAction("color", Summary = nameof(ColorSummary))]
        [RequireUserArgumentCountSyntax(1, 2, nameof(ColorSyntaxMessage))]
        [TShockCommand(nameof(Permissions.managegroup))]
        public static CommandOutcome Color(
            [GroupRef(nameof(GroupInvalidGroupMessage), LookupMode = TSLookupMatchMode.ExactOnly)]
            Group? group = null,
            [RemainingText] string? newColor = null) {

            if (group is null) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}group color <group name> [new color(000,000,000)].", Commands.Specifier));
            }

            if (newColor is null) {
                return CommandOutcome.Success(GetString("Chat color for \"{0}\" is \"{1}\".", group.Name, group.ChatColor));
            }

            var parts = newColor.Split(',');
            if (parts.Length != 3
                || !byte.TryParse(parts[0], out _)
                || !byte.TryParse(parts[1], out _)
                || !byte.TryParse(parts[2], out _)) {
                return CommandOutcome.Error(GetString("Invalid syntax for color, expected \"rrr,ggg,bbb\"."));
            }

            try {
                TShock.Groups.UpdateGroup(group.Name, group.ParentName, group.Permissions, newColor, group.Suffix, group.Prefix);
                return CommandOutcome.Success(GetString("Chat color for group \"{0}\" set to \"{1}\".", group.Name, newColor));
            }
            catch (GroupManagerException ex) {
                return CommandOutcome.Error(ex.Message);
            }
        }

        [CommandAction("rename", Summary = nameof(RenameSummary))]
        [TShockCommand(nameof(Permissions.managegroup))]
        public static CommandOutcome Rename(
            [GroupRef(LookupMode = TSLookupMatchMode.ExactOnly)]
            Group? group = null,
            [RemainingText] string? newName = null) {

            if (group is null || string.IsNullOrWhiteSpace(newName)) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}group rename <group> <new name>.", Commands.Specifier));
            }

            try {
                var response = TShock.Groups.RenameGroup(group.Name, newName);
                return CommandOutcome.Success(response);
            }
            catch (GroupManagerException ex) {
                return CommandOutcome.Error(ex.Message);
            }
        }

        [CommandAction("del", Summary = nameof(DeleteSummary))]
        [TShockCommand(nameof(Permissions.managegroup))]
        public static CommandOutcome Delete(
            [GroupRef(Name = "group", LookupMode = TSLookupMatchMode.ExactOnly)] Group? group = null) {

            if (group is null) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}group del <group name>.", Commands.Specifier));
            }

            try {
                var response = TShock.Groups.DeleteGroup(group.Name, true);
                return response.Length > 0
                    ? CommandOutcome.Success(response)
                    : CommandOutcome.Empty;
            }
            catch (GroupManagerException ex) {
                return CommandOutcome.Error(ex.Message);
            }
        }

        [CommandAction("delperm", Summary = nameof(DeletePermissionsFromAllSummary))]
        [RequireUserArgumentCountSyntax(2, int.MaxValue, nameof(DeletePermissionsFromAllSyntaxMessage))]
        [TShockCommand(nameof(Permissions.managegroup))]
        public static CommandOutcome DeletePermissionsFromAll(
            [CommandPromptSemantic<TSCommandPromptParamKeys>(nameof(TSCommandPromptParamKeys.GroupRef))]
            [CommandLiteral("*")]
            [CommandParam(Name = "group")]
            string allGroups,
            [RemainingArgs(Name = "permissions")]
            params string[] permissions) {
            List<string> permissionTerms = [.. permissions];
            if (permissionTerms.Count == 0) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}group delperm <group name> <permissions...>.", Commands.Specifier));
            }

            foreach (var group in TShock.Groups) {
                TShock.Groups.DeletePermissions(group.Name, permissionTerms);
            }

            return CommandOutcome.Success(GetString("The permissions have been removed from all of the groups in the system."));
        }

        [CommandAction("delperm", Summary = nameof(DeletePermissionsSummary))]
        [RequireUserArgumentCountSyntax(2, int.MaxValue, nameof(DeletePermissionsSyntaxMessage))]
        [TShockCommand(nameof(Permissions.managegroup))]
        public static CommandOutcome DeletePermissions(
            [GroupRef(nameof(GroupInvalidGroupMessage), Name = "group", LookupMode = TSLookupMatchMode.ExactOnly)]
            Group? group = null,
            [RemainingArgs(Name = "permissions")]
            params string[] permissions) {
            List<string> permissionTerms = [.. permissions];
            if (group is null || permissionTerms.Count == 0) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}group delperm <group name> <permissions...>.", Commands.Specifier));
            }

            try {
                var response = TShock.Groups.DeletePermissions(group.Name, permissionTerms);
                return response.Length > 0
                    ? CommandOutcome.Success(response)
                    : CommandOutcome.Empty;
            }
            catch (GroupManagerException ex) {
                return CommandOutcome.Error(ex.Message);
            }
        }

        [CommandAction("list", Summary = nameof(ListSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.managegroup))]
        public static CommandOutcome List(
            [PageRef<GroupListPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber = 1) {
            var lines = CreateGroupListLines();
            return CommandOutcome.Page(
                pageNumber,
                lines,
                lines.Count,
                CreateGroupListPageSettings());
        }

        [CommandAction("listperm", Summary = nameof(ListPermissionsSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.managegroup))]
        public static CommandOutcome ListPermissions(
            [GroupRef(LookupMode = TSLookupMatchMode.ExactOnly)]
            Group? group = null,
            [PageRef<GroupPermissionPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber = 1) {
            if (group is null) {
                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}group listperm <group name> [page].", Commands.Specifier));
            }

            var lines = CreateGroupPermissionLines(group);
            return CommandOutcome.Page(
                pageNumber,
                lines,
                lines.Count,
                CreateGroupPermissionPageSettings(group.Name));
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch(CommandInvocationContext invocation) {
            return invocation.UserArguments.Length == 0
                ? Help()
                : CommandOutcome.Error(GetString("Invalid subcommand! Type {0}group help for more information on valid commands.", Commands.Specifier));
        }

        private sealed class GroupHelpPageSource : IPageRefSource<GroupHelpPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var lines = CreateHelpLines();
                return TSPageRefResolver.CountPages(lines.Count, CreateHelpPageSettings());
            }
        }

        private sealed class GroupListPageSource : IPageRefSource<GroupListPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var lines = CreateGroupListLines();
                return lines.Count == 0
                    ? null
                    : TSPageRefResolver.CountPages(lines.Count, CreateGroupListPageSettings());
            }
        }

        private sealed class GroupPermissionPageSource : IPageRefSource<GroupPermissionPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var groupName = context.InvocationContext?.UserArguments.Length > 0
                    ? context.InvocationContext.UserArguments[0]
                    : null;
                var group = string.IsNullOrWhiteSpace(groupName)
                    ? null
                    : TShock.Groups.GetGroupByName(groupName);
                if (group is null) {
                    return null;
                }

                var lines = CreateGroupPermissionLines(group);
                return lines.Count == 0
                    ? null
                    : TSPageRefResolver.CountPages(lines.Count, CreateGroupPermissionPageSettings(group.Name));
            }
        }

        private static List<string> CreateHelpLines() {
            return [
                GetString("add <name> <permissions...> - Adds a new group."),
                GetString("addperm <group> <permissions...> - Adds permissions to a group."),
                GetString("color <group> <rrr,ggg,bbb> - Changes a group's chat color."),
                GetString("rename <group> <new name> - Changes a group's name."),
                GetString("del <group> - Deletes a group."),
                GetString("delperm <group> <permissions...> - Removes permissions from a group."),
                GetString("list [page] - Lists groups."),
                GetString("listperm <group> [page] - Lists a group's permissions."),
                GetString("parent <group> <parent group> - Changes a group's parent group."),
                GetString("prefix <group> <prefix> - Changes a group's prefix."),
                GetString("suffix <group> <suffix> - Changes a group's suffix."),
            ];
        }

        private static PaginationTools.Settings CreateHelpPageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Group Sub-Commands ({0}/{1}):"),
                FooterFormat = GetString("Type {0}group help {{0}} for more sub-commands.", Commands.Specifier),
            };
        }

        private static List<string> CreateGroupListLines() {
            return PaginationTools.BuildLinesFromTerms(TShock.Groups.groups.Select(static group => group.Name));
        }

        private static PaginationTools.Settings CreateGroupListPageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Groups ({0}/{1}):"),
                FooterFormat = GetString("Type {0}group list {{0}} for more.", Commands.Specifier),
            };
        }

        private static List<string> CreateGroupPermissionLines(Group group) {
            return PaginationTools.BuildLinesFromTerms(group.TotalPermissions);
        }

        private static PaginationTools.Settings CreateGroupPermissionPageSettings(string groupName) {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Permissions for {0} ({{0}}/{{1}}):", groupName),
                FooterFormat = GetString("Type {0}group listperm {1} {{0}} for more.", Commands.Specifier, groupName),
                NothingToDisplayString = GetString("There are currently no permissions for {0}.", groupName),
            };
        }

    }

    [CommandController("user", Summary = nameof(ControllerSummary))]
    [TSCommandRoot(DoLog = false)]
    internal static class UserCommand
    {
        private static string GroupInvalidGroupMessage(params object?[] args) => GetString("Group {0} does not exist.", args);
        private static string AccountInvalidUserAccountMessage(params object?[] args) => GetString("User {0} does not exist.", args);

        private static string ControllerSummary => GetString("Manages user accounts.");
        private static string AddSummary => GetString("Adds a user account.");
        private static string DeleteSummary => GetString("Deletes a user account.");
        private static string PasswordSummary => GetString("Changes a user's password.");
        private static string GroupSummary => GetString("Changes a user's group.");
        private static string HelpSummary => GetString("Shows user command help.");

        [CommandAction("add", Summary = nameof(AddSummary))]
        [TShockCommand(nameof(Permissions.user))]
        public static CommandOutcome Add(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "username")]
            string username = "",
            string password = "",
            [GroupRef(nameof(GroupInvalidGroupMessage), Name = "group", LookupMode = TSLookupMatchMode.ExactOnly)] Group? group = null) {

            if (string.IsNullOrWhiteSpace(username)
                || string.IsNullOrWhiteSpace(password)
                || group is null) {
                return CommandOutcome.Error(GetString("Invalid user syntax. Try {0}user help.", Commands.Specifier));
            }

            UserAccount account = new() {
                Name = username,
                Group = group.Name,
            };

            try {
                account.CreateBCryptHash(password);
            }
            catch (ArgumentOutOfRangeException) {
                return CommandOutcome.Error(GetString("Password must be greater than or equal to {0} characters.", TShock.Config.GlobalSettings.MinimumPasswordLength));
            }

            try {
                TShock.UserAccounts.AddUserAccount(account);
                var builder = CommandOutcome.SuccessBuilder(GetString("Account {0} has been added to group {1}.", account.Name, account.Group));
                builder.AddLog(LogLevel.Info, GetString("{0} added account {1} to group {2}.", context.Executor.Name, account.Name, account.Group));
                return builder.Build();
            }
            catch (GroupNotExistsException) {
                return CommandOutcome.Error(GetString("Group {0} does not exist.", account.Group));
            }
            catch (UserAccountExistsException) {
                return CommandOutcome.Error(GetString("User {0} already exists.", account.Name));
            }
            catch (UserAccountManagerException e) {
                var builder = CommandOutcome.ErrorBuilder(GetString("User {0} could not be added, check console for details.", account.Name));
                builder.AddLog(LogLevel.Error, e.ToString());
                return builder.Build();
            }
        }

        [CommandAction("del", Summary = nameof(DeleteSummary))]
        [TShockCommand(nameof(Permissions.user))]
        public static CommandOutcome Delete(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "username")] string username = "") {

            if (string.IsNullOrWhiteSpace(username)) {
                return CommandOutcome.Error(GetString("Invalid user syntax. Try {0}user help.", Commands.Specifier));
            }

            UserAccount account = new UserAccount { Name = username };
            try {
                TShock.UserAccounts.RemoveUserAccount(account);
                var builder = CommandOutcome.SuccessBuilder(GetString("Account removed successfully."));
                builder.AddLog(LogLevel.Info, GetString("{0} successfully deleted account: {1}.", context.Executor.Name, account.Name));
                return builder.Build();
            }
            catch (UserAccountNotExistException) {
                return CommandOutcome.Error(GetString("The user {0} does not exist! Therefore, the account was not deleted.", account.Name));
            }
            catch (UserAccountManagerException ex) {
                var builder = CommandOutcome.ErrorBuilder(ex.Message);
                builder.AddLog(LogLevel.Error, ex.ToString());
                return builder.Build();
            }
        }

        [CommandAction("password", Summary = nameof(PasswordSummary))]
        [TShockCommand(nameof(Permissions.user))]
        public static CommandOutcome Password(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "username")] string username = "",
            string newPassword = "") {

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(newPassword)) {
                return CommandOutcome.Error(GetString("Invalid user syntax. Try {0}user help.", Commands.Specifier));
            }

            UserAccount account = new UserAccount { Name = username };
            try {
                TShock.UserAccounts.SetUserAccountPassword(account, newPassword);
                var builder = CommandOutcome.SuccessBuilder(GetString("Password change succeeded for {0}.", account.Name));
                builder.AddLog(LogLevel.Info, GetString("{0} changed the password for account {1}", context.Executor.Name, account.Name));
                return builder.Build();
            }
            catch (UserAccountNotExistException) {
                return CommandOutcome.Error(GetString("Account {0} does not exist! Therefore, the password cannot be changed.", account.Name));
            }
            catch (ArgumentOutOfRangeException) {
                return CommandOutcome.Error(GetString("Password must be greater than or equal to {0} characters.", TShock.Config.GlobalSettings.MinimumPasswordLength));
            }
            catch (UserAccountManagerException e) {
                var builder = CommandOutcome.ErrorBuilder(GetString("Password change attempt for {0} failed for an unknown reason. Check the server console for more details.", account.Name));
                builder.AddLog(LogLevel.Error, e.ToString());
                return builder.Build();
            }
        }

        [CommandAction("group", Summary = nameof(GroupSummary))]
        [TShockCommand(nameof(Permissions.user))]
        public static CommandOutcome Group(
            [FromAmbientContext] TSExecutionContext context,
            [UserAccountRef(nameof(AccountInvalidUserAccountMessage), Name = "username", LookupMode = TSLookupMatchMode.ExactOnly)]
            UserAccount? account = null,
            [GroupRef(nameof(GroupInvalidGroupMessage), Name = "group", LookupMode = TSLookupMatchMode.ExactOnly)] Group? group = null) {
            if (account is null || group is null) {
                return CommandOutcome.Error(GetString("Invalid user syntax. Try {0}user help.", Commands.Specifier));
            }

            try {
                TShock.UserAccounts.SetUserGroup(context.Executor.Player, account, group.Name);
                var builder = CommandOutcome.SuccessBuilder(GetString("Account {0} has been changed to group {1}.", account.Name, group.Name));
                builder.AddLog(LogLevel.Info, GetString("{0} changed account {1} to group {2}.", context.Executor.Name, account.Name, group.Name));

                TSPlayer? player = TShock.Players.FirstOrDefault(player => player is not null && player.Account?.Name == account.Name);
                if (player is not null && !context.Silent) {
                    builder.AddPlayerSuccess(
                        player,
                        GetString("{0} has changed your group to {1}.", context.Executor.Name, group.Name));
                }

                return builder.Build();
            }
            catch (GroupNotExistsException) {
                return CommandOutcome.Error(GetString("That group does not exist."));
            }
            catch (UserAccountNotExistException) {
                return CommandOutcome.Error(GetString("User {0} does not exist.", account.Name));
            }
            catch (UserGroupUpdateLockedException) {
                return CommandOutcome.Error(GetString("Hook blocked the attempt to change the user group."));
            }
            catch (UserAccountManagerException e) {
                var builder = CommandOutcome.ErrorBuilder(GetString("User {0} could not be added. Check console for details.", account.Name));
                builder.AddLog(LogLevel.Error, e.ToString());
                return builder.Build();
            }
        }

        [CommandAction("help", Summary = nameof(HelpSummary))]
        [TShockCommand(nameof(Permissions.user))]
        public static CommandOutcome Help([RemainingText] string _ = "") {
            return CommandOutcome.InfoLines([
                GetString("User management command help:"),
                GetString("{0}user add username password group   -- Adds a specified user", Commands.Specifier),
                GetString("{0}user del username                  -- Removes a specified user", Commands.Specifier),
                GetString("{0}user password username newpassword -- Changes a user's password", Commands.Specifier),
                GetString("{0}user group username newgroup       -- Changes a user's group", Commands.Specifier),
            ]);
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Invalid user syntax. Try {0}user help.", Commands.Specifier));
        }
    }

    [CommandController("whitelist", Summary = nameof(ControllerSummary))]
    internal static class WhitelistCommand
    {
        private static string ExecuteSyntaxMessage(params object?[] args) => GetString("Invalid Whitelist syntax. Usage: {0}whitelist <ip[/range]>", args);

        private static string ControllerSummary => GetString("Adds IP rules to the whitelist.");
        private static string ExecuteSummary => GetString("Adds an address or range to the whitelist.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [RequireUserArgumentCountSyntax(1, nameof(ExecuteSyntaxMessage))]
        [TShockCommand(nameof(Permissions.whitelist))]
        public static CommandOutcome Execute([RemainingText] string ipOrRange = "") {
            if (string.IsNullOrWhiteSpace(ipOrRange)) {
                return CommandOutcome.Error(GetString("Invalid Whitelist syntax. Usage: {0}whitelist <ip[/range]>", Commands.Specifier));
            }

            var builder = CommandOutcome.CreateBuilder();
            if (ipOrRange.Contains(':')) {
                builder.AddWarning(GetString(
                    "IPv6 addresses are not supported as of yet by TShock. This rule will have no effect for now. Adding anyways."));
            }

            if (TShock.Whitelist.AddToWhitelist(ipOrRange)) {
                builder.AddSuccess(GetString("Added {0} to the whitelist.", ipOrRange));
            }
            else {
                builder.AddError(GetString("Failed to add {0} to the whitelist. Perhaps it is already whitelisted?", ipOrRange));
            }

            return builder.Build();
        }
    }
}
