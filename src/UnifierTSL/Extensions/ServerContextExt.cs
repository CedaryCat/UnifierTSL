using System.Runtime.CompilerServices;
using UnifiedServerProcess;
using UnifierTSL.Servers;

namespace UnifierTSL.Extensions
{
    public static class ServerContextExt
    {
        public static ServerContext ToServer(this RootContext root) {
            return Unsafe.As<ServerContext>(root);
        }
    }
}
