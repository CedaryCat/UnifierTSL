using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using TrProtocol.NetPackets;
using UnifierTSL.Network;

namespace Kaleido.Hosting.SharedProjection
{
    internal sealed class ProjectionHandshakeHandler
    {
        public bool TryHandle(SharedProjectionContext context, LocalClientSender remote, TrProtocol.MessageID packetType) {
            switch (packetType) {
                case TrProtocol.MessageID.RequestWorldInfo:
                    if (remote.Client.State != 1) {
                        ProjectionPacketValidation.RejectHandshake(remote, 2);
                        return true;
                    }
                    remote.Client.State = 2;
                    context.SendWorldData(remote);
                    context.ClearEquipment(remote);
                    return true;
                case TrProtocol.MessageID.RequestTileData:
                    if (remote.Client.State != 2) {
                        ProjectionPacketValidation.RejectHandshake(remote, 3);
                        return true;
                    }
                    context.SendSectionDataWhenEnter(remote);
                    context.Input.InvokeSync(remote);
                    context.SendWorldTime(remote);
                    remote.SendFixedPacket(new StartPlaying());
                    remote.Client.State = 3;
                    return true;
                case TrProtocol.MessageID.SpawnPlayer:
                    if (remote.Client.State != 3) {
                        return false;
                    }

                    int playerId = remote.ID;
                    remote.Client.State = 10;
                    var player = context.Main.player[playerId];
                    player.active = true;
                    player.position = new Vector2(
                        context.Main.spawnTileX * 16 + 8 - player.width / 2,
                        context.Main.spawnTileY * 16 - player.height);
                    player.velocity = default;
                    remote.SendFixedPacket(new SpawnPlayer(
                        (byte)playerId,
                        new Point16(context.Main.spawnTileX, context.Main.spawnTileY),
                        0,
                        0,
                        0,
                        0,
                        PlayerSpawnContext.SpawningIntoWorld));
                    remote.SendFixedPacket(new FinishedConnectingToServer());
                    context.Input.InvokeEntered(remote);
                    return true;
                default:
                    return false;
            }
        }
    }
}
