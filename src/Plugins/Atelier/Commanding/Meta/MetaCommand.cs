using Atelier.Presentation.Window;
using Atelier.Session;
using Atelier.Session.Context;

namespace Atelier.Commanding.Meta
{
    internal enum MetaCommandKind : byte
    {
        Unknown,
        Help,
        Reset,
        Clear,
        Imports,
        Target,
        Paste,
        Transient,
    }

    internal sealed record MetaCommand(
        MetaCommandKind Kind,
        string Name,
        string HeaderLine,
        string HeaderRemainder,
        string BodyText,
        string TransientCode);

    internal delegate ValueTask<MetaCommandResult> MetaCommandHandler(
        MetaCommandExecutionContext context,
        MetaCommand command,
        CancellationToken cancellationToken);

    internal sealed class MetaCommandInfo(
        MetaCommandKind kind,
        string name,
        Func<string> arguments,
        Func<string> summary,
        MetaCommandArgumentHint[] argumentHints,
        MetaCommandHandler executeAsync)
    {
        public MetaCommandKind Kind { get; } = kind;
        public string Name { get; } = name;
        public string Arguments => arguments();
        public string Summary => summary();
        public MetaCommandArgumentHint[] ArgumentHints { get; } = argumentHints;

        public ValueTask<MetaCommandResult> ExecuteAsync(
            MetaCommandExecutionContext context,
            MetaCommand command,
            CancellationToken cancellationToken) {
            return executeAsync(context, command, cancellationToken);
        }
    }

    internal sealed class MetaCommandArgumentHint(int argumentIndex, string value, Func<string> summary)
    {
        public int ArgumentIndex { get; } = argumentIndex;
        public string Value { get; } = value;
        public string Summary => summary();
    }

    internal sealed record MetaCommandResult(bool ClearDraft)
    {
        public static MetaCommandResult KeepDraft { get; } = new(false);
        public static MetaCommandResult ClearDraftInput { get; } = new(true);
    }

    internal interface IMetaCommandState
    {
        ReplSession Session { get; }
        OpenOptions Options { get; }
    }

    internal interface IMetaCommandEditor
    {
        EditorSubmitKeyMode SubmitKeyMode { get; }
        void SetSubmitKeyMode(EditorSubmitKeyMode mode, bool autoReset);
        void ResetTemporarySubmitKeyMode();
    }

    internal interface IMetaCommandExecution
    {
        void EnterBlockingExecutionState();
        void EndConsoleInteraction();
        void ExitBlockingExecutionState();
        void PublishTransientRunResult(RunResult runResult);
        void TrackBackgroundTask(RunResult runResult);
    }

    internal interface IMetaCommandOutput
    {
        void PublishLine(string text);
        void PublishClearTranscript();
        void PublishValueList(IReadOnlyList<string> values);
        void PublishErrorBox(string title, IReadOnlyList<string> lines);
    }

    internal sealed record MetaCommandExecutionContext(
        IMetaCommandState State,
        IMetaCommandOutput Output,
        IMetaCommandEditor Editor,
        IMetaCommandExecution Execution);

    internal static class MetaCommands {
        private static readonly MetaCommandInfo[] Items = [
            new(
                MetaCommandKind.Help,
                "help",
                static () => string.Empty,
                static () => GetString("Show REPL usage guidance."),
                [],
                ExecuteHelpAsync),
            new(
                MetaCommandKind.Reset,
                "reset",
                static () => string.Empty,
                static () => GetString("Reset session runtime state and draft."),
                [],
                ExecuteResetAsync),
            new(
                MetaCommandKind.Clear,
                "clear",
                static () => string.Empty,
                static () => GetString("Clear transcript output and redraw current frame."),
                [],
                ExecuteClearAsync),
            new(
                MetaCommandKind.Imports,
                "imports",
                static () => string.Empty,
                static () => GetString("Show baseline/effective imports and reference paths."),
                [],
                ExecuteImportsAsync),
            new(
                MetaCommandKind.Target,
                "target",
                static () => string.Empty,
                static () => GetString("Show current target and host."),
                [],
                ExecuteTargetAsync),
            new(
                MetaCommandKind.Paste,
                "paste",
                static () => GetString("[on|off]"),
                static () => GetString("Use Ctrl+Enter as submit for the next pasted source block."),
                [
                    new(0, "on", static () => GetString("Enable paste mode for the next submit.")),
                    new(0, "off", static () => GetString("Disable paste mode and restore smart submit.")),
                ],
                ExecutePasteAsync),
            new(
                MetaCommandKind.Transient,
                "transient",
                static () => GetString("<code>"),
                static () => GetString("Run transient code without committing."),
                [],
                ExecuteTransientAsync),
        ];

        public static IReadOnlyList<MetaCommandInfo> All => Items;

        public static bool TryResolve(string commandName, out MetaCommandInfo command) {
            var name = commandName.Trim();
            foreach (var candidate in Items) {
                if (!string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                command = candidate;
                return true;
            }

            command = default!;
            return false;
        }

        public static MetaCommandKind ResolveKind(string commandName) {
            return TryResolve(commandName, out var command) ? command.Kind : MetaCommandKind.Unknown;
        }

        public static ValueTask<MetaCommandResult> ExecuteAsync(
            MetaCommandExecutionContext context,
            MetaCommand command,
            CancellationToken cancellationToken) {
            return TryResolve(command.Name, out var definition)
                ? definition.ExecuteAsync(context, command, cancellationToken)
                : ExecuteUnknownAsync(context, command, cancellationToken);
        }

        private static ValueTask<MetaCommandResult> ExecuteHelpAsync(
            MetaCommandExecutionContext context,
            MetaCommand command,
            CancellationToken cancellationToken) {
            context.Output.PublishLine(GetString("Atelier REPL quick help:"));
            context.Output.PublishLine(GetString("  Mental model:"));
            context.Output.PublishLine(GetString("    Atelier is a Roslyn C# script session, not a line-by-line shell."));
            context.Output.PublishLine(GetString("    The editor analyzes committed history plus the current draft as one synthetic script."));
            context.Output.PublishLine(GetString("    Normal submissions that change state are committed to session history."));
            context.Output.PublishLine(GetString("    Later drafts see earlier declarations, imports, references, and loaded submissions."));
            context.Output.PublishLine(GetString("    Meta commands start with ':' and are intercepted before C# compilation."));
            context.Output.PublishLine(GetString("    :transient compiles against the current session, runs as a probe, then discards its changes."));
            context.Output.PublishLine(GetString("  Commands:"));
            foreach (var item in Items) {
                var insertion = ":" + item.Name;
                var usage = string.IsNullOrWhiteSpace(item.Arguments)
                    ? insertion
                    : insertion + " " + item.Arguments;
                context.Output.PublishLine(GetString("    {0} - {1}", usage, item.Summary));
            }

            context.Output.PublishLine(GetString("  Async and cancellation:"));
            context.Output.PublishLine(GetString("    Top-level await may keep running in background after the prompt returns."));
            context.Output.PublishLine(GetString("    Background execution Tasks are available as PendingTasks; LastTask is the newest one."));
            context.Output.PublishLine(GetString("    An unfinished Task returned as the result is also tracked by the window indicator."));
            context.Output.PublishLine(GetString("    The session Cancellation token is available as Cancellation."));
            context.Output.PublishLine(GetString("    Cancellation injection mainly prevents runaway loops from surviving a closed session."));
            context.Output.PublishLine(GetString("    Loops check Cancellation automatically, so intentional long-running loops can stop on close."));
            context.Output.PublishLine(GetString("    Token-aware calls may get Cancellation appended automatically when a safe overload exists."));
            context.Output.PublishLine(GetString("    Task.Run/Thread/Timer callbacks are guarded so detached work also observes cancellation."));
            context.Output.PublishLine(GetString("    Blocking APIs that ignore tokens still need explicit cancellation-aware design."));
            context.Output.PublishLine(GetString("  Lifetime:"));
            context.Output.PublishLine(GetString("    Closing the REPL releases the surface and session automatically."));
            context.Output.PublishLine(GetString("    Session disposal cancels pending work, clears PendingTasks, and unloads script contexts."));
            context.Output.PublishLine(GetString("    Reset clears committed history and restores baseline imports."));
            context.Output.PublishLine(GetString("  Editing:"));
            context.Output.PublishLine(GetString("    Enter follows Roslyn's complete-submission check; unfinished blocks stay open."));
            context.Output.PublishLine(GetString("    Shift+Enter always inserts a line; paste mode uses Ctrl+Enter to submit once."));
            context.Output.PublishLine(GetString("    Diagnostics, completions, and signature help describe the draft in accumulated context."));
            context.Output.PublishLine(GetString("    Pair closers can stay virtual until typed through."));
            context.Output.PublishLine(GetString("    Shift+Tab formats the draft without submitting."));
            return ValueTask.FromResult(MetaCommandResult.ClearDraftInput);
        }

        private static async ValueTask<MetaCommandResult> ExecuteResetAsync(
            MetaCommandExecutionContext context,
            MetaCommand command,
            CancellationToken cancellationToken) {
            var publication = await context.State.Session.ResetAsync(cancellationToken).ConfigureAwait(false);
            context.Output.PublishLine(publication.Invalidation is null
                ? GetString("Session reset.")
                : publication.Invalidation.Reason);
            return MetaCommandResult.KeepDraft;
        }

        private static ValueTask<MetaCommandResult> ExecuteClearAsync(
            MetaCommandExecutionContext context,
            MetaCommand command,
            CancellationToken cancellationToken) {
            context.Output.PublishClearTranscript();
            return ValueTask.FromResult(MetaCommandResult.ClearDraftInput);
        }

        private static ValueTask<MetaCommandResult> ExecuteImportsAsync(
            MetaCommandExecutionContext context,
            MetaCommand command,
            CancellationToken cancellationToken) {
            var importState = context.State.Session.ImportState;
            context.Output.PublishLine(GetString("BaselineImports ({0}):", importState.BaselineImports.Length));
            context.Output.PublishValueList(importState.BaselineImports);
            context.Output.PublishLine(GetString("EffectiveImports ({0}):", importState.EffectiveImports.Length));
            context.Output.PublishValueList(importState.EffectiveImports);
            context.Output.PublishLine(GetString("ReferencePaths ({0}):", importState.ReferencePaths.Length));
            context.Output.PublishValueList(importState.ReferencePaths);
            return ValueTask.FromResult(MetaCommandResult.ClearDraftInput);
        }

        private static ValueTask<MetaCommandResult> ExecuteTargetAsync(
            MetaCommandExecutionContext context,
            MetaCommand command,
            CancellationToken cancellationToken) {
            context.Output.PublishLine(GetString("Target: {0}", context.State.Options.TargetProfile.Label));
            context.Output.PublishLine(GetString("Host: {0}", context.State.Options.InvocationHost.Label));
            return ValueTask.FromResult(MetaCommandResult.ClearDraftInput);
        }

        private static ValueTask<MetaCommandResult> ExecutePasteAsync(
            MetaCommandExecutionContext context,
            MetaCommand command,
            CancellationToken cancellationToken) {
            if (!TryResolvePasteMode(context, command.HeaderRemainder, out var mode, out var autoReset)) {
                context.Output.PublishLine(GetString("Usage: :paste [on|off]"));
                return ValueTask.FromResult(MetaCommandResult.ClearDraftInput);
            }

            context.Editor.SetSubmitKeyMode(mode, autoReset);
            context.Output.PublishLine(mode == EditorSubmitKeyMode.CtrlEnter
                ? GetString("Paste mode enabled for the next submit. Enter inserts new lines; Ctrl+Enter submits.")
                : GetString("Paste mode disabled. Enter uses smart submit; Shift+Enter inserts new lines."));
            return ValueTask.FromResult(MetaCommandResult.ClearDraftInput);
        }

        private static async ValueTask<MetaCommandResult> ExecuteTransientAsync(
            MetaCommandExecutionContext context,
            MetaCommand command,
            CancellationToken cancellationToken) {
            context.Output.PublishLine(GetString("Running transient code."));
            RunResult? runResult = null;
            context.Editor.ResetTemporarySubmitKeyMode();
            context.Execution.EnterBlockingExecutionState();
            try {
                runResult = await context.State.Session.QueueTransientRunAsync(command.TransientCode, cancellationToken).ConfigureAwait(false);
                context.Execution.PublishTransientRunResult(runResult);
                context.Execution.TrackBackgroundTask(runResult);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
                context.Output.PublishErrorBox(GetString("Execution"), [ex.Message]);
            }
            finally {
                context.Execution.EndConsoleInteraction();
                context.Execution.ExitBlockingExecutionState();
            }

            return MetaCommandResult.ClearDraftInput;
        }

        private static ValueTask<MetaCommandResult> ExecuteUnknownAsync(
            MetaCommandExecutionContext context,
            MetaCommand command,
            CancellationToken cancellationToken) {
            context.Output.PublishLine(string.IsNullOrWhiteSpace(command.Name)
                ? GetString("Unknown command. Type ':' for command suggestions, or run :help for usage guidance.")
                : GetString("Unknown command ':{0}'. Type ':' for command suggestions, or run :help for usage guidance.", command.Name));
            return ValueTask.FromResult(MetaCommandResult.ClearDraftInput);
        }

        private static bool TryResolvePasteMode(
            MetaCommandExecutionContext context,
            string text,
            out EditorSubmitKeyMode mode,
            out bool autoReset) {
            var value = (text ?? string.Empty).Trim();
            if (value.Length == 0) {
                var pasteModeActive = context.Editor.SubmitKeyMode == EditorSubmitKeyMode.CtrlEnter;
                mode = pasteModeActive ? EditorSubmitKeyMode.Enter : EditorSubmitKeyMode.CtrlEnter;
                autoReset = !pasteModeActive;
                return true;
            }

            switch (value.ToLowerInvariant()) {
                case "on":
                case "ctrl":
                case "ctrl-enter":
                case "ctrl+enter":
                    mode = EditorSubmitKeyMode.CtrlEnter;
                    autoReset = true;
                    return true;
                case "off":
                case "enter":
                case "normal":
                    mode = EditorSubmitKeyMode.Enter;
                    autoReset = false;
                    return true;
                default:
                    mode = default;
                    autoReset = false;
                    return false;
            }
        }
    }
}
