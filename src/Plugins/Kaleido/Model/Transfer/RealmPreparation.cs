using Kaleido.Model.Instances;

namespace Kaleido.Model.Transfer
{
    public sealed record RealmPreparation(RealmInstance Instance, RealmHold? Hold) : IDisposable
    {
        public void Dispose() => Hold?.Dispose();
    }
}
