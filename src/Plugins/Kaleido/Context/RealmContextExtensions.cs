using Kaleido.Model.Instances;
using UnifierTSL.Servers;

namespace Kaleido.Context
{
    internal sealed class RealmContextData : IExtensionData
    {
        public RealmInstance? Instance { get; set; }
        public void Dispose() { }
    }

    internal static class RealmExtensionsBootstrap
    {
        private static int initialized;

        public static void EnsureInitialized() {
            if (Interlocked.Exchange(ref initialized, 1) == 1) {
                return;
            }

            ServerContext.RegisterExtension(static _ => new RealmContextData());
        }
    }

    public static class ServerContextRealmExtensions
    {
        public static RealmInstance? TryGetRealmInstance(this ServerContext server) {
            ArgumentNullException.ThrowIfNull(server);
            return server.GetExtension<RealmContextData>().Instance;
        }

        internal static void SetRealmInstance(this ServerContext server, RealmInstance instance) {
            ArgumentNullException.ThrowIfNull(server);
            server.GetExtension<RealmContextData>().Instance = instance;
        }

        internal static void ClearRealmInstance(this ServerContext server) {
            ArgumentNullException.ThrowIfNull(server);
            server.GetExtension<RealmContextData>().Instance = null;
        }
    }
}
