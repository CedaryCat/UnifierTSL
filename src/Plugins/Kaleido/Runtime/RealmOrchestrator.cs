using Kaleido.Hosting;
using Kaleido.Hosting.ServerContext;
using Kaleido.Hosting.SharedProjection;
using UnifierTSL.Logging;
using UnifierTSL.Servers;

namespace Kaleido.Runtime
{
    public sealed partial class RealmOrchestrator : IAsyncDisposable, IServerJoinResolver, IServerTransferObserver, IServerLeaveObserver
    {
        private readonly RoleLogger logger;
        private readonly List<IRealmHost> hosts = [];
        private readonly List<Systems.RealmSystemLease> systemLeases = [];
        private readonly HashSet<string> mountedSystemIds = new(StringComparer.Ordinal);
        private readonly List<JoinRegistration> joinHandlers = [];
        private readonly List<MaintenanceRegistration> maintenance = [];
        private readonly List<InstanceRetiringRegistration> instanceRetiringHandlers = [];
        private readonly CancellationTokenSource lifetime = new();
        private readonly object registrationGate = new();
        private readonly object transferGate = new();
        private readonly PlayerTransferLock playerTransfers = new();
        private readonly Dictionary<int, TransferTransition> pendingTransfers = [];
        private readonly OperationSet activities = new();
        private readonly OperationSet retirements = new();
        private readonly IDisposable joinSubscription;
        private readonly IDisposable transferSubscription;
        private readonly IDisposable leaveSubscription;
        private readonly RealmScheduler scheduler;
        private long registrationSequence;
        private readonly TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int disposalStarted;

        public RealmOrchestrator(RoleLogger logger) {
            this.logger = logger;
            Registry = new(lifetime.Token);
            RegisterHost(new SharedProjectionHost());
            RegisterHost(new ServerContextHost());
            joinSubscription = ServerRuntime.RegisterJoinResolver(this);
            transferSubscription = ServerRuntime.RegisterTransferObserver(this);
            leaveSubscription = ServerRuntime.RegisterLeaveObserver(this);
            scheduler = new(this, logger);
        }

        public RealmInstanceRegistry Registry { get; }
        internal RealmScheduler Scheduler => scheduler;
        internal CancellationToken LifetimeToken => lifetime.Token;

        public void RegisterHost(IRealmHost host) {
            ArgumentNullException.ThrowIfNull(host);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposalStarted) != 0, this);
            hosts.Add(host);
        }
    }
}
