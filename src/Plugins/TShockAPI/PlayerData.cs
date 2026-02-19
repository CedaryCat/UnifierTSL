/*
TShock, a server mod for Terraria
Copyright (C) 2011-2019 Pryaxis & TShock Contributors

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.GameContent.NetModules;
using Terraria.ID;

namespace TShockAPI
{
	public class PlayerData
	{
		public NetItem[] inventory = new NetItem[NetItem.MaxInventory];
		public int health = TShock.ServerSideCharacterConfig.Settings.StartingHealth;
		public int maxHealth = TShock.ServerSideCharacterConfig.Settings.StartingHealth;
		public int mana = TShock.ServerSideCharacterConfig.Settings.StartingMana;
		public int maxMana = TShock.ServerSideCharacterConfig.Settings.StartingMana;
		public bool exists;
		public int spawnX = -1;
		public int spawnY = -1;
		public int? extraSlot;
		public int? skinVariant;
		public int? hair;
		public byte hairDye;
		public Color? hairColor;
		public Color? pantsColor;
		public Color? shirtColor;
		public Color? underShirtColor;
		public Color? shoeColor;
		public Color? skinColor;
		public Color? eyeColor;
		public bool[] hideVisuals;
		public int questsCompleted;
		public int usingBiomeTorches;
		public int happyFunTorchTime;
		public int unlockedBiomeTorches;
		public int currentLoadoutIndex;
		public int ateArtisanBread;
		public int usedAegisCrystal;
		public int usedAegisFruit;
		public int usedArcaneCrystal;
		public int usedGalaxyPearl;
		public int usedGummyWorm;
		public int usedAmbrosia;
		public int unlockedSuperCart;
		public int enabledSuperCart;
		public int deathsPVE;
		public int deathsPVP;

		/// <summary>
		/// Sets the default values for the inventory.
		/// </summary>
		[Obsolete("The player argument is not used.")]
		public PlayerData(TSPlayer player) : this(true) { }

		/// <summary>
		/// Sets the default values for the inventory.
		/// </summary>
		/// <param name="includingStarterInventory">Is it necessary to load items from TShock's config</param>
		public PlayerData(bool includingStarterInventory = true)
		{
			for (int i = 0; i < NetItem.MaxInventory; i++)
				this.inventory[i] = new NetItem();

			if (includingStarterInventory)
				for (int i = 0; i < TShock.ServerSideCharacterConfig.Settings.StartingInventory.Count; i++)
				{
					var item = TShock.ServerSideCharacterConfig.Settings.StartingInventory[i];
					StoreSlot(i, item.NetId, item.PrefixId, item.Stack, item.Favorited);
				}
		}

		/// <summary>
		/// Stores an item at the specific storage slot
		/// </summary>
		/// <param name="slot"></param>
		/// <param name="netID"></param>
		/// <param name="prefix"></param>
		/// <param name="stack"></param>
		public void StoreSlot(int slot, int netID, byte prefix, int stack)
		{
			StoreSlot(slot, netID, prefix, stack, false);
		}

		/// <summary>
		/// Stores an item at the specific storage slot
		/// </summary>
		/// <param name="slot"></param>
		/// <param name="netID"></param>
		/// <param name="prefix"></param>
		/// <param name="stack"></param>
		/// <param name="favorited"></param>
		public void StoreSlot(int slot, int netID, byte prefix, int stack, bool favorited)
		{
			StoreSlot(slot, new NetItem(netID, stack, prefix, favorited));
		}

		/// <summary>
		/// Stores an item at the specific storage slot
		/// </summary>
		/// <param name="slot"></param>
		/// <param name="item"></param>
		public void StoreSlot(int slot, NetItem item)
		{
			if (slot > (this.inventory.Length - 1) || slot < 0) //if the slot is out of range then dont save
			{
				return;
			}

			this.inventory[slot] = item;
		}

		/// <summary>
		/// Copies a characters data to this object
		/// </summary>
		/// <param name="player"></param>
		public void CopyCharacter(TSPlayer player)
		{
			this.health = player.TPlayer.statLife > 0 ? player.TPlayer.statLife : 1;
			this.maxHealth = player.TPlayer.statLifeMax;
			this.mana = player.TPlayer.statMana;
			this.maxMana = player.TPlayer.statManaMax;
			this.spawnX = player.TPlayer.SpawnX;
			this.spawnY = player.TPlayer.SpawnY;
			extraSlot = player.TPlayer.extraAccessory ? 1 : 0;
			this.skinVariant = player.TPlayer.skinVariant;
			this.hair = player.TPlayer.hair;
			this.hairDye = player.TPlayer.hairDye;
			this.hairColor = player.TPlayer.hairColor;
			this.pantsColor = player.TPlayer.pantsColor;
			this.shirtColor = player.TPlayer.shirtColor;
			this.underShirtColor = player.TPlayer.underShirtColor;
			this.shoeColor = player.TPlayer.shoeColor;
			this.hideVisuals = player.TPlayer.hideVisibleAccessory;
			this.skinColor = player.TPlayer.skinColor;
			this.eyeColor = player.TPlayer.eyeColor;
			this.questsCompleted = player.TPlayer.anglerQuestsFinished;
			this.usingBiomeTorches = player.TPlayer.UsingBiomeTorches ? 1 : 0;
			this.happyFunTorchTime = player.TPlayer.happyFunTorchTime ? 1 : 0;
			this.unlockedBiomeTorches = player.TPlayer.unlockedBiomeTorches ? 1 : 0;
			this.currentLoadoutIndex = player.TPlayer.CurrentLoadoutIndex;
			this.ateArtisanBread = player.TPlayer.ateArtisanBread ? 1 : 0;
			this.usedAegisCrystal = player.TPlayer.usedAegisCrystal ? 1 : 0;
			this.usedAegisFruit = player.TPlayer.usedAegisFruit ? 1 : 0;
			this.usedArcaneCrystal = player.TPlayer.usedArcaneCrystal ? 1 : 0;
			this.usedGalaxyPearl = player.TPlayer.usedGalaxyPearl ? 1 : 0;
			this.usedGummyWorm = player.TPlayer.usedGummyWorm ? 1 : 0;
			this.usedAmbrosia = player.TPlayer.usedAmbrosia ? 1 : 0;
			this.unlockedSuperCart = player.TPlayer.unlockedSuperCart ? 1 : 0;
			this.enabledSuperCart = player.TPlayer.enabledSuperCart ? 1 : 0;
			this.deathsPVE = player.DeathsPVE;
			this.deathsPVP = player.DeathsPVP;

			Item[] inventory = player.TPlayer.inventory;
			Item[] armor = player.TPlayer.armor;
			Item[] dye = player.TPlayer.dye;
			Item[] miscEqups = player.TPlayer.miscEquips;
			Item[] miscDyes = player.TPlayer.miscDyes;
			Item[] piggy = player.TPlayer.bank.item;
			Item[] safe = player.TPlayer.bank2.item;
			Item[] forge = player.TPlayer.bank3.item;
			Item[] voidVault = player.TPlayer.bank4.item;
			Item trash = player.TPlayer.trashItem;
			Item[] loadout1Armor = player.TPlayer.Loadouts[0].Armor;
			Item[] loadout1Dye = player.TPlayer.Loadouts[0].Dye;
			Item[] loadout2Armor = player.TPlayer.Loadouts[1].Armor;
			Item[] loadout2Dye = player.TPlayer.Loadouts[1].Dye;
			Item[] loadout3Armor = player.TPlayer.Loadouts[2].Armor;
			Item[] loadout3Dye = player.TPlayer.Loadouts[2].Dye;

			for (int i = 0; i < NetItem.MaxInventory; i++)
			{
				if (i < NetItem.InventoryIndex.Item2)
				{
					//0-58
					this.inventory[i] = (NetItem)inventory[i];
				}
				else if (i < NetItem.ArmorIndex.Item2)
				{
					//59-78
					var index = i - NetItem.ArmorIndex.Item1;
					this.inventory[i] = (NetItem)armor[index];
				}
				else if (i < NetItem.DyeIndex.Item2)
				{
					//79-88
					var index = i - NetItem.DyeIndex.Item1;
					this.inventory[i] = (NetItem)dye[index];
				}
				else if (i < NetItem.MiscEquipIndex.Item2)
				{
					//89-93
					var index = i - NetItem.MiscEquipIndex.Item1;
					this.inventory[i] = (NetItem)miscEqups[index];
				}
				else if (i < NetItem.MiscDyeIndex.Item2)
				{
					//93-98
					var index = i - NetItem.MiscDyeIndex.Item1;
					this.inventory[i] = (NetItem)miscDyes[index];
				}
				else if (i < NetItem.PiggyIndex.Item2)
				{
					//98-138
					var index = i - NetItem.PiggyIndex.Item1;
					this.inventory[i] = (NetItem)piggy[index];
				}
				else if (i < NetItem.SafeIndex.Item2)
				{
					//138-178
					var index = i - NetItem.SafeIndex.Item1;
					this.inventory[i] = (NetItem)safe[index];
				}
				else if (i < NetItem.TrashIndex.Item2)
				{
					//179-219
					this.inventory[i] = (NetItem)trash;
				}
				else if (i < NetItem.ForgeIndex.Item2)
				{
					//220
					var index = i - NetItem.ForgeIndex.Item1;
					this.inventory[i] = (NetItem)forge[index];
				}
				else if(i < NetItem.VoidIndex.Item2)
				{
					//220
					var index = i - NetItem.VoidIndex.Item1;
					this.inventory[i] = (NetItem)voidVault[index];
				}
				else if(i < NetItem.Loadout1Armor.Item2)
				{
					var index = i - NetItem.Loadout1Armor.Item1;
					this.inventory[i] = (NetItem)loadout1Armor[index];
				}
				else if(i < NetItem.Loadout1Dye.Item2)
				{
					var index = i - NetItem.Loadout1Dye.Item1;
					this.inventory[i] = (NetItem)loadout1Dye[index];
				}
				else if(i < NetItem.Loadout2Armor.Item2)
				{
					var index = i - NetItem.Loadout2Armor.Item1;
					this.inventory[i] = (NetItem)loadout2Armor[index];
				}
				else if(i < NetItem.Loadout2Dye.Item2)
				{
					var index = i - NetItem.Loadout2Dye.Item1;
					this.inventory[i] = (NetItem)loadout2Dye[index];
				}
				else if(i < NetItem.Loadout3Armor.Item2)
				{
					var index = i - NetItem.Loadout3Armor.Item1;
					this.inventory[i] = (NetItem)loadout3Armor[index];
				}
				else if(i < NetItem.Loadout3Dye.Item2)
				{
					var index = i - NetItem.Loadout3Dye.Item1;
					this.inventory[i] = (NetItem)loadout3Dye[index];
				}
			}
		}

		/// <summary>
		/// Restores a player's character to the state stored in the database
		/// </summary>
		/// <param name="player"></param>
		public void RestoreCharacter(TSPlayer player)
		{
			var server = player.GetCurrentServer();
			var tplayer = player.TPlayer;

            // Start ignoring SSC-related packets! This is critical so that we don't send or receive dirty data!
            player.IgnoreSSCPackets = true;

			try
			{

			tplayer.statLife = this.health;
			tplayer.statLifeMax = this.maxHealth;
			tplayer.statMana = this.maxMana;
			tplayer.statManaMax = this.maxMana;
			tplayer.SpawnX = this.spawnX;
			tplayer.SpawnY = this.spawnY;
			tplayer.hairDye = this.hairDye;
			tplayer.anglerQuestsFinished = this.questsCompleted;
			tplayer.UsingBiomeTorches = this.usingBiomeTorches == 1;
			tplayer.happyFunTorchTime = this.happyFunTorchTime == 1;
			tplayer.unlockedBiomeTorches = this.unlockedBiomeTorches == 1;
			tplayer.CurrentLoadoutIndex = this.currentLoadoutIndex;
			tplayer.ateArtisanBread = this.ateArtisanBread == 1;
			tplayer.usedAegisCrystal = this.usedAegisCrystal == 1;
			tplayer.usedAegisFruit = this.usedAegisFruit == 1;
			tplayer.usedArcaneCrystal = this.usedArcaneCrystal == 1;
			tplayer.usedGalaxyPearl = this.usedGalaxyPearl == 1;
			tplayer.usedGummyWorm = this.usedGummyWorm == 1;
			tplayer.usedAmbrosia = this.usedAmbrosia == 1;
			tplayer.unlockedSuperCart = this.unlockedSuperCart == 1;
			tplayer.enabledSuperCart = this.enabledSuperCart == 1;
			player.sscDeathsPVE = this.deathsPVE;
			player.sscDeathsPVP = this.deathsPVP;
			tplayer.numberOfDeathsPVE = this.deathsPVE;
			tplayer.numberOfDeathsPVP = this.deathsPVP;

			if (extraSlot != null)
				tplayer.extraAccessory = extraSlot.Value == 1 ? true : false;
			if (this.skinVariant != null)
				tplayer.skinVariant = this.skinVariant.Value;
			if (this.hair != null)
				tplayer.hair = this.hair.Value;
			if (this.hairColor != null)
				tplayer.hairColor = this.hairColor.Value;
			if (this.pantsColor != null)
				tplayer.pantsColor = this.pantsColor.Value;
			if (this.shirtColor != null)
				tplayer.shirtColor = this.shirtColor.Value;
			if (this.underShirtColor != null)
				tplayer.underShirtColor = this.underShirtColor.Value;
			if (this.shoeColor != null)
				tplayer.shoeColor = this.shoeColor.Value;
			if (this.skinColor != null)
				tplayer.skinColor = this.skinColor.Value;
			if (this.eyeColor != null)
				tplayer.eyeColor = this.eyeColor.Value;

			if (this.hideVisuals != null)
				tplayer.hideVisibleAccessory = this.hideVisuals;
			else
				tplayer.hideVisibleAccessory = new bool[tplayer.hideVisibleAccessory.Length];

			for (int i = 0; i < NetItem.MaxInventory; i++)
			{
				if (i < NetItem.InventoryIndex.Item2)
				{
					//0-58
					tplayer.inventory[i].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.inventory[i].type != 0)
					{
						tplayer.inventory[i].stack = this.inventory[i].Stack;
						tplayer.inventory[i].prefix = this.inventory[i].PrefixId;
						tplayer.inventory[i].favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.ArmorIndex.Item2)
				{
					//59-78
					var index = i - NetItem.ArmorIndex.Item1;
					tplayer.armor[index].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.armor[index].type != 0)
					{
						tplayer.armor[index].stack = this.inventory[i].Stack;
						tplayer.armor[index].prefix = (byte)this.inventory[i].PrefixId;
						tplayer.armor[index].favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.DyeIndex.Item2)
				{
					//79-88
					var index = i - NetItem.DyeIndex.Item1;
					tplayer.dye[index].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.dye[index].type != 0)
					{
						tplayer.dye[index].stack = this.inventory[i].Stack;
						tplayer.dye[index].prefix = (byte)this.inventory[i].PrefixId;
						tplayer.dye[index].favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.MiscEquipIndex.Item2)
				{
					//89-93
					var index = i - NetItem.MiscEquipIndex.Item1;
					tplayer.miscEquips[index].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.miscEquips[index].type != 0)
					{
						tplayer.miscEquips[index].stack = this.inventory[i].Stack;
						tplayer.miscEquips[index].prefix = (byte)this.inventory[i].PrefixId;
						tplayer.miscEquips[index].favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.MiscDyeIndex.Item2)
				{
					//93-98
					var index = i - NetItem.MiscDyeIndex.Item1;
					tplayer.miscDyes[index].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.miscDyes[index].type != 0)
					{
						tplayer.miscDyes[index].stack = this.inventory[i].Stack;
						tplayer.miscDyes[index].prefix = (byte)this.inventory[i].PrefixId;
						tplayer.miscDyes[index].favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.PiggyIndex.Item2)
				{
					//98-138
					var index = i - NetItem.PiggyIndex.Item1;
					tplayer.bank.item[index].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.bank.item[index].type != 0)
					{
						tplayer.bank.item[index].stack = this.inventory[i].Stack;
						tplayer.bank.item[index].prefix = (byte)this.inventory[i].PrefixId;
						tplayer.bank.item[index].favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.SafeIndex.Item2)
				{
					//138-178
					var index = i - NetItem.SafeIndex.Item1;
					tplayer.bank2.item[index].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.bank2.item[index].type != 0)
					{
						tplayer.bank2.item[index].stack = this.inventory[i].Stack;
						tplayer.bank2.item[index].prefix = (byte)this.inventory[i].PrefixId;
						tplayer.bank2.item[index].favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.TrashIndex.Item2)
				{
					//179-219
					var index = i - NetItem.TrashIndex.Item1;
					tplayer.trashItem.netDefaults(server, this.inventory[i].NetId);

					if (tplayer.trashItem.type != 0)
					{
						tplayer.trashItem.stack = this.inventory[i].Stack;
						tplayer.trashItem.prefix = (byte)this.inventory[i].PrefixId;
						tplayer.trashItem.favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.ForgeIndex.Item2)
				{
					//220
					var index = i - NetItem.ForgeIndex.Item1;
					tplayer.bank3.item[index].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.bank3.item[index].type != 0)
					{
						tplayer.bank3.item[index].stack = this.inventory[i].Stack;
						tplayer.bank3.item[index].Prefix(server, (byte)this.inventory[i].PrefixId);
						tplayer.bank3.item[index].favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.VoidIndex.Item2)
				{
					//260
					var index = i - NetItem.VoidIndex.Item1;
					tplayer.bank4.item[index].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.bank4.item[index].type != 0)
					{
						tplayer.bank4.item[index].stack = this.inventory[i].Stack;
						tplayer.bank4.item[index].Prefix(server, (byte)this.inventory[i].PrefixId);
						tplayer.bank4.item[index].favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.Loadout1Armor.Item2)
				{
					var index = i - NetItem.Loadout1Armor.Item1;
					tplayer.Loadouts[0].Armor[index].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.Loadouts[0].Armor[index].type != 0)
					{
						tplayer.Loadouts[0].Armor[index].stack = this.inventory[i].Stack;
						tplayer.Loadouts[0].Armor[index].Prefix(server, (byte)this.inventory[i].PrefixId);
						tplayer.Loadouts[0].Armor[index].favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.Loadout1Dye.Item2)
				{
					var index = i - NetItem.Loadout1Dye.Item1;
					tplayer.Loadouts[0].Dye[index].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.Loadouts[0].Dye[index].type != 0)
					{
						tplayer.Loadouts[0].Dye[index].stack = this.inventory[i].Stack;
						tplayer.Loadouts[0].Dye[index].Prefix(server, (byte)this.inventory[i].PrefixId);
						tplayer.Loadouts[0].Dye[index].favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.Loadout2Armor.Item2)
				{
					var index = i - NetItem.Loadout2Armor.Item1;
					tplayer.Loadouts[1].Armor[index].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.Loadouts[1].Armor[index].type != 0)
					{
						tplayer.Loadouts[1].Armor[index].stack = this.inventory[i].Stack;
						tplayer.Loadouts[1].Armor[index].Prefix(server, (byte)this.inventory[i].PrefixId);
						tplayer.Loadouts[1].Armor[index].favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.Loadout2Dye.Item2)
				{
					var index = i - NetItem.Loadout2Dye.Item1;
					tplayer.Loadouts[1].Dye[index].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.Loadouts[1].Dye[index].type != 0)
					{
						tplayer.Loadouts[1].Dye[index].stack = this.inventory[i].Stack;
						tplayer.Loadouts[1].Dye[index].Prefix(server, (byte)this.inventory[i].PrefixId);
						tplayer.Loadouts[1].Dye[index].favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.Loadout3Armor.Item2)
				{
					var index = i - NetItem.Loadout3Armor.Item1;
					tplayer.Loadouts[2].Armor[index].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.Loadouts[2].Armor[index].type != 0)
					{
						tplayer.Loadouts[2].Armor[index].stack = this.inventory[i].Stack;
						tplayer.Loadouts[2].Armor[index].Prefix(server, (byte)this.inventory[i].PrefixId);
						tplayer.Loadouts[2].Armor[index].favorited = this.inventory[i].Favorited;
					}
				}
				else if (i < NetItem.Loadout3Dye.Item2)
				{
					var index = i - NetItem.Loadout3Dye.Item1;
					tplayer.Loadouts[2].Dye[index].netDefaults(server, this.inventory[i].NetId);

					if (tplayer.Loadouts[2].Dye[index].type != 0)
					{
						tplayer.Loadouts[2].Dye[index].stack = this.inventory[i].Stack;
						tplayer.Loadouts[2].Dye[index].Prefix(server, (byte)this.inventory[i].PrefixId);
						tplayer.Loadouts[2].Dye[index].favorited = this.inventory[i].Favorited;
					}
				}
			}

			// Just like in MessageBuffer when the client receives a ContinueConnecting, let's sync the CurrentLoadoutIndex _before_ any of
			// the items.
			// This is sent to everyone BUT this player, and then ONLY this player. When using UUID login, it is too soon for the server to
			// broadcast packets to this client.
			server.NetMessage.SendData(MessageID.SyncLoadout, remoteClient: player.Index, number: player.Index, number2: tplayer.CurrentLoadoutIndex);
			server.NetMessage.SendData(MessageID.SyncLoadout, ignoreClient: player.Index, number: player.Index, number2: tplayer.CurrentLoadoutIndex);

			for (int k = 0; k < NetItem.InventorySlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Inventory0 + k);
			}
			for (int k = 0; k < NetItem.ArmorSlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Armor0 + k);
			}
			for (int k = 0; k < NetItem.DyeSlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Dye0 + k);
			}
			for (int k = 0; k < NetItem.MiscEquipSlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Misc0 + k);
			}
			for (int k = 0; k < NetItem.MiscDyeSlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.MiscDye0 + k);
			}
			for (int k = 0; k < NetItem.PiggySlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Bank1_0 + k);
			}
			for (int k = 0; k < NetItem.SafeSlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Bank2_0 + k);
			}
			server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.TrashItem);
			for (int k = 0; k < NetItem.ForgeSlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Bank3_0 + k);
			}
			for (int k = 0; k < NetItem.VoidSlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Bank4_0 + k);
			}
			for (int k = 0; k < NetItem.LoadoutArmorSlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Loadout1_Armor_0 + k);
			}
			for (int k = 0; k < NetItem.LoadoutDyeSlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Loadout1_Dye_0 + k);
			}
			for (int k = 0; k < NetItem.LoadoutArmorSlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Loadout2_Armor_0 + k);
			}
			for (int k = 0; k < NetItem.LoadoutDyeSlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Loadout2_Dye_0 + k);
			}
			for (int k = 0; k < NetItem.LoadoutArmorSlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Loadout3_Armor_0 + k);
			}
			for (int k = 0; k < NetItem.LoadoutDyeSlots; k++)
			{
				server.NetMessage.SendData(5, -1, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Loadout3_Dye_0 + k);
			}


			server.NetMessage.SendData(4, -1, -1, NetworkText.FromLiteral(player.Name), player.Index, 0f, 0f, 0f, 0);
			server.NetMessage.SendData(42, -1, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);
			server.NetMessage.SendData(16, -1, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);

			for (int k = 0; k < NetItem.InventorySlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Inventory0 + k);
			}
			for (int k = 0; k < NetItem.ArmorSlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Armor0 + k);
			}
			for (int k = 0; k < NetItem.DyeSlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Dye0 + k);
			}
			for (int k = 0; k < NetItem.MiscEquipSlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Misc0 + k);
			}
			for (int k = 0; k < NetItem.MiscDyeSlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.MiscDye0 + k);
			}
			for (int k = 0; k < NetItem.PiggySlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Bank1_0 + k);
			}
			for (int k = 0; k < NetItem.SafeSlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Bank2_0 + k);
			}
			server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.TrashItem);
			for (int k = 0; k < NetItem.ForgeSlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Bank3_0 + k);
			}
			for (int k = 0; k < NetItem.VoidSlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Bank4_0 + k);
			}
			for (int k = 0; k < NetItem.LoadoutArmorSlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Loadout1_Armor_0 + k);
			}
			for (int k = 0; k < NetItem.LoadoutDyeSlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Loadout1_Dye_0 + k);
			}
			for (int k = 0; k < NetItem.LoadoutArmorSlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Loadout2_Armor_0 + k);
			}
			for (int k = 0; k < NetItem.LoadoutDyeSlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Loadout2_Dye_0 + k);
			}
			for (int k = 0; k < NetItem.LoadoutArmorSlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Loadout3_Armor_0 + k);
			}
			for (int k = 0; k < NetItem.LoadoutDyeSlots; k++)
			{
				server.NetMessage.SendData(5, player.Index, -1, NetworkText.Empty, player.Index, PlayerItemSlotID.Loadout3_Dye_0 + k);
			}



			server.NetMessage.SendData(4, player.Index, -1, NetworkText.FromLiteral(player.Name), player.Index, 0f, 0f, 0f, 0);
			server.NetMessage.SendData(42, player.Index, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);
			server.NetMessage.SendData(16, player.Index, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);

			for (int k = 0; k < Player.maxBuffs; k++)
			{
				tplayer.buffType[k] = 0;
			}

			/*
			 * The following packets are sent twice because the server will not send a packet to a client
			 * if they have not spawned yet if the remoteclient is -1
			 * This is for when players login via uuid or serverpassword instead of via
			 * the login command.
			 */
			server.NetMessage.SendData(50, -1, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);
			server.NetMessage.SendData(50, player.Index, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);

			server.NetMessage.SendData(76, player.Index, -1, NetworkText.Empty, player.Index);
			server.NetMessage.SendData(76, -1, -1, NetworkText.Empty, player.Index);

			server.NetMessage.SendData(39, player.Index, -1, NetworkText.Empty, 400);

			if (server.Main.GameMode == GameModeID.Creative)
			{
				var sacrificedItems = TShock.ResearchDatastore.GetSacrificedItems(server.Main.worldID);
				for(int i = 0; i < ItemID.Count; i++)
				{
					var amount = 0;
					if (sacrificedItems.ContainsKey(i))
					{
						amount = sacrificedItems[i];
					}

					var response = NetCreativeUnlocksPlayerReportModule.SerializeSacrificeRequest(server, player.Index, i, amount);
					server.NetManager.SendToClient(response, player.Index);
				}
			}
			}
			catch (Exception ex)
			{
				TShock.Log.Error(GetString($"SSC restore failed for {player.Name}: {ex.Message}"));
				player.Kick(GetString("SSC restore failed. Please rejoin."));
			}
			finally
			{
				player.IgnoreSSCPackets = false;
			}
		}
	}
}
