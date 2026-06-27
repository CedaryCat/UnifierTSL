using Kaleido.Model.Instances;
using UnifierTSL.Servers;

namespace Kaleido.Model.Transfer
{
    public sealed record RealmTransferResult(bool Succeeded, RealmInstance? Instance, ServerTransferResult? ServerResult, string? Error)
    {
        public static RealmTransferResult Success(RealmInstance? instance, ServerTransferResult result) => new(true, instance, result, null);
        public static RealmTransferResult Failure(string error, RealmInstance? instance = null, ServerTransferResult? result = null) => new(false, instance, result, error);
    }
}
