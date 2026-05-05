using UnifierTSL.Surface.Prompting.Runtime;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Surface.Prompting.Model;

namespace UnifierTSL.Surface.Prompting.Sessions;
    /*
        PromptInteractionRunner owns host-side ghost continuity.

        The important invariant is that "preferred completion" continuity is only a hint, never
        a source of truth. We intentionally keep a previously displayed ghost path while the user
        extends input, but we must revalidate that path against the newly computed candidate set
        before applying it again. This protects continuation cases like:
        - typing a trailing space to move into the next semantic segment,
        - provider match semantics that are richer than plain prefix matching,
        - quoted/escaped completions whose visible preview is no longer valid after the edit.

        Do not collapse the two-pass build below into a single "carry old string if StartsWith"
        shortcut. That simplification was the direct cause of prior regressions where ghost text
        stuck to an obsolete path and masked the real best candidate.

        Preserve existing comments unless the continuity mechanism truly changes. If it does,
        update the comment in the same change and document what now guards against stale ghost
        paths, otherwise a later refactor will likely reintroduce the same regression.
    */
    internal readonly record struct PromptInteractionState(
        PromptInputPurpose Purpose,
        PromptInputState InputState,
        ProjectionDocumentContent Content,
        PromptComputation Computation,
        PromptCandidateWindowState CandidateWindow);

    internal sealed class PromptInteractionRunner
    {
        public static readonly PromptSurfaceProjectionOptions LocalRenderOptions = PromptSurfaceProjectionOptions.Unpaged;
        public static readonly PromptSurfaceProjectionOptions PagedRenderOptions = new(
            EnablePaging: true,
            PageSize: 80,
            PrefetchThreshold: 20);

        private readonly PromptSurfaceCompiler compiler;
        private readonly PromptSurfaceProjectionOptions renderOptions;
        private PromptRuntimeRevision runtimeRevision;

        public PromptInteractionRunner(
            PromptSurfaceCompiler compiler,
            PromptSurfaceProjectionOptions renderOptions) {

            this.compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
            this.renderOptions = renderOptions;

            var promptCompilation = this.compiler.BuildInitial();
            var inputState = CreateInitialInputState();
            inputState.PreferredCompletionText = promptCompilation.Computation.PreferredCompletionText;
            var candidateWindow = PromptCandidateWindowProjector.Create(promptCompilation.Computation, inputState, renderOptions);
            Current = new(
                this.compiler.Purpose,
                inputState,
                promptCompilation.Content,
                promptCompilation.Computation,
                candidateWindow);
            runtimeRevision = compiler.GetRuntimeRevision(Current.Purpose, Current.InputState);
        }

        public PromptInteractionState Current { get; private set; }

        public PromptInteractionState Update(PromptInputState inputState) {

            var carriedPreferredCompletionText = ResolvePreferredCompletionText(Current, inputState);
            inputState.PreferredCompletionText = string.Empty;
            // First build without any carried preference so the new computation exposes the real
            // candidate pool for the current raw input.
            var promptCompilation = compiler.BuildReactive(inputState);
            var promptComputation = promptCompilation.Computation;
            // Only re-apply continuity if the previously displayed path still exists and can
            // still render as inline preview for the current text. Removing this check causes
            // stale ghost text to survive edits that changed semantic meaning.
            if (CanApplyPreferredCompletionText(inputState, promptComputation, carriedPreferredCompletionText)) {
                inputState.PreferredCompletionText = carriedPreferredCompletionText;
                promptCompilation = compiler.BuildReactive(inputState);
                promptComputation = promptCompilation.Computation;
            }

            inputState.PreferredCompletionText = promptComputation.PreferredCompletionText;
            var candidateWindow = PromptCandidateWindowProjector.Create(promptComputation, inputState, renderOptions);
            Current = new(
                Current.Purpose,
                inputState,
                promptCompilation.Content,
                promptComputation,
                candidateWindow);
            runtimeRevision = compiler.GetRuntimeRevision(Current.Purpose, Current.InputState);
            return Current;
        }

        public bool TryRefreshRuntimeDependencies(out PromptInteractionState state) {
            var currentRevision = compiler.GetRuntimeRevision(Current.Purpose, Current.InputState);
            if (currentRevision == runtimeRevision) {
                state = Current;
                return false;
            }

            state = Update(Current.InputState);
            return true;
        }

        public static PromptInputState CreateInitialInputState() {
            return new PromptInputState();
        }

        private static string ResolvePreferredCompletionText(
            PromptInteractionState current,
            PromptInputState nextState) {
            var previousInput = current.InputState.InputText ?? string.Empty;
            var nextInput = nextState.InputText ?? string.Empty;
            var previousPreferred = current.Computation.PreferredCompletionText ?? string.Empty;

            if (string.IsNullOrWhiteSpace(previousPreferred)
                || nextState.CursorIndex != nextInput.Length
                || current.InputState.CursorIndex != previousInput.Length) {
                return string.Empty;
            }

            if (nextInput.Length <= previousInput.Length) {
                return string.Empty;
            }

            return nextInput.StartsWith(previousInput, StringComparison.Ordinal)
                ? previousPreferred
                : string.Empty;
        }

        private static bool CanApplyPreferredCompletionText(
            PromptInputState inputState,
            PromptComputation computation,
            string preferredCompletionText) {
            if (string.IsNullOrWhiteSpace(preferredCompletionText)) {
                return false;
            }

            var currentText = inputState.InputText ?? string.Empty;
            foreach (var candidate in computation.Suggestions) {
                if (!string.Equals(
                    candidate.PrimaryEdit.Apply(currentText),
                    preferredCompletionText,
                    StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                // Re-check preview visibility, not just string equality. A candidate may still
                // exist textually but no longer be displayable as ghost text after quoting or
                // token-boundary changes.
                if (PromptInlinePreview.TryCreateInsertions(currentText, candidate, out _)) {
                    return true;
                }
            }

            return false;
        }
    }
