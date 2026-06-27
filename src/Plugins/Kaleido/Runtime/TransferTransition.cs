using Kaleido.Model.Transfer;
using UnifierTSL.Servers;

namespace Kaleido.Runtime
{
    internal sealed class TransferTransition(
        int playerId,
        ServerContext? source,
        ServerContext target,
        RealmRuntime? sourceRuntime,
        RealmRuntime? targetRuntime,
        RealmAdmission? targetAdmission,
        RealmEntry entry,
        RealmExit exit)
    {
        public int PlayerId { get; } = playerId;
        public ServerContext? Source { get; } = source;
        public ServerContext Target { get; } = target;
        public RealmRuntime? SourceRuntime { get; } = sourceRuntime;
        public RealmRuntime? TargetRuntime { get; } = targetRuntime;
        public RealmAdmission? TargetAdmission { get; } = targetAdmission;
        public RealmEntry Entry { get; } = entry;
        public RealmExit Exit { get; } = exit;
        public bool Applied { get; set; }
        public Task Application { get; set; } = Task.CompletedTask;

        public bool Matches(ServerContext source, ServerContext target)
            => ReferenceEquals(Target, target) && (Source is null || ReferenceEquals(Source, source));
    }
}
