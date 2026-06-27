using Kaleido.Model.Lifecycle;
using UnifierTSL.Logging;
using UnifierTSL.Servers;

namespace Kaleido.Runtime
{
    internal sealed class RealmScheduler : IAsyncDisposable, IDispatchScheduler
    {
        private readonly RealmOrchestrator orchestrator;
        private readonly RoleLogger logger;
        private readonly Thread worker;
        private readonly Lock dispatchGate = new();
        private readonly List<DispatchRegistration> dispatchers = [];
        private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool running = true;
        private volatile bool active = true;
        private bool acceptingDispatch = true;
        private int disposed;
        private long frame;

        public ServerDispatchDomain Domain { get; } = new();

        public RealmScheduler(RealmOrchestrator orchestrator, RoleLogger logger) {
            this.orchestrator = orchestrator;
            this.logger = logger;
            worker = new Thread(Loop) {
                IsBackground = true,
                Name = "Kaleido Realm Scheduler"
            };
            worker.Start();
        }

        private void Loop() {
            const int frameMs = 16;
            try {
                while (running) {
                    var started = Environment.TickCount64;
                    try {
                        Tick();
                    }
                    catch (Exception ex) {
                        LogError("Kaleido scheduler tick failed.", ex);
                    }

                    int elapsed = (int)(Environment.TickCount64 - started);
                    if (elapsed < frameMs) {
                        Thread.Sleep(frameMs - elapsed);
                    }
                }
            }
            finally {
                stopped.TrySetResult();
            }
        }

        private void Tick() {
            var tick = new RealmTick(Interlocked.Increment(ref frame), DateTimeOffset.UtcNow);
            DrainDispatchers();
            if (!active) {
                return;
            }

            orchestrator.TickMaintenance(orchestrator.LifetimeToken);
            foreach (var runtime in orchestrator.Registry.Runtimes) {
                if (runtime.IsRetiring) {
                    continue;
                }

                try {
                    runtime.Driver.Tick(tick);
                }
                catch (Exception ex) {
                    LogError($"Realm '{runtime.Plan.Key}' tick failed.", ex);
                }

                try {
                    EvaluateLifecycle(runtime);
                }
                catch (Exception ex) {
                    LogError($"Realm '{runtime.Plan.Key}' lifecycle evaluation failed.", ex);
                }
            }
        }

        IDisposable IDispatchScheduler.Register(string name, Action dispatch, Action stopped) {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(dispatch);
            ArgumentNullException.ThrowIfNull(stopped);
            lock (dispatchGate) {
                if (!acceptingDispatch) {
                    throw new ObjectDisposedException(nameof(RealmScheduler));
                }

                var registration = new DispatchRegistration(this, name, dispatch, stopped);
                dispatchers.Add(registration);
                return registration;
            }
        }

        private void DrainDispatchers() {
            DispatchRegistration[] snapshot;
            lock (dispatchGate) {
                snapshot = [.. dispatchers];
            }

            foreach (var registration in snapshot) {
                try {
                    registration.Invoke();
                }
                catch (Exception ex) {
                    LogError($"Dispatcher '{registration.Name}' failed while draining scheduled work.", ex);
                }
            }
        }

        private void Unregister(DispatchRegistration registration) {
            lock (dispatchGate) {
                dispatchers.Remove(registration);
            }
        }

        private void EvaluateLifecycle(RealmRuntime runtime) {
            var now = DateTimeOffset.UtcNow;
            if (!runtime.TryRequestEmptyRetire(now, out var reason)) {
                return;
            }

            orchestrator.StartRetirement(runtime, reason, Task.CompletedTask);
        }

        private void LogError(string message, Exception ex) {
            try {
                logger.Error(category: "Scheduler", message: message, ex: ex);
            }
            catch {
            }
        }

        internal void Quiesce() => active = false;

        internal async Task StopAsync() {
            running = false;
            await stopped.Task.ConfigureAwait(false);
            StopDispatchers();
        }

        public ValueTask DisposeAsync() => new(StopAsync());

        private void StopDispatchers() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }

            DispatchRegistration[] snapshot;
            lock (dispatchGate) {
                acceptingDispatch = false;
                snapshot = [.. dispatchers];
                dispatchers.Clear();
            }

            foreach (var registration in snapshot) {
                registration.Stop();
            }
        }

        private sealed class DispatchRegistration(
            RealmScheduler owner,
            string name,
            Action dispatch,
            Action stopped) : IDisposable
        {
            private int disposed;

            public string Name { get; } = name;

            public void Invoke() {
                if (Volatile.Read(ref disposed) == 0) {
                    dispatch();
                }
            }

            public void Stop() {
                if (Interlocked.Exchange(ref disposed, 1) == 0) {
                    stopped();
                }
            }

            public void Dispose() {
                if (Interlocked.Exchange(ref disposed, 1) == 0) {
                    owner.Unregister(this);
                }
            }
        }
    }
}
