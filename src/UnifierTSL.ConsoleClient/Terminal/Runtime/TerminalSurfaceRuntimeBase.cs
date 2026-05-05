using System.Text;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Contracts.Terminal;
using UnifierTSL.Terminal.Shell;

namespace UnifierTSL.Terminal.Runtime {
    internal class TerminalSurfaceRuntimeBase : IDisposable {
        private readonly ITerminalDevice terminalDevice;
        private readonly ConsoleShell shell;

        internal TerminalSurfaceRuntimeBase()
            : this(new SystemConsoleTerminalDevice(), TimeProvider.System) {
        }

        protected TerminalSurfaceRuntimeBase(ITerminalDevice terminalDevice, TimeProvider timeProvider) {
            this.terminalDevice = terminalDevice;
            shell = new ConsoleShell(terminalDevice, timeProvider);
        }

        public virtual TerminalSurfaceInteraction CreateInteraction(
            InteractionScopeId scopeId,
            TerminalSurfaceInteraction? existingInteraction,
            LifecyclePayload? lifecycle,
            TerminalSurfaceRuntimeFrame? frame) {
            return frame?.InteractionMode == TerminalSurfaceInteractionMode.Display
                ? CreateDisplayInteraction(scopeId, existingInteraction, lifecycle)
                : CreateBufferedAuthoringInteraction(scopeId, existingInteraction, lifecycle, frame);
        }

        public void ApplySurfaceHostOperation(SurfaceHostOperation operation) {
            if (SurfaceHostOperations.TryGetProperties(operation, out var properties)) {
                if (properties.InputEncoding is { } inputEncoding) {
                    terminalDevice.SetInputEncoding(Encoding.GetEncoding(inputEncoding));
                }

                if (properties.OutputEncoding is { } outputEncoding) {
                    terminalDevice.SetOutputEncoding(Encoding.GetEncoding(outputEncoding));
                }

                if (properties.Width is { } width) {
                    terminalDevice.SetWindowWidth(width);
                }

                if (properties.Height is { } height) {
                    terminalDevice.SetWindowHeight(height);
                }

                if (properties.Left is { } left) {
                    terminalDevice.SetWindowLeft(left);
                }

                if (properties.Top is { } top) {
                    terminalDevice.SetWindowTop(top);
                }

                if (properties.Title is { } title) {
                    terminalDevice.SetTitle(title);
                }

                return;
            }

            if (SurfaceHostOperations.IsClear(operation)) {
                shell.Clear();
            }
        }

        public void ApplyStream(StreamPayload payload) {
            switch (payload.Kind) {
                case StreamPayloadKind.AppendText:
                    ApplyStreamText(payload, appendLine: false);
                    break;

                case StreamPayloadKind.AppendLine:
                    ApplyStreamText(payload, appendLine: true);
                    break;

                case StreamPayloadKind.Clear:
                    if (payload.Channel == StreamChannel.Status) {
                        shell.ClearStatusFrame();
                        break;
                    }

                    shell.ClearLog();
                    break;
            }
        }

        private void ApplyStreamText(StreamPayload payload, bool appendLine) {
            var text = payload.StyledText is { } styledText
                ? TerminalStreamTextFormatter.FormatStyledText(styledText, payload.Styles)
                : payload.Text;
            if (appendLine) {
                text += Environment.NewLine;
            }

            shell.AppendLog(text, payload.IsAnsi || payload.StyledText is not null);
        }

        public void ApplyStatusSnapshot(TerminalSurfaceRuntimeFrame frame) {
            shell.UpdateStatusFrame(frame);
        }

        public virtual void ApplyInteractionFrame(TerminalSurfaceInteraction interaction, TerminalSurfaceRuntimeFrame frame) {
            if (interaction.Mode == TerminalSurfaceInteractionMode.Display) {
                ApplyDisplayInteractionFrame(interaction, frame);
                return;
            }

            ApplyBufferedInteractionFrame(interaction, frame);
        }

        public virtual SurfaceCompletion ExecuteInteraction(
            TerminalSurfaceInteraction interaction,
            TerminalSurfaceRuntimeFrame frame,
            Action<ClientBufferedEditorState> onBufferedEditorState,
            Action<ClientBufferedEditorState> onSubmitBufferedEditorState,
            Action<ConsoleKeyInfo> onKeyPressed,
            Action<int> onActivitySelectionRequested,
            CancellationToken cancellationToken) {
            if (interaction.Mode == TerminalSurfaceInteractionMode.Display) {
                return ExecuteDisplayInteraction(frame, cancellationToken);
            }

            return ExecuteClientBufferedAuthoring(
                frame,
                () => interaction.EditorPane,
                onBufferedEditorState,
                onSubmitBufferedEditorState,
                onKeyPressed,
                onActivitySelectionRequested,
                cancellationToken);
        }

        public void Dispose() {
            shell.Dispose();
        }

        protected SurfaceCompletion ExecuteClientBufferedReadLine(
            TerminalSurfaceRuntimeFrame frame,
            Func<EditorPaneRuntimeState> getEditorPane,
            Action<ClientBufferedEditorState> onBufferedEditorState,
            Action<int> onActivitySelectionRequested,
            CancellationToken cancellationToken) {
            if (!shell.IsInteractive) {
                return CreateClosedCompletion();
            }

            var editorPane = getEditorPane();
            ValidateReadLineEditorPane(editorPane);
            string? line = null;
            try {
                line = shell.RunBufferedEditor(
                    frame: frame,
                    trim: false,
                    cancellationToken: cancellationToken,
                    onInputStateChanged: state => {
                        onBufferedEditorState(ToBufferedEditorState(getEditorPane(), state));
                        return false;
                    },
                    onActivitySelectionRequested: onActivitySelectionRequested,
                    onSubmit: static (_, _, _) => true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                return CreateClosedCompletion();
            }

            return new SurfaceCompletion {
                Kind = SurfaceCompletionKind.Text,
                Accepted = true,
                TextResult = line,
            };
        }

        protected SurfaceCompletion ExecuteClientBufferedAuthoring(
            TerminalSurfaceRuntimeFrame frame,
            Func<EditorPaneRuntimeState> getEditorPane,
            Action<ClientBufferedEditorState> onBufferedEditorState,
            Action<ClientBufferedEditorState> onSubmitBufferedEditorState,
            Action<ConsoleKeyInfo> onKeyPressed,
            Action<int> onActivitySelectionRequested,
            CancellationToken cancellationToken) {
            if (!shell.IsInteractive) {
                return CreateClosedCompletion();
            }

            var editorPane = getEditorPane();
            ValidateAuthoringEditorPane(editorPane);

            try {
                shell.RunBufferedEditor(
                    frame: frame,
                    trim: false,
                    cancellationToken: cancellationToken,
                    onInputStateChanged: state => {
                        onBufferedEditorState(ToBufferedEditorState(getEditorPane(), state));
                        return false;
                    },
                    onKeyPressed: onKeyPressed,
                    onActivitySelectionRequested: onActivitySelectionRequested,
                    onSubmit: (state, _, _) => {
                        onSubmitBufferedEditorState(ToBufferedEditorState(getEditorPane(), state));
                        return false;
                    });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            }

            return CreateClosedCompletion();
        }

        protected void UpdateReadLineFrame(TerminalSurfaceRuntimeFrame? frame) {
            if (frame is not null) {
                shell.UpdateReadLineFrame(frame);
            }
        }

        protected TerminalSurfaceInteraction CreateBufferedAuthoringInteraction(
            InteractionScopeId scopeId,
            TerminalSurfaceInteraction? existingInteraction,
            LifecyclePayload? lifecycle,
            TerminalSurfaceRuntimeFrame? frame) {
            var interactionKind = string.IsNullOrWhiteSpace(lifecycle?.InteractionKind)
                ? existingInteraction?.Scope.Kind ?? "interaction"
                : lifecycle!.InteractionKind;
            return new TerminalSurfaceInteraction {
                Scope = new InteractionScope {
                    Id = scopeId,
                    State = InteractionScopeState.Active,
                    Kind = interactionKind,
                    IsTransient = lifecycle?.IsTransient ?? existingInteraction?.Scope.IsTransient ?? true,
                },
                Mode = TerminalSurfaceInteractionMode.Editor,
                EditorPane = frame?.EditorPane
                    ?? existingInteraction?.EditorPane
                    ?? new EditorPaneRuntimeState(),
            };
        }

        protected TerminalSurfaceInteraction CreateDisplayInteraction(
            InteractionScopeId scopeId,
            TerminalSurfaceInteraction? existingInteraction,
            LifecyclePayload? lifecycle) {
            var interactionKind = string.IsNullOrWhiteSpace(lifecycle?.InteractionKind)
                ? existingInteraction?.Scope.Kind ?? "interaction"
                : lifecycle!.InteractionKind;
            return new TerminalSurfaceInteraction {
                Scope = new InteractionScope {
                    Id = scopeId,
                    State = InteractionScopeState.Active,
                    Kind = interactionKind,
                    IsTransient = lifecycle?.IsTransient ?? existingInteraction?.Scope.IsTransient ?? true,
                },
                Mode = TerminalSurfaceInteractionMode.Display,
                EditorPane = existingInteraction?.EditorPane ?? new EditorPaneRuntimeState(),
            };
        }

        protected void ApplyBufferedInteractionFrame(TerminalSurfaceInteraction interaction, TerminalSurfaceRuntimeFrame frame) {
            ValidateInteractionFrameMode(interaction, frame);
            interaction.EditorPane = frame.EditorPane;
            shell.UpdateBufferedEditorFrame(frame);
        }

        protected void ApplyDisplayInteractionFrame(TerminalSurfaceInteraction interaction, TerminalSurfaceRuntimeFrame frame) {
            ValidateInteractionFrameMode(interaction, frame);
            shell.UpdateBufferedEditorFrame(frame);
        }

        protected SurfaceCompletion ExecuteDisplayInteraction(
            TerminalSurfaceRuntimeFrame frame,
            CancellationToken cancellationToken) {
            if (!shell.IsInteractive) {
                return CreateClosedCompletion();
            }

            shell.RunPassiveInteraction(frame, cancellationToken);
            return CreateClosedCompletion();
        }

        protected void ValidateInteractionFrameMode(TerminalSurfaceInteraction interaction, TerminalSurfaceRuntimeFrame frame) {
            if (interaction.Mode != frame.InteractionMode) {
                throw new InvalidOperationException(
                    $"Terminal surface runtime does not allow active interaction mode changes. Interaction={interaction.Mode}, Frame={frame.InteractionMode}.");
            }

            if (interaction.Mode == TerminalSurfaceInteractionMode.Display
                || interaction.EditorPane.HasSameInteractionMode(frame.EditorPane)) {
                return;
            }

            throw new InvalidOperationException(
                $"Terminal surface runtime does not allow active interaction editor mode changes. " +
                $"Interaction={DescribeEditorPane(interaction.EditorPane)}, Frame={DescribeEditorPane(frame.EditorPane)}.");
        }

        private static void ValidateReadLineEditorPane(EditorPaneRuntimeState editorPane) {
            if (editorPane.Kind != EditorPaneKind.SingleLine) {
                throw new InvalidOperationException($"Terminal surface runtime only supports a single-line editor pane for interactive reads. Actual kind: {editorPane.Kind}.");
            }

            if (editorPane.Authority != EditorAuthority.ClientBuffered) {
                throw new InvalidOperationException($"Terminal surface runtime requires a client-buffered editor pane. Actual authority: {editorPane.Authority}.");
            }

            if (!editorPane.AcceptsSubmit) {
                throw new InvalidOperationException("Terminal surface runtime cannot execute an editor pane that rejects submit.");
            }
        }

        private static void ValidateAuthoringEditorPane(EditorPaneRuntimeState editorPane) {
            if (editorPane.Kind == EditorPaneKind.ReadonlyBuffer) {
                throw new InvalidOperationException("Terminal surface runtime cannot execute a readonly editor pane as buffered authoring.");
            }

            if (editorPane.Authority != EditorAuthority.ClientBuffered) {
                throw new InvalidOperationException($"Terminal surface runtime requires a client-buffered editor pane. Actual authority: {editorPane.Authority}.");
            }

            if (!editorPane.AcceptsSubmit) {
                throw new InvalidOperationException("Terminal surface runtime cannot execute an editor pane that rejects submit.");
            }
        }

        private static ClientBufferedEditorState ToBufferedEditorState(EditorPaneRuntimeState editorPane, ClientBufferedEditorState state) {
            return state.WithEditorKind(editorPane.Kind);
        }

        private static SurfaceCompletion CreateClosedCompletion() {
            return new SurfaceCompletion {
                Kind = SurfaceCompletionKind.Closed,
                Accepted = true,
            };
        }

        private static string DescribeEditorPane(EditorPaneRuntimeState editorPane) {
            return $"Kind={editorPane.Kind}, Authority={editorPane.Authority}, ExpectedClientBufferRevision={editorPane.ExpectedClientBufferRevision}, RemoteRevision={editorPane.RemoteRevision}, AcceptsSubmit={editorPane.AcceptsSubmit}, Keymap={editorPane.Keymap.DispatchPolicy}";
        }
    }
}
