using MemoryPack;

namespace UnifierTSL.Contracts.Projection {
    public enum ProjectionInputKind : byte {
        None,
        SingleLine,
        MultiLine,
        ReadonlyBuffer,
    }

    public enum ProjectionInputAuthority : byte {
        None,
        ClientBuffered,
        HostBuffered,
        Readonly,
    }

    public enum ProjectionMultilineSubmitMode : byte {
        AlwaysSubmit,
        UseReadiness,
    }

    public enum ProjectionInputCommandKind : byte {
        Submit,
        AlternateSubmit,
        InsertNewLine,
        DismissAssist,
        PreviousContextItem,
        NextContextItem,
    }

    [MemoryPackable]
    public sealed partial class ProjectionKeyboardGesture {
        public string Key { get; init; } = string.Empty;
        public bool Shift { get; init; }
        public bool Alt { get; init; }
        public bool Control { get; init; }
        public bool Meta { get; init; }
    }

    [MemoryPackable]
    [MemoryPackUnion(0, typeof(ProjectionCommandInputBinding))]
    [MemoryPackUnion(1, typeof(ProjectionActionInputBinding))]
    public abstract partial class ProjectionInputBinding {
        public ProjectionKeyboardGesture Gesture { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class ProjectionCommandInputBinding : ProjectionInputBinding {
        public ProjectionInputCommandKind Command { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ProjectionActionInputBinding : ProjectionInputBinding {
        public string ActionId { get; init; } = string.Empty;
    }

    [MemoryPackable]
    public sealed partial class ProjectionAuthoringPolicy {
        public bool OpensCompletionAutomatically { get; init; } = true;
        public bool CapturesRawKeys { get; init; }
        public ProjectionMultilineSubmitMode MultilineSubmitMode { get; init; } = ProjectionMultilineSubmitMode.AlwaysSubmit;
    }

    [MemoryPackable]
    public sealed partial class ProjectionInputPolicy {
        public ProjectionInputKind Kind { get; init; }
        public ProjectionInputAuthority Authority { get; init; }
        public ProjectionInputBinding[] Bindings { get; init; } = [];
        public ProjectionAuthoringPolicy Authoring { get; init; } = new();
    }
}
