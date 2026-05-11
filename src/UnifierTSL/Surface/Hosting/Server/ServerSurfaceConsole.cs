using UnifiedServerProcess;
using UnifierTSL.Servers;
using UnifierTSL.Surface.Hosting;
using UnifierTSL.Surface.Prompting;

namespace UnifierTSL.Surface.Hosting.Server;

public abstract partial class ServerSurfaceConsole(
    ServerContext server,
    Func<PromptSurfaceSpec> defaultPromptSpecFactory) : ConsoleSystemContext(server)
{
    private readonly ServerContext server = server ?? throw new ArgumentNullException(nameof(server));
    private readonly Func<PromptSurfaceSpec> defaultPromptSpecFactory =
        defaultPromptSpecFactory ?? throw new ArgumentNullException(nameof(defaultPromptSpecFactory));

    protected ServerContext Server => server;
    protected PromptSurfaceSpec CreateDefaultPromptSpec() => defaultPromptSpecFactory();

    protected abstract ISurfaceSession Session { get; }

    protected void InitializeSurfaceRuntime() {
        InitializeRuntime();
    }
}
