using UnifierTSL.Surface.Adapter.Cli.Sessions;
using UnifierTSL.Surface.Hosting;
using UnifierTSL.Surface.Hosting.Server;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Servers;

namespace UnifierTSL.Surface.Adapter.Cli.Server;

internal sealed class CliServerSurfaceConsole : ServerSurfaceConsole
{
    private readonly ISurfaceSession session;

    public CliServerSurfaceConsole(ServerContext server)
        : base(server, () => PromptRegistry.CreateDefaultCommandPromptSpec(server)) {

        session = new ServerSurfaceSessionHost(server)
            .CreateSession(SurfaceSessionOptions.HostOwned);
        InitializeSurfaceRuntime();
    }

    protected override ISurfaceSession Session => session;
}
