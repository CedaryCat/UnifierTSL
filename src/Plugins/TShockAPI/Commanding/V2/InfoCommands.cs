using System.Diagnostics;
using Terraria.GameContent;
using UnifierTSL.Commanding;

namespace TShockAPI.Commanding.V2
{
    [Flags]
    internal enum PlayingFlags
    {
        None = 0,
        [CommandFlag("-i")]
        IncludeIds = 1 << 0,
    }

    [CommandController("version", Summary = nameof(ControllerSummary))]
    internal static class VersionCommand
    {
        private static string ControllerSummary => GetString("Shows the TShock version.");
        private static string ExecuteSummary => GetString("Shows the TShock version.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.maintenance))]
        public static CommandOutcome Execute() {
            return CommandOutcome.Info(
                GetString($"TShock: {TShock.VersionNum.Color(Utils.BoldHighlight)} {TShock.VersionCodename.Color(Utils.RedHighlight)}."));
        }
    }

    [CommandController("motd", Summary = nameof(ControllerSummary))]
    internal static class MotdCommand
    {
        private static string ControllerSummary => GetString("Shows the message of the day.");
        private static string ExecuteSummary => GetString("Shows the message of the day.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand]
        public static CommandOutcome Execute() {
            return CommandOutcome.FileText(FileTools.MotdPath);
        }
    }

    [CommandController("rules", Summary = nameof(ControllerSummary))]
    internal static class RulesCommand
    {
        private static string ControllerSummary => GetString("Shows the server's rules.");
        private static string ExecuteSummary => GetString("Shows the server's rules.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand]
        public static CommandOutcome Execute() {
            return CommandOutcome.FileText(FileTools.RulesPath);
        }
    }

    [CommandController("serverinfo", Summary = nameof(ControllerSummary))]
    internal static class ServerInfoCommand
    {
        private static string ControllerSummary => GetString("Shows host process information.");
        private static string ExecuteSummary => GetString("Shows host process information.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.serverinfo))]
        public static CommandOutcome Execute() {
            var process = Process.GetCurrentProcess();
            return CommandOutcome.InfoLines([
                GetString($"Memory usage: {process.WorkingSet64}"),
                GetString($"Allocated memory: {process.VirtualMemorySize64}"),
                GetString($"Total processor time: {process.TotalProcessorTime}"),
                GetString($"Operating system: {Environment.OSVersion}"),
                GetString($"Proc count: {Environment.ProcessorCount}"),
                GetString($"Machine name: {Environment.MachineName}"),
            ]);
        }
    }

    [CommandController("aliases", Summary = nameof(ControllerSummary))]
    internal static class AliasesCommand
    {
        private static string ControllerSummary => GetString("Shows a command's aliases.");
        private static string ExecuteSummary => GetString("Shows a command's aliases.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand]
        public static CommandOutcome Execute([RemainingText] string commandOrAlias = "") {
            if (string.IsNullOrWhiteSpace(commandOrAlias)) {
                return CommandOutcome.Usage(GetString("aliases <command or alias>"));
            }

            var commandName = commandOrAlias.Trim();
            if (commandName.StartsWith(Commands.Specifier, StringComparison.OrdinalIgnoreCase)) {
                commandName = commandName[Commands.Specifier.Length..];
            }
            else if (commandName.StartsWith(Commands.SilentSpecifier, StringComparison.OrdinalIgnoreCase)) {
                commandName = commandName[Commands.SilentSpecifier.Length..];
            }

            IReadOnlyList<TSCommandCatalogEntry> matchingCommands = TSCommandBridge.FindRegisteredCommands(commandName);
            if (matchingCommands.Count == 0) {
                return CommandOutcome.Error(GetString("No command or command alias matching \"{0}\" found.", commandOrAlias.Trim()));
            }

            return CommandOutcome.InfoLines(matchingCommands.Select(command =>
                command.Aliases.Length > 0
                    ? GetString("Aliases of {0}{1}: {0}{2}", Commands.Specifier, command.PrimaryName, string.Join($", {Commands.Specifier}", command.Aliases))
                    : GetString("{0}{1} defines no aliases.", Commands.Specifier, command.PrimaryName)));
        }
    }

    [CommandController("playing", Summary = nameof(ControllerSummary))]
    [Aliases("online", "who")]
    internal static class PlayingCommand
    {
        private static string ControllerSummary => GetString("Lists connected players.");
        private static string ExecuteSummary => GetString("Lists connected players.");
        private static string PageInvalidTokenMessage(params object?[] args) => GetString("\"{0}\" is not a valid page number.", args);

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [PageRef<PlayingPageSource>(
                InvalidTokenMessage = nameof(PageInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int page = 1,
            [CommandFlags<PlayingFlags>] PlayingFlags flags = PlayingFlags.None) {
            return BuildOutcome(
                context.Executor,
                displayIds: (flags & PlayingFlags.IncludeIds) != 0,
                page);
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() => BuildUsageOutcome();

        private static CommandOutcome BuildOutcome(CommandExecutor executor, bool displayIds, int page) {
            if (displayIds && !executor.HasPermission(Permissions.seeids)) {
                return CommandOutcome.Error(GetString("You do not have permission to see player IDs."));
            }

            var players = CreatePlayerEntries(displayIds);
            if (players.Count == 0) {
                return CommandOutcome.Info(GetString("There are currently no players online."));
            }

            CommandOutcome.Builder builder = CommandOutcome.InfoBuilder(GetString(
                $"Online Players ({players.Count.Color(Utils.GreenHighlight)}/{TShock.Config.GlobalSettings.MaxSlots})"));
            var lines = PaginationTools.BuildLinesFromTerms(players);
            builder.AddPage(
                page,
                lines,
                lines.Count,
                CreatePlayingPageSettings(displayIds));
            return builder.Build();
        }

        private static CommandOutcome BuildUsageOutcome() {
            return CommandOutcome.InfoLines([
                GetString("List Online Players Syntax"),
                GetString($"{"playing".Color(Utils.BoldHighlight)} {"[-i]".Color(Utils.RedHighlight)} {"[page]".Color(Utils.GreenHighlight)}"),
                GetString($"Command aliases: {"playing".Color(Utils.GreenHighlight)}, {"online".Color(Utils.GreenHighlight)}, {"who".Color(Utils.GreenHighlight)}"),
                GetString($"Example usage: {"who".Color(Utils.BoldHighlight)} {"-i".Color(Utils.RedHighlight)}"),
            ]);
        }

        private static List<string> CreatePlayerEntries(bool displayIds) {
            List<string> players = [];
            foreach (TSPlayer? player in TShock.Players) {
                if (player is null || !player.Active || !player.FinishedHandshake) {
                    continue;
                }

                if (displayIds) {
                    players.Add(player.Account is null
                        ? GetString($"{player.Name} (Index: {player.Index})")
                        : GetString($"{player.Name} (Index: {player.Index}, Account ID: {player.Account.ID})"));
                }
                else {
                    players.Add(player.Name);
                }
            }

            return players;
        }

        private static PaginationTools.Settings CreatePlayingPageSettings(bool displayIds) {
            return new PaginationTools.Settings {
                IncludeHeader = false,
                FooterFormat = GetString($"Type {Commands.Specifier}who {(displayIds ? "-i" : string.Empty)} for more."),
            };
        }

        private sealed class PlayingPageSource : IPageRefSource<PlayingPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var displayIds = context.InvocationContext?.RecognizedFlags.Any(flag =>
                    flag.Equals("-i", StringComparison.OrdinalIgnoreCase)) == true;
                var lines = PaginationTools.BuildLinesFromTerms(CreatePlayerEntries(displayIds));
                return lines.Count == 0
                    ? null
                    : TSPageRefResolver.CountPages(lines.Count, CreatePlayingPageSettings(displayIds));
            }
        }
    }

    internal static class InfoCommandHelpers
    {
        public static CommandOutcome BuildDeathRankOutcome(
            Func<TSPlayer, int> selector,
            Func<string, int, string> createLine) {
            List<TSPlayer> players = [.. TShock.Players
                .Where(static player => player is { Active: true })
                .Cast<TSPlayer>()
                .OrderByDescending(selector)];
            if (players.Count == 0) {
                return CommandOutcome.Error(GetString("There are currently no players online."));
            }

            List<string> lines = [.. players.Select(player => createLine(player.Name, selector(player)))];
            return CommandOutcome.Info(string.Join('\n', lines));
        }
    }

    [CommandController("death", Summary = nameof(ControllerSummary))]
    internal static class DeathCommand
    {
        private static string ControllerSummary => GetString("Shows your number of deaths.");
        private static string ExecuteSummary => GetString("Shows your number of PvE deaths.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(PlayerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            return CommandOutcome.Info(GetString("*You were slain {0} times.", context.Executor.Player.DeathsPVE));
        }
    }

    [CommandController("pvpdeath", Summary = nameof(ControllerSummary))]
    internal static class PvpDeathCommand
    {
        private static string ControllerSummary => GetString("Shows your number of PVP deaths.");
        private static string ExecuteSummary => GetString("Shows your number of PVP deaths.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(PlayerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            return CommandOutcome.Info(GetString("*You were slain by other players {0} times.", context.Executor.Player.DeathsPVP));
        }
    }

    [CommandController("alldeath", Summary = nameof(ControllerSummary))]
    internal static class AllDeathCommand
    {
        private static string ControllerSummary => GetString("Shows the number of deaths for all online players.");
        private static string ExecuteSummary => GetString("Shows PvE death counts for all online players.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand]
        public static CommandOutcome Execute() {
            return InfoCommandHelpers.BuildDeathRankOutcome(
                static player => player.DeathsPVE,
                static (playerName, deathCount) => GetString("*{0} was slain {1} times.", playerName, deathCount));
        }
    }

    [CommandController("allpvpdeath", Summary = nameof(ControllerSummary))]
    internal static class AllPvpDeathCommand
    {
        private static string ControllerSummary => GetString("Shows the number of PVP deaths for all online players.");
        private static string ExecuteSummary => GetString("Shows the number of PVP deaths for all online players.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand]
        public static CommandOutcome Execute() {
            return InfoCommandHelpers.BuildDeathRankOutcome(
                static player => player.DeathsPVP,
                static (playerName, deathCount) => GetString("*{0} was slain by other players {1} times.", playerName, deathCount));
        }
    }

    [CommandController("bossdamage", Summary = nameof(ControllerSummary))]
    internal static class BossDamageCommand
    {
        private static string ControllerSummary => GetString("Shows recent boss kill contribution.");
        private static string ExecuteSummary => GetString("Shows recent boss kill contribution.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(ServerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            var attempts = context.Server!.NPCDamageTracker.RecentAttempts().ToList();
            if (attempts.Count == 0) {
                return CommandOutcome.Warning(GetString("No recent boss kill data found."));
            }

            List<string> reports = [];
            foreach (NPCDamageTracker? recentAttempt in attempts) {
                for (var playerId = 0; playerId < byte.MaxValue; playerId++) {
                    if (!context.Server.Main.player[playerId].active) {
                        continue;
                    }

                    reports.Add(recentAttempt.GetReport(context.Server.Main.player[playerId]).ToString());
                }
            }

            if (reports.Count == 0) {
                return CommandOutcome.Empty;
            }

            CommandOutcome.Builder builder = CommandOutcome.SuccessBuilder(reports[0]);
            for (var i = 1; i < reports.Count; i++) {
                builder.AddSuccess(reports[i]);
            }

            return builder.Build();
        }
    }
}
