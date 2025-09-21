using Microsoft.Xna.Framework;
using NuGet.Protocol.Plugins;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using TrProtocol.Models;
using TrProtocol.NetPackets;
using TShockAPI.Extension;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Servers;

namespace TShockAPI
{
    public static class GetDataHandlers
    {
        public static void InitGetDataHandler() {
            NetPacketHandler.Register<SyncPlayer>(HandlePlayerInfo, HandlerPriority.Low);
            NetPacketHandler.Register<SyncEquipment>(HandlePlayerSlot, HandlerPriority.Low);
            NetPacketHandler.Register<RequestWorldInfo>(HandleConnecting, HandlerPriority.Low);
            NetPacketHandler.Register<RequestTileData>(HandleGetSection, HandlerPriority.Low);
            NetPacketHandler.Register<SpawnPlayer>(HandleSpawn, HandlerPriority.Low);
            NetPacketHandler.Register<PlayerControls>(HandlePlayerUpdate_NullCheck, HandlerPriority.High);
            NetPacketHandler.Register<PlayerControls>(HandlePlayerUpdate, HandlerPriority.Low);
            NetPacketHandler.Register<PlayerHealth>(HandlePlayerHp, HandlerPriority.Low);
            NetPacketHandler.Register<ChangeDoor>(HandleDoorUse, HandlerPriority.Low);
            NetPacketHandler.Register<ItemOwner>(HandleItemOwner, HandlerPriority.Low);
            NetPacketHandler.Register<UnusedStrikeNPC>(HandleNpcItemStrike, HandlerPriority.Low);
            NetPacketHandler.Register<SyncProjectile>(HandleProjectileNew, HandlerPriority.Low);
            NetPacketHandler.Register<StrikeNPC>(HandleNpcStrike, HandlerPriority.Low);
            NetPacketHandler.Register<KillProjectile>(HandleProjectileKill, HandlerPriority.Low);
            NetPacketHandler.Register<PlayerPvP>(HandleTogglePvp, HandlerPriority.Low);
            NetPacketHandler.Register<SyncChestItem>(HandleChestItem, HandlerPriority.Low);
            NetPacketHandler.Register<SyncPlayerChest>(HandleChestActive, HandlerPriority.Low);
            NetPacketHandler.Register<PlayerZone>(HandlePlayerZone_NullCheck, HandlerPriority.High);
            NetPacketHandler.Register<SendPassword>(HandlePassword, HandlerPriority.Low);
            NetPacketHandler.Register<PlayerTalkingNPC>(HandleNpcTalk, HandlerPriority.Low);
            NetPacketHandler.Register<PlayerMana>(HandlePlayerMana, HandlerPriority.Low);
            NetPacketHandler.Register<PlayerTeam>(HandlePlayerTeam, HandlerPriority.Low);
            NetPacketHandler.Register<RequestReadSign>(HandleSignRead, HandlerPriority.Low);
            NetPacketHandler.Register<ReadSign>(HandleSign, HandlerPriority.Low);
            NetPacketHandler.Register<PlayerBuffs>(HandlePlayerBuffList, HandlerPriority.Low);
            NetPacketHandler.Register<Assorted1>(HandleSpecial, HandlerPriority.Low);
            NetPacketHandler.Register<NPCHome>(HandleUpdateNPCHome, HandlerPriority.Low);
            NetPacketHandler.Register<SpawnBoss>(HandleSpawnBoss, HandlerPriority.Low);
            NetPacketHandler.Register<PaintTile>(HandlePaintTile, HandlerPriority.Low);
            NetPacketHandler.Register<PaintWall>(HandlePaintWall, HandlerPriority.Low);
            NetPacketHandler.Register<Teleport>(HandleTeleport, HandlerPriority.Low);
            NetPacketHandler.Register<BugCatching>(HandleCatchNpc, HandlerPriority.Low);
            NetPacketHandler.Register<TeleportationPotion>(HandleTeleportationPotion, HandlerPriority.Low);
            NetPacketHandler.Register<AnglerQuestFinished>(HandleCompleteAnglerQuest, HandlerPriority.Low);
            NetPacketHandler.Register<AnglerQuestCountSync>(HandleNumberOfAnglerQuestsCompleted, HandlerPriority.Low);
            NetPacketHandler.Register<TileEntityPlacement>(HandlePlaceTileEntity, HandlerPriority.Low);
            NetPacketHandler.Register<SyncExtraValue>(HandleSyncExtraValue, HandlerPriority.Low);
            NetPacketHandler.Register<MurderSomeoneElsesProjectile>(HandleKillPortal, HandlerPriority.Low);
            NetPacketHandler.Register<ToggleParty>(HandleToggleParty, HandlerPriority.Low);
            NetPacketHandler.Register<TeleportNPCThroughPortal>(HandleNpcTeleportPortal, HandlerPriority.Low);
            NetPacketHandler.Register<CrystalInvasionStart>(HandleOldOnesArmy, HandlerPriority.Low);
            NetPacketHandler.Register<PlayerDeathV2>(HandlePlayerKillMeV2, HandlerPriority.Low);
            NetPacketHandler.Register<SyncCavernMonsterType>(HandleSyncCavernMonsterType, HandlerPriority.Low);
            NetPacketHandler.Register<SyncLoadout>(HandleSyncLoadout, HandlerPriority.Low);
        }

        private static void HandlePlayerInfo(ref RecievePacketEvent<SyncPlayer> args) {

            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            if (args.HandleMode is PacketHandleMode.Cancel) {
                server.Log.Debug(GetString("GetDataHandlers / HandlePlayerInfo rejected plugin phase {0}", args.Packet.Name));
                tsPlayer.Kick(GetString("A plugin on this server stopped your login."), true, true);
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }

            // 0-3 male; 4-7 female
            int skinVariant = args.Packet.SkinVariant;
            var hair = args.Packet.Hair;
            string name = args.Packet.Name;
            byte hairDye = args.Packet.HairDye;

            BitsByte hideVisual = args.Packet.Bit1;
            BitsByte hideVisual2 = args.Packet.Bit2;
            BitsByte hideMisc = args.Packet.HideMisc;

            Color hairColor = args.Packet.HairColor;
            Color skinColor = args.Packet.SkinColor;
            Color eyeColor = args.Packet.EyeColor;
            Color shirtColor = args.Packet.ShirtColor;
            Color underShirtColor = args.Packet.UnderShirtColor;
            Color pantsColor = args.Packet.PantsColor;
            Color shoeColor = args.Packet.ShoeColor;

            BitsByte extra = args.Packet.Bit3;
            byte difficulty = 0;
            if (extra[0]) {
                difficulty = 1;
            }
            else if (extra[1]) {
                difficulty = 2;
            }
            else if (extra[3]) {
                difficulty = 3;
            }
            bool extraSlot = extra[2];
            BitsByte torchFlags = args.Packet.Bit4;
            bool usingBiomeTorches = torchFlags[0];
            bool happyFunTorchTime = torchFlags[1];
            bool unlockedBiomeTorches = torchFlags[2];
            bool unlockedSuperCart = torchFlags[3];
            bool enabledSuperCart = torchFlags[4];
            BitsByte bitsByte10 = args.Packet.Bit5;
            bool usedAegisCrystal = bitsByte10[0];
            bool usedAegisFruit = bitsByte10[1];
            bool usedArcaneCrystal = bitsByte10[2];
            bool usedGalaxyPearl = bitsByte10[3];
            bool usedGummyWorm = bitsByte10[4];
            bool usedAmbrosia = bitsByte10[5];
            bool ateArtisanBread = bitsByte10[6];

            if (name.Trim().Length == 0) {
                server.Log.Debug(GetString("GetDataHandlers / HandlePlayerInfo rejected name length 0"));
                tsPlayer.Kick(GetString("You have been Bounced."), true, true);
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }

            if (name.Trim().StartsWith("tsi:") || name.Trim().StartsWith("tsn:")) {
                server.Log.Debug(GetString("GetDataHandlers / rejecting player for name prefix starting with tsi: or tsn:."));
                tsPlayer.Kick(GetString("Illegal name: prefixes tsi: and tsn: are forbidden."), true, true);
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }

            if (tsPlayer.ReceivedInfo) {
                // Since Terraria 1.2.3 these character properties can change ingame.
                tsPlayer.TPlayer.hair = hair;
                tsPlayer.TPlayer.hairColor = hairColor;
                tsPlayer.TPlayer.hairDye = hairDye;
                tsPlayer.TPlayer.skinVariant = skinVariant;
                tsPlayer.TPlayer.skinColor = skinColor;
                tsPlayer.TPlayer.eyeColor = eyeColor;
                tsPlayer.TPlayer.pantsColor = pantsColor;
                tsPlayer.TPlayer.shirtColor = shirtColor;
                tsPlayer.TPlayer.underShirtColor = underShirtColor;
                tsPlayer.TPlayer.shoeColor = shoeColor;
                //@Olink: If you need to change bool[10], please make sure you also update the for loops below to account for it.
                //There are two arrays from terraria that we only have a single array for.  You will need to make sure that you are looking
                //at the correct terraria array (hideVisual or hideVisual2).
                tsPlayer.TPlayer.hideVisibleAccessory = new bool[10];
                for (int i = 0; i < 8; i++)
                    tsPlayer.TPlayer.hideVisibleAccessory[i] = hideVisual[i];
                for (int i = 0; i < 2; i++)
                    tsPlayer.TPlayer.hideVisibleAccessory[i + 8] = hideVisual2[i];
                tsPlayer.TPlayer.hideMisc = hideMisc;
                tsPlayer.TPlayer.extraAccessory = extraSlot;
                tsPlayer.TPlayer.UsingBiomeTorches = usingBiomeTorches;
                tsPlayer.TPlayer.happyFunTorchTime = happyFunTorchTime;
                tsPlayer.TPlayer.unlockedBiomeTorches = unlockedBiomeTorches;
                tsPlayer.TPlayer.ateArtisanBread = ateArtisanBread;
                tsPlayer.TPlayer.usedAegisCrystal = usedAegisCrystal;
                tsPlayer.TPlayer.usedAegisFruit = usedAegisFruit;
                tsPlayer.TPlayer.usedArcaneCrystal = usedArcaneCrystal;
                tsPlayer.TPlayer.usedGalaxyPearl = usedGalaxyPearl;
                tsPlayer.TPlayer.usedGummyWorm = usedGummyWorm;
                tsPlayer.TPlayer.usedAmbrosia = usedAmbrosia;
                tsPlayer.TPlayer.unlockedSuperCart = unlockedSuperCart;
                tsPlayer.TPlayer.enabledSuperCart = enabledSuperCart;

                server.NetMessage.SendData((int)PacketTypes.PlayerInfo, -1, tsPlayer.Index, NetworkText.FromLiteral(tsPlayer.Name), tsPlayer.Index);
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }
            if (setting.SoftcoreOnly && difficulty != 0) {
                server.Log.Debug(GetString("GetDataHandlers / HandlePlayerInfo rejected softcore required"));
                tsPlayer.Kick(GetString("You need to join with a softcore player."), true, true);
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }
            if (setting.MediumcoreOnly && difficulty < 1) {
                server.Log.Debug(GetString("GetDataHandlers / HandlePlayerInfo rejected mediumcore required"));
                tsPlayer.Kick(GetString("You need to join with a mediumcore player or higher."), true, true);
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }
            if (setting.HardcoreOnly && difficulty < 2) {
                server.Log.Debug(GetString("GetDataHandlers / HandlePlayerInfo rejected hardcore required"));
                tsPlayer.Kick(GetString("You need to join with a hardcore player."), true, true);
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }
            tsPlayer.Difficulty = difficulty;
            tsPlayer.TPlayer.name = name;
            tsPlayer.ReceivedInfo = true;
        }

        private static void HandlePlayerSlot(ref RecievePacketEvent<SyncEquipment> args) {

            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            byte plr = args.Packet.PlayerSlot;
            short slot = args.Packet.ItemSlot;
            short stack = args.Packet.Stack;
            byte prefix = args.Packet.Prefix;
            short type = args.Packet.ItemType;

            // Players send a slot update packet for each inventory slot right after they've joined.
            bool bypassTrashCanCheck = false;
            if (plr == tsPlayer.Index && !tsPlayer.HasSentInventory && slot == NetItem.MaxInventory) {
                tsPlayer.HasSentInventory = true;
                bypassTrashCanCheck = true;
            }

            if (/*OnPlayerSlot(tsPlayer, args.Data, plr, slot, stack, prefix, type) ||*/ plr != tsPlayer.Index || slot < 0 ||
                slot > NetItem.MaxInventory) {
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }

            if (tsPlayer.IgnoreSSCPackets) {
                server.Log.Debug(GetString("GetDataHandlers / HandlePlayerSlot rejected ignore ssc packets"));
                tsPlayer.SendData(PacketTypes.PlayerSlot, "", tsPlayer.Index, slot, prefix);
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }

            // Garabage? Or will it cause some internal initialization or whatever?
            var item = new Item();
            item.netDefaults(server, type);
            item.Prefix(server, prefix);

            if (tsPlayer.IsLoggedIn) {
                tsPlayer.PlayerData.StoreSlot(slot, type, prefix, stack);
            }
            else if (server.Main.ServerSideCharacter && setting.DisableLoginBeforeJoin && !bypassTrashCanCheck &&
                     tsPlayer.HasSentInventory && !tsPlayer.HasPermission(Permissions.bypassssc)) {
                // The player might have moved an item to their trash can before they performed a single login attempt yet.
                tsPlayer.IsDisabledPendingTrashRemoval = true;
            }

            if (slot == 58) //this is the hand
            {
                item.stack = stack;
                tsPlayer.ItemInHand = item;
            }
        }

        private static void HandleConnecting(ref RecievePacketEvent<RequestWorldInfo> args) {

            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            var account = TShock.UserAccounts.GetUserAccountByName(tsPlayer.Name);
            tsPlayer.DataWhenJoined = new PlayerData(false);
            tsPlayer.DataWhenJoined.CopyCharacter(tsPlayer);
            tsPlayer.PlayerData = new PlayerData(false);
            tsPlayer.PlayerData.CopyCharacter(tsPlayer);

            if (account != null && !setting.DisableUUIDLogin) {
                if (account.UUID == tsPlayer.UUID) {
                    if (tsPlayer.State == (int)ConnectionState.AssigningPlayerSlot)
                        tsPlayer.State = (int)ConnectionState.AwaitingPlayerInfo;

                    server.NetMessage.SendData((int)PacketTypes.WorldInfo, tsPlayer.Index);

                    var group = TShock.Groups.GetGroupByName(account.Group);

                    if (!TShock.Groups.AssertGroupValid(tsPlayer, group, true)) {
                        args.HandleMode = PacketHandleMode.Cancel;
                        args.StopPropagation = true;
                        return;
                    }

                    tsPlayer.PlayerData = TShock.CharacterDB.GetPlayerData(tsPlayer, account.ID);

                    tsPlayer.Group = group;
                    tsPlayer.tempGroup = null;
                    tsPlayer.Account = account;
                    tsPlayer.IsLoggedIn = true;
                    tsPlayer.IsDisabledForSSC = false;

                    if (server.Main.ServerSideCharacter) {
                        if (tsPlayer.HasPermission(Permissions.bypassssc)) {
                            if (tsPlayer.PlayerData.exists && TShock.ServerSideCharacterConfig.Settings.WarnPlayersAboutBypassPermission) {
                                tsPlayer.SendWarningMessage(GetString("Bypass SSC is enabled for your account. SSC data will not be loaded or saved."));
                                server.Log.Info(GetString($"{tsPlayer.Name} has SSC data in the database, but has the tshock.ignore.ssc permission. This means their SSC data is being ignored."));
                                server.Log.Info(GetString("You may wish to consider removing the tshock.ignore.ssc permission or negating it for this player."));
                            }
                            tsPlayer.PlayerData.CopyCharacter(tsPlayer);
                            TShock.CharacterDB.InsertPlayerData(tsPlayer);
                        }
                        tsPlayer.PlayerData.RestoreCharacter(tsPlayer);
                    }
                    tsPlayer.LoginFailsBySsi = false;

                    if (tsPlayer.HasPermission(Permissions.ignorestackhackdetection))
                        tsPlayer.IsDisabledForStackDetection = false;

                    if (tsPlayer.HasPermission(Permissions.usebanneditem))
                        tsPlayer.IsDisabledForBannedWearable = false;

                    tsPlayer.SendSuccessMessage(GetString($"Authenticated as {account.Name} successfully."));
                    server.Log.Info(GetString($"{tsPlayer.Name} authenticated successfully as user {tsPlayer.Name}."));
                    Hooks.PlayerHooks.OnPlayerPostLogin(tsPlayer);
                    args.HandleMode = PacketHandleMode.Cancel;
                }
            }
            else if (account != null && !setting.DisableLoginBeforeJoin) {
                tsPlayer.RequiresPassword = true;
                server.NetMessage.SendData((int)PacketTypes.PasswordRequired, tsPlayer.Index);
                args.HandleMode = PacketHandleMode.Cancel;
            }
            else if (!string.IsNullOrEmpty(setting.ServerPassword)) {
                tsPlayer.RequiresPassword = true;
                server.NetMessage.SendData((int)PacketTypes.PasswordRequired, tsPlayer.Index);
                args.HandleMode = PacketHandleMode.Cancel;
            }

            if (tsPlayer.State == (int)ConnectionState.AssigningPlayerSlot)
                tsPlayer.State = (int)ConnectionState.AwaitingPlayerInfo;

            server.NetMessage.SendData((int)PacketTypes.WorldInfo, tsPlayer.Index);
            args.HandleMode = PacketHandleMode.Cancel;
        }

        private static void HandleGetSection(ref RecievePacketEvent<RequestTileData> args) {

            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            if (Utils.GetActivePlayerCount() + 1 > setting.MaxSlots &&
                !tsPlayer.HasPermission(Permissions.reservedslot)) {
                server.Log.Debug(GetString("GetDataHandlers / HandleGetSection rejected reserve slot"));
                tsPlayer.Kick(setting.ServerFullReason, true, true);
                args.HandleMode = PacketHandleMode.Cancel;
            }

            server.NetMessage.SendData((int)PacketTypes.TimeSet, -1, -1, NetworkText.Empty, server.Main.dayTime ? 1 : 0, (int)server.Main.time, server.Main.sunModY, server.Main.moonModY);
            return;
        }

        private static void HandleSpawn(ref RecievePacketEvent<SpawnPlayer> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            if (tsPlayer.Dead && tsPlayer.RespawnTimer > 0) {
                server.Log.Debug(GetString("GetDataHandlers / HandleSpawn rejected dead player spawn request {0}", tsPlayer.Name));
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }

            short spawnX = args.Packet.Position.X;
            short spawnY = args.Packet.Position.Y;
            int respawnTimer = args.Packet.Timer;
            short numberOfDeathsPVE = args.Packet.DeathsPVE;
            short numberOfDeathsPVP = args.Packet.DeathsPVP;
            PlayerSpawnContext context = args.Packet.Context;

            if (tsPlayer.State >= (int)ConnectionState.RequestingWorldData && !tsPlayer.FinishedHandshake)
                tsPlayer.FinishedHandshake = true; //If the player has requested world data before sending spawn player, they should be at the obvious ClientRequestedWorldData state. Also only set this once to remove redundant updates.

            //if (OnPlayerSpawn(tsPlayer, args.Data, player, spawnX, spawnY, respawnTimer, numberOfDeathsPVE, numberOfDeathsPVP, context))
            //    args.HandleMode = PacketHandleMode.Cancel;

            tsPlayer.Dead = respawnTimer > 0;

            if (server.Main.ServerSideCharacter) {
                // As long as the player has not changed his spawnpoint since initial connection,
                // we should not use the client's spawnpoint value. This is because the spawnpoint 
                // value is not saved on the client when SSC is enabled. Hence, we have to assert 
                // the server-saved spawnpoint value until we can detect that the player has changed 
                // his spawn. Once we detect the spawnpoint changed, the client's spawnpoint value
                // becomes the correct one to use.
                //
                // Note that spawnpoint changes (right-clicking beds) are not broadcasted to the 
                // server. Hence, the only way to detect spawnpoint changes is from the 
                // PlayerSpawn packet.

                // handle initial connection
                if (tsPlayer.State == 3) {
                    // server saved spawnpoint value
                    tsPlayer.initialSpawn = true;
                    tsPlayer.initialServerSpawnX = tsPlayer.TPlayer.SpawnX;
                    tsPlayer.initialServerSpawnY = tsPlayer.TPlayer.SpawnY;

                    // initial client spawn point, do not use this to spawn the player
                    // we only use it to detect if the spawnpoint has changed during this session
                    tsPlayer.initialClientSpawnX = spawnX;
                    tsPlayer.initialClientSpawnY = spawnY;

                    // we first let the game handle completing the connection (state 3 => 10), 
                    // then we will spawn the player at the saved spawnpoint in the next second, 
                    // by reasserting the correct spawnpoint value
                    return;
                }

                // once we detect the client has changed his spawnpoint in the current session, 
                // the client spawnpoint value will be correct for the rest of the session
                if (tsPlayer.spawnSynced || tsPlayer.initialClientSpawnX != spawnX || tsPlayer.initialClientSpawnY != spawnY) {
                    // Player has changed his spawnpoint, client and server TPlayer.Spawn{X,Y} is now synced
                    tsPlayer.spawnSynced = true;
                    return;
                }

                // spawn the player before teleporting
                server.NetMessage.SendData((int)PacketTypes.PlayerSpawn, -1, tsPlayer.Index, null, tsPlayer.Index, (int)PlayerSpawnContext.ReviveFromDeath);

                // the player has not changed his spawnpoint yet, so we assert the server-saved spawnpoint 
                // by teleporting the player instead of letting the game use the client's incorrect spawnpoint.
                server.Log.Debug(GetString("GetDataHandlers / HandleSpawn force ssc teleport for {0} at ({1},{2})", tsPlayer.Name, tsPlayer.TPlayer.SpawnX, tsPlayer.TPlayer.SpawnY));
                tsPlayer.TeleportSpawnpoint();

                tsPlayer.TPlayer.respawnTimer = respawnTimer;
                tsPlayer.TPlayer.numberOfDeathsPVE = numberOfDeathsPVE;
                tsPlayer.TPlayer.numberOfDeathsPVP = numberOfDeathsPVP;
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
            }
            return;
        }

        private static void HandlePlayerUpdate_NullCheck(ref RecievePacketEvent<PlayerControls> args) {
            var tsPlayer = args.GetTSPlayer();
            if (tsPlayer == null || tsPlayer.TPlayer == null) {
                var server = args.LocalReciever.Server;
                server.Log.Debug(GetString("GetDataHandlers / OnPlayerUpdate rejected from null player."));
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }
        }
        private static void HandlePlayerUpdate(ref RecievePacketEvent<PlayerControls> args) {
            if (args.Packet.PlayerMiscData2.CanReturnWithPotionOfReturn) {
                var server = args.LocalReciever.Server;
                var tsPlayer = args.GetTSPlayer();
                server.Log.Debug(GetString("GetDataHandlers / HandlePlayerUpdate home position delta {0}", tsPlayer.Name));
            }

            //if (OnPlayerUpdate(tsPlayer, args.Data, playerID, controls, miscData1, miscData2, miscData3, selectedItem, position, velocity, originalPosition, homePosition))
            //    args.HandleMode = PacketHandleMode.Cancel;
        }

        private static void HandlePlayerHp(ref RecievePacketEvent<PlayerHealth> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            var cur = args.Packet.StatLife;
            var max = args.Packet.StatLifeMax;

            if (/*OnPlayerHP(tsPlayer, args.Data, plr, cur, max) || cur <= 0 || max <= 0 || */tsPlayer.IgnoreSSCPackets) {
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }

            if (max > setting.MaxHP && !tsPlayer.HasPermission(Permissions.ignorehp)) {
                server.Log.Debug(GetString("GetDataHandlers / HandlePlayerHp rejected over max hp {0}", tsPlayer.Name));
                tsPlayer.Disable("Maximum HP beyond limit", DisableFlags.WriteToLogAndConsole);
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }

            if (tsPlayer.IsLoggedIn) {
                tsPlayer.TPlayer.statLife = cur;
                tsPlayer.TPlayer.statLifeMax = max;
                tsPlayer.PlayerData.maxHealth = max;
            }
        }

        private static void HandleDoorUse(ref RecievePacketEvent<ChangeDoor> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            var doorAction = args.Packet.ChangeType;
            short x = args.Packet.Position.X;
            short y = args.Packet.Position.Y;

            //if (OnDoorUse(tsPlayer, args.Data, x, y, direction, doorAction))
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            ushort tileType = server.Main.tile[x, y].type;

            if (x >= server.Main.maxTilesX || y >= server.Main.maxTilesY || x < 0 || y < 0) // Check for out of range
            {
                server.Log.Debug(GetString("GetDataHandlers / HandleDoorUse rejected out of range door {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (Enum.IsDefined(doorAction)) {
                server.Log.Debug(GetString("GetDataHandlers / HandleDoorUse rejected type 0 5 check {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }


            if (tileType != TileID.ClosedDoor && tileType != TileID.OpenDoor
                                              && tileType != TileID.TallGateClosed && tileType != TileID.TallGateOpen
                                              && tileType != TileID.TrapdoorClosed && tileType != TileID.TrapdoorOpen) {
                server.Log.Debug(GetString("GetDataHandlers / HandleDoorUse rejected door gap check {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
        }

        private static void HandleItemOwner(ref RecievePacketEvent<ItemOwner> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            var id = args.Packet.ItemSlot;
            var owner = args.Packet.OtherPlayerSlot;

            if (id < 0 || id > 400) { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            if (id == 400 && owner == 255) {
                tsPlayer.IgnoreSSCPackets = false; 
                args.HandleMode = PacketHandleMode.Cancel; 
                args.StopPropagation = true; 
                return;
            }
        }

        private static void HandleNpcItemStrike(ref RecievePacketEvent<UnusedStrikeNPC> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            // Never sent by vanilla client, ignore this
            server.Log.Debug(GetString("GetDataHandlers / HandleNpcItemStrike surprise packet! Someone tell the TShock team! {0}", tsPlayer.Name));
            args.HandleMode = PacketHandleMode.Cancel; 
            args.StopPropagation = true; 
            return;
        }

        private static void HandleProjectileNew(ref RecievePacketEvent<SyncProjectile> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            short ident = args.Packet.ProjSlot;
            byte owner = args.Packet.PlayerSlot;
            var index = Utils.SearchProjectile(server, ident, owner);

            lock (tsPlayer.RecentlyCreatedProjectiles) {
                if (!tsPlayer.RecentlyCreatedProjectiles.Any(p => p.Index == index)) {
                    tsPlayer.RecentlyCreatedProjectiles.Add(new ProjectileStruct() {
                        Index = index,
                        Type = args.Packet.ProjType,
                        CreatedAt = DateTime.Now
                    });
                }
            }
        }

        private static void HandleNpcStrike(ref RecievePacketEvent<StrikeNPC> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            var id = args.Packet.NPCSlot;
            var dmg = args.Packet.Damage;
            var knockback = args.Packet.Knockback;
            var direction = (byte)(args.Packet.HitDirection - 1);
            var crit = args.Packet;

            //if (OnNPCStrike(tsPlayer, args.Data, id, direction, dmg, knockback, crit))
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            if (server.Main.npc[id].townNPC && !tsPlayer.HasPermission(Permissions.hurttownnpc)) {
                tsPlayer.SendErrorMessage(GetString("You do not have permission to hurt Town NPCs."));
                tsPlayer.SendData(PacketTypes.NpcUpdate, "", id);
                server.Log.Debug(GetString($"GetDataHandlers / HandleNpcStrike rejected npc strike {tsPlayer.Name}"));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (server.Main.npc[id].netID == NPCID.EmpressButterfly) {
                if (!tsPlayer.HasPermission(Permissions.summonboss)) {
                    tsPlayer.SendErrorMessage(GetString("You do not have permission to summon the Empress of Light."));
                    tsPlayer.SendData(PacketTypes.NpcUpdate, "", id);
                    server.Log.Debug(GetString($"GetDataHandlers / HandleNpcStrike rejected EoL summon from {tsPlayer.Name}"));
                    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                }
                else if (!setting.AnonymousBossInvasions) {
                    Utils.Broadcast(server, GetString($"{tsPlayer.Name} summoned the Empress of Light!"), 175, 75, 255);
                }
                else
                    Utils.SendLogs(server, GetString($"{tsPlayer.Name} summoned the Empress of Light!"), Color.PaleVioletRed, tsPlayer);
            }

            if (server.Main.npc[id].netID == NPCID.CultistDevote || server.Main.npc[id].netID == NPCID.CultistArcherBlue) {
                if (!tsPlayer.HasPermission(Permissions.summonboss)) {
                    tsPlayer.SendErrorMessage(GetString("You do not have permission to summon the Lunatic Cultist!"));
                    tsPlayer.SendData(PacketTypes.NpcUpdate, "", id);
                    server.Log.Debug(GetString($"GetDataHandlers / HandleNpcStrike rejected Cultist summon from {tsPlayer.Name}"));
                    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                }
            }
        }

        private static void HandleProjectileKill(ref RecievePacketEvent<KillProjectile> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            var ident = args.Packet.ProjSlot;
            var owner = (byte)tsPlayer.Index;

            var index = Utils.SearchProjectile(server, ident, owner);

            //if (OnProjectileKill(tsPlayer, args.Data, ident, owner, index)) {
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            //}

            short type = (short)server.Main.projectile[index].type;

            // TODO: This needs to be moved somewhere else.

            if (type == ProjectileID.Tombstone) {
                server.Log.Debug(GetString("GetDataHandlers / HandleProjectileKill rejected tombstone {0}", tsPlayer.Name));
                tsPlayer.RemoveProjectile(ident, owner);
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (TShock.ProjectileBans.ProjectileIsBanned(type, tsPlayer) && !setting.IgnoreProjKill) {
                // According to 2012 deathmax, this is a workaround to fix skeletron prime issues
                // https://github.com/Pryaxis/TShock/commit/a5aa9231239926f361b7246651e32144bbf28dda
                if (type == ProjectileID.Bomb || type == ProjectileID.DeathLaser) {
                    server.Log.Debug(GetString("GetDataHandlers / HandleProjectileKill permitted skeletron prime exemption {0}", tsPlayer.Name));
                    server.Log.Debug(GetString("If this was not skeletron prime related, please report to TShock what happened."));
                    return;
                }
                server.Log.Debug(GetString("GetDataHandlers / HandleProjectileKill rejected banned projectile {0}", tsPlayer.Name));
                tsPlayer.RemoveProjectile(ident, owner);
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            tsPlayer.LastKilledProjectile = type;
            lock (tsPlayer.RecentlyCreatedProjectiles) {
                tsPlayer.RecentlyCreatedProjectiles.ForEach(s => { if (s.Index == index) { s.Killed = true; } });
            }

            return;
        }

        private static void HandleTogglePvp(ref RecievePacketEvent<PlayerPvP> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            var id = args.Packet.PlayerSlot;
            //if (OnPvpToggled(tsPlayer, args.Data, id, pvp))
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            if (id != tsPlayer.Index) {
                server.Log.Debug(GetString("GetDataHandlers / HandleTogglePvp rejected index mismatch {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            string pvpMode = setting.PvPMode.ToLowerInvariant();
            if (pvpMode == "disabled" || pvpMode == "always" || pvpMode == "pvpwithnoteam" || (DateTime.UtcNow - tsPlayer.LastPvPTeamChange).TotalSeconds < 5) {
                server.Log.Debug(GetString("GetDataHandlers / HandleTogglePvp rejected fastswitch {0}", tsPlayer.Name));
                tsPlayer.SendData(PacketTypes.TogglePvp, "", id);
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            tsPlayer.LastPvPTeamChange = DateTime.UtcNow;
        }

        private static void HandleChestItem(ref RecievePacketEvent<SyncChestItem> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            var stacks = args.Packet.Stack;
            var type = args.Packet.ItemType;

            //if (OnChestItemChange(tsPlayer, args.Data, id, slot, stacks, prefix, type))
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            Item item = new Item();
            item.netDefaults(server, type);
            if (args.Packet.Stack > item.maxStack) {
                server.Log.Debug(GetString("GetDataHandlers / HandleChestItem rejected max stacks {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
        }

        private static void HandleChestActive(ref RecievePacketEvent<SyncPlayerChest> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            //chest ID
            var id = args.Packet.Chest;
            //chest x
            var x = args.Packet.Position.X;
            //chest y
            var y = args.Packet.Position.X;

            tsPlayer.ActiveChest = id;

            if (!tsPlayer.HasBuildPermission(x, y) && setting.RegionProtectChests) {
                server.Log.Debug(GetString("GetDataHandlers / HandleChestActive rejected build permission and region check {0}", tsPlayer.Name));
                tsPlayer.SendData(PacketTypes.ChestOpen, "", -1);
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
        }

        private static void HandlePlayerZone_NullCheck(ref RecievePacketEvent<PlayerZone> args) {
            var tsPlayer = args.GetTSPlayer();
            if (tsPlayer == null || tsPlayer.TPlayer == null) {
                var server = args.LocalReciever.Server;
                server.Log.Debug(GetString("GetDataHandlers / HandlePlayerZone rejected null check"));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
        }

        private static void HandlePassword(ref RecievePacketEvent<SendPassword> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GlobalSettings;
// #error TODO HandlePassword Logic

            if (!tsPlayer.RequiresPassword) { 
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }

            string password = args.Packet.Password;

            if (Hooks.PlayerHooks.OnPlayerPreLogin(tsPlayer, tsPlayer.Name, password)) { 
                args.HandleMode = PacketHandleMode.Cancel; 
                args.StopPropagation = true;
                return; 
            }

            var account = TShock.UserAccounts.GetUserAccountByName(tsPlayer.Name);
            if (account != null && !setting.DisableLoginBeforeJoin) {
                if (account.VerifyPassword(password)) {
                    tsPlayer.RequiresPassword = false;
                    tsPlayer.PlayerData = TShock.CharacterDB.GetPlayerData(tsPlayer, account.ID);

                    if (tsPlayer.State == (int)ConnectionState.AssigningPlayerSlot)
                        tsPlayer.State = (int)ConnectionState.AwaitingPlayerInfo;

                    server.NetMessage.SendData((int)PacketTypes.WorldInfo, tsPlayer.Index);

                    var group = TShock.Groups.GetGroupByName(account.Group);

                    if (!TShock.Groups.AssertGroupValid(tsPlayer, group, true)) { 
                        args.HandleMode = PacketHandleMode.Cancel; 
                        args.StopPropagation = true; 
                        return;
                    }

                    tsPlayer.Group = group;
                    tsPlayer.tempGroup = null;
                    tsPlayer.Account = account;
                    tsPlayer.IsLoggedIn = true;
                    tsPlayer.IsDisabledForSSC = false;

                    if (server.Main.ServerSideCharacter) {
                        if (tsPlayer.HasPermission(Permissions.bypassssc)) {
                            tsPlayer.PlayerData.CopyCharacter(tsPlayer);
                            TShock.CharacterDB.InsertPlayerData(tsPlayer);
                        }
                        tsPlayer.PlayerData.RestoreCharacter(tsPlayer);
                    }
                    tsPlayer.LoginFailsBySsi = false;

                    if (tsPlayer.HasPermission(Permissions.ignorestackhackdetection))
                        tsPlayer.IsDisabledForStackDetection = false;

                    if (tsPlayer.HasPermission(Permissions.usebanneditem))
                        tsPlayer.IsDisabledForBannedWearable = false;


                    tsPlayer.SendMessage(GetString($"Authenticated as {tsPlayer.Name} successfully."), Color.LimeGreen);
                    server.Log.Info(GetString($"{tsPlayer.Name} authenticated successfully as user {tsPlayer.Name}."));
                    TShock.UserAccounts.SetUserAccountUUID(account, tsPlayer.UUID);
                    Hooks.PlayerHooks.OnPlayerPostLogin(tsPlayer);

                    args.HandleMode = PacketHandleMode.Cancel;
                    args.StopPropagation = true;
                    return;
                }
                tsPlayer.Kick(GetString("Your password did not match this character's password."), true, true);

                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }

            if (!string.IsNullOrEmpty(setting.ServerPassword)) {
                if (setting.ServerPassword == password) {
                    tsPlayer.RequiresPassword = false;

                    if (tsPlayer.State == (int)ConnectionState.AssigningPlayerSlot)
                        tsPlayer.State = (int)ConnectionState.AwaitingPlayerInfo;

                    server.NetMessage.SendData((int)PacketTypes.WorldInfo, tsPlayer.Index);
                    args.HandleMode = PacketHandleMode.Cancel;
                    args.StopPropagation = true;
                    return;
                }
                tsPlayer.Kick(GetString("Invalid server password."), true, true);
                args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
                return;
            }

            tsPlayer.Kick(GetParticularString("Likely non-vanilla client send zero-length password", "You have been Bounced for invalid password."), true, true);
            args.HandleMode = PacketHandleMode.Cancel;
            args.StopPropagation = true;
            return;
        }

        private static void HandleNpcTalk(ref RecievePacketEvent<PlayerTalkingNPC> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            var npc = args.Packet.NPCSlot;

            //if (OnNpcTalk(tsPlayer, args.Data, plr, npc))
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            //Rejecting player who trying to talk to a npc if player were disabled, server.Mainly for unregistered and logged out players. Preventing smuggling or duplicating their items if player put it in a npc's item slot
            if (tsPlayer.IsBeingDisabled()) {
                server.Log.Debug(GetString("GetDataHandlers / HandleNpcTalk rejected npc talk {0}", tsPlayer.Name));
                tsPlayer.SendData(PacketTypes.NpcTalk, "", tsPlayer.Index, -1);
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (tsPlayer.IsBouncerThrottled()) {
                server.Log.Debug(GetString("Bouncer / HandleNpcTalk rejected from bouncer throttle from {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            // -1 is a magic value, represents not talking to an NPC
            if (npc < -1 || npc >= Main.maxNPCs) {
                server.Log.Debug(GetString("Bouncer / HandleNpcTalk rejected from bouncer out of bounds from {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
        }

        private static void HandlePlayerMana(ref RecievePacketEvent<PlayerMana> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            var plr = args.Packet.PlayerSlot;
            var cur = args.Packet.StatMana;
            var max = args.Packet.StatManaMax;

            if (/*OnPlayerMana(tsPlayer, args.Data, plr, cur, max) || */cur < 0 || max < 0 || tsPlayer.IgnoreSSCPackets)
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            if (max > setting.MaxMP && !tsPlayer.HasPermission(Permissions.ignoremp)) {
                server.Log.Debug(GetString("GetDataHandlers / HandlePlayerMana rejected max mana {0} {1}/{2}", tsPlayer.Name, max, setting.MaxMP));
                tsPlayer.Disable("Maximum MP beyond limit", DisableFlags.WriteToLogAndConsole);
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (tsPlayer.IsLoggedIn) {
                tsPlayer.TPlayer.statMana = cur;
                tsPlayer.TPlayer.statManaMax = max;
                tsPlayer.PlayerData.maxMana = max;
            }
        }

        private static void HandlePlayerTeam(ref RecievePacketEvent<PlayerTeam> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            byte id = args.Packet.PlayerSlot;
            byte team = args.Packet.Team;
            //if (OnPlayerTeam(tsPlayer, args.Data, id, team))
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            if (id != tsPlayer.Index)
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            string pvpMode = setting.PvPMode.ToLowerInvariant();
            if (pvpMode == "pvpwithnoteam" || (DateTime.UtcNow - tsPlayer.LastPvPTeamChange).TotalSeconds < 5) {
                tsPlayer.SendData(PacketTypes.PlayerTeam, "", id);
                server.Log.Debug(GetString("GetDataHandlers / HandlePlayerTeam rejected team fastswitch {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            tsPlayer.LastPvPTeamChange = DateTime.UtcNow;
            // return false;
        }

        private static void HandleSignRead(ref RecievePacketEvent<RequestReadSign> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            var x = args.Packet.Position.X;
            var y = args.Packet.Position.Y;

            //if (OnSignRead(tsPlayer, args.Data, x, y))
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            if (x < 0 || y < 0 || x >= server.Main.maxTilesX || y >= server.Main.maxTilesY) {
                server.Log.Debug(GetString("GetDataHandlers / HandleSignRead rejected out of bounds {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
        }

        private static void HandleSign(ref RecievePacketEvent<ReadSign> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            var id = args.Packet.SignSlot;
            var x = args.Packet.Position.X;
            var y = args.Packet.Position.Y;

            //if (OnSignEvent(tsPlayer, args.Data, id, x, y))
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            if (!tsPlayer.HasBuildPermission(x, y)) {
                tsPlayer.SendData(PacketTypes.SignNew, "", id);
                server.Log.Debug(GetString("GetDataHandlers / HandleSign rejected sign on build permission {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (!tsPlayer.IsInRange(x, y)) {
                tsPlayer.SendData(PacketTypes.SignNew, "", id);
                server.Log.Debug(GetString("GetDataHandlers / HandleSign rejected sign range check {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
        }

        private static void HandlePlayerBuffList(ref RecievePacketEvent<PlayerBuffs> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            var id = args.Packet.PlayerSlot;

            //if (OnPlayerBuffUpdate(tsPlayer, args.Data, id))
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            for (int i = 0; i < Terraria.Player.maxBuffs; i++) {
                var buff = args.Packet.BuffTypes[i];

                if (buff == 10 && setting.DisableInvisPvP && tsPlayer.TPlayer.hostile)
                    buff = 0;

                if (server.Netplay.Clients[tsPlayer.TPlayer.whoAmI].State < (int)ConnectionState.AwaitingPlayerInfo && (buff == 156 || buff == 47 || buff == 149)) {
                    server.Log.Debug(GetString("GetDataHandlers / HandlePlayerBuffList zeroed player buff due to below state awaiting player information {0} {1}", tsPlayer.Name, buff));
                    buff = 0;
                }

                tsPlayer.TPlayer.buffType[i] = buff;
                if (tsPlayer.TPlayer.buffType[i] > 0) {
                    tsPlayer.TPlayer.buffTime[i] = 60;
                }
                else {
                    tsPlayer.TPlayer.buffTime[i] = 0;
                }
            }

            server.Log.Debug(GetString("GetDataHandlers / HandlePlayerBuffList handled event and sent data {0}", tsPlayer.Name));
            server.NetMessage.SendData((int)PacketTypes.PlayerBuff, -1, tsPlayer.Index, NetworkText.Empty, tsPlayer.Index);
            { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
        }

        private static void HandleSpecial(ref RecievePacketEvent<Assorted1> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            var id = args.Packet.PlayerSlot;
            var type = args.Packet.Unknown;

            //if (OnNPCSpecial(tsPlayer, args.Data, id, type))
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            if (type == 1) {
                if (!tsPlayer.HasPermission(Permissions.summonboss)) {
                    tsPlayer.SendErrorMessage(GetString("You do not have permission to summon the Skeletron."));
                    server.Log.Debug(GetString($"GetDataHandlers / HandleNpcStrike rejected Skeletron summon from {tsPlayer.Name}"));
                    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                }
            }
            else if (type == 2) {
                // Plays SoundID.Item1
            }
            else if (type == 3) {
                if (!tsPlayer.HasPermission(Permissions.usesundial)) {
                    server.Log.Debug(GetString($"GetDataHandlers / HandleSpecial rejected enchanted sundial permission {tsPlayer.Name}"));
                    tsPlayer.SendErrorMessage(GetString("You do not have permission to use the Enchanted Sundial."));
                    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                }
                else if (setting.ForceTime != "normal") {
                    server.Log.Debug(GetString($"GetDataHandlers / HandleSpecial rejected enchanted sundial permission (ForceTime) {tsPlayer.Name}"));
                    if (!tsPlayer.HasPermission(Permissions.cfgreload)) {
                        tsPlayer.SendErrorMessage(GetString("You cannot use the Enchanted Sundial because time is stopped."));
                    }
                    else
                        tsPlayer.SendErrorMessage(GetString("You must set ForceTime to normal via config to use the Enchanted Sundial."));
                    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                }
            }
            else if (type == 4) {
                // Big Mimic Spawn Smoke
            }
            else if (type == 5) {
                // Register Kill for Torch God in Bestiary
            }
            else if (type == 6) {
                if (!tsPlayer.HasPermission(Permissions.usemoondial)) {
                    server.Log.Debug(GetString($"GetDataHandlers / HandleSpecial rejected enchanted moondial permission {tsPlayer.Name}"));
                    tsPlayer.SendErrorMessage(GetString("You do not have permission to use the Enchanted Moondial."));
                    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                }
                else if (setting.ForceTime != "normal") {
                    server.Log.Debug(GetString($"GetDataHandlers / HandleSpecial rejected enchanted moondial permission (ForceTime) {tsPlayer.Name}"));
                    if (!tsPlayer.HasPermission(Permissions.cfgreload)) {
                        tsPlayer.SendErrorMessage(GetString("You cannot use the Enchanted Moondial because time is stopped."));
                    }
                    else
                        tsPlayer.SendErrorMessage(GetString("You must set ForceTime to normal via config to use the Enchanted Moondial."));
                    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                }
            }
            else if (!tsPlayer.HasPermission($"tshock.specialeffects.{type}")) {
                tsPlayer.SendErrorMessage(GetString("You do not have permission to use this effect."));
                server.Log.Error(GetString("Unrecognized special effect (Packet 51). Please report this to the TShock developers."));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
        }

        private static void HandleUpdateNPCHome(ref RecievePacketEvent<NPCHome> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            var id = args.Packet.NPCSlot;
            var x = args.Packet.Position.X;
            var y = args.Packet.Position.Y;
            var householdStatus = args.Packet.Homeless;

            //if (OnUpdateNPCHome(tsPlayer, args.Data, id, x, y, householdStatus))
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            if (!tsPlayer.HasPermission(Permissions.movenpc)) {
                server.Log.Debug(GetString("GetDataHandlers / UpdateNPCHome rejected no permission {0}", tsPlayer.Name));
                tsPlayer.SendErrorMessage(GetString("You do not have permission to relocate Town NPCs."));
                tsPlayer.SendData(PacketTypes.UpdateNPCHome, "", id, server.Main.npc[id].homeTileX, server.Main.npc[id].homeTileY,
                    Convert.ToByte(server.Main.npc[id].homeless));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
        }

        private static readonly int[] invasions = { -1, -2, -3, -4, -5, -6, -7, -8, -10 };
        private static readonly int[] pets = { -12, -13, -14, -15 };
        private static readonly int[] upgrades = { -11, -17, -18 };
        private static void HandleSpawnBoss(ref RecievePacketEvent<SpawnBoss> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            if (tsPlayer.IsBouncerThrottled()) {
                server.Log.Debug(GetString("GetDataHandlers / HandleSpawnBoss rejected bouner throttled {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            var plr = args.Packet.OtherPlayerSlot;
            var thingType = args.Packet.NPCType;

            var isKnownBoss = (thingType > 0 && thingType < Terraria.ID.NPCID.Count && NPCID.Sets.MPAllowedEnemies[thingType]) || thingType == -16;
            if (isKnownBoss && !tsPlayer.HasPermission(Permissions.summonboss)) {
                server.Log.Debug(GetString("GetDataHandlers / HandleSpawnBoss rejected boss {0} {1}", tsPlayer.Name, thingType));
                tsPlayer.SendErrorMessage(GetString("You do not have permission to summon bosses."));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (invasions.Contains(thingType) && !tsPlayer.HasPermission(Permissions.startinvasion)) {
                server.Log.Debug(GetString("GetDataHandlers / HandleSpawnBoss rejected invasion {0} {1}", tsPlayer.Name, thingType));
                tsPlayer.SendErrorMessage(GetString("You do not have permission to start invasions."));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (pets.Contains(thingType) && !tsPlayer.HasPermission(Permissions.spawnpets)) {
                server.Log.Debug(GetString("GetDataHandlers / HandleSpawnBoss rejected pet {0} {1}", tsPlayer.Name, thingType));
                tsPlayer.SendErrorMessage(GetString("You do not have permission to spawn pets."));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (upgrades.Contains(thingType) && !tsPlayer.HasPermission(Permissions.worldupgrades)) {
                server.Log.Debug(GetString("GetDataHandlers / HandleSpawnBoss rejected upgrade {0} {1}", tsPlayer.Name, thingType));
                tsPlayer.SendErrorMessage(GetString("You do not have permission to use permanent boosters."));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (plr != tsPlayer.Index)
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            string thing;
            switch (thingType) {
                case -18:
                    thing = GetString("{0} applied traveling merchant's satchel!", tsPlayer.Name);
                    break;
                case -17:
                    thing = GetString("{0} applied advanced combat techniques volume 2!", tsPlayer.Name);
                    break;
                case -16:
                    thing = GetString("{0} summoned a Mechdusa!", tsPlayer.Name);
                    break;
                case -15:
                    thing = GetString("{0} has sent a request to the slime delivery service!", tsPlayer.Name);
                    break;
                case -14:
                    thing = GetString("{0} has sent a request to the bunny delivery service!", tsPlayer.Name);
                    break;
                case -13:
                    thing = GetString("{0} has sent a request to the dog delivery service!", tsPlayer.Name);
                    break;
                case -12:
                    thing = GetString("{0} has sent a request to the cat delivery service!", tsPlayer.Name);
                    break;
                case -11:
                    thing = GetString("{0} applied advanced combat techniques!", tsPlayer.Name);
                    break;
                case -10:
                    thing = GetString("{0} summoned a Blood Moon!", tsPlayer.Name);
                    break;
                case -8:
                    thing = GetString("{0} summoned a Moon Lord!", tsPlayer.Name);
                    break;
                case -7:
                    thing = GetString("{0} summoned a Martian invasion!", tsPlayer.Name);
                    break;
                case -6:
                    thing = GetString("{0} summoned an eclipse!", tsPlayer.Name);
                    break;
                case -5:
                    thing = GetString("{0} summoned a frost moon!", tsPlayer.Name);
                    break;
                case -4:
                    thing = GetString("{0} summoned a pumpkin moon!", tsPlayer.Name);
                    break;
                case -3:
                    thing = GetString("{0} summoned the Pirates!", tsPlayer.Name);
                    break;
                case -2:
                    thing = GetString("{0} summoned the Snow Legion!", tsPlayer.Name);
                    break;
                case -1:
                    thing = GetString("{0} summoned a Goblin Invasion!", tsPlayer.Name);
                    break;
                default:
                    if (!isKnownBoss)
                        server.Log.Debug(GetString("GetDataHandlers / HandleSpawnBoss unknown boss {0} summoned by {1}", thingType, tsPlayer.Name));
                    NPC npc = new NPC();
                    npc.SetDefaults(server, thingType);
                    thing = GetString("{0} summoned the {1}!", tsPlayer.Name, npc.FullName);
                    break;
            }

            if (thingType < 0 || isKnownBoss) {
                if (setting.AnonymousBossInvasions)
                    Utils.SendLogs(server, thing, Color.PaleVioletRed, tsPlayer);
                else
                    Utils.Broadcast(server, thing, 175, 75, 255);
            }
        }

        private static bool HasPaintSprayerAbilities(Item item)
            => item is not null && item.stack > 0 && (
               item.type == ItemID.PaintSprayer ||
               item.type == ItemID.ArchitectGizmoPack ||
               item.type == ItemID.HandOfCreation);

        private static void HandlePaintTile(ref RecievePacketEvent<PaintTile> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            var x = args.Packet.Position.X;
            var y = args.Packet.Position.Y;
            var t = args.Packet.Color;
            var ct = args.Packet.CoatPaint;//PaintCoatTile

            if (x < 0 || y < 0 || x >= server.Main.maxTilesX || y >= server.Main.maxTilesY || t > Main.numTileColors) {
                server.Log.Debug(GetString("GetDataHandlers / HandlePaintTile rejected range check {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
            //if (OnPaintTile(tsPlayer, args.Data, x, y, t, ct)) {
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            //}

            // Not selecting paintbrush or paint scraper or the spectre versions? Hacking.
            if (tsPlayer.SelectedItem.type != ItemID.PaintRoller &&
                tsPlayer.SelectedItem.type != ItemID.PaintScraper &&
                tsPlayer.SelectedItem.type != ItemID.Paintbrush &&
                tsPlayer.SelectedItem.type != ItemID.SpectrePaintRoller &&
                tsPlayer.SelectedItem.type != ItemID.SpectrePaintScraper &&
                tsPlayer.SelectedItem.type != ItemID.SpectrePaintbrush &&
                !tsPlayer.Accessories.Any(HasPaintSprayerAbilities) &&
                !tsPlayer.Inventory.Any(HasPaintSprayerAbilities)) {
                server.Log.Debug(GetString("GetDataHandlers / HandlePaintTile rejected select consistency {0}", tsPlayer.Name));
                tsPlayer.SendData(PacketTypes.PaintTile, "", x, y, server.Main.tile[x, y].color());
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (tsPlayer.IsBouncerThrottled() ||
                !tsPlayer.HasPaintPermission(x, y) ||
                !tsPlayer.IsInRange(x, y)) {
                server.Log.Debug(GetString("GetDataHandlers / HandlePaintTile rejected throttle/permission/range check {0}", tsPlayer.Name));
                tsPlayer.SendData(PacketTypes.PaintTile, "", x, y, server.Main.tile[x, y].color());
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (!tsPlayer.HasPermission(Permissions.ignorepaintdetection)) {
                tsPlayer.PaintThreshold++;
            }
        }

        private static void HandlePaintWall(ref RecievePacketEvent<PaintWall> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            var x = args.Packet.Position.X;
            var y = args.Packet.Position.Y;
            var w = args.Packet.Color;
            var cw = args.Packet.CoatPaint;//PaintCoatTile

            if (x < 0 || y < 0 || x >= server.Main.maxTilesX || y >= server.Main.maxTilesY || w > Main.numTileColors) {
                server.Log.Debug(GetString("GetDataHandlers / HandlePaintWall rejected range check {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
            //if (OnPaintWall(tsPlayer, args.Data, x, y, w, cw)) {
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            //}

            // Not selecting paint roller or paint scraper or the spectre versions? Hacking.
            if (tsPlayer.SelectedItem.type != ItemID.PaintRoller &&
                tsPlayer.SelectedItem.type != ItemID.PaintScraper &&
                tsPlayer.SelectedItem.type != ItemID.Paintbrush &&
                tsPlayer.SelectedItem.type != ItemID.SpectrePaintRoller &&
                tsPlayer.SelectedItem.type != ItemID.SpectrePaintScraper &&
                tsPlayer.SelectedItem.type != ItemID.SpectrePaintbrush &&
                !tsPlayer.Accessories.Any(HasPaintSprayerAbilities) &&
                !tsPlayer.Inventory.Any(HasPaintSprayerAbilities)) {
                server.Log.Debug(GetString("GetDataHandlers / HandlePaintWall rejected selector consistency {0}", tsPlayer.Name));
                tsPlayer.SendData(PacketTypes.PaintWall, "", x, y, server.Main.tile[x, y].wallColor());
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (tsPlayer.IsBouncerThrottled() ||
                !tsPlayer.HasPaintPermission(x, y) ||
                !tsPlayer.IsInRange(x, y)) {
                server.Log.Debug(GetString("GetDataHandlers / HandlePaintWall rejected throttle/permission/range {0}", tsPlayer.Name));
                tsPlayer.SendData(PacketTypes.PaintWall, "", x, y, server.Main.tile[x, y].wallColor());
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (!tsPlayer.HasPermission(Permissions.ignorepaintdetection)) {
                tsPlayer.PaintThreshold++;
            }
        }

        private static void HandleTeleport(ref RecievePacketEvent<Teleport> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            BitsByte flag = args.Packet.Bit1;
            short id = args.Packet.PlayerSlot;
            var x = args.Packet;
            var y = args.Packet;
            byte style = args.Packet.Style;

            int type = 0;
            bool isNPC = type == 1;
            int extraInfo = -1;
            bool getPositionFromTarget = false;

            if (flag[0]) {
                type = 1;
            }
            if (flag[1]) {
                type = 2;
            }
            if (flag[2]) {
                getPositionFromTarget = true;
            }
            if (flag[3]) {
                extraInfo = args.Packet.ExtraInfo;
            }

            //if (OnTeleport(tsPlayer, args.Data, id, flag, x, y, style, extraInfo))
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            //Rod of Discord teleport (usually (may be used by modded clients to teleport))
            if (type == 0 && !tsPlayer.HasPermission(Permissions.rod)) {
                server.Log.Debug(GetString("GetDataHandlers / HandleTeleport rejected rod type {0} {1}", tsPlayer.Name, type));
                tsPlayer.SendErrorMessage(GetString("You do not have permission to teleport using items.")); // Was going to write using RoD but Hook of Disonnance and Potion of Return both use the same teleport packet as RoD. 
                tsPlayer.Teleport(tsPlayer.TPlayer.position.X, tsPlayer.TPlayer.position.Y); // Suggest renaming rod permission unless someone plans to add separate perms for the other 2 tp items.
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            //NPC teleport
            if (type == 1 && id >= Main.maxNPCs) {
                server.Log.Debug(GetString("GetDataHandlers / HandleTeleport rejected npc teleport {0} {1}", tsPlayer.Name, type));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            //Player to player teleport (wormhole potion, usually (may be used by modded clients to teleport))
            if (type == 2) {
                if (id >= Main.maxPlayers || server.Main.player[id] == null || TShock.Players[id] == null) {
                    server.Log.Debug(GetString("GetDataHandlers / HandleTeleport rejected p2p extents {0} {1}", tsPlayer.Name, type));
                    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                }

                if (!tsPlayer.HasPermission(Permissions.wormhole)) {
                    server.Log.Debug(GetString("GetDataHandlers / HandleTeleport rejected p2p wormhole permission {0} {1}", tsPlayer.Name, type));
                    tsPlayer.SendErrorMessage(GetString("You do not have permission to teleport using Wormhole Potions."));
                    tsPlayer.Teleport(tsPlayer.TPlayer.position.X, tsPlayer.TPlayer.position.Y);
                    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                }
            }
        }

        private static void HandleCatchNpc(ref RecievePacketEvent<BugCatching> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            var npcID = args.Packet.NPCSlot;

            if (server.Main.npc[npcID]?.catchItem == 0) {
                server.Log.Debug(GetString("GetDataHandlers / HandleCatchNpc catch zero {0}", tsPlayer.Name));
                server.Main.npc[npcID].active = true;
                server.NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, NetworkText.Empty, npcID);
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (tsPlayer.IsBeingDisabled()) {
                server.Log.Debug(GetString("GetDataHandlers / HandleCatchNpc rejected catch npc {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
        }

        private static void HandleTeleportationPotion(ref RecievePacketEvent<TeleportationPotion> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            var type = args.Packet.Style;

            void Fail(ServerContext server, TSPlayer tsPlayer, string tpItem) {
                server.Log.Debug(GetString("GetDataHandlers / HandleTeleportationPotion rejected permissions {0} {1}", tsPlayer.Name, type));
                tsPlayer.SendErrorMessage(GetString("You do not have permission to teleport using {0}.", tpItem));
            }

            switch (type) {
                case 0: // Teleportation Potion
                    if (tsPlayer.ItemInHand.type != ItemID.TeleportationPotion &&
                        tsPlayer.SelectedItem.type != ItemID.TeleportationPotion) {
                        server.Log.Debug(GetString("GetDataHandlers / HandleTeleportationPotion rejected not holding the correct item {0} {1}", tsPlayer.Name, type));
                        { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                    }

                    if (!tsPlayer.HasPermission(Permissions.tppotion)) {
                        Fail(server, tsPlayer, "Teleportation Potions");
                        { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                    }
                    break;
                case 1: // Magic Conch or Shellphone (Ocean)
                    if (tsPlayer.ItemInHand.type != ItemID.MagicConch &&
                        tsPlayer.SelectedItem.type != ItemID.MagicConch &&
                        tsPlayer.ItemInHand.type != ItemID.ShellphoneOcean &&
                        tsPlayer.SelectedItem.type != ItemID.ShellphoneOcean) {
                        server.Log.Debug(GetString("GetDataHandlers / HandleTeleportationPotion rejected not holding the correct item {0} {1}", tsPlayer.Name, type));
                        { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                    }

                    if (!tsPlayer.HasPermission(Permissions.magicconch)) {
                        if (tsPlayer.ItemInHand.type == ItemID.ShellphoneOcean || tsPlayer.SelectedItem.type == ItemID.ShellphoneOcean) {
                            Fail(server, tsPlayer, "the Shellphone (Ocean)");
                        }
                        else {
                            Fail(server, tsPlayer, "the Magic Conch");
                        }
                        { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                    }
                    break;
                case 2: // Demon Conch or Shellphone (Underworld)
                    if (tsPlayer.ItemInHand.type != ItemID.DemonConch &&
                        tsPlayer.SelectedItem.type != ItemID.DemonConch &&
                        tsPlayer.ItemInHand.type != ItemID.ShellphoneHell &&
                        tsPlayer.SelectedItem.type != ItemID.ShellphoneHell) {
                        server.Log.Debug(GetString("GetDataHandlers / HandleTeleportationPotion rejected not holding the correct item {0} {1}", tsPlayer.Name, type));
                        { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                    }

                    if (!tsPlayer.HasPermission(Permissions.demonconch)) {
                        if (tsPlayer.ItemInHand.type == ItemID.ShellphoneHell || tsPlayer.SelectedItem.type == ItemID.ShellphoneHell) {
                            Fail(server, tsPlayer, "the Shellphone (Underworld)");
                        }
                        else {
                            Fail(server, tsPlayer, "the Demon Conch");
                        }
                        { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                    }
                    break;
                case 3: // Shellphone (Spawn)
                    if (tsPlayer.ItemInHand.type != ItemID.ShellphoneSpawn && tsPlayer.SelectedItem.type != ItemID.ShellphoneSpawn) {
                        server.Log.Debug(GetString("GetDataHandlers / HandleTeleportationPotion rejected not holding the correct item {0} {1}", tsPlayer.Name, type));
                        { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                    }
                    break;
            }
        }

        private static void HandleCompleteAnglerQuest(ref RecievePacketEvent<AnglerQuestFinished> args) {
            // Since packet 76 is NEVER sent to us, we actually have to rely on this to get the true count
            var tsPlayer = args.GetTSPlayer();
            tsPlayer.TPlayer.anglerQuestsFinished++;
        }

        private static void HandleNumberOfAnglerQuestsCompleted(ref RecievePacketEvent<AnglerQuestCountSync> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            // Never sent by vanilla client, ignore this
            server.Log.Debug(GetString("GetDataHandlers / HandleNumberOfAnglerQuestsCompleted surprise packet! Someone tell the TShock team! {0}", tsPlayer.Name));
            { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
        }

        private static void HandlePlaceTileEntity(ref RecievePacketEvent<TileEntityPlacement> args) {
            var tsPlayer = args.GetTSPlayer();

            var x = args.Packet.Position.X;
            var y = args.Packet.Position.Y;
            var type = args.Packet.TileEntityType;

            //if (OnPlaceTileEntity(tsPlayer, args.Data, x, y, type)) {
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            //}

            // ItemBan subsystem

            if (type is (byte)TileEntityType.TELogicSensor && TShock.TileBans.TileIsBanned((short)TileID.LogicSensor, tsPlayer)) {
                tsPlayer.SendTileSquareCentered(x, y, 1);
                tsPlayer.SendErrorMessage(GetString("You do not have permission to place Logic Sensors."));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
        }

        private static void HandleSyncExtraValue(ref RecievePacketEvent<SyncExtraValue> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            var npcIndex = args.Packet.NPCSlot;
            var extraValue = args.Packet.Extra;
            var position = args.Packet.MoneyPing;

            if (position.X < 0 || position.X >= (server.Main.maxTilesX * 16.0f) || position.Y < 0 || position.Y >= (server.Main.maxTilesY * 16.0f)) {
                server.Log.Debug(GetString("GetDataHandlers / HandleSyncExtraValue rejected extents check {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (!server.Main.expertMode && !server.Main.masterMode) {
                server.Log.Debug(GetString("GetDataHandlers / HandleSyncExtraValue rejected expert/master mode check {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (npcIndex < 0 || npcIndex >= server.Main.npc.Length) {
                server.Log.Debug(GetString("GetDataHandlers / HandleSyncExtraValue rejected npc id out of bounds check - NPC ID: {0}", npcIndex));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            var npc = server.Main.npc[npcIndex];
            if (npc == null) {
                server.Log.Debug(GetString("GetDataHandlers / HandleSyncExtraValue rejected npc is null - NPC ID: {0}", npcIndex));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            var distanceFromCoinPacketToNpc = Utils.Distance(position, npc.position);
            if (distanceFromCoinPacketToNpc >= (5 * 16f)) //5 tile range
            {
                server.Log.Debug(GetString("GetDataHandlers / HandleSyncExtraValue rejected range check {0},{1} vs {2},{3} which is {4}", npc.position.X, npc.position.Y, position.X, position.Y, distanceFromCoinPacketToNpc));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
        }

        private static void HandleKillPortal(ref RecievePacketEvent<MurderSomeoneElsesProjectile> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            short projectileIndex = args.Packet.OtherPlayerSlot;
            // args.Packet.; // Read byte projectile AI

            Projectile projectile = server.Main.projectile[projectileIndex];
            if (projectile != null && projectile.active) {
                if (projectile.owner != tsPlayer.TPlayer.whoAmI) {
                    server.Log.Debug(GetString("GetDataHandlers / HandleKillPortal rejected owner mismatch check {0}", tsPlayer.Name));
                    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
                }
            }
        }

        private static void HandleNpcTeleportPortal(ref RecievePacketEvent<TeleportNPCThroughPortal> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            var npcIndex = args.Packet.NPCSlot;
            var portalColorIndex = args.Packet.Extra;
            var newPosition = args.Packet.Position;
            var velocity = args.Packet.Velocity;

            var projectile = server.Main.projectile.FirstOrDefault(p => p.position.X == newPosition.X && p.position.Y == newPosition.Y); // Check for projectiles at this location

            if (projectile == null || !projectile.active) {
                server.Log.Debug(GetString("GetDataHandlers / HandleNpcTeleportPortal rejected null check {0}", tsPlayer.Name));
                server.NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, NetworkText.Empty, npcIndex);
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (projectile.type != ProjectileID.PortalGunGate) {
                server.Log.Debug(GetString("GetDataHandlers / HandleNpcTeleportPortal rejected not thinking with portals {0}", tsPlayer.Name));
                server.NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, NetworkText.Empty, npcIndex);
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
        }

        private static void HandleToggleParty(ref RecievePacketEvent<ToggleParty> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            if (tsPlayer != null && !tsPlayer.HasPermission(Permissions.toggleparty)) {
                server.Log.Debug(GetString("GetDataHandlers / HandleToggleParty rejected no party {0}", tsPlayer.Name));
                tsPlayer.SendErrorMessage(GetString("You do not have permission to start a party."));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }
        }

        private static void HandleOldOnesArmy(ref RecievePacketEvent<CrystalInvasionStart> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            if (tsPlayer.IsBouncerThrottled()) {
                server.Log.Debug(GetString("GetDataHandlers / HandleOldOnesArmy rejected throttled {0}", tsPlayer.Name));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (!tsPlayer.HasPermission(Permissions.startdd2)) {
                server.Log.Debug(GetString("GetDataHandlers / HandleOldOnesArmy rejected permissions {0}", tsPlayer.Name));
                tsPlayer.SendErrorMessage(GetString("You do not have permission to start the Old One's Army."));
                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (setting.AnonymousBossInvasions)
                Utils.SendLogs(server, GetString("{0} started the Old One's Army event!", tsPlayer.Name), Color.PaleVioletRed, tsPlayer);
            else
                Utils.Broadcast(server, GetString("{0} started the Old One's Army event!", tsPlayer.Name), 175, 75, 255);
        }

        private static void HandlePlayerKillMeV2(ref RecievePacketEvent<PlayerDeathV2> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            var id = args.Packet.PlayerSlot;
            var playerDeathReason = args.Packet.Reason;
            var dmg = args.Packet;
            var direction = (byte)(args.Packet.HitDirection - 1);
            BitsByte bits = args.Packet.Bits1;
            bool pvp = bits[0];

            //if (OnKillMe(tsPlayer, args.Data, id, direction, dmg, pvp, playerDeathReason))
            //    { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }

            tsPlayer.Dead = true;
            tsPlayer.RespawnTimer = setting.RespawnSeconds;

            foreach (NPC npc in server.Main.npc) {
                if (npc.active && (npc.boss || npc.type == 13 || npc.type == 14 || npc.type == 15) &&
                    Math.Abs(tsPlayer.TPlayer.Center.X - npc.Center.X) + Math.Abs(tsPlayer.TPlayer.Center.Y - npc.Center.Y) < 4000f) {
                    tsPlayer.RespawnTimer = setting.RespawnBossSeconds;
                    break;
                }
            }

            // Handle kicks/bans on mediumcore/hardcore deaths.
            if (tsPlayer.TPlayer.difficulty == 1 || tsPlayer.TPlayer.difficulty == 2) // Player is not softcore
            {
                bool mediumcore = tsPlayer.TPlayer.difficulty == 1;
                bool shouldBan = mediumcore ? setting.BanOnMediumcoreDeath : setting.BanOnHardcoreDeath;
                bool shouldKick = mediumcore ? setting.KickOnMediumcoreDeath : setting.KickOnHardcoreDeath;
                string banReason = mediumcore ? setting.MediumcoreBanReason : setting.HardcoreBanReason;
                string kickReason = mediumcore ? setting.MediumcoreKickReason : setting.HardcoreKickReason;

                if (shouldBan) {
                    if (!tsPlayer.Ban(banReason, "TShock")) {
                        server.Log.Debug(GetString("GetDataHandlers / HandlePlayerKillMeV2 kicked with difficulty {0} {1}", tsPlayer.Name, tsPlayer.TPlayer.difficulty));
                        tsPlayer.Kick(GetString("You died! Normally, you'd be banned."), true, true);
                    }
                }
                else if (shouldKick) {
                    server.Log.Debug(GetString("GetDataHandlers / HandlePlayerKillMeV2 kicked with difficulty {0} {1}", tsPlayer.Name, tsPlayer.TPlayer.difficulty));
                    tsPlayer.Kick(kickReason, true, true, null, false);
                }
            }

            if (tsPlayer.TPlayer.difficulty == 2 && server.Main.ServerSideCharacter && tsPlayer.IsLoggedIn) {
                if (TShock.CharacterDB.RemovePlayer(tsPlayer.Account.ID)) {
                    server.Log.Debug(GetString("GetDataHandlers / HandlePlayerKillMeV2 ssc delete {0} {1}", tsPlayer.Name, tsPlayer.TPlayer.difficulty));
                    tsPlayer.SendErrorMessage(GetString("You have fallen in hardcore mode, and your items have been lost forever."));
                    TShock.CharacterDB.SeedInitialData(tsPlayer.Account);
                }
            }
        }

        private static void HandleSyncCavernMonsterType(ref RecievePacketEvent<SyncCavernMonsterType> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            tsPlayer.Kick(GetString("Exploit attempt detected!"));
            server.Log.Debug(GetString($"HandleSyncCavernMonsterType: Player is trying to modify NPC cavernMonsterType; this is a crafted packet! - From {tsPlayer.Name}"));
            { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
        }

        private static void HandleSyncLoadout(ref RecievePacketEvent<SyncLoadout> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            var loadoutIndex = args.Packet.LoadOutSlot;

            // When syncing a player's own loadout index, they then sync it back to us...
            // So let's only care if the index has actually changed, otherwise we might end up in a loop...
            if (loadoutIndex == tsPlayer.TPlayer.CurrentLoadoutIndex)
                return;

            if (loadoutIndex >= tsPlayer.TPlayer.Loadouts.Length) {
                server.Log.Debug(GetString("GetDataHandlers / HandleSyncLoadout rejected loadout index sync out of bounds {0}",
                    tsPlayer.Name));
                server.NetMessage.SendData((int)PacketTypes.SyncLoadout, number: tsPlayer.Index, number2: tsPlayer.TPlayer.CurrentLoadoutIndex);

                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            if (tsPlayer.IsBeingDisabled()) {
                server.Log.Debug(GetString("GetDataHandlers / HandleSyncLoadout rejected loadout index sync {0}", tsPlayer.Name));
                server.NetMessage.SendData((int)PacketTypes.SyncLoadout, number: tsPlayer.Index, number2: tsPlayer.TPlayer.CurrentLoadoutIndex);

                { args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true; return; }
            }

            // Don't modify the player data if it isn't there.
            // This is the case whilst the player is connecting, as we receive the SyncLoadout packet before the ContinueConnecting2 packet.
            if (tsPlayer.PlayerData == null)
                return;

            // The client does not sync slot changes when changing loadouts, it only tells the server the loadout index changed,
            // and the server will replicate the changes the client did. This means that PlayerData.StoreSlot is never called, so we need to
            // swap around the PlayerData items ourself.

            Tuple<int, int> GetArmorSlotsForLoadoutIndex(int index) {
                return index switch {
                    0 => NetItem.Loadout1Armor,
                    1 => NetItem.Loadout2Armor,
                    2 or _ => NetItem.Loadout3Armor
                };
            }

            Tuple<int, int> GetDyeSlotsForLoadoutIndex(int index) {
                return index switch {
                    0 => NetItem.Loadout1Dye,
                    1 => NetItem.Loadout2Dye,
                    2 or _ => NetItem.Loadout3Dye
                };
            }

            var (currentLoadoutArmorSlotStartIndex, _) = GetArmorSlotsForLoadoutIndex(tsPlayer.TPlayer.CurrentLoadoutIndex);
            var (currentLoadoutDyeSlotStartIndex, _) = GetDyeSlotsForLoadoutIndex(tsPlayer.TPlayer.CurrentLoadoutIndex);

            var (switchedLoadoutArmorSlotStartIndex, _) = GetArmorSlotsForLoadoutIndex(loadoutIndex);
            var (switchedLoadoutDyeSlotStartIndex, _) = GetDyeSlotsForLoadoutIndex(loadoutIndex);

            // Emulate what is seen in Player.TrySwitchingLoadout:
            // - Swap the current loadout items with the player's equipment
            // - Swap the switching loadout items with the player's equipment

            // At the end of all of this:
            // - The current loadout will contain the player's original equipment
            // - The switched loadout will contain the current loadout's items
            // - The player's equipment will contain the switched loadout's item

            for (var i = 0; i < NetItem.LoadoutArmorSlots; i++)
                Terraria.Utils.Swap(ref tsPlayer.PlayerData.inventory[currentLoadoutArmorSlotStartIndex + i],
                    ref tsPlayer.PlayerData.inventory[NetItem.ArmorIndex.Item1 + i]);
            for (var i = 0; i < NetItem.LoadoutDyeSlots; i++)
                Terraria.Utils.Swap(ref tsPlayer.PlayerData.inventory[currentLoadoutDyeSlotStartIndex + i],
                    ref tsPlayer.PlayerData.inventory[NetItem.DyeIndex.Item1 + i]);

            for (var i = 0; i < NetItem.LoadoutArmorSlots; i++)
                Terraria.Utils.Swap(ref tsPlayer.PlayerData.inventory[switchedLoadoutArmorSlotStartIndex + i],
                    ref tsPlayer.PlayerData.inventory[NetItem.ArmorIndex.Item1 + i]);
            for (var i = 0; i < NetItem.LoadoutDyeSlots; i++)
                Terraria.Utils.Swap(ref tsPlayer.PlayerData.inventory[switchedLoadoutDyeSlotStartIndex + i],
                    ref tsPlayer.PlayerData.inventory[NetItem.DyeIndex.Item1 + i]);
        }
    }
}
