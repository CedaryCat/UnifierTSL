using Atelier.Session.Context;
using Atelier.Session.Execution;
using Atelier.Session.Roslyn;
using System.Collections.Immutable;
using UnifierTSL;
using UnifierTSL.PluginHost;

namespace Atelier.Session
{
    internal sealed class ReplManager : IDisposable
    {
        private readonly Lock sync = new();
        private readonly HashSet<ReplSession> sessions = [];
        private readonly ScriptOptionsFactory scriptOptionsFactory = new();
        private readonly HostContextFactory hostContextFactory = new();
        private readonly Func<AtelierConfig> configProvider;
        private readonly ManagedPluginAssemblyCatalog managedPluginCatalog;
        private readonly ReplPrewarmer prewarmer;
        private readonly CancellationTokenSource warmupCancellation = new();
        private Task? warmupTask;
        private bool disposed;

        public ReplManager(Func<AtelierConfig>? configProvider = null) {
            this.configProvider = configProvider ?? (() => new AtelierConfig());
            managedPluginCatalog = new ManagedPluginAssemblyCatalog(
                UnifierApi.PluginHosts.RegisteredPluginHosts.OfType<IManagedPluginAssemblyCatalog>());
            prewarmer = new ReplPrewarmer(scriptOptionsFactory, hostContextFactory, managedPluginCatalog, this.configProvider);
            managedPluginCatalog.AssembliesInvalidating += OnManagedAssembliesInvalidating;
        }

        public void StartWarmup() {
            lock (sync) {
                if (disposed || warmupTask is not null) {
                    return;
                }

                warmupTask = Task.Run(() => RunWarmupAsync(warmupCancellation.Token));
            }
        }

        public ReplSession CreateSession(OpenOptions options) {
            StartWarmup();

            lock (sync) {
                ObjectDisposedException.ThrowIf(disposed, this);

                var sessionCancellation = new CancellationTokenSource();
                ScriptHostContext? hostContext = null;
                try {
                    hostContext = hostContextFactory.Create(options, sessionCancellation.Token);
                    var configuration = scriptOptionsFactory.Create(options, hostContext, managedPluginCatalog, configProvider());
                    var session = new ReplSession(configuration, hostContext, managedPluginCatalog, sessionCancellation);
                    sessions.Add(session);
                    return session;
                }
                catch {
                    hostContext?.Dispose();
                    sessionCancellation.Dispose();
                    throw;
                }
            }
        }

        public void ReleaseSession(ReplSession session) {

            var shouldDispose = false;
            lock (sync) {
                if (disposed) {
                    shouldDispose = true;
                }
                else if (sessions.Remove(session)) {
                    shouldDispose = true;
                }
            }

            if (!shouldDispose) {
                return;
            }

            try {
                session.Dispose();
            }
            catch {
            }
        }

        public void Dispose() {
            ReplSession[] sessionsToDispose;
            lock (sync) {
                if (disposed) {
                    return;
                }

                disposed = true;
                managedPluginCatalog.AssembliesInvalidating -= OnManagedAssembliesInvalidating;
                sessionsToDispose = [.. sessions];
                sessions.Clear();
            }

            warmupCancellation.Cancel();
            foreach (var session in sessionsToDispose) {
                try {
                    session.Dispose();
                }
                catch {
                }
            }

            warmupCancellation.Dispose();
            managedPluginCatalog.Dispose();
        }

        private async Task RunWarmupAsync(CancellationToken cancellationToken) {
            try {
                await prewarmer.WarmAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) {
            }
            catch (Exception ex) {
                UnifierApi.Logger.Warning(
                    GetString("Atelier Roslyn prewarm failed."),
                    category: AtelierIds.RoslynLoggerCategory,
                    ex: ex);
            }
        }

        private void OnManagedAssembliesInvalidating(ImmutableArray<string> stableKeys) {
            if (stableKeys.IsDefaultOrEmpty) {
                return;
            }

            ReplSession[] sessionsToInvalidate;
            lock (sync) {
                if (disposed || sessions.Count == 0) {
                    return;
                }

                sessionsToInvalidate = [.. sessions];
            }

            foreach (var session in sessionsToInvalidate) {
                session.InvalidateForManagedAssemblyChange(
                    stableKeys,
                    "A referenced plugin assembly changed. Reopen the Atelier REPL session.");
            }
        }
    }
}
