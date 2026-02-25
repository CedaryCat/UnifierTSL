using System.Collections.Generic;
using System.IO;

using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using TrProtocol.Models;
using TrProtocol.NetPackets;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Servers;

namespace TShockAPI.Handlers
{
	/// <summary>
	/// Provides processors for handling tile rect packets.
	/// This required many hours of reverse engineering work, and is kindly provided to TShock for free by @punchready.
	/// </summary>
	public sealed class SendTileRectHandler : IPacketHandler<TileSquare>
	{
		/// <summary>
		/// Represents a common tile rect operation (Placement, State MaxSpawns, Removal).
		/// </summary>
		private readonly struct TileRectMatch
		{
			private const short IGNORE_FRAME = -1;

			private enum MatchType
			{
				Placement,
				StateChange,
				Removal,
			}

			public enum MatchResult
			{
				NotMatched,
				RejectChanges,
				BroadcastChanges,
			}

			private readonly int Width;
			private readonly int Height;

			private readonly ushort TileType;
			private readonly short MaxFrameX;
			private readonly short MaxFrameY;
			private readonly short FrameXStep;
			private readonly short FrameYStep;

			private readonly MatchType Type;

			private TileRectMatch(MatchType type, int width, int height, ushort tileType, short maxFrameX, short maxFrameY, short frameXStep, short frameYStep)
			{
				Type = type;
				Width = width;
				Height = height;
				TileType = tileType;
				MaxFrameX = maxFrameX;
				MaxFrameY = maxFrameY;
				FrameXStep = frameXStep;
				FrameYStep = frameYStep;
			}

			/// <summary>
			/// Creates a new placement operation.
			/// </summary>
			/// <param name="width">The width of the placement.</param>
			/// <param name="height">The height of the placement.</param>
			/// <param name="tileType">The tile type of the placement.</param>
			/// <param name="maxFrameX">The maximum allowed frameX of the placement.</param>
			/// <param name="maxFrameY">The maximum allowed frameY of the placement.</param>
			/// <param name="frameXStep">The step size in which frameX changes for this placement, or <c>1</c> if any value is allowed.</param>
			/// <param name="frameYStep">The step size in which frameX changes for this placement, or <c>1</c> if any value is allowed.</param>
			/// <returns>The resulting operation match.</returns>
			public static TileRectMatch Placement(int width, int height, ushort tileType, short maxFrameX, short maxFrameY, short frameXStep, short frameYStep)
			{
				return new TileRectMatch(MatchType.Placement, width, height, tileType, maxFrameX, maxFrameY, frameXStep, frameYStep);
			}

			/// <summary>
			/// Creates a new state change operation.
			/// </summary>
			/// <param name="width">The width of the state change.</param>
			/// <param name="height">The height of the state change.</param>
			/// <param name="tileType">The target tile type of the state change.</param>
			/// <param name="maxFrameX">The maximum allowed frameX of the state change.</param>
			/// <param name="maxFrameY">The maximum allowed frameY of the state change.</param>
			/// <param name="frameXStep">The step size in which frameX changes for this placement, or <c>1</c> if any value is allowed.</param>
			/// <param name="frameYStep">The step size in which frameY changes for this placement, or <c>1</c> if any value is allowed.</param>
			/// <returns>The resulting operation match.</returns>
			public static TileRectMatch StateChange(int width, int height, ushort tileType, short maxFrameX, short maxFrameY, short frameXStep, short frameYStep)
			{
				return new TileRectMatch(MatchType.StateChange, width, height, tileType, maxFrameX, maxFrameY, frameXStep, frameYStep);
			}

			/// <summary>
			/// Creates a new state change operation which only changes frameX.
			/// </summary>
			/// <param name="width">The width of the state change.</param>
			/// <param name="height">The height of the state change.</param>
			/// <param name="tileType">The target tile type of the state change.</param>
			/// <param name="maxFrame">The maximum allowed frameX of the state change.</param>
			/// <param name="frameStep">The step size in which frameX changes for this placement, or <c>1</c> if any value is allowed.</param>
			/// <returns>The resulting operation match.</returns>
			public static TileRectMatch StateChangeX(int width, int height, ushort tileType, short maxFrame, short frameStep)
			{
				return new TileRectMatch(MatchType.StateChange, width, height, tileType, maxFrame, IGNORE_FRAME, frameStep, 0);
			}

			/// <summary>
			/// Creates a new state change operation which only changes frameY.
			/// </summary>
			/// <param name="width">The width of the state change.</param>
			/// <param name="height">The height of the state change.</param>
			/// <param name="tileType">The target tile type of the state change.</param>
			/// <param name="maxFrame">The maximum allowed frameY of the state change.</param>
			/// <param name="frameStep">The step size in which frameY changes for this placement, or <c>1</c> if any value is allowed.</param>
			/// <returns>The resulting operation match.</returns>
			public static TileRectMatch StateChangeY(int width, int height, ushort tileType, short maxFrame, short frameStep)
			{
				return new TileRectMatch(MatchType.StateChange, width, height, tileType, IGNORE_FRAME, maxFrame, 0, frameStep);
			}

			/// <summary>
			/// Creates a new removal operation.
			/// </summary>
			/// <param name="width">The width of the removal.</param>
			/// <param name="height">The height of the removal.</param>
			/// <param name="tileType">The target tile type of the removal.</param>
			/// <returns>The resulting operation match.</returns>
			public static TileRectMatch Removal(int width, int height, ushort tileType)
			{
				return new TileRectMatch(MatchType.Removal, width, height, tileType, 0, 0, 0, 0);
			}

			/// <summary>
			/// Determines whether the given tile rectangle matches this operation, and if so, applies it to the world.
			/// </summary>
			/// <param name="player">The player the operation originates from.</param>
			/// <param name="rect">The tile rectangle of the operation.</param>
			/// <returns><see langword="true"/>, if the rect matches this operation and the changes have been applied, otherwise <see langword="false"/>.</returns>
			public MatchResult Matches(ServerContext server, TSPlayer player, SquareData rect)
			{
				if (rect.Width != Width || rect.Height != Height)
				{
					return MatchResult.NotMatched;
				}

				for (int x = 0; x < rect.Width; x++)
				{
					for (int y = 0; y < rect.Height; y++)
					{
						SimpleTileData tile = rect.Tiles[x, y];
						if (Type is MatchType.Placement or MatchType.StateChange)
						{
							if (tile.TileType != TileType)
							{
								return MatchResult.NotMatched;
							}
						}
						if (Type is MatchType.Placement or MatchType.StateChange)
						{
							if (MaxFrameX != IGNORE_FRAME)
							{
								if (tile.FrameX < 0 || tile.FrameX > MaxFrameX || tile.FrameX % FrameXStep != 0)
								{
									return MatchResult.NotMatched;
								}
							}
							if (MaxFrameY != IGNORE_FRAME)
							{
								if (tile.FrameY < 0 || tile.FrameY > MaxFrameY || tile.FrameY % FrameYStep != 0)
								{
									// this is the only tile type sent in a tile rect where the frame have a different pattern (56, 74, 92 instead of 54, 72, 90)
									if (!(TileType == TileID.LunarMonolith && tile.FrameY % FrameYStep == 2))
									{
										return MatchResult.NotMatched;
									}
								}
							}
						}
						if (Type == MatchType.Removal)
						{
							if (tile.Flags1[0])
							{
								return MatchResult.NotMatched;
							}
						}
					}
				}

				for (int x = rect.TilePosX; x < rect.TilePosX + rect.Width; x++)
				{
					for (int y = rect.TilePosY; y < rect.TilePosY + rect.Height; y++)
					{
						if (!player.HasBuildPermission(x, y))
						{
							// for simplicity, let's pretend that the edit was valid, but do not execute it
							return MatchResult.RejectChanges;
						}
					}
				}

				switch (Type)
				{
					case MatchType.Placement:
						{
							return MatchPlacement(server, player, rect);
						}
					case MatchType.StateChange:
						{
							return MatchStateChange(server, player, rect);
						}
					case MatchType.Removal:
						{
							return MatchRemoval(server, player, rect);
						}
				}

				return MatchResult.NotMatched;
			}

			private MatchResult MatchPlacement(ServerContext server, TSPlayer player, SquareData rect)
			{
				for (int x = rect.TilePosX; x < rect.TilePosX + rect.Width; x++)
				{
					for (int y = rect.TilePosY; y < rect.TilePosY + rect.Height; y++)
					{
						if (server.Main.tile[x, y].active()) // the client will kill tiles that auto break before placing the object
						{
							return MatchResult.NotMatched;
						}
					}
				}

				// let's hope tile types never go out of short range (they use ushort in terraria's code)
				if (TShock.TileBans.TileIsBanned((short)TileType, player))
				{
					// for simplicity, let's pretend that the edit was valid, but do not execute it
					return MatchResult.RejectChanges;
				}

				for (int x = 0; x < rect.Width; x++)
				{
					for (int y = 0; y < rect.Height; y++)
					{
						server.Main.tile[x + rect.TilePosX, y + rect.TilePosY].active(active: true);
						server.Main.tile[x + rect.TilePosX, y + rect.TilePosY].type = rect.Tiles[x, y].TileType;
						server.Main.tile[x + rect.TilePosX, y + rect.TilePosY].frameX = rect.Tiles[x, y].FrameX;
						server.Main.tile[x + rect.TilePosX, y + rect.TilePosY].frameY = rect.Tiles[x, y].FrameY;
					}
				}

				return MatchResult.BroadcastChanges;
			}

			private MatchResult MatchStateChange(ServerContext server, TSPlayer player, SquareData rect)
			{
				for (int x = rect.TilePosX; x < rect.TilePosX + rect.Width; x++)
				{
					for (int y = rect.TilePosY; y < rect.TilePosY + rect.Height; y++)
					{
						if (!server.Main.tile[x, y].active() || server.Main.tile[x, y].type != TileType)
						{
							return MatchResult.NotMatched;
						}
					}
				}

				for (int x = 0; x < rect.Width; x++)
				{
					for (int y = 0; y < rect.Height; y++)
					{
						if (MaxFrameX != IGNORE_FRAME)
						{
							server.Main.tile[x + rect.TilePosX, y + rect.TilePosY].frameX = rect.Tiles[x, y].FrameX;
						}
						if (MaxFrameY != IGNORE_FRAME)
						{
							server.Main.tile[x + rect.TilePosX, y + rect.TilePosY].frameY = rect.Tiles[x, y].FrameY;
						}
					}
				}

				return MatchResult.BroadcastChanges;
			}

			private MatchResult MatchRemoval(ServerContext server, TSPlayer player, SquareData rect)
			{
				for (int x = rect.TilePosX; x < rect.TilePosX + rect.Width; x++)
				{
					for (int y = rect.TilePosY; y < rect.TilePosY + rect.Height; y++)
					{
						if (!server.Main.tile[x, y].active() || server.Main.tile[x, y].type != TileType)
						{
							return MatchResult.NotMatched;
						}
					}
				}

				for (int x = 0; x < rect.Width; x++)
				{
					for (int y = 0; y < rect.Height; y++)
					{
						server.Main.tile[x + rect.TilePosX, y + rect.TilePosY].active(active: false);
						server.Main.tile[x + rect.TilePosX, y + rect.TilePosY].frameX = -1;
						server.Main.tile[x + rect.TilePosX, y + rect.TilePosY].frameY = -1;
					}
				}

				return MatchResult.BroadcastChanges;
			}
		}

		/// <summary>
		/// Contains the complete list of valid tile rect operations the game currently performs.
		/// </summary>
		// The matches restrict the tile rects to only place one kind of tile, and only with the given maximum values and step sizes for frameX and frameY. This performs pretty much perfect checks on the rect, allowing only valid placements.
		// For TileID.MinecartTrack, the rect is taken from `Minecart._trackSwitchOptions`, allowing any framing value in this array (currently 0-36).
		// For TileID.Plants, it is taken from `ItemID.Sets.flowerPacketInfo[n].stylesOnPurity`, allowing every style multiplied by 18.
		// The other operations are based on code analysis and manual observation.
		private static readonly TileRectMatch[] Matches = new TileRectMatch[]
		{
			TileRectMatch.Placement(2, 3, TileID.TargetDummy, 54, 36, 18, 18),
			TileRectMatch.Placement(3, 4, TileID.TeleportationPylon, (short)((int)TeleportPylonType.Count * 54 - 18), 54, 18, 18),
			TileRectMatch.Placement(2, 3, TileID.DisplayDoll, 126, 36, 18, 18),
			TileRectMatch.Placement(3, 4, TileID.HatRack, 90, 54, 18, 18),
			TileRectMatch.Placement(2, 2, TileID.ItemFrame, 162, 18, 18, 18),
			TileRectMatch.Placement(3, 3, TileID.WeaponsRack2, 90, 36, 18, 18),
			TileRectMatch.Placement(1, 1, TileID.FoodPlatter, 18, 0, 18, 18),
			TileRectMatch.Placement(1, 1, TileID.LogicSensor, 18, 108, 18, 18),
			TileRectMatch.Placement(1, 1, TileID.KiteAnchor, 72, 0, 18, 18),
			TileRectMatch.Placement(1, 1, TileID.CritterAnchor, 72, 72, 18, 18),

			TileRectMatch.StateChangeY(3, 2, TileID.Campfire, 54, 18),
			TileRectMatch.StateChangeY(4, 3, TileID.Cannon, 468, 18),
			TileRectMatch.StateChangeY(2, 2, TileID.ArrowSign, 270, 18),
			TileRectMatch.StateChangeY(2, 2, TileID.PaintedArrowSign, 270, 18),

			TileRectMatch.StateChangeX(2, 2, TileID.MusicBoxes, 54, 18),

			TileRectMatch.StateChangeY(2, 3, TileID.LunarMonolith, 92, 18),
			TileRectMatch.StateChangeY(2, 3, TileID.BloodMoonMonolith, 90, 18),
			TileRectMatch.StateChangeY(2, 3, TileID.VoidMonolith, 90, 18),
			TileRectMatch.StateChangeY(2, 3, TileID.EchoMonolith, 90, 18),
			TileRectMatch.StateChangeY(2, 3, TileID.ShimmerMonolith, 144, 18),
			TileRectMatch.StateChangeY(2, 4, TileID.WaterFountain, 126, 18),

			TileRectMatch.StateChangeX(1, 1, TileID.Candles, 18, 18),
			TileRectMatch.StateChangeX(1, 1, TileID.PeaceCandle, 18, 18),
			TileRectMatch.StateChangeX(1, 1, TileID.WaterCandle, 18, 18),
			TileRectMatch.StateChangeX(1, 1, TileID.PlatinumCandle, 18, 18),
			TileRectMatch.StateChangeX(1, 1, TileID.ShadowCandle, 18, 18),

			TileRectMatch.StateChange(1, 1, TileID.Traps, 90, 90, 18, 18),
			TileRectMatch.StateChange(1, 1, TileID.Torches, 110, (short)(TorchID.Count * 22 - 22), 22, 22),

			TileRectMatch.StateChangeX(1, 1, TileID.WirePipe, 36, 18),
			TileRectMatch.StateChangeX(1, 1, TileID.ProjectilePressurePad, 66, 22),
			TileRectMatch.StateChangeX(1, 1, TileID.Plants, 792, 18),
			TileRectMatch.StateChangeX(1, 1, TileID.MinecartTrack, 36, 1),

			TileRectMatch.Removal(1, 2, TileID.Firework),
			TileRectMatch.Removal(1, 1, TileID.LandMine),
		};



        public void OnReceive(ref RecievePacketEvent<TileSquare> args) {
			var player = TShock.Players[args.RecieveFrom.ID];
			var server = args.LocalReciever.Server;

            if (player.HasPermission(Permissions.allowclientsideworldedit)) {
                server.Log.Debug(GetString($"Bouncer / SendTileRect accepted clientside world edit from {player.Name}"));

                // use vanilla handling
                return;
            }

			args.StopPropagation = true;
			args.HandleMode = PacketHandleMode.Cancel;

			var rect = args.Packet.Data;

            // as of 1.4 this is the biggest size the client will send in any case, determined by full code analysis
            // see default matches above and special cases below
            if (rect.Width > 4 || rect.Height > 4) {
                server.Log.Debug(GetString($"Bouncer / SendTileRect rejected from size from {player.Name}"));

                // definitely invalid; do not send any correcting rect
                return;
            }

            // player throttled?
            if (player.IsBouncerThrottled()) {
                server.Log.Debug(GetString($"Bouncer / SendTileRect rejected from throttle from {player.Name}"));

                // send correcting rect
                player.SendTileRect(rect.TilePosX, rect.TilePosY, rect.Width, rect.Height);
                return;
            }

            // player disabled?
            if (player.IsBeingDisabled()) {
                server.Log.Debug(GetString($"Bouncer / SendTileRect rejected from being disabled from {player.Name}"));

                // send correcting rect
                player.SendTileRect(rect.TilePosX, rect.TilePosY, rect.Width, rect.Height);
                return;
            }

            // check if the positioning is valid
            if (!IsRectPositionValid(player, rect)) {
                server.Log.Debug(GetString($"Bouncer / SendTileRect rejected from out of bounds / build permission from {player.Name}"));

                // send nothing due to out of bounds
                return;
            }

            // a very special case, due to the clentaminator having a larger range than TSPlayer.IsInRange() allows
            if (MatchesConversionSpread(server, player, rect)) {
                server.Log.Debug(GetString($"Bouncer / SendTileRect reimplemented from {player.Name}"));

                // apply vanilla-style framing and broadcast
                FrameAndSyncRect(server, rect);
                return;
            }

            // check if the distance is valid
            if (!IsRectDistanceValid(player, rect)) {
                server.Log.Debug(GetString($"Bouncer / SendTileRect rejected from out of range from {player.Name}"));

                // send correcting rect
                player.SendTileRect(rect.TilePosX, rect.TilePosY, rect.Width, rect.Height);
                return;
            }

            // a very special case, due to the flower seed check otherwise hijacking this
            if (MatchesFlowerBoots(server, player, rect)) {
                server.Log.Debug(GetString($"Bouncer / SendTileRect reimplemented from {player.Name}"));

                // apply vanilla-style framing and broadcast
                FrameAndSyncRect(server, rect);
                return;
            }

            // check if the rect matches any valid operation
            foreach (TileRectMatch match in Matches) {
                var result = match.Matches(server, player, rect);
                if (result != TileRectMatch.MatchResult.NotMatched) {
                    server.Log.Debug(GetString($"Bouncer / SendTileRect reimplemented from {player.Name}"));

                    // send correcting rect
                    if (result == TileRectMatch.MatchResult.RejectChanges)
                        player.SendTileRect(rect.TilePosX, rect.TilePosY, rect.Width, rect.Height);
                    if (result == TileRectMatch.MatchResult.BroadcastChanges)
                        FrameAndSyncRect(server, rect);
                    return;
                }
            }

            // a few special cases
            if (MatchesGrassMow(server, player, rect) || MatchesChristmasTree(server, player, rect)) {
                server.Log.Debug(GetString($"Bouncer / SendTileRect reimplemented from {player.Name}"));

                // apply vanilla-style framing and broadcast
                FrameAndSyncRect(server, rect);
                return;
            }

            server.Log.Debug(GetString($"Bouncer / SendTileRect rejected from matches from {player.Name}"));

            // send correcting rect
            player.SendTileRect(rect.TilePosX, rect.TilePosY, rect.Width, rect.Height);
            return;
        }

		/// <summary>
		/// Calls tile framing and then syncs the rectangle to all clients.
		/// </summary>
		private static void FrameAndSyncRect(ServerContext server, SquareData rect)
		{
			server.WorldGen.RangeFrame(rect.TilePosX, rect.TilePosY, rect.TilePosX + rect.Width, rect.TilePosY + rect.Height);
			server.NetMessage.SendTileSquare(rect.TilePosX, rect.TilePosY, rect.Width, rect.Height);
		}

		/// <summary>
		/// Checks whether the tile rect is at a valid position for the given player.
		/// </summary>
		/// <param name="player">The player the operation originates from.</param>
		/// <param name="rect">The tile rectangle of the operation.</param>
		/// <returns><see langword="true"/>, if the rect at a valid position, otherwise <see langword="false"/>.</returns>
		private static bool IsRectPositionValid(TSPlayer player, SquareData rect)
		{
			var server = player.GetCurrentServer();
            for (int x = 0; x < rect.Width; x++)
			{
				for (int y = 0; y < rect.Height; y++)
				{
					int realX = rect.TilePosX + x;
					int realY = rect.TilePosY + y;

					if (realX < 0 || realX >= server.Main.maxTilesX || realY < 0 || realY >= server.Main.maxTilesY)
					{
						return false;
					}
				}
			}

			return true;
		}

		/// <summary>
		/// Checks whether the tile rect is at a valid distance to the given player.
		/// </summary>
		/// <param name="player">The player the operation originates from.</param>
		/// <param name="rect">The tile rectangle of the operation.</param>
		/// <returns><see langword="true"/>, if the rect at a valid distance, otherwise <see langword="false"/>.</returns>
		private static bool IsRectDistanceValid(TSPlayer player, SquareData rect)
		{
			for (int x = 0; x < rect.Width; x++)
			{
				for (int y = 0; y < rect.Height; y++)
				{
					int realX = rect.TilePosX + x;
					int realY = rect.TilePosY + y;

					if (!player.IsInRange(realX, realY))
					{
						return false;
					}
				}
			}

			return true;
		}


		/// <summary>
		/// Checks whether the tile rect is a valid conversion spread (Clentaminator, Powders, etc.).
		/// </summary>
		/// <param name="player">The player the operation originates from.</param>
		/// <param name="rect">The tile rectangle of the operation.</param>
		/// <returns><see langword="true"/>, if the rect matches a conversion spread operation, otherwise <see langword="false"/>.</returns>
		private static bool MatchesConversionSpread(ServerContext server, TSPlayer player, SquareData rect)
		{
			if (rect.Width != 1 || rect.Height != 1)
			{
				return false;
			}

			var oldTile = server.Main.tile[rect.TilePosX, rect.TilePosY];
			SimpleTileData newTile = rect.Tiles[0, 0];

			WorldGenMock.SimulateConversionChange(server, rect.TilePosX, rect.TilePosY, out HashSet<ushort> validTiles, out HashSet<ushort> validWalls);

			if (newTile.TileType != oldTile.type && validTiles.Contains(newTile.TileType))
			{
				if (TShock.TileBans.TileIsBanned((short)newTile.TileType, player))
				{
					// for simplicity, let's pretend that the edit was valid, but do not execute it
					return true;
				}
				else if (!player.HasBuildPermission(rect.TilePosX, rect.TilePosY))
				{
					// for simplicity, let's pretend that the edit was valid, but do not execute it
					return true;
				}
				else
				{
					server.Main.tile[rect.TilePosX, rect.TilePosY].type = newTile.TileType;
					server.Main.tile[rect.TilePosX, rect.TilePosY].frameX = newTile.FrameX;
					server.Main.tile[rect.TilePosX, rect.TilePosY].frameY = newTile.FrameY;

					return true;
				}
			}

			if (newTile.WallType != oldTile.wall && validWalls.Contains(newTile.WallType))
			{
				// wallbans when?

				if (!player.HasBuildPermission(rect.TilePosX, rect.TilePosY))
				{
					// for simplicity, let's pretend that the edit was valid, but do not execute it
					return true;
				}
				else
				{
					server.Main.tile[rect.TilePosX, rect.TilePosY].wall = newTile.WallType;

					return true;
				}
			}

			return false;
		}


		private static readonly Dictionary<ushort, HashSet<ushort>> PlantToGrassMap = new Dictionary<ushort, HashSet<ushort>>
		{
			{ TileID.Plants, new HashSet<ushort>()
			{
				TileID.Grass, TileID.GolfGrass
			} },
			{ TileID.HallowedPlants, new HashSet<ushort>()
			{
				TileID.HallowedGrass, TileID.GolfGrassHallowed
			} },
			{ TileID.HallowedPlants2, new HashSet<ushort>()
			{
				TileID.HallowedGrass, TileID.GolfGrassHallowed
			} },
			{ TileID.JunglePlants2, new HashSet<ushort>()
			{
				TileID.JungleGrass
			} },
			{ TileID.AshPlants, new HashSet<ushort>()
			{
				TileID.AshGrass
			} },
		};

		private static readonly Dictionary<ushort, HashSet<ushort>> GrassToStyleMap = new Dictionary<ushort, HashSet<ushort>>()
		{
			{ TileID.Plants, new HashSet<ushort>()
			{
				6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 24, 27, 30, 33, 36, 39, 42,
				22, 23, 25, 26, 28, 29, 31, 32, 34, 35, 37, 38, 40, 41, 43, 44,
			} },
			{ TileID.HallowedPlants, new HashSet<ushort>()
			{
				4, 6,
			} },
			{ TileID.HallowedPlants2, new HashSet<ushort>()
			{
				2, 3, 4, 6, 7,
			} },
			{ TileID.JunglePlants2, new HashSet<ushort>()
			{
				9, 10, 11, 12, 13, 14, 15, 16,
			} },
			{ TileID.AshPlants, new HashSet<ushort>()
			{
				6, 7, 8, 9, 10,
			} },
		};

		/// <summary>
		/// Checks whether the tile rect is a valid Flower Boots placement.
		/// </summary>
		/// <param name="player">The player the operation originates from.</param>
		/// <param name="rect">The tile rectangle of the operation.</param>
		/// <returns><see langword="true"/>, if the rect matches a Flower Boots placement, otherwise <see langword="false"/>.</returns>
		private static bool MatchesFlowerBoots(ServerContext server, TSPlayer player, SquareData rect)
		{
			if (rect.Width != 1 || rect.Height != 1)
			{
				return false;
			}

			if (!player.TPlayer.flowerBoots)
			{
				return false;
			}

			var oldTile = server.Main.tile[rect.TilePosX, rect.TilePosY];
			SimpleTileData newTile = rect.Tiles[0, 0];

			if (
				PlantToGrassMap.TryGetValue(newTile.TileType, out HashSet<ushort> grassTiles) &&
				!oldTile.active() && grassTiles.Contains(server.Main.tile[rect.TilePosX, rect.TilePosY + 1].type) &&
				GrassToStyleMap[newTile.TileType].Contains((ushort)(newTile.FrameX / 18))
			)
			{
				if (TShock.TileBans.TileIsBanned((short)newTile.TileType, player))
				{
					// for simplicity, let's pretend that the edit was valid, but do not execute it
					return true;
				}

				if (!player.HasBuildPermission(rect.TilePosX, rect.TilePosY))
				{
					// for simplicity, let's pretend that the edit was valid, but do not execute it
					return true;
				}

				server.Main.tile[rect.TilePosX, rect.TilePosY].active(active: true);
				server.Main.tile[rect.TilePosX, rect.TilePosY].type = newTile.TileType;
				server.Main.tile[rect.TilePosX, rect.TilePosY].frameX = newTile.FrameX;
				server.Main.tile[rect.TilePosX, rect.TilePosY].frameY = 0;

				return true;
			}

			return false;
		}


		private static readonly Dictionary<ushort, ushort> GrassToMowedMap = new Dictionary<ushort, ushort>
		{
			{ TileID.Grass, TileID.GolfGrass },
			{ TileID.HallowedGrass, TileID.GolfGrassHallowed },
		};

		/// <summary>
		/// Checks whether the tile rect is a valid grass mow.
		/// </summary>
		/// <param name="player">The player the operation originates from.</param>
		/// <param name="rect">The tile rectangle of the operation.</param>
		/// <returns><see langword="true"/>, if the rect matches a grass mowing operation, otherwise <see langword="false"/>.</returns>
		private static bool MatchesGrassMow(ServerContext server, TSPlayer player, SquareData rect)
		{
			if (rect.Width != 1 || rect.Height != 1)
			{
				return false;
			}

			var oldTile = server.Main.tile[rect.TilePosX, rect.TilePosY];
			SimpleTileData newTile = rect.Tiles[0, 0];

			if (GrassToMowedMap.TryGetValue(oldTile.type, out ushort mowed) && newTile.TileType == mowed)
			{
				if (TShock.TileBans.TileIsBanned((short)newTile.TileType, player))
				{
					// for simplicity, let's pretend that the edit was valid, but do not execute it
					return true;
				}

				if (!player.HasBuildPermission(rect.TilePosX, rect.TilePosY))
				{
					// for simplicity, let's pretend that the edit was valid, but do not execute it
					return true;
				}

				server.Main.tile[rect.TilePosX, rect.TilePosY].type = newTile.TileType;
				if (!newTile.FrameXYExist)
				{
					server.Main.tile[rect.TilePosX, rect.TilePosY].frameX = -1;
					server.Main.tile[rect.TilePosX, rect.TilePosY].frameY = -1;
				}

				// prevent a common crash when the game checks all vines in an unlimited horizontal length
				if (TileID.Sets.IsVine[server.Main.tile[rect.TilePosX, rect.TilePosY + 1].type])
				{
					server.WorldGen.KillTile(rect.TilePosX, rect.TilePosY + 1);
				}

				return true;
			}

			return false;
		}


		/// <summary>
		/// Checks whether the tile rect is a valid christmas tree modification.
		/// This also required significant reverse engineering effort.
		/// </summary>
		/// <param name="player">The player the operation originates from.</param>
		/// <param name="rect">The tile rectangle of the operation.</param>
		/// <returns><see langword="true"/>, if the rect matches a christmas tree operation, otherwise <see langword="false"/>.</returns>
		private static bool MatchesChristmasTree(ServerContext server, TSPlayer player, SquareData rect)
		{
			if (rect.Width != 1 || rect.Height != 1)
			{
				return false;
			}

			var oldTile = server.Main.tile[rect.TilePosX, rect.TilePosY];
			SimpleTileData newTile = rect.Tiles[0, 0];

			if (oldTile.type == TileID.ChristmasTree && newTile.TileType == TileID.ChristmasTree)
			{
				if (newTile.FrameX != 10)
				{
					return false;
				}

				int obj_0 = (newTile.FrameY & 0b0000000000000111);
				int obj_1 = (newTile.FrameY & 0b0000000000111000) >> 3;
				int obj_2 = (newTile.FrameY & 0b0000001111000000) >> 6;
				int obj_3 = (newTile.FrameY & 0b0011110000000000) >> 10;
				int obj_x = (newTile.FrameY & 0b1100000000000000) >> 14;

				if (obj_x != 0)
				{
					return false;
				}

				if (obj_0 is < 0 or > 4 || obj_1 is < 0 or > 6 || obj_2 is < 0 or > 11 || obj_3 is < 0 or > 11)
				{
					return false;
				}

				if (!player.HasBuildPermission(rect.TilePosX, rect.TilePosY))
				{
					// for simplicity, let's pretend that the edit was valid, but do not execute it
					return true;
				}

				server.Main.tile[rect.TilePosX, rect.TilePosY].frameY = newTile.FrameY;

				return true;
			}

			return false;
		}
    }

	/// <summary>
	/// This helper class allows simulating a `WorldGen.Convert` call and retrieving all valid changes for a given tile.
	/// </summary>
	internal static class WorldGenMock
	{
		/// <summary>
		/// This is a mock tile which collects all possible changes the `WorldGen.Convert` code could make in its property setters.
		/// </summary>
		private sealed class MockTile
		{
			private readonly HashSet<ushort> _setTypes;
			private readonly HashSet<ushort> _setWalls;

			private ushort _type;
			private ushort _wall;

			public MockTile(ushort type, ushort wall, HashSet<ushort> setTypes, HashSet<ushort> setWalls)
			{
				_setTypes = setTypes;
				_setWalls = setWalls;
				_type = type;
				_wall = wall;
			}

#pragma warning disable IDE1006

			public ushort type
			{
				get => _type;
				set
				{
					_setTypes.Add(value);
					_type = value;
				}
			}

			public ushort wall
			{
				get => _wall;
				set
				{
					_setWalls.Add(value);
					_wall = value;
				}
			}

#pragma warning restore IDE1006
		}

		/// <summary>
		/// Simulates what would happen if `WorldGen.Convert` was called on the given coordinates and returns two sets with the possible tile type and wall types that the conversion could change the tile to.
		/// </summary>
		public static void SimulateConversionChange(ServerContext server, int x, int y, out HashSet<ushort> validTiles, out HashSet<ushort> validWalls)
		{
			validTiles = new HashSet<ushort>();
			validWalls = new HashSet<ushort>();

			// all the conversion types used in the code, most apparent in Projectile ai 31
			foreach (int conversionType in new int[] { 0, 1, 2, 3, 4, 5, 6, 7 })
			{
				MockTile mock = new(server.Main.tile[x, y].type, server.Main.tile[x, y].wall, validTiles, validWalls);
				Convert(server, mock, x, y, conversionType);
			}
		}

		/*
		 * This is a copy of the `WorldGen.Convert` method with the following precise changes:
		 *  - Added a `MockTile tile` parameter
		 *  - Changed the `i` and `j` parameters to `k` and `l`
		 *  - Removed the size parameter
		 *  - Removed the area loop and `Tile tile = Main.tile[k, l]` access in favor of using the tile parameter
		 *  - Removed all calls to `WorldGen.SquareWallFrame`, `NetMessage.SendTileSquare`, `WorldGen.TryKillingTreesAboveIfTheyWouldBecomeInvalid`
		 *  - Changed all `continue` statements to `break` statements
		 *  - Removed the ifs checking the bounds of the tile and wall types
		 *  - Removed branches that would call `WorldGen.KillTile`
		 *  - Changed branches depending on randomness to instead set the property to both values after one another
		 *
		 * This overall leads to a method that can be called on a MockTile and real-world coordinates and will spit out the proper conversion changes into the MockTile.
		 */

		private static void Convert(ServerContext server, MockTile tile, int k, int l, int conversionType)
		{
			int type = tile.type;
			int wall = tile.wall;
			switch (conversionType)
			{
				case 4:
					if (WallID.Sets.Conversion.Grass[wall] && wall != 81)
					{
						tile.wall = 81;
					}
					else if (WallID.Sets.Conversion.Stone[wall] && wall != 83)
					{
						tile.wall = 83;
					}
					else if (WallID.Sets.Conversion.HardenedSand[wall] && wall != 218)
					{
						tile.wall = 218;
					}
					else if (WallID.Sets.Conversion.Sandstone[wall] && wall != 221)
					{
						tile.wall = 221;
					}
					else if (WallID.Sets.Conversion.NewWall1[wall] && wall != 192)
					{
						tile.wall = 192;
					}
					else if (WallID.Sets.Conversion.NewWall2[wall] && wall != 193)
					{
						tile.wall = 193;
					}
					else if (WallID.Sets.Conversion.NewWall3[wall] && wall != 194)
					{
						tile.wall = 194;
					}
					else if (WallID.Sets.Conversion.NewWall4[wall] && wall != 195)
					{
						tile.wall = 195;
					}
					if ((Main.tileMoss[type] || TileID.Sets.Conversion.Stone[type]) && type != 203)
					{
						tile.type = 203;
					}
					else if (TileID.Sets.Conversion.JungleGrass[type] && type != 662)
					{
						tile.type = 662;
					}
					else if (TileID.Sets.Conversion.Grass[type] && type != 199)
					{
						tile.type = 199;
					}
					else if (TileID.Sets.Conversion.Ice[type] && type != 200)
					{
						tile.type = 200;
					}
					else if (TileID.Sets.Conversion.Sand[type] && type != 234)
					{
						tile.type = 234;
					}
					else if (TileID.Sets.Conversion.HardenedSand[type] && type != 399)
					{
						tile.type = 399;
					}
					else if (TileID.Sets.Conversion.Sandstone[type] && type != 401)
					{
						tile.type = 401;
					}
					else if (TileID.Sets.Conversion.Thorn[type] && type != 352)
					{
						tile.type = 352;
					}
					break;
				case 2:
					if (WallID.Sets.Conversion.Grass[wall] && wall != 70)
					{
						tile.wall = 70;
					}
					else if (WallID.Sets.Conversion.Stone[wall] && wall != 28)
					{
						tile.wall = 28;
					}
					else if (WallID.Sets.Conversion.HardenedSand[wall] && wall != 219)
					{
						tile.wall = 219;
					}
					else if (WallID.Sets.Conversion.Sandstone[wall] && wall != 222)
					{
						tile.wall = 222;
					}
					else if (WallID.Sets.Conversion.NewWall1[wall] && wall != 200)
					{
						tile.wall = 200;
					}
					else if (WallID.Sets.Conversion.NewWall2[wall] && wall != 201)
					{
						tile.wall = 201;
					}
					else if (WallID.Sets.Conversion.NewWall3[wall] && wall != 202)
					{
						tile.wall = 202;
					}
					else if (WallID.Sets.Conversion.NewWall4[wall] && wall != 203)
					{
						tile.wall = 203;
					}
					if ((Main.tileMoss[type] || TileID.Sets.Conversion.Stone[type]) && type != 117)
					{
						tile.type = 117;
					}
					else if (TileID.Sets.Conversion.GolfGrass[type] && type != 492)
					{
						tile.type = 492;
					}
					else if (TileID.Sets.Conversion.Grass[type] && type != 109 && type != 492)
					{
						tile.type = 109;
					}
					else if (TileID.Sets.Conversion.Ice[type] && type != 164)
					{
						tile.type = 164;
					}
					else if (TileID.Sets.Conversion.Sand[type] && type != 116)
					{
						tile.type = 116;
					}
					else if (TileID.Sets.Conversion.HardenedSand[type] && type != 402)
					{
						tile.type = 402;
					}
					else if (TileID.Sets.Conversion.Sandstone[type] && type != 403)
					{
						tile.type = 403;
					}
					if (type == 59 && (server.Main.tile[k - 1, l].type == 109 || server.Main.tile[k + 1, l].type == 109 || server.Main.tile[k, l - 1].type == 109 || server.Main.tile[k, l + 1].type == 109))
					{
						tile.type = 0;
					}
					break;
				case 1:
					if (WallID.Sets.Conversion.Grass[wall] && wall != 69)
					{
						tile.wall = 69;
					}
					else if (TileID.Sets.Conversion.JungleGrass[type] && type != 661)
					{
						tile.type = 661;
					}
					else if (WallID.Sets.Conversion.Stone[wall] && wall != 3)
					{
						tile.wall = 3;
					}
					else if (WallID.Sets.Conversion.HardenedSand[wall] && wall != 217)
					{
						tile.wall = 217;
					}
					else if (WallID.Sets.Conversion.Sandstone[wall] && wall != 220)
					{
						tile.wall = 220;
					}
					else if (WallID.Sets.Conversion.NewWall1[wall] && wall != 188)
					{
						tile.wall = 188;
					}
					else if (WallID.Sets.Conversion.NewWall2[wall] && wall != 189)
					{
						tile.wall = 189;
					}
					else if (WallID.Sets.Conversion.NewWall3[wall] && wall != 190)
					{
						tile.wall = 190;
					}
					else if (WallID.Sets.Conversion.NewWall4[wall] && wall != 191)
					{
						tile.wall = 191;
					}
					if ((Main.tileMoss[type] || TileID.Sets.Conversion.Stone[type]) && type != 25)
					{
						tile.type = 25;
					}
					else if (TileID.Sets.Conversion.Grass[type] && type != 23)
					{
						tile.type = 23;
					}
					else if (TileID.Sets.Conversion.Ice[type] && type != 163)
					{
						tile.type = 163;
					}
					else if (TileID.Sets.Conversion.Sand[type] && type != 112)
					{
						tile.type = 112;
					}
					else if (TileID.Sets.Conversion.HardenedSand[type] && type != 398)
					{
						tile.type = 398;
					}
					else if (TileID.Sets.Conversion.Sandstone[type] && type != 400)
					{
						tile.type = 400;
					}
					else if (TileID.Sets.Conversion.Thorn[type] && type != 32)
					{
						tile.type = 32;
					}
					break;
				case 3:
					if (WallID.Sets.CanBeConvertedToGlowingMushroom[wall])
					{
						tile.wall = 80;
					}
					if (tile.type == 60)
					{
						tile.type = 70;
					}
					break;
				case 5:
					if ((WallID.Sets.Conversion.Stone[wall] || WallID.Sets.Conversion.NewWall1[wall] || WallID.Sets.Conversion.NewWall2[wall] || WallID.Sets.Conversion.NewWall3[wall] || WallID.Sets.Conversion.NewWall4[wall] || WallID.Sets.Conversion.Ice[wall] || WallID.Sets.Conversion.Sandstone[wall]) && wall != 187)
					{
						tile.wall = 187;
					}
					else if ((WallID.Sets.Conversion.HardenedSand[wall] || WallID.Sets.Conversion.Dirt[wall] || WallID.Sets.Conversion.Snow[wall]) && wall != 216)
					{
						tile.wall = 216;
					}
					if ((TileID.Sets.Conversion.Grass[type] || TileID.Sets.Conversion.Sand[type] || TileID.Sets.Conversion.Snow[type] || TileID.Sets.Conversion.Dirt[type]) && type != 53)
					{
						int num = 53;
						if (server.WorldGen.BlockBelowMakesSandConvertIntoHardenedSand(k, l))
						{
							num = 397;
						}
						tile.type = (ushort)num;
					}
					else if (TileID.Sets.Conversion.HardenedSand[type] && type != 397)
					{
						tile.type = 397;
					}
					else if ((Main.tileMoss[type] || TileID.Sets.Conversion.Stone[type] || TileID.Sets.Conversion.Ice[type] || TileID.Sets.Conversion.Sandstone[type]) && type != 396)
					{
						tile.type = 396;
					}
					break;
				case 6:
					if ((WallID.Sets.Conversion.Stone[wall] || WallID.Sets.Conversion.NewWall1[wall] || WallID.Sets.Conversion.NewWall2[wall] || WallID.Sets.Conversion.NewWall3[wall] || WallID.Sets.Conversion.NewWall4[wall] || WallID.Sets.Conversion.Ice[wall] || WallID.Sets.Conversion.Sandstone[wall]) && wall != 71)
					{
						tile.wall = 71;
					}
					else if ((WallID.Sets.Conversion.HardenedSand[wall] || WallID.Sets.Conversion.Dirt[wall] || WallID.Sets.Conversion.Snow[wall]) && wall != 40)
					{
						tile.wall = 40;
					}
					if ((TileID.Sets.Conversion.Grass[type] || TileID.Sets.Conversion.Sand[type] || TileID.Sets.Conversion.HardenedSand[type] || TileID.Sets.Conversion.Snow[type] || TileID.Sets.Conversion.Dirt[type]) && type != 147)
					{
						tile.type = 147;
					}
					else if ((Main.tileMoss[type] || TileID.Sets.Conversion.Stone[type] || TileID.Sets.Conversion.Ice[type] || TileID.Sets.Conversion.Sandstone[type]) && type != 161)
					{
						tile.type = 161;
					}
					break;
				case 7:
					if ((WallID.Sets.Conversion.Stone[wall] || WallID.Sets.Conversion.Ice[wall] || WallID.Sets.Conversion.Sandstone[wall]) && wall != 1)
					{
						tile.wall = 1;
					}
					else if ((WallID.Sets.Conversion.HardenedSand[wall] || WallID.Sets.Conversion.Snow[wall] || WallID.Sets.Conversion.Dirt[wall]) && wall != 2)
					{
						tile.wall = 2;
					}
					else if (WallID.Sets.Conversion.NewWall1[wall] && wall != 196)
					{
						tile.wall = 196;
					}
					else if (WallID.Sets.Conversion.NewWall2[wall] && wall != 197)
					{
						tile.wall = 197;
					}
					else if (WallID.Sets.Conversion.NewWall3[wall] && wall != 198)
					{
						tile.wall = 198;
					}
					else if (WallID.Sets.Conversion.NewWall4[wall] && wall != 199)
					{
						tile.wall = 199;
					}
					if ((TileID.Sets.Conversion.Stone[type] || TileID.Sets.Conversion.Ice[type] || TileID.Sets.Conversion.Sandstone[type]) && type != 1)
					{
						tile.type = 1;
					}
					else if (TileID.Sets.Conversion.GolfGrass[type] && type != 477)
					{
						tile.type = 477;
					}
					else if (TileID.Sets.Conversion.Grass[type] && type != 2 && type != 477)
					{
						tile.type = 2;
					}
					else if ((TileID.Sets.Conversion.Sand[type] || TileID.Sets.Conversion.HardenedSand[type] || TileID.Sets.Conversion.Snow[type] || TileID.Sets.Conversion.Dirt[type]) && type != 0)
					{
						int num2 = 0;
						if (server.WorldGen.TileIsExposedToAir(k, l))
						{
							num2 = 2;
						}
						tile.type = (ushort)num2;
					}
					break;
			}
			if (tile.wall == 69 || tile.wall == 70 || tile.wall == 81)
			{
				if (l < server.Main.worldSurface)
				{
					tile.wall = 65;
					tile.wall = 63;
				}
				else
				{
					tile.wall = 64;
				}
			}
			else if (WallID.Sets.Conversion.Stone[wall] && wall != 1 && wall != 262 && wall != 274 && wall != 61 && wall != 185)
			{
				tile.wall = 1;
			}
			else if (WallID.Sets.Conversion.Stone[wall] && wall == 262)
			{
				tile.wall = 61;
			}
			else if (WallID.Sets.Conversion.Stone[wall] && wall == 274)
			{
				tile.wall = 185;
			}
			if (WallID.Sets.Conversion.NewWall1[wall] && wall != 212)
			{
				tile.wall = 212;
			}
			else if (WallID.Sets.Conversion.NewWall2[wall] && wall != 213)
			{
				tile.wall = 213;
			}
			else if (WallID.Sets.Conversion.NewWall3[wall] && wall != 214)
			{
				tile.wall = 214;
			}
			else if (WallID.Sets.Conversion.NewWall4[wall] && wall != 215)
			{
				tile.wall = 215;
			}
			else if (tile.wall == 80)
			{
				tile.wall = 15;
				tile.wall = 64;
			}
			else if (WallID.Sets.Conversion.HardenedSand[wall] && wall != 216)
			{
				tile.wall = 216;
			}
			else if (WallID.Sets.Conversion.Sandstone[wall] && wall != 187)
			{
				tile.wall = 187;
			}
			if (tile.type == 492)
			{
				tile.type = 477;
			}
			else if (TileID.Sets.Conversion.JungleGrass[type] && type != 60)
			{
				tile.type = 60;
			}
			else if (TileID.Sets.Conversion.Grass[type] && type != 2 && type != 477)
			{
				tile.type = 2;
			}
			else if (TileID.Sets.Conversion.Stone[type] && type != 1)
			{
				tile.type = 1;
			}
			else if (TileID.Sets.Conversion.Sand[type] && type != 53)
			{
				tile.type = 53;
			}
			else if (TileID.Sets.Conversion.HardenedSand[type] && type != 397)
			{
				tile.type = 397;
			}
			else if (TileID.Sets.Conversion.Sandstone[type] && type != 396)
			{
				tile.type = 396;
			}
			else if (TileID.Sets.Conversion.Ice[type] && type != 161)
			{
				tile.type = 161;
			}
			else if (TileID.Sets.Conversion.MushroomGrass[type])
			{
				tile.type = 60;
			}
		}
	}
}
