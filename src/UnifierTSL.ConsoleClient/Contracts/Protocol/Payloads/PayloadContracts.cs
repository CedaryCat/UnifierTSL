using MemoryPack;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Contracts.Terminal;

namespace UnifierTSL.Contracts.Protocol.Payloads {
    public interface ISurfacePayload {
    }

    public enum LifecyclePhase : byte {
        Created,
        Attached,
        Active,
        Suspended,
        Closing,
        Completed,
        Released,
    }

    public enum StreamPayloadKind : byte {
        AppendText,
        AppendLine,
        Separator,
        Clear,
    }

    [MemoryPackable]
    public sealed partial class BootstrapPayload : ISurfacePayload {
        public SurfaceBootstrap Bootstrap { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class SurfaceHostOperationPayload : ISurfacePayload {
        public long OperationSequence { get; init; }
        public required SurfaceHostOperation Operation { get; init; }
    }

    [MemoryPackable]
    [MemoryPackUnion(0, typeof(SurfaceHostPropertiesPatchOperation))]
    [MemoryPackUnion(1, typeof(SurfaceHostClearOperation))]
    public abstract partial class SurfaceHostOperation;

    [MemoryPackable]
    public sealed partial class SurfaceHostPropertiesPatchOperation : SurfaceHostOperation {
        public string? Title { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public int? Left { get; init; }
        public int? Top { get; init; }
        public string? InputEncoding { get; init; }
        public string? OutputEncoding { get; init; }
    }

    [MemoryPackable]
    public sealed partial class SurfaceHostClearOperation : SurfaceHostOperation;

    [MemoryPackable]
    public sealed partial class SurfaceOperationPayload : ISurfacePayload {
        public long OperationSequence { get; init; }
        public required SurfaceOperation Operation { get; init; }
    }

    [MemoryPackable]
    [MemoryPackUnion(0, typeof(StreamSurfaceOperation))]
    [MemoryPackUnion(1, typeof(LifecycleSurfaceOperation))]
    public abstract partial class SurfaceOperation;

    [MemoryPackable]
    public sealed partial class StreamSurfaceOperation : SurfaceOperation {
        public StreamPayload Payload { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class LifecycleSurfaceOperation : SurfaceOperation {
        public LifecyclePayload Payload { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class StreamPayload {
        public long StreamSequence { get; init; }
        public StreamPayloadKind Kind { get; init; }
        public StreamChannel Channel { get; init; } = StreamChannel.Output;
        public string Text { get; init; } = string.Empty;
        public bool IsAnsi { get; init; }
        public StyledTextLine? StyledText { get; init; }
        public StyleDictionary? Styles { get; init; }
    }

    [MemoryPackable]
    public sealed partial class InputEventPayload : ISurfacePayload {
        public InteractionScopeId? InteractionScopeId { get; init; }
        public long InputSequence { get; init; }
        public InputEvent Event { get; init; } = new();
        public ClientBufferedEditorState? BufferedEditorState { get; init; }
    }

    [MemoryPackable]
    public sealed partial class LifecyclePayload : ISurfacePayload {
        public InteractionScopeId? InteractionScopeId { get; init; }
        public long LifecycleSequence { get; init; }
        public LifecyclePhase Phase { get; init; } = LifecyclePhase.Created;
        public string InteractionKind { get; init; } = string.Empty;
        public bool IsTransient { get; init; } = true;
    }

    [MemoryPackable]
    public sealed partial class SurfaceCompletionPayload : ISurfacePayload {
        public InteractionScopeId? InteractionScopeId { get; init; }
        public long CompletionSequence { get; init; }
        public SurfaceCompletion Completion { get; init; } = new();
    }
}
