using System.Collections.Concurrent;
using System.Collections.Immutable;
using UnifierTSL.PluginHost.Configs;
using UnifierTSL.Plugins;
using UnifierTSL.PluginService;

namespace UnifierTSL.PluginHost.Hosts.Dotnet
{
    public partial class DotnetPluginHost
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> HotReloadLocks = new(StringComparer.OrdinalIgnoreCase);

        public async Task<PluginHotReloadResult> TryHotReloadAsync(PluginHotReloadRequest request, CancellationToken cancellationToken = default) {
            if (request is null) {
                return PluginHotReloadResult.Rejected(
                    reasonCode: HotReloadReasonCode.InvalidRequest,
                    message: GetString("Hot reload request is null."),
                    matchKey: "",
                    pluginFilePath: "",
                    entryPoint: "");
            }

            string resolvedMatchKey;
            try {
                resolvedMatchKey = request.ResolveMatchKey();
            }
            catch (Exception ex) {
                return PluginHotReloadResult.Rejected(
                    reasonCode: HotReloadReasonCode.InvalidRequest,
                    message: GetParticularString("{0} is error message", $"Invalid hot reload request: {ex.Message}"),
                    matchKey: request.MatchKey ?? "",
                    pluginFilePath: request.PluginFilePath,
                    entryPoint: request.EntryPoint);
            }

            SemaphoreSlim gate = HotReloadLocks.GetOrAdd(resolvedMatchKey, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try {
                return await TryHotReloadInternalAsync(request, resolvedMatchKey, cancellationToken);
            }
            finally {
                gate.Release();
            }
        }

        private async Task<PluginHotReloadResult> TryHotReloadInternalAsync(
            PluginHotReloadRequest request,
            string matchKey,
            CancellationToken cancellationToken) {
            PluginContainer? oldContainer = Plugins
                .FirstOrDefault(p => p.LoadStatus is PluginLoadStatus.Loaded
                    && string.Equals(GetMatchKey(p), matchKey, StringComparison.OrdinalIgnoreCase));

            if (oldContainer is null) {
                return Reject(
                    code: HotReloadReasonCode.TargetNotFound,
                    message: GetParticularString("{0} is match key", $"No loaded plugin matches hot reload key '{matchKey}'."),
                    request: request,
                    matchKey: matchKey);
            }

            if (oldContainer.Plugin is not IHotReloadPlugin oldHotReload) {
                return Reject(
                    code: HotReloadReasonCode.NotOptedIn,
                    message: GetParticularString("{0} is plugin name", $"Plugin '{oldContainer.Name}' does not implement IHotReloadCapablePlugin."),
                    request: request,
                    matchKey: matchKey,
                    oldVersion: oldContainer.Version);
            }

            if (!CanHotReloadTopology(oldContainer, out string? blockedReason)) {
                return Reject(
                    code: HotReloadReasonCode.DependencyBlocked,
                    message: blockedReason ?? GetString("Hot reload was blocked by plugin dependency topology."),
                    request: request,
                    matchKey: matchKey,
                    oldVersion: oldContainer.Version);
            }

            DotnetPluginInfo[] matchedCandidates = [.. PluginDiscoverer
                .DiscoverPlugins("plugins", PluginDiscoveryMode.UpdatedOnly)
                .OfType<DotnetPluginInfo>()
                .Where(x => string.Equals(
                    PluginHotReloadMatchKey.Create(x.Location.FilePath, x.EntryPoint.EntryPointString),
                    matchKey,
                    StringComparison.OrdinalIgnoreCase))];

            if (matchedCandidates.Length == 0) {
                return Reject(
                    code: HotReloadReasonCode.NoUpdatedCandidate,
                    message: GetParticularString("{0} is plugin name", $"No updated candidate was found for plugin '{oldContainer.Name}'."),
                    request: request,
                    matchKey: matchKey,
                    oldVersion: oldContainer.Version);
            }

            if (matchedCandidates.Length > 1) {
                return Reject(
                    code: HotReloadReasonCode.AmbiguousCandidate,
                    message: GetParticularString("{0} is match key", $"Multiple updated plugin candidates were found for key '{matchKey}'."),
                    request: request,
                    matchKey: matchKey,
                    oldVersion: oldContainer.Version);
            }

            DotnetPluginInfo candidateInfo = matchedCandidates[0];
            PluginContainer? candidateContainer = dotnetPluginLoader.LoadPluginCandidate(candidateInfo, out LoadDetails loadDetails);
            if (candidateContainer is null) {
                return Reject(
                    code: HotReloadReasonCode.LoadFailed,
                    message: GetParticularString("{0} is plugin name, {1} is load details", $"Failed to load hot reload candidate for plugin '{oldContainer.Name}'. Result: {loadDetails}."),
                    request: request,
                    matchKey: matchKey,
                    oldVersion: oldContainer.Version,
                    newVersion: candidateInfo.Metadata.Version);
            }

            if (candidateContainer.Plugin is not IHotReloadPlugin candidateHotReload) {
                CleanupCandidate(candidateContainer, matchKey, request);
                return Reject(
                    code: HotReloadReasonCode.NotOptedIn,
                    message: GetParticularString("{0} is plugin name", $"Candidate plugin '{candidateContainer.Name}' does not implement IHotReloadCapablePlugin."),
                    request: request,
                    matchKey: matchKey,
                    oldVersion: oldContainer.Version,
                    newVersion: candidateContainer.Version);
            }

            HotReloadRuntimeSnapshot runtimeSnapshot = CaptureRuntimeSnapshot();
            HotReloadExportResult exportResult;
            try {
                HotReloadExportContext exportContext = new(
                    SchemaVersion: request.SchemaVersion,
                    MatchKey: matchKey,
                    CurrentVersion: oldContainer.Version,
                    CandidateVersion: candidateContainer.Version,
                    RuntimeSnapshot: runtimeSnapshot);

                exportResult = await oldHotReload.ExportHotReloadStateAsync(exportContext, cancellationToken);
            }
            catch (Exception ex) {
                CleanupCandidate(candidateContainer, matchKey, request);
                Logger.LogHandledExceptionWithMetadata(
                    category: "HotReload",
                    message: GetParticularString("{0} is plugin name", $"Plugin '{oldContainer.Name}' threw while exporting hot reload state."),
                    metadata: BuildHotReloadMetadata(oldContainer.Location.FilePath, oldContainer.Plugin.GetType().FullName ?? oldContainer.Plugin.GetType().Name, matchKey),
                    ex: ex);
                return Reject(
                    code: HotReloadReasonCode.ExportFailed,
                    message: GetParticularString("{0} is plugin name, {1} is error message", $"Plugin '{oldContainer.Name}' failed while exporting hot reload state: {ex.Message}"),
                    request: request,
                    matchKey: matchKey,
                    oldVersion: oldContainer.Version,
                    newVersion: candidateContainer.Version);
            }

            if (!exportResult.Accepted) {
                CleanupCandidate(candidateContainer, matchKey, request);
                HotReloadReasonCode reason = exportResult.ReasonCode is HotReloadReasonCode.None
                    ? HotReloadReasonCode.RejectedByOld
                    : exportResult.ReasonCode;
                return Reject(
                    code: reason,
                    message: exportResult.Message ?? GetParticularString("{0} is plugin name", $"Plugin '{oldContainer.Name}' rejected hot reload export."),
                    request: request,
                    matchKey: matchKey,
                    oldVersion: oldContainer.Version,
                    newVersion: candidateContainer.Version);
            }

            if (exportResult.State is null) {
                CleanupCandidate(candidateContainer, matchKey, request);
                return Reject(
                    code: HotReloadReasonCode.MissingRequiredState,
                    message: GetParticularString("{0} is plugin name", $"Plugin '{oldContainer.Name}' accepted export but returned no state payload."),
                    request: request,
                    matchKey: matchKey,
                    oldVersion: oldContainer.Version,
                    newVersion: candidateContainer.Version);
            }

            HotReloadEnvelope envelope = new(
                SchemaVersion: request.SchemaVersion,
                MatchKey: matchKey,
                OldVersion: oldContainer.Version,
                NewVersion: candidateContainer.Version,
                State: exportResult.State,
                RuntimeSnapshot: runtimeSnapshot);

            ConfigRegistrar candidateConfig = new(candidateContainer, Path.Combine(LauncherConfigStore.RootConfigRelativeDir, Path.GetFileNameWithoutExtension(candidateContainer.Location.FilePath)));

            try {
                HotReloadPrepareResult prepareResult = await candidateHotReload.PrepareHotReloadAsync(
                    new HotReloadPrepareContext(envelope, candidateConfig),
                    cancellationToken);

                if (!prepareResult.Accepted) {
                    CleanupCandidate(candidateContainer, matchKey, request);
                    HotReloadReasonCode reason = prepareResult.ReasonCode is HotReloadReasonCode.None
                        ? HotReloadReasonCode.RejectedByNew
                        : prepareResult.ReasonCode;
                    return Reject(
                        code: reason,
                        message: prepareResult.Message ?? GetParticularString("{0} is plugin name", $"Candidate plugin '{candidateContainer.Name}' rejected hot reload prepare."),
                        request: request,
                        matchKey: matchKey,
                        oldVersion: oldContainer.Version,
                        newVersion: candidateContainer.Version);
                }
            }
            catch (Exception ex) {
                CleanupCandidate(candidateContainer, matchKey, request);
                Logger.LogHandledExceptionWithMetadata(
                    category: "HotReload",
                    message: GetParticularString("{0} is plugin name", $"Candidate plugin '{candidateContainer.Name}' failed during hot reload prepare."),
                    metadata: BuildHotReloadMetadata(oldContainer.Location.FilePath, oldContainer.Plugin.GetType().FullName ?? oldContainer.Plugin.GetType().Name, matchKey),
                    ex: ex);
                return Reject(
                    code: HotReloadReasonCode.PrepareFailed,
                    message: GetParticularString("{0} is plugin name, {1} is error message", $"Candidate plugin '{candidateContainer.Name}' failed during hot reload prepare: {ex.Message}"),
                    request: request,
                    matchKey: matchKey,
                    oldVersion: oldContainer.Version,
                    newVersion: candidateContainer.Version);
            }

            return await CommitHotReloadAsync(
                request: request,
                matchKey: matchKey,
                oldContainer: oldContainer,
                oldHotReload: oldHotReload,
                candidateContainer: candidateContainer,
                candidateHotReload: candidateHotReload,
                envelope: envelope,
                candidateConfig: candidateConfig,
                cancellationToken: cancellationToken);
        }

        private async Task<PluginHotReloadResult> CommitHotReloadAsync(
            PluginHotReloadRequest request,
            string matchKey,
            PluginContainer oldContainer,
            IHotReloadPlugin oldHotReload,
            PluginContainer candidateContainer,
            IHotReloadPlugin candidateHotReload,
            HotReloadEnvelope envelope,
            ConfigRegistrar candidateConfig,
            CancellationToken cancellationToken) {
            HotReloadCommitStage stage = HotReloadCommitStage.ShutdownOld;
            HotReloadReasonCode commitFailureCode = HotReloadReasonCode.None;
            string commitFailureMessage = string.Empty;

            try {
                Logger.WarningWithMetadata(
                    category: "HotReload",
                    message: GetParticularString("{0} is plugin name", $"Starting hot reload commit for plugin '{oldContainer.Name}'. No global isolation gate is enabled; in-flight events may still hit the transition window."),
                    metadata: BuildHotReloadMetadata(oldContainer.Location.FilePath, oldContainer.Plugin.GetType().FullName ?? oldContainer.Plugin.GetType().Name, matchKey));

                await oldContainer.Plugin.ShutdownAsync(cancellationToken);

                stage = HotReloadCommitStage.ActivateNew;
                HotReloadActivateResult activateResult = await candidateHotReload.ActivateHotReloadAsync(
                    new HotReloadActivateContext(envelope, candidateConfig),
                    cancellationToken);

                if (!activateResult.Accepted) {
                    commitFailureCode = activateResult.ReasonCode is HotReloadReasonCode.None
                        ? HotReloadReasonCode.ActivateFailed
                        : activateResult.ReasonCode;
                    commitFailureMessage = activateResult.Message ?? GetParticularString("{0} is plugin name", $"Candidate plugin '{candidateContainer.Name}' rejected activation.");
                    return await RollbackAsync(
                        request: request,
                        matchKey: matchKey,
                        oldContainer: oldContainer,
                        oldHotReload: oldHotReload,
                        candidateContainer: candidateContainer,
                        envelope: envelope,
                        commitFailureCode: commitFailureCode,
                        commitFailureMessage: commitFailureMessage,
                        exception: null,
                        cancellationToken: cancellationToken);
                }

                stage = HotReloadCommitStage.UnloadOld;
                PluginLoader.ForceUnloadPlugin(oldContainer);

                stage = HotReloadCommitStage.PromoteNew;
                candidateContainer.LoadStatus = PluginLoadStatus.Loaded;
                ImmutableInterlocked.Update(ref Plugins, plugins => plugins.Add(candidateContainer));

                Logger.SuccessWithMetadata(
                    category: "HotReload",
                    message: GetParticularString("{0} is plugin name, {1} is old version, {2} is new version", $"Hot reload succeeded for plugin '{oldContainer.Name}'. {oldContainer.Version} -> {candidateContainer.Version}."),
                    metadata: BuildHotReloadMetadata(oldContainer.Location.FilePath, oldContainer.Plugin.GetType().FullName ?? oldContainer.Plugin.GetType().Name, matchKey));

                return PluginHotReloadResult.Accepted(
                    matchKey: matchKey,
                    pluginFilePath: request.PluginFilePath,
                    entryPoint: request.EntryPoint,
                    oldVersion: oldContainer.Version,
                    newVersion: candidateContainer.Version,
                    message: GetParticularString("{0} is plugin name", $"Plugin '{oldContainer.Name}' hot reloaded successfully."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception ex) {
                commitFailureCode = stage == HotReloadCommitStage.ShutdownOld
                    ? HotReloadReasonCode.ShutdownFailed
                    : HotReloadReasonCode.ActivateFailed;
                commitFailureMessage = GetParticularString("{0} is plugin name, {1} is error message", $"Commit stage '{stage}' failed for plugin '{oldContainer.Name}': {ex.Message}");

                return await RollbackAsync(
                    request: request,
                    matchKey: matchKey,
                    oldContainer: oldContainer,
                    oldHotReload: oldHotReload,
                    candidateContainer: candidateContainer,
                    envelope: envelope,
                    commitFailureCode: commitFailureCode,
                    commitFailureMessage: commitFailureMessage,
                    exception: ex,
                    cancellationToken: cancellationToken);
            }
        }

        private async Task<PluginHotReloadResult> RollbackAsync(
            PluginHotReloadRequest request,
            string matchKey,
            PluginContainer oldContainer,
            IHotReloadPlugin oldHotReload,
            PluginContainer candidateContainer,
            HotReloadEnvelope envelope,
            HotReloadReasonCode commitFailureCode,
            string commitFailureMessage,
            Exception? exception,
            CancellationToken cancellationToken) {
            try {
                HotReloadAbortResult rollback = await oldHotReload.ResumeAfterHotReloadAbortAsync(
                    new HotReloadAbortContext(envelope, commitFailureCode, commitFailureMessage),
                    cancellationToken);

                if (!rollback.Recovered) {
                    string rollbackMessage = rollback.Message ?? GetParticularString("{0} is plugin name", $"Plugin '{oldContainer.Name}' failed to recover after hot reload commit failure.");
                    CleanupCandidate(candidateContainer, matchKey, request);
                    Logger.CriticalWithMetadata(
                        category: "HotReload",
                        message: rollbackMessage,
                        metadata: BuildHotReloadMetadata(oldContainer.Location.FilePath, oldContainer.Plugin.GetType().FullName ?? oldContainer.Plugin.GetType().Name, matchKey),
                        ex: exception);
                    return Reject(
                        code: HotReloadReasonCode.RollbackFailed,
                        message: rollbackMessage,
                        request: request,
                        matchKey: matchKey,
                        oldVersion: oldContainer.Version,
                        newVersion: candidateContainer.Version);
                }

                CleanupCandidate(candidateContainer, matchKey, request);
                Logger.WarningWithMetadata(
                    category: "HotReload",
                    message: commitFailureMessage,
                    metadata: BuildHotReloadMetadata(oldContainer.Location.FilePath, oldContainer.Plugin.GetType().FullName ?? oldContainer.Plugin.GetType().Name, matchKey),
                    ex: exception);
                return Reject(
                    code: commitFailureCode,
                    message: commitFailureMessage,
                    request: request,
                    matchKey: matchKey,
                    oldVersion: oldContainer.Version,
                    newVersion: candidateContainer.Version);
            }
            catch (Exception ex) {
                CleanupCandidate(candidateContainer, matchKey, request);
                Logger.CriticalWithMetadata(
                    category: "HotReload",
                    message: GetParticularString("{0} is plugin name", $"Rollback failed with an exception for plugin '{oldContainer.Name}'."),
                    metadata: BuildHotReloadMetadata(oldContainer.Location.FilePath, oldContainer.Plugin.GetType().FullName ?? oldContainer.Plugin.GetType().Name, matchKey),
                    ex: ex);
                return Reject(
                    code: HotReloadReasonCode.RollbackFailed,
                    message: GetParticularString("{0} is plugin name, {1} is error message", $"Rollback failed for plugin '{oldContainer.Name}': {ex.Message}"),
                    request: request,
                    matchKey: matchKey,
                    oldVersion: oldContainer.Version,
                    newVersion: candidateContainer.Version);
            }
        }

        private void CleanupCandidate(
            PluginContainer candidateContainer,
            string matchKey,
            PluginHotReloadRequest request) {
            try {
                dotnetPluginLoader.UnloadCandidate(candidateContainer);
            }
            catch (Exception ex) {
                Logger.WarningWithMetadata(
                    category: "HotReload",
                    message: GetParticularString("{0} is plugin name", $"Failed to unload hot reload candidate '{candidateContainer.Name}'."),
                    metadata: BuildHotReloadMetadata(request.PluginFilePath, request.EntryPoint, matchKey),
                    ex: ex);
            }
        }

        private static string GetMatchKey(PluginContainer container) {
            string entryPoint = container.Plugin.GetType().FullName ?? container.Plugin.GetType().Name;
            return PluginHotReloadMatchKey.Create(container.Location.FilePath, entryPoint);
        }

        private bool CanHotReloadTopology(PluginContainer oldContainer, out string? blockedReason) {
            blockedReason = null;

            if (oldContainer.Module.CoreModule is not null) {
                blockedReason = GetParticularString("{0} is plugin name", $"Plugin '{oldContainer.Name}' is a satellite module and shares a core module context. V1 hot reload refuses this topology.");
                return false;
            }

            if (oldContainer.Module.DependentModules.Length > 0) {
                blockedReason = GetParticularString("{0} is plugin name", $"Plugin '{oldContainer.Name}' has dependent modules. V1 hot reload refuses this topology.");
                return false;
            }

            bool hasSiblingInSameModule = Plugins.Any(x =>
                !ReferenceEquals(x, oldContainer)
                && ReferenceEquals(x.Module, oldContainer.Module)
                && x.LoadStatus is not PluginLoadStatus.Unloaded);
            if (hasSiblingInSameModule) {
                blockedReason = GetParticularString("{0} is plugin name", $"Plugin '{oldContainer.Name}' shares its module with other active plugin entry points. V1 hot reload refuses this topology.");
                return false;
            }

            return true;
        }

        private static HotReloadRuntimeSnapshot CaptureRuntimeSnapshot() {
            List<HotReloadPlayerSnapshot> players = [];
            for (int i = 0; i < UnifiedServerCoordinator.globalClients.Length; i++) {
                var client = UnifiedServerCoordinator.globalClients[i];
                string? serverName = UnifiedServerCoordinator.GetClientCurrentlyServer(i)?.Name;
                string? playerName = string.IsNullOrWhiteSpace(client.Name) ? null : client.Name;
                players.Add(new HotReloadPlayerSnapshot(i, client.IsActive, serverName, playerName));
            }

            return new HotReloadRuntimeSnapshot(
                CapturedAtUtc: DateTime.UtcNow,
                ActiveConnections: UnifiedServerCoordinator.ActiveConnections,
                Players: [.. players]);
        }

        private static Logging.Metadata.KeyValueMetadata[] BuildHotReloadMetadata(string pluginFilePath, string entryPoint, string matchKey)
            => [new("PluginFile", pluginFilePath), new("EntryPoint", entryPoint), new("MatchKey", matchKey)];

        private static PluginHotReloadResult Reject(
            HotReloadReasonCode code,
            string message,
            PluginHotReloadRequest request,
            string matchKey,
            Version? oldVersion = null,
            Version? newVersion = null) {
            return PluginHotReloadResult.Rejected(
                reasonCode: code,
                message: message,
                matchKey: matchKey,
                pluginFilePath: request.PluginFilePath,
                entryPoint: request.EntryPoint,
                oldVersion: oldVersion,
                newVersion: newVersion);
        }

        private enum HotReloadCommitStage
        {
            ShutdownOld,
            ActivateNew,
            UnloadOld,
            PromoteNew
        }
    }
}
