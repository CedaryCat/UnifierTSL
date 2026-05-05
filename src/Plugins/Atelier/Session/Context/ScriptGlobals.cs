using UnifierTSL.Logging;
using System.Collections.Immutable;

namespace Atelier.Session.Context
{
    public class ScriptGlobals(
        HostConsole console,
        LauncherGlobals launcher,
        ServerGlobals? server,
        IStandardLogger log,
        string hostLabel,
        string targetLabel,
        CancellationToken cancellation)
    {
        private readonly Lock sync = new();
        private ImmutableArray<Task> pendingTasks = [];

        internal HostConsole SessionConsole { get; } = console ?? throw new ArgumentNullException(nameof(console));

        public LauncherGlobals Launcher { get; } = launcher ?? throw new ArgumentNullException(nameof(launcher));

        public ServerGlobals? Server { get; } = server;

        public IStandardLogger Log { get; } = log ?? throw new ArgumentNullException(nameof(log));

        public string HostLabel { get; } = string.IsNullOrWhiteSpace(hostLabel)
            ? throw new ArgumentException(GetString("Host label is required."), nameof(hostLabel))
            : hostLabel.Trim();

        public string TargetLabel { get; } = string.IsNullOrWhiteSpace(targetLabel)
            ? throw new ArgumentException(GetString("Target label is required."), nameof(targetLabel))
            : targetLabel.Trim();

        public CancellationToken Cancellation { get; } = cancellation;

        public ImmutableArray<Task> PendingTasks {
            get {
                lock (sync) {
                    return pendingTasks;
                }
            }
        }

        public Task? LastTask {
            get {
                lock (sync) {
                    return pendingTasks.Length == 0 ? null : pendingTasks[^1];
                }
            }
        }

        internal void UpdatePendingTasks(ImmutableArray<Task> tasks) {
            lock (sync) {
                pendingTasks = tasks;
            }
        }
    }
}
