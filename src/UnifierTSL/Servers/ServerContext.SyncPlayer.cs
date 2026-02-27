using Microsoft.Xna.Framework;
using Terraria;
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
            SyncCountsAsHostForGameplay(plr, NetMessage.DoesPlayerSlotCountAsAHost(plr));

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

            SendSectionRectAtTile(whoAmI, Main.spawnTileX, Main.spawnTileY, sentSections, existingPos);

            if (Main.teamBasedSpawnsSeed && player.team != 0 && ExtraSpawnPointManager.TryGetExtraSpawnPointForTeam(player.team, out Point teamSpawnPoint)) {
                SendSectionRectAtTile(whoAmI, teamSpawnPoint.X, teamSpawnPoint.Y, sentSections, existingPos);
            }

            PortalHelper.SyncPortalsOnPlayerJoin(whoAmI, 1, existingPos, out List<Point>? portalSections);
            foreach (Point section in portalSections) {
                NetMessage.SendSection(whoAmI, section.X, section.Y);
            }
        }
        protected virtual void SendSectionRectAtTile(int whoAmI, int tileX, int tileY, HashSet<Point> sentSections, List<Point> existingPos) {
            int sectionXBegin = Terraria.Netplay.GetSectionX(tileX) - 2;
            int sectionYBegin = Terraria.Netplay.GetSectionY(tileY) - 1;
            int sectionXEnd = sectionXBegin + 5;
            int sectionYEnd = sectionYBegin + 3;
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
            NetMessage.SyncConnectedPlayer(whoAmI);
            for (int i = 0; i < Terraria.Main.maxItems; i++) {
                NetMessage.TrySendData(MessageID.SyncItem, whoAmI, -1, null, i);
                if (Main.item[i].active) {
                    NetMessage.TrySendData(MessageID.ItemOwner, whoAmI, -1, null, i);
                }
            }
            for (int i = 0; i < Terraria.Main.maxNPCs; i++) {
                NetMessage.TrySendData(MessageID.SyncNPC, whoAmI, -1, null, i);
                if (Main.npc[i].active) {
                    NetMessage.TrySendData(MessageID.NPCBuffs, whoAmI, -1, null, i);
                }
            }
            for (int i = 0; i < Terraria.Main.maxProjectiles; i++) {
                if (Main.projectile[i].active) {
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
            NetMessage.TrySendData(MessageID.SetMiscEventValues, whoAmI, -1, null, 0, CreditsRollEvent._creditsRollRemainingTime);
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
                if (!item.active || item.playerIndexTheItemIsReservedFor != plr) {
                    continue;
                }
                sender.SendFixedPacket(new ItemOwner((short)i, 255, item.position));
            }
            for (int i = 0; i < Terraria.Main.maxProjectiles; i++) {
                Projectile proj = Main.projectile[i];
                if (!proj.active) {
                    continue;
                }
                NetMessage.TrySendData(MessageID.KillProjectile, plr, -1, null, proj.identity, proj.owner);
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
            Player player = Main.player[whoAmI];
            NetMessage.TrySendData(MessageID.PlayerActive, -1, whoAmI, null, whoAmI);
            NetMessage.TrySendData(MessageID.SyncPlayer, -1, whoAmI, null, whoAmI);
            NetMessage.TrySendData(MessageID.PlayerLifeMana, -1, whoAmI, null, whoAmI);
            NetMessage.TrySendData(42, -1, whoAmI, null, whoAmI);
            NetMessage.TrySendData(MessageID.PlayerBuffs, -1, whoAmI, null, whoAmI);
            NetMessage.TrySendData(MessageID.SyncLoadout, -1, whoAmI, null, whoAmI, player.CurrentLoadoutIndex);
            for (int i = 0; i < 59; i++) {
                NetMessage.TrySendData(MessageID.SyncEquipment, -1, whoAmI, null, whoAmI, PlayerItemSlotID.Inventory0 + i, player.inventory[i].prefix);
            }
            TrySendingItemArrayToOther(whoAmI, player.armor, PlayerItemSlotID.Armor0);
            TrySendingItemArrayToOther(whoAmI, player.dye, PlayerItemSlotID.Dye0);
            TrySendingItemArrayToOther(whoAmI, player.miscEquips, PlayerItemSlotID.Misc0);
            TrySendingItemArrayToOther(whoAmI, player.miscDyes, PlayerItemSlotID.MiscDye0);
            TrySendingItemArrayToOther(whoAmI, player.bank.item, PlayerItemSlotID.Bank1_0);
            TrySendingItemArrayToOther(whoAmI, player.bank2.item, PlayerItemSlotID.Bank2_0);
            NetMessage.TrySendData(5, -1, whoAmI, null, whoAmI, PlayerItemSlotID.TrashItem, player.trashItem.prefix);
            TrySendingItemArrayToOther(whoAmI, player.bank3.item, PlayerItemSlotID.Bank3_0);
            TrySendingItemArrayToOther(whoAmI, player.bank4.item, PlayerItemSlotID.Bank4_0);
            TrySendingItemArrayToOther(whoAmI, player.Loadouts[0].Armor, PlayerItemSlotID.Loadout1_Armor_0);
            TrySendingItemArrayToOther(whoAmI, player.Loadouts[0].Dye, PlayerItemSlotID.Loadout1_Dye_0);
            TrySendingItemArrayToOther(whoAmI, player.Loadouts[1].Armor, PlayerItemSlotID.Loadout2_Armor_0);
            TrySendingItemArrayToOther(whoAmI, player.Loadouts[1].Dye, PlayerItemSlotID.Loadout2_Dye_0);
            TrySendingItemArrayToOther(whoAmI, player.Loadouts[2].Armor, PlayerItemSlotID.Loadout3_Armor_0);
            TrySendingItemArrayToOther(whoAmI, player.Loadouts[2].Dye, PlayerItemSlotID.Loadout3_Dye_0);

            Point spawnTile = new(Main.spawnTileX, Main.spawnTileY);
            if (Main.teamBasedSpawnsSeed && player.team != 0 && ExtraSpawnPointManager.TryGetExtraSpawnPointForTeam(player.team, out var sp)) {
                spawnTile = sp;
                RemoteClient.CheckSection(whoAmI, sp.ToWorldCoordinates());
                NetMessage.SendData(158, whoAmI, -1, null, whoAmI);
            }
            else {
                NetMessage.SendData(MessageID.PlayerSpawn, whoAmI, -1, null, whoAmI, (byte)PlayerSpawnContext.SpawningIntoWorld);
            }

            player.position = new(
                spawnTile.X * 16 + 8 - player.width / 2,
                spawnTile.Y * 16 - player.height);
            player.velocity = default;
            NetMessage.SendData(MessageID.TeleportEntity, -1, -1, null, 0, whoAmI, player.position.X, player.position.Y, -1);
            NetMessage.greetPlayer(whoAmI);
        }
        private void TrySendingItemArrayToOther(int plr, Item[] array, int slotStartIndex) {
            for (int i = 0; i < array.Length; i++) {
                NetMessage.TrySendData(5, -1, plr, null, plr, slotStartIndex + i, array[i].prefix);
            }
        }
        #endregion

        #region Sync Player Leave To Others
        public virtual void SyncPlayerLeaveToOthers(int plr) {
            NetMessage.SendData(MessageID.PlayerActive, -1, plr, null, plr, 0);
        }
        #endregion
    }
}
