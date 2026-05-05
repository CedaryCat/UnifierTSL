using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Terminal;

namespace UnifierTSL.Contracts.Protocol.Payloads {
    public static class SurfaceOperations {
        public const string StreamId = "stream";
        public const string LifecycleId = "lifecycle";

        public static SurfaceOperation Stream(StreamPayload payload) {
            ArgumentNullException.ThrowIfNull(payload);
            return new StreamSurfaceOperation {
                Payload = payload,
            };
        }

        public static SurfaceOperation Stream(
            StreamPayloadKind kind,
            string text = "",
            bool isAnsi = false,
            StreamChannel channel = StreamChannel.Output) {
            return Stream(new StreamPayload {
                Kind = kind,
                Channel = channel,
                Text = text,
                IsAnsi = isAnsi,
            });
        }

        public static SurfaceOperation Stream(
            StreamPayloadKind kind,
            StyledTextLine styledText,
            StyleDictionary? styles = null,
            StreamChannel channel = StreamChannel.Output) {
            ArgumentNullException.ThrowIfNull(styledText);
            return Stream(new StreamPayload {
                Kind = kind,
                Channel = channel,
                StyledText = styledText,
                Styles = styles,
            });
        }

        public static SurfaceOperation Lifecycle(LifecyclePayload payload) {
            ArgumentNullException.ThrowIfNull(payload);
            return new LifecycleSurfaceOperation {
                Payload = payload,
            };
        }

        public static bool TryGetStream(SurfaceOperation operation, out StreamPayload payload) {
            ArgumentNullException.ThrowIfNull(operation);
            if (operation is StreamSurfaceOperation typed) {
                payload = typed.Payload;
                return true;
            }

            payload = null!;
            return false;
        }

        public static bool TryGetLifecycle(SurfaceOperation operation, out LifecyclePayload payload) {
            ArgumentNullException.ThrowIfNull(operation);
            if (operation is LifecycleSurfaceOperation typed) {
                payload = typed.Payload;
                return true;
            }

            payload = null!;
            return false;
        }
    }
}
