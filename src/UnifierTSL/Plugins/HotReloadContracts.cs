using System.Collections.Immutable;
using Newtonsoft.Json.Linq;

namespace UnifierTSL.Plugins
{
    public enum HotReloadReasonCode
    {
        None = 0,
        TargetNotFound,
        NotOptedIn,
        NoUpdatedCandidate,
        AmbiguousCandidate,
        InvalidRequest,
        DependencyBlocked,
        LoadFailed,
        ExportFailed,
        RejectedByOld,
        RejectedByNew,
        UnsupportedSchema,
        MissingRequiredState,
        PrepareFailed,
        ShutdownFailed,
        ActivateFailed,
        RollbackFailed
    }

    public readonly record struct HotReloadPlayerSnapshot(int ClientIndex, bool IsActive, string? ServerName, string? PlayerName);

    public readonly record struct HotReloadRuntimeSnapshot(
        DateTime CapturedAtUtc,
        int ActiveConnections,
        ImmutableArray<HotReloadPlayerSnapshot> Players);

    public sealed record HotReloadEnvelope(
        int SchemaVersion,
        string MatchKey,
        Version OldVersion,
        Version NewVersion,
        JObject State,
        HotReloadRuntimeSnapshot RuntimeSnapshot);

    public sealed record HotReloadExportContext(
        int SchemaVersion,
        string MatchKey,
        Version CurrentVersion,
        Version CandidateVersion,
        HotReloadRuntimeSnapshot RuntimeSnapshot);

    public sealed record HotReloadPrepareContext(
        HotReloadEnvelope Envelope,
        IPluginConfigRegistrar ConfigRegistrar);

    public sealed record HotReloadActivateContext(
        HotReloadEnvelope Envelope,
        IPluginConfigRegistrar ConfigRegistrar);

    public sealed record HotReloadAbortContext(
        HotReloadEnvelope Envelope,
        HotReloadReasonCode FailureReason,
        string? Message);

    public readonly record struct HotReloadExportResult(bool Accepted, JObject? State, HotReloadReasonCode ReasonCode, string? Message)
    {
        public static HotReloadExportResult Success(JObject state) => new(true, state, HotReloadReasonCode.None, null);
        public static HotReloadExportResult Reject(HotReloadReasonCode reasonCode, string? message = null) => new(false, null, reasonCode, message);
    }

    public readonly record struct HotReloadPrepareResult(bool Accepted, HotReloadReasonCode ReasonCode, string? Message)
    {
        public static HotReloadPrepareResult Success() => new(true, HotReloadReasonCode.None, null);
        public static HotReloadPrepareResult Reject(HotReloadReasonCode reasonCode, string? message = null) => new(false, reasonCode, message);
    }

    public readonly record struct HotReloadActivateResult(bool Accepted, HotReloadReasonCode ReasonCode, string? Message)
    {
        public static HotReloadActivateResult Success() => new(true, HotReloadReasonCode.None, null);
        public static HotReloadActivateResult Reject(HotReloadReasonCode reasonCode, string? message = null) => new(false, reasonCode, message);
    }

    public readonly record struct HotReloadAbortResult(bool Recovered, HotReloadReasonCode ReasonCode, string? Message)
    {
        public static HotReloadAbortResult Success() => new(true, HotReloadReasonCode.None, null);
        public static HotReloadAbortResult Failed(HotReloadReasonCode reasonCode, string? message = null) => new(false, reasonCode, message);
    }
}
