using MemoryPack;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Sessions;

namespace UnifierTSL.Contracts.Protocol.Wire {
    public static class SurfaceWireCodec {
        public static SurfaceWireEnvelope Encode(ISurfacePayload payload) {
            return payload switch {
                BootstrapPayload typed => Encode(SurfaceWirePayloadKind.Bootstrap, typed),
                ProjectionSnapshotOperationPayload typed => Encode(SurfaceWirePayloadKind.ProjectionSnapshotOperation, typed),
                SurfaceHostOperationPayload typed => Encode(SurfaceWirePayloadKind.SurfaceHostOperation, typed),
                SurfaceOperationPayload typed => Encode(SurfaceWirePayloadKind.SurfaceOperation, typed),
                InputEventPayload typed => Encode(SurfaceWirePayloadKind.InputEvent, typed),
                LifecyclePayload typed => Encode(SurfaceWirePayloadKind.Lifecycle, typed),
                SurfaceCompletionPayload typed => Encode(SurfaceWirePayloadKind.SurfaceCompletion, typed),
                _ => throw new InvalidOperationException($"Unsupported surface-client payload type: {payload.GetType().FullName}."),
            };
        }

        public static bool TryDecode(byte packetId, byte[] content, out ISurfacePayload? payload) {
            payload = null;
            if (packetId != SurfaceWireEnvelope.id) {
                return false;
            }

            payload = Decode(IPacket.Read<SurfaceWireEnvelope>(content));
            return true;
        }

        public static ISurfacePayload Decode(SurfaceWireEnvelope envelope) {
            ISurfacePayload payload = envelope.Kind switch {
                SurfaceWirePayloadKind.Bootstrap => Deserialize<BootstrapPayload>(envelope),
                SurfaceWirePayloadKind.ProjectionSnapshotOperation => Deserialize<ProjectionSnapshotOperationPayload>(envelope),
                SurfaceWirePayloadKind.SurfaceHostOperation => Deserialize<SurfaceHostOperationPayload>(envelope),
                SurfaceWirePayloadKind.SurfaceOperation => Deserialize<SurfaceOperationPayload>(envelope),
                SurfaceWirePayloadKind.InputEvent => Deserialize<InputEventPayload>(envelope),
                SurfaceWirePayloadKind.Lifecycle => Deserialize<LifecyclePayload>(envelope),
                SurfaceWirePayloadKind.SurfaceCompletion => Deserialize<SurfaceCompletionPayload>(envelope),
                _ => throw new InvalidDataException($"Unsupported surface-client payload kind: {envelope.Kind}."),
            };
            if (payload is BootstrapPayload bootstrap) {
                EnsureProtocolVersion(bootstrap.Bootstrap.ProtocolVersion, SurfaceProtocolVersion.Initial);
            }

            return payload;
        }

        private static SurfaceWireEnvelope Encode<TPayload>(SurfaceWirePayloadKind kind, TPayload payload) where TPayload : class {
            return new SurfaceWireEnvelope(kind, MemoryPackSerializer.Serialize(payload));
        }

        private static TPayload Deserialize<TPayload>(SurfaceWireEnvelope envelope) where TPayload : class {
            return MemoryPackSerializer.Deserialize<TPayload>(envelope.PayloadContent)
                ?? throw new InvalidDataException($"Failed to deserialize {typeof(TPayload).Name} from surface-client wire payload.");
        }

        private static void EnsureProtocolVersion(SurfaceProtocolVersion actualVersion, SurfaceProtocolVersion expectedVersion) {
            if (actualVersion.Major != expectedVersion.Major || actualVersion.Minor != expectedVersion.Minor) {
                throw new InvalidDataException($"Unsupported surface protocol version: {actualVersion}.");
            }
        }
    }
}
