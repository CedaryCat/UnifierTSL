using MaxMind;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using System.Net;
using Terraria;
using Terraria.GameContent.Creative;
using Terraria.ID;
using Terraria.Localization;
using TShockAPI.Configuration;
using TShockAPI.Hooks;
using UnifiedServerProcess;
using UnifierTSL;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Servers;
using static TShockAPI.TShock;

namespace TShockAPI
{
    public class TSServerData : IExtensionData
    {
        internal DateTime LastSecondCheck = DateTime.UtcNow;

        /// <summary>LastSave - Used to keep track of SSC save intervals.</summary>
        internal DateTime LastSave = DateTime.UtcNow;
        public void Dispose() { }
    }
    public static class MiscHandler
    {
        public static void Attach() {
            TShock.Config.OnConfigRead += Config_OnConfigRead;
            UnifierApi.EventHub.Server.AddServer.Register(OnServerListAdded, HandlerPriority.Normal);
            UnifierApi.EventHub.Coordinator.Started.Register(OnPostInit, HandlerPriority.Normal);
            UnifierApi.EventHub.Coordinator.ServerCheckPlayerCanJoinIn.Register(OnCheckJoinIn, HandlerPriority.Higher + 1);
            UnifierApi.EventHub.Coordinator.JoinServer.Register(OnJoinServer, HandlerPriority.Higher + 1);
            On.Terraria.Main.Update += OnUpdate;
            On.OTAPI.HooksSystemContext.WorldGenSystemContext.InvokeHardmodeTileUpdate += OnHardUpdate;
            On.Terraria.WorldGenSystemContext.StartHardmode += OnStartHardMode;
            On.Terraria.WorldGenSystemContext.SpreadGrass += OnWorldGrassSpread;
            On.OTAPI.HooksSystemContext.ItemSystemContext.InvokeMechSpawn += OnItemMechSpawn;
            On.OTAPI.HooksSystemContext.NPCSystemContext.InvokeMechSpawn += OnNpcMechSpawn;
            UnifierApi.EventHub.Netplay.ReceiveFullClientInfoEvent.Register(OnJoin, HandlerPriority.Higher + 1);
            UnifierApi.EventHub.Netplay.ConnectEvent.Register(OnConnect, HandlerPriority.Higher + 1);
            UnifierApi.EventHub.Netplay.LeaveEvent.Register(OnLeave, HandlerPriority.Normal + 1);
            UnifierApi.EventHub.Chat.MessageEvent.Register(OnMessage, HandlerPriority.Higher + 1);
            NetPacketHandler.ProcessPacketEvent.Register(OnGetData, HandlerPriority.Higher + 1);
            On.Terraria.NetMessageSystemContext.greetPlayer += OnGreetPlayer;
            On.Terraria.NPC.StrikeNPC += OnStrikeNpc;
            On.Terraria.Projectile.SetDefaults += OnProjectileSetDefaults;
            On.Terraria.NetMessageSystemContext.SendData += NetMessageSystemContext_SendData;

            Hooks.PlayerHooks.PlayerPreLogin += OnPlayerPreLogin;
            Hooks.PlayerHooks.PlayerPostLogin += OnPlayerLogin;
            Hooks.AccountHooks.AccountDelete += OnAccountDelete;
            Hooks.AccountHooks.AccountCreate += OnAccountCreate;
        }

        private static void Config_OnConfigRead(ServerConfigFile<TShockSettings> file) {
            TShock.ApplyConfig();
        }


        private static void OnServerListAdded(ref ReadonlyNoCancelEventArgs<AddServer> args) {
            var server = args.Content.Server;
            var settings = TShock.Config.GetServerSettings(server.Name);
            server.NPC.defaultMaxSpawns = settings.DefaultMaximumSpawns;
            server.NPC.defaultSpawnRate = settings.DefaultSpawnRate;
            server.Main.ServerSideCharacter = TShock.ServerSideCharacterConfig.Settings.Enabled;
        }

        

        /// <summary>OnPlayerLogin - Fires the PlayerLogin hook to listening plugins.</summary>
        /// <param name="args">args - The PlayerPostLoginEventArgs object.</param>
        static void OnPlayerLogin(PlayerPostLoginEventArgs args) {
            List<string> KnownIps = new List<string>();
            if (!string.IsNullOrWhiteSpace(args.Player.Account.KnownIps)) {
                KnownIps = JsonConvert.DeserializeObject<List<string>>(args.Player.Account.KnownIps);
            }

            if (KnownIps.Count == 0) {
                KnownIps.Add(args.Player.IP);
            }
            else {
                bool last = KnownIps.Last() == args.Player.IP;
                if (!last) {
                    if (KnownIps.Count == 100) {
                        KnownIps.RemoveAt(0);
                    }

                    KnownIps.Add(args.Player.IP);
                }
            }

            args.Player.Account.KnownIps = JsonConvert.SerializeObject(KnownIps, Formatting.Indented);
            UserAccounts.UpdateLogin(args.Player.Account);

            Bans.CheckBan(args.Player);
        }

        /// <summary>OnAccountDelete - Internal hook fired on account delete.</summary>
        /// <param name="args">args - The AccountDeleteEventArgs object.</param>
        static void OnAccountDelete(Hooks.AccountDeleteEventArgs args) {
            CharacterDB.RemovePlayer(args.Account.ID);
        }

        /// <summary>OnAccountCreate - Internal hook fired on account creation.</summary>
        /// <param name="args">args - The AccountCreateEventArgs object.</param>
        static void OnAccountCreate(Hooks.AccountCreateEventArgs args) {
            CharacterDB.SeedInitialData(UserAccounts.GetUserAccount(args.Account));
        }

        /// <summary>OnPlayerPreLogin - Internal hook fired when on player pre login.</summary>
        /// <param name="args">args - The PlayerPreLoginEventArgs object.</param>
        static void OnPlayerPreLogin(Hooks.PlayerPreLoginEventArgs args) {
            if (args.Player.IsLoggedIn)
                args.Player.SaveServerCharacter();
        }

        static void OnUpdate(On.Terraria.Main.orig_Update orig, Terraria.Main self, RootContext root, GameTime gameTime) {
            if(root is ServerContext server) {
                OnUpdate(server);
            }
            orig(self, root, gameTime);
        }

        static void OnPostInit(ref ReadonlyNoCancelEventArgs<StartedEvent> args) {
            if (!Config.GlobalSettings.DisableUUIDLogin) {
                Log.Info(
                    "UUID WARNING:\r\n" +
                    GetString("Login using UUID enabled. Users automatically login via UUID.") + "\r\n" +
                    GetString("A malicious server can easily steal a user's UUID. You may consider turning this option off if you run a public server."));
            }

            // Disable the auth system if "setup.lock" is present or a user account already exists
            if (File.Exists(Path.Combine(SavePath, "setup.lock")) || (UserAccounts.GetUserAccounts().Count > 0)) {
                SetupToken = 0;

                if (File.Exists(Path.Combine(SavePath, "setup-code.txt"))) {
                    Log.Info(
                        "Setup Admin Guidance:\r\n" +
                        GetString("An account has been detected in the user database, but setup-code.txt is still present.") + "\r\n" +
                        GetString("TShock will now disable the initial setup system and remove setup-code.txt as it is no longer needed."));
                    File.Delete(Path.Combine(SavePath, "setup-code.txt"));
                }

                if (!File.Exists(Path.Combine(SavePath, "setup.lock"))) {
                    // This avoids unnecessary database work, which can get ridiculously high on old servers as all users need to be fetched
                    File.Create(Path.Combine(SavePath, "setup.lock"));
                }
            }
            else if (!File.Exists(Path.Combine(SavePath, "setup-code.txt"))) {
                var r = new Random((int)DateTime.Now.ToBinary());
                SetupToken = r.Next(100000, 10000000);
                Log.Warning(
                    "Setup Admin Guidance:\r\n" +
                    GetString("To setup the server, join the game and type {0}setup {1}", Commands.Specifier, SetupToken) + "\r\n" +
                    GetString("This token will display until disabled by verification. ({0}setup)", Commands.Specifier));
                File.WriteAllText(Path.Combine(SavePath, "setup-code.txt"), SetupToken.ToString());
            }
            else {
                SetupToken = Convert.ToInt32(File.ReadAllText(Path.Combine(SavePath, "setup-code.txt")));
                Log.Warning(
                    "Setup Admin Guidance:\r\n" +
                    GetString("TShock Notice: setup-code.txt is still present, and the code located in that file will be used.") + "\r\n" +
                    GetString("To setup the server, join the game and type {0}setup {1}", Commands.Specifier, SetupToken) + "\r\n" +
                    GetString("This token will display until disabled by verification. ({0}setup)", Commands.Specifier));
            }

            Regions.Reload();
            Warps.ReloadWarps();

            Utils.ComputeMaxStyles();
            foreach (var server in UnifiedServerCoordinator.Servers.Where(s => s.IsRunning)) {
                Utils.FixChestStacks(server);
            }
        }

        private static void OnCheckJoinIn(ref ValueEventNoCancelArgs<ServerCheckPlayerCanJoinIn> args) {
            var player = args.Content.Player;
            var server = args.Content.Server;
            var settings = Config.GetServerSettings(server.Name);
            var difficulty = player.difficulty;

            if (settings.SoftcoreOnly && difficulty != 0) {
                args.Content.FailReason = NetworkText.FromLiteral(GetString("You need to join with a softcore player."));
                args.Content.CanJoin = false;
                return;
            }
            if (settings.MediumcoreOnly && difficulty < 1) {
                args.Content.FailReason = NetworkText.FromLiteral(GetString("You need to join with a mediumcore player or higher."));
                args.Content.CanJoin = false;
                return;
            }
            if (settings.HardcoreOnly && difficulty < 2) {
                args.Content.FailReason = NetworkText.FromLiteral(GetString("You need to join with a hardcore player."));
                args.Content.CanJoin = false;
                return;
            }
        }

        private static void OnJoinServer(ref ReadonlyNoCancelEventArgs<JoinServerEvent> args) {
            TShock.Players[args.Content.Who].ReceivedInfo = true;
        }

        /// <summary>OnUpdate - Called when ever the server ticks.</summary>
        /// <param name="args">args - EventArgs args</param>
        static void OnUpdate(ServerContext server) {
            var data = server.GetExtension<TSServerData>();

            //if (Backups.IsBackupTime(server))
            //    Backups.Backup(server);
            //call these every second, not every update
            if ((DateTime.UtcNow - data.LastSecondCheck).TotalSeconds >= 1) {
                OnSecondUpdate(server);
                data.LastSecondCheck = DateTime.UtcNow;
            }

            if (server.Main.ServerSideCharacter && (DateTime.UtcNow - data.LastSave).TotalMinutes >= ServerSideCharacterConfig.Settings.ServerSideCharacterSave) {
                foreach (TSPlayer player in Players) {
                    // prevent null point exceptions
                    if (player != null && player.IsLoggedIn && !player.IsDisabledPendingTrashRemoval && player.GetCurrentServer() == server) {
                        CharacterDB.InsertPlayerData(player);
                    }
                }
                data.LastSave = DateTime.UtcNow;
            }
        }

        /// <summary>OnSecondUpdate - Called effectively every second for all time based checks.</summary>
        static void OnSecondUpdate(ServerContext server) {
            var setting = Config.GetServerSettings(server.Name);
            var serverPlr = server.GetExtension<TSServerPlayer>();

            DisableFlags flags = setting.DisableSecondUpdateLogs ? DisableFlags.WriteToConsole : DisableFlags.WriteToLogAndConsole;

            if (setting.ForceTime != "normal") {
                switch (setting.ForceTime) {
                    case "day":
                        serverPlr.SetTime(true, 27000.0);
                        break;
                    case "night":
                        serverPlr.SetTime(false, 16200.0);
                        break;
                }
            }

            foreach (TSPlayer player in Players) {
                if (player != null && player.Active) {
                    if (player.TilesDestroyed != null) {
                        if (player.TileKillThreshold >= setting.TileKillThreshold) {
                            player.Disable(GetString("Reached TileKill threshold."), flags);
                            serverPlr.RevertTiles(player.TilesDestroyed);
                            player.TilesDestroyed.Clear();
                        }
                    }
                    if (player.TileKillThreshold > 0) {
                        player.TileKillThreshold = 0;
                        //We don't want to revert the entire map in case of a disable.
                        lock (player.TilesDestroyed)
                            player.TilesDestroyed.Clear();
                    }

                    if (player.TilesCreated != null) {
                        if (player.TilePlaceThreshold >= setting.TilePlaceThreshold) {
                            player.Disable(GetString("Reached TilePlace threshold"), flags);
                            lock (player.TilesCreated) {
                                serverPlr.RevertTiles(player.TilesCreated);
                                player.TilesCreated.Clear();
                            }
                        }
                    }
                    if (player.TilePlaceThreshold > 0) {
                        player.TilePlaceThreshold = 0;
                    }

                    if (player.RecentFuse > 0)
                        player.RecentFuse--;

                    if (server.Main.ServerSideCharacter && player.initialSpawn) {
                        player.initialSpawn = false;

                        // reassert the correct spawnpoint value after the game's Spawn handler changed it
                        player.TPlayer.SpawnX = player.initialServerSpawnX;
                        player.TPlayer.SpawnY = player.initialServerSpawnY;

                        player.TeleportSpawnpoint();
                        TShock.Log.Debug(GetString("OnSecondUpdate / initial ssc spawn for {0} at ({1}, {2})", player.Name, player.TPlayer.SpawnX, player.TPlayer.SpawnY));
                    }

                    if (player.RPPending > 0) {
                        if (player.RPPending == 1) {
                            var pos = RememberedPos.GetLeavePos(server.Main.worldID.ToString(), player.Name, player.IP);
                            player.Teleport(pos.X * 16, pos.Y * 16);
                            player.RPPending = 0;
                        }
                        else {
                            player.RPPending--;
                        }
                    }

                    if (player.TileLiquidThreshold >= setting.TileLiquidThreshold) {
                        player.Disable(GetString("Reached TileLiquid threshold"), flags);
                    }
                    if (player.TileLiquidThreshold > 0) {
                        player.TileLiquidThreshold = 0;
                    }

                    if (player.ProjectileThreshold >= setting.ProjectileThreshold) {
                        player.Disable(GetString("Reached projectile threshold"), flags);
                    }
                    if (player.ProjectileThreshold > 0) {
                        player.ProjectileThreshold = 0;
                    }

                    if (player.PaintThreshold >= setting.TilePaintThreshold) {
                        player.Disable(GetString("Reached paint threshold"), flags);
                    }
                    if (player.PaintThreshold > 0) {
                        player.PaintThreshold = 0;
                    }

                    if (player.HealOtherThreshold >= setting.HealOtherThreshold) {
                        player.Disable(GetString("Reached HealOtherPlayer threshold"), flags);
                    }
                    if (player.HealOtherThreshold > 0) {
                        player.HealOtherThreshold = 0;
                    }

                    if (player.RespawnTimer > 0 && --player.RespawnTimer == 0 && player.Difficulty != 2) {
                        player.Spawn(PlayerSpawnContext.ReviveFromDeath);
                    }

                    if (!server.Main.ServerSideCharacter || (server.Main.ServerSideCharacter && player.IsLoggedIn)) {
                        if (!player.HasPermission(Permissions.ignorestackhackdetection)) {
                            player.IsDisabledForStackDetection = player.HasHackedItemStacks(shouldWarnPlayer: true);
                        }

                        if (player.IsBeingDisabled()) {
                            player.Disable(flags: flags);
                        }
                    }
                }
            }
            TShock.Bouncer.OnSecondUpdate();
            UnifierApi.UpdateTitle();
        }

        static bool OnHardUpdate(On.OTAPI.HooksSystemContext.WorldGenSystemContext.orig_InvokeHardmodeTileUpdate orig,
            OTAPI.HooksSystemContext.WorldGenSystemContext self, int x, int y, ushort type) {
            if (self.root is ServerContext server && !OnCreep(server, type)) {
                return false;
            }
            return orig(self, x, y, type);
        }

        private static void OnWorldGrassSpread(On.Terraria.WorldGenSystemContext.orig_SpreadGrass orig, WorldGenSystemContext self, int i, int j, int dirt, int grass, bool repeat, TileColorCache color) {
            if (self.root is ServerContext server && !OnCreep(server, grass)) {
                return;
            }
            orig(self, i, j, dirt, grass, repeat, color);
        }

        /// <summary>
        /// Checks if the tile type is allowed to creep
        /// </summary>
        /// <param name="tileType">Tile id</param>
        /// <returns>True if allowed, otherwise false</returns>
        static bool OnCreep(ServerContext server, int tileType) {
            if (server.WorldGen.generatingWorld) {
                return true;
            }

            var setting = Config.GetServerSettings(server.Name);
            if (!setting.AllowCrimsonCreep && (tileType == TileID.Dirt || tileType == TileID.CrimsonGrass
                || TileID.Sets.Crimson[tileType])) {
                return false;
            }

            if (!setting.AllowCorruptionCreep && (tileType == TileID.Dirt || tileType == TileID.CorruptThorns
                || TileID.Sets.Corrupt[tileType])) {
                return false;
            }

            if (!setting.AllowHallowCreep && (TileID.Sets.Hallow[tileType])) {
                return false;
            }

            return true;
        }


        private static bool OnNpcMechSpawn(On.OTAPI.HooksSystemContext.NPCSystemContext.orig_InvokeMechSpawn orig,
            OTAPI.HooksSystemContext.NPCSystemContext self, bool result, float x, float y, int type, int num, int num2, int num3) {
            if (self.root is ServerContext server && OnStatueSpawn(server, num, num2, num3, (int)x / 16, (int)y / 16, type, false)) {
                return false;
            }
            return orig(self, result, x, y, type, num, num2, num3);
        }

        private static bool OnItemMechSpawn(On.OTAPI.HooksSystemContext.ItemSystemContext.orig_InvokeMechSpawn orig,
            OTAPI.HooksSystemContext.ItemSystemContext self, bool result, float x, float y, int type, int num, int num2, int num3) {
            if (self.root is ServerContext server && OnStatueSpawn(server, num, num2, num3, (int)x / 16, (int)y / 16, type, true)) {
                return false;
            }
            return orig(self, result, x, y, type, num, num2, num3);
        }
        /// <summary>OnStatueSpawn - Fired when a statue spawns.</summary>
        /// <param name="args">args - The StatueSpawnEventArgs object.</param>
        static bool OnStatueSpawn(ServerContext server, int within200, int within600, int worldWide, int x, int y, int type, bool npc) {
            var setting = Config.GetServerSettings(server.Name);
            if (within200 < setting.StatueSpawn200 && within600 < setting.StatueSpawn600 && worldWide < setting.StatueSpawnWorld) {
                return true;
            }
            else {
                return false;
            }
        }
        private static void OnConnect(ref ReadonlyEventArgs<Connect> args) {
            var client = args.Content.Client;
            var sender = UnifiedServerCoordinator.clientSenders[client.Id];
            if (Utils.GetActivePlayerCount() + 1 > Config.GlobalSettings.MaxSlots + Config.GlobalSettings.ReservedSlots) {
                sender.Kick(NetworkText.FromLiteral(Config.GlobalSettings.ServerFullNoReservedReason), false);
                args.Handled = true;
                return;
            }

            var ip = Utils.GetRealIP(client.Socket.GetRemoteAddress().ToString());
            if (!FileTools.OnWhitelist(ip)) {
                sender.Kick(NetworkText.FromLiteral(Config.GlobalSettings.WhitelistKickReason), false);
                args.Handled = true;
                return;
            }

            if (Geo != null) {
                var code = Geo.TryGetCountryCode(IPAddress.Parse(ip));
                if (code == "A1") {
                    if (Config.GlobalSettings.KickProxyUsers) {
                        sender.Kick(NetworkText.FromLiteral(GetString("Connecting via a proxy is not allowed.")), false);
                        args.Handled = true;
                        return;
                    }
                }
            }
        }


        private static void OnJoin(ref ReadonlyEventArgs<ReceiveFullClientInfo> args) {
            var client = args.Content.Client;
            var player = new TSPlayer(client.Id);
            Players[player.Index] = player;

            var ip = Utils.GetRealIP(client.Socket.GetRemoteAddress().ToString()!);
            player.IP = ip;
            if (Geo != null) {
                var code = Geo.TryGetCountryCode(IPAddress.Parse(ip));
                player.Country = code == null ? "N/A" : GeoIPCountry.GetCountryNameByCode(code);
            }

            if (Bans.CheckBan(player)) {
                args.StopPropagation = true;
                return;
            }

            if (Config.GlobalSettings.KickEmptyUUID && String.IsNullOrWhiteSpace(player.UUID)) {
                player.Kick(GetString("Your client sent a blank UUID. Configure it to send one or use a different client."), true, true, null, false);
                args.StopPropagation = true;
                return;
            }

            if (Bans.CheckBan(player))
                return;
        }


        private static void OnLeave(ref ReadonlyNoCancelEventArgs<LeaveEvent> args) {
            var who = args.Content.Who;
            if (who >= Players.Length || who < 0) {
                //Something not right has happened
                return;
            }

            var tsplr = Players[who];
            if (tsplr == null) {
                return;
            }
            Players[who] = null;
            var server = tsplr.GetCurrentServer();
            if (server is null) {
                return;
            }

            //Reset toggle creative powers to default, preventing potential power transfer & desync on another user occupying this slot later.

            foreach (var kv in server.CreativePowerManager._powersById) {
                var power = kv.Value;

                //No need to reset sliders - those are reset manually by the game, most likely an oversight that toggles don't receive this treatment.

                if (power is CreativePowers.APerPlayerTogglePower toggle) {
                    if (toggle._perPlayerIsEnabled[who] == toggle._defaultToggleState)
                        continue;

                    toggle.SetEnabledState(server, who, toggle._defaultToggleState);
                }
            }
            var settings = Config.GetServerSettings(server.Name);

            if (tsplr.ReceivedInfo) {
                if (!tsplr.SilentKickInProgress && tsplr.State >= (int)ConnectionState.RequestingWorldData && tsplr.FinishedHandshake) //The player has left, do not broadcast any clients exploiting the behaviour of not spawning their player.
                    Utils.Broadcast(server, GetString("{0} has left.", tsplr.Name), Color.Yellow);
                Log.Info(GetString("{0} disconnected.", tsplr.Name));

                if (tsplr.IsLoggedIn && !tsplr.IsDisabledPendingTrashRemoval && server.Main.ServerSideCharacter && (!tsplr.Dead || tsplr.TPlayer.difficulty != 2)) {
                    tsplr.PlayerData.CopyCharacter(tsplr);
                    CharacterDB.InsertPlayerData(tsplr);
                }

                if (settings.RememberLeavePos && !tsplr.LoginHarassed) {
                    RememberedPos.InsertLeavePos(server.Main.worldID.ToString(), tsplr.Name, tsplr.IP, (int)(tsplr.X / 16), (int)(tsplr.Y / 16));
                }

                if (tsplr.tempGroupTimer != null) {
                    tsplr.tempGroupTimer.Stop();
                }
            }

            tsplr.FinishedHandshake = false;

            // Fire the OnPlayerLogout hook too, if the player was logged in and they have a TSPlayer object.
            if (tsplr.IsLoggedIn) {
                Hooks.PlayerHooks.OnPlayerLogout(tsplr);
            }

            // If this is the last player online, update the console title and save the world if needed
            if (Utils.GetActivePlayerCount() == 0) {
                if (settings.SaveWorldOnLastPlayerExit)
                    SaveManager.SaveWorld(server);
                UnifierApi.UpdateTitle(empty: true);
            }
        }
        private static void OnMessage(ref ReadonlyEventArgs<MessageEvent> args) {
            if (args.Content.Sender.IsClient) {
                var tsplr = Players[args.Content.Sender.UserId];
                if (tsplr == null) {
                    args.Handled = true;
                    return;
                }

                if (!tsplr.FinishedHandshake) {
                    tsplr.Kick(GetString("Your client didn't send the right connection information."), true, true);
                    args.Handled = true;
                    return;
                }

                if (args.Content.RawText.Length > 500) {
                    tsplr.Kick(GetString("Crash attempt via long chat packet."), true);
                    args.Handled = true;
                    return;
                }
            }

            string text = args.Content.Text.Trim();
            bool isCmd = false;
            if (args.Content.Sender.IsServer) {
                if (!text.StartsWith(Config.GlobalSettings.CommandSpecifier) && !text.StartsWith(Config.GlobalSettings.CommandSilentSpecifier)) {
                    text = "/" + text;
                }
                isCmd = true;
            }
            else if ((text.StartsWith(Config.GlobalSettings.CommandSpecifier) || text.StartsWith(Config.GlobalSettings.CommandSilentSpecifier))
                    && !string.IsNullOrWhiteSpace(text[1..])) {
                isCmd = true;
            }

            if (isCmd) {
                var sender = args.Content.Sender;
                var executor = new CommandExecutor(sender.SourceServer, sender.UserId);
                try {
                    args.Handled = true;
                    if (!Commands.HandleCommand(executor, text)) {
                        // This is required in case anyone makes HandleCommand return false again
                        if (executor.IsClient) {
                            executor.Player.SendErrorMessage(GetString("Unable to parse command. Please contact an administrator for assistance."));
                        }
                        Log.Error(GetString("Unable to parse command '{0}' from player {1}.", text, executor.Name));
                    }
                }
                catch (Exception ex) {
                    executor.SendErrorMessage(GetString("An exception occurred executing a command."));
                    executor.LogError(ex.ToString());
                }
            }
            else if (args.Content.Sender.IsClient) {
                var tsplr = Players[args.Content.Sender.UserId];
                var server = tsplr.GetCurrentServer();
                var settings = Config.GetServerSettings(server.Name);
                if (!tsplr.HasPermission(Permissions.canchat)) {
                    args.Handled = true;
                    args.StopPropagation = true;
                }
                else if (tsplr.mute) {
                    tsplr.SendErrorMessage(GetString("You are muted!"));
                    args.Handled = true;
                    args.StopPropagation = true;
                }
                else if (!settings.EnableChatAboveHeads) {
                    text = string.Format(settings.ChatFormat, tsplr.Group.Name, tsplr.Group.Prefix, tsplr.Name, tsplr.Group.Suffix,
                                             args.Content.Text);

                    //Invoke the PlayerChat hook. If this hook event is handled then we need to prevent sending the chat message
                    bool cancelChat = PlayerHooks.OnPlayerChat(tsplr, args.Content.Text, ref text);
                    args.Handled = true;

                    if (cancelChat) {
                        return;
                    }

                    server.GetExtension<TSServerPlayer>().BCMessage(text, tsplr.Group.R, tsplr.Group.G, tsplr.Group.B);
                }
                else {
                    var who = args.Content.Sender.UserId;
                    Player ply = tsplr.TPlayer;
                    string name = ply.name;
                    ply.name = string.Format(settings.ChatAboveHeadsFormat, tsplr.Group.Name, tsplr.Group.Prefix, tsplr.Name, tsplr.Group.Suffix);
                    //Update the player's name to format text nicely. This needs to be done because Terraria automatically formats messages against our will
                    server.NetMessage.SendData((int)PacketTypes.PlayerInfo, -1, -1, NetworkText.FromLiteral(ply.name), who);

                    //Give that poor player their name back :'c
                    ply.name = name;

                    bool cancelChat = PlayerHooks.OnPlayerChat(tsplr, args.Content.Text, ref text);
                    if (cancelChat) {
                        args.Handled = true;
                        return;
                    }

                    //This netpacket is used to send chat text from the server to clients, in this case on behalf of a client
                    Terraria.Net.NetPacket packet = Terraria.GameContent.NetModules.NetTextModule.SerializeServerMessage(
                        server, NetworkText.FromLiteral(text), new Color(tsplr.Group.R, tsplr.Group.G, tsplr.Group.B), who
                    );
                    //Broadcast to everyone except the player who sent the message.
                    //This is so that we can send them the same nicely formatted message that everyone else gets
                    server.NetManager.Broadcast(packet, who);

                    //Reset their name
                    server.NetMessage.SendData((int)PacketTypes.PlayerInfo, -1, -1, NetworkText.FromLiteral(name), who);

                    string msg = String.Format("<{0}> {1}",
                        String.Format(settings.ChatAboveHeadsFormat, tsplr.Group.Name, tsplr.Group.Prefix, tsplr.Name, tsplr.Group.Suffix),
                        text
                    );

                    //Send the original sender their nicely formatted message, and do all the loggy things
                    tsplr.SendMessage(msg, tsplr.Group.R, tsplr.Group.G, tsplr.Group.B);
                    server.GetExtension<TSServerPlayer>().SendMessage(msg, tsplr.Group.R, tsplr.Group.G, tsplr.Group.B);
                    args.Handled = true;
                }
            }
        }

        private static void OnGetData(ref ReadonlyEventArgs<ProcessPacketEvent> args) {
            if (args.Content.EventType is not ProcessPacketEventType.BeforeOriginalProcess) {
                return;
            }
            var type = (PacketTypes)args.Content.RawData[0];

            var player = TShock.Players[args.Content.RecieveFrom.ID];
            if (player == null || !player.ConnectionAlive) {
                args.Handled = true;
                args.StopPropagation = true;
                return;
            }

            if ((player.State < (int)ConnectionState.Complete || player.Dead) && (int)type > 12 && (int)type != 16 && (int)type != 42 && (int)type != 50 &&
                (int)type != 38 && (int)type != 21 && (int)type != 22 && type != PacketTypes.SyncLoadout) {
                args.Handled = true;
                args.StopPropagation = true;
                return;
            }
        }

        private static void OnGreetPlayer(On.Terraria.NetMessageSystemContext.orig_greetPlayer orig, NetMessageSystemContext self, int plr) {
            var player = TShock.Players[plr];
            if (player == null) {
                return;
            }
            var server = player.GetCurrentServer();
            var serverPlr = server.GetExtension<TSServerPlayer>();
            var setting = Config.GetServerSettings(server.Name);

            player.LoginMS = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

            if (Config.GlobalSettings.EnableGeoIP && TShock.Geo != null) {
                Log.Info(GetString("{0} ({1}) from '{2}' group from '{3}' joined. ({4}/{5})", player.Name, player.IP,
                                       player.Group.Name, player.Country, Utils.GetActivePlayerCount(),
                                       TShock.Config.GlobalSettings.MaxSlots));
                if (!player.SilentJoinInProgress && player.FinishedHandshake)
                    server.ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(GetString("{0} ({1}) has joined.", player.Name, player.Country)), Color.Yellow);
            }
            else {
                Log.Info(GetString("{0} ({1}) from '{2}' group joined. ({3}/{4})", player.Name, player.IP,
                                       player.Group.Name, Utils.GetActivePlayerCount(), TShock.Config.GlobalSettings.MaxSlots));
                if (!player.SilentJoinInProgress && player.FinishedHandshake)
                    server.ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(GetString("{0} has joined.", player.Name)), Color.Yellow);
            }

            if (Config.GlobalSettings.DisplayIPToAdmins) {
                Utils.SendLogs(server, GetString("{0} has joined. IP: {1}", player.Name, player.IP), Color.Blue);
            }

            player.SendFileTextAsMessage(FileTools.MotdPath);

            string pvpMode = setting.PvPMode.ToLowerInvariant();
            if (pvpMode == "always" || pvpMode == "pvpwithnoteam") {
                player.TPlayer.hostile = true;
                player.SendData(PacketTypes.TogglePvp, "", player.Index);
                server.NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, null, player.Index);
            }

            if (!player.IsLoggedIn) {
                if (server.Main.ServerSideCharacter) {
                    player.IsDisabledForSSC = true;
                    player.SendErrorMessage(GetString("Server side characters is enabled! Please {0}register or {0}login to play!", Commands.Specifier));
                    player.LoginHarassed = true;
                }
                else if (Config.GlobalSettings.RequireLogin) {
                    player.SendErrorMessage(GetString("Please {0}register or {0}login to play!", Commands.Specifier));
                    player.LoginHarassed = true;
                }
            }

            player.LastNetPosition = new Vector2(server.Main.spawnTileX * 16f, server.Main.spawnTileY * 16f);

            if (setting.RememberLeavePos && (RememberedPos.GetLeavePos(server.Main.worldID.ToString(), player.Name, player.IP) != Vector2.Zero) && !player.LoginHarassed) {
                player.RPPending = 3;
                player.SendInfoMessage(GetString("You will be teleported to your last known location..."));
            }
        }

        private static double OnStrikeNpc(On.Terraria.NPC.orig_StrikeNPC orig, NPC self, RootContext root, int Damage, float knockBack, int hitDirection, bool crit, bool noEffect, bool fromNet, int owner, Entity entity) {
            var dmg = orig(self, root, Damage, knockBack, hitDirection, crit, noEffect, fromNet, owner, entity);
            if (root is ServerContext server && TShock.Config.GetServerSettings(server.Name).InfiniteInvasion) {
                if (server.Main.invasionSize < 10) {
                    server.Main.invasionSize = 20000000;
                }
            }
            return dmg;
        }
        private static void OnProjectileSetDefaults(On.Terraria.Projectile.orig_SetDefaults orig, Projectile self, RootContext root, int Type) {
            orig(self, root, Type);
            if (root is not ServerContext server) {
                return;
            }
            var setting = Config.GetServerSettings(server.Name);
            //tombstone fix.
            if (Type == ProjectileID.Tombstone || (Type >= ProjectileID.GraveMarker && Type <= ProjectileID.Obelisk) || (Type >= ProjectileID.RichGravestone1 && Type <= ProjectileID.RichGravestone5))
                if (setting.DisableTombstones)
                    self.SetDefaults(server, 0);
            if (Type == ProjectileID.HappyBomb)
                if (setting.DisableClownBombs)
                    self.SetDefaults(server, 0);
            if (Type == ProjectileID.SnowBallHostile)
                if (setting.DisableSnowBalls)
                    self.SetDefaults(server, 0);
            if (Type == ProjectileID.BombSkeletronPrime)
                if (setting.DisablePrimeBombs)
                    self.SetDefaults(server, 0);
        }
        private static void NetMessageSystemContext_SendData(On.Terraria.NetMessageSystemContext.orig_SendData orig, NetMessageSystemContext self, int msgType, int remoteClient, int ignoreClient, NetworkText text, int number, float number2, float number3, float number4, int number5, int number6, int number7) {
            if (msgType == (int)PacketTypes.PlayerHp) {
                if (self.root.Main.player[number].statLife <= 0) {
                    return;
                }
            }
            if (msgType == (int)PacketTypes.ProjectileNew) {
                if (self.root is ServerContext server && number >= 0 && number < Main.maxProjectiles) {
                    var projectile = server.Main.projectile[number];
                    if (projectile.active && projectile.owner >= 0 &&
                        (Bouncer.projectileCreatesLiquid.ContainsKey(projectile.type) || Bouncer.projectileCreatesTile.ContainsKey(projectile.type))) {
                        var player = Players[projectile.owner];
                        if (player != null) {
                            if (player.RecentlyCreatedProjectiles.Any(p => p.Index == number && p.Killed)) {
                                player.RecentlyCreatedProjectiles.RemoveAll(p => p.Index == number && p.Killed);
                            }

                            if (!player.RecentlyCreatedProjectiles.Any(p => p.Index == number)) {
                                player.RecentlyCreatedProjectiles.Add(new ProjectileStruct() {
                                    Index = number,
                                    Type = (short)projectile.type,
                                    CreatedAt = DateTime.Now
                                });
                            }
                        }
                    }
                }
            }
            orig(self, msgType, remoteClient, ignoreClient, text, number, number2, number3, number4, number5, number6, number7);
        }
        private static void OnStartHardMode(On.Terraria.WorldGenSystemContext.orig_StartHardmode orig, WorldGenSystemContext self, bool force) {
            if (self.root is ServerContext server && Config.GetServerSettings(server.Name).DisableHardmode) {
                return;
            }
            orig(self, force);
        }
    }
}
