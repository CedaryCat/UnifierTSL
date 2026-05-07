using Atelier.Session;
using Atelier.Session.Context;
using UnifierTSL.Surface.Adapter.Cli.Sessions;
using UnifierTSL.Commanding;
using UnifierTSL.Surface.Hosting;

namespace Atelier.Presentation.Window
{
    internal sealed class ReplWindowService(ReplManager manager) : IDisposable
    {
        private readonly Lock sync = new();
        private readonly ReplManager manager = manager ?? throw new ArgumentNullException(nameof(manager));
        private readonly HashSet<ReplWindowAdapter> adapters = [];
        private bool disposed;

        public CommandOutcome Open(OpenOptions options, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            var session = manager.CreateSession(options);
            ISurfaceSession? surfaceSession = null;
            ReplWindowAdapter? adapter = null;
            try {
                surfaceSession = CreateHost(options.InvocationHost).CreateSession(SurfaceSessionOptions.UserReleasable);
                adapter = new ReplWindowAdapter(
                    options,
                    surfaceSession,
                    session,
                    ReleaseAdapter,
                    manager.ReleaseSession);

                lock (sync) {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    adapters.Add(adapter);
                }

                adapter.Start();
                return CommandOutcome.Success(GetString($"Opened Atelier REPL on {options.InvocationHost.Label} targeting {options.TargetProfile.Label}."));
            }
            catch {
                if (adapter is not null) {
                    adapter.Dispose();
                }
                else {
                    try {
                        surfaceSession?.Dispose();
                    }
                    catch {
                    }

                    manager.ReleaseSession(session);
                }

                throw;
            }
        }

        public void Dispose() {
            ReplWindowAdapter[] adaptersToDispose;
            lock (sync) {
                if (disposed) {
                    return;
                }

                disposed = true;
                adaptersToDispose = [.. adapters];
                adapters.Clear();
            }

            foreach (var adapter in adaptersToDispose) {
                adapter.Dispose();
            }
        }

        private void ReleaseAdapter(ReplWindowAdapter adapter) {
            lock (sync) {
                if (disposed) {
                    return;
                }

                adapters.Remove(adapter);
            }
        }

        private static ISurfaceSessionHost CreateHost(InvocationHost invocationHost) {
            return invocationHost switch {
                LauncherInvocationHost => new LauncherSurfaceSessionHost(),
                ServerInvocationHost serverHost => new ServerSurfaceSessionHost(serverHost.HostServer),
                _ => throw new InvalidOperationException(GetString($"Unsupported atelier invocation host '{invocationHost.GetType().FullName}'.")),
            };
        }
    }
}
