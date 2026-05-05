using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Sources;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;

namespace UnifierTSL.Servers
{
    public abstract class ServerDispatcher(ServerContext server) : IDisposable
    {
        protected ServerContext Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

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

    internal sealed class UpdateThreadServerDispatcher : ServerDispatcher
    {
        private readonly ConcurrentQueue<Action> queue = new();
        private readonly DispatcherTaskScheduler scheduler;
        private readonly DispatcherSynchronizationContext synchronizationContext;
        private readonly ReadonlyEventNoCancelDelegate<ServerEvent> preUpdateHandler;
        private int disposed;

        public UpdateThreadServerDispatcher(ServerContext server) : base(server) {
            scheduler = new DispatcherTaskScheduler(this);
            synchronizationContext = new DispatcherSynchronizationContext(this);
            preUpdateHandler = OnPreUpdate;
            UnifierApi.EventHub.Game.PreUpdate.Register(preUpdateHandler, HandlerPriority.Highest);
        }

        public override TaskScheduler Scheduler => scheduler;

        public override bool CheckAccess() {
            if (Volatile.Read(ref disposed) != 0) {
                return false;
            }

            return TryGetDispatchThread(out Thread? thread)
                && ReferenceEquals(Thread.CurrentThread, thread);
        }

        public override void Post(Action action) {
            ThrowIfUnavailable();
            queue.Enqueue(WrapPostedAction(action));
        }

        public override Task InvokeAsync(Action action, CancellationToken cancellationToken = default) {

            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled(cancellationToken);
            }

            ThrowIfUnavailable();
            if (CheckAccess()) {
                try {
                    ExecuteWithDispatchContext(action);
                    return Task.CompletedTask;
                }
                catch (Exception ex) {
                    return Task.FromException(ex);
                }
            }

            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(() => {
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
            });
            return completion.Task;
        }

        public override Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default) {

            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled<T>(cancellationToken);
            }

            ThrowIfUnavailable();
            if (CheckAccess()) {
                try {
                    return Task.FromResult(ExecuteWithDispatchContext(action));
                }
                catch (Exception ex) {
                    return Task.FromException<T>(ex);
                }
            }

            TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(() => {
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
            });
            return completion.Task;
        }

        public override Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default) {

            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled(cancellationToken);
            }

            ThrowIfUnavailable();
            if (CheckAccess()) {
                return InvokeTaskInline(action, cancellationToken);
            }

            return Task.Factory.StartNew(
                    () => InvokeTaskInline(action, cancellationToken),
                    cancellationToken,
                    TaskCreationOptions.DenyChildAttach,
                    scheduler)
                .Unwrap();
        }

        public override Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default) {

            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled<T>(cancellationToken);
            }

            ThrowIfUnavailable();
            if (CheckAccess()) {
                return InvokeTaskInline(action, cancellationToken);
            }

            return Task.Factory.StartNew(
                    () => InvokeTaskInline(action, cancellationToken),
                    cancellationToken,
                    TaskCreationOptions.DenyChildAttach,
                    scheduler)
                .Unwrap();
        }

        public override ValueTask SwitchAsync(CancellationToken cancellationToken = default) {
            if (cancellationToken.IsCancellationRequested) {
                return ValueTask.FromCanceled(cancellationToken);
            }

            ThrowIfUnavailable();
            if (CheckAccess()) {
                return ValueTask.CompletedTask;
            }

            ServerDispatchSwitchSource source = new(this, cancellationToken);
            return source.CreateValueTask();
        }

        public override void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }

            UnifierApi.EventHub.Game.PreUpdate.UnRegister(preUpdateHandler);

            while (queue.TryDequeue(out _)) {
            }
        }

        internal void QueueTask(Task task) {
            ThrowIfUnavailable();
            queue.Enqueue(() => scheduler.Execute(task));
        }

        private void OnPreUpdate(ref ReadonlyNoCancelEventArgs<ServerEvent> args) {
            if (!ReferenceEquals(args.Content.Server, Server)) {
                return;
            }

            DrainQueue();
        }

        private void DrainQueue() {
            while (queue.TryDequeue(out Action? action)) {
                try {
                    ExecuteWithDispatchContext(action);
                }
                catch (Exception ex) {
                    Server.Log.Error(
                        category: "Dispatcher",
                        message: GetString("Unhandled exception while executing a queued server-dispatch callback."),
                        ex: ex);
                }
            }
        }

        private Task InvokeTaskInline(Func<Task> action, CancellationToken cancellationToken) {
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled(cancellationToken);
            }

            try {
                return ExecuteWithDispatchContext(action) ?? Task.CompletedTask;
            }
            catch (Exception ex) {
                return Task.FromException(ex);
            }
        }

        private Task<T> InvokeTaskInline<T>(Func<Task<T>> action, CancellationToken cancellationToken) {
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled<T>(cancellationToken);
            }

            try {
                return ExecuteWithDispatchContext(action) ?? Task.FromCanceled<T>(cancellationToken);
            }
            catch (Exception ex) {
                return Task.FromException<T>(ex);
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

        private T ExecuteWithDispatchContext<T>(Func<T> callback) {
            SynchronizationContext? previous = SynchronizationContext.Current;
            try {
                SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                return callback();
            }
            finally {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        private void ExecuteWithDispatchContext(Action callback) {
            SynchronizationContext? previous = SynchronizationContext.Current;
            try {
                SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                callback();
            }
            finally {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        private bool TryGetDispatchThread(out Thread? thread) {
            thread = Server.RunningThread;
            return thread is not null && thread.IsAlive;
        }

        private void ThrowIfUnavailable() {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (!TryGetDispatchThread(out _)) {
                throw new InvalidOperationException(GetString("The server dispatcher is unavailable because the server update thread is not running."));
            }
        }

        private sealed class DispatcherTaskScheduler(UpdateThreadServerDispatcher owner) : TaskScheduler
        {
            private readonly UpdateThreadServerDispatcher owner = owner;

            protected override IEnumerable<Task> GetScheduledTasks() {
                throw new NotSupportedException();
            }

            protected override void QueueTask(Task task) {
                owner.QueueTask(task);
            }

            protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) {
                if (!owner.CheckAccess() || taskWasPreviouslyQueued) {
                    return false;
                }

                return owner.ExecuteWithDispatchContext(() => TryExecuteTask(task));
            }

            public void Execute(Task task) {
                owner.ExecuteWithDispatchContext(() => TryExecuteTask(task));
            }
        }

        private sealed class DispatcherSynchronizationContext(UpdateThreadServerDispatcher owner) : SynchronizationContext
        {
            private readonly UpdateThreadServerDispatcher owner = owner;

            public override void Post(SendOrPostCallback d, object? state) {

                owner.ThrowIfUnavailable();
                owner.queue.Enqueue(() => d(state));
            }

            public override void Send(SendOrPostCallback d, object? state) {

                if (owner.CheckAccess()) {
                    owner.ExecuteWithDispatchContext(() => d(state));
                    return;
                }

                owner.InvokeAsync(() => d(state)).GetAwaiter().GetResult();
            }
        }

        private sealed class ServerDispatchSwitchSource(UpdateThreadServerDispatcher owner, CancellationToken cancellationToken) : IValueTaskSource
        {
            private Exception? completionException;

            public ValueTask CreateValueTask() => new(this, token: 0);

            public ValueTaskSourceStatus GetStatus(short token) {
                return completionException is not null
                    ? ValueTaskSourceStatus.Faulted
                    : cancellationToken.IsCancellationRequested
                    ? ValueTaskSourceStatus.Canceled
                    : ValueTaskSourceStatus.Pending;
            }

            public void OnCompleted(
                Action<object?> continuation,
                object? state,
                short token,
                ValueTaskSourceOnCompletedFlags flags) {

                if (cancellationToken.IsCancellationRequested) {
                    completionException = new OperationCanceledException(cancellationToken);
                    continuation(state);
                    return;
                }

                ExecutionContext? executionContext =
                    (flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) != 0
                    ? ExecutionContext.Capture()
                    : null;

                try {
                    owner.Post(() => {
                        if (executionContext is null) {
                            continuation(state);
                            return;
                        }

                        ExecutionContext.Run(
                            executionContext,
                            static context => {
                                ((State)context!).Continuation(((State)context).ContinuationState);
                            },
                            new State(continuation, state));
                    });
                }
                catch (Exception ex) {
                    completionException = ex;
                    continuation(state);
                }
            }

            public void GetResult(short token) {
                if (completionException is null) {
                    return;
                }

                ExceptionDispatchInfo.Capture(completionException).Throw();
            }

            private sealed record State(Action<object?> Continuation, object? ContinuationState);
        }
    }
}
