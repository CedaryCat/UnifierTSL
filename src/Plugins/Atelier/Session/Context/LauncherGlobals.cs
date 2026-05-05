using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using UnifierTSL;
using UnifierTSL.Logging;
using UnifierTSL.Servers;

namespace Atelier.Session.Context
{
    public sealed class LauncherGlobals
    {
        public UnifierApi.LifecyclePhase Phase => UnifierApi.CurrentPhase;

        public RoleLogger Log => UnifierApi.Logger;

        public string BaseDirectory => UnifierApi.BaseDirectory;

        public string LibraryDirectory => UnifierApi.LibraryDirectory;

        public string TranslationsDirectory => UnifierApi.TranslationsDirectory;

        public CultureInfo TranslationCulture => UnifierApi.TranslationCultureInfo;

        public int ListenPort => UnifiedServerCoordinator.ListenPort;

        public IPEndPoint ListeningEndpoint => UnifiedServerCoordinator.ListeningEndpoint;

        public int ActiveConnections => UnifiedServerCoordinator.ActiveConnections;

        public bool AnyClientsConnected => UnifiedServerCoordinator.AnyClientsConnected;

        public int ClientSpace => UnifiedServerCoordinator.GetClientSpace();

        public int ActiveClientCount => UnifiedServerCoordinator.GetActiveClientCount();

        public ImmutableArray<ServerContext> Servers => UnifiedServerCoordinator.Servers;

        public ImmutableArray<ServerContext> RunningServers => [.. UnifiedServerCoordinator.Servers.Where(static server => server.IsRunning)];

        public bool IsInteractive => UnifierApi.IsInteractiveConsole;

        public bool UseColorfulConsoleStatus => UnifierApi.UseColorfulConsoleStatus;

        public string RootConfigPath => UnifierApi.RootConfigPath;
    }
}
