using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria.ID;
using TrProtocol.NetPackets;
using TShockAPI.Extension;
using UnifierTSL.Events.Handlers;

namespace TShockAPI.Handlers
{
    internal class LandGolfBallInCupHandler : IPacketHandler<LandGolfBallInCup>
    {
        /// <summary>
        /// List of golf ball projectile IDs.
        /// </summary>
        public static readonly List<int> GolfBallProjectileIDs = new List<int>()
        {
            ProjectileID.DirtGolfBall,
            ProjectileID.GolfBallDyedBlack,
            ProjectileID.GolfBallDyedBlue,
            ProjectileID.GolfBallDyedBrown,
            ProjectileID.GolfBallDyedCyan,
            ProjectileID.GolfBallDyedGreen,
            ProjectileID.GolfBallDyedLimeGreen,
            ProjectileID.GolfBallDyedOrange,
            ProjectileID.GolfBallDyedPink,
            ProjectileID.GolfBallDyedPurple,
            ProjectileID.GolfBallDyedRed,
            ProjectileID.GolfBallDyedSkyBlue,
            ProjectileID.GolfBallDyedTeal,
            ProjectileID.GolfBallDyedViolet,
            ProjectileID.GolfBallDyedYellow
        };

        /// <summary>
        /// List of golf club item IDs
        /// </summary>
        public static readonly List<int> GolfClubItemIDs = new List<int>()
        {
            ItemID.GolfClubChlorophyteDriver,
            ItemID.GolfClubDiamondWedge,
            ItemID.GolfClubShroomitePutter,
            ItemID.Fake_BambooChest,
            ItemID.GolfClubTitaniumIron,
            ItemID.GolfClubGoldWedge,
            ItemID.GolfClubLeadPutter,
            ItemID.GolfClubMythrilIron,
            ItemID.GolfClubWoodDriver,
            ItemID.GolfClubBronzeWedge,
            ItemID.GolfClubRustyPutter,
            ItemID.GolfClubStoneIron,
            ItemID.GolfClubPearlwoodDriver,
            ItemID.GolfClubIron,
            ItemID.GolfClubDriver,
            ItemID.GolfClubWedge,
            ItemID.GolfClubPutter
        };
        /// <summary>
        /// List of golf ball item IDs.
        /// </summary>
        public static readonly List<int> GolfBallItemIDs = new List<int>()
        {
            ItemID.GolfBall,
            ItemID.GolfBallDyedBlack,
            ItemID.GolfBallDyedBlue,
            ItemID.GolfBallDyedBrown,
            ItemID.GolfBallDyedCyan,
            ItemID.GolfBallDyedGreen,
            ItemID.GolfBallDyedLimeGreen,
            ItemID.GolfBallDyedOrange,
            ItemID.GolfBallDyedPink,
            ItemID.GolfBallDyedPurple,
            ItemID.GolfBallDyedRed,
            ItemID.GolfBallDyedSkyBlue,
            ItemID.GolfBallDyedTeal,
            ItemID.GolfBallDyedViolet
        };
        public void OnReceive(ref RecievePacketEvent<LandGolfBallInCup> args) {
            var player = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var packetPlr = args.Packet.OtherPlayerSlot;
            var pos = args.Packet.Position;

            if (player.Index != packetPlr) {
                server.Log.Debug(GetString($"LandGolfBallInCupHandler: Packet rejected for ID spoofing. Expected {player.Index}, received {packetPlr} from {player.Name}."));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }

            if (pos.X > server.Main.maxTilesX || pos.X < 0
               || pos.Y > server.Main.maxTilesY || pos.Y < 0) {
                server.Log.Debug(GetString($"LandGolfBallInCupHandler: X and Y position is out of world bounds! - From {player.Name}"));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }

            if (!server.Main.tile[pos.X, pos.Y].active() && server.Main.tile[pos.X, pos.Y].type != TileID.GolfHole) {
                server.Log.Debug(GetString($"LandGolfBallInCupHandler: Tile at packet position X:{pos.X} Y:{pos.Y} is not a golf hole! - From {player.Name}"));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }

            if (!GolfBallProjectileIDs.Contains(args.Packet.ProjType)) {
                server.Log.Debug(GetString($"LandGolfBallInCupHandler: Invalid golf ball projectile ID {args.Packet.ProjType}! - From {player.Name}"));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }

            var usedGolfBall = player.RecentlyCreatedProjectiles.Any(e => GolfBallProjectileIDs.Contains(e.Type));
            var usedGolfClub = player.RecentlyCreatedProjectiles.Any(e => e.Type == ProjectileID.GolfClubHelper);
            if (!usedGolfClub && !usedGolfBall) {
                server.Log.Debug(GetString($"GolfPacketHandler: Player did not have create a golf club projectile the last 5 seconds! - From {player.Name}"));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }

            if (!GolfClubItemIDs.Contains(player.SelectedItem.type)) {
                server.Log.Debug(GetString($"LandGolfBallInCupHandler: Item selected is not a golf club! - From {player.Name}"));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }
        }
    }
}
