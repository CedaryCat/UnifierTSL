using System.Collections.Immutable;
using UnifierTSL.Surface.Activities;
using UnifierTSL.Surface.Hosting;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Surface.Status;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Commanding.Endpoints;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Terminal;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Servers;

namespace UnifierTSL.Commanding.Prompting
{
    internal static class CommandPrompting
    {
        private static int installed;

        public static void Install() {
            if (Interlocked.Exchange(ref installed, 1) != 0) {
                return;
            }

            PromptRegistry.SetDefaultCommandPromptSpecFactory(BuildPromptSpec);
            UnifierApi.EventHub.Chat.MessageEvent.Register(OnMessage, HandlerPriority.Highest);
        }

        private static PromptSurfaceSpec BuildPromptSpec(ServerContext? server) {
            var registeredPrefixes = ResolveCommandPrefixes(server);
            IReadOnlyList<string> prefixes = registeredPrefixes.Count == 0 ? ["/", "."] : [.. registeredPrefixes];
            var bufferedAuthoring = ResolveBufferedAuthoring();
            return new PromptSurfaceSpec {
                Purpose = PromptInputPurpose.CommandLine,
                Content = PromptProjectionDocumentFactory.CreateContent(
                    PromptProjectionDocumentFactory.CreateRenderOptions(bufferedAuthoring),
                    PromptProjectionDocumentFactory.CreateProjectionStyleDictionary(null),
                    nodes: [
                        new TextProjectionNodeState {
                            NodeId = PromptProjectionDocumentFactory.NodeIds.Label,
                            State = new TextNodeState {
                                Content = PromptProjectionDocumentFactory.CreateSingleLineBlock("cmd> ", SurfaceStyleCatalog.PromptLabel),
                            },
                        },
                        new DetailProjectionNodeState {
                            NodeId = PromptProjectionDocumentFactory.NodeIds.StatusDetail,
                            State = new DetailNodeState {
                                Lines = PromptProjectionDocumentFactory.CreateBlocks(BuildStatusBodyLines(server), SurfaceStyleCatalog.StatusDetail),
                            },
                        },
                    ]),
                SemanticSpec = BuildSemanticSpec(server, prefixes),
                StaticCandidates = ImmutableDictionary<string, ImmutableArray<PromptSuggestion>>.Empty
                    .Add(PromptSuggestionKindIds.Boolean, PromptSuggestionCatalog.DefaultBooleanSuggestions),
                ParameterExplainers = CommandPromptCommonObjects.ParameterExplainers.ToImmutableDictionary(),
                ParameterCandidateProviders = CommandPromptCommonObjects.ParameterCandidateProviders.ToImmutableDictionary(),
                BufferedAuthoring = bufferedAuthoring,
            };
        }

        private static void OnMessage(ref ReadonlyEventArgs<MessageEvent> args) {
            if (!args.Content.Sender.IsServer || string.IsNullOrWhiteSpace(args.Content.RawText)) {
                return;
            }

            var commandPrefixes = ResolveCommandPrefixes(args.Content.Sender.SourceServer);
            var adapter = TerminalCommandDispatchRegistry.Resolve(args.Content.Sender);
            var dispatchTask = adapter is null
                ? DispatchDefaultMaybeBatchAsync(args.Content.Sender, args.Content.Text, commandPrefixes)
                : adapter.DispatchAsync(args.Content.Sender, args.Content.Text, commandPrefixes);
            if (!dispatchTask.IsCompleted) {
                args.Handled = true;
                _ = ObservePendingDispatchAsync(dispatchTask, args.Content.Sender, args.Content.Text);
                return;
            }

            var result = dispatchTask.GetAwaiter().GetResult();
            if (!result.Handled) {
                return;
            }

            args.Handled = true;
        }

        private static async Task ObservePendingDispatchAsync(
            Task<CommandDispatchResult> dispatchTask,
            MessageSender sender,
            string rawInput) {
            try {
                var result = await dispatchTask.ConfigureAwait(false);
                if (result.Handled) {
                    return;
                }

                var sourceName = sender.SourceServer?.Name ?? "launcher";
                UnifierApi.Logger.Warning(
                    GetParticularString(
                        "{0} is terminal source name, {1} is raw command input",
                        $"A pending terminal dispatch for '{sourceName}' returned Handled = false after claiming '{rawInput}'."),
                    category: "Commanding");
            }
            catch (OperationCanceledException ex) {
                var sourceName = sender.SourceServer?.Name ?? "launcher";
                UnifierApi.Logger.Warning(
                    GetParticularString(
                        "{0} is terminal source name, {1} is raw command input",
                        $"An asynchronous terminal dispatch was canceled for '{sourceName}' while executing '{rawInput}'."),
                    category: "Commanding",
                    ex: ex);
            }
            catch (Exception ex) {
                var sourceName = sender.SourceServer?.Name ?? "launcher";
                UnifierApi.Logger.Error(
                    GetParticularString(
                        "{0} is terminal source name, {1} is raw command input",
                        $"An asynchronous terminal dispatch failed for '{sourceName}' while executing '{rawInput}'."),
                    category: "Commanding",
                    ex: ex);
            }
        }

        private static IReadOnlyList<string> ResolveCommandPrefixes(ServerContext? server) {
            var prefixes = PromptRegistry.GetRegisteredCommandPrefixes(server);
            return prefixes.Count == 0 ? ["/", "."] : prefixes;
        }

        private static PromptSemanticSpec BuildSemanticSpec(
            ServerContext? server,
            IReadOnlyList<string> activationPrefixes) {
            var catalog = CommandSystem.GetEndpointCatalog();
            List<PromptAlternativeSpec> alternatives = [];

            foreach (var endpoint in catalog.Endpoints) {
                if (endpoint.Presentation is { } presentation) {
                    alternatives.AddRange(presentation.BuildPromptAlternatives(catalog, server));
                }
            }

            return new PromptSemanticSpec {
                StatusLabel = "cmd",
                LiteralExpectationLabel = "command",
                ModifierLabel = "flag",
                OverflowExpectationLabel = "no more args",
                ActivationPrefixes = [.. activationPrefixes
                    .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
                    .Select(static prefix => prefix.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)],
                Alternatives = [.. alternatives
                    .Where(static alternative => alternative.Segments.Length > 0)
                    .OrderBy(static alternative => alternative.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static alternative => alternative.DisplayGroupKey, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static alternative => alternative.AlternativeId, StringComparer.OrdinalIgnoreCase)],
            };
        }

        private static Task<CommandDispatchResult> DispatchDefaultMaybeBatchAsync(
            MessageSender sender,
            string rawInput,
            IReadOnlyList<string> commandPrefixes,
            CancellationToken cancellationToken = default) {
            if (!CommandPromptSettings.EnableMultiLineCommandInput || !ContainsLineBreak(rawInput)) {
                return DispatchDefaultAsync(sender, rawInput, commandPrefixes, cancellationToken);
            }

            var lines = TerminalCommandBatchDispatcher.SplitNonEmptyLines(rawInput);
            return lines.Count switch {
                0 => Task.FromResult(new CommandDispatchResult {
                    Handled = false,
                    Matched = false,
                }),
                1 => DispatchDefaultBatchLineAsync(sender, lines[0], commandPrefixes, cancellationToken),
                _ => TerminalCommandBatchDispatcher.DispatchSequentialAsync(
                    lines,
                    (line, token) => DispatchDefaultBatchLineAsync(sender, line, commandPrefixes, token),
                    () => WriteBatchAborted(sender),
                    cancellationToken: cancellationToken),
            };
        }

        private static async Task<CommandDispatchResult> DispatchDefaultAsync(
            MessageSender sender,
            string rawInput,
            IReadOnlyList<string> commandPrefixes,
            CancellationToken cancellationToken = default) {
            var result = await DispatchDefaultCoreAsync(sender, rawInput, commandPrefixes, cancellationToken).ConfigureAwait(false);
            if (!result.Handled) {
                return result;
            }

            CommandSystem.GetOutcomeWriter<MessageSender>().Write(
                sender,
                result.Outcome ?? CommandOutcome.Error(GetString("Invalid command entered.")));
            return result;
        }

        private static async Task<CommandDispatchResult> DispatchDefaultBatchLineAsync(
            MessageSender sender,
            string rawInput,
            IReadOnlyList<string> commandPrefixes,
            CancellationToken cancellationToken) {
            var result = await DispatchDefaultCoreAsync(sender, rawInput, commandPrefixes, cancellationToken).ConfigureAwait(false);
            if (result.Handled) {
                CommandSystem.GetOutcomeWriter<MessageSender>().Write(
                    sender,
                    result.Outcome ?? CommandOutcome.Error(GetString("Invalid command entered.")));
                return result;
            }

            var outcome = CommandOutcome.Error(GetString("Invalid command entered."));
            CommandSystem.GetOutcomeWriter<MessageSender>().Write(sender, outcome);
            return new CommandDispatchResult {
                Handled = true,
                Matched = false,
                Outcome = outcome,
            };
        }

        private static async Task<CommandDispatchResult> DispatchDefaultCoreAsync(
            MessageSender sender,
            string rawInput,
            IReadOnlyList<string> commandPrefixes,
            CancellationToken cancellationToken) {
            TerminalExecutionContext executionContext = new() {
                Target = sender.SourceServer is null
                    ? CommandInvocationTarget.LauncherConsole
                    : CommandInvocationTarget.ServerConsole,
                Server = sender.SourceServer,
            };
            var request = new CommandDispatchRequest {
                EndpointId = TerminalCommandEndpoint.EndpointId,
                ExecutionContext = executionContext,
                RawInput = rawInput ?? string.Empty,
                CommandPrefixes = commandPrefixes,
            };
            using var activityScope = BeginActivityScope(sender, request.RawInput, cancellationToken);
            if (activityScope.Activity is not null) {
                executionContext.ExecutionFeedback = new SurfaceActivityExecutionFeedback(activityScope.Activity);
            }

            return await CommandDispatchCoordinator.DispatchAsync(request, activityScope.CancellationToken).ConfigureAwait(false);
        }

        private static SurfaceActivityScope BeginActivityScope(
            MessageSender sender,
            string rawInput,
            CancellationToken cancellationToken) {
            var message = string.IsNullOrWhiteSpace(rawInput)
                ? GetString("Executing command.")
                : GetParticularString("{0} is raw command input", $"Executing {rawInput.Trim()}.");
            return sender.SourceServer is null
                ? Console.BeginSurfaceActivity("command", message, cancellationToken: cancellationToken)
                : CreateServerActivityScope(sender.SourceServer, message, cancellationToken);
        }

        private static SurfaceActivityScope CreateServerActivityScope(
            ServerContext server,
            string message,
            CancellationToken cancellationToken) {
            var activity = server.Console.BeginSurfaceActivity("command", message, cancellationToken: cancellationToken);
            return new SurfaceActivityScope(activity, activity, cancellationToken);
        }

        private static ImmutableArray<string> BuildStatusBodyLines(ServerContext? server) {
            var activityActive = server?.Console.HasActiveSurfaceActivity ?? Console.HasActiveSurfaceActivity;
            return [.. PromptEditorKeymaps.CreateCommandStatusBodyLines(
                    CommandPromptSettings.EnableMultiLineCommandInput,
                    activityActive)];
        }

        private static PromptBufferedAuthoringOptions ResolveBufferedAuthoring() {
            return CommandPromptSettings.EnableMultiLineCommandInput
                ? new PromptBufferedAuthoringOptions {
                    EditorKind = EditorPaneKind.MultiLine,
                    Keymap = PromptEditorKeymaps.CreateMultiLine(),
                    AnalyzeCurrentLogicalLine = true,
                }
                : new PromptBufferedAuthoringOptions {
                    Keymap = PromptEditorKeymaps.CreateSingleLine(),
                };
        }

        private static void WriteBatchAborted(MessageSender sender) {
            CommandSystem.GetOutcomeWriter<MessageSender>().Write(
                sender,
                CommandOutcome.Warning(GetString("batch aborted")));
        }

        private static bool ContainsLineBreak(string? rawInput) {
            return !string.IsNullOrEmpty(rawInput)
                && (rawInput.Contains('\n') || rawInput.Contains('\r'));
        }
    }
}
