using Atelier.Session.Context;
using UnifierTSL;
using UnifierTSL.Logging;

namespace Atelier.Session.Context
{
    internal sealed class HostContextFactory
    {
        private readonly ScriptHostTypeFactory scriptHostTypeFactory = new();

        internal ScriptHostContext Create(OpenOptions options, CancellationToken cancellation = default) {

            var console = new HostConsole();
            var launcher = new LauncherGlobals();
            ServerGlobals? server = null;
            IStandardLogger log = UnifierApi.Logger;
            switch (options.TargetProfile) {
                case LauncherProfile:
                    break;

                case ServerProfile serverProfile:
                    server = new ServerGlobals(serverProfile.Server);
                    log = server.Log;
                    break;

                default:
                    throw new InvalidOperationException(GetString($"Unsupported atelier target profile '{options.TargetProfile.GetType().FullName}'."));
            }

            return scriptHostTypeFactory.Create(
                console,
                launcher,
                server,
                log,
                options.InvocationHost.Label,
                options.TargetProfile.Label,
                cancellation);
        }
    }
}
