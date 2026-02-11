/*
TShock, a server mod for Terraria
Copyright (C) 2011-2018 Pryaxis & TShock Contributors

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
using Terraria.ID;
using TShockAPI.DB;
using Terraria;
using TShockAPI.Localization;
using LinqToDB.Data;
using UnifierTSL;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;
using TrProtocol.NetPackets;
using TrProtocol.Models;

namespace TShockAPI
{
	/// <summary>The TShock item ban subsystem. It handles keeping things out of people's inventories.</summary>
	public sealed class ItemBans
	{

		/// <summary>The database connection layer to for the item ban subsystem.</summary>
		public ItemManager DataModel;

		/// <summary>The last time the second update process was run. Used to throttle task execution.</summary>
		private DateTime LastTimelyRun = DateTime.UtcNow;

		/// <summary>A reference to the TShock plugin so we can register events.</summary>
		private TShock Plugin;

		/// <summary>Creates an ItemBan system given a plugin to register events to and a database.</summary>
		/// <param name="plugin">The executing plugin.</param>
		/// <param name="database">The database the item ban information is stored in.</param>
		/// <returns>A new item ban system.</returns>
		internal ItemBans(TShock plugin, DataConnection database)
		{
			DataModel = new ItemManager(database);
			Plugin = plugin;

			UnifierApi.EventHub.Game.PreUpdate.Register(OnGameUpdate, HandlerPriority.Low);
			NetPacketHandler.Register<PlayerControls>(OnPlayerUpdate, HandlerPriority.High);
			NetPacketHandler.Register<SyncChestItem>(OnChestItemChange, HandlerPriority.Normal);
			NetPacketHandler.Register<TileChange>(OnTileEdit, HandlerPriority.Normal);
		}

        private void OnGameUpdate(ref ReadonlyNoCancelEventArgs<ServerEvent> args) {
            if ((DateTime.UtcNow - LastTimelyRun).TotalSeconds >= 1) {
                OnSecondlyUpdate(ref args);
            }
        }

		/// <summary>Called by OnGameUpdate once per second to execute tasks regularly but not too often.</summary>
		/// <param name="args">The standard event arguments.</param>
		internal void OnSecondlyUpdate(ref ReadonlyNoCancelEventArgs<ServerEvent> args)
		{
			foreach (TSPlayer player in TShock.Players)
			{
				if (player == null || !player.Active)
				{
					continue;
				}

				var disableFlags = player.GetCurrentSettings().DisableSecondUpdateLogs ? DisableFlags.WriteToConsole : DisableFlags.WriteToLogAndConsole;
				var server = player.GetCurrentServer();

                // Untaint now, re-taint if they fail the check.
                UnTaint(player);

				// No matter the player type, we do a check when a player is holding an item that's banned.
				if (DataModel.ItemIsBanned(EnglishLanguage.GetItemNameById(player.TPlayer.inventory[player.TPlayer.selectedItem].type), player))
				{
					string itemName = player.TPlayer.inventory[player.TPlayer.selectedItem].Name;
					player.Disable(GetString($"holding banned item: {itemName}"), disableFlags);
					SendCorrectiveMessage(player, itemName);
				}

				// If SSC isn't enabled OR if SSC is enabled and the player is logged in
				// In a case like this, we do the full check too.
				if (!server.Main.ServerSideCharacter || (server.Main.ServerSideCharacter && player.IsLoggedIn))
				{
					// The Terraria inventory is composed of a multicultural set of arrays
					// with various different contents and beliefs

					// Armor ban checks
					foreach (Item item in player.TPlayer.armor)
					{
						if (DataModel.ItemIsBanned(EnglishLanguage.GetItemNameById(item.type), player))
						{
							Taint(player);
							SendCorrectiveMessage(player, item.Name);
						}
					}

					// Dye ban checks
					foreach (Item item in player.TPlayer.dye)
					{
						if (DataModel.ItemIsBanned(EnglishLanguage.GetItemNameById(item.type), player))
						{
							Taint(player);
							SendCorrectiveMessage(player, item.Name);
						}
					}

					// Misc equip ban checks
					foreach (Item item in player.TPlayer.miscEquips)
					{
						if (DataModel.ItemIsBanned(EnglishLanguage.GetItemNameById(item.type), player))
						{
							Taint(player);
							SendCorrectiveMessage(player, item.Name);
						}
					}

					// Misc dye ban checks
					foreach (Item item in player.TPlayer.miscDyes)
					{
						if (DataModel.ItemIsBanned(EnglishLanguage.GetItemNameById(item.type), player))
						{
							Taint(player);
							SendCorrectiveMessage(player, item.Name);
						}
					}
				}
			}

			// Set the update time to now, so that we know when to execute next.
			// We do this at the end so that the task can't re-execute faster than we expected.
			// (If we did this at the start of the method, the method execution would count towards the timer.)
			LastTimelyRun = DateTime.UtcNow;
		}

        private void OnPlayerUpdate(ref RecievePacketEvent<PlayerControls> args) {
            var player = TShock.Players[args.RecieveFrom.ID];
            var server = args.LocalReciever.Server;
            var disableFlags = player.GetCurrentSettings().DisableSecondUpdateLogs ? DisableFlags.WriteToConsole : DisableFlags.WriteToLogAndConsole;

			string itemName = player.TPlayer.inventory[args.Packet.SelectedItem].Name;

            if (DataModel.ItemIsBanned(EnglishLanguage.GetItemNameById(player.TPlayer.inventory[args.Packet.SelectedItem].type), player)) {
                player.TPlayer.controlUseItem = false;
				args.Packet.PlayerControlData.IsUsingItem = false;
                player.Disable(GetString($"holding banned item: {itemName}"), disableFlags);

                SendCorrectiveMessage(player, itemName);

                player.TPlayer.Update(server, player.TPlayer.whoAmI);
				args.StopPropagation = true;
				args.HandleMode = PacketHandleMode.Overwrite;
                return;
            }
            return;
        }

        private void OnChestItemChange(ref RecievePacketEvent<SyncChestItem> args) {
            var player = TShock.Players[args.RecieveFrom.ID];
			var server = args.LocalReciever.Server;

            Item item = new Item();
            item.netDefaults(server, args.Packet.ItemType);


            if (DataModel.ItemIsBanned(EnglishLanguage.GetItemNameById(item.type), player)) {
                SendCorrectiveMessage(player, item.Name);
				args.HandleMode = PacketHandleMode.Cancel;
				args.StopPropagation = true;
                return;
            }
        }

        private void OnTileEdit(ref RecievePacketEvent<TileChange> args) {
			var action = args.Packet.ChangeType;

            if (action == TileEditAction.PlaceTile || action == TileEditAction.PlaceWall) {
				var player = TShock.Players[args.RecieveFrom.ID];
                if (player.TPlayer.autoActuator && DataModel.ItemIsBanned("Actuator", player)) {
                    player.SendTileSquareCentered(args.Packet.Position.X, args.Packet.Position.Y, 1);
                    player.SendErrorMessage(GetString("You do not have permission to place actuators."));
					args.HandleMode = PacketHandleMode.Cancel;
                    return;
                }

                if (DataModel.ItemIsBanned(EnglishLanguage.GetItemNameById(player.SelectedItem.type), player)) {
                    player.SendTileSquareCentered(args.Packet.Position.X, args.Packet.Position.Y, 4);
                    args.HandleMode = PacketHandleMode.Cancel;
                    return;
                }
            }
        }

		private void UnTaint(TSPlayer player)
		{
			player.IsDisabledForBannedWearable = false;
		}

		private void Taint(TSPlayer player)
		{
			// Arbitrarily does things to the player
			player.SetBuff(BuffID.Frozen, 330, true);
			player.SetBuff(BuffID.Stoned, 330, true);
			player.SetBuff(BuffID.Webbed, 330, true);

			// Marks them as a target for future disables
			player.IsDisabledForBannedWearable = true;
		}

		private void SendCorrectiveMessage(TSPlayer player, string itemName)
		{
			player.SendErrorMessage(GetString("{0} is banned! Remove it!", itemName));
		}
	}
}
