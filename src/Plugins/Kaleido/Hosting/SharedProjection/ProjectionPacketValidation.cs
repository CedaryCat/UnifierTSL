using Terraria.Localization;
using TrProtocol;
using UnifierTSL.Network;
using ProtocolMessageID = TrProtocol.MessageID;

namespace Kaleido.Hosting.SharedProjection
{
    internal static class ProjectionPacketValidation
    {
        public static bool ValidateHandshakeState(LocalClientSender remote, ProtocolMessageID packetType) {
            if (remote.Client.State >= 10 || IsHandshakePacketAllowed(packetType)) {
                return true;
            }

            RejectHandshake(remote, 1);
            return false;
        }

        public static void RejectHandshake(LocalClientSender remote, int code) {
            remote.Kick(NetworkText.FromLiteral($"This realm rejected the request. code {code}"));
        }

        private static bool IsHandshakePacketAllowed(ProtocolMessageID packetType) {
            int id = (int)packetType;
            return id <= 12
                || id is 93 or 16 or 42 or 50 or 38 or 68 or 147
                || packetType is ProtocolMessageID.PlayerPlatformInfo;
        }
    }
}
