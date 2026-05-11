using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Sessions;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Terminal.Shell;

namespace UnifierTSL.Surface.Adapter.Cli.Terminal;

internal sealed class LocalPromptReadInteractionDriver(
    ConsoleShell shell,
    Func<int, bool> trySelectRelativeActivity)
{
    private readonly ConsoleShell shell = shell ?? throw new ArgumentNullException(nameof(shell));
    private readonly Func<int, bool> trySelectRelativeActivity = trySelectRelativeActivity ?? throw new ArgumentNullException(nameof(trySelectRelativeActivity));

    public string ReadLine(PromptSurfaceSpec promptSpec, bool trim = false, CancellationToken cancellationToken = default) {
        if (!shell.IsInteractive) {
            throw new InvalidOperationException(GetString("Local prompt read interactions require an interactive terminal."));
        }

        using var promptSession = new ClientBufferedPromptInteractionSession(
            ClientBufferedPromptInteractionSessionOptions.CreateLocal(promptSpec));
        var scopeId = InteractionScopeId.New();
        long documentSequence = 0;
        long NextDocumentSequence() => Interlocked.Increment(ref documentSequence);
        ProjectionDocument CreateDocument() => promptSession.CreateDocument(scopeId);
        void PublishPromptSessionFrame(ClientBufferedPromptInteractionState _) {
            shell.UpdateReadLineDocument(CreateDocument(), NextDocumentSequence());
        }

        promptSession.StateChanged += PublishPromptSessionFrame;
        promptSession.SetRuntimeRefreshEnabled(promptSession.Purpose == PromptInputPurpose.CommandLine);
        var initialDocument = CreateDocument();
        var line = shell.RunBufferedEditor(
            initialDocument,
            NextDocumentSequence(),
            trim: false,
            cancellationToken: cancellationToken,
            onInputStateChanged: reactiveState => {
                try {
                    return promptSession.PublishReactiveState(reactiveState);
                }
                catch (ObjectDisposedException) {
                    return false;
                }
            },
            onActivitySelectionRequested: delta => trySelectRelativeActivity(delta));
        promptSession.StateChanged -= PublishPromptSessionFrame;
        return trim ? line.Trim() : line;
    }
}
