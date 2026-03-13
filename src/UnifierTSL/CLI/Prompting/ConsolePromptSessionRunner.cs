using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;

namespace UnifierTSL.CLI.Prompting
{
    internal readonly record struct ConsolePromptSessionState(
        ConsoleInputState InputState,
        ConsoleRenderSnapshot RenderSnapshot);

    internal sealed class ConsolePromptSessionRunner
    {
        public static readonly ConsoleRenderMapOptions LocalRenderOptions = ConsoleRenderMapOptions.Unpaged;
        public static readonly ConsoleRenderMapOptions PagedRenderOptions = new(
            EnablePaging: true,
            PageSize: 80,
            PrefetchThreshold: 20);

        private readonly ConsolePromptCompiler compiler;
        private readonly ConsoleRenderMapOptions renderOptions;
        private ConsolePromptRuntimeRevision runtimeRevision;

        public ConsolePromptSessionRunner(
            ConsolePromptCompiler compiler,
            ConsoleRenderMapOptions renderOptions) {
            this.compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
            this.renderOptions = renderOptions;

            ConsolePromptSnapshot promptSnapshot = this.compiler.BuildInitial();
            ConsoleInputState inputState = CreateInitialInputState(promptSnapshot.Purpose);
            Current = new(inputState, ConsoleRenderMapper.Map(promptSnapshot, inputState, renderOptions));
            runtimeRevision = compiler.GetRuntimeRevision(Current.InputState);
        }

        public ConsolePromptSessionState Current { get; private set; }

        public ConsolePromptSessionState Update(ConsoleInputState inputState) {
            ArgumentNullException.ThrowIfNull(inputState);

            ConsoleInputState normalizedState = NormalizeInputState(inputState);
            ConsolePromptSnapshot promptSnapshot = compiler.BuildReactive(normalizedState);
            normalizedState = normalizedState with {
                Purpose = promptSnapshot.Purpose,
            };
            Current = new(normalizedState, ConsoleRenderMapper.Map(promptSnapshot, normalizedState, renderOptions));
            runtimeRevision = compiler.GetRuntimeRevision(Current.InputState);
            return Current;
        }

        public bool TryRefreshRuntimeDependencies(out ConsolePromptSessionState state) {
            ConsolePromptRuntimeRevision currentRevision = compiler.GetRuntimeRevision(Current.InputState);
            if (currentRevision == runtimeRevision) {
                state = Current;
                return false;
            }

            state = Update(Current.InputState);
            return true;
        }

        public static ConsoleInputState CreateInitialInputState(ConsoleInputPurpose purpose) {
            return new ConsoleInputState {
                Purpose = purpose,
            };
        }

        private static ConsoleInputState NormalizeInputState(ConsoleInputState inputState) {
            return inputState with {
                InputText = inputState.InputText ?? string.Empty,
                CursorIndex = Math.Max(0, inputState.CursorIndex),
                CompletionIndex = Math.Max(0, inputState.CompletionIndex),
                CompletionCount = Math.Max(0, inputState.CompletionCount),
                CandidateWindowOffset = Math.Max(0, inputState.CandidateWindowOffset),
            };
        }
    }
}
