using Kaleido.Systems;
using UnifierTSL.Servers;

namespace Kaleido.Runtime
{
    public sealed partial class RealmOrchestrator
    {
        public ServerContext? ResolveJoin(ServerJoinRequest request) {
            var join = new RealmJoin(request.Client.Id, request.Player, request.Client, request.CandidateServers);
            foreach (var registration in GetJoinHandlersSnapshot()) {
                RealmJoinDecision? decision;
                try {
                    decision = registration.Handler(join);
                }
                catch (Exception ex) {
                    LogError("Join", $"Realm system '{registration.SystemId}' join handler failed for player #{join.PlayerId}.", ex);
                    continue;
                }

                if (decision is null) {
                    continue;
                }

                try {
                    var target = ResolveJoinDecision(join, decision, CancellationToken.None);
                    if (target is not null) {
                        return target;
                    }
                }
                catch (Exception ex) {
                    LogError("Join", $"Realm system '{registration.SystemId}' join routing failed for player #{join.PlayerId}.", ex);
                }
            }

            return null;
        }

        private ServerContext? ResolveJoinDecision(RealmJoin join, RealmJoinDecision decision, CancellationToken cancellationToken) {
            RealmRuntime? targetRuntime = null;
            RealmAdmission? admission = null;
            var targetServer = decision.Target.Server;
            try {
                if (targetServer is null) {
                    if (decision.Target.Plan is null) {
                        return null;
                    }

                    admission = Registry.AcquireAsync(decision.Target.Plan, CreateRuntimeAsync, cancellationToken).GetAwaiter().GetResult();
                    targetRuntime = admission.Runtime;
                    targetRuntime.Driver.WaitUntilReadyAsync(cancellationToken).GetAwaiter().GetResult();
                    targetServer = targetRuntime.Server;
                }
                else if (Registry.TryGetRuntime(targetServer, out targetRuntime)) {
                    if (!targetRuntime.TryEnterAdmission()) {
                        return null;
                    }

                    admission = new(targetRuntime);
                }

                if (targetRuntime is not null) {
                    targetRuntime.EnterPlayer(join.PlayerId);
                    admission?.Dispose();
                    admission = null;
                    TryAttach(targetRuntime, join.PlayerId, decision.Entry);
                }

                return targetServer;
            }
            finally {
                admission?.Dispose();
            }
        }
    }
}
