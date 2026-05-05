using MemoryPack;

namespace UnifierTSL.Contracts.Sessions {
    [MemoryPackable]
    public readonly partial record struct SurfaceProtocolVersion(int Major, int Minor) {
        public static SurfaceProtocolVersion Initial { get; } = new(0, 17);
        public override string ToString() => $"{Major}.{Minor}";
    }

    [MemoryPackable]
    public sealed partial class SurfaceRevisionSet {
        public long BootstrapRevision { get; init; }
        public long DefinitionRevision { get; init; }
        public long StateRevision { get; init; }
        public long StyleVersion { get; init; }
    }

    [MemoryPackable]
    public sealed partial class SurfaceBootstrap {
        public SurfaceProtocolVersion ProtocolVersion { get; init; } = SurfaceProtocolVersion.Initial;
        public SurfaceRevisionSet Revisions { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class SurfaceCompletion {
        public SurfaceCompletionKind Kind { get; init; }
        public bool Accepted { get; init; }
        public string? TextResult { get; init; }
        public string PayloadJson { get; init; } = string.Empty;
    }
}
