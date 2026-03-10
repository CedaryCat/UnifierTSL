using UnifiedServerProcess;
using UnifierTSL.CLI.Prompting;
using UnifierTSL.CLI.Status;
using UnifierTSL.CLI.Remote;
using UnifierTSL.Servers;

namespace UnifierTSL.CLI;

public partial class RemoteConsoleService : ConsoleSystemContext
{
    private readonly PipeRemoteConsoleTransport transport;
    private readonly RemoteConsoleReadCoordinator readCoordinator;
    private readonly ConsoleStatusController statusController;

    public RemoteConsoleService(ServerContext server) : base(server) {
        transport = new PipeRemoteConsoleTransport(server);
        transport.Reconnected += HandleTransportReconnected;
        UnifierApi.ConsoleAppearanceChanged += HandleConsoleAppearanceChanged;
        readCoordinator = new RemoteConsoleReadCoordinator(
            transport,
            () =>
            ConsolePromptRegistry.CreateCompiler(
                ConsolePromptRegistry.CreateDefaultCommandPromptSpec(server),
                ConsolePromptScenario.PagedInitial,
                ConsolePromptScenario.PagedReactive));
        statusController = new ConsoleStatusController(
            server,
            PublishStatusFrame,
            shouldPublish: () => transport.IsConnected);
        transport.Start();
    }

    public override void Dispose(bool disposing) {
        if (disposing) {
            try {
                statusController.Dispose();
            }
            catch {
            }

            UnifierApi.ConsoleAppearanceChanged -= HandleConsoleAppearanceChanged;
            transport.Reconnected -= HandleTransportReconnected;
            readCoordinator.Dispose();
            transport.Dispose();
        }
        base.Dispose(disposing);
    }
}
