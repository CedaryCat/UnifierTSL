using System.Collections.Immutable;
using System.Text;
using TShockAPI;
using UnifierTSL;
using UnifierTSL.CLI;
using UnifierTSL.Plugins;
using UnifierTSL.Servers;

namespace CommandTeleport
{
    [PluginMetadata("CommandTeleport", "1.0.0", "Anonymous", "A example plugin to show how to reference another plugin")]
    public class CommandTeleportPlugin : BasePlugin
    {
        private Command? transferCommand;
        private Command? serversCommand;
        private IDisposable? promptSpecRegistration;

        public override int InitializationOrder => TShock.Order + 1; // after tshock
        public override async Task InitializeAsync(IPluginConfigRegistrar configRegistrar, ImmutableArray<PluginInitInfo> priorInitializations, CancellationToken cancellationToken = default) {
            foreach (var initInfo in priorInitializations) {
                if (initInfo.Plugin.Name == "TShock") {
                    await initInfo.InitializationTask;
                }
            }

            transferCommand = new Command([Permissions.ServerTransfer], Command_Transfer, "transfer", "connect", "tr", "worldwarp", "ww") {
                AllowServer = false,
                HelpText = "Transfers you to another running server."
            };
            serversCommand = new Command([Permissions.ListServers], Command_ListServers, "servers", "serverlist") {
                HelpText = "Lists available running servers."
            };

            Commands.ChatCommands.Add(transferCommand);
            Commands.ChatCommands.Add(serversCommand);
            promptSpecRegistration = ConsolePromptRegistry.RegisterCommandSpecProvider(static () => CommandTeleportConsoleCommandSpecs.All);

            Directory.CreateDirectory(configRegistrar.Directory);
            string setupCheckFile = Path.Combine(configRegistrar.Directory, "complete.permissions.setup");
            if (!File.Exists(setupCheckFile)) {
                TShock.Groups.AddPermissions("default", [Permissions.ServerTransfer, Permissions.ListServers]);
                File.Create(setupCheckFile).Close();
            }
        }

        public override Task ShutdownAsync(CancellationToken cancellationToken = default) {
            UnregisterRuntimeBindings();
            return Task.CompletedTask;
        }

        public override ValueTask DisposeAsync(bool isDisposing) {
            UnregisterRuntimeBindings();
            return base.DisposeAsync(isDisposing);
        }

        private void Command_ListServers(CommandArgs args) {
            var executor = args.Executor;
            var servers = UnifiedServerCoordinator.Servers;
            if (!args.Executor.IsClient) {
                var sb = new StringBuilder();
                sb.AppendLine("Server List: ");
                for (int i = 0; i < servers.Length; i++) {
                    var server = servers[i];
                    if (!server.IsRunning) {
                        continue;
                    }
                    sb.AppendLine($"{i + 1}: {server.Name}");
                }
                if (executor.SourceServer is not null) {
                    sb.AppendLine($"Current Server: {executor.SourceServer.Name}");
                }
                sb.Remove(sb.Length - 1, 1);
                executor.SendInfoMessage(sb.ToString());
            }
            else {
                executor.SendInfoMessage("Server List: ");
                for (int i = 0; i < servers.Length; i++) {
                    var server = servers[i];
                    if (!server.IsRunning) {
                        continue;
                    }
                    executor.SendInfoMessage($"{i + 1}: {server.Name}");
                }
                if (executor.SourceServer is not null) {
                    executor.SendInfoMessage($"Current Server: {executor.SourceServer.Name}");
                }
            }
        }

        private void Command_Transfer(CommandArgs args) {
            var executor = args.Executor;

            if (!executor.IsClient) {
                executor.SendWarningMessage("You can't use this command in console.");
                return;
            }

            if (args.Parameters.Count != 1) {
                if (args.Parameters.Count > 1) {
                    executor.SendInfoMessage("Useless parameters.");
                }
                executor.SendInfoMessage("Use /transfer <server name|id> to transfer to another server.");
                executor.SendInfoMessage("Aliases: /connect, /tr, /worldwarp, /ww");
                executor.SendInfoMessage("You can also use /servers to list all available servers.");
                return;
            }

            var currentServer = executor.SourceServer;
            var target = FindServer(args.Parameters[0]);

            if (target is null) {
                executor.SendWarningMessage($"Server '{args.Parameters[0]}' not found.");
                return;
            }

            if (ReferenceEquals(target, currentServer)) {
                executor.SendWarningMessage("You are already on this server.");
                return;
            }

            UnifiedServerCoordinator.TransferPlayerToServer(executor.UserId, target);
            return;
        }

        static ServerContext? FindServer(string name) {
            var servers = UnifiedServerCoordinator.Servers;
            for (int i = 0; i < servers.Length; i++) {
                ServerContext server = servers[i];
                if (!server.IsRunning) {
                    continue;
                }
                if (server.Name == name) {
                    return server;
                }
            }
            if (int.TryParse(name, out int id)) {
                var index = id - 1;
                if (index >= 0 && index < servers.Length && servers[index].IsRunning) {
                    return servers[index];
                }
            }
            return null;
        }

        private void UnregisterRuntimeBindings() {
            promptSpecRegistration?.Dispose();
            promptSpecRegistration = null;

            if (transferCommand is not null) {
                Commands.ChatCommands.Remove(transferCommand);
                transferCommand = null;
            }

            if (serversCommand is not null) {
                Commands.ChatCommands.Remove(serversCommand);
                serversCommand = null;
            }
        }
    }
}
