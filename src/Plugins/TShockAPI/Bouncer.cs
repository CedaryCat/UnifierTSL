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
using OTAPI;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ObjectData;
using TrProtocol.Models;
using TrProtocol.NetPackets;
using TrProtocol.NetPackets.Modules;
using TShockAPI.Extension;
using TShockAPI.Localization;
using UnifierTSL;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Extensions;
using UnifierTSL.Servers;

namespace TShockAPI
{
	/// <summary>Bouncer is the TShock anti-hack and anti-cheat system.</summary>
	internal sealed class Bouncer
	{

        /// <summary>
        /// The maximum place styles for each tile.
        /// </summary>
        public static Dictionary<int, int> MaxPlaceStyles = new Dictionary<int, int>();

        /// <summary>
        /// Tiles that can be broken without any pickaxes/etc.
        /// </summary>
        internal static int[] breakableTiles = new int[]
        {
            TileID.Books,
            TileID.Bottles,
            TileID.BreakableIce,
            TileID.Candles,
            TileID.CorruptGrass,
            TileID.Dirt,
            TileID.CrimsonGrass,
            TileID.Grass,
            TileID.HallowedGrass,
            TileID.MagicalIceBlock,
            TileID.Mannequin,
            TileID.Torches,
            TileID.WaterCandle,
            TileID.Womannequin,
        };

        /// <summary>
        /// List of Fishing rod item IDs.
        /// </summary>
        internal static readonly List<int> FishingRodItemIDs = new List<int>()
        {
            ItemID.WoodFishingPole,
            ItemID.ReinforcedFishingPole,
            ItemID.FiberglassFishingPole,
            ItemID.FisherofSouls,
            ItemID.GoldenFishingRod,
            ItemID.MechanicsRod,
            ItemID.SittingDucksFishingRod,
            ItemID.Fleshcatcher,
            ItemID.HotlineFishingHook,
            ItemID.BloodFishingRod,
            ItemID.ScarabFishingRod
        };

        /// <summary>
        /// List of NPC IDs that can be fished out by the player.
        /// </summary>
        internal static readonly List<int> FishableNpcIDs = new List<int>()
        {
            NPCID.EyeballFlyingFish,
            NPCID.ZombieMerman,
            NPCID.GoblinShark,
            NPCID.BloodEelHead,
            NPCID.BloodEelBody,
            NPCID.BloodEelTail,
            NPCID.BloodNautilus,
            NPCID.DukeFishron,
            NPCID.TownSlimeRed
        };

        /// <summary>
        /// These projectiles create tiles on death.
        /// </summary>
        internal static Dictionary<int, int> projectileCreatesTile = new Dictionary<int, int>
        {
            { ProjectileID.DirtBall, TileID.Dirt },
            { ProjectileID.MudBallPlayer, TileID.Mud },
            { ProjectileID.SandBallGun, TileID.Sand },
            { ProjectileID.EbonsandBallGun, TileID.Ebonsand },
            { ProjectileID.PearlSandBallGun, TileID.Pearlsand },
            { ProjectileID.CrimsandBallGun, TileID.Crimsand },
            { ProjectileID.MysticSnakeCoil, TileID.MysticSnakeRope },
            { ProjectileID.RopeCoil, TileID.Rope },
            { ProjectileID.SilkRopeCoil, TileID.SilkRope },
            { ProjectileID.VineRopeCoil, TileID.VineRope },
            { ProjectileID.WebRopeCoil, TileID.WebRope }
        };

        internal static List<int> CoilTileIds = new List<int>()
        {
            TileID.MysticSnakeRope,
            TileID.Rope,
            TileID.SilkRope,
            TileID.VineRope,
            TileID.WebRope
        };

        /// <summary>
        /// LiquidType - supported liquid types
        /// </summary>
        public enum LiquidType : byte
        {
            Water = 0,
            Lava = 1,
            Honey = 2,
            Shimmer = 3,
            Removal = 255 //@Olink: lets hope they never invent 255 fluids or decide to also use this :(
        }

        internal static Dictionary<int, LiquidType> projectileCreatesLiquid = new Dictionary<int, LiquidType>
        {
            {ProjectileID.LavaBomb, LiquidType.Lava},
            {ProjectileID.LavaRocket, LiquidType.Lava },
            {ProjectileID.LavaGrenade, LiquidType.Lava },
            {ProjectileID.LavaMine, LiquidType.Lava },
			//{ProjectileID.LavaSnowmanRocket, LiquidType.Lava }, //these require additional checks.
			{ProjectileID.WetBomb, LiquidType.Water},
            {ProjectileID.WetRocket, LiquidType.Water },
            {ProjectileID.WetGrenade, LiquidType.Water},
            {ProjectileID.WetMine, LiquidType.Water},
			//{ProjectileID.WetSnowmanRocket, LiquidType.Water}, //these require additional checks.
			{ProjectileID.HoneyBomb, LiquidType.Honey},
            {ProjectileID.HoneyRocket, LiquidType.Honey },
            {ProjectileID.HoneyGrenade, LiquidType.Honey },
            {ProjectileID.HoneyMine, LiquidType.Honey },
			//{ProjectileID.HoneySnowmanRocket, LiquidType.Honey }, //these require additional checks.
			{ProjectileID.DryBomb, LiquidType.Removal },
            {ProjectileID.DryRocket, LiquidType.Removal },
            {ProjectileID.DryGrenade, LiquidType.Removal },
            {ProjectileID.DryMine, LiquidType.Removal },
			//{ProjectileID.DrySnowmanRocket, LiquidType.Removal } //these require additional checks.
		};

        internal static Dictionary<int, int> ropeCoilPlacements = new Dictionary<int, int>
        {
            {ItemID.RopeCoil, TileID.Rope},
            {ItemID.SilkRopeCoil, TileID.SilkRope},
            {ItemID.VineRopeCoil, TileID.VineRope},
            {ItemID.WebRopeCoil, TileID.WebRope}
        };

        /// <summary>
        /// Extra place style limits for strange hardcoded values in Terraria
        /// </summary>
        internal static Dictionary<int, int> ExtraneousPlaceStyles = new Dictionary<int, int>
        {
            {TileID.Presents, 6},
            {TileID.Explosives, 1},
            {TileID.MagicalIceBlock, 0},
            {TileID.Crystals, 17},
            {TileID.MinecartTrack, 3}
        };

        internal Handlers.SendTileRectHandler STSHandler { get; private set; }
		internal Handlers.NetModules.AmbienceHandler AmbienceHandler { get; private set; }
		internal Handlers.NetModules.BestiaryHandler BestiaryHandler { get; private set; }
		internal Handlers.NetModules.CreativePowerHandler CreativePowerHandler { get; private set; }
		internal Handlers.NetModules.CreativeUnlocksHandler CreativeUnlocksHandler { get; private set; }
		internal Handlers.NetModules.LiquidHandler LiquidHandler { get; private set; }
		internal Handlers.NetModules.PylonHandler PylonHandler { get; private set; }
		internal Handlers.EmojiHandler EmojiHandler { get; private set; }
		internal Handlers.DisplayDollItemSyncHandler DisplayDollItemSyncHandler { get; private set; }
		internal Handlers.RequestTileEntityInteractionHandler RequestTileEntityInteractionHandler { get; private set; }
		internal Handlers.LandGolfBallInCupHandler LandGolfBallInCupHandler { get; private set; }
		internal Handlers.SyncTilePickingHandler SyncTilePickingHandler { get; private set; }

		/// <summary>
		/// A class that represents the limits for a particular buff when a client applies it with PlayerAddBuff.
		/// </summary>
		internal class BuffLimit
		{
			/// <summary>
			/// How many ticks at the maximum a player can apply this to another player for.
			/// </summary>
			public int MaxTicks { get; set; }
			/// <summary>
			/// Can this buff be added without the receiver being hostile (PvP)
			/// </summary>
			public bool CanBeAddedWithoutHostile { get; set; }
			/// <summary>
			/// Can this buff only be applied to the sender?
			/// </summary>
			public bool CanOnlyBeAppliedToSender { get; set; }
		}

		internal static BuffLimit[] PlayerAddBuffWhitelist;

		/// <summary>
		/// Represents a place style corrector.
		/// </summary>
		/// <param name="player">The player placing the tile.</param>
		/// <param name="requestedPlaceStyle">The requested place style to be placed.</param>
		/// <param name="actualItemPlaceStyle">The actual place style that should be placed, based of the player's held item.</param>
		/// <returns>The correct place style in the current context.</returns>
		internal delegate int PlaceStyleCorrector(Player player, int requestedPlaceStyle, int actualItemPlaceStyle);

		/// <summary>
		/// Represents a dictionary of <see cref="PlaceStyleCorrector"/>s, the key is the tile ID and the value is the corrector.
		/// </summary>
		internal Dictionary<int, PlaceStyleCorrector> PlaceStyleCorrectors = new Dictionary<int, PlaceStyleCorrector>();

		/// <summary>Constructor call initializes Bouncer and related functionality.</summary>
		/// <returns>A new Bouncer.</returns>
		internal Bouncer()
		{
			STSHandler = new();
			NetPacketHandler.Register<TileSquare>(STSHandler.OnReceive, HandlerPriority.High);
			AmbienceHandler = new();
            NetPacketHandler.Register<NetAmbienceModule>(AmbienceHandler.OnReceive, HandlerPriority.High);
			BestiaryHandler = new();
            NetPacketHandler.Register<NetBestiaryModule>(BestiaryHandler.OnReceive, HandlerPriority.High);
            CreativePowerHandler = new();
            NetPacketHandler.Register<NetCreativePowersModule>(CreativePowerHandler.OnReceive, HandlerPriority.High);
            CreativeUnlocksHandler = new();
            NetPacketHandler.Register<NetCreativeUnlocksPlayerReportModule>(CreativeUnlocksHandler.OnReceive, HandlerPriority.High);
            LiquidHandler = new();
            NetPacketHandler.Register<NetLiquidModule>(LiquidHandler.OnReceive, HandlerPriority.High);
            PylonHandler = new();
            NetPacketHandler.Register<NetTeleportPylonModule>(PylonHandler.OnReceive, HandlerPriority.High);
			EmojiHandler = new();
            NetPacketHandler.Register<Emoji>(EmojiHandler.OnReceive, HandlerPriority.High);
			DisplayDollItemSyncHandler = new();
			NetPacketHandler.Register<TEDisplayDollItemSync>(DisplayDollItemSyncHandler.OnReceive, HandlerPriority.High);
            RequestTileEntityInteractionHandler = new();
			NetPacketHandler.Register<RequestTileEntityInteraction>(RequestTileEntityInteractionHandler.OnReceive, HandlerPriority.High);
			LandGolfBallInCupHandler = new();
            NetPacketHandler.Register<LandGolfBallInCup>(LandGolfBallInCupHandler.OnReceive, HandlerPriority.High);
			SyncTilePickingHandler = new();
            NetPacketHandler.Register<SyncTilePicking>(SyncTilePickingHandler.OnReceive, HandlerPriority.High);

			// Setup hooks
			NetPacketHandler.Register<TileSection>(OnGetSection, HandlerPriority.High);
			NetPacketHandler.Register<PlayerControls>(OnPlayerUpdate, HandlerPriority.High);
            NetPacketHandler.Register<TileChange>(OnTileEdit, HandlerPriority.High);
            NetPacketHandler.Register<SyncItem>(OnItemDrop, HandlerPriority.High);
            NetPacketHandler.Register<InstancedItem>(OnItemDrop, HandlerPriority.High);
			NetPacketHandler.Register<SyncProjectile>(OnNewProjectile, HandlerPriority.High);
			NetPacketHandler.Register<KillProjectile>(OnProjectileKill, HandlerPriority.High);
            NetPacketHandler.Register<SyncChestItem>(OnChestItemChange, HandlerPriority.High);
            NetPacketHandler.Register<RequestChestOpen>(OnChestOpen, HandlerPriority.High);
            NetPacketHandler.Register<ChestUpdates>(OnPlaceChest, HandlerPriority.High);
            NetPacketHandler.Register<PlayerZone>(OnPlayerZone, HandlerPriority.High);
			NetPacketHandler.Register<ItemAnimation>(OnPlayerAnimation, HandlerPriority.High);
            NetPacketHandler.Register<LiquidUpdate>(OnLiquidSet, HandlerPriority.High);
            NetPacketHandler.Register<QuickStackChests>(OnQuickStackPacket, HandlerPriority.High);
            NetPacketHandler.Register<AddPlayerBuff>(OnPlayerBuff, HandlerPriority.High);
            NetPacketHandler.Register<AddNPCBuff>(OnNPCAddBuff, HandlerPriority.High);
            NetPacketHandler.Register<NPCHome>(OnUpdateNPCHome, HandlerPriority.High);
            NetPacketHandler.Register<SpiritHeal>(OnHealOtherPlayer, HandlerPriority.High);
            NetPacketHandler.Register<BugReleasing>(OnReleaseNPC, HandlerPriority.High);

			NetPacketHandler.Register<PlaceObject>(OnPlaceObject, HandlerPriority.High);
            NetPacketHandler.Register<TileEntityPlacement>(OnPlaceTileEntity, HandlerPriority.High);
            NetPacketHandler.Register<ItemFrameTryPlacing>(OnPlaceItemFrame, HandlerPriority.High);
            NetPacketHandler.Register<TeleportPlayerThroughPortal>(OnPlayerPortalTeleport, HandlerPriority.High);
            NetPacketHandler.Register<GemLockToggle>(OnGemLockToggle, HandlerPriority.High);
            NetPacketHandler.Register<MassWireOperation>(OnMassWireOperation, HandlerPriority.High);
            NetPacketHandler.Register<PlayerHurtV2>(OnPlayerDamage, HandlerPriority.High);
            NetPacketHandler.Register<PlayerDeathV2>(OnKillMe, HandlerPriority.High);
            NetPacketHandler.Register<FishOutNPC>(OnFishOutNPC, HandlerPriority.High);
            NetPacketHandler.Register<FoodPlatterTryPlacing>(OnFoodPlatterTryPlacing, HandlerPriority.High);

            On.OTAPI.HooksSystemContext.ChestSystemContext.InvokeQuickStack += OnQuickStack;

			// The following section is based off Player.PlaceThing_Tiles_PlaceIt and Player.PlaceThing_Tiles_PlaceIt_GetLegacyTileStyle.
			// Multi-block tiles are intentionally ignored because they don't pass through OnTileEdit.
			PlaceStyleCorrectors.Add(TileID.Torches,
				(player, requestedPlaceStyle, actualItemPlaceStyle) =>
				{
					// If the client is attempting to place a default torch, we need to check that the torch they are attempting to place is valid.
					// The place styles may mismatch if the player is placing a biome torch.
					// Biome torches can only be placed if the player has unlocked them (Torch God's Favor)
					// Therefore, the following conditions need to be true:
					// - The client's selected item will create a default torch(this should be true if this handler is running)
					// - The client's selected item's place style will be that of a default torch
					// - The client has unlocked biome torches
					if (actualItemPlaceStyle == TorchID.Torch && player.unlockedBiomeTorches)
					{
						// The server isn't notified when the player turns on biome torches.
						// So on the client it can be on, while on the server it's off.
						// BiomeTorchPlaceStyle returns placeStyle as-is if biome torches is off.
						// Because of the uncertainty, we:
						// 1. Ensure that UsingBiomeTorches is on, so we can get the correct
						// value from BiomeTorchPlaceStyle.
						// 2. Check if the torch is either 0 or the biome torch since we aren't
						// sure if the player has biome torches on
						var usingBiomeTorches = player.UsingBiomeTorches;
						player.UsingBiomeTorches = true;
						// BiomeTorchPlaceStyle mutates the style argument by ref.
						int biomeTorchPlaceStyle = actualItemPlaceStyle;
						{
							int typeCopy = TileID.Torches;
							player.BiomeTorchPlaceStyle(UnifiedServerCoordinator.GetClientCurrentlyServer(player.whoAmI), ref typeCopy, ref biomeTorchPlaceStyle);
						}
						// Reset UsingBiomeTorches value
						player.UsingBiomeTorches = usingBiomeTorches;

						return biomeTorchPlaceStyle;
					}
					else
					{
						// If the player isn't holding the default torch, then biome torches don't apply and return item place style.
						// Or, they are holding the default torch but haven't unlocked biome torches yet, so return item place style.
						return actualItemPlaceStyle;
					}
				});
			PlaceStyleCorrectors.Add(TileID.Presents,
				(player, requestedPlaceStyle, actualItemPlaceStyle) =>
				{
					// RNG only generates placeStyles less than 7, so permit only <7
					// Note: there's an 8th present(blue, golden stripes) that's unplaceable.
					// https://terraria.fandom.com/wiki/Presents, last present of the 8 displayed
					if (requestedPlaceStyle < 7)
					{
						return requestedPlaceStyle;
					}
					else
					{
						// Return 0 for now, but ideally 0-7 should be returned.
						return 0;
					}
				});
			PlaceStyleCorrectors.Add(TileID.Explosives,
				(player, requestedPlaceStyle, actualItemPlaceStyle) =>
				{
					// RNG only generates placeStyles less than 2, so permit only <2
					if (requestedPlaceStyle < 2)
					{
						return requestedPlaceStyle;
					}
					else
					{
						// Return 0 for now, but ideally 0-1 should be returned.
						return 0;
					}
				});
			PlaceStyleCorrectors.Add(TileID.Crystals,
				(player, requestedPlaceStyle, actualItemPlaceStyle) =>
				{
					// RNG only generates placeStyles less than 18, so permit only <18.
					// Note: Gelatin Crystals(Queen Slime summon) share the same ID as Crystal Shards.
					// <18 includes all shards except Gelatin Crystals.
					if (requestedPlaceStyle < 18)
					{
						return requestedPlaceStyle;
					}
					else
					{
						// Return 0 for now, but ideally 0-17 should be returned.
						return 0;
					}
				});
			PlaceStyleCorrectors.Add(TileID.MinecartTrack,
				(player, requestedPlaceStyle, actualItemPlaceStyle) =>
				{
					// Booster tracks have 2 variations, but only 1 item.
					// The variation depends on the direction the player is facing.
					if (actualItemPlaceStyle == 2)
					{
						// Check the direction the player is facing.
						// 1 is right and -1 is left, these are the only possible values.
						if (player.direction == 1)
						{
							// Right-facing booster tracks
							return 3;
						}
						else if (player.direction == -1)
						{
							// Left-facing booster tracks
							return 2;
						}
						else
						{
							throw new InvalidOperationException(GetString("Unrecognized player direction"));
						}
					}
					else
					{
						// Not a booster track, return as-is.
						return actualItemPlaceStyle;
					}
				});
			PlaceStyleCorrectors.Add(TileID.Grass, // it's TileID.ImmatureHerbs actually...
				(player, requestedPlaceStyle, actualItemPlaceStyle) =>
				{
					if (player.selectedItem is (ItemID.AcornAxe or ItemID.StaffofRegrowth) &&
						actualItemPlaceStyle is <= 6 and >= 0)
					{
						return actualItemPlaceStyle;
					}

					return requestedPlaceStyle;
				});

			#region PlayerAddBuff Whitelist

			PlayerAddBuffWhitelist = new BuffLimit[Terraria.ID.BuffID.Count];
			PlayerAddBuffWhitelist[BuffID.Poisoned] = new BuffLimit
			{
				MaxTicks = 60 * 60
			};
			PlayerAddBuffWhitelist[BuffID.OnFire] = new BuffLimit
			{
				MaxTicks = 60 * 20
			};
			PlayerAddBuffWhitelist[BuffID.Confused] = new BuffLimit
			{
				MaxTicks = 60 * 4
			};
			PlayerAddBuffWhitelist[BuffID.CursedInferno] = new BuffLimit
			{
				MaxTicks = 60 * 7
			};
			PlayerAddBuffWhitelist[BuffID.Wet] = new BuffLimit
			{
				MaxTicks = 60 * 30,
				// The Water Gun can be shot at other players and inflict Wet while not in PvP
				CanBeAddedWithoutHostile = true
			};
			PlayerAddBuffWhitelist[BuffID.Ichor] = new BuffLimit
			{
				MaxTicks = 60 * 20
			};
			PlayerAddBuffWhitelist[BuffID.Venom] = new BuffLimit
			{
				MaxTicks = 60 * 30
			};
			PlayerAddBuffWhitelist[BuffID.GelBalloonBuff] = new BuffLimit
			{
				MaxTicks = 60 * 30,
				// The Sparkle Slime Balloon inflicts this while not in PvP
				CanBeAddedWithoutHostile = true
			};
			PlayerAddBuffWhitelist[BuffID.Frostburn] = new BuffLimit
			{
				MaxTicks = 60 * 8
			};
			PlayerAddBuffWhitelist[BuffID.Campfire] = new BuffLimit
			{
				MaxTicks = 2,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true,
			};
			PlayerAddBuffWhitelist[BuffID.Sunflower] = new BuffLimit
			{
				MaxTicks = 2,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true,
			};
			PlayerAddBuffWhitelist[BuffID.WaterCandle] = new BuffLimit
			{
				MaxTicks = 2,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true,
			};
			PlayerAddBuffWhitelist[BuffID.BeetleEndurance1] = new BuffLimit
			{
				MaxTicks = 5,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true,
			};
			PlayerAddBuffWhitelist[BuffID.BeetleEndurance2] = new BuffLimit
			{
				MaxTicks = 5,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true,
			};
			PlayerAddBuffWhitelist[BuffID.BeetleEndurance3] = new BuffLimit
			{
				MaxTicks = 5,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true,
			};
			PlayerAddBuffWhitelist[BuffID.BeetleMight1] = new BuffLimit
			{
				MaxTicks = 5,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true,
			};
			PlayerAddBuffWhitelist[BuffID.BeetleMight2] = new BuffLimit
			{
				MaxTicks = 5,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true,
			};
			PlayerAddBuffWhitelist[BuffID.BeetleMight3] = new BuffLimit
			{
				MaxTicks = 5,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true,
			};
			PlayerAddBuffWhitelist[BuffID.SolarShield1] = new BuffLimit
			{
				MaxTicks = 5,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = false,
			};
			PlayerAddBuffWhitelist[BuffID.SolarShield2] = new BuffLimit
			{
				MaxTicks = 5,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = false,
			};
			PlayerAddBuffWhitelist[BuffID.SolarShield3] = new BuffLimit
			{
				MaxTicks = 5,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = false,
			};
			PlayerAddBuffWhitelist[BuffID.MonsterBanner] = new BuffLimit
			{
				MaxTicks = 2,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true
			};
			PlayerAddBuffWhitelist[BuffID.HeartLamp] = new BuffLimit
			{
				MaxTicks = 2,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true
			};
			PlayerAddBuffWhitelist[BuffID.PeaceCandle] = new BuffLimit
			{
				MaxTicks = 2,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true
			};
			PlayerAddBuffWhitelist[BuffID.StarInBottle] = new BuffLimit
			{
				MaxTicks = 2,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true
			};
			PlayerAddBuffWhitelist[BuffID.CatBast] = new BuffLimit
			{
				MaxTicks = 2,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true
			};
			PlayerAddBuffWhitelist[BuffID.OnFire3] = new BuffLimit
			{
				MaxTicks = 60 * 6,
				CanBeAddedWithoutHostile = false,
				CanOnlyBeAppliedToSender = false
			};
			PlayerAddBuffWhitelist[BuffID.HeartyMeal] = new BuffLimit
			{
				MaxTicks = 60 * 7,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true
			};
			PlayerAddBuffWhitelist[BuffID.Frostburn2] = new BuffLimit
			{
				MaxTicks = 60 * 7,
				CanBeAddedWithoutHostile = false,
				CanOnlyBeAppliedToSender = false
			};
			PlayerAddBuffWhitelist[BuffID.ShadowCandle] = new BuffLimit
			{
				MaxTicks = 2,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true
			};
			PlayerAddBuffWhitelist[BuffID.BrainOfConfusionBuff] = new BuffLimit
			{
				MaxTicks = 60 * 4,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true
			};
			PlayerAddBuffWhitelist[BuffID.WindPushed] = new BuffLimit
			{
				MaxTicks = 2,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true
			};
			PlayerAddBuffWhitelist[BuffID.ParryDamageBuff] = new BuffLimit
			{
				MaxTicks = 60 * 5,
				CanBeAddedWithoutHostile = true,
				CanOnlyBeAppliedToSender = true
			};

			#endregion Whitelist
		}

        private bool OnQuickStack(On.OTAPI.HooksSystemContext.ChestSystemContext.orig_InvokeQuickStack orig, OTAPI.HooksSystemContext.ChestSystemContext self, int playerId, Item item, int chestIndex) {
			var server = self.root.ToServer();
			var Main = server.Main;
			var settings = TShock.Config.GetServerSettings(server.Name);

            var id = chestIndex;
            var plr = TShock.Players[playerId];

            if (plr is not { Active: true }) {
                return false;
            }

            if (plr.IsBeingDisabled()) {
                server.Log.Debug(GetString("Bouncer / OnQuickStack rejected from disable from {0}", plr.Name));
                return false;
            }

            if (!plr.HasBuildPermission(Main.chest[id].x, Main.chest[id].y) && settings.RegionProtectChests) {
                server.Log.Debug(GetString("Bouncer / OnQuickStack rejected from region protection? from {0}", plr.Name));
                return false;
            }

			return orig(self, playerId, item, chestIndex);
        }

        private void OnGetSection(ref ReceivePacketEvent<TileSection> args) {
			var tsPlayer = args.GetTSPlayer();
			var server = args.LocalReceiver.Server;

            if (tsPlayer.RequestedSection) {
                server.Log.Debug(GetString("Bouncer / OnGetSection rejected GetSection packet from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
                return;
            }
            tsPlayer.RequestedSection = true;

            if (String.IsNullOrEmpty(tsPlayer.Name)) {
                server.Log.Debug(GetString("Bouncer / OnGetSection rejected empty player name."));
                tsPlayer.Kick(GetString("Your client sent a blank character name."), true, true);
                args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
                return;
            }

            if (!tsPlayer.HasPermission(Permissions.ignorestackhackdetection)) {
                tsPlayer.IsDisabledForStackDetection = tsPlayer.HasHackedItemStacks(shouldWarnPlayer: true);
            }
        }

		/// <summary>Handles disabling enforcement and minor anti-exploit stuff</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnPlayerUpdate(ref ReceivePacketEvent<PlayerControls> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReceiver.Server;
			var setting = TShock.Config.GetServerSettings(server.Name);

			byte item = args.Packet.SelectedItem;
			var pos = args.Packet.Position;
			var vel = args.Packet.Velocity;

			if (Single.IsInfinity(vel.X) || Single.IsInfinity(vel.Y))
			{
				server.Log.Info(GetString("Bouncer / OnPlayerUpdate force kicked (attempted to set velocity to infinity) from {0}", tsPlayer.Name));
				tsPlayer.Kick(GetString("Detected DOOM set to ON position."), true, true);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (Single.IsNaN(vel.X) || Single.IsNaN(vel.Y))
			{
				server.Log.Info(GetString("Bouncer / OnPlayerUpdate force kicked (attempted to set velocity to NaN) from {0}", tsPlayer.Name));
				tsPlayer.Kick(GetString("Detected DOOM set to ON position."), true, true);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (vel.X > 50000 || vel.Y > 50000 || vel.X < -50000 || vel.Y < -50000)
			{
				server.Log.Info(GetString("Bouncer / OnPlayerUpdate force kicked (attempted to set velocity +/- 50000) from {0}", tsPlayer.Name));
				tsPlayer.Kick(GetString("Detected DOOM set to ON position."), true, true);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y))
			{
				server.Log.Info(GetString("Bouncer / OnPlayerUpdate force kicked (attempted to set position to infinity or NaN) from {0}", tsPlayer.Name));
				tsPlayer.Kick(GetString("Detected DOOM set to ON position."), true, true);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (pos.X < 0 || pos.Y < 0 || pos.X >= server.Main.maxTilesX * 16 - 16 || pos.Y >= server.Main.maxTilesY * 16 - 16)
			{
				server.Log.Debug(GetString("Bouncer / OnPlayerUpdate rejected from (position check) {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (item < 0 || item >= tsPlayer.TPlayer.inventory.Length)
			{
				server.Log.Debug(GetString("Bouncer / OnPlayerUpdate rejected from (inventory length) {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.LastNetPosition == Vector2.Zero)
			{
				server.Log.Info(GetString("Bouncer / OnPlayerUpdate *would have rejected* from (last network position zero) {0}", tsPlayer.Name));
				// args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				// return;
			}

			if (!pos.Equals(tsPlayer.LastNetPosition))
			{
				float distance = Vector2.Distance(new Vector2(pos.X / 16f, pos.Y / 16f),
					new Vector2(tsPlayer.LastNetPosition.X / 16f, tsPlayer.LastNetPosition.Y / 16f));

				if (tsPlayer.IsBeingDisabled())
				{
					// If the player has moved outside the disabled zone...
					if (distance > setting.MaxRangeForDisabled)
					{
						// We need to tell them they were disabled and why, then revert the change.
						if (tsPlayer.IsDisabledForStackDetection)
						{
							tsPlayer.SendErrorMessage(GetString("Disabled. You went too far with hacked item stacks."));
						}
						else if (tsPlayer.IsDisabledForBannedWearable)
						{
							tsPlayer.SendErrorMessage(GetString("Disabled. You went too far with banned armor."));
						}
						else if (tsPlayer.IsDisabledForSSC)
						{
							tsPlayer.SendErrorMessage(GetString("Disabled. You need to {0}login to load your saved data.", setting.CommandSpecifier));
						}
						else if (setting.RequireLogin && !tsPlayer.IsLoggedIn)
						{
							tsPlayer.SendErrorMessage(GetString("Account needed! Please {0}register or {0}login to play!", setting.CommandSpecifier));
						}
						else if (tsPlayer.IsDisabledPendingTrashRemoval)
						{
							tsPlayer.SendErrorMessage(GetString("You need to rejoin to ensure your trash can is cleared!"));
						}

						// ??
						var lastTileX = tsPlayer.LastNetPosition.X;
						var lastTileY = tsPlayer.LastNetPosition.Y - 48;
						if (!tsPlayer.Teleport(lastTileX, lastTileY))
						{
							tsPlayer.Spawn(PlayerSpawnContext.RecallFromItem);
						}
						server.Log.Debug(GetString("Bouncer / OnPlayerUpdate rejected from (??) {0}", tsPlayer.Name));
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}
					server.Log.Debug(GetString("Bouncer / OnPlayerUpdate rejected from (below ??) {0}", tsPlayer.Name));
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				// Corpses don't move, but ghosts can.
				if (tsPlayer.Dead && !tsPlayer.TPlayer.ghost)
				{
					server.Log.Debug(GetString("Bouncer / OnPlayerUpdate rejected from (corpses don't move) {0}", tsPlayer.Name));
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}
			}

			tsPlayer.LastNetPosition = pos;
			return;
		}
        public enum EditType : byte
        {
            Fail = 0,
            Type,
            Slope,
        }
        /// <summary>Bouncer's TileEdit hook is used to revert malicious tile changes.</summary>
        /// <param name="sender">The object that triggered the event.</param>
        /// <param name="args">The packet arguments that the event has.</param>
        internal void OnTileEdit(ref ReceivePacketEvent<TileChange> args) {
            var tsPlayer = args.GetTSPlayer();
            var server = args.LocalReceiver.Server;
            var setting = TShock.Config.GetServerSettings(server.Name);

            // TODO: Add checks on the new edit actions. ReplaceTile, ReplaceWall, TryKillTile, Acutate, PokeLogicGate, SlopePoundTile
            var action = args.Packet.ChangeType;
			int tileX = args.Packet.Position.X;
			int tileY = args.Packet.Position.Y;
			short editData = args.Packet.TileType;

            EditType type = (action == TileEditAction.KillTile || action == TileEditAction.KillWall ||
                             action == TileEditAction.KillTileNoItem || action == TileEditAction.TryKillTile)
                ? EditType.Fail
                : (action == TileEditAction.PlaceTile || action == TileEditAction.PlaceWall || action == TileEditAction.ReplaceTile || action == TileEditAction.ReplaceWall)
                    ? EditType.Type
                    : EditType.Slope;

            var tile = server.Main.tile[tileX, tileY];

			// 'placeStyle' is a term used in Terraria land to determine which frame of a sprite is displayed when the sprite is placed. The placeStyle
			// determines the frameX and frameY offsets
			byte requestedPlaceStyle = args.Packet.Style;

			try
			{
				if (!Utils.TilePlacementValid(server, tileX, tileY))
				{
					server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from (tile placement valid) {0} {1} {2}", tsPlayer.Name, action, editData));
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				// I do not understand the ice tile check enough to be able to modify it, however I do know that it can be used to completely bypass region protection
				// This check ensures that build permission is always checked no matter what
				if (!tsPlayer.HasBuildPermission(tileX, tileY))
				{
					server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from build from {0} {1} {2}", tsPlayer.Name, action, editData));

					if (tile.type == TileID.ItemFrame)
					{
						int itemFrameId = TEItemFrame.Find(server, tileX - tile.frameX % 36 / 18, tileY - tile.frameY % 36 / 18);
						if (itemFrameId != -1)
						{
                            server.NetMessage.SendData((int)PacketTypes.UpdateTileEntity, -1, -1, NetworkText.Empty, itemFrameId, 0, 1);
						}
					}

					GetRollbackRectSize(server, tileX, tileY, out byte width, out byte length, out int offsetY);
					tsPlayer.SendTileRect((short)(tileX - width), (short)(tileY + offsetY), (byte)(width * 2), (byte)(length + 1));
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				if (editData < 0 ||
					((action == TileEditAction.PlaceTile || action == TileEditAction.ReplaceTile) && editData >= Terraria.ID.TileID.Count) ||
					((action == TileEditAction.PlaceWall || action == TileEditAction.ReplaceWall) && editData >= Terraria.ID.WallID.Count))
				{
					server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from editData out of bounds {0} {1} {2}", tsPlayer.Name, action, editData));
					tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				if (action == TileEditAction.KillTile && server.Main.tile[tileX, tileY].type == TileID.MagicalIceBlock)
				{
					server.Log.Debug(GetString("Bouncer / OnTileEdit super accepted from (ice block) {0} {1} {2}", tsPlayer.Name, action, editData));
					return;
				}

				if (tsPlayer.Dead && setting.PreventDeadModification)
				{
					server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from (pdm) {0} {1} {2}", tsPlayer.Name, action, editData));
					tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				Item selectedItem = tsPlayer.SelectedItem;
				int lastKilledProj = tsPlayer.LastKilledProjectile;

				if (action == TileEditAction.PlaceTile || action == TileEditAction.ReplaceTile)
				{
					if (TShock.TileBans.TileIsBanned(editData, tsPlayer))
					{
						server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from (tb) {0} {1} {2}", tsPlayer.Name, action, editData));
						tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
						tsPlayer.SendErrorMessage(GetString("You do not have permission to place this tile."));
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}

					// This is the actual tile ID we expect the selected item to create. If the tile ID from the packet and the tile ID from the item do not match
					// we need to inspect further to determine if Terraria is sending funny information (which it does sometimes) or if someone is being malicious
					var actualTileToBeCreated = selectedItem.createTile;
					// This is the actual place style we expect the selected item to create. Same as above - if it differs from what the client tells us,
					// we need to do some inspection to check if its valid
					var actualItemPlaceStyle = selectedItem.placeStyle;

					// The client has requested to place a style that does not match their held item's actual place style
					if (requestedPlaceStyle != actualItemPlaceStyle)
					{
						var tplayer = tsPlayer.TPlayer;
						// Search for an extraneous tile corrector
						// If none found then it can't be a false positive so deny the action
						if (!PlaceStyleCorrectors.TryGetValue(actualTileToBeCreated, out PlaceStyleCorrector corrector))
						{
							server.Log.Error(GetString("Bouncer / OnTileEdit rejected from (placestyle) {0} {1} {2} placeStyle: {3} expectedStyle: {4}",
								tsPlayer.Name, action, editData, requestedPlaceStyle, actualItemPlaceStyle));
							tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
							args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
							return;
						}

						// See if the corrector's expected style matches
						var correctedPlaceStyle = corrector(tplayer, requestedPlaceStyle, actualItemPlaceStyle);
						if (requestedPlaceStyle != correctedPlaceStyle)
						{
							server.Log.Error(GetString("Bouncer / OnTileEdit rejected from (placestyle) {0} {1} {2} placeStyle: {3} expectedStyle: {4}",
								tsPlayer.Name, action, editData, requestedPlaceStyle, correctedPlaceStyle));
							tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
							args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
							return;
						}
					}
				}

				if (action == TileEditAction.KillTile && !Main.tileCut[tile.type] && !breakableTiles.Contains(tile.type) && tsPlayer.RecentFuse == 0)
				{
					// If the tile is an axe tile and they aren't selecting an axe, they're hacking.
					if (Main.tileAxe[tile.type] && ((tsPlayer.TPlayer.mount.Type != MountID.Drill && selectedItem.axe == 0) && !ItemID.Sets.Explosives[selectedItem.type]))
					{
						server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from (axe) {0} {1} {2}", tsPlayer.Name, action, editData));
						tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}
					// If the tile is a hammer tile and they aren't selecting a hammer, they're hacking.
					else if (Main.tileHammer[tile.type] && ((tsPlayer.TPlayer.mount.Type != MountID.Drill && selectedItem.hammer == 0) && !ItemID.Sets.Explosives[selectedItem.type]))
					{
						server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from (hammer) {0} {1} {2}", tsPlayer.Name, action, editData));
						tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}
					// If the tile is a pickaxe tile and they aren't selecting a pickaxe, they're hacking.
					// Item frames can be modified without pickaxe tile.
					// also add an exception for snake coils, they can be removed when the player places a new one or after x amount of time
					// If the tile is part of the breakable when placing set, it might be getting broken by a placement.
					else if (tile.type != TileID.ItemFrame && tile.type != TileID.MysticSnakeRope
														   && !ItemID.Sets.Explosives[selectedItem.type]
														   && !TileID.Sets.BreakableWhenPlacing[tile.type]
														   && !Main.tileAxe[tile.type] && !Main.tileHammer[tile.type] && tile.wall == 0
														   && selectedItem.pick == 0 && selectedItem.type != ItemID.GravediggerShovel
														   && tsPlayer.TPlayer.mount.Type != MountID.Drill
														   && tsPlayer.TPlayer.mount.Type != MountID.DiggingMoleMinecart)
					{
						if (tsPlayer.TPlayer.ownedProjectileCounts[ProjectileID.PalworldDigtoise] > 0)
						{
							var digtoiseProjectile = server.Main.projectile
								.FirstOrDefault(p => p is { active: true, type: ProjectileID.PalworldDigtoise } && p.owner == tsPlayer.Index);

							// Digtoise starts digging.
							if (digtoiseProjectile?.ai[0] is 1f or 2f or 3f && digtoiseProjectile.ai[1] > 40f)
							{
								return;
							}
						}

						server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from (pick) {0} {1} {2}", tsPlayer.Name, action,
							editData));
						tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}
				}
				else if (action == TileEditAction.KillWall)
				{
					// If they aren't selecting a hammer, they could be hacking.
					if (selectedItem.hammer == 0 && !ItemID.Sets.Explosives[selectedItem.type] && tsPlayer.RecentFuse == 0 && selectedItem.createWall == 0)
					{
						server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from (hammer2) {0} {1} {2}", tsPlayer.Name, action, editData));
						tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}
				}
				else if (action == TileEditAction.PlaceTile && (projectileCreatesTile.ContainsKey(lastKilledProj) && editData == projectileCreatesTile[lastKilledProj]))
				{
					tsPlayer.LastKilledProjectile = 0;
				}
				else if (CoilTileIds.Contains(editData))
				{
					// Handle placement if the user is placing rope that comes from a ropecoil,
					// but have not created the ropecoil projectile recently or the projectile was not at the correct coordinate, or the tile that the projectile places does not match the rope it is suposed to place
					// projectile should be the same X coordinate as all tile places (Note by @Olink)
					if (ropeCoilPlacements.ContainsKey(selectedItem.type) &&
						!tsPlayer.RecentlyCreatedProjectiles.Any(p => projectileCreatesTile.ContainsKey(p.Type) && projectileCreatesTile[p.Type] == editData &&
						!p.Killed && Math.Abs((int)(server.Main.projectile[p.Index].position.X / 16f) - tileX) <= Math.Abs(server.Main.projectile[p.Index].velocity.X)))
					{
						server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from (inconceivable rope coil) {0} {1} {2} selectedItem:{3} itemCreateTile:{4}", tsPlayer.Name, action, editData, selectedItem.type, selectedItem.createTile));
						tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}
				}
				else if (action == TileEditAction.PlaceTile || action == TileEditAction.ReplaceTile || action == TileEditAction.PlaceWall || action == TileEditAction.ReplaceWall)
				{
					if ((action == TileEditAction.PlaceTile && setting.PreventInvalidPlaceStyle) &&
						requestedPlaceStyle > GetMaxPlaceStyle(editData))
					{
						server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from (ms1) {0} {1} {2}", tsPlayer.Name, action, editData));
						tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}

					// Handle placement action for Regrowth tools to ensure they only replant herbs on valid containers.
					if (selectedItem.type is ItemID.AcornAxe or ItemID.StaffofRegrowth)
					{
						if ((int)editData is not (TileID.Grass or TileID.HallowedGrass or TileID.CorruptGrass
							or TileID.CrimsonGrass or TileID.JungleGrass or TileID.MushroomGrass
							or TileID.CorruptJungleGrass or TileID.CrimsonJungleGrass or TileID.AshGrass)
							&& !TileID.Sets.Conversion.Moss[editData])
						{
							if (editData != TileID.ImmatureHerbs)
							{
								server.Log.Debug(GetString(
									"Bouncer / OnTileEdit rejected {0} from placing non-herb tile {1} using {2}",
									tsPlayer.Name, editData, selectedItem.Name));
								tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
								args.HandleMode = PacketHandleMode.Cancel;
								args.StopPropagation = true;
							}

							var containerTile = server.Main.tile[tileX, tileY + 1];
							if (!containerTile.active() ||
								containerTile.type is not (TileID.ClayPot or TileID.RockGolemHead or TileID.PlanterBox))
							{
								server.Log.Debug(GetString(
									"Bouncer / OnTileEdit rejected {0} from planting herb on invalid tile {1} using {2}",
									tsPlayer.Name, containerTile.type, selectedItem.Name));
								tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
								args.HandleMode = PacketHandleMode.Cancel;
								args.StopPropagation = true;
							}
						}
					}
					// Handle placement action if the player is using an Ice Rod but not placing the iceblock.
					if (selectedItem.type == ItemID.IceRod && editData != TileID.MagicalIceBlock)
					{
						server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from using ice rod but not placing ice block {0} {1} {2}", tsPlayer.Name, action, editData));
						tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
						args.HandleMode = PacketHandleMode.Cancel;
						args.StopPropagation = true;
					}
					// If they aren't selecting the item which creates the tile, they're hacking.
					if ((action == TileEditAction.PlaceTile || action == TileEditAction.ReplaceTile) && editData != selectedItem.createTile)
					{
						// These would get caught up in the below check because Terraria does not set their createTile field.
						if (selectedItem.type != ItemID.IceRod &&
							selectedItem.type != ItemID.DirtBomb &&
							selectedItem.type != ItemID.StickyBomb &&
							selectedItem.type != ItemID.MudBallPlayer &&
							selectedItem.type != ItemID.AcornAxe &&
							selectedItem.type != ItemID.StaffofRegrowth &&
							!(tsPlayer.RecentlyCreatedProjectiles.Any(x => x.Type == ProjectileID.AcornSlingshotAcorn) && editData == TileID.Saplings) &&
							!(tsPlayer.TPlayer.mount.Type == MountID.DiggingMoleMinecart && editData == TileID.MinecartTrack))
						{
							server.Log.Debug(GetString(
								"Bouncer / OnTileEdit rejected from tile placement not matching selected item createTile {0} {1} {2} selectedItemID:{3} createTile:{4}",
								tsPlayer.Name, action, editData, selectedItem.type, selectedItem.createTile));
							tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
							args.HandleMode = PacketHandleMode.Cancel;
							args.StopPropagation = true;
							return;
						}
					}
					// If they aren't selecting the item which creates the wall, they're hacking.
					if ((action == TileEditAction.PlaceWall || action == TileEditAction.ReplaceWall) && editData != selectedItem.createWall)
					{
						server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from wall placement not matching selected item createWall {0} {1} {2} selectedItemID:{3} createWall:{4}", tsPlayer.Name, action, editData, selectedItem.type, selectedItem.createWall));
						tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}
					if (action == TileEditAction.PlaceTile && (editData == TileID.Containers || editData == TileID.Containers2))
					{
						if (Utils.HasWorldReachedMaxChests(server))
						{
							server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from (chestcap) {0} {1} {2}", tsPlayer.Name, action, editData));
							tsPlayer.SendErrorMessage(GetString("The world's chest limit has been reached - unable to place more."));
							tsPlayer.SendTileSquareCentered(tileX, tileY, 3);
							args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
							return;
						}
					}
				}
				else if (action == TileEditAction.PlaceWire || action == TileEditAction.PlaceWire2 || action == TileEditAction.PlaceWire3)
				{
					// If they aren't selecting a wrench, they're hacking.
					// WireKite = The Grand Design
					if (selectedItem.type != ItemID.Wrench
						&& selectedItem.type != ItemID.BlueWrench
						&& selectedItem.type != ItemID.GreenWrench
						&& selectedItem.type != ItemID.YellowWrench
						&& selectedItem.type != ItemID.MulticolorWrench
						&& selectedItem.type != ItemID.WireKite)
					{
						server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from place wire from {0} {1} {2}", tsPlayer.Name, action, editData));
						tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}
				}
				else if (action == TileEditAction.KillActuator || action == TileEditAction.KillWire ||
					action == TileEditAction.KillWire2 || action == TileEditAction.KillWire3)
				{
					// If they aren't selecting the wire cutter, they're hacking.
					if (selectedItem.type != ItemID.WireCutter
						&& selectedItem.type != ItemID.WireKite
						&& selectedItem.type != ItemID.MulticolorWrench)
					{
						server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from wire cutter from {0} {1} {2}", tsPlayer.Name, action, editData));
						tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}
				}
				else if (action == TileEditAction.PlaceActuator)
				{
					// If they aren't selecting the actuator and don't have the Presserator equipped, they're hacking.
					if (selectedItem.type != ItemID.Actuator && !tsPlayer.TPlayer.autoActuator)
					{
						server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from actuator/presserator from {0} {1} {2}", tsPlayer.Name, action, editData));
						tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}
				}
				if (setting.AllowCutTilesAndBreakables && Main.tileCut[tile.type])
				{
					if (action == TileEditAction.KillWall || action == TileEditAction.ReplaceWall)
					{
						server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from sts allow cut from {0} {1} {2}", tsPlayer.Name, action, editData));
						tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}
					return;
				}

				if (tsPlayer.IsBeingDisabled())
				{
					server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from disable from {0} {1} {2}", tsPlayer.Name, action, editData));
					tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				if (!tsPlayer.HasModifiedIceSuccessfully(tileX, tileY, editData, action)
					&& !tsPlayer.HasBuildPermission(tileX, tileY))
				{
					server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from ice/build from {0} {1} {2}", tsPlayer.Name, action, editData));

					GetRollbackRectSize(server, tileX, tileY, out byte width, out byte length, out int offsetY);
					tsPlayer.SendTileRect((short)(tileX - width), (short)(tileY + offsetY), (byte)(width * 2), (byte)(length + 1));
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				//make sure it isnt a snake coil related edit so it doesnt spam debug logs with range check failures
				if (((action == TileEditAction.PlaceTile && editData != TileID.MysticSnakeRope) || (action == TileEditAction.KillTile && tile.type != TileID.MysticSnakeRope)) && !tsPlayer.IsInRange(tileX, tileY))
				{
					if (action == TileEditAction.PlaceTile && (editData == TileID.Rope || editData == TileID.SilkRope || editData == TileID.VineRope || editData == TileID.WebRope || editData == TileID.MysticSnakeRope))
					{
						return;
					}

					if (action == TileEditAction.KillTile || action == TileEditAction.KillWall && ItemID.Sets.Explosives[selectedItem.type] && tsPlayer.RecentFuse == 0)
					{
						return;
					}

					// Dirt bomb makes dirt everywhere
					if ((action == TileEditAction.PlaceTile || action == TileEditAction.SlopeTile) && editData == TileID.Dirt && tsPlayer.RecentFuse > 0)
					{
						return;
					}

					server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from explosives/fuses from {0} {1} {2}", tsPlayer.Name, action, editData));
					tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				if (tsPlayer.TileKillThreshold >= setting.TileKillThreshold)
				{
					if (setting.KickOnTileKillThresholdBroken)
					{
						tsPlayer.Kick(GetString("Tile kill threshold exceeded {0}.", setting.TileKillThreshold));
					}
					else
					{
						tsPlayer.Disable(GetString("Reached TileKill threshold."), DisableFlags.WriteToLogAndConsole);
						tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
					}

					server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from tile kill threshold from {0}, (value: {1})", tsPlayer.Name, tsPlayer.TileKillThreshold));
					server.Log.Debug(GetString("If this player wasn't hacking, please report the tile kill threshold they were disabled for to TShock so we can improve this!"));
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				if (tsPlayer.TilePlaceThreshold >= setting.TilePlaceThreshold)
				{
					if (setting.KickOnTilePlaceThresholdBroken)
					{
						tsPlayer.Kick(GetString("Tile place threshold exceeded {0}.", setting.TilePlaceThreshold));
					}
					else
					{
						tsPlayer.Disable(GetString("Reached TilePlace threshold."), DisableFlags.WriteToLogAndConsole);
						tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
					}

					server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from tile place threshold from {0}, (value: {1})", tsPlayer.Name, tsPlayer.TilePlaceThreshold));
					server.Log.Debug(GetString("If this player wasn't hacking, please report the tile place threshold they were disabled for to TShock so we can improve this!"));
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				if (tsPlayer.IsBouncerThrottled())
				{
					server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from throttled from {0} {1} {2}", tsPlayer.Name, action, editData));
					tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				//snake coil can allow massive amounts of tile edits so it gets an exception
				if (!((action == TileEditAction.PlaceTile && editData == TileID.MysticSnakeRope) || (action == TileEditAction.KillTile && tile.type == TileID.MysticSnakeRope)))
				{
					if ((action == TileEditAction.PlaceTile || action == TileEditAction.ReplaceTile || action == TileEditAction.PlaceWall || action == TileEditAction.ReplaceWall) && !tsPlayer.HasPermission(Permissions.ignoreplacetiledetection))
					{
						tsPlayer.TilePlaceThreshold++;
						var coords = new Vector2(tileX, tileY);
						lock (tsPlayer.TilesCreated)
							if (!tsPlayer.TilesCreated.ContainsKey(coords))
								tsPlayer.TilesCreated.Add(coords, server.Main.tile[tileX, tileY]);
					}

					if ((action == TileEditAction.KillTile || action == TileEditAction.KillTileNoItem || action == TileEditAction.ReplaceTile || action == TileEditAction.KillWall || action == TileEditAction.ReplaceWall) && server.Main.tileSolid[server.Main.tile[tileX, tileY].type] &&
						!tsPlayer.HasPermission(Permissions.ignorekilltiledetection))
					{
						tsPlayer.TileKillThreshold++;
						var coords = new Vector2(tileX, tileY);
						lock (tsPlayer.TilesDestroyed)
							if (!tsPlayer.TilesDestroyed.ContainsKey(coords))
								tsPlayer.TilesDestroyed.Add(coords, server.Main.tile[tileX, tileY]);
					}
				}
				return;
			}
			catch
			{
				server.Log.Debug(GetString("Bouncer / OnTileEdit rejected from weird confusing flow control from {0}", tsPlayer.Name));
				server.Log.Debug(GetString("If you're seeing this message and you know what that player did, please report it to TShock for further investigation."));
				tsPlayer.SendTileSquareCentered(tileX, tileY, 4);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
		}

		/// <summary>
		/// Gets the size of the rectangle required to rollback all tiles impacted by a single tile.
		/// Eg, rolling back the destruction of a tile that had a Safe on top would require rolling back the safe as well as the
		/// tile that was destroyed
		/// </summary>
		/// <param name="tileX">X position of the initial tile</param>
		/// <param name="tileY">Y position of the initial tile</param>
		/// <param name="width">The calculated width of the rectangle</param>
		/// <param name="length">The calculated length of the rectangle</param>
		/// <param name="offsetY">The Y offset from the initial tile Y that the rectangle should begin at</param>
		private void GetRollbackRectSize(ServerContext server, int tileX, int tileY, out byte width, out byte length, out int offsetY)
		{
			CheckForTileObjectsAbove(out byte topWidth, out byte topLength, out offsetY);
			CheckForTileObjectsBelow(out byte botWidth, out byte botLength);

			// If no tile object exists around the given tile, width will be 1. Else the width of the largest tile object will be used
			width = Math.Max((byte)1, Math.Max(topWidth, botWidth));
			// If no tile object exists around the given tile, length will be 1. Else the sum of all tile object lengths will be used
			length = Math.Max((byte)1, (byte)(topLength + botLength));

			// Checks for the presence of tile objects above the tile being checked
			void CheckForTileObjectsAbove(out byte objWidth, out byte objLength, out int yOffset)
			{
				objWidth = 0;
				objLength = 0;
				yOffset = 0;

				if (tileY <= 0)
				{
					return;
				}

				var above = server.Main.tile[tileX, tileY - 1];
				if (above.type < TileObjectData._data.Count && TileObjectData._data[above.type] != null)
				{
					var data = TileObjectData._data[above.type];
                    objWidth = (byte)data.Width;
					objLength = (byte)data.Height;
					yOffset = -data.Height; //y offset is the negative of the height of the tile object
				}
			}

			//Checks for the presence of tile objects below the tile being checked
			void CheckForTileObjectsBelow(out byte objWidth, out byte objLength)
			{
				objWidth = 0;
				objLength = 0;

				if (tileY == server.Main.maxTilesY)
				{
					return;
				}

				var below = server.Main.tile[tileX, tileY + 1];
				if (below.type < TileObjectData._data.Count && TileObjectData._data[below.type] != null)
				{
					TileObjectData data = TileObjectData._data[below.type];
					objWidth = (byte)data.Width;
					objLength = (byte)data.Height;
				}
			}
        }
        internal void OnItemDrop(ref ReceivePacketEvent<SyncItem> args) {
            OnItemDrop(args.LocalReceiver.Server, args.GetTSPlayer(), args.Packet, ref args.HandleMode);
        }
        internal void OnItemDrop(ref ReceivePacketEvent<InstancedItem> args) {
			var pkt = default(SyncItem);
            pkt.ItemSlot = args.Packet.ItemSlot;
            pkt.Position = args.Packet.Position;
            pkt.Velocity = args.Packet.Velocity;
            pkt.ItemType = args.Packet.ItemType;
            pkt.Stack = args.Packet.Stack;
            pkt.Prefix = args.Packet.Prefix;
            pkt.Owner = args.Packet.Owner;
            OnItemDrop(args.LocalReceiver.Server, args.GetTSPlayer(), pkt, ref args.HandleMode);
			if (args.HandleMode == PacketHandleMode.Cancel) {
                args.StopPropagation = true;
            }
        }
        /// <summary>Registered when items fall to the ground to prevent cheating.</summary>
        /// <param name="sender">The object that triggered the event.</param>
        /// <param name="args">The packet arguments that the event has.</param>
        internal void OnItemDrop(ServerContext server, TSPlayer tsPlayer, in SyncItem args, ref PacketHandleMode handleMode)
		{
			short id = args.ItemSlot;
			Vector2 pos = args.Position;
			Vector2 vel = args.Velocity;
			short stacks = args.Stack;
			short prefix = args.Prefix;
			bool noDelay = args.Owner == 1;
			short type = args.ItemType;

			if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y))
			{
				server.Log.Info(GetString("Bouncer / OnItemDrop force kicked (attempted to set position to infinity or NaN) from {0}", tsPlayer.Name));
				tsPlayer.Kick(GetString("Detected DOOM set to ON position."), true, true);
				handleMode = PacketHandleMode.Cancel;
				return;
			}

			if (!float.IsFinite(vel.X) || !float.IsFinite(vel.Y))
			{
				server.Log.Info(GetString("Bouncer / OnItemDrop force kicked (attempted to set velocity to infinity or NaN) from {0}", tsPlayer.Name));
				tsPlayer.Kick(GetString("Detected DOOM set to ON position."), true, true);
				handleMode = PacketHandleMode.Cancel;
				return;
			}

			// player is attempting to crash clients
			if (type < -48 || type >= Terraria.ID.ItemID.Count)
			{
				server.Log.Debug(GetString("Bouncer / OnItemDrop rejected from attempt crash from {0}", tsPlayer.Name));
				handleMode = PacketHandleMode.Cancel;
				return;
			}

			// make sure the prefix is a legit value
			if (prefix > PrefixID.Count)
			{
				server.Log.Debug(GetString("Bouncer / OnItemDrop rejected from prefix check from {0}", tsPlayer.Name));

				tsPlayer.SendData(PacketTypes.SyncItemDespawn, "", id);
				handleMode = PacketHandleMode.Cancel;
				return;
			}

			if (type == 0)
			{
				if (!tsPlayer.IsInRange((int)(server.Main.item[id].position.X / 16f), (int)(server.Main.item[id].position.Y / 16f)))
				{
					// Causes item duplications. Will be re added if necessary
					//tsPlayer.SendData(PacketTypes.ItemDrop, "", id);
					server.Log.Debug(GetString("Bouncer / OnItemDrop rejected from dupe range check from {0}", tsPlayer.Name));
					handleMode = PacketHandleMode.Cancel;
					return;
				}

				return;
			}

			if (!tsPlayer.IsInRange((int)(pos.X / 16f), (int)(pos.Y / 16f), 128))
			{
				server.Log.Debug(GetString("Bouncer / OnItemDrop rejected from range check from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.SyncItemDespawn, "", id);
				handleMode = PacketHandleMode.Cancel;
				return;
			}

			// stop the client from changing the item type of a drop
			if (server.Main.item[id].active && server.Main.item[id].type != type &&
				!(server.Main.item[id].type == ItemID.EmptyBucket && type == ItemID.WaterBucket)) // Empty bucket turns into Water Bucket on rainy days.
			{
				server.Log.Debug(GetString("Bouncer / OnItemDrop rejected from item drop check from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.ItemDrop, "", id);
				handleMode = PacketHandleMode.Cancel;
				return;
			}

			Item item = new Item();
			item.netDefaults(server, type);
			if ((stacks > item.maxStack || stacks <= 0) || (TShock.ItemBans.DataModel.ItemIsBanned(EnglishLanguage.GetItemNameById(item.type), tsPlayer) && !tsPlayer.HasPermission(Permissions.allowdroppingbanneditems)))
			{
				server.Log.Debug(GetString("Bouncer / OnItemDrop rejected from drop item ban check / max stack check / min stack check from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.SyncItemDespawn, "", id);
				handleMode = PacketHandleMode.Cancel;
				return;
			}

			// TODO: Remove item ban part of this check
			if ((server.Main.ServerSideCharacter) && (DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond - tsPlayer.LoginMS < TShock.ServerSideCharacterConfig.Settings.LogonDiscardThreshold))
			{
				//Player is probably trying to sneak items onto the server in their hands!!!
				server.Log.Info(GetString("Player {0} tried to sneak {1} onto the server!", tsPlayer.Name, item.Name));
				server.Log.Debug(GetString("Bouncer / OnItemDrop rejected from sneaky from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.SyncItemDespawn, "", id);
				handleMode = PacketHandleMode.Cancel;
				return;

			}

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnItemDrop rejected from disabled from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.SyncItemDespawn, "", id);
				handleMode = PacketHandleMode.Cancel;
				return;
			}
		}

		/// <summary>Bouncer's projectile trigger hook stops world damaging projectiles from destroying the world.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnNewProjectile(ref ReceivePacketEvent<SyncProjectile> args)
		{
			var server = args.LocalReceiver.Server;
			var tsPlayer = args.GetTSPlayer();
			var setting = TShock.Config.GetServerSettings(server.Name);

			short ident = args.Packet.UUID;
			Vector2 pos = args.Packet.Position;
			Vector2 vel = args.Packet.Velocity;
			float knockback = args.Packet.Knockback;
			short damage = args.Packet.Damage;
			byte owner = args.Packet.PlayerSlot;
			short type = args.Packet.ProjType;
			int index = args.Packet.ProjSlot;
			Span<float> ai = [args.Packet.AI1, args.Packet.AI2, args.Packet.AI3];

			// Clients do send NaN values so we can't just kick them
			// See https://github.com/Pryaxis/TShock/issues/3076
			if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y))
			{
				server.Log.Info(GetString("Bouncer / OnNewProjectile rejected set position to infinity or NaN from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!float.IsFinite(vel.X) || !float.IsFinite(vel.Y))
			{
				server.Log.Info(GetString("Bouncer / OnNewProjectile rejected set velocity to infinity or NaN from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (index > Main.maxProjectiles)
			{
				server.Log.Debug(GetString("Bouncer / OnNewProjectile rejected from above projectile limit from {0}", tsPlayer.Name));
				tsPlayer.RemoveProjectile(ident, owner);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (TShock.ProjectileBans.ProjectileIsBanned(type, tsPlayer))
			{
				tsPlayer.Disable(GetString("Player does not have permission to create projectile {0}.", type), DisableFlags.WriteToLogAndConsole);
				server.Log.Debug(GetString("Bouncer / OnNewProjectile rejected from permission check from {0} {1}", tsPlayer.Name, type));
				tsPlayer.SendErrorMessage(GetString("You do not have permission to create that projectile."));
				tsPlayer.RemoveProjectile(ident, owner);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (damage > setting.MaxProjDamage && !tsPlayer.HasPermission(Permissions.ignoredamagecap))
			{
				tsPlayer.Disable(GetString("Projectile damage is higher than {0}.", setting.MaxProjDamage), DisableFlags.WriteToLogAndConsole);
				server.Log.Debug(GetString("Bouncer / OnNewProjectile rejected from projectile damage limit from {0} {1}/{2}", tsPlayer.Name, damage, setting.MaxProjDamage));
				tsPlayer.RemoveProjectile(ident, owner);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnNewProjectile rejected from disabled from {0}", tsPlayer.Name));
				tsPlayer.RemoveProjectile(ident, owner);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (type == ProjectileID.PortalGunGate)
			{
				var wrappedAngle = MathHelper.WrapAngle(ai[0]);
				var discreteDirection = (int)Math.Round(wrappedAngle / (MathF.PI / 4f));
				if (discreteDirection is < -3 or > 4)
				{
					server.Log.Debug(GetString("Bouncer / OnNewProjectile rejected portal gate from {0} (invalid angle: {1})", tsPlayer.Name, discreteDirection));
					tsPlayer.RemoveProjectile(ident, owner);
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				var boltIndex = tsPlayer.RecentlyCreatedProjectiles.FindIndex(p =>
					p.Type == ProjectileID.PortalGunBolt &&
					!p.Killed &&
					p.Index >= 0 &&
					p.Index < Main.maxProjectiles &&
					server.Main.projectile[p.Index].active);
				if (boltIndex < 0)
				{
					server.Log.Debug(GetString("Bouncer / OnNewProjectile rejected portal gate from {0} (missing active bolt)", tsPlayer.Name));
					tsPlayer.RemoveProjectile(ident, owner);
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				var bolt = tsPlayer.RecentlyCreatedProjectiles[boltIndex];
				bolt.Killed = true;
				tsPlayer.RecentlyCreatedProjectiles[boltIndex] = bolt;
			}

			/// If the projectile is a directional projectile, check if the player is holding their respected item to validate the projectile creation.
			if (directionalProjectiles.ContainsKey(type))
			{
				if (directionalProjectiles[type] == tsPlayer.TPlayer.HeldItem.type)
				{
					return;
				}
			}

			/// If the created projectile is a golf club, check if the player is holding one of the golf club items to validate the projectile creation.
			if (type == ProjectileID.GolfClubHelper && Handlers.LandGolfBallInCupHandler.GolfClubItemIDs.Contains(tsPlayer.TPlayer.HeldItem.type))
			{
				return;
			}

			/// If the created projectile is a golf ball and the player is not holding a golf club item and neither a golf ball item and neither they have had a golf club projectile created recently.
			if (Handlers.LandGolfBallInCupHandler.GolfBallProjectileIDs.Contains(type) &&
				!Handlers.LandGolfBallInCupHandler.GolfClubItemIDs.Contains(tsPlayer.TPlayer.HeldItem.type) &&
				!Handlers.LandGolfBallInCupHandler.GolfBallItemIDs.Contains(tsPlayer.TPlayer.HeldItem.type) &&
				!tsPlayer.RecentlyCreatedProjectiles.Any(p => p.Type == ProjectileID.GolfClubHelper))
			{
				server.Log.Debug(GetString("Bouncer / OnNewProjectile please report to tshock about this! normally this is a reject from {0} {1} (golf)", tsPlayer.Name, type));
			}

			// server.Main.projHostile contains projectiles that can harm players
			// without PvP enabled and belong to enemy mobs, so they shouldn't be
			// possible for players to create. (Source: Ijwu, QuiCM)
			if (server.Main.projHostile[type])
			{
				server.Log.Debug(GetString("Bouncer / OnNewProjectile rejected from hostile projectile from {0}", tsPlayer.Name));
				tsPlayer.RemoveProjectile(ident, owner);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			// Tombstones should never be permitted by players
			// This check means like, invalid or hacked tombstones (sent from hacked clients)
			// Death does not create a tombstone projectile by default
			if (type == ProjectileID.Tombstone)
			{
				server.Log.Debug(GetString("Bouncer / OnNewProjectile rejected from tombstones from {0}", tsPlayer.Name));
				tsPlayer.RemoveProjectile(ident, owner);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!setting.IgnoreProjUpdate && !tsPlayer.HasPermission(Permissions.ignoreprojectiledetection))
			{
				if (type == ProjectileID.BlowupSmokeMoonlord
					|| type == ProjectileID.PhantasmalEye
					|| type == ProjectileID.CultistBossIceMist
					|| (type >= ProjectileID.MoonlordBullet && type <= ProjectileID.MoonlordTurretLaser)
					|| type == ProjectileID.DeathLaser || type == ProjectileID.Landmine
					|| type == ProjectileID.BulletDeadeye || type == ProjectileID.BoulderStaffOfEarth
					|| (type > ProjectileID.ConfettiMelee && type < ProjectileID.SpiritHeal)
					|| (type >= ProjectileID.FlamingWood && type <= ProjectileID.GreekFire3)
					|| (type >= ProjectileID.PineNeedleHostile && type <= ProjectileID.Spike)
					|| (type >= ProjectileID.MartianTurretBolt && type <= ProjectileID.RayGunnerLaser)
					|| type == ProjectileID.CultistBossLightningOrb)
				{
					server.Log.Debug(GetString("Bouncer / OnNewProjectile rejected from weird check from {0} {1}", tsPlayer.Name, type));
					server.Log.Debug(GetString("Certain projectiles have been ignored for cheat detection."));
				}
				else
				{
					server.Log.Debug(GetString("Bouncer / OnNewProjectile please report to tshock about this! normally this is a reject from {0} {1}", tsPlayer.Name, type));
					// tsPlayer.Disable(String.Format("Does not have projectile permission to update projectile. ({0})", type), DisableFlags.WriteToLogAndConsole);
					// tsPlayer.RemoveProjectile(ident, owner);
				}
				// args.Handled = false;
				// return;
			}

			if (tsPlayer.ProjectileThreshold >= setting.ProjectileThreshold)
			{
				if (setting.KickOnProjectileThresholdBroken)
				{
					tsPlayer.Kick(GetString("Projectile create threshold exceeded {0}.", setting.ProjectileThreshold));
				}
				else
				{
					tsPlayer.Disable(GetString("Reached projectile create threshold."), DisableFlags.WriteToLogAndConsole);
					tsPlayer.RemoveProjectile(ident, owner);
				}

				server.Log.Debug(GetString("Bouncer / OnNewProjectile rejected from projectile create threshold from {0} {1}/{2}", tsPlayer.Name, tsPlayer.ProjectileThreshold, setting.ProjectileThreshold));
				server.Log.Debug(GetString("If this player wasn't hacking, please report the projectile create threshold they were disabled for to TShock so we can improve this!"));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBouncerThrottled())
			{
				server.Log.Debug(GetString("Bouncer / OnNewProjectile rejected from bouncer throttle from {0}", tsPlayer.Name));
				tsPlayer.RemoveProjectile(ident, owner);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (
				(Projectile_MaxValuesAI.ContainsKey(type) &&
					(Projectile_MaxValuesAI[type] < ai[0] || Projectile_MinValuesAI[type] > ai[0])) ||
				(Projectile_MaxValuesAI2.ContainsKey(type) &&
					(Projectile_MaxValuesAI2[type] < ai[1] || Projectile_MinValuesAI2[type] > ai[1]))
			)
			{
				server.Log.Debug(GetString("Bouncer / OnNewProjectile rejected from bouncer modified AI from {0}.", tsPlayer.Name));
				tsPlayer.RemoveProjectile(ident, owner);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			/*
			 * ai - Arguments that Projectile.AI uses for easier projectile control.
			 *	ai[0] - Distance from player (Doesn't affect the result very much)
			 *	ai[1] - The identifier of the object that will fly.
			 *
			 * FinalFractalHelper._fractalProfiles - A list of items that must be used in Zenith. (And also their colors)
			 *	If you add an item to this collection, it will also fly in the Zenith. (not active from server)
			*/
			if (setting.DisableModifiedZenith && type == ProjectileID.FinalFractal && (ai[0] < -100 || ai[0] > 101) && !Terraria.Graphics.FinalFractalHelper._fractalProfiles.ContainsKey((int)ai[1]))
			{
				server.Log.Debug(GetString("Bouncer / OnNewProjectile rejected from bouncer modified Zenith projectile from {0}.", tsPlayer.Name));
				tsPlayer.RemoveProjectile(ident, owner);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.HasPermission(Permissions.ignoreprojectiledetection))
			{
				if (type == ProjectileID.CrystalShard && setting.ProjIgnoreShrapnel) // Ignore crystal shards
				{
					server.Log.Debug(GetString("Ignoring shrapnel per config.."));
				}
				else if (!server.Main.projectile[index].active)
				{
					tsPlayer.ProjectileThreshold++; // Creating new projectile
				}
			}

			if ((type == ProjectileID.Bomb
				|| type == ProjectileID.Dynamite
				|| type == ProjectileID.StickyBomb
				|| type == ProjectileID.StickyDynamite
				|| type == ProjectileID.BombFish
				|| type == ProjectileID.ScarabBomb
				|| type == ProjectileID.DirtBomb))
			{
				//  Denotes that the player has recently set a fuse - used for cheat detection.
				tsPlayer.RecentFuse = 10;
			}
		}

		/// <summary>Handles the NPC Strike event for Bouncer.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnNPCStrike(ref ReceivePacketEvent<StrikeNPC> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
            var setting = TShock.Config.GetServerSettings(server.Name);

            short id = args.Packet.NPCSlot;
			//byte direction = args.Packet.HitDirection;
			short damage = args.Packet.Damage;
			//float knockback = args.Packet.Knockback;
			//byte crit = (byte)(args.Packet.Crit ? 1 : 0);

			if (server.Main.npc[id] == null)
			{
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (damage >= setting.MaxDamage && !tsPlayer.HasPermission(Permissions.ignoredamagecap))
			{
				if (setting.KickOnDamageThresholdBroken)
				{
					tsPlayer.Kick(GetString("NPC damage exceeded {0}.", setting.MaxDamage));
				}
				else
				{
					tsPlayer.Disable(GetString("NPC damage exceeded {0}.", setting.MaxDamage), DisableFlags.WriteToLogAndConsole);
					tsPlayer.SendData(PacketTypes.NpcUpdate, "", id);
				}

				server.Log.Debug(GetString("Bouncer / OnNPCStrike rejected from damage threshold from {0} {1}/{2}", tsPlayer.Name, damage, setting.MaxDamage));
				server.Log.Debug(GetString("If this player wasn't hacking, please report the damage threshold they were disabled for to TShock so we can improve this!"));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnNPCStrike rejected from disabled from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.NpcUpdate, "", id);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (setting.RangeChecks &&
				!tsPlayer.IsInRange((int)(server.Main.npc[id].position.X / 16f), (int)(server.Main.npc[id].position.Y / 16f), 128))
			{
				server.Log.Debug(GetString("Bouncer / OnNPCStrike rejected from range checks from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.NpcUpdate, "", id);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBouncerThrottled())
			{
				server.Log.Debug(GetString("Bouncer / OnNPCStrike rejected from bouncer throttle from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.NpcUpdate, "", id);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
		}

		/// <summary>Handles ProjectileKill events for throttling and out of bounds projectiles.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnProjectileKill(ref ReceivePacketEvent<KillProjectile> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();

			var ident = args.Packet.ProjSlot;
			var owner = args.Packet.PlayerSlot;
			var index = Utils.SearchProjectile(server, ident, owner);

            if (index < 0)
			{
				server.Log.Debug(GetString("Bouncer / OnProjectileKill rejected from negative projectile index from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnProjectileKill rejected from disabled from {0}", tsPlayer.Name));
				tsPlayer.RemoveProjectile(ident, owner);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBouncerThrottled())
			{
				server.Log.Debug(GetString("Bouncer / OnProjectileKill rejected from bouncer throttle from {0}", tsPlayer.Name));
				tsPlayer.RemoveProjectile(ident, owner);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
		}

		/// <summary>Handles when a chest item is changed.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnChestItemChange(ref ReceivePacketEvent<SyncChestItem> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
			var setting = TShock.Config.GetServerSettings(server.Name);

            short id = args.Packet.ChestSlot;
			byte slot = args.Packet.ChestItemSlot;
			short stacks = args.Packet.Stack;
			byte prefix = args.Packet.Prefix;
			short type = args.Packet.ItemType;

			if (tsPlayer.TPlayer.chest != id)
			{
				server.Log.Debug(GetString("Bouncer / OnChestItemChange rejected from chest mismatch from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnChestItemChange rejected from disable from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.ChestItem, "", id, slot);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.HasBuildPermission(server.Main.chest[id].x, server.Main.chest[id].y) && setting.RegionProtectChests)
			{
				server.Log.Debug(GetString("Bouncer / OnChestItemChange rejected from region protection? from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.IsInRange(server.Main.chest[id].x, server.Main.chest[id].y))
			{
				server.Log.Debug(GetString("Bouncer / OnChestItemChange rejected from range check from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
		}

		/// <summary>The Bouncer handler for when chests are opened.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnChestOpen(ref ReceivePacketEvent<RequestChestOpen> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
            var setting = TShock.Config.GetServerSettings(server.Name);

            var pos = args.Packet.Position;

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnChestOpen rejected from disabled from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.IsInRange(pos.X, pos.Y))
			{
				server.Log.Debug(GetString("Bouncer / OnChestOpen rejected from range check from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.HasBuildPermission(pos.X, pos.Y) && setting.RegionProtectChests)
			{
				server.Log.Debug(GetString("Bouncer / OnChestOpen rejected from region check from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			int id = server.Chest.FindChest(pos.X, pos.Y);
			tsPlayer.ActiveChest = id;
		}

		/// <summary>The place chest event that Bouncer hooks to prevent accidental damage.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnPlaceChest(ref ReceivePacketEvent<ChestUpdates> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
            var setting = TShock.Config.GetServerSettings(server.Name);

            int tileX = args.Packet.Position.X;
			int tileY = args.Packet.Position.Y;
			int flag = args.Packet.Operation;
			short style = args.Packet.Style;

			if (!Utils.TilePlacementValid(server, tileX, tileY) || (tsPlayer.Dead && setting.PreventDeadModification))
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceChest rejected from invalid check from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceChest rejected from disabled from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(tileX, tileY, 3);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.SelectedItem.placeStyle != style)
			{
				server.Log.Error(GetString("Bouncer / OnPlaceChest / rejected from invalid place style from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(tileX, tileY, 3);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (flag != 0 && flag != 4 // if no container or container2 placement
				&& server.Main.tile[tileX, tileY].type != TileID.Containers
				&& server.Main.tile[tileX, tileY].type != TileID.Dressers
				&& server.Main.tile[tileX, tileY].type != TileID.Containers2
				&& (!Utils.HasWorldReachedMaxChests(server) && server.Main.tile[tileX, tileY].type != TileID.Dirt)) //Chest
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceChest rejected from weird check from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(tileX, tileY, 3);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (flag == 2) //place dresser
			{
				if ((Utils.TilePlacementValid(server, tileX, tileY + 1) && server.Main.tile[tileX, tileY + 1].type == TileID.Teleporter) ||
					(Utils.TilePlacementValid(server, tileX + 1, tileY + 1) && server.Main.tile[tileX + 1, tileY + 1].type == TileID.Teleporter))
				{
					server.Log.Debug(GetString("Bouncer / OnPlaceChest rejected from weird placement check from {0}", tsPlayer.Name));
					//Prevent a dresser from being placed on a teleporter, as this can cause client and server crashes.
					tsPlayer.SendTileSquareCentered(tileX, tileY, 3);
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}
			}

			if (!tsPlayer.HasBuildPermission(tileX, tileY))
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceChest rejected from invalid permission from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(tileX, tileY, 3);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.IsInRange(tileX, tileY))
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceChest rejected from range check from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(tileX, tileY, 3);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
		}

		/// <summary>Handles PlayerZone events for preventing spawning NPC maliciously.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnPlayerZone(ref ReceivePacketEvent<PlayerZone> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();

            var zone2 = (BitsByte)args.Packet.Zone[1];
			if (zone2[1] || zone2[2] || zone2[3] || zone2[4])
			{
				bool hasSolarTower = false;
				bool hasVortexTower = false;
				bool hasNebulaTower = false;
				bool hasStardustTower = false;

				foreach (var npc in server.Main.npc)
				{
					if (npc.netID == NPCID.LunarTowerSolar)
						hasSolarTower = true;
					else if (npc.netID == NPCID.LunarTowerVortex)
						hasVortexTower = true;
					else if (npc.netID == NPCID.LunarTowerNebula)
						hasNebulaTower = true;
					else if (npc.netID == NPCID.LunarTowerStardust)
						hasStardustTower = true;
				}

				if ((zone2[1] && !hasSolarTower)
					|| (zone2[2] && !hasVortexTower)
					|| (zone2[3] && !hasNebulaTower)
					|| (zone2[4] && !hasStardustTower)
					)
				{
					server.Log.Debug(GetString("Bouncer / OnPlayerZone rejected from {0}", tsPlayer.Name));
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}
			}
		}

		/// <summary>Handles basic animation throttling for disabled players.</summary>
		/// <param name="sender">sender</param>
		/// <param name="args">args</param>
		internal void OnPlayerAnimation(ref ReceivePacketEvent<ItemAnimation> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();

            if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnPlayerAnimation rejected from disabled from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.PlayerAnimation, "", tsPlayer.Index);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBouncerThrottled())
			{
				server.Log.Debug(GetString("Bouncer / OnPlayerAnimation rejected from throttle from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.PlayerAnimation, "", tsPlayer.Index);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
		}

		/// <summary>Handles Bouncer's liquid set anti-cheat.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnLiquidSet(ref ReceivePacketEvent<LiquidUpdate> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
            var setting = TShock.Config.GetServerSettings(server.Name);

            int tileX = args.Packet.TileX;
			int tileY = args.Packet.TileY;
			byte amount = args.Packet.Liquid;
			LiquidType type = (LiquidType)args.Packet.LiquidType;

			if (!Utils.TilePlacementValid(server, tileX, tileY) || (tsPlayer.Dead && setting.PreventDeadModification))
			{
				server.Log.Debug(GetString("Bouncer / OnLiquidSet rejected invalid check from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnLiquidSet rejected disabled from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.TileLiquidThreshold >= setting.TileLiquidThreshold)
			{
				if (setting.KickOnTileLiquidThresholdBroken)
				{
					tsPlayer.Kick(GetString("Reached TileLiquid threshold {0}.", setting.TileLiquidThreshold));
				}
				else
				{
					tsPlayer.Disable(GetString("Reached TileLiquid threshold."), DisableFlags.WriteToLogAndConsole);
					tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
				}

				server.Log.Debug(GetString("Bouncer / OnLiquidSet rejected from liquid threshold from {0} {1}/{2}", tsPlayer.Name, tsPlayer.TileLiquidThreshold, setting.TileLiquidThreshold));
				server.Log.Debug(GetString("If this player wasn't hacking, please report the tile liquid threshold they were disabled for to TShock so we can improve this!"));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.HasPermission(Permissions.ignoreliquidsetdetection))
			{
				tsPlayer.TileLiquidThreshold++;
			}

			bool wasThereABombNearby = false;
			lock (tsPlayer.RecentlyCreatedProjectiles)
			{
				IEnumerable<int> projectileTypesThatPerformThisOperation;
				if (amount > 0) //handle the projectiles that create fluid.
				{
					projectileTypesThatPerformThisOperation = projectileCreatesLiquid.Where(k => k.Value == type).Select(k => k.Key);
				}
				else //handle the scenario where we are removing liquid
				{
					projectileTypesThatPerformThisOperation = projectileCreatesLiquid.Where(k => k.Value == LiquidType.Removal).Select(k => k.Key);
				}

				var recentBombs = tsPlayer.RecentlyCreatedProjectiles.Where(p => projectileTypesThatPerformThisOperation.Contains(server.Main.projectile[p.Index].type));
				wasThereABombNearby = recentBombs.Any(r => Math.Abs(tileX - (server.Main.projectile[r.Index].position.X / 16.0f)) < setting.BombExplosionRadius
														&& Math.Abs(tileY - (server.Main.projectile[r.Index].position.Y / 16.0f)) < setting.BombExplosionRadius);
			}

			// Liquid anti-cheat
			// Arguably the banned buckets bit should be in the item bans system
			if (amount != 0 && !wasThereABombNearby)
			{
				int selectedItemType = tsPlayer.TPlayer.inventory[tsPlayer.TPlayer.selectedItem].type;

				void Reject(ref ReceivePacketEvent<LiquidUpdate> args, string reason)
				{
					server.Log.Debug(GetString("Bouncer / OnLiquidSet rejected liquid type {0} from {1} holding {2}", type, tsPlayer.Name, selectedItemType));
					tsPlayer.SendErrorMessage(GetString("You do not have permission to perform this action."));
					tsPlayer.Disable(reason, DisableFlags.WriteToLogAndConsole);
					tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				}

				if (TShock.ItemBans.DataModel.ItemIsBanned(EnglishLanguage.GetItemNameById(selectedItemType), tsPlayer))
				{
					Reject(ref args, GetString("Using banned {0} to manipulate liquid", Lang.GetItemNameValue(selectedItemType)));
					return;
				}

				switch (type)
				{
					case LiquidType.Water:
						if (TShock.ItemBans.DataModel.ItemIsBanned(EnglishLanguage.GetItemNameById(ItemID.WaterBucket), tsPlayer))
						{
							Reject(ref args, GetString("Using banned water bucket without permissions"));
							return;
						}
						break;
					case LiquidType.Lava:
						if (TShock.ItemBans.DataModel.ItemIsBanned(EnglishLanguage.GetItemNameById(ItemID.LavaBucket), tsPlayer))
						{
							Reject(ref args, GetString("Using banned lava bucket without permissions"));
							return;
						}
						break;
					case LiquidType.Honey:
						if (TShock.ItemBans.DataModel.ItemIsBanned(EnglishLanguage.GetItemNameById(ItemID.HoneyBucket), tsPlayer))
						{
							Reject(ref args, GetString("Using banned honey bucket without permissions"));
							return;
						}
						break;
					case LiquidType.Shimmer:
						if (TShock.ItemBans.DataModel.ItemIsBanned(EnglishLanguage.GetItemNameById(ItemID.BottomlessShimmerBucket), tsPlayer))
						{
							Reject(ref args, GetString("Using banned shimmering water bucket without permissions"));
							return;
						}
						break;
					default:
						Reject(ref args, GetString("Manipulating unknown liquid type"));
						return;
				}

				switch (selectedItemType)
				{
					case ItemID.WaterBucket:
					case ItemID.BottomlessBucket:
						if (type != LiquidType.Water)
						{
							Reject(ref args, GetString("Using {0} on non-water", Lang.GetItemNameValue(selectedItemType)));
							return;
						}
						break;
					case ItemID.HoneyBucket:
					case ItemID.HoneyAbsorbantSponge:
					case ItemID.BottomlessHoneyBucket:
						if (type != LiquidType.Honey)
						{
							Reject(ref args, GetString("Using {0} on non-honey", Lang.GetItemNameValue(selectedItemType)));
							return;
						}
						break;
					case ItemID.LavaAbsorbantSponge:
					case ItemID.BottomlessLavaBucket:
					case ItemID.LavaBucket:
						if (type != LiquidType.Lava)
						{
							Reject(ref args, GetString("Using {0} on non-lava", Lang.GetItemNameValue(selectedItemType)));
							return;
						}
						break;
					case ItemID.BottomlessShimmerBucket:
						if (type != LiquidType.Shimmer)
						{
							Reject(ref args, GetString("Using {0} on non-shimmer", Lang.GetItemNameValue(selectedItemType)));
							return;
						}
						break;
					case ItemID.SuperAbsorbantSponge:
						if (type != LiquidType.Water && type != LiquidType.Shimmer)
						{
							Reject(ref args, GetString("Using {0} on non-water or shimmer", Lang.GetItemNameValue(selectedItemType)));
							return;
						}
						break;
					case ItemID.EmptyBucket:
					case ItemID.UltraAbsorbantSponge:
						break;
					default:
						Reject(ref args, GetString("Using {0} to manipulate unknown liquid {1}", Lang.GetItemNameValue(selectedItemType), type));
						return;
				}
			}

			if (!tsPlayer.HasBuildPermission(tileX, tileY))
			{
				server.Log.Debug(GetString("Bouncer / OnLiquidSet rejected build permission from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!wasThereABombNearby && !tsPlayer.IsInRange(tileX, tileY, 16))
			{
				server.Log.Debug(GetString("Bouncer / OnLiquidSet rejected range checks from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBouncerThrottled())
			{
				server.Log.Debug(GetString("Bouncer / OnLiquidSet rejected throttle from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(tileX, tileY, 1);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
		}

		/// <summary>Handles Buff events.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnPlayerBuff(ref ReceivePacketEvent<AddPlayerBuff> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();

            byte id = args.Packet.OtherPlayerSlot;
			int type = args.Packet.BuffType;
			int time = args.Packet.BuffTime;

			void Reject(ref ReceivePacketEvent<AddPlayerBuff> args, bool shouldResync = true)
			{
                args.HandleMode = PacketHandleMode.Cancel;
				args.StopPropagation = true;

				if (shouldResync)
					tsPlayer.SendData(PacketTypes.PlayerBuff, number: id);
			}

			if (id >= Main.maxPlayers)
			{
				server.Log.Debug(GetString(
					"Bouncer / OnPlayerBuff rejected {0} ({1}) applying buff {2} to {3} for {4} ticks: target ID out of bounds",
					tsPlayer.Name, tsPlayer.Index, type, id, time));
				Reject(ref args, false);
				return;
			}

			if (TShock.Players[id] == null || !TShock.Players[id].Active)
			{
				server.Log.Debug(GetString(
					"Bouncer / OnPlayerBuff rejected {0} ({1}) applying buff {2} to {3} for {4} ticks: target is null", tsPlayer.Name,
					tsPlayer.Index, type, id, time));
				Reject(ref args, false);
				return;
			}

			if (type >= Terraria.ID.BuffID.Count)
			{
				server.Log.Debug(GetString(
					"Bouncer / OnPlayerBuff rejected {0} ({1}) applying buff {2} to {3} for {4} ticks: invalid buff type", tsPlayer.Name,
					tsPlayer.Index, type, id, time));
				Reject(ref args, false);
				return;
			}

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString(
					"Bouncer / OnPlayerBuff rejected {0} ({1}) applying buff {2} to {3} for {4} ticks: sender is being disabled",
					tsPlayer.Name, tsPlayer.Index, type, id, time));
				Reject(ref args);
				return;
			}

			if (tsPlayer.IsBouncerThrottled())
			{
				server.Log.Debug(GetString(
					"Bouncer / OnPlayerBuff rejected {0} ({1}) applying buff {2} to {3} for {4} ticks: sender is being throttled",
					tsPlayer.Name, tsPlayer.Index, type, id, time));
				Reject(ref args);

				return;
			}

			var targetPlayer = TShock.Players[id];
			var buffLimit = PlayerAddBuffWhitelist[type];

			if (!tsPlayer.IsInRange(targetPlayer.TileX, targetPlayer.TileY, 50))
			{
				server.Log.Debug(GetString(
					"Bouncer / OnPlayerBuff rejected {0} ({1}) applying buff {2} to {3} for {4} ticks: sender is not in range of target",
					tsPlayer.Name, tsPlayer.Index, type, id, time));
				Reject(ref args);
				return;
			}

			if (buffLimit == null)
			{
				server.Log.Debug(GetString(
					"Bouncer / OnPlayerBuff rejected {0} ({1}) applying buff {2} to {3} for {4} ticks: buff is not whitelisted",
					tsPlayer.Name, tsPlayer.Index, type, id, time));
				Reject(ref args);
				return;
			}

			if (buffLimit.CanOnlyBeAppliedToSender && id != tsPlayer.Index)
			{
				server.Log.Debug(GetString(
					"Bouncer / OnPlayerBuff rejected {0} ({1}) applying buff {2} to {3} for {4} ticks: buff cannot be applied to non-senders",
					tsPlayer.Name, tsPlayer.Index, type, id, time));
				Reject(ref args);
				return;
			}

			if (!buffLimit.CanBeAddedWithoutHostile && !targetPlayer.TPlayer.hostile)
			{
				server.Log.Debug(GetString(
					"Bouncer / OnPlayerBuff rejected {0} ({1}) applying buff {2} to {3} for {4} ticks: buff cannot be applied without pvp",
					tsPlayer.Name, tsPlayer.Index, type, id, time));
				Reject(ref args);
				return;
			}

			if (time <= 0 || time > buffLimit.MaxTicks)
			{
				server.Log.Debug(GetString(
					"Bouncer / OnPlayerBuff rejected {0} ({1}) applying buff {2} to {3} for {4} ticks: buff cannot be applied for that long",
					tsPlayer.Name, tsPlayer.Index, type, id, time));
				Reject(ref args);
				return;
			}
		}

		/// <summary>Handles NPCAddBuff events.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnNPCAddBuff(ref ReceivePacketEvent<AddNPCBuff> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();

            short id = args.Packet.NPCSlot;
			int type = args.Packet.BuffType;
			short time = args.Packet.BuffTime;

			if (id >= server.Main.npc.Length)
			{
				server.Log.Debug(GetString("Bouncer / OnNPCAddBuff rejected out of bounds NPC update from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			NPC npc = server.Main.npc[id];

			if (npc == null)
			{
				server.Log.Debug(GetString("Bouncer / OnNPCAddBuff rejected null npc from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnNPCAddBuff rejected disabled from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.HasPermission(Permissions.ignorenpcbuffdetection))
			{
				bool detectedNPCBuffTimeCheat = false;

				if (NPCAddBuffTimeMax.ContainsKey(type))
				{
					if (time > NPCAddBuffTimeMax[type])
					{
						detectedNPCBuffTimeCheat = true;
					}

					if (npc.townNPC)
					{
						if (type != BuffID.Poisoned
							&& type != BuffID.OnFire
							&& type != BuffID.Confused
							&& type != BuffID.CursedInferno
							&& type != BuffID.Ichor
							&& type != BuffID.Venom
							&& type != BuffID.Midas
							&& type != BuffID.Wet
							&& type != BuffID.Lovestruck
							&& type != BuffID.Stinky
							&& type != BuffID.Slimed
							&& type != BuffID.DryadsWard
							&& type != BuffID.GelBalloonBuff
							&& type != BuffID.OnFire3
							&& type != BuffID.Frostburn2
							&& type != BuffID.Shimmer)
						{
							detectedNPCBuffTimeCheat = true;
						}
					}
				}
				else
				{
					detectedNPCBuffTimeCheat = true;
				}

				if (detectedNPCBuffTimeCheat)
				{
					server.Log.Debug(GetString("Bouncer / OnNPCAddBuff rejected abnormal buff ({0}, last for {4}) added to {1} ({2}) from {3}.", type, npc.TypeName, npc.netID, tsPlayer.Name, time));
					tsPlayer.Kick(GetString($"Added buff to {npc.TypeName} NPC abnormally."), true);
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				}
			}
		}

        public enum HouseholdStatus : byte
        {
            None = 0,
            Homeless = 1,
            HasRoom = 2,
        }

        /// <summary>The Bouncer handler for when an NPC is rehomed.</summary>
        /// <param name="sender">The object that triggered the event.</param>
        /// <param name="args">The packet arguments that the event has.</param>
        internal void OnUpdateNPCHome(ref ReceivePacketEvent<NPCHome> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();

            int id = args.Packet.NPCSlot;
			short x = args.Packet.Position.X;
			short y = args.Packet.Position.Y;

			if (!tsPlayer.HasBuildPermission(x, y))
			{
				tsPlayer.SendData(PacketTypes.UpdateNPCHome, "", id, server.Main.npc[id].homeTileX, server.Main.npc[id].homeTileY,
									 Convert.ToByte(server.Main.npc[id].homeless));
				server.Log.Debug(GetString("Bouncer / OnUpdateNPCHome rejected npc home build permission from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			// When kicking out an npc, x and y in args are 0, we shouldn't check range at this case
			if (args.Packet.Homeless != (byte)HouseholdStatus.Homeless && !tsPlayer.IsInRange(x, y))
			{
				tsPlayer.SendData(PacketTypes.UpdateNPCHome, "", id, server.Main.npc[id].homeTileX, server.Main.npc[id].homeTileY,
									 Convert.ToByte(server.Main.npc[id].homeless));
				server.Log.Debug(GetString("Bouncer / OnUpdateNPCHome rejected range checks from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
		}

		/// <summary>Bouncer's HealOther handler prevents gross misuse of HealOther packets by hackers.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnHealOtherPlayer(ref ReceivePacketEvent<SpiritHeal> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
            var setting = TShock.Config.GetServerSettings(server.Name);

            short amount = args.Packet.Amount;
			byte plr = args.Packet.OtherPlayerSlot;

			if (amount <= 0 || TShock.Players[plr] == null || !TShock.Players[plr].Active)
			{
				server.Log.Debug(GetString("Bouncer / OnHealOtherPlayer rejected null checks"));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			// Why 0.2?
			// @bartico6: Because heal other player only happens when you are using the spectre armor with the hood,
			// and the healing you can do with that is 20% of your damage.
			if (amount >= setting.MaxDamage * 0.2 && !tsPlayer.HasPermission(Permissions.ignoredamagecap))
			{
				server.Log.Debug(GetString("Bouncer / OnHealOtherPlayer 0.2 check from {0}", tsPlayer.Name));
				tsPlayer.Disable(GetString("HealOtherPlayer cheat attempt!"), DisableFlags.WriteToLogAndConsole);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.HealOtherThreshold >= setting.HealOtherThreshold)
			{
				if (setting.KickOnHealOtherThresholdBroken)
				{
					tsPlayer.Kick(GetString("HealOtherPlayer threshold exceeded {0}.", setting.HealOtherThreshold));
				}
				else
				{
					tsPlayer.Disable(GetString("Reached HealOtherPlayer threshold."), DisableFlags.WriteToLogAndConsole);
				}
				server.Log.Debug(GetString("Bouncer / OnHealOtherPlayer rejected heal other threshold from {0} {1}/{2}", tsPlayer.Name, tsPlayer.HealOtherThreshold, setting.HealOtherThreshold));
				server.Log.Debug(GetString("If this player wasn't hacking, please report the HealOtherPlayer threshold they were disabled for to TShock so we can improve this!"));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBeingDisabled() || tsPlayer.IsBouncerThrottled())
			{
				server.Log.Debug(GetString("Bouncer / OnHealOtherPlayer rejected disabled/throttled from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			tsPlayer.HealOtherThreshold++;
			return;
		}

		/// <summary>
		/// A bouncer for checking NPC released by player
		/// </summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnReleaseNPC(ref ReceivePacketEvent<BugReleasing> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
            var setting = TShock.Config.GetServerSettings(server.Name);

            int x = args.Packet.Position.X;
			int y = args.Packet.Position.Y;
			short type = args.Packet.NPCType;
			byte style = args.Packet.Styl;

			// if npc released outside allowed tile
			if (x >= server.Main.maxTilesX * 16 - 16 || x < 0 || y >= server.Main.maxTilesY * 16 - 16 || y < 0)
			{
				server.Log.Debug(GetString("Bouncer / OnReleaseNPC rejected out of bounds from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			// if player disabled
			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnReleaseNPC rejected npc release from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			void rejectForCritterNotReleasedFromItem(ref ReceivePacketEvent<BugReleasing> args)
			{
				server.Log.Debug(GetString("Bouncer / OnReleaseNPC released different critter from {0}", tsPlayer.Name));
				tsPlayer.Kick(GetString("Released critter was not from its item."), true);
				args.HandleMode = PacketHandleMode.Cancel;
                args.StopPropagation = true;
			}

			// if released npc not from its item (from crafted packet)
			// e.g. using bunny item to release golden bunny
			if (tsPlayer.TPlayer.lastVisualizedSelectedItem.makeNPC != type || tsPlayer.TPlayer.lastVisualizedSelectedItem.placeStyle != style)
			{
				// If the critter is an Explosive Bunny, check if we've recently created an Explosive Bunny projectile.
				// If we have, check if the critter we are trying to create is within range of the projectile
				// If we have at least one of those, then this wasn't a crafted packet, but simply a delayed critter release from an
				// Explosive Bunny projectile.
				if (type == NPCID.ExplosiveBunny)
				{
					bool areAnyBunnyProjectilesInRange;

					lock (tsPlayer.RecentlyCreatedProjectiles)
					{
						areAnyBunnyProjectilesInRange = tsPlayer.RecentlyCreatedProjectiles.Any(projectile =>
						{
							if (projectile.Type != ProjectileID.ExplosiveBunny)
								return false;

							var projectileInstance = server.Main.projectile[projectile.Index];
							return projectileInstance.active && projectileInstance.WithinRange(new Vector2(x, y), 32.0f);
						});
					}

					if (!areAnyBunnyProjectilesInRange)
					{
						rejectForCritterNotReleasedFromItem(ref args);
						return;
					}
				}
				else
				{
					rejectForCritterNotReleasedFromItem(ref args);
					return;
				}
			}

			if (tsPlayer.IsBouncerThrottled())
			{
				server.Log.Debug(GetString("Bouncer / OnReleaseNPC rejected throttle from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
		}

		/// <summary>Bouncer's PlaceObject hook reverts malicious tile placement.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnPlaceObject(ref ReceivePacketEvent<PlaceObject> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
            var setting = TShock.Config.GetServerSettings(server.Name);

            short x = args.Packet.Position.X;
			short y = args.Packet.Position.Y;
			short type = args.Packet.ObjectType;
			short style = args.Packet.Style;

			if (!Utils.TilePlacementValid(server, x, y))
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected valid placements from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (type < 0 || type >= Terraria.ID.TileID.Count)
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected out of bounds tile from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (x < 0 || x >= server.Main.maxTilesX)
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected out of bounds tile x from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (y < 0 || y >= server.Main.maxTilesY)
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected out of bounds tile y from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			//style 52 and 53 are used by ItemID.Fake_newchest1 and ItemID.Fake_newchest2
			//These two items cause localised lag and rendering issues
			if (type == TileID.FakeContainers && (style == 52 || style == 53))
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected fake containers from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(x, y, 4);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			// TODO: REMOVE. This does NOT look like Bouncer code.
			if (TShock.TileBans.TileIsBanned(type, tsPlayer))
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected banned tiles from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(x, y, 1);
				tsPlayer.SendErrorMessage(GetString("You do not have permission to place this tile."));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.Dead && setting.PreventDeadModification)
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected dead people don't do things from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(x, y, 4);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected disabled from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(x, y, 4);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.SelectedItem.type is ItemID.RubblemakerSmall or ItemID.RubblemakerMedium or ItemID.RubblemakerLarge)
			{
				if (type != TileID.LargePilesEcho && type != TileID.LargePiles2Echo && type != TileID.SmallPiles2x1Echo &&
					type != TileID.SmallPiles1x1Echo && type != TileID.PlantDetritus3x2Echo && type != TileID.PlantDetritus2x2Echo)
				{
					server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected rubblemaker I can't believe it's not rubble! from {0}",
						tsPlayer.Name));
					tsPlayer.SendTileSquareCentered(x, y, 4);
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}
			}
			else if (type == TileID.KiteAnchor)
			{
				if (style != 0)
				{
					server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected {0} due to invalid kite anchor style {1}", tsPlayer.Name, style));
					tsPlayer.SendTileSquareCentered(x, y, 4);
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}
			}
			else if (type == TileID.CritterAnchor)
			{
				if (style is > 4 or < 0)
				{
					server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected {0} due to invalid critter anchor style {1}", tsPlayer.Name, style));
					tsPlayer.SendTileSquareCentered(x, y, 4);
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}
			}
			else
			{
				List<int> allowTypes = [tsPlayer.TPlayer.inventory[tsPlayer.TPlayer.selectedItem].createTile];
				List<int> allowStyles = [tsPlayer.TPlayer.inventory[tsPlayer.TPlayer.selectedItem].placeStyle];
				var flexibleTileWand = tsPlayer.SelectedItem.GetFlexibleTileWand(server);
				if (flexibleTileWand != null)
				{
					var flexibleTypes = flexibleTileWand._options
						.SelectMany(kvp => kvp.Value.Options)
						.Select(option => option.TileIdToPlace);

					allowTypes.AddRange(flexibleTypes);

					var flexibleStyles = flexibleTileWand._options
						.SelectMany(kvp => kvp.Value.Options)
						.Select(option => option.TileStyleToPlace);

					allowStyles.AddRange(flexibleStyles);
				}

				// This is necessary to check in order to prevent special tiles such as
				// queen bee larva, paintings etc that use this packet from being placed
				// without selecting the right item.
				if (!allowTypes.Contains(type))
				{
					server.Log.Error(GetString("Bouncer / OnPlaceObject rejected object placement with invalid tile type {1} (expected {2}) from {0}", tsPlayer.Name, type, string.Join(',', allowTypes)));
					tsPlayer.SendTileSquareCentered(x, y, 4);
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				if (!allowStyles.Contains(style))
				{
					int biomeTorchPlaceStyle = tsPlayer.SelectedItem.placeStyle;
					{
						int typeCopy = tsPlayer.SelectedItem.createTile;
						tsPlayer.TPlayer.BiomeTorchPlaceStyle(server, ref typeCopy, ref biomeTorchPlaceStyle);
					}
					int biomeCampfirePlaceStyle = tsPlayer.SelectedItem.placeStyle;
					{
						int typeCopy = tsPlayer.SelectedItem.createTile;
						tsPlayer.TPlayer.BiomeCampfirePlaceStyle(server, ref typeCopy, ref biomeCampfirePlaceStyle);
					}
					var validTorch = tsPlayer.SelectedItem.createTile == TileID.Torches && biomeTorchPlaceStyle == style;
					var validCampfire = tsPlayer.SelectedItem.createTile == TileID.Campfire && biomeCampfirePlaceStyle == style;
					if (!tsPlayer.TPlayer.unlockedBiomeTorches || (!validTorch && !validCampfire))
					{
						server.Log.Error(GetString("Bouncer / OnPlaceObject rejected object placement with invalid style {1} (expected {2}) from {0}", tsPlayer.Name, style, string.Join(',', allowStyles)));
						tsPlayer.SendTileSquareCentered(x, y, 4);
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}
				}
			}

			TileObjectData tileData = TileObjectData.GetTileData(type, style, 0);
			if (tileData == null)
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected null tile data from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			x -= tileData.Origin.X;
			y -= tileData.Origin.Y;

			var w = tileData.Width;
			var h = tileData.Height;


            for (int i = x; i < x + w; i++)
			{
				for (int j = y; j < y + h; j++)
				{
					if (!tsPlayer.HasModifiedIceSuccessfully(i, j, type, TileEditAction.PlaceTile)
						&& !tsPlayer.HasBuildPermission(i, j))
					{
						server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected mad loop from {0}", tsPlayer.Name));
						tsPlayer.SendTileSquareCentered(i, j, 4);
						args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
						return;
					}
				}
			}

			// Ignore rope placement range
			if ((type != TileID.Rope
					|| type != TileID.SilkRope
					|| type != TileID.VineRope
					|| type != TileID.WebRope
					|| type != TileID.MysticSnakeRope)
					&& !tsPlayer.IsInRange(x, y))
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected range checks from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(x, y, 4);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.TilePlaceThreshold >= setting.TilePlaceThreshold)
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceObject rejected tile place threshold from {0} {1}/{2}", tsPlayer.Name, tsPlayer.TilePlaceThreshold, setting.TilePlaceThreshold));
				tsPlayer.Disable(GetString("Reached TilePlace threshold."), DisableFlags.WriteToLogAndConsole);
				tsPlayer.SendTileSquareCentered(x, y, 4);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.HasPermission(Permissions.ignoreplacetiledetection))
			{
				tsPlayer.TilePlaceThreshold++;
				var coords = new Vector2(x, y);
				lock (tsPlayer.TilesCreated)
					if (!tsPlayer.TilesCreated.ContainsKey(coords))
						tsPlayer.TilesCreated.Add(coords, server.Main.tile[x, y]);
			}
		}

		/// <summary>Fired when a PlaceTileEntity occurs for basic anti-cheat on perms and range.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnPlaceTileEntity(ref ReceivePacketEvent<TileEntityPlacement> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
			var pos = args.Packet.Position;

            if (!Utils.TilePlacementValid(server, pos.X, pos.Y))
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceTileEntity rejected tile placement valid from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceTileEntity rejected disabled from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.HasBuildPermission(pos.X, pos.Y))
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceTileEntity rejected permissions from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.IsInRange(pos.X, pos.Y))
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceTileEntity rejected range checks from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
		}

		/// <summary>Fired when an item frame is placed for anti-cheat detection.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnPlaceItemFrame(ref ReceivePacketEvent<ItemFrameTryPlacing> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
            var pos = args.Packet.Position;

			if (!server.TileEntity.ByPosition.TryGetValue(pos, out var entity) || entity is not TEItemFrame itemFrame) {
                args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
                return;
            }

            if (!Utils.TilePlacementValid(server, pos.X, pos.Y))
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceItemFrame rejected tile placement valid from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceItemFrame rejected disabled from {0}", tsPlayer.Name));
				server.NetMessage.SendData((int)PacketTypes.UpdateTileEntity, -1, -1, NetworkText.Empty, itemFrame.ID, 0, 1);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.HasBuildPermission(pos.X, pos.Y))
			{
				int num = server.Item.NewItem(null, (pos.X * 16) + 8, (pos.Y * 16) + 8, tsPlayer.TPlayer.width, tsPlayer.TPlayer.height, args.Packet.ItemType, args.Packet.Stack, noBroadcast: true, args.Packet.Prefix, noGrabDelay: true);
				server.Main.item[num].playerIndexTheItemIsReservedFor = tsPlayer.Index;
				server.NetMessage.SendData((int)PacketTypes.ItemDrop, tsPlayer.Index, -1, NetworkText.Empty, num, 1f);
				server.NetMessage.SendData((int)PacketTypes.ItemOwner, tsPlayer.Index, -1, NetworkText.Empty, num);
				
				server.Log.Debug(GetString("Bouncer / OnPlaceItemFrame rejected permissions from {0}", tsPlayer.Name));
				server.NetMessage.SendData((int)PacketTypes.UpdateTileEntity, -1, -1, NetworkText.Empty, itemFrame.ID, 0, 1);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.IsInRange(pos.X, pos.Y))
			{
				server.Log.Debug(GetString("Bouncer / OnPlaceItemFrame rejected range checks from {0}", tsPlayer.Name));
				server.NetMessage.SendData((int)PacketTypes.UpdateTileEntity, -1, -1, NetworkText.Empty, itemFrame.ID, 0, 1);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
		}

		internal void OnPlayerPortalTeleport(ref ReceivePacketEvent<TeleportPlayerThroughPortal> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
            var pos = args.Packet.Position;

            //Packet 96 (player teleport through portal) has no validation on whether or not the player id provided
            //belongs to the player who sent the packet.
            if (tsPlayer.Index != args.Packet.OtherPlayerSlot)
			{
				server.Log.Debug(GetString("Bouncer / OnPlayerPortalTeleport rejected untargetable teleport from {0}", tsPlayer.Name));
				//If the player who sent the packet is not the player being teleported, cancel this packet
				tsPlayer.Disable(GetString("Malicious portal attempt."), DisableFlags.WriteToLogAndConsole); //Todo: this message is not particularly clear - suggestions wanted
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			//Generic bounds checking, though I'm not sure if anyone would willingly hack themselves outside the map?
			if (pos.X > server.Main.maxTilesX * 16 || pos.X < 0
				|| pos.Y > server.Main.maxTilesY * 16 || pos.Y < 0)
			{
				server.Log.Debug(GetString("Bouncer / OnPlayerPortalTeleport rejected teleport out of bounds from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			//May as well reject teleport attempts if the player is being throttled
			if (tsPlayer.IsBeingDisabled() || tsPlayer.IsBouncerThrottled())
			{
				server.Log.Debug(GetString("Bouncer / OnPlayerPortalTeleport rejected disabled/throttled from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
		}

		internal void OnQuickStackPacket(ref ReceivePacketEvent<QuickStackChests> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();

			// TODO: Fine-grained chest-level quick stack validation is skipped in current package.
			// See: Plugins/TShockAPI/docs/trprotocol-mismatch-log.md

            if (tsPlayer.IsBeingDisabled() || tsPlayer.IsBouncerThrottled()) {
				server.Log.Debug(GetString("Bouncer / OnQuickStackPacket rejected from disable/throttle from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel;
				args.StopPropagation = true;
				return;
			}

            var request = args.Packet.QuickStackRequest;
            var slots = request.ReferenceInventorySlots ?? Array.Empty<short>();
            if (request.SlotsCount < 0 || request.SlotsCount > NetItem.MaxInventory || request.SlotsCount != slots.Length) {
				server.Log.Debug(GetString("Bouncer / OnQuickStackPacket rejected malformed slot list from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel;
				args.StopPropagation = true;
				return;
			}

            for (int i = 0; i < slots.Length; i++) {
				if (slots[i] < 0 || slots[i] >= NetItem.MaxInventory) {
					server.Log.Debug(GetString("Bouncer / OnQuickStackPacket rejected out of range slot from {0}", tsPlayer.Name));
					args.HandleMode = PacketHandleMode.Cancel;
					args.StopPropagation = true;
					return;
				}
			}
        }

		/// <summary>Handles the anti-cheat components of gem lock toggles.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnGemLockToggle(ref ReceivePacketEvent<GemLockToggle> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
			var setting = TShock.Config.GetServerSettings(server.Name);
            var pos = args.Packet.Position;

            if (pos.X < 0 || pos.Y < 0 || pos.X >= server.Main.maxTilesX || pos.Y >= server.Main.maxTilesY)
			{
				server.Log.Debug(GetString("Bouncer / OnGemLockToggle rejected boundaries check from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!Utils.TilePlacementValid(server, pos.X, pos.Y) || (tsPlayer.Dead && setting.PreventDeadModification))
			{
				server.Log.Debug(GetString("Bouncer / OnGemLockToggle invalid placement/deadmod from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnGemLockToggle rejected disabled from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (setting.RegionProtectGemLocks)
			{
				if (!tsPlayer.HasBuildPermission(pos.X, pos.Y))
				{
					server.Log.Debug(GetString("Bouncer / OnGemLockToggle rejected permissions check from {0}", tsPlayer.Name));
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}
			}
		}

		/// <summary>Handles validation of of basic anti-cheat on mass wire operations.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnMassWireOperation(ref ReceivePacketEvent<MassWireOperation> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
			var setting = TShock.Config.GetServerSettings(server.Name);

            short startX = args.Packet.Start.X;
			short startY = args.Packet.Start.Y;
			short endX = args.Packet.End.X;
			short endY = args.Packet.End.Y;

			List<Point> points = Utils.GetMassWireOperationRange(
				new Point(startX, startY),
				new Point(endX, endY),
				tsPlayer.TPlayer.direction == 1);

			int x;
			int y;
			foreach (Point p in points)
			{
				/* Perform similar checks to TileKill
				 * The server-side nature of this packet removes the need to use SendTileSquare
				 * Range checks are currently ignored here as the items that send this seem to have infinite range */

				x = p.X;
				y = p.Y;

				if (!Utils.TilePlacementValid(server, x, y) || (tsPlayer.Dead && setting.PreventDeadModification))
				{
					server.Log.Debug(GetString("Bouncer / OnMassWireOperation rejected valid placement from {0}", tsPlayer.Name));
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				if (tsPlayer.IsBeingDisabled())
				{
					server.Log.Debug(GetString("Bouncer / OnMassWireOperation rejected disabled from {0}", tsPlayer.Name));
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}

				if (!tsPlayer.HasBuildPermission(x, y))
				{
					server.Log.Debug(GetString("Bouncer / OnMassWireOperation rejected build perms from {0}", tsPlayer.Name));
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}
			}
		}

		/// <summary>Called when a player is damaged.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnPlayerDamage(ref ReceivePacketEvent<PlayerHurtV2> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
            var setting = TShock.Config.GetServerSettings(server.Name);

            byte id = args.Packet.OtherPlayerSlot;
			short damage = args.Packet.Damage;
			bool pvp = args.Packet.Bits1[1];
			bool crit = args.Packet.Bits1[0];
			byte direction = args.Packet.HitDirection;
			PlayerDeathReason reason = args.Packet.Reason;

			if (id >= Main.maxPlayers || TShock.Players[id] == null || !TShock.Players[id].Active)
			{
				server.Log.Debug(GetString("Bouncer / OnPlayerDamage rejected null check"));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (damage > setting.MaxDamage && !tsPlayer.HasPermission(Permissions.ignoredamagecap) && id != tsPlayer.Index)
			{
				if (setting.KickOnDamageThresholdBroken)
				{
					server.Log.Debug(GetString("Bouncer / OnPlayerDamage rejected damage threshold from {0} {1}/{2}", tsPlayer.Name, damage, setting.MaxDamage));
					tsPlayer.Kick(GetString("Player damage exceeded {0}.", setting.MaxDamage));
					args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
					return;
				}
				else
				{
					server.Log.Debug(GetString("Bouncer / OnPlayerDamage rejected damage threshold2 from {0} {1}/{2}", tsPlayer.Name, damage, setting.MaxDamage));
					tsPlayer.Disable(GetString("Player damage exceeded {0}.", setting.MaxDamage), DisableFlags.WriteToLogAndConsole);
				}
				tsPlayer.SendData(PacketTypes.PlayerHp, "", id);
				tsPlayer.SendData(PacketTypes.PlayerUpdate, "", id);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!TShock.Players[id].TPlayer.hostile && pvp && id != tsPlayer.Index)
			{
				server.Log.Debug(GetString("Bouncer / OnPlayerDamage rejected hostile from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.PlayerHp, "", id);
				tsPlayer.SendData(PacketTypes.PlayerUpdate, "", id);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnPlayerDamage rejected disabled from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.PlayerHp, "", id);
				tsPlayer.SendData(PacketTypes.PlayerUpdate, "", id);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.IsInRange(TShock.Players[id].TileX, TShock.Players[id].TileY, 100))
			{
				server.Log.Debug(GetString("Bouncer / OnPlayerDamage rejected range checks from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.PlayerHp, "", id);
				tsPlayer.SendData(PacketTypes.PlayerUpdate, "", id);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (tsPlayer.IsBouncerThrottled())
			{
				server.Log.Debug(GetString("Bouncer / OnPlayerDamage rejected throttled from {0}", tsPlayer.Name));
				tsPlayer.SendData(PacketTypes.PlayerHp, "", id);
				tsPlayer.SendData(PacketTypes.PlayerUpdate, "", id);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			/*
			 * PlayerDeathReason does not initially contain any information, so all fields have values -1 or null.
			 * We can use this to determine the real cause of death.
			 *
			 * If the player was not specified, that is, the player index is -1, then it is definitely a custom cause, as you can only deal damage with a projectile or another player.
			 * This is how everything else works. If an NPC is specified, its value is not -1, which is a custom cause.
			 *
			 * An exception to this is damage dealt by the Inferno potion to other players -- it is only identified by the other index value of 16,
			 * even lacking a source player index.
			 *
			 * Checking whether this damage came from the player is necessary, because the damage from the player can come even when it is hit by a NPC
			*/
			if (setting.DisableCustomDeathMessages && id != tsPlayer.Index && reason._sourceOtherIndex != 16 &&
				(reason._sourcePlayerIndex == -1 || reason._sourceNPCIndex != -1 || reason._sourceOtherIndex != -1 || reason._sourceCustomReason != null))
			{
				server.Log.Debug(GetString("Bouncer / OnPlayerDamage rejected custom death message from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
		}

		/// <summary>Bouncer's KillMe hook stops crash exploits from out of bounds values.</summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="args">The packet arguments that the event has.</param>
		internal void OnKillMe(ref ReceivePacketEvent<PlayerDeathV2> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
            var setting = TShock.Config.GetServerSettings(server.Name);

            short damage = args.Packet.Damage;
			short id = args.Packet.PlayerSlot;
			PlayerDeathReason playerDeathReason = args.Packet.Reason;

			if (damage > 42000) //Abnormal values have the potential to cause infinite loops in the server.
			{
				server.Log.Debug(GetString("Bouncer / OnKillMe rejected high damage from {0} {1}", tsPlayer.Name, damage));
				tsPlayer.Kick(GetString("Failed to shade polygon normals."), true, true);
				server.Log.Error(GetString("Death Exploit Attempt: Damage {0}", damage));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (id >= Main.maxPlayers)
			{
				server.Log.Debug(GetString("Bouncer / OnKillMe rejected index check from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			// This was formerly marked as a crash check; does not actually crash on this specific packet.
			if (playerDeathReason != null)
			{
				if (setting.DisableCustomDeathMessages && playerDeathReason._sourceCustomReason != null)
				{
					server.Log.Debug(GetString("Bouncer / OnKillMe rejected custom death message from {0}", tsPlayer.Name));
					tsPlayer.KillPlayer(PlayerDeathReason.LegacyDefault());
					tsPlayer.Dead = true;
					tsPlayer.RespawnTimer = setting.RespawnSeconds;
					args.HandleMode = PacketHandleMode.Cancel;
					args.StopPropagation = true;
					return;
				}

				if (playerDeathReason.GetDeathText(server, TShock.Players[id].Name).ToString().Length > Math.Clamp(setting.MaximumChatMessageLength, 250, 2000))
				{
					server.Log.Debug(GetString("Bouncer / OnKillMe rejected excessive length death text from {0}", tsPlayer.Name));
					tsPlayer.KillPlayer(PlayerDeathReason.LegacyDefault());
					tsPlayer.Dead = true;
					tsPlayer.RespawnTimer = setting.RespawnSeconds;
					args.HandleMode = PacketHandleMode.Cancel;
					args.StopPropagation = true;
					return;
				}
			}
		}

		/// <summary>
		/// Called when the player fishes out an NPC.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		internal void OnFishOutNPC(ref ReceivePacketEvent<FishOutNPC> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
            var setting = TShock.Config.GetServerSettings(server.Name);

            /// Getting recent projectiles of the player and selecting the first that is a bobber.
            var projectile = tsPlayer.RecentlyCreatedProjectiles.FirstOrDefault(p => server.Main.projectile[p.Index].bobber);

			if (!FishingRodItemIDs.Contains(tsPlayer.SelectedItem.type))
			{
				server.Log.Debug(GetString("Bouncer / OnFishOutNPC rejected for not using a fishing rod! - From {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
			if (projectile.Type == 0 || projectile.Killed) /// The bobber projectile is never killed when the NPC spawns. Type can only be 0 if no recent projectile is found that is named Bobber.
			{
				server.Log.Debug(GetString("Bouncer / OnFishOutNPC rejected for not finding active bobber projectile! - From {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
			if (!FishableNpcIDs.Contains(args.Packet.Start))
			{
				server.Log.Debug(GetString("Bouncer / OnFishOutNPC rejected for the NPC not being on the fishable NPCs list! - From {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
			if (args.Packet.Start == NPCID.DukeFishron && !tsPlayer.HasPermission(Permissions.summonboss))
			{
				server.Log.Debug(GetString("Bouncer / OnFishOutNPC rejected summon boss permissions from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
			if (!tsPlayer.IsInRange(args.Packet.Position.X, args.Packet.Position.Y, 55))
			{
				server.Log.Debug(GetString("Bouncer / OnFishOutNPC rejected range checks from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
			}
		}

		/// <summary>
		/// Called when a player is trying to place an item into a food plate.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		internal void OnFoodPlatterTryPlacing(ref ReceivePacketEvent<FoodPlatterTryPlacing> args) {
            var server = args.LocalReceiver.Server;
            var tsPlayer = args.GetTSPlayer();
            var setting = TShock.Config.GetServerSettings(server.Name);

			var pos = args.Packet.Position;
			var itemId = args.Packet.ItemType;

            if (!Utils.TilePlacementValid(server, pos.X, pos.Y))
			{
				server.Log.Debug(GetString("Bouncer / OnFoodPlatterTryPlacing rejected tile placement valid from {0}", tsPlayer.Name));
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if ((tsPlayer.SelectedItem.type != itemId && tsPlayer.ItemInHand.type != itemId))
			{
				server.Log.Debug(GetString("Bouncer / OnFoodPlatterTryPlacing rejected item not placed by hand from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(pos.X, pos.Y, 1);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
			if (tsPlayer.IsBeingDisabled())
			{
				server.Log.Debug(GetString("Bouncer / OnFoodPlatterTryPlacing rejected disabled from {0}", tsPlayer.Name));
				Item item = new Item();
				item.netDefaults(server, itemId);
				tsPlayer.GiveItemCheck(itemId, item.Name, args.Packet.Stack, args.Packet.Prefix);
				tsPlayer.SendTileSquareCentered(pos.X, pos.Y, 1);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.HasBuildPermission(pos.X, pos.Y))
			{
				server.Log.Debug(GetString("Bouncer / OnFoodPlatterTryPlacing rejected permissions from {0}", tsPlayer.Name));
				Item item = new Item();
				item.netDefaults(server, itemId);
				tsPlayer.GiveItemCheck(itemId, item.Name, args.Packet.Stack, args.Packet.Prefix);
				tsPlayer.SendTileSquareCentered(pos.X, pos.Y, 1);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}

			if (!tsPlayer.IsInRange(pos.X, pos.Y, range: 13)) // To my knowledge, max legit tile reach with accessories.
			{
				server.Log.Debug(GetString("Bouncer / OnFoodPlatterTryPlacing rejected range checks from {0}", tsPlayer.Name));
				tsPlayer.SendTileSquareCentered(pos.X, pos.Y, 1);
				args.HandleMode = PacketHandleMode.Cancel; args.StopPropagation = true;
				return;
			}
		}

		internal void OnSecondUpdate()
		{
			Task.Run(() =>
			{
				foreach (var player in TShock.Players)
				{
					if (player != null && player.TPlayer.whoAmI >= 0)
					{
						var threshold = DateTime.Now.AddSeconds(-5);
						lock (player.RecentlyCreatedProjectiles)
						{
							player.RecentlyCreatedProjectiles = player.RecentlyCreatedProjectiles.Where(s => s.CreatedAt > threshold).ToList();
						}
					}
				}
			});
		}

		/// <summary>
		/// Returns the max <see cref="Item.placeStyle"/> associated with the given <paramref name="tileID"/>. Or -1 if there's no association
		/// </summary>
		/// <param name="tileID">Tile ID to query for</param>
		/// <returns>The max <see cref="Item.placeStyle"/>, otherwise -1 if there's no association</returns>
		internal static int GetMaxPlaceStyle(int tileID)
		{
			int result;
			if (ExtraneousPlaceStyles.TryGetValue(tileID, out result)
				|| MaxPlaceStyles.TryGetValue(tileID, out result))
			{
				return result;
			}
			else
			{
				return -1;
			}
		}

		// These time values are references from Projectile.cs, at npc.AddBuff() calls.
		// Moved to Projectile.StatusNPC(int i).
		private static Dictionary<int, short> NPCAddBuffTimeMax = new Dictionary<int, short>()
		{
			{ BuffID.Shimmer, 100 },
			{ BuffID.Venom, 1800 },
			{ BuffID.CursedInferno, 600 },
			{ BuffID.OnFire, 19392 }, // FTW world: 216000 overflows to ushort -> 19392 for torch slime.
			{ BuffID.Ichor, 1140 },
			{ BuffID.Confused, short.MaxValue },
			{ BuffID.Poisoned, 3600 },
			{ BuffID.Midas, 120 },
			{ BuffID.Bleeding, 720 },
			{ BuffID.Frostburn2, 1200 },
			{ BuffID.OnFire3, 1200 },
			{ BuffID.Stinky, 1800 },
			{ BuffID.Slimed, 180 },
			{ BuffID.Hemorrhage, 720 },
			{ BuffID.BrokenArmor, 1200 },
			{ BuffID.BoneJavelin, 900 },
			{ BuffID.Daybreak, 300 },
			{ BuffID.TentacleSpike, 540 },
			{ BuffID.BloodButcherer, 540 },
			{ BuffID.BetsysCurse, 600 },
			{ BuffID.StardustMinionBleed, 900 },
			{ BuffID.ShadowFlame, 600 },
			{ BuffID.Frostburn, 239 },
			{ BuffID.Oiled, 510 },
			{ BuffID.SoulDrain, 30 },
			{ BuffID.EelWhipNPCDebuff, 240 },
			{ BuffID.ScytheWhipEnemyDebuff, 240 },
			{ BuffID.Wet, 1500 },
			{ BuffID.DryadsWard, 120 },
			{ BuffID.DryadsWardDebuff, 120 },
			{ BuffID.Tipsy, 3659 },
			{ BuffID.Lovestruck, 1800 },
			{ BuffID.GelBalloonBuff, 1800 },
		};

		/// <summary>
		/// Tile IDs that can be oriented:
		/// Cannon,
		/// Chairs,
		/// Beds,
		/// Bathtubs,
		/// Statues,
		/// Mannequin,
		/// Traps,
		/// MusicBoxes,
		/// ChristmasTree,
		/// WaterFountain,
		/// Womannequin,
		/// MinecartTrack,
		/// WeaponsRack,
		/// LunarMonolith,
		/// TargetDummy,
		/// Campfire
		/// </summary>
		private static int[] orientableTiles = new int[]
		{
			TileID.Cannon,
			TileID.Chairs,
			TileID.Beds,
			TileID.Bathtubs,
			TileID.Statues,
			TileID.Mannequin,
			TileID.Traps,
			TileID.MusicBoxes,
			TileID.ChristmasTree,
			TileID.WaterFountain,
			TileID.Womannequin,
			TileID.MinecartTrack,
			TileID.WeaponsRack,
			TileID.ItemFrame,
			TileID.LunarMonolith,
			TileID.TargetDummy,
			TileID.Campfire
		};

		/// <summary>
		/// These projectiles have been added or modified with Terraria 1.4.
		/// They come from normal items, but to have the directional functionality, they must be projectiles.
		/// </summary>
		private static Dictionary<int, int> directionalProjectiles = new Dictionary<int, int>()
		{
			///Spears
			{ ProjectileID.DarkLance, ItemID.DarkLance},
			{ ProjectileID.Trident, ItemID.Trident},
			{ ProjectileID.Spear, ItemID.Spear},
			{ ProjectileID.MythrilHalberd, ItemID.MythrilHalberd},
			{ ProjectileID.AdamantiteGlaive, ItemID.AdamantiteGlaive},
			{ ProjectileID.CobaltNaginata, ItemID.CobaltNaginata},
			{ ProjectileID.Gungnir, ItemID.Gungnir},
			{ ProjectileID.MushroomSpear, ItemID.MushroomSpear},
			{ ProjectileID.TheRottedFork, ItemID.TheRottedFork},
			{ ProjectileID.PalladiumPike, ItemID.PalladiumPike},
			{ ProjectileID.OrichalcumHalberd, ItemID.OrichalcumHalberd},
			{ ProjectileID.TitaniumTrident, ItemID.TitaniumTrident},
			{ ProjectileID.ChlorophytePartisan, ItemID.ChlorophytePartisan},
			{ ProjectileID.NorthPoleWeapon, ItemID.NorthPole},
			{ ProjectileID.ObsidianSwordfish, ItemID.ObsidianSwordfish},
			{ ProjectileID.Swordfish, ItemID.Swordfish},
			{ ProjectileID.MonkStaffT2, ItemID.MonkStaffT2},
			{ ProjectileID.ThunderSpear, ItemID.ThunderSpear},
			{ ProjectileID.GladiusStab, ItemID.Gladius},
			/// ShortSwords
			{ ProjectileID.RulerStab, ItemID.Ruler },
			{ ProjectileID.CopperShortswordStab, ItemID.CopperShortsword },
			{ ProjectileID.TinShortswordStab, ItemID.TinShortsword },
			{ ProjectileID.IronShortswordStab, ItemID.IronShortsword },
			{ ProjectileID.LeadShortswordStab, ItemID.LeadShortsword },
			{ ProjectileID.SilverShortswordStab, ItemID.SilverShortsword },
			{ ProjectileID.TungstenShortswordStab, ItemID.TungstenShortsword },
			{ ProjectileID.GoldShortswordStab, ItemID.GoldShortsword },
			{ ProjectileID.PlatinumShortswordStab, ItemID.PlatinumShortsword }
		};

		private Dictionary<short, float> Projectile_MinValuesAI = new Dictionary<short, float> {
			{ 611, -1 },

			{ 950, 0 }
		};
		private Dictionary<short, float> Projectile_MaxValuesAI = new Dictionary<short, float> {
			{ 611, 1 },

			{ 950, 0 }
		};

		private Dictionary<short, float> Projectile_MinValuesAI2 = new Dictionary<short, float> {
			{ 405, 0f },
			{ 410, 0f },

			{ 424, 0.5f },
			{ 425, 0.5f },
			{ 426, 0.5f },

			{ 612, 0.4f },
			{ 953, 0.85f },

			{ 756, 0.5f },
			{ 522, 0 }
		};
		private Dictionary<short, float> Projectile_MaxValuesAI2 = new Dictionary<short, float> {
			{ 405, 1.2f },
			{ 410, 1.2f },

			{ 424, 0.8f },
			{ 425, 0.8f },
			{ 426, 0.8f },

			{ 612, 0.7f },
			{ 953, 2 },

			{ 756, 1 },
			{ 522, 40f }
		};
	}
}
