using System.Collections.Concurrent;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Sessions;
using UnifierTSL.Surface.Prompting.Runtime;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Servers;

namespace UnifierTSL.Surface.Adapter.Web.Prompting
{
    // wip
    internal static class WebPromptSessionStore
    {
        private sealed class SessionEntry
        {
            public required string SessionId { get; init; }

            public required string ServerName { get; init; }

            public required PromptInteractionRunner PromptRunner { get; init; }

            public DateTime UpdatedUtc { get; set; }
        }

        private static readonly ConcurrentDictionary<string, SessionEntry> Sessions = new(StringComparer.Ordinal);
        private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(30);

        public static bool TryCreateSession(string? serverName, out WebPromptSessionState session, out string error) {
            CleanupExpiredSessions();

            var server = ResolveServer(serverName, out var normalizedServerName, out error);
            if (error.Length > 0) {
                session = default;
                return false;
            }

            var contextSpec = PromptRegistry.CreateDefaultCommandPromptSpec(server);
            var compiler = PromptRegistry.CreateCompiler(
                contextSpec,
                PromptSurfaceScenario.PagedInitial,
                PromptSurfaceScenario.PagedReactive);
            PromptInteractionRunner promptRunner = new(compiler, PromptInteractionRunner.PagedRenderOptions);

            var sessionId = Guid.NewGuid().ToString("N");
            SessionEntry entry = new() {
                SessionId = sessionId,
                ServerName = normalizedServerName,
                PromptRunner = promptRunner,
                UpdatedUtc = DateTime.UtcNow,
            };

            Sessions[sessionId] = entry;
            session = CloneSessionState(entry);
            return true;
        }

        public static bool TryGetSession(string sessionId, out WebPromptSessionState session, out string error) {
            CleanupExpiredSessions();

            if (!Sessions.TryGetValue(sessionId, out var entry)) {
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
            string? preferredInterpretationId,
            out WebPromptSessionState session,
            out string error) {
            CleanupExpiredSessions();

            if (!Sessions.TryGetValue(sessionId, out var entry)) {
                error = $"Session '{sessionId}' was not found.";
                session = default;
                return false;
            }

            var input = inputText ?? string.Empty;
            PromptInputState reactiveState = new() {
                InputText = input,
                CursorIndex = Math.Clamp(cursorIndex, 0, input.Length),
                CompletionIndex = Math.Max(0, completionIndex),
                CompletionCount = Math.Max(0, completionCount),
                CandidateWindowOffset = Math.Max(0, candidateWindowOffset),
                PreferredInterpretationId = preferredInterpretationId ?? string.Empty,
            };

            entry.PromptRunner.Update(reactiveState);
            entry.UpdatedUtc = DateTime.UtcNow;

            session = CloneSessionState(entry);
            error = string.Empty;
            return true;
        }

        public static bool TryCloseSession(string sessionId) {
            return Sessions.TryRemove(sessionId, out _);
        }

        private static ServerContext? ResolveServer(string? serverName, out string normalizedServerName, out string error) {
            if (string.IsNullOrWhiteSpace(serverName)) {
                normalizedServerName = "*";
                error = string.Empty;
                return null;
            }

            var name = serverName.Trim();
            var server = UnifiedServerCoordinator.Servers
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

        private static WebPromptSessionState CloneSessionState(SessionEntry entry) {
            return new WebPromptSessionState(
                SessionId: entry.SessionId,
                ServerName: entry.ServerName,
                ReactiveState: entry.PromptRunner.Current.InputState,
                Computation: PromptCandidateWindowProjector.CreateWindowedComputation(
                    entry.PromptRunner.Current.Computation,
                    entry.PromptRunner.Current.CandidateWindow),
                CandidateWindow: entry.PromptRunner.Current.CandidateWindow,
                UpdatedUtc: entry.UpdatedUtc);
        }

        private static void CleanupExpiredSessions() {
            var now = DateTime.UtcNow;
            foreach ((var sessionId, var entry) in Sessions) {
                if (now - entry.UpdatedUtc <= SessionTimeout) {
                    continue;
                }

                Sessions.TryRemove(sessionId, out _);
            }
        }
    }

    internal readonly record struct WebPromptSessionState(
        string SessionId,
        string ServerName,
        PromptInputState ReactiveState,
        PromptComputation Computation,
        PromptCandidateWindowState CandidateWindow,
        DateTime UpdatedUtc);
}
