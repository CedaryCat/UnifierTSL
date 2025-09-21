using Terraria.IO;
using UnifiedServerProcess;

namespace UnifierTSL.Servers
{
    /// <summary>
    /// Only for general sample creation, such as for setting up default item sample 
    /// </summary>
    public class SampleServer : ServerContext
    {
        private class EmptyWorldDataProvider : IWorldDataProvider
        {
            public string WorldName => "EmptyWorld";
            public string WorldFileName => "EmptyWorld.wld";
            public WorldFileData ApplyMetadata(ServerContext server) {
                return new WorldFileData();
            }
        }
        public SampleServer() : base("Sample", new EmptyWorldDataProvider()) {
            Main.dedServ = true;
            Main.player[Terraria.Main.myPlayer] = new();
        }
        protected sealed override void InitializeExtension() { }
        protected sealed override ConsoleSystemContext CreateConsoleService() => new(this);
    }
}
