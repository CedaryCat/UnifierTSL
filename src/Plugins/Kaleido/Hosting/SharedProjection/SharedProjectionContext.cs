using System.Net;
using Microsoft.Xna.Framework;
using Terraria;
using TrProtocol.Models;
using TrProtocol.NetPackets;
using UnifierTSL;
using UnifierTSL.Network;
using UnifierTSL.Servers;
using UnifierTSL.Surface.Hosting.Server;

namespace Kaleido.Hosting.SharedProjection
{
    public sealed class SharedProjectionContext : global::UnifierTSL.Servers.ServerContext
    {
        private readonly ProjectionEntities entities = new();
        private readonly IProjectionWorldDataProvider worldData;

        public SharedProjectionContext(string serverName, IProjectionWorldDataProvider worldData) : base(serverName, worldData, UnifierApi.LogCore) {
            this.worldData = worldData;
            Input = new(this);
            Main.player = entities.Players;
            Main.npc = entities.Npcs;
            Main.projectile = entities.Projectiles;
            Main.item = entities.Items;
            Main.dust = entities.Dusts;
            Main.gore = entities.Gores;

            Main.ServerSideCharacter = true;
            NPC.combatBookWasUsed = true;

            Main.tile = worldData.CreateTileProvider();
            Main._cameraSceneMetrics = new(this);
            Main.Initialize_TileAndNPCData1();
            Main.Initialize_TileAndNPCData2();
            Main.Initialize_Items();

            Main.maxTilesX = worldData.MaxTilesX;
            Main.maxTilesY = worldData.MaxTilesY;
            Main.spawnTileX = worldData.SpawnTileX;
            Main.spawnTileY = worldData.SpawnTileY;
            Main.worldSurface = worldData.WorldSurface;
            Main.rockLayer = worldData.RockLayer;
            WorldGen.setWorldSize();
            ActiveSections.Reset();
            LeashedEntity.Clear();

            Netplay.Connection.ResetSpecialFlags();
            Netplay.ResetNetDiag();

            Netplay.ServerIP = IPAddress.Any;
            Main.menuMode = 14;
            Main.statusText = Lang.menu[8].Value;
            Netplay.Disconnect = false;

            Netplay.Clients = ServerRuntime.Clients;
            NetMessage.buffer = ServerRuntime.MessageBuffers;
            Main.PylonSystem = new(this);
        }

        internal ProjectionDispatcher ProjectionDispatcher => (ProjectionDispatcher)Dispatcher;
        internal ProjectionInput Input { get; }

        protected sealed override ServerDispatcher CreateDispatcher() => new ProjectionDispatcher(this);

        protected sealed override ServerSurfaceConsole CreateSurfaceConsole() {
            return new LauncherBackedServerSurfaceConsole(this);
        }

        public override void SyncServerOnlineToPlayer(int plr) {
            var remote = ServerRuntime.GetSender(plr);
            SendWorldData(remote);
            ClearEquipment(remote);
            SendSectionDataWhenEnter(remote);
            Input.InvokeSync(remote);
            SyncPlayerJoinToOthers(plr);
            Input.InvokeEntered(remote);
        }

        public override void SyncServerOfflineToPlayer(int plr) {
            SyncPlayerLeaveToOthers(plr);
        }

        public override void SyncPlayerJoinToOthers(int whoAmI) {
            var player = Main.player[whoAmI];
            player.active = true;
            NetMessage.SendData(Terraria.ID.MessageID.PlayerSpawn, whoAmI, -1, null, whoAmI, (byte)PlayerSpawnContext.SpawningIntoWorld);
            player.position = new Vector2(
                Main.spawnTileX * 16 + 8 - player.width / 2,
                Main.spawnTileY * 16 - player.height);
            player.velocity = default;
            NetMessage.SendData(Terraria.ID.MessageID.TeleportEntity, -1, -1, null, 0, whoAmI, player.position.X, player.position.Y, -1);
            NetMessage.greetPlayer(whoAmI);
        }

        internal void SendWorldData(LocalClientSender remote) {
            remote.SendDynamicPacket(worldData.CreateWorldDataPacket(this));
            SendWorldTime(remote);
        }

        internal void SendWorldTime(LocalClientSender remote) {
            NetMessage.SendData((int)TrProtocol.MessageID.TimeSet, remote.ID, -1, null, Main.dayTime ? 1 : 0, (int)Main.time, Main.sunModY, Main.moonModY);
        }

        internal void ClearEquipment(LocalClientSender remote) {
            for (short slot = 0; slot <= 58; slot++) {
                remote.SendFixedPacket(new SyncEquipment((byte)remote.ID, slot, 0, 0, 0, default));
            }
        }

        internal void SendSectionDataWhenEnter(LocalClientSender remote) {
            if (remote.Client.State == 2) {
                remote.SendDynamicPacket(new StatusText(6, Lang.inter[44].ToNetworkText(), 0));
                remote.Client.State = 3;
            }

            int sectionBeginX = Terraria.Netplay.GetSectionX(Main.spawnTileX) - 1;
            int sectionBeginY = Terraria.Netplay.GetSectionY(Main.spawnTileY) - 1;
            for (int x = 0; x < 3; x++) {
                for (int y = 0; y < 2; y++) {
                    NetMessage.SendSection(remote.ID, sectionBeginX + x, sectionBeginY + y);
                }
            }
        }

        internal void CheckBytes(int playerId) => NetMessage.CheckBytes(playerId);
    }
}
