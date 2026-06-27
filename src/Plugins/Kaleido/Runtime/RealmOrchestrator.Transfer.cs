using Kaleido.Model.Transfer;
using Kaleido.Systems.Installation;
using UnifierTSL.Servers;

namespace Kaleido.Runtime
{
    public sealed partial class RealmOrchestrator
    {
        public Task<RealmTransferResult> TransferAsync(RealmTransferRequest request, CancellationToken cancellationToken = default) {
            var operation = TransferCoreAsync(request, cancellationToken);
            activities.Track(operation);
            return operation;
        }

        private async Task<RealmTransferResult> TransferCoreAsync(RealmTransferRequest request, CancellationToken cancellationToken) {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            var operationToken = linkedCancellation.Token;
            using var playerTransfer = await playerTransfers.EnterAsync(request.PlayerId, operationToken).ConfigureAwait(false);
            RealmRuntime? sourceRuntime = null;
            var sourceServer = ServerRuntime.GetCurrentServer(request.PlayerId);
            if (sourceServer is not null) {
                Registry.TryGetRuntime(sourceServer, out sourceRuntime);
            }

            RealmRuntime? targetRuntime = null;
            RealmAdmission? targetAdmission = null;
            var targetServer = request.Target.Server;
            try {
                if (targetServer is null) {
                    if (request.Target.Plan is null) {
                        return RealmTransferResult.Failure("Transfer request did not specify a server or realm plan.");
                    }

                    targetAdmission = await Registry.AcquireAsync(request.Target.Plan, CreateRuntimeAsync, operationToken).ConfigureAwait(false);
                    targetRuntime = targetAdmission.Runtime;
                    targetServer = targetRuntime.Server;
                }
                else if (Registry.TryGetRuntime(targetServer, out targetRuntime)) {
                    if (!targetRuntime.TryEnterAdmission()) {
                        return RealmTransferResult.Failure("Target realm is retiring.", targetRuntime.Instance);
                    }

                    targetAdmission = new(targetRuntime);
                }

                if (targetRuntime is not null) {
                    await targetRuntime.Driver.WaitUntilReadyAsync(operationToken).ConfigureAwait(false);
                }

                var transition = RegisterPendingTransfer(
                    request.PlayerId,
                    sourceServer,
                    targetServer,
                    sourceRuntime,
                    targetRuntime,
                    targetAdmission,
                    request.Entry,
                    request.Exit);
                var options = ResolveTransferOptions(targetRuntime, request.ServerOptions);
                ServerTransferResult result;
                try {
                    result = await ServerRuntime.TransferAsync(new(request.PlayerId, targetServer, options), operationToken).ConfigureAwait(false);
                }
                catch (Exception ex) {
                    if (ReferenceEquals(ServerRuntime.GetCurrentServer(request.PlayerId), targetServer)) {
                        LogError(
                            "Transfer",
                            $"Server transfer for player #{request.PlayerId} to '{targetServer.Name}' threw after the target was already current. Continuing without rollback.",
                            ex);
                        result = ServerTransferResult.Success(request.PlayerId, sourceServer, targetServer);
                    }
                    else {
                        ClearPendingTransfer(transition);
                        LogError(
                            "Transfer",
                            $"Server transfer for player #{request.PlayerId} to '{targetServer.Name}' failed before the target became current.",
                            ex);
                        return RealmTransferResult.Failure("Server transfer failed with an exception.", targetRuntime?.Instance);
                    }
                }

                if (!result.Succeeded) {
                    ClearPendingTransfer(transition);
                    return RealmTransferResult.Failure(result.Error ?? "Server transfer failed.", targetRuntime?.Instance, result);
                }

                await ApplyPendingTransferAsync(transition).ConfigureAwait(false);
                return RealmTransferResult.Success(targetRuntime?.Instance, result);
            }
            finally {
                targetAdmission?.Dispose();
            }
        }

        private TransferTransition? RegisterPendingTransfer(
            int playerId,
            ServerContext? source,
            ServerContext target,
            RealmRuntime? sourceRuntime,
            RealmRuntime? targetRuntime,
            RealmAdmission? targetAdmission,
            RealmEntry entry,
            RealmExit exit) {

            if (ReferenceEquals(sourceRuntime, targetRuntime) || sourceRuntime is null && targetRuntime is null) {
                return null;
            }

            var transition = new TransferTransition(playerId, source, target, sourceRuntime, targetRuntime, targetAdmission, entry, exit);
            lock (transferGate) {
                pendingTransfers[playerId] = transition;
            }

            return transition;
        }

        private TransferTransition? TakePendingTransfer(ServerTransferNotification transfer) {
            lock (transferGate) {
                if (!pendingTransfers.TryGetValue(transfer.PlayerId, out var transition)
                    || !transition.Matches(transfer.Source, transfer.Target)) {
                    return null;
                }

                transition.Applied = true;
                pendingTransfers.Remove(transfer.PlayerId);
                return transition;
            }
        }

        private Task ApplyPendingTransferAsync(TransferTransition? transition) {
            if (transition is null) {
                return Task.CompletedTask;
            }

            lock (transferGate) {
                if (transition.Applied) {
                    return transition.Application;
                }

                transition.Applied = true;
                if (pendingTransfers.TryGetValue(transition.PlayerId, out var current) && ReferenceEquals(current, transition)) {
                    pendingTransfers.Remove(transition.PlayerId);
                }
            }

            transition.Application = ApplyTransferAsync(
                transition.PlayerId,
                transition.SourceRuntime,
                transition.TargetRuntime,
                transition.TargetAdmission,
                transition.Entry,
                transition.Exit);
            return transition.Application;
        }

        private void ClearPendingTransfer(TransferTransition? transition) {
            if (transition is null) {
                return;
            }

            lock (transferGate) {
                if (pendingTransfers.TryGetValue(transition.PlayerId, out var current) && ReferenceEquals(current, transition)) {
                    pendingTransfers.Remove(transition.PlayerId);
                }
            }
        }

        private Task ApplyTransferAsync(
            int playerId,
            RealmRuntime? sourceRuntime,
            RealmRuntime? targetRuntime,
            RealmAdmission? targetAdmission,
            RealmEntry entry,
            RealmExit exit) {

            if (ReferenceEquals(sourceRuntime, targetRuntime)) {
                return Task.CompletedTask;
            }

            List<Task> applications = [];
            if (sourceRuntime is not null) {
                sourceRuntime.LeavePlayer(playerId);
                applications.Add(DispatchAsync(sourceRuntime, () => TryDetach(sourceRuntime, playerId, exit)));
            }
            if (targetRuntime is not null) {
                targetRuntime.EnterPlayer(playerId);
                targetAdmission?.Dispose();
                applications.Add(DispatchAsync(targetRuntime, () => TryAttach(targetRuntime, playerId, entry)));
            }

            return Task.WhenAll(applications);
        }

        private async Task DispatchAsync(RealmRuntime runtime, Action action) {
            try {
                await runtime.Server.Dispatcher.InvokeAsync(action, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) {
            }
            catch (Exception ex) {
                LogError("Transfer", $"Realm dispatch failed for player transition in realm '{runtime.Plan.Key}'.", ex);
            }
        }

        private void TryAttach(RealmRuntime runtime, int playerId, RealmEntry entry) {
            runtime.InstallScope?.InvokeEntering(
                new(playerId, runtime.Instance, runtime.Server, entry),
                ex => LogError("Transfer", $"Realm entering hook failed for player #{playerId} in realm '{runtime.Plan.Key}'.", ex));
            try {
                runtime.Driver.Attach(new(playerId), entry);
            }
            catch (Exception ex) {
                LogError("Transfer", $"Realm attach failed for player #{playerId} in realm '{runtime.Plan.Key}'. Transfer state will not be rolled back.", ex);
            }
        }

        private void TryDetach(RealmRuntime runtime, int playerId, RealmExit exit) {
            runtime.InstallScope?.InvokeLeaving(
                new(playerId, runtime.Instance, runtime.Server, exit),
                ex => LogError("Transfer", $"Realm leaving hook failed for player #{playerId} in realm '{runtime.Plan.Key}'.", ex));
            try {
                runtime.Driver.Detach(new(playerId), exit);
            }
            catch (Exception ex) {
                LogError("Transfer", $"Realm detach failed for player #{playerId} in realm '{runtime.Plan.Key}'. Transfer state will not be rolled back.", ex);
            }
        }

        private static ServerTransferOptions ResolveTransferOptions(RealmRuntime? targetRuntime, ServerTransferOptions? requestedOptions) {
            if (requestedOptions is not null) {
                return requestedOptions;
            }

            return targetRuntime?.Capabilities is { HasProjection: true, HasServerContext: false }
                ? ServerTransferOptions.TransientTarget
                : ServerTransferOptions.Default;
        }
    }
}
