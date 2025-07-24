using Microsoft.Xna.Framework;
using Terraria.ID;
using Terraria.Localization;
using TrProtocol.NetPackets;
using Terraria;
using MessageID = Terraria.ID.MessageID;

namespace UnifierTSL.Servers
{
    public partial class ServerContext
	{
		#region Sync Server Online To Player
		public virtual void SyncServerOnlineToPlayer(int plr) {
			NetMessage.TrySendData(MessageID.WorldData, plr);
			Main.SyncAnInvasion(plr);
			SendSectionsWhenJoin(plr);
			SendWorldEntities(plr);
			SendWorldInfo(plr);
		}

		protected virtual void SendSectionsWhenJoin(int whoAmI) {
			int spawnSectionXBegin = Terraria.Netplay.GetSectionX(Main.spawnTileX) - 2;
			int spawnSectionYBegin = Terraria.Netplay.GetSectionY(Main.spawnTileY) - 1;
			int spawnSectionXEnd = spawnSectionXBegin + 5;
			int spawnSectionYEnd = spawnSectionYBegin + 3;
			if (spawnSectionXBegin < 0) {
				spawnSectionXBegin = 0;
			}
			if (spawnSectionXEnd >= Main.maxSectionsX) {
				spawnSectionXEnd = Main.maxSectionsX;
			}
			if (spawnSectionYBegin < 0) {
				spawnSectionYBegin = 0;
			}
			if (spawnSectionYEnd >= Main.maxSectionsY) {
				spawnSectionYEnd = Main.maxSectionsY;
			}
			List<Point> existingPos = new((spawnSectionXEnd - spawnSectionXBegin) * (spawnSectionYEnd - spawnSectionYBegin));
			for (int x = spawnSectionXBegin; x < spawnSectionXEnd; x++) {
				for (int y = spawnSectionYBegin; y < spawnSectionYEnd; y++) {
					NetMessage.SendSection(whoAmI, x, y);
					existingPos.Add(new Point(x, y));
				}
			}
			PortalHelper.SyncPortalsOnPlayerJoin(whoAmI, 1, existingPos, out var portalSections);
			foreach (var section in portalSections) {
				NetMessage.SendSection(whoAmI, section.X, section.Y);
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
			}
			for (int i = 0; i < Terraria.Main.maxProjectiles; i++) {
				if (Main.projectile[i].active) {
					NetMessage.TrySendData(MessageID.SyncProjectile, whoAmI, -1, null, i);
				}
			}
		}
		protected virtual void SendWorldInfo(int whoAmI) {
			for (int i = 0; i < 290; i++) {
				NetMessage.TrySendData(MessageID.NPCKillCountDeathTally, whoAmI, -1, null, i);
			}
			NetMessage.TrySendData(57, whoAmI);
			NetMessage.TrySendData(MessageID.MoonlordHorror);
			NetMessage.TrySendData(MessageID.UpdateTowerShieldStrengths, whoAmI);
			NetMessage.TrySendData(MessageID.SyncCavernMonsterType, whoAmI);
			Main.BestiaryTracker.OnPlayerJoining(this, whoAmI);
			CreativePowerManager.SyncThingsToJoiningPlayer(whoAmI);
			Main.PylonSystem.OnPlayerJoining(this, whoAmI);
			NetMessage.TrySendData(MessageID.AnglerQuest, whoAmI, -1, NetworkText.FromLiteral(this.Main.player[whoAmI].name), this.Main.anglerQuest);
		}
		#endregion

		#region Sync Server Offline To Player
		public virtual void SyncServerOfflineToPlayer(int plr) {
			var sender = UnifiedServerCoordinator.clientSenders[plr];

            for (int i = 0; i < Terraria.Main.maxItems; i++) {
				var item = Main.item[i];
				if (!item.active || item.playerIndexTheItemIsReservedFor != plr) {
					continue;
				}
                sender.SendFixedPacket(new ItemOwner((short)i, 255));
			}
			for (int i = 0; i < Terraria.Main.maxProjectiles; i++) {
				var proj = Main.projectile[i];
				if (!proj.active) {
					continue;
				}
				NetMessage.TrySendData(MessageID.KillProjectile, plr, -1, null, proj.identity, proj.owner);
			}
			for (int i = 0; i < Terraria.Main.maxPlayers; i++) {
				var player = Main.player[i];
				if (!player.active) {
					continue;
				}
				NetMessage.TrySendData(MessageID.PlayerActive, plr, i, null, i, 0);
			}
		}
		#endregion

		#region Sync Player Join To Others
		public virtual void SyncPlayerJoinToOthers(int whoAmI) {
			var player = Main.player[whoAmI];
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

			NetMessage.SendData(MessageID.PlayerSpawn, whoAmI, -1, null, whoAmI, (byte)PlayerSpawnContext.SpawningIntoWorld);
			player.position = new(
				Main.spawnTileX * 16 + 8 - player.width / 2,
				Main.spawnTileY * 16 - player.height);
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
