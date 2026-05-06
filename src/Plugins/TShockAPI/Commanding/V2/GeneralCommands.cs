using Rests;
using UnifierTSL.Commanding;

namespace TShockAPI.Commanding.V2
{
    [CommandController("help", Summary = nameof(ControllerSummary))]
    [TSCommandRoot(HelpText = nameof(RootHelpText))]
    internal static class HelpCommand
    {
        private static string ControllerSummary => GetString("Lists commands or gives help on them.");
        private static string RootHelpText => GetString("Lists commands or gives help on them.");
        private static string ExecuteSummary => GetString("Lists commands or shows help for one command.");
        private static string ExecutePageSummary => GetString("Lists commands or shows help for one command.");
        private static string PageNumberInvalidTokenMessage(params object?[] args) => GetString("\"{0}\" is not a valid page number.", args);
        private static string ExecuteTopicSummary => GetString("Lists commands or shows help for one command.");


        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context) {
            return BuildPageOutcome(context, pageNumber: 1);
        }

        [CommandAction(Summary = nameof(ExecutePageSummary))]
        [RequireNumericUserArgument]
        [TShockCommand]
        public static CommandOutcome ExecutePage(
            [FromAmbientContext] TSExecutionContext context,
            [PageRef<HelpPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber) {
            return BuildPageOutcome(context, pageNumber);
        }

        [CommandAction(Summary = nameof(ExecuteTopicSummary))]
        [RejectNumericUserArgument]
        [TShockCommand]
        public static CommandOutcome ExecuteTopic(
            [FromAmbientContext] TSExecutionContext context,
            [CommandRef(Recursive = true)]
            string commandName) {
            if (!TSCommandRefResolver.TryResolveInvocationTarget(commandName, context, out var target)) {
                return CommandOutcome.Error(GetString("Invalid command."));
            }

            var accessFailure = TSCommandRefResolver.ResolveAccessFailure(target, context);
            if (accessFailure is not null) {
                return accessFailure;
            }

            if (target.Action is not null) {
                return BuildActionOutcome(target);
            }

            var command = target.LegacyEntry ?? TSCommandBridge.FindRegisteredCommand(target.CanonicalPath);
            if (command is null) {
                return CommandOutcome.Error(GetString("Invalid command."));
            }

            var builder = CommandOutcome.SuccessBuilder(GetString("{0}{1} help: ", Commands.Specifier, command.PrimaryName));
            if (command.HelpLines.Length == 0) {
                return builder.AddInfo(command.HelpText).Build();
            }

            foreach (var line in command.HelpLines) {
                builder.AddInfo(line);
            }

            return builder.Build();
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}help <command/page>", Commands.Specifier));
        }

        private static CommandOutcome BuildActionOutcome(TSCommandRefTarget target) {
            var usageLines = TSCommandRefResolver.BuildUsageLines(target, Commands.Specifier);
            if (usageLines.Count == 0) {
                return CommandOutcome.Error(GetString("Invalid command."));
            }

            return CommandOutcome.InfoLines([
                GetString("{0}{1} help: ", Commands.Specifier, target.CanonicalPath),
                .. usageLines,
            ]);
        }

        private sealed class HelpPageSource : IPageRefSource<HelpPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var lines = PaginationTools.BuildLinesFromTerms(BuildCommandNames(context.InvocationContext?.ExecutionContext as TSExecutionContext));
                return TSPageRefResolver.CountPages(lines.Count, CreatePageSettings());
            }
        }

        private static CommandOutcome BuildPageOutcome(TSExecutionContext context, int pageNumber) {
            var lines = PaginationTools.BuildLinesFromTerms(BuildCommandNames(context));
            return CommandOutcome.Page(
                pageNumber,
                lines,
                lines.Count,
                CreatePageSettings());
        }

        private static IEnumerable<string> BuildCommandNames(TSExecutionContext? context) {
            return from catalogCommand in TSCommandBridge.GetRegisteredCommandCatalog()
                   where (context is null || catalogCommand.CanRun(context.Executor))
                       && (catalogCommand.PrimaryName != "setup" || TShock.SetupToken != 0)
                   select Commands.Specifier + catalogCommand.PrimaryName;
        }

        private static PaginationTools.Settings CreatePageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Commands ({{0}}/{{1}}):"),
                FooterFormat = GetString("Type {0}help {{0}} for more.", Commands.Specifier),
            };
        }
    }

    [CommandController("rest", Summary = nameof(ControllerSummary))]
    [TSCommandRoot(HelpText = nameof(RootHelpText))]
    internal static class RestCommand
    {
        private static string ControllerSummary => GetString("Manages the REST API.");
        private static string RootHelpText => GetString("Manages the REST API.");
        private static string ExecuteSummary => GetString("Shows REST subcommand help.");
        private static string HelpSummary => GetString("Shows REST subcommand help.");
        private static string ListUsersSummary => GetString("listusers - Lists all REST users and their current active tokens.");
        private static string PageNumberInvalidTokenMessage(params object?[] args) => GetString("\"{0}\" is not a valid page number.", args);
        private static string DestroyTokensSummary => GetString("destroytokens - Destroys all current REST tokens.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(RestPermissions.restmanage)]
        public static CommandOutcome Execute() {
            return BuildHelpOutcome();
        }

        [CommandAction("help", Summary = nameof(HelpSummary))]
        [TShockCommand(RestPermissions.restmanage)]
        public static CommandOutcome Help() {
            return BuildHelpOutcome();
        }

        [CommandAction("listusers", Summary = nameof(ListUsersSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(RestPermissions.restmanage)]
        public static CommandOutcome ListUsers(
            [PageRef<RestUserPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber = 1) {
            var lines = CreateRestUserLines();
            return CommandOutcome.Page(
                pageNumber,
                lines,
                lines.Count,
                CreateRestUserPageSettings());
        }

        [CommandAction("destroytokens", Summary = nameof(DestroyTokensSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(RestPermissions.restmanage)]
        public static CommandOutcome DestroyTokens() {
            TShock.RestApi.Tokens.Clear();
            return CommandOutcome.Success(GetString("All REST tokens have been destroyed."));
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return BuildHelpOutcome();
        }

        private static CommandOutcome BuildHelpOutcome() {
            return CommandOutcome.InfoLines([
                GetString("Available REST Sub-Commands:"),
                GetString("listusers - Lists all REST users and their current active tokens."),
                GetString("destroytokens - Destroys all current REST tokens."),
            ]);
        }

        private sealed class RestUserPageSource : IPageRefSource<RestUserPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var lines = CreateRestUserLines();
                return lines.Count == 0
                    ? null
                    : TSPageRefResolver.CountPages(lines.Count, CreateRestUserPageSettings());
            }
        }

        private static List<string> CreateRestUserLines() {
            Dictionary<string, int> restUsersTokens = [];
            foreach (var tokenData in TShock.RestApi.Tokens.Values) {
                if (restUsersTokens.TryGetValue(tokenData.Username, out var tokenCount)) {
                    restUsersTokens[tokenData.Username] = tokenCount + 1;
                }
                else {
                    restUsersTokens[tokenData.Username] = 1;
                }
            }

            return PaginationTools.BuildLinesFromTerms(restUsersTokens.Select(static entry =>
                GetString("{0} ({1} tokens)", entry.Key, entry.Value)));
        }

        private static PaginationTools.Settings CreateRestUserPageSettings() {
            return new PaginationTools.Settings {
                NothingToDisplayString = GetString("There are currently no active REST users."),
                HeaderFormat = GetString("Active REST Users ({{0}}/{{1}}):"),
                FooterFormat = GetString("Type {0}rest listusers {{0}} for more.", Commands.Specifier),
            };
        }
    }

    [CommandController("displaylogs", Summary = nameof(ControllerSummary))]
    [TSCommandRoot(HelpText = nameof(RootHelpText))]
    internal static class DisplayLogsCommand
    {
        private static string ControllerSummary => GetString("Toggles whether you receive server logs.");
        private static string RootHelpText => GetString("Toggles whether you receive server logs.");
        private static string ExecuteSummary => GetString("Toggles whether the executing player receives server log messages.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.logs), PlayerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            var player = context.Executor.Player;
            player.DisplayLogs = !player.DisplayLogs;
            return CommandOutcome.Success(player.DisplayLogs
                ? GetString("Log display enabled.")
                : GetString("Log display disabled."));
        }

    }

    [CommandController("dump-reference-data", Summary = nameof(ControllerSummary))]
    [TSCommandRoot(HelpText = nameof(RootHelpText))]
    internal static class DumpReferenceDataCommand
    {
        private static string ControllerSummary => GetString("Creates reference tables for Terraria data types and permissions.");
        private static string RootHelpText => GetString("Creates a reference tables for Terraria data types and the TShock permission system in the server folder.");
        private static string ExecuteSummary => GetString("Creates Terraria and permission reference dumps in the server folder.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.createdumps))]
        public static CommandOutcome Execute() {
            Utils.DumpPermissionMatrix("PermissionMatrix.txt");
            Utils.Dump(false);
            return CommandOutcome.Success(GetString("Your reference dumps have been created in the server folder."));
        }
    }
}
