using System.Collections.Immutable;
using TShockAPI;
using UnifierTSL;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Plugins;
using UnifierTSL.Servers;

namespace CommandTeleport
{
    [PluginMetadata("CommandTeleport", "1.0.0", "Anonymous", "A example plugin to show how to reference another plugin")]
    public class CommandTeleportPlugin : BasePlugin
    {
        private IDisposable? commandingRegistration;

        public override int InitializationOrder => TShock.Order + 1; // after tshock
        public override async Task InitializeAsync(IPluginConfigRegistrar configRegistrar, ImmutableArray<PluginInitInfo> priorInitializations, CancellationToken cancellationToken = default) {
            foreach (var initInfo in priorInitializations) {
                if (initInfo.Plugin.Name == "TShock") {
                    await initInfo.InitializationTask;
                }
            }

            commandingRegistration = CommandSystem.Install(static context =>
                context.AddControllerGroup<TeleportCommandController>());

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

        internal static ServerContext? FindServer(string name) {
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
            commandingRegistration?.Dispose();
            commandingRegistration = null;
        }
    }
}
