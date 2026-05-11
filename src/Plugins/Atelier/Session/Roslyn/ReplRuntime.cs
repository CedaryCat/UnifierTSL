using Atelier.Session.Context;
using Atelier.Session.Execution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using System.Collections.Immutable;

namespace Atelier.Session.Roslyn
{
    internal sealed class ReplRuntime : IDisposable
    {
        private readonly SessionConfiguration configuration;
        private readonly ScriptGlobals globals;
        private readonly ManagedPluginAssemblyCatalog managedPluginCatalog;
        private readonly ExecutionReferencePlanner referencePlanner;
        private readonly SubmissionPreprocessor submissionPreprocessor;
        private readonly SemaphoreSlim executionGate = new(1, 1);
        private readonly Lock managedPluginKeySync = new();
        private ImmutableArray<string> attachedManagedPluginKeys = [];
        private Dictionary<string, int> runningManagedPluginKeyCounts = new(StringComparer.Ordinal);
        private PersistentRuntimeState? persistentState;
        private bool disposed;

        public ReplRuntime(
            SessionConfiguration configuration,
            ScriptGlobals globals,
            ManagedPluginAssemblyCatalog managedPluginCatalog) {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.globals = globals ?? throw new ArgumentNullException(nameof(globals));
            this.managedPluginCatalog = managedPluginCatalog ?? throw new ArgumentNullException(nameof(managedPluginCatalog));
            referencePlanner = new ExecutionReferencePlanner(configuration.Frontend.ManagedPluginReferences);
            submissionPreprocessor = new SubmissionPreprocessor(configuration.Frontend.HostOutExpression);
        }

        public ImmutableArray<string> AttachedManagedPluginKeys {
            get {
                lock (managedPluginKeySync) {
                    return [.. attachedManagedPluginKeys.Union(runningManagedPluginKeyCounts.Keys, StringComparer.Ordinal)];
                }
            }
        }

        public async Task<ExecutionLaunch> RunPersistentAsync(string code, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(disposed, this);

            await executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                if (!TryPrepareRun(
                        code,
                        OperationKind.PersistentSubmit,
                        cancellationToken,
                        out var prepared,
                        out var failureLaunch)) {
                    return failureLaunch!;
                }

                var run = prepared!;
                var state = EnsurePersistentState();
                lock (managedPluginKeySync) {
                    attachedManagedPluginKeys = [.. attachedManagedPluginKeys.Union(run.ManagedPluginKeys, StringComparer.Ordinal)];
                }

                try {
                    var launch = state.ExecutionSession.LaunchPersistent(run.Submission, run.ManagedPluginKeys, cancellationToken);
                    state.Script = run.Script;
                    if (launch.PendingTask is null) {
                        return new ExecutionLaunch(CreateExecutionResult(
                            OperationKind.PersistentSubmit,
                            launch.ImmediateReturnValue,
                            run.HasReturnValue,
                            stateChanged: launch.StateAccepted));
                    }

                    state.RetainBackgroundExecution();
                    AddRunningManagedPluginKeys(run.ManagedPluginKeys);
                    return new ExecutionLaunch(
                        RunResult.Pending(
                            OperationKind.PersistentSubmit,
                            executionPhase: ExecutionPhase.Background,
                            stateChanged: launch.StateAccepted),
                        AwaitStateBoundBackgroundExecutionAsync(
                            state,
                            launch.PendingTask,
                            OperationKind.PersistentSubmit,
                            run.Diagnostics,
                            run.ManagedPluginKeys,
                            run.HasReturnValue,
                            stateChanged: launch.StateAccepted,
                            cancellationToken));
                }
                catch (Exception ex) {
                    return new ExecutionLaunch(CreateRuntimeFailureResult(
                        OperationKind.PersistentSubmit,
                        run.Diagnostics,
                        ex,
                        stateChanged: false,
                        cancellationToken));
                }
            }
            finally {
                executionGate.Release();
            }
        }

        public async Task<ExecutionLaunch> RunTransientAsync(string code, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(disposed, this);

            await executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                if (!TryPrepareRun(
                        code,
                        OperationKind.TransientRun,
                        cancellationToken,
                        out var prepared,
                        out var failureLaunch)) {
                    return failureLaunch!;
                }

                var run = prepared!;
                try {
                    if (persistentState is null) {
                        var transientSession = new ExecutionSession(configuration.Execution, managedPluginCatalog, globals);
                        var launch = transientSession.LaunchPersistent(run.Submission, run.ManagedPluginKeys, cancellationToken);
                        if (launch.PendingTask is null) {
                            transientSession.Dispose();
                            return new ExecutionLaunch(CreateExecutionResult(
                                OperationKind.TransientRun,
                                launch.ImmediateReturnValue,
                                run.HasReturnValue,
                                stateChanged: false));
                        }

                        AddRunningManagedPluginKeys(run.ManagedPluginKeys);
                        return new ExecutionLaunch(
                            RunResult.Pending(
                                OperationKind.TransientRun,
                                executionPhase: ExecutionPhase.Background,
                                stateChanged: false),
                            AwaitDetachedBackgroundExecutionAsync(
                                transientSession,
                                launch.PendingTask,
                                OperationKind.TransientRun,
                                run.Diagnostics,
                                run.ManagedPluginKeys,
                                run.HasReturnValue,
                                stateChanged: false,
                                cancellationToken));
                    }

                    var state = persistentState;
                    var launchFromState = state.ExecutionSession.LaunchTransient(run.Submission, run.ManagedPluginKeys, cancellationToken);
                    if (launchFromState.PendingTask is null) {
                        return new ExecutionLaunch(CreateExecutionResult(
                            OperationKind.TransientRun,
                            launchFromState.ImmediateReturnValue,
                            run.HasReturnValue,
                            stateChanged: false));
                    }

                    state.RetainBackgroundExecution();
                    AddRunningManagedPluginKeys(run.ManagedPluginKeys);
                    return new ExecutionLaunch(
                        RunResult.Pending(
                            OperationKind.TransientRun,
                            executionPhase: ExecutionPhase.Background,
                            stateChanged: false),
                        AwaitStateBoundBackgroundExecutionAsync(
                            state,
                            launchFromState.PendingTask,
                            OperationKind.TransientRun,
                            run.Diagnostics,
                            run.ManagedPluginKeys,
                            run.HasReturnValue,
                            stateChanged: false,
                            cancellationToken));
                }
                catch (Exception ex) {
                    return new ExecutionLaunch(CreateRuntimeFailureResult(
                        OperationKind.TransientRun,
                        run.Diagnostics,
                        ex,
                        stateChanged: false,
                        cancellationToken));
                }
            }
            finally {
                executionGate.Release();
            }
        }

        public RunResult Reset() {
            ObjectDisposedException.ThrowIf(disposed, this);

            executionGate.Wait();
            try {
                RetirePersistentState();
                lock (managedPluginKeySync) {
                    attachedManagedPluginKeys = [];
                }
                return RunResult.ResetResult();
            }
            finally {
                executionGate.Release();
            }
        }

        public void Invalidate() {
            executionGate.Wait();
            try {
                RetirePersistentState();
                lock (managedPluginKeySync) {
                    attachedManagedPluginKeys = [];
                }
            }
            finally {
                executionGate.Release();
            }
        }

        public void Dispose() {
            if (disposed) {
                return;
            }

            executionGate.Wait();
            try {
                if (disposed) {
                    return;
                }

                disposed = true;
                RetirePersistentState();
                lock (managedPluginKeySync) {
                    attachedManagedPluginKeys = [];
                }
            }
            finally {
                executionGate.Release();
                executionGate.Dispose();
            }
        }

        private Script<object?> CreateScript(string code) {
            return persistentState?.Script is { } currentScript
                ? currentScript.ContinueWith(code, configuration.Frontend.ScriptOptions)
                : CSharpScript.Create(code, configuration.Frontend.ScriptOptions, configuration.Frontend.GlobalsType);
        }

        private Script<object?> CreatePreparedScript(string code, CancellationToken cancellationToken) {
            return submissionPreprocessor.RewriteScript(CreateScript(code), CreateScript, cancellationToken);
        }

        private bool TryPrepareRun(
            string code,
            OperationKind operation,
            CancellationToken cancellationToken,
            out PreparedRun? prepared,
            out ExecutionLaunch? failureLaunch) {
            var script = CreatePreparedScript(code ?? string.Empty, cancellationToken);
            var diagnostics = CreateDiagnostics(script.Compile(cancellationToken));
            if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) {
                prepared = null;
                failureLaunch = new ExecutionLaunch(CreateCompilationFailureResult(operation, diagnostics));
                return false;
            }

            var compilation = script.GetCompilation();
            var emitDiagnostics = TryPrepareSubmission(compilation, cancellationToken, out var preparedSubmission);
            if (emitDiagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) {
                prepared = null;
                failureLaunch = new ExecutionLaunch(CreateCompilationFailureResult(operation, emitDiagnostics));
                return false;
            }

            prepared = new PreparedRun(
                script,
                diagnostics,
                preparedSubmission!,
                referencePlanner.ResolveManagedPluginKeys(script, cancellationToken),
                HasValueReturningStatement(compilation, cancellationToken));
            failureLaunch = null;
            return true;
        }

        private PersistentRuntimeState EnsurePersistentState() {
            persistentState ??= new PersistentRuntimeState(new ExecutionSession(configuration.Execution, managedPluginCatalog, globals));
            return persistentState;
        }

        private void RetirePersistentState() {
            if (persistentState is not { } state) {
                return;
            }

            persistentState = null;
            state.Script = null;
            state.RetireOrDispose();
        }

        private async Task<RunResult> AwaitStateBoundBackgroundExecutionAsync(
            PersistentRuntimeState state,
            Task<object?> executionTask,
            OperationKind operation,
            ImmutableArray<DiagnosticInfo> diagnostics,
            ImmutableArray<string> managedPluginKeys,
            bool hasReturnValue,
            bool stateChanged,
            CancellationToken cancellationToken) {
            try {
                var returnValue = await executionTask.ConfigureAwait(false);
                return CreateExecutionResult(operation, returnValue, hasReturnValue, stateChanged);
            }
            catch (Exception ex) {
                return CreateRuntimeFailureResult(operation, diagnostics, ex, stateChanged, cancellationToken);
            }
            finally {
                RemoveRunningManagedPluginKeys(managedPluginKeys);
                state.ReleaseBackgroundExecution();
            }
        }

        private async Task<RunResult> AwaitDetachedBackgroundExecutionAsync(
            ExecutionSession transientSession,
            Task<object?> executionTask,
            OperationKind operation,
            ImmutableArray<DiagnosticInfo> diagnostics,
            ImmutableArray<string> managedPluginKeys,
            bool hasReturnValue,
            bool stateChanged,
            CancellationToken cancellationToken) {
            try {
                var returnValue = await executionTask.ConfigureAwait(false);
                return CreateExecutionResult(operation, returnValue, hasReturnValue, stateChanged);
            }
            catch (Exception ex) {
                return CreateRuntimeFailureResult(operation, diagnostics, ex, stateChanged, cancellationToken);
            }
            finally {
                RemoveRunningManagedPluginKeys(managedPluginKeys);
                transientSession.Dispose();
            }
        }

        private void AddRunningManagedPluginKeys(ImmutableArray<string> managedPluginKeys) {
            if (managedPluginKeys.IsDefaultOrEmpty) {
                return;
            }

            lock (managedPluginKeySync) {
                foreach (var key in managedPluginKeys) {
                    if (string.IsNullOrWhiteSpace(key)) {
                        continue;
                    }

                    runningManagedPluginKeyCounts.TryGetValue(key, out var count);
                    runningManagedPluginKeyCounts[key] = count + 1;
                }
            }
        }

        private void RemoveRunningManagedPluginKeys(ImmutableArray<string> managedPluginKeys) {
            if (managedPluginKeys.IsDefaultOrEmpty) {
                return;
            }

            lock (managedPluginKeySync) {
                foreach (var key in managedPluginKeys) {
                    if (!runningManagedPluginKeyCounts.TryGetValue(key, out var count)) {
                        continue;
                    }

                    if (count <= 1) {
                        runningManagedPluginKeyCounts.Remove(key);
                        continue;
                    }

                    runningManagedPluginKeyCounts[key] = count - 1;
                }
            }
        }

        private static ImmutableArray<DiagnosticInfo> TryPrepareSubmission(
            Compilation compilation,
            CancellationToken cancellationToken,
            out PreparedSubmission? preparedSubmission) {
            cancellationToken.ThrowIfCancellationRequested();

            using var peStream = new MemoryStream();
            using var pdbStream = new MemoryStream();
            var emitResult = compilation.Emit(peStream, pdbStream, cancellationToken: cancellationToken);
            if (!emitResult.Success) {
                preparedSubmission = null;
                return CreateDiagnostics(emitResult.Diagnostics);
            }

            var entryPoint = compilation.GetEntryPoint(cancellationToken)
                ?? throw new InvalidOperationException(GetString("Atelier submission compilation is missing an entry point."));
            preparedSubmission = new PreparedSubmission(
                peStream.ToArray(),
                pdbStream.ToArray(),
                ResolveEntryTypeName(entryPoint),
                entryPoint.MetadataName);
            return [];
        }

        private static string ResolveEntryTypeName(IMethodSymbol entryPoint) {
            var containingType = entryPoint.ContainingType
                ?? throw new InvalidOperationException(GetString("Atelier submission entry point is missing a containing type."));
            var typeSegments = new Stack<string>();
            for (var currentType = containingType; currentType is not null; currentType = currentType.ContainingType) {
                typeSegments.Push(currentType.MetadataName);
            }

            var typeName = string.Join("+", typeSegments);
            return containingType.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace
                ? $"{containingNamespace.ToDisplayString()}.{typeName}"
                : typeName;
        }

        private static RunResult CreateCompilationFailureResult(
            OperationKind operation,
            ImmutableArray<DiagnosticInfo> diagnostics) {
            return new RunResult(
                operation,
                OutcomeKind.CompilationFailed,
                ExecutionPhase.None,
                diagnostics,
                null,
                false,
                null,
                false,
                0,
                null,
                DateTimeOffset.UtcNow);
        }

        private static RunResult CreateRuntimeFailureResult(
            OperationKind operation,
            ImmutableArray<DiagnosticInfo> diagnostics,
            Exception exception,
            bool stateChanged,
            CancellationToken cancellationToken) {
            var unwrapped = CancellationGuard.Unwrap(exception);
            if (CancellationGuard.IsRequestedBy(cancellationToken, unwrapped)) {
                return CreateCancelledResult(operation, diagnostics, unwrapped, stateChanged);
            }

            return new RunResult(
                operation,
                OutcomeKind.RuntimeFailed,
                ExecutionPhase.None,
                diagnostics,
                null,
                false,
                unwrapped,
                stateChanged,
                0,
                null,
                DateTimeOffset.UtcNow);
        }

        private static RunResult CreateCancelledResult(
            OperationKind operation,
            ImmutableArray<DiagnosticInfo> diagnostics,
            Exception exception,
            bool stateChanged) {
            return new RunResult(
                operation,
                OutcomeKind.Cancelled,
                ExecutionPhase.None,
                diagnostics,
                null,
                false,
                exception,
                stateChanged,
                0,
                null,
                DateTimeOffset.UtcNow);
        }

        private static RunResult CreateExecutionResult(
            OperationKind operation,
            object? returnValue,
            bool hasReturnValue,
            bool stateChanged) {
            return new RunResult(
                operation,
                OutcomeKind.Executed,
                ExecutionPhase.None,
                [],
                returnValue,
                hasReturnValue,
                null,
                stateChanged,
                0,
                null,
                DateTimeOffset.UtcNow);
        }

        private static bool HasValueReturningStatement(Compilation compilation, CancellationToken cancellationToken) {
            var syntaxTree = compilation.SyntaxTrees.LastOrDefault();
            if (syntaxTree is null) {
                return false;
            }

            var root = syntaxTree.GetRoot(cancellationToken);
            if (root is not CompilationUnitSyntax compilationUnit
                || compilationUnit.Members.Count == 0
                || compilationUnit.Members[^1] is not GlobalStatementSyntax {
                    Statement: ExpressionStatementSyntax { SemicolonToken.IsMissing: true } expressionStatement
                }) {
                return false;
            }

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var returnType = semanticModel.GetTypeInfo(expressionStatement.Expression, cancellationToken).ConvertedType;
            return returnType?.SpecialType is not (null or SpecialType.System_Void);
        }

        private static ImmutableArray<DiagnosticInfo> CreateDiagnostics(IEnumerable<Diagnostic> diagnostics) {
            return [.. diagnostics
                .Where(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden)
                .Select(static diagnostic => new DiagnosticInfo(
                    diagnostic.Id,
                    diagnostic.Severity,
                    diagnostic.GetMessage(),
                    diagnostic.Location.IsInSource ? diagnostic.Location.SourceSpan.Start : null,
                    diagnostic.Location.IsInSource ? diagnostic.Location.SourceSpan.Length : null,
                    BuildDisplayText(diagnostic)))];
        }

        private static string BuildDisplayText(Diagnostic diagnostic) {
            var severity = diagnostic.Severity.ToString().ToLowerInvariant();
            if (!diagnostic.Location.IsInSource) {
                return $"{severity} {diagnostic.Id}: {diagnostic.GetMessage()}";
            }

            var span = diagnostic.Location.GetMappedLineSpan();
            var line = span.StartLinePosition.Line + 1;
            var column = span.StartLinePosition.Character + 1;
            return $"{severity} {diagnostic.Id} L{line}:C{column}: {diagnostic.GetMessage()}";
        }

        private sealed record PreparedRun(
            Script<object?> Script,
            ImmutableArray<DiagnosticInfo> Diagnostics,
            PreparedSubmission Submission,
            ImmutableArray<string> ManagedPluginKeys,
            bool HasReturnValue);

        private sealed class PersistentRuntimeState
        {
            private readonly Lock sync = new();

            public PersistentRuntimeState(ExecutionSession executionSession) {
                ExecutionSession = executionSession ?? throw new ArgumentNullException(nameof(executionSession));
            }

            public ExecutionSession ExecutionSession { get; }

            public Script<object?>? Script { get; set; }

            public void RetainBackgroundExecution() {
                lock (sync) {
                    ActiveBackgroundCount++;
                }
            }

            public void ReleaseBackgroundExecution() {
                var disposeNow = false;
                lock (sync) {
                    if (ActiveBackgroundCount > 0) {
                        ActiveBackgroundCount--;
                    }

                    disposeNow = Retired && ActiveBackgroundCount == 0;
                }

                if (disposeNow) {
                    ExecutionSession.Dispose();
                }
            }

            public void RetireOrDispose() {
                var disposeNow = false;
                lock (sync) {
                    Retired = true;
                    disposeNow = ActiveBackgroundCount == 0;
                }

                if (disposeNow) {
                    ExecutionSession.Dispose();
                }
            }

            private int ActiveBackgroundCount { get; set; }

            private bool Retired { get; set; }
        }
    }
}
