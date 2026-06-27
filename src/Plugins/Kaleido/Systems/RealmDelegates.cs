namespace Kaleido.Systems
{
    public delegate RealmJoinDecision? RealmJoinHandler(RealmJoin join);
    public delegate ValueTask RealmMaintenanceCallback(CancellationToken cancellationToken);
    public delegate ValueTask RealmEventHandler<in TEvent>(TEvent evt, CancellationToken cancellationToken);
}
