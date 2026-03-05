using System.Collections.Concurrent;
using UnifierTSL.ConsoleClient.Shell;
using UnifierTSL.Servers;

namespace UnifierTSL.CLI.Sessions
{
    internal static class WebConsoleInputSessionService
    {
        private sealed class SessionEntry
        {
            public required string SessionId { get; init; }

            public required string ServerName { get; init; }

            public required IReadLineSemanticProvider SemanticProvider { get; init; }

            public required ReadLineReactiveState ReactiveState { get; set; }

            public required ReadLineRenderSnapshot RenderSnapshot { get; set; }

            public DateTime UpdatedUtc { get; set; }
        }

        private static readonly ConcurrentDictionary<string, SessionEntry> Sessions = new(StringComparer.Ordinal);
        private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(30);

        public static bool TryCreateSession(string? serverName, out WebConsoleInputSessionState session, out string error)
        {
            CleanupExpiredSessions();

            ServerContext? server = ResolveServer(serverName, out string normalizedServerName, out error);
            if (error.Length > 0) {
                session = default;
                return false;
            }

            ReadLineContextSpec contextSpec = ConsoleCommandHintRegistry.CreateCommandLineContextSpec(server);
            IReadLineSemanticProvider semanticProvider = ConsoleCommandHintRegistry.CreateProvider(
                contextSpec,
                ReadLineMaterializationScenario.ProtocolInitial,
                ReadLineMaterializationScenario.ProtocolReactive);
            ReadLineSemanticSnapshot initialSemantic = semanticProvider.BuildInitial();
            ReadLineReactiveState reactiveState = new() {
                Purpose = initialSemantic.Payload.Purpose,
                InputText = string.Empty,
                CursorIndex = 0,
                CompletionIndex = 0,
                CompletionCount = 0,
                CandidateWindowOffset = 0,
            };
            ReadLineRenderSnapshot initialRender = ReadLineRenderPaging.BuildSnapshot(initialSemantic, reactiveState);

            string sessionId = Guid.NewGuid().ToString("N");
            SessionEntry entry = new() {
                SessionId = sessionId,
                ServerName = normalizedServerName,
                SemanticProvider = semanticProvider,
                ReactiveState = reactiveState,
                RenderSnapshot = initialRender,
                UpdatedUtc = DateTime.UtcNow,
            };

            Sessions[sessionId] = entry;
            session = CloneSessionState(entry);
            return true;
        }

        public static bool TryGetSession(string sessionId, out WebConsoleInputSessionState session, out string error)
        {
            CleanupExpiredSessions();

            if (!Sessions.TryGetValue(sessionId, out SessionEntry? entry)) {
                error = $"Session '{sessionId}' was not found.";
                session = default;
                return false;
            }

            entry.UpdatedUtc = DateTime.UtcNow;
            session = CloneSessionState(entry);
            error = string.Empty;
            return true;
        }

        public static bool TryPushInput(
            string sessionId,
            string? inputText,
            int cursorIndex,
            int completionIndex,
            int completionCount,
            int candidateWindowOffset,
            out WebConsoleInputSessionState session,
            out string error)
        {
            CleanupExpiredSessions();

            if (!Sessions.TryGetValue(sessionId, out SessionEntry? entry)) {
                error = $"Session '{sessionId}' was not found.";
                session = default;
                return false;
            }

            string input = inputText ?? string.Empty;
            ReadLineReactiveState reactiveState = new() {
                Purpose = entry.ReactiveState.Purpose,
                InputText = input,
                CursorIndex = Math.Clamp(cursorIndex, 0, input.Length),
                CompletionIndex = Math.Max(0, completionIndex),
                CompletionCount = Math.Max(0, completionCount),
                CandidateWindowOffset = Math.Max(0, candidateWindowOffset),
            };

            ReadLineSemanticSnapshot semanticSnapshot = entry.SemanticProvider.BuildReactive(reactiveState);
            reactiveState.Purpose = semanticSnapshot.Payload.Purpose;
            ReadLineRenderSnapshot renderSnapshot = ReadLineRenderPaging.BuildSnapshot(semanticSnapshot, reactiveState);
            entry.ReactiveState = reactiveState;
            entry.RenderSnapshot = renderSnapshot;
            entry.UpdatedUtc = DateTime.UtcNow;

            session = CloneSessionState(entry);
            error = string.Empty;
            return true;
        }

        public static bool TryCloseSession(string sessionId)
        {
            return Sessions.TryRemove(sessionId, out _);
        }

        private static ServerContext? ResolveServer(string? serverName, out string normalizedServerName, out string error)
        {
            if (string.IsNullOrWhiteSpace(serverName)) {
                normalizedServerName = "*";
                error = string.Empty;
                return null;
            }

            string name = serverName.Trim();
            ServerContext? server = UnifiedServerCoordinator.Servers
                .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (server is null) {
                normalizedServerName = name;
                error = $"Server '{name}' was not found.";
                return null;
            }

            normalizedServerName = server.Name;
            error = string.Empty;
            return server;
        }

        private static WebConsoleInputSessionState CloneSessionState(SessionEntry entry)
        {
            return new WebConsoleInputSessionState(
                SessionId: entry.SessionId,
                ServerName: entry.ServerName,
                ReactiveState: new ReadLineReactiveState {
                    Purpose = entry.ReactiveState.Purpose,
                    InputText = entry.ReactiveState.InputText,
                    CursorIndex = entry.ReactiveState.CursorIndex,
                    CompletionIndex = entry.ReactiveState.CompletionIndex,
                    CompletionCount = entry.ReactiveState.CompletionCount,
                    CandidateWindowOffset = entry.ReactiveState.CandidateWindowOffset,
                },
                RenderSnapshot: SemanticToRenderMapper.CloneRender(entry.RenderSnapshot),
                UpdatedUtc: entry.UpdatedUtc);
        }

        private static void CleanupExpiredSessions()
        {
            DateTime now = DateTime.UtcNow;
            foreach ((string sessionId, SessionEntry entry) in Sessions) {
                if (now - entry.UpdatedUtc <= SessionTimeout) {
                    continue;
                }

                Sessions.TryRemove(sessionId, out _);
            }
        }
    }

    internal readonly record struct WebConsoleInputSessionState(
        string SessionId,
        string ServerName,
        ReadLineReactiveState ReactiveState,
        ReadLineRenderSnapshot RenderSnapshot,
        DateTime UpdatedUtc);
}
