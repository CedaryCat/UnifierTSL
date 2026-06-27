using System.Collections.Immutable;
using Kaleido.Hosting;
using Kaleido.Model.Hosting;
using Kaleido.Model.Ids;
using Kaleido.Model.Lifecycle;
using Kaleido.Systems.Installation;
using UnifierTSL.Servers;

namespace Kaleido.Model.Planning
{
    public sealed record RealmPlan(
        RealmKey Key,
        string DisplayName,
        RealmHostRequirement Host,
        RealmLifecyclePolicy Lifecycle,
        Func<RealmCreation, ServerContext> ContextFactory,
        ImmutableArray<IRealmContentInstaller> Content,
        ImmutableHashSet<string> Tags,
        ImmutableDictionary<string, object?> Metadata)
    {
        public static RealmPlan Create(
            RealmKey key,
            string displayName,
            RealmHostRequirement host,
            RealmLifecyclePolicy lifecycle,
            Func<RealmCreation, ServerContext> contextFactory,
            IEnumerable<IRealmContentInstaller>? content = null,
            IEnumerable<string>? tags = null,
            IReadOnlyDictionary<string, object?>? metadata = null) {

            return new(
                key,
                displayName,
                host,
                lifecycle,
                contextFactory,
                content is null ? ImmutableArray<IRealmContentInstaller>.Empty : [.. content],
                tags is null ? ImmutableHashSet<string>.Empty : [.. tags.Where(static tag => !string.IsNullOrWhiteSpace(tag))],
                metadata is null ? ImmutableDictionary<string, object?>.Empty : metadata.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase));
        }
    }
}
