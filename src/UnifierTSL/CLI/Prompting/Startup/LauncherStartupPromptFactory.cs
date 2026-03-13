using System.Collections.Immutable;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;

namespace UnifierTSL.CLI.Prompting.Startup
{
    internal static class LauncherStartupPromptFactory
    {
        public static ConsolePromptSpec CreateListenPortPrompt(
            string? lastError,
            IReadOnlyList<int> portCandidates,
            Func<ConsoleInputState, IReadOnlyList<ConsoleSuggestion>> suggestionResolver,
            Func<ConsoleInputState, IReadOnlyList<string>> statusResolver) {
            ArgumentNullException.ThrowIfNull(portCandidates);
            ArgumentNullException.ThrowIfNull(suggestionResolver);
            ArgumentNullException.ThrowIfNull(statusResolver);

            List<string> baseStatusBodyLines = [
                GetString("use Tab/Shift+Tab to rotate; Right to accept"),
                GetString("Enter on empty uses ghost; Ctrl+Enter keeps raw input."),
            ];
            if (!string.IsNullOrWhiteSpace(lastError)) {
                baseStatusBodyLines.Add(GetParticularString("{0} is error reason", $"last error: {lastError}"));
            }

            return new ConsolePromptSpec {
                Purpose = ConsoleInputPurpose.StartupPort,
                Prompt = "listen-port> ",
                GhostText = "7777",
                EmptySubmitBehavior = EmptySubmitBehavior.AcceptGhostIfAvailable,
                EnableCtrlEnterBypassGhostFallback = true,
                InputSummary = GetString("Setup Listen Port"),
                HelpText = GetString("Select the launcher listen port"),
                BaseStatusBodyLines = [.. baseStatusBodyLines],
                StaticCandidates = ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>>.Empty
                    .Add(ConsoleSuggestionKind.Plain, [.. portCandidates.Select(static port => new ConsoleSuggestion(port.ToString(), 0))]),
                RuntimeResolver = ConsolePromptRuntimeResolver.Create(
                    resolveContext => new ConsolePromptUpdate {
                        CandidateOverrides = ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>>.Empty
                            .Add(ConsoleSuggestionKind.Plain, [.. suggestionResolver(resolveContext.State)]),
                        AdditionalStatusBodyLines = [.. statusResolver(resolveContext.State)],
                    },
                    resolveContext => BuildRuntimeRevision(resolveContext.State, suggestionResolver, statusResolver)),
            };
        }

        public static ConsolePromptSpec CreateServerPasswordPrompt(
            IReadOnlyList<string> passwordCandidates,
            Func<ConsoleInputState, IReadOnlyList<ConsoleSuggestion>> suggestionResolver,
            Func<ConsoleInputState, IReadOnlyList<string>> statusResolver) {
            ArgumentNullException.ThrowIfNull(passwordCandidates);
            ArgumentNullException.ThrowIfNull(suggestionResolver);
            ArgumentNullException.ThrowIfNull(statusResolver);

            return new ConsolePromptSpec {
                Purpose = ConsoleInputPurpose.StartupPassword,
                Prompt = "server-password> ",
                GhostText = passwordCandidates.Count > 0 ? passwordCandidates[0] : string.Empty,
                EmptySubmitBehavior = EmptySubmitBehavior.KeepInput,
                EnableCtrlEnterBypassGhostFallback = true,
                InputSummary = GetString("Setup Server Password"),
                HelpText = GetString("Pick a short startup password (plain input)."),
                BaseStatusBodyLines = [
                    GetString("use Tab/Shift+Tab to rotate; Right to accept"),
                    GetString("Press Enter to keep your current input (can be empty)."),
                ],
                StaticCandidates = ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>>.Empty
                    .Add(ConsoleSuggestionKind.Plain, [.. passwordCandidates.Select(static value => new ConsoleSuggestion(value, 0))]),
                RuntimeResolver = ConsolePromptRuntimeResolver.Create(
                    resolveContext => new ConsolePromptUpdate {
                        CandidateOverrides = ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>>.Empty
                            .Add(ConsoleSuggestionKind.Plain, [.. suggestionResolver(resolveContext.State)]),
                        AdditionalStatusBodyLines = [.. statusResolver(resolveContext.State)],
                    },
                    resolveContext => BuildRuntimeRevision(resolveContext.State, suggestionResolver, statusResolver)),
            };
        }

        private static long BuildRuntimeRevision(
            ConsoleInputState state,
            Func<ConsoleInputState, IReadOnlyList<ConsoleSuggestion>> suggestionResolver,
            Func<ConsoleInputState, IReadOnlyList<string>> statusResolver) {
            HashCode hash = new();
            foreach (ConsoleSuggestion suggestion in suggestionResolver(state) ?? []) {
                if (string.IsNullOrWhiteSpace(suggestion.Value)) {
                    continue;
                }

                hash.Add(suggestion.Value.Trim(), StringComparer.OrdinalIgnoreCase);
                hash.Add(suggestion.Weight);
            }

            foreach (string statusLine in statusResolver(state) ?? []) {
                if (string.IsNullOrWhiteSpace(statusLine)) {
                    continue;
                }

                hash.Add(statusLine.Trim(), StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }
}
