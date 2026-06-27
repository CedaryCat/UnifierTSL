using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Sources;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;

namespace UnifierTSL.Servers
{
    public abstract class ServerDispatcher(ServerContext server) : IDisposable
    {
        protected ServerContext Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

        public abstract ServerDispatchDomain Domain { get; }
        public abstract TaskScheduler Scheduler { get; }

        public abstract bool CheckAccess();

        public abstract void Post(Action action);

        public abstract Task InvokeAsync(Action action, CancellationToken cancellationToken = default);

        public abstract Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default);

        public abstract Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default);

        public abstract Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default);

        public abstract ValueTask SwitchAsync(CancellationToken cancellationToken = default);

        public abstract void Dispose();
    }

    public abstract class QueuedServerDispatcher : ServerDispatcher
    {
        private readonly Queue<DispatchWork> queue = [];
        private readonly Lock gate = new();
        private readonly DispatcherTaskScheduler scheduler;
        private readonly DispatcherSynchronizationContext synchronizationContext;
        private int disposed;

        protected QueuedServerDispatcher(ServerContext server) : base(server) {
            scheduler = new(this);
            synchronizationContext = new(this);
        }

        public sealed override TaskScheduler Scheduler => scheduler;

        protected bool IsDisposed => Volatile.Read(ref disposed) != 0;

        protected abstract void EnsureTargetAvailable();

        public override void Post(Action action) {
            ArgumentNullException.ThrowIfNull(action);
            Enqueue(new(WrapPostedAction(action), null));
        }

        public override Task InvokeAsync(Action action, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(action);
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled(cancellationToken);
            }

            EnsureAvailable();
            if (CheckAccess()) {
                try {
                    RunInDispatchContext(action);
                    return Task.CompletedTask;
                }
                catch (Exception ex) {
                    return Task.FromException(ex);
                }
            }

            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(new(() => {
                if (cancellationToken.IsCancellationRequested) {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try {
                    action();
                    completion.TrySetResult();
                }
                catch (Exception ex) {
                    completion.TrySetException(ex);
                }
            }, ex => completion.TrySetException(ex)));
            return completion.Task;
        }

        public override Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(action);
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled<T>(cancellationToken);
            }

            EnsureAvailable();
            if (CheckAccess()) {
                try {
                    return Task.FromResult(RunInDispatchContext(action));
                }
                catch (Exception ex) {
                    return Task.FromException<T>(ex);
                }
            }

            TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(new(() => {
                if (cancellationToken.IsCancellationRequested) {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try {
                    completion.TrySetResult(action());
                }
                catch (Exception ex) {
                    completion.TrySetException(ex);
                }
            }, ex => completion.TrySetException(ex)));
            return completion.Task;
        }

        public override Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(action);
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled(cancellationToken);
            }

            EnsureAvailable();
            if (CheckAccess()) {
                return InvokeTaskInline(action);
            }

            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(new(() => {
                if (cancellationToken.IsCancellationRequested) {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                Task task;
                try {
                    task = action() ?? Task.CompletedTask;
                }
                catch (Exception ex) {
                    completion.TrySetException(ex);
                    return;
                }

                Complete(task, completion);
            }, ex => completion.TrySetException(ex)));
            return completion.Task;
        }

        public override Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(action);
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled<T>(cancellationToken);
            }

            EnsureAvailable();
            if (CheckAccess()) {
                return InvokeTaskInline(action, cancellationToken);
            }

            TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(new(() => {
                if (cancellationToken.IsCancellationRequested) {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                Task<T> task;
                try {
                    task = action() ?? Task.FromException<T>(new InvalidOperationException("The dispatched async callback returned null."));
                }
                catch (Exception ex) {
                    completion.TrySetException(ex);
                    return;
                }

                Complete(task, completion);
            }, ex => completion.TrySetException(ex)));
            return completion.Task;
        }

        public override ValueTask SwitchAsync(CancellationToken cancellationToken = default) {
            if (cancellationToken.IsCancellationRequested) {
                return ValueTask.FromCanceled(cancellationToken);
            }

            EnsureAvailable();
            if (CheckAccess()) {
                return ValueTask.CompletedTask;
            }

            return new DispatchSwitchSource(this, cancellationToken).CreateValueTask();
        }

        protected int DrainQueue(int maxWorkItems = int.MaxValue) {
            if (maxWorkItems <= 0) {
                throw new ArgumentOutOfRangeException(nameof(maxWorkItems));
            }

            var completed = 0;
            while (completed < maxWorkItems) {
                DispatchWork work;
                lock (gate) {
                    if (disposed != 0 || !queue.TryDequeue(out work!)) {
                        return completed;
                    }
                }

                RunInDispatchContext(work.Invoke);
                completed++;
            }

            return completed;
        }

        protected void RunInDispatchContext(Action callback) {
            var previous = SynchronizationContext.Current;
            try {
                SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                callback();
            }
            finally {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        protected T RunInDispatchContext<T>(Func<T> callback) {
            var previous = SynchronizationContext.Current;
            try {
                SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                return callback();
            }
            finally {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        protected void RejectPending(Exception exception) {
            DispatchWork[] pending;
            lock (gate) {
                pending = [.. queue];
                queue.Clear();
            }

            foreach (var work in pending) {
                work.Reject?.Invoke(exception);
            }
        }

        protected bool TryDisposeQueue() {
            DispatchWork[] pending;
            lock (gate) {
                if (disposed != 0) {
                    return false;
                }

                disposed = 1;
                pending = [.. queue];
                queue.Clear();
            }

            var exception = new ObjectDisposedException(GetType().Name);
            foreach (var work in pending) {
                work.Reject?.Invoke(exception);
            }

            return true;
        }

        protected void EnsureAvailable() {
            lock (gate) {
                ObjectDisposedException.ThrowIf(disposed != 0, this);
                EnsureTargetAvailable();
            }
        }

        private void Enqueue(DispatchWork work) {
            lock (gate) {
                ObjectDisposedException.ThrowIf(disposed != 0, this);
                EnsureTargetAvailable();
                queue.Enqueue(work);
            }
        }

        private Action WrapPostedAction(Action action) {
            return () => {
                try {
                    action();
                }
                catch (Exception ex) {
                    Server.Log.Error(
                        category: "Dispatcher",
                        message: GetString("Unhandled exception while executing a posted server-dispatch callback."),
                        ex: ex);
                }
            };
        }

        private Task InvokeTaskInline(Func<Task> action) {
            try {
                return RunInDispatchContext(action) ?? Task.CompletedTask;
            }
            catch (Exception ex) {
                return Task.FromException(ex);
            }
        }

        private Task<T> InvokeTaskInline<T>(Func<Task<T>> action, CancellationToken cancellationToken) {
            try {
                return RunInDispatchContext(action)
                    ?? Task.FromException<T>(new InvalidOperationException("The dispatched async callback returned null."));
            }
            catch (Exception ex) {
                return Task.FromException<T>(ex);
            }
        }

        private static void Complete(Task task, TaskCompletionSource completion) {
            if (task.IsCompleted) {
                CompleteNow(task, completion);
            }
            else {
                _ = CompleteAsync(task, completion);
            }
        }

        private static void Complete<T>(Task<T> task, TaskCompletionSource<T> completion) {
            if (task.IsCompleted) {
                CompleteNow(task, completion);
            }
            else {
                _ = CompleteAsync(task, completion);
            }
        }

        private static void CompleteNow(Task task, TaskCompletionSource completion) {
            if (task.IsCompletedSuccessfully) {
                completion.TrySetResult();
            }
            else if (task.IsCanceled) {
                completion.TrySetCanceled();
            }
            else {
                completion.TrySetException(task.Exception!.InnerExceptions);
            }
        }

        private static void CompleteNow<T>(Task<T> task, TaskCompletionSource<T> completion) {
            if (task.IsCompletedSuccessfully) {
                completion.TrySetResult(task.Result);
            }
            else if (task.IsCanceled) {
                completion.TrySetCanceled();
            }
            else {
                completion.TrySetException(task.Exception!.InnerExceptions);
            }
        }

        private static async Task CompleteAsync(Task task, TaskCompletionSource completion) {
            try {
                await task.ConfigureAwait(false);
                completion.TrySetResult();
            }
            catch (OperationCanceledException) {
                completion.TrySetCanceled();
            }
            catch (Exception ex) {
                completion.TrySetException(ex);
            }
        }

        private static async Task CompleteAsync<T>(Task<T> task, TaskCompletionSource<T> completion) {
            try {
                completion.TrySetResult(await task.ConfigureAwait(false));
            }
            catch (OperationCanceledException) {
                completion.TrySetCanceled();
            }
            catch (Exception ex) {
                completion.TrySetException(ex);
            }
        }

        private sealed record DispatchWork(Action Invoke, Action<Exception>? Reject);

        private sealed class DispatcherTaskScheduler(QueuedServerDispatcher owner) : TaskScheduler
        {
            protected override IEnumerable<Task> GetScheduledTasks() {
                throw new NotSupportedException();
            }

            protected override void QueueTask(Task task) {
                owner.Enqueue(new(() => TryExecuteTask(task), null));
            }

            protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) {
                return owner.CheckAccess()
                    && !taskWasPreviouslyQueued
                    && owner.RunInDispatchContext(() => TryExecuteTask(task));
            }
        }

        private sealed class DispatcherSynchronizationContext(QueuedServerDispatcher owner) : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state) {
                owner.Post(() => d(state));
            }

            public override void Send(SendOrPostCallback d, object? state) {
                if (owner.CheckAccess()) {
                    owner.RunInDispatchContext(() => d(state));
                    return;
                }

                owner.InvokeAsync(() => d(state)).GetAwaiter().GetResult();
            }
        }

        private sealed class DispatchSwitchSource(QueuedServerDispatcher owner, CancellationToken cancellationToken) : IValueTaskSource
        {
            private Exception? completionException;
            private int completed;

            public ValueTask CreateValueTask() => new(this, token: 0);

            public ValueTaskSourceStatus GetStatus(short token) {
                if (Volatile.Read(ref completed) == 0) {
                    return cancellationToken.IsCancellationRequested
                        ? ValueTaskSourceStatus.Canceled
                        : ValueTaskSourceStatus.Pending;
                }

                return completionException is not null
                    ? ValueTaskSourceStatus.Faulted
                    : cancellationToken.IsCancellationRequested
                    ? ValueTaskSourceStatus.Canceled
                    : ValueTaskSourceStatus.Succeeded;
            }

            public void OnCompleted(
                Action<object?> continuation,
                object? state,
                short token,
                ValueTaskSourceOnCompletedFlags flags) {

                var executionContext = (flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) != 0
                    ? ExecutionContext.Capture()
                    : null;
                void Continue() {
                    Volatile.Write(ref completed, 1);
                    if (executionContext is null) {
                        continuation(state);
                    }
                    else {
                        ExecutionContext.Run(
                            executionContext,
                            static value => {
                                var continuationState = (ContinuationState)value!;
                                continuationState.Continuation(continuationState.State);
                            },
                            new ContinuationState(continuation, state));
                    }
                }

                try {
                    owner.Enqueue(new(Continue, ex => {
                        completionException = ex;
                        Continue();
                    }));
                }
                catch (Exception ex) {
                    completionException = ex;
                    Continue();
                }
            }

            public void GetResult(short token) {
                if (completionException is not null) {
                    ExceptionDispatchInfo.Capture(completionException).Throw();
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            private sealed record ContinuationState(Action<object?> Continuation, object? State);
        }
    }

    internal sealed class UpdateThreadServerDispatcher : QueuedServerDispatcher
    {
        private readonly ServerDispatchDomain domain = new();
        private readonly ReadonlyEventNoCancelDelegate<ServerEvent> preUpdateHandler;

        public UpdateThreadServerDispatcher(ServerContext server) : base(server) {
            preUpdateHandler = OnPreUpdate;
            UnifierApi.EventHub.Game.PreUpdate.Register(preUpdateHandler, HandlerPriority.Highest);
        }

        public override ServerDispatchDomain Domain => domain;

        public override bool CheckAccess() {
            return !IsDisposed
                && TryGetDispatchThread(out var thread)
                && ReferenceEquals(Thread.CurrentThread, thread);
        }

        protected override void EnsureTargetAvailable() {
            if (!TryGetDispatchThread(out _)) {
                throw new InvalidOperationException(GetString("The server dispatcher is unavailable because the server update thread is not running."));
            }
        }

        private void OnPreUpdate(ref ReadonlyNoCancelEventArgs<ServerEvent> args) {
            if (ReferenceEquals(args.Content.Server, Server)) {
                DrainQueue();
            }
        }

        private bool TryGetDispatchThread(out Thread? thread) {
            thread = Server.RunningThread;
            return thread is not null && thread.IsAlive;
        }

        public override void Dispose() {
            if (!TryDisposeQueue()) {
                return;
            }

            UnifierApi.EventHub.Game.PreUpdate.UnRegister(preUpdateHandler);
        }
    }
}
