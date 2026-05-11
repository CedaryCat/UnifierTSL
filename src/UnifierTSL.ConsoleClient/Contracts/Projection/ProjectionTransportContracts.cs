using MemoryPack;
using UnifierTSL.Contracts.Protocol.Payloads;

namespace UnifierTSL.Contracts.Projection {
    [MemoryPackable]
    public readonly partial record struct ProjectionProtocolVersion(int Major, int Minor) {
        public static ProjectionProtocolVersion Initial { get; } = new(0, 1);
    }

    [MemoryPackable]
    public sealed partial class ProjectionSequenceSet {
        public long LifecycleSequence { get; init; }
        public long DocumentSequence { get; init; }
        public long StreamSequence { get; init; }
        public long InputSequence { get; init; }
        public long CompletionSequence { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ProjectionRevisionSet {
        public long BootstrapRevision { get; init; }
        public long DefinitionRevision { get; init; }
        public long StateRevision { get; init; }
        public long StyleRevision { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ProjectionBootstrap {
        public ProjectionProtocolVersion ProtocolVersion { get; init; } = ProjectionProtocolVersion.Initial;
        public ProjectionRevisionSet Revisions { get; init; } = new();
    }

    [MemoryPackable]
    [MemoryPackUnion(0, typeof(ProjectionFullSnapshotBody))]
    [MemoryPackUnion(1, typeof(ProjectionUpdateSnapshotBody))]
    public abstract partial class ProjectionSnapshotBody;

    [MemoryPackable]
    public sealed partial class ProjectionFullSnapshotBody : ProjectionSnapshotBody {
        public required ProjectionDocument Document { get; init; }
    }

    // Reserved for explicit incremental transport; current producers still emit full snapshots.
    [MemoryPackable]
    public sealed partial class ProjectionUpdateSnapshotBody : ProjectionSnapshotBody {
        public required ProjectionScope Scope { get; init; }
        public required ProjectionDocumentPatch Patch { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ProjectionSnapshotPayload : ISurfacePayload {
        public ProjectionRevisionSet Revisions { get; init; } = new();
        public ProjectionSequenceSet Sequences { get; init; } = new();
        public required ProjectionSnapshotBody Body { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ProjectionSnapshotOperationPayload : ISurfacePayload {
        public long OperationSequence { get; init; }
        public required ProjectionSnapshotPayload Snapshot { get; init; }
    }
}
