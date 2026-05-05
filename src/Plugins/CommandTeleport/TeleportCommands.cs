using System.Text;
using TShockAPI;
using TShockAPI.Commanding;
using UnifierTSL;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Prompting;
using UnifierTSL.Servers;

namespace CommandTeleport
{
    [CommandController("transfer", Summary = nameof(ControllerSummary))]
    [Aliases("connect", "tr", "worldwarp", "ww")]
    internal static class CommandTeleportTransferCommand
    {
        private static string ControllerSummary => "Transfers you to another running server.";
        private static string ExecuteSummary => "Transfers you to another running server.";

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(Permissions.ServerTransfer, PlayerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "server")]
            [CommandPromptSemantic<CommandPromptParamKeys>(nameof(CommandPromptParamKeys.ServerRef))] string serverNameOrId)
        {
            ServerContext? target = CommandTeleportPlugin.FindServer(serverNameOrId);
            if (target is null) {
                return CommandOutcome.Warning($"Server '{serverNameOrId}' not found.");
            }

            if (ReferenceEquals(target, context.Server)) {
                return CommandOutcome.Warning("You are already on this server.");
            }

            UnifiedServerCoordinator.TransferPlayerToServer(context.Executor.UserId, target);
            return CommandOutcome.Success($"Transferring to {target.Name}.");
        }
    }

    [CommandController("servers", Summary = nameof(ControllerSummary))]
    [Aliases("serverlist")]
    internal static class CommandTeleportServersCommand
    {
        private static string ControllerSummary => "Lists available running servers.";
        private static string ExecuteSummary => "Lists available running servers.";

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(Permissions.ListServers)]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context)
        {
            StringBuilder sb = new();
            sb.AppendLine("Server List: ");

            var servers = UnifiedServerCoordinator.Servers;
            for (int i = 0; i < servers.Length; i++) {
                ServerContext server = servers[i];
                if (!server.IsRunning) {
                    continue;
                }

                sb.AppendLine($"{i + 1}: {server.Name}");
            }

            if (context.Server is not null) {
                sb.AppendLine($"Current Server: {context.Server.Name}");
            }

            if (sb.Length > 0 && sb[^1] == '\n') {
                sb.Length--;
                if (sb.Length > 0 && sb[^1] == '\r') {
                    sb.Length--;
                }
            }

            return CommandOutcome.Info(sb.ToString());
        }
    }
}
