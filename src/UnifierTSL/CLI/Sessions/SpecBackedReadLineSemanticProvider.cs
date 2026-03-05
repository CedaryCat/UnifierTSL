using UnifierTSL.ConsoleClient.Shell;

namespace UnifierTSL.CLI.Sessions
{
    internal sealed class SpecBackedReadLineSemanticProvider : IReadLineSemanticProvider
    {
        private readonly ReadLineContextSpec contextSpec;
        private readonly IReadLineContextMaterializer materializer;
        private readonly ReadLineMaterializationScenario initialScenario;
        private readonly ReadLineMaterializationScenario reactiveScenario;
        private readonly ReadLineSemanticEngine semanticEngine = new();

        public SpecBackedReadLineSemanticProvider(
            ReadLineContextSpec contextSpec,
            IReadLineContextMaterializer materializer,
            ReadLineMaterializationScenario initialScenario = ReadLineMaterializationScenario.ProtocolInitial,
            ReadLineMaterializationScenario reactiveScenario = ReadLineMaterializationScenario.ProtocolReactive)
        {
            this.contextSpec = contextSpec ?? throw new ArgumentNullException(nameof(contextSpec));
            this.materializer = materializer ?? throw new ArgumentNullException(nameof(materializer));
            this.initialScenario = initialScenario;
            this.reactiveScenario = reactiveScenario;
        }

        public ReadLineSemanticSnapshot BuildInitial()
        {
            ReadLineReactiveState initialState = materializer.CreateInitialReactiveState(contextSpec);
            return BuildSnapshot(initialState, initialScenario);
        }

        public ReadLineSemanticSnapshot BuildReactive(ReadLineReactiveState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            return BuildSnapshot(state, reactiveScenario);
        }

        private ReadLineSemanticSnapshot BuildSnapshot(ReadLineReactiveState state, ReadLineMaterializationScenario scenario)
        {
            ReadLineResolvedContext context = materializer.BuildContext(contextSpec, state, scenario);
            string input = state.InputText ?? string.Empty;
            IReadOnlyList<string> resolved = semanticEngine.ResolveSuggestions(context, input);

            string helpText = context.HelpText;
            string parameterHint = context.ParameterHint;
            if (context.Purpose == ConsoleInputPurpose.CommandLine) {
                ConsoleCommandHint? activeHint = semanticEngine.ResolveActiveCommandHint(context, state, resolved);
                helpText = activeHint?.HelpText ?? string.Empty;
                parameterHint = activeHint?.ParameterHint ?? string.Empty;
            }

            List<string> statusLines = semanticEngine.BuildStatusLines(context, state, resolved);

            return new ReadLineSemanticSnapshot {
                Payload = new ReadLineSnapshotPayload {
                    Purpose = context.Purpose,
                    Prompt = context.Prompt,
                    GhostText = context.GhostText,
                    EmptySubmitBehavior = context.EmptySubmitBehavior,
                    EnableCtrlEnterBypassGhostFallback = context.EnableCtrlEnterBypassGhostFallback,
                    HelpText = helpText,
                    ParameterHint = parameterHint,
                    StatusPanelHeight = context.StatusPanelHeight,
                    StatusLines = statusLines,
                    AllowAnsiStatusEscapes = context.AllowAnsiStatusEscapes,
                    Candidates = [.. resolved.Select(static value => new ConsoleSuggestionItem {
                        Value = value,
                        Weight = 0,
                    })],
                },
            };
        }
    }
}
