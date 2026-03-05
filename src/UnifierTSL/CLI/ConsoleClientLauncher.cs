using UnifiedServerProcess;
using UnifierTSL.CLI.Sessions;
using UnifierTSL.CLI.Transports;
using UnifierTSL.Servers;

namespace UnifierTSL.CLI;

public partial class ConsoleClientLauncher : ConsoleSystemContext
{
    private readonly IConsoleSessionTransport transport;
    private readonly IReadSessionBroker readSessionBroker;

    public ConsoleClientLauncher(ServerContext server) : base(server) {
        transport = new PipeConsoleSessionHost(server);
        transport.Reconnected += HandleTransportReconnected;
        readSessionBroker = new ReadSessionBroker(
            transport,
            () =>
            // We lock in one IReadLineSemanticProvider for ReadSessionBroker when this launcher is created,
            // so every ReadLine call on this launcher just keeps using that same context.
            ConsoleCommandHintRegistry.CreateProvider(
                ConsoleCommandHintRegistry.CreateCommandLineContextSpec(server),
                ReadLineMaterializationScenario.ProtocolInitial,
                ReadLineMaterializationScenario.ProtocolReactive));
        transport.Start();
    }

    public override void Dispose(bool disposing) {
        if (disposing) {
            transport.Reconnected -= HandleTransportReconnected;
            readSessionBroker.Dispose();
            transport.Dispose();
        }
        base.Dispose(disposing);
    }
}
