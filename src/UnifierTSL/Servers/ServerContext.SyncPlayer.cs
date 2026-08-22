using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.NetModules;
using Terraria.ID;
using TrProtocol.NetPackets;
using MessageID = Terraria.ID.MessageID;

namespace UnifierTSL.Servers
{
    public partial class ServerContext
    {
        #region Sync Server Online To Player
        public static event Action<ServerContext, int>? SyncServerOnlineToPlayerEvent;
        public virtual void SyncServerOnlineToPlayer(int plr) {
            SyncServerOnlineToPlayerEvent?.Invoke(this, plr);

            Player player = Main.player[plr];
            player.SpawnX = -1;
            player.SpawnY = -1;
            player.chest = -1;
            player.sign = -1;
            player.SetTalkNPC(this, -1);
            player.tileEntityAnchor.Clear();
            player.MinionRestTargetPoint = Vector2.Zero;
            player.MinionAttackTargetNPC = -1;
            player.piggyBankProjTracker.Clear();
            player.voidLensChest.Clear();
            player.PotionOfReturnOriginalUsePosition = null;
            player.PotionOfReturnHomePosition = null;
            player.isOperatingAnotherEntity = false;
            player.netCameraTarget = null;
            player.spectating = -1;

            NetMessage.TrySendData(MessageID.WorldData, plr);
            Main.SyncAnInvasion(plr);
            SendSectionsWhenJoin(plr);
            SendLeashedEntitiesInLoadedSections(plr);
            SendWorldEntities(plr);
            SendWorldInfo(plr);
        }

        protected virtual void SendSectionsWhenJoin(int whoAmI) {
            Player player = Main.player[whoAmI];
            HashSet<Point> sentSections = new();
            List<Point> existingPos = new();

            SendSectionRectAtTile(whoAmI, Main.spawnTileX, Main.spawnTileY, sentSections, existingPos, expanded: false);

            if (Main.teamBasedSpawnsSeed && player.team != 0 && ExtraSpawnPointManager.TryGetExtraSpawnPointForTeam(player.team, out Point teamSpawnPoint)) {
                SendSectionRectAtTile(whoAmI, teamSpawnPoint.X, teamSpawnPoint.Y, sentSections, existingPos, expanded: true);
            }

            PortalHelper.SyncPortalsOnPlayerJoin(whoAmI, 1, existingPos, out List<Point>? portalSections);
            foreach (Point section in portalSections) {
                if (sentSections.Add(section)) {
                    NetMessage.SendSection(whoAmI, section.X, section.Y);
                }
            }
        }
        protected virtual void SendSectionRectAtTile(int whoAmI, int tileX, int tileY, HashSet<Point> sentSections, List<Point> existingPos, bool expanded) {
            int sectionXBegin = Terraria.Netplay.GetSectionX(tileX) - 2;
            int sectionYBegin = Terraria.Netplay.GetSectionY(tileY) - 1;
            int sectionXEnd = sectionXBegin + (expanded ? 6 : 5);
            int sectionYEnd = sectionYBegin + (expanded ? 4 : 3);
            if (sectionXBegin < 0) {
                sectionXBegin = 0;
            }
            if (sectionXEnd >= Main.maxSectionsX) {
                sectionXEnd = Main.maxSectionsX;
            }
            if (sectionYBegin < 0) {
                sectionYBegin = 0;
            }
            if (sectionYEnd >= Main.maxSectionsY) {
                sectionYEnd = Main.maxSectionsY;
            }
            for (int x = sectionXBegin; x < sectionXEnd; x++) {
                for (int y = sectionYBegin; y < sectionYEnd; y++) {
                    Point section = new(x, y);
                    if (!sentSections.Add(section)) {
                        continue;
                    }
                    NetMessage.SendSection(whoAmI, x, y);
                    existingPos.Add(section);
                }
            }
        }
        protected virtual void SendLeashedEntitiesInLoadedSections(int whoAmI) {
            RemoteClient client = Netplay.Clients[whoAmI];
            for (int x = 0; x < Main.maxSectionsX; x++) {
                for (int y = 0; y < Main.maxSectionsY; y++) {
                    if (!client.TileSections[x, y]) {
                        continue;
                    }
                    LeashedEntity.SectionEntityList? section = LeashedEntity.BySection[x, y];
                    section?.Sync(this, whoAmI);
                }
            }
        }
        protected virtual void SendWorldEntities(int whoAmI) {
            Network.LocalClientSender sender = UnifiedServerCoordinator.clientSenders[whoAmI];
            NetMessage.SyncConnectedPlayer(whoAmI);
            NetMessage.TrySendData(MessageID.SyncPlayerChestIndex, whoAmI, -1, null, whoAmI, Main.player[whoAmI].chest);
            NetMessage.TrySendData(MessageID.SyncProjectileTrackers, whoAmI, -1, null, whoAmI);
            NetMessage.TrySendData(MessageID.RequestTileEntityInteraction, whoAmI, -1, null, -1, whoAmI);
            NetMessage.TrySendData(MessageID.MinionRestTargetUpdate, whoAmI, -1, null, whoAmI);
            NetMessage.TrySendData(MessageID.MinionAttackTargetUpdate, whoAmI, -1, null, whoAmI);
            for (int i = 0; i < Terraria.Main.maxItems; i++) {
                NetMessage.TrySendData(MessageID.SyncItem, whoAmI, -1, null, i);
                if (Main.item[i].active) {
                    NetMessage.TrySendData(MessageID.ItemOwner, whoAmI, -1, null, i);
                }
            }
            for (int i = 0; i < Terraria.Main.maxNPCs; i++) {
                NPC npc = Main.npc[i];
                SyncNPC invalidation = new() {
                    NPCSlot = (byte)i,
                    Generation = (byte)(npc.generation + (npc.active ? 1 : 0)),
                    Target = ushort.MaxValue,
                    NPCType = 0,
                    ExtraData = [],
                };
                invalidation.Bit3[0] = true;
                sender.SendDynamicPacket(in invalidation);
                if (!npc.active) {
                    continue;
                }
                NetMessage.TrySendData(MessageID.SyncNPC, whoAmI, -1, null, i);
                if (npc.active) {
                    NetMessage.TrySendData(MessageID.NPCBuffs, whoAmI, -1, null, i);
                }
            }
            for (int i = 0; i < Terraria.Main.maxProjectiles; i++) {
                Projectile projectile = Main.projectile[i];
                if (projectile.active) {
                    ProjectileKey invalidationKey = new(projectile.key.Spawner, projectile.key.Index, (projectile.key.Generation + 1) & 0x3FFF);
                    SyncProjectile invalidation = new() {
                        Key = invalidationKey,
                        Position = projectile.position,
                        Velocity = projectile.velocity,
                        ProjType = 0,
                    };
                    sender.SendFixedPacket(in invalidation);
                    sender.SendFixedPacket(new KillProjectile(invalidationKey, new Vector2(float.NaN, float.NaN)));
                    NetMessage.TrySendData(MessageID.SyncProjectile, whoAmI, -1, null, i);
                }
            }
        }
        protected virtual void SendWorldInfo(int whoAmI) {
            NetManager.SendToClient(Terraria.GameContent.BannerSystem.NetBannersModule.WriteFullState(this), whoAmI);
            NetMessage.TrySendData(57, whoAmI);
            NetMessage.TrySendData(MessageID.MoonlordHorror);
            NetMessage.TrySendData(MessageID.UpdateTowerShieldStrengths, whoAmI);
            NetMessage.TrySendData(MessageID.SyncCavernMonsterType, whoAmI);
            Main.BestiaryTracker.OnPlayerJoining(this, whoAmI);
            CreativePowerManager.SyncThingsToJoiningPlayer(whoAmI);
            Main.PylonSystem.OnPlayerJoining(this, whoAmI);
        }
        #endregion

        #region Sync Server Offline To Player
        public static event Action<ServerContext, int>? SyncServerOfflineToPlayerEvent;
        public virtual void SyncServerOfflineToPlayer(int plr) {
            SyncServerOfflineToPlayerEvent?.Invoke(this, plr);
            SyncCountsAsHostForGameplay(plr, false);

            Network.LocalClientSender sender = UnifiedServerCoordinator.clientSenders[plr];
            RemoteClient client = Netplay.Clients[plr];

            for (int i = 0; i < Terraria.Main.maxChests; i++) {
                Chest? chest = Main.chest[i];
                if (chest is null || !client.TileSections[Terraria.Netplay.GetSectionX(chest.x), Terraria.Netplay.GetSectionY(chest.y)]) {
                    continue;
                }
                sender.SendFixedPacket(new ChestUpdates {
                    Operation = 1,
                    Position = new(chest.x, chest.y),
                    ChestSlot = (short)i,
                });
            }
            foreach (var entity in TileEntity.ByID.Values) {
                if (client.TileSections[Terraria.Netplay.GetSectionX(entity.Position.X), Terraria.Netplay.GetSectionY(entity.Position.Y)]) {
                    sender.SendDynamicPacket(new TileEntitySharing { ID = entity.ID, IsNew = false });
                }
            }

            foreach (TeleportPylonInfo pylon in Main.PylonSystem.Pylons) {
                NetManager.SendToClient(
                    NetTeleportPylonModule.SerializePylonWasAddedOrRemoved(this, pylon, NetTeleportPylonModule.SubPacketType.PylonWasRemoved),
                    plr);
            }

            foreach (CoinLossRevengeSystem.RevengeMarker marker in CoinLossRevengeSystem._markers) {
                NetMessage.TrySendData(MessageID.RemoveRevengeMarker, plr, -1, null, marker.UniqueID);
            }

            for (int i = 0; i < LeashedEntity.ByWhoAmI.Count; i++) {
                if (LeashedEntity.ByWhoAmI[i] is null) {
                    continue;
                }
                Terraria.Net.NetPacket packet = Terraria.Net.NetModule.CreatePacket<LeashedEntity.NetModule>(this);
                packet.Writer.Write((byte)Terraria.GameContent.LeashedEntity.NetModule.MessageType.Remove);
                packet.Writer.Write7BitEncodedInt(i);
                NetManager.SendToClient(packet, plr);
            }

            for (int i = 0; i < Terraria.Main.maxItems; i++) {
                WorldItem item = Main.item[i];
                if (item.active && item.playerIndexTheItemIsReservedFor == plr) {
                    sender.SendFixedPacket(new ItemOwner((short)i, byte.MaxValue, 0, (byte)item.grabDelayPlayer, item.grabDelayTime, item.position));
                }
                else if (!item.active && Main.timeItemSlotCannotBeReusedFor[i] > 0) {
                    sender.SendFixedPacket(new SyncItemDespawn((short)i));
                }
            }
            for (int i = 0; i < Terraria.Main.maxProjectiles; i++) {
                Projectile proj = Main.projectile[i];
                if (!proj.active) {
                    continue;
                }
                NetMessage.TrySendData(MessageID.KillProjectile, plr, -1, null, proj.key, float.NaN, float.NaN);
            }
            for (int i = 0; i < Terraria.Main.maxPlayers; i++) {
                Player player = Main.player[i];
                if (!player.active) {
                    continue;
                }
                NetMessage.TrySendData(MessageID.PlayerActive, plr, i, null, i, 0);
            }
        }
        private void SyncCountsAsHostForGameplay(int whoAmI, bool value) {
            Main.countsAsHostForGameplay[whoAmI] = value;
            NetMessage.TrySendData(MessageID.SetCountsAsHostForGameplay, whoAmI, -1, null, whoAmI, value ? 1 : 0);
        }
        #endregion

        #region Sync Player Join To Others
        public virtual void SyncPlayerJoinToOthers(int whoAmI) {
            SyncCountsAsHostForGameplay(whoAmI, NetMessage.DoesPlayerSlotCountAsAHost(whoAmI));
            NetMessage.SyncOnePlayer(whoAmI, -1, whoAmI);
            NetMessage.greetPlayer(whoAmI);
        }
        #endregion

        #region Sync Player Leave To Others
        public virtual void SyncPlayerLeaveToOthers(int plr) {
            NetMessage.SendData(MessageID.PlayerActive, -1, plr, null, plr, 0);
        }
        #endregion
    }
}
