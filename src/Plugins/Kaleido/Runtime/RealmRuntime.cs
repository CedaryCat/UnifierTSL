using System.Collections.Immutable;
using Kaleido.Hosting;
using Kaleido.Model.Hosting;
using Kaleido.Model.Ids;
using Kaleido.Model.Instances;
using Kaleido.Model.Lifecycle;
using Kaleido.Model.Planning;
using Kaleido.Model.Transfer;
using Kaleido.Systems.Installation;
using UnifierTSL.Servers;

namespace Kaleido.Runtime
{
    internal sealed class RealmRuntime
    {
        private readonly Lock gate = new();
        private readonly HashSet<int> players = [];
        private readonly List<RealmLease> leases = [];
        private readonly TaskCompletionSource removed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private RealmInstallScope? installScope;
        private TaskCompletionSource? retirementReady;
        private DateTimeOffset? emptySinceUtc;
        private int admissions;
        private bool retirementRequested;
        private bool retiring;

        public RealmRuntime(
            RealmOrchestrator orchestrator,
            RealmInstanceId instanceId,
            RealmPlan plan,
            RealmHostSession session,
            RealmHostCapabilities capabilities) {

            Orchestrator = orchestrator;
            InstanceId = instanceId;
            Plan = plan;
            Server = session.Server;
            Driver = session.Driver;
            Capabilities = capabilities;
            CreatedAtUtc = DateTimeOffset.UtcNow;
            emptySinceUtc = CreatedAtUtc;
            Instance = new(this);
        }

        public RealmOrchestrator Orchestrator { get; }
        public RealmInstanceId InstanceId { get; }
        public RealmPlan Plan { get; }
        public ServerContext Server { get; }
        public IRealmDriver Driver { get; }
        public RealmHostCapabilities Capabilities { get; }
        public RealmInstance Instance { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public RealmRuntimeState State => Driver.State;
        public Task Removed => removed.Task;
        public bool IsRetiring {
            get {
                lock (gate) {
                    return retirementRequested;
                }
            }
        }

        public int PlayerCount {
            get {
                lock (gate) {
                    return players.Count;
                }
            }
        }

        public ImmutableArray<int> Players {
            get {
                lock (gate) {
                    return [.. players];
                }
            }
        }

        public DateTimeOffset? EmptySinceUtc {
            get {
                lock (gate) {
                    return emptySinceUtc;
                }
            }
        }

        public RealmInstallScope? InstallScope {
            get {
                lock (gate) {
                    return installScope;
                }
            }
        }

        public void SetInstallScope(RealmInstallScope scope) {
            lock (gate) {
                installScope = scope;
            }
        }

        public bool TryRequestRetire(out Task ready) {
            lock (gate) {
                if (retirementRequested) {
                    ready = Task.CompletedTask;
                    return false;
                }

                retirementRequested = true;
                if (admissions == 0) {
                    retiring = true;
                    ready = Task.CompletedTask;
                }
                else {
                    retirementReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    ready = retirementReady.Task;
                }

                return true;
            }
        }

        public bool TryRequestEmptyRetire(DateTimeOffset now, out RealmRetireReason reason) {
            lock (gate) {
                PruneExpiredHolds(now);
                var policy = Plan.Lifecycle;
                if (retirementRequested
                    || players.Count != 0
                    || admissions != 0
                    || leases.Count != 0
                    || emptySinceUtc is not { } emptySince
                    || policy.Retention == RealmRetentionKind.Resident
                    || now - emptySince < (policy.EmptyDelay ?? TimeSpan.Zero)) {
                    reason = null!;
                    return false;
                }

                retirementRequested = true;
                retiring = true;
                reason = policy.Retention == RealmRetentionKind.ElasticSuspend
                    ? new(RealmRetireKind.Empty, "Realm suspended after becoming empty.")
                    : RealmRetireReason.Empty;
                return true;
            }
        }

        public bool TryEnterAdmission() {
            lock (gate) {
                if (retirementRequested) {
                    return false;
                }

                admissions++;
                return true;
            }
        }

        public void AddAdmissions(int count) {
            if (count == 0) {
                return;
            }
            if (count < 0) {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            lock (gate) {
                if (retirementRequested) {
                    throw new InvalidOperationException($"Cannot admit work to retiring realm '{Plan.Key}'.");
                }

                admissions += count;
            }
        }

        public void ExitAdmission() {
            TaskCompletionSource? ready = null;
            lock (gate) {
                if (admissions <= 0) {
                    throw new InvalidOperationException($"Realm '{Plan.Key}' has no active admission to release.");
                }

                admissions--;
                if (admissions == 0 && retirementRequested && !retiring) {
                    retiring = true;
                    ready = retirementReady;
                    retirementReady = null;
                }
            }

            ready?.SetResult();
        }

        public RealmHold Hold(TimeSpan? duration = null) {
            if (duration is { } value && value < TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(duration), duration, "Realm hold duration cannot be negative.");
            }

            var lease = new RealmLease(duration.HasValue ? DateTimeOffset.UtcNow + duration.Value : null);
            lock (gate) {
                if (retirementRequested) {
                    throw new InvalidOperationException($"Cannot hold retiring realm '{Plan.Key}'.");
                }

                leases.Add(lease);
            }

            return new(this, lease);
        }

        public void ReleaseHold(RealmLease lease) {
            lock (gate) {
                leases.Remove(lease);
            }
        }

        public bool HasActiveHold(DateTimeOffset now) {
            lock (gate) {
                PruneExpiredHolds(now);
                return leases.Count != 0;
            }
        }

        private void PruneExpiredHolds(DateTimeOffset now) {
            for (int i = leases.Count - 1; i >= 0; i--) {
                if (leases[i].ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= now) {
                    leases.RemoveAt(i);
                }
            }
        }

        public void MarkRemoved() => removed.TrySetResult();

        public bool EnterPlayer(int playerId) {
            lock (gate) {
                var added = players.Add(playerId);
                if (added) {
                    emptySinceUtc = null;
                }

                return added;
            }
        }

        public bool LeavePlayer(int playerId) {
            lock (gate) {
                var removed = players.Remove(playerId);
                if (removed && players.Count == 0) {
                    emptySinceUtc = DateTimeOffset.UtcNow;
                }

                return removed;
            }
        }
    }
}
