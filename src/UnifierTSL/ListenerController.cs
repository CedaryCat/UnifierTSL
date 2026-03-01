using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace UnifierTSL
{
    internal sealed class ListenerSession(TcpListener listener, int port, int generation, CancellationTokenSource cts)
    {
        public TcpListener Listener { get; } = listener;
        public int Port { get; } = port;
        public int Generation { get; } = generation;
        public CancellationTokenSource Cts { get; } = cts;
    }

    internal readonly record struct ListenerChange(
        ListenerSession? Started,
        ListenerSession? Stopped,
        bool Success,
        bool PortChanged)
    {
        public static ListenerChange SuccessNoop => new(
            Started: null,
            Stopped: null,
            Success: true,
            PortChanged: false);

        public static ListenerChange FailedNoop => new(
            Started: null,
            Stopped: null,
            Success: false,
            PortChanged: false);
    }

    internal sealed class ListenerController
    {
        private readonly Lock gate = new();
        private readonly IPAddress bindAddress;

        private int listenPort;
        private IPEndPoint listeningEndpoint;
        private int generation;
        private ListenerSession? activeSession;

        public ListenerController(int initialPort) {
            bindAddress = IPAddress.Any;
            listenPort = initialPort;
            listeningEndpoint = new IPEndPoint(bindAddress, initialPort);
        }

        public int ListenPort => Volatile.Read(ref listenPort);
        public IPEndPoint ListeningEndpoint => Volatile.Read(ref listeningEndpoint);

        public bool IsCurrentSession(int targetGeneration) {
            ListenerSession? current = Volatile.Read(ref activeSession);
            return current is not null && current.Generation == targetGeneration;
        }

        public bool TryRebindPort(int requestedPort, bool hasListeningWork, out ListenerChange change) {
            if (!LauncherPortRules.IsValidListenPort(requestedPort)) {
                change = ListenerChange.FailedNoop;
                return false;
            }

            lock (gate) {
                int currentPort = listenPort;
                if (requestedPort == currentPort) {
                    change = ListenerChange.SuccessNoop;
                    return true;
                }

                ListenerSession? currentSession = activeSession;
                if (hasListeningWork) {
                    if (!TryCreateSession(requestedPort, out ListenerSession? startedSession)) {
                        change = ListenerChange.FailedNoop;
                        return false;
                    }

                    activeSession = startedSession;
                    listenPort = requestedPort;
                    Volatile.Write(ref listeningEndpoint, new IPEndPoint(bindAddress, requestedPort));

                    change = new ListenerChange(
                        Started: startedSession,
                        Stopped: currentSession,
                        Success: true,
                        PortChanged: true);
                    return true;
                }

                activeSession = null;
                listenPort = requestedPort;
                Volatile.Write(ref listeningEndpoint, new IPEndPoint(bindAddress, requestedPort));
                change = new ListenerChange(
                    Started: null,
                    Stopped: currentSession,
                    Success: true,
                    PortChanged: true);
                return true;
            }
        }

        public bool TryEnsureState(bool hasListeningWork, out ListenerChange change) {
            lock (gate) {
                int desiredPort = listenPort;
                ListenerSession? currentSession = activeSession;

                if (!hasListeningWork) {
                    if (currentSession is null) {
                        change = ListenerChange.SuccessNoop;
                        return true;
                    }

                    activeSession = null;
                    change = new ListenerChange(
                        Started: null,
                        Stopped: currentSession,
                        Success: true,
                        PortChanged: false);
                    return true;
                }

                if (currentSession is not null && currentSession.Port == desiredPort) {
                    change = ListenerChange.SuccessNoop;
                    return true;
                }

                if (!TryCreateSession(desiredPort, out ListenerSession? startedSession)) {
                    change = ListenerChange.FailedNoop;
                    return false;
                }

                activeSession = startedSession;
                change = new ListenerChange(
                    Started: startedSession,
                    Stopped: currentSession,
                    Success: true,
                    PortChanged: false);
                return true;
            }
        }

        private bool TryCreateSession(int port, out ListenerSession? session) {
            TcpListener listener = new(new IPEndPoint(bindAddress, port));
            try {
                listener.Start();
            }
            catch {
                try {
                    listener.Stop();
                }
                catch {
                }

                try {
                    listener.Dispose();
                }
                catch {
                }

                session = null;
                return false;
            }

            int sessionGeneration = unchecked(++generation);
            session = new ListenerSession(
                listener: listener,
                port: port,
                generation: sessionGeneration,
                cts: new CancellationTokenSource());
            return true;
        }
    }
}
