using UnifierTSL;
using UnifierTSL.Commanding;
using UnifierTSL.Servers;

namespace TShockAPI.Commanding.V2
{
    internal static class MaintenanceCommandHelpers
    {
        public static CommandOutcome StopServers(
            TSExecutionContext context,
            bool saveOnServerExit,
            string reason) {

            ServerContext[] servers = context.Server is not null
                ? [context.Server]
                : [.. UnifiedServerCoordinator.Servers.Where(static server => server.IsRunning)];

            var completed = 0;
            foreach (var server in servers) {
                if (!saveOnServerExit) {
                    server.Netplay.SaveOnServerExit = false;
                }

                Utils.StopServer(server, true, reason);
                completed++;
            }
            return CommandOutcome.Empty;
        }
    }

    [CommandController("checkupdates", Summary = nameof(ControllerSummary))]
    internal static class CheckUpdatesCommand
    {
        private static string ControllerSummary => GetString("Checks for TShock updates.");
        private static string ExecuteSummary => GetString("Queues an update check.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.maintenance))]
        public static CommandOutcome Execute() {
            return CommandOutcome.Info(GetString("An update check has been queued. If an update is available, you will be notified shortly."));
        }
    }

    [CommandController("off", Summary = nameof(ControllerSummary))]
    [Aliases("exit", "stop")]
    internal static class OffCommand
    {
        private static string ControllerSummary => GetString("Shuts down one server or all running servers while saving.");
        private static string ExecuteSummary => GetString("Stops one server or all running servers with save enabled.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.maintenance))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [RemainingText] string? reasonText = null) {
            var reason = reasonText is not null
                ? GetString("Server shutting down: ") + reasonText
                : GetString("Server shutting down!");

            return MaintenanceCommandHelpers.StopServers(
                context,
                saveOnServerExit: true,
                reason);
        }
    }

    [CommandController("off-nosave", Summary = nameof(ControllerSummary))]
    [Aliases("exit-nosave", "stop-nosave")]
    internal static class OffNoSaveCommand
    {
        private static string ControllerSummary => GetString("Shuts down one server or all running servers without saving.");
        private static string ExecuteSummary => GetString("Stops one server or all running servers with save disabled.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.maintenance))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [RemainingText] string? reasonText = null) {
            var reason = reasonText is not null
                ? GetString("Server shutting down: ") + reasonText
                : GetString("Server shutting down!");

            return MaintenanceCommandHelpers.StopServers(
                context,
                saveOnServerExit: false,
                reason);
        }
    }

    [CommandController("reload", Summary = nameof(ControllerSummary))]
    internal static class ReloadCommand
    {
        private static string ControllerSummary => GetString("Reloads config, permissions and regions.");
        private static string ExecuteSummary => GetString("Reloads the current TShock runtime configuration.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.cfgreload))]
        public static CommandOutcome Execute() {
            Utils.Reload();
            return CommandOutcome.Success(GetString("Configuration, permissions, and regions reload complete. Some changes may require a server restart."));
        }
    }

    [CommandController("serverpassword", Summary = nameof(ControllerSummary))]
    internal static class ServerPasswordCommand
    {
        private static string ExecuteSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}serverpassword \"<new password>\".", args);

        private static string ControllerSummary => GetString("Changes the server password.");
        private static string ExecuteSummary => GetString("Sets a new shared server password.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [RequireUserArgumentCountSyntax(1, nameof(ExecuteSyntaxMessage))]
        [TShockCommand(nameof(Permissions.cfgpassword))]
        public static CommandOutcome Execute(
            [RemainingText] string password = "") {
            UnifiedServerCoordinator.ServerPassword = password;
            return CommandOutcome.Success(GetString("Server password has been changed to: {0}.", password));
        }
    }

}
