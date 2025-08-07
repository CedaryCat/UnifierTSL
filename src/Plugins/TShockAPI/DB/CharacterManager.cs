using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using LinqToDB.SqlQuery;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TShockAPI.DB
{
    public class CharacterManager
    {
        [Table(Name = "tsCharacter")]
        private class Character
        {
            [Column(Name = "Account"), PrimaryKey]
            public int AccountId { get; set; }
            [Column] public int Health { get; set; }
            [Column] public int MaxHealth { get; set; }
            [Column] public int Mana { get; set; }
            [Column] public int MaxMana { get; set; }
            [Column, NotNull] public required string Inventory { get; set; }
            [Column] public int? extraSlot { get; set; }
            [Column] public int spawnX { get; set; }
            [Column] public int spawnY { get; set; }
            [Column] public int? skinVariant { get; set; }
            [Column] public int? hair { get; set; }
            [Column] public int hairDye { get; set; }
            [Column] public int? hairColor { get; set; }
            [Column] public int? pantsColor { get; set; }
            [Column] public int? shirtColor { get; set; }
            [Column] public int? underShirtColor { get; set; }
            [Column] public int? shoeColor { get; set; }
            [Column] public int? hideVisuals { get; set; }
            [Column] public int? skinColor { get; set; }
            [Column] public int? eyeColor { get; set; }
            [Column] public int questsCompleted { get; set; }
            [Column] public int usingBiomeTorches { get; set; }
            [Column] public int happyFunTorchTime { get; set; }
            [Column] public int unlockedBiomeTorches { get; set; }
            [Column] public int currentLoadoutIndex { get; set; }
            [Column] public int ateArtisanBread { get; set; }
            [Column] public int usedAegisCrystal { get; set; }
            [Column] public int usedAegisFruit { get; set; }
            [Column] public int usedArcaneCrystal { get; set; }
            [Column] public int usedGalaxyPearl { get; set; }
            [Column] public int usedGummyWorm { get; set; }
            [Column] public int usedAmbrosia { get; set; }
            [Column] public int unlockedSuperCart { get; set; }
            [Column] public int enabledSuperCart { get; set; }
        }
        public DataConnection database;
        ITable<Character> characterTable;

        public CharacterManager(DataConnection db) {
            database = db;
            characterTable = db.CreateTable<Character>(tableOptions: TableOptions.CreateIfNotExists);
        }
        public PlayerData GetPlayerData(TSPlayer player, int acctid) {
            var playerData = new PlayerData(true);

            try {
                var character = characterTable.FirstOrDefault(c => c.AccountId == acctid);

                if (character != null) {
                    playerData.exists = true;
                    playerData.health = character.Health;
                    playerData.maxHealth = character.MaxHealth;
                    playerData.mana = character.Mana;
                    playerData.maxMana = character.MaxMana;

                    List<NetItem> inventory = character.Inventory.Split('~')
                        .Select(NetItem.Parse).ToList();

                    if (inventory.Count < NetItem.MaxInventory) {
                        inventory.InsertRange(67, new NetItem[2]);
                        inventory.InsertRange(77, new NetItem[2]);
                        inventory.InsertRange(87, new NetItem[2]);
                        inventory.AddRange(new NetItem[NetItem.MaxInventory - inventory.Count]);
                    }
                    playerData.inventory = inventory.ToArray();

                    playerData.extraSlot = character.extraSlot;
                    playerData.spawnX = character.spawnX;
                    playerData.spawnY = character.spawnY;
                    playerData.skinVariant = character.skinVariant;
                    playerData.hair = character.hair;
                    playerData.hairDye = (byte)character.hairDye;
                    playerData.hairColor = Utils.DecodeColor(character.hairColor);
                    playerData.pantsColor = Utils.DecodeColor(character.pantsColor);
                    playerData.shirtColor = Utils.DecodeColor(character.shirtColor);
                    playerData.underShirtColor = Utils.DecodeColor(character.underShirtColor);
                    playerData.shoeColor = Utils.DecodeColor(character.shoeColor);
                    playerData.hideVisuals = Utils.DecodeBoolArray(character.hideVisuals);
                    playerData.skinColor = Utils.DecodeColor(character.skinColor);
                    playerData.eyeColor = Utils.DecodeColor(character.eyeColor);
                    playerData.questsCompleted = character.questsCompleted;
                    playerData.usingBiomeTorches = character.usingBiomeTorches;
                    playerData.happyFunTorchTime = character.happyFunTorchTime;
                    playerData.unlockedBiomeTorches = character.unlockedBiomeTorches;
                    playerData.currentLoadoutIndex = character.currentLoadoutIndex;
                    playerData.ateArtisanBread = character.ateArtisanBread;
                    playerData.usedAegisCrystal = character.usedAegisCrystal;
                    playerData.usedAegisFruit = character.usedAegisFruit;
                    playerData.usedArcaneCrystal = character.usedArcaneCrystal;
                    playerData.usedGalaxyPearl = character.usedGalaxyPearl;
                    playerData.usedGummyWorm = character.usedGummyWorm;
                    playerData.usedAmbrosia = character.usedAmbrosia;
                    playerData.unlockedSuperCart = character.unlockedSuperCart;
                    playerData.enabledSuperCart = character.enabledSuperCart;
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }

            return playerData;
        }
        public bool SeedInitialData(UserAccount account) {
            var items = new List<NetItem>(TShock.ServerSideCharacterConfig.Settings.StartingInventory);
            if (items.Count < NetItem.MaxInventory)
                items.AddRange(new NetItem[NetItem.MaxInventory - items.Count]);

            string initialItems = string.Join("~", items.Take(NetItem.MaxInventory));

            try {
                database.Insert(new Character {
                    AccountId = account.ID,
                    Health = TShock.ServerSideCharacterConfig.Settings.StartingHealth,
                    MaxHealth = TShock.ServerSideCharacterConfig.Settings.StartingHealth,
                    Mana = TShock.ServerSideCharacterConfig.Settings.StartingMana,
                    MaxMana = TShock.ServerSideCharacterConfig.Settings.StartingMana,
                    Inventory = initialItems,
                    spawnX = -1,
                    spawnY = -1,
                    questsCompleted = 0
                });
                return true;
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
                return false;
            }
        }
        public bool InsertPlayerData(TSPlayer player, bool fromCommand = false) {
            if (!player.IsLoggedIn || player.State < (int)ConnectionState.Complete)
                return false;

            if (player.HasPermission(Permissions.bypassssc) && !fromCommand) {
                TShock.Log.Info(GetParticularString("{0} is a player name",
                    $"Skipping SSC save (due to tshock.ignore.ssc) for {player.Account.Name}"));
                return false;
            }

            var playerData = player.PlayerData;
            var character = new Character {
                AccountId = player.Account.ID,
                Health = playerData.health,
                MaxHealth = playerData.maxHealth,
                Mana = playerData.mana,
                MaxMana = playerData.maxMana,
                Inventory = string.Join("~", playerData.inventory),
                extraSlot = playerData.extraSlot,
                spawnX = player.TPlayer.SpawnX,
                spawnY = player.TPlayer.SpawnY,
                skinVariant = player.TPlayer.skinVariant,
                hair = player.TPlayer.hair,
                hairDye = player.TPlayer.hairDye,
                hairColor = Utils.EncodeColor(player.TPlayer.hairColor),
                pantsColor = Utils.EncodeColor(player.TPlayer.pantsColor),
                shirtColor = Utils.EncodeColor(player.TPlayer.shirtColor),
                underShirtColor = Utils.EncodeColor(player.TPlayer.underShirtColor),
                shoeColor = Utils.EncodeColor(player.TPlayer.shoeColor),
                hideVisuals = Utils.EncodeBoolArray(player.TPlayer.hideVisibleAccessory),
                skinColor = Utils.EncodeColor(player.TPlayer.skinColor),
                eyeColor = Utils.EncodeColor(player.TPlayer.eyeColor),
                questsCompleted = player.TPlayer.anglerQuestsFinished,
                usingBiomeTorches = player.TPlayer.UsingBiomeTorches ? 1 : 0,
                happyFunTorchTime = player.TPlayer.happyFunTorchTime ? 1 : 0,
                unlockedBiomeTorches = player.TPlayer.unlockedBiomeTorches ? 1 : 0,
                currentLoadoutIndex = player.TPlayer.CurrentLoadoutIndex,
                ateArtisanBread = player.TPlayer.ateArtisanBread ? 1 : 0,
                usedAegisCrystal = player.TPlayer.usedAegisCrystal ? 1 : 0,
                usedAegisFruit = player.TPlayer.usedAegisFruit ? 1 : 0,
                usedArcaneCrystal = player.TPlayer.usedArcaneCrystal ? 1 : 0,
                usedGalaxyPearl = player.TPlayer.usedGalaxyPearl ? 1 : 0,
                usedGummyWorm = player.TPlayer.usedGummyWorm ? 1 : 0,
                usedAmbrosia = player.TPlayer.usedAmbrosia ? 1 : 0,
                unlockedSuperCart = player.TPlayer.unlockedSuperCart ? 1 : 0,
                enabledSuperCart = player.TPlayer.enabledSuperCart ? 1 : 0
            };

            try {
                if (!GetPlayerData(player, player.Account.ID).exists) {
                    database.Insert(character);
                }
                else {
                    database.Update(character);
                }
                return true;
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
                return false;
            }
        }

        public bool RemovePlayer(int userid) {
            try {
                database.GetTable<Character>()
                    .Where(c => c.AccountId == userid)
                    .Delete();
                return true;
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
                return false;
            }
        }

        public bool InsertSpecificPlayerData(TSPlayer player, PlayerData data) {
            if (!player.IsLoggedIn)
                return false;

            if (player.HasPermission(Permissions.bypassssc)) {
                TShock.Log.Info(GetParticularString("{0} is a player name",
                    $"Skipping SSC save (due to tshock.ignore.ssc) for {player.Account.Name}"));
                return true;
            }

            var character = new Character {
                AccountId = player.Account.ID,
                Health = data.health,
                MaxHealth = data.maxHealth,
                Mana = data.mana,
                MaxMana = data.maxMana,
                Inventory = string.Join("~", data.inventory),
                extraSlot = data.extraSlot,
                spawnX = data.spawnX,
                spawnY = data.spawnY,
                skinVariant = data.skinVariant,
                hair = data.hair,
                hairDye = data.hairDye,
                hairColor = Utils.EncodeColor(data.hairColor),
                pantsColor = Utils.EncodeColor(data.pantsColor),
                shirtColor = Utils.EncodeColor(data.shirtColor),
                underShirtColor = Utils.EncodeColor(data.underShirtColor),
                shoeColor = Utils.EncodeColor(data.shoeColor),
                hideVisuals = Utils.EncodeBoolArray(data.hideVisuals),
                skinColor = Utils.EncodeColor(data.skinColor),
                eyeColor = Utils.EncodeColor(data.eyeColor),
                questsCompleted = data.questsCompleted,
                usingBiomeTorches = data.usingBiomeTorches,
                happyFunTorchTime = data.happyFunTorchTime,
                unlockedBiomeTorches = data.unlockedBiomeTorches,
                currentLoadoutIndex = data.currentLoadoutIndex,
                ateArtisanBread = data.ateArtisanBread,
                usedAegisCrystal = data.usedAegisCrystal,
                usedAegisFruit = data.usedAegisFruit,
                usedArcaneCrystal = data.usedArcaneCrystal,
                usedGalaxyPearl = data.usedGalaxyPearl,
                usedGummyWorm = data.usedGummyWorm,
                usedAmbrosia = data.usedAmbrosia,
                unlockedSuperCart = data.unlockedSuperCart,
                enabledSuperCart = data.enabledSuperCart
            };

            try {
                if (!GetPlayerData(player, player.Account.ID).exists) {
                    database.Insert(character);
                }
                else {
                    database.Update(character);
                }
                return true;
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
                return false;
            }
        }
    }
}
