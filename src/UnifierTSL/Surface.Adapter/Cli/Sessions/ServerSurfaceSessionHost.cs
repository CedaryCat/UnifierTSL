using UnifierTSL.Servers;

namespace UnifierTSL.Surface.Adapter.Cli.Sessions {
    public sealed class ServerSurfaceSessionHost(ServerContext server) : PipeSurfaceSessionHost(CreatePipeNamePrefix(server)) {
        private static string CreatePipeNamePrefix(ServerContext server) {
            return $"USP_Surface_Server_{server.Name}_{server.UniqueId:N}";
        }
    }
}
