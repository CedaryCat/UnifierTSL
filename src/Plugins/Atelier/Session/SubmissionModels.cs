using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Scripting;
using System.Collections.Immutable;
using System.Reflection;
using UnifierTSL.Surface.Prompting.Model;

namespace Atelier.Session
{
    internal sealed record ManagedPluginReference(
        string StableKey,
        string DisplayName,
        AssemblyName RootAssemblyName,
        string AssemblyPath);

    internal sealed record FrontendConfiguration(
        ScriptOptions ScriptOptions,
        CSharpParseOptions ParseOptions,
        CSharpCompilationOptions CompilationOptions,
        ImmutableArray<MetadataReference> MetadataReferences,
        ImmutableArray<string> Imports,
        ImmutableArray<string> ReferencePaths,
        bool UseKAndRBraceStyle,
        bool UseSmartSubmitDetection,
        Type GlobalsType,
        string HostOutExpression,
        ImmutableArray<ManagedPluginReference> ManagedPluginReferences);

    internal sealed record ExecutionConfiguration(
        Type GlobalsType,
        Assembly HostAssembly);

    internal sealed record SessionConfiguration(
        FrontendConfiguration Frontend,
        ExecutionConfiguration Execution);

    internal sealed record CommittedSubmission(long Revision, string Text, DateTimeOffset TimestampUtc);

    internal sealed record SyntheticDocument(
        string Text,
        string DraftText,
        int DraftStart,
        int DraftLength,
        int CaretIndex,
        ImmutableArray<SourceTextMarker> SourceMarkers)
    {
        public int DraftEnd => DraftStart + DraftLength;

        public int SyntheticCaretIndex => Math.Clamp(DraftStart + CaretIndex, DraftStart, DraftEnd);

        public bool TryMapDraftSourceSpan(int draftSourceStart, int draftSourceLength, out int encodedStart, out int encodedLength)
        {
            return DraftMarkers.TryMapSourceSpan(SourceMarkers, DraftLength, draftSourceStart, draftSourceLength, out encodedStart, out encodedLength);
        }
    }

    internal sealed record DiagnosticInfo(
        string Id,
        DiagnosticSeverity Severity,
        string Message,
        int? DraftStartIndex,
        int? DraftLength,
        string DisplayText);

    internal sealed record SignatureHelpSection(
        string Label,
        ImmutableArray<PromptStyledText> Lines);

    internal sealed record SignatureHelpItem(
        string Id,
        PromptStyledText Summary,
        ImmutableArray<SignatureHelpSection> Sections);

    internal sealed record SignatureHelpInfo(
        ImmutableArray<SignatureHelpItem> Items,
        string ActiveItemId,
        int ActiveItemIndex)
    {
        public static SignatureHelpInfo Empty { get; } = new([], string.Empty, -1);
    }

    internal enum CompletionTriggerMode : byte
    {
        Manual,
        Automatic,
    }

    internal sealed record CompletionInfo(
        ImmutableArray<PromptCompletionItem> Items,
        string PreferredCompletionText,
        CompletionTriggerMode ActivationMode)
    {
        public static CompletionInfo Empty { get; } = new([], string.Empty, CompletionTriggerMode.Manual);
    }

    internal sealed record WorkspaceAnalysis(
        ImmutableArray<DiagnosticInfo> Diagnostics,
        ImmutableArray<PromptHighlightSpan> DraftSourceHighlights,
        SignatureHelpInfo SignatureHelp,
        CompletionInfo Completion,
        bool IsCompleteSubmission)
    {
        public static WorkspaceAnalysis Empty { get; } = new([], [], SignatureHelpInfo.Empty, CompletionInfo.Empty, true);
    }

    internal enum OperationKind : byte
    {
        Idle,
        PersistentSubmit,
        TransientRun,
        Reset,
    }

    internal enum OutcomeKind : byte
    {
        None,
        Pending,
        Invalidated,
        CompilationFailed,
        Executed,
        RuntimeFailed,
        Cancelled,
        Reset,
    }

    internal enum ExecutionPhase : byte
    {
        None,
        Foreground,
        Background,
    }

    internal sealed record BackgroundExecution(
        long Serial,
        bool IsPersistent,
        Task<RunResult> CompletionTask);

    internal sealed record ExecutionLaunch(
        RunResult RunResult,
        Task<RunResult>? BackgroundCompletion = null)
    {
        public bool IsBackground => BackgroundCompletion is not null;
    }

    internal sealed record RunResult(
        OperationKind Operation,
        OutcomeKind Outcome,
        ExecutionPhase ExecutionPhase,
        ImmutableArray<DiagnosticInfo> Diagnostics,
        object? ReturnValue,
        bool HasReturnValue,
        Exception? Exception,
        bool StateChanged,
        long ExecutionSerial,
        BackgroundExecution? BackgroundExecution,
        DateTimeOffset TimestampUtc)
    {
        public static RunResult Idle { get; } = new(
            OperationKind.Idle,
            OutcomeKind.None,
            ExecutionPhase.None,
            [],
            null,
            false,
            null,
            false,
            0,
            null,
            DateTimeOffset.UtcNow);

        public static RunResult Pending(
            OperationKind operation,
            long executionSerial = 0,
            ExecutionPhase executionPhase = ExecutionPhase.Foreground,
            bool stateChanged = false) {
            return new RunResult(
                operation,
                OutcomeKind.Pending,
                executionPhase,
                [],
                null,
                false,
                null,
                stateChanged,
                executionSerial,
                null,
                DateTimeOffset.UtcNow);
        }

        public static RunResult ResetResult() {
            return new RunResult(
                OperationKind.Reset,
                OutcomeKind.Reset,
                ExecutionPhase.None,
                [],
                null,
                false,
                null,
                true,
                0,
                null,
                DateTimeOffset.UtcNow);
        }

        public static RunResult Invalidated(OperationKind operation, string reason, long executionSerial = 0) {
            return new RunResult(
                operation,
                OutcomeKind.Invalidated,
                ExecutionPhase.None,
                [],
                null,
                false,
                new InvalidOperationException(reason),
                false,
                executionSerial,
                null,
                DateTimeOffset.UtcNow);
        }

        public RunResult WithExecutionSerial(long executionSerial) {
            return this with {
                ExecutionSerial = executionSerial,
            };
        }

        public RunResult WithBackgroundExecution(long executionSerial, bool isPersistent, Task<RunResult> completionTask) {

            return this with {
                ExecutionSerial = executionSerial,
                ExecutionPhase = ExecutionPhase.Background,
                BackgroundExecution = new BackgroundExecution(executionSerial, isPersistent, completionTask),
            };
        }
    }

    internal sealed record SessionInvalidation(string Reason, DateTimeOffset TimestampUtc);

    internal sealed record SessionPublication(
        SessionRevision Revision,
        ImmutableArray<CommittedSubmission> CommittedHistory,
        SyntheticDocument SyntheticDocument,
        WorkspaceAnalysis Workspace,
        SessionInvalidation? Invalidation,
        string SourceText);
}
