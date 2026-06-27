using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kaleido.Hosting;
using Kaleido.Hosting.SharedProjection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.IO;
using TrProtocol.Models;
using TrProtocol.Models.TileEntities;
using TrProtocol.NetPackets;
using UnifierTSL.Servers;

namespace Kaleido.LoginLobby
{

    internal sealed class FlatLoginLobbyWorldDataProvider : IProjectionWorldDataProvider
    {
        public static readonly FlatLoginLobbyWorldDataProvider Instance = new();
        public const int WorldWidth = 4200;
        public const int WorldHeight = 1200;
        public const int SpawnX = 2100;
        public const int SpawnY = 336;
        public const int NoonTime = 27000;
        public const int WorldSurfaceTile = 350;
        public const int RockLayerTile = 480;
        public const int StructureWidth = 24;
        public const int StructureHeight = 28;
        public const int SpawnLocalX = 15;
        public const int SpawnLocalY = 15;
        public const int BuildingOriginX = SpawnX - SpawnLocalX;
        public const int BuildingOriginY = SpawnY - SpawnLocalY;
        public const int TileSize = 16;
        public const int RackEntityId = 0;

        private const int TileDataBinarySize = 14;
        private static readonly TileData[] PrototypeTiles = LoadPrototypeTiles();

        static FlatLoginLobbyWorldDataProvider() {
            RackPosition = FindWeaponRackPosition(PrototypeTiles);
            RackPlacedSection = CreateRackPlacedSection(PrototypeTiles);
        }

        private FlatLoginLobbyWorldDataProvider() { }

        public static Point16 RackPosition { get; }
        public static TileSection RackPlacedSection { get; }
        public string WorldName => "Kaleido Gate";
        public string WorldFileName => "KaleidoGate.wld";
        public int MaxTilesX => WorldWidth;
        public int MaxTilesY => WorldHeight;
        public int SpawnTileX => SpawnX;
        public int SpawnTileY => SpawnY;
        public double WorldSurface => WorldSurfaceTile;
        public double RockLayer => RockLayerTile;
        public Guid UniqueId { get; } = Guid.Parse("79ae4700-fd8a-4b34-a43b-3edfc7b0a3a4");
        public int WorldId => 0x4B4C444F;

        public WorldFileData ApplyMetadata(ServerContext server) {
            var worldFileData = new WorldFileData {
                GameMode = GameModeID.Master,
                _seedText = WorldId.ToString(),
                _seed = WorldId,
                WorldId = WorldId,
                WorldGeneratorVersion = WorldFileData.GUID_IN_WORLD_FILE_VERSION,
                Metadata = FileMetadata.FromCurrentSettings(FileType.World),
                HasCorruption = true,
                HasCrimson = true,
                IsHardMode = true,
                Name = WorldFileName,
                UniqueId = UniqueId,
                CreationTime = DateTime.Now
            };
            worldFileData.SetWorldSize(MaxTilesX, MaxTilesY);

            server.Main.worldName = WorldName;
            server.Main.maxTilesX = MaxTilesX;
            server.Main.maxTilesY = MaxTilesY;
            server.Main.spawnTileX = SpawnTileX;
            server.Main.spawnTileY = SpawnTileY;
            server.Main.worldSurface = WorldSurface;
            server.Main.rockLayer = RockLayer;
            server.Main.ActiveWorldFileData = worldFileData;
            server.Main.dayTime = true;
            server.Main.time = NoonTime;
            server.CreativePowerManager.GetPower<Terraria.GameContent.Creative.CreativePowers.FreezeTime>().Enabled = true;

            return worldFileData;
        }

        public TileCollection CreateTileProvider() => new LobbyTileProvider();

        public static bool IsRackPosition(Point16 position) => position.X == RackPosition.X && position.Y == RackPosition.Y;

        public static bool IsRackArea(Point16 position) {
            int x = position.X - RackPosition.X;
            int y = position.Y - RackPosition.Y;
            return (uint)x < 3 && (uint)y < 3;
        }

        public static Vector2 GetPlayerStandPosition(Player player) {
            return new Vector2(
                SpawnX * TileSize + TileSize / 2f - player.width / 2f,
                SpawnY * TileSize - player.height);
        }

        public WorldData CreateWorldDataPacket(ServerContext server) {
            return new WorldData(
                (short)MaxTilesX,
                (short)MaxTilesY,
                (short)SpawnTileX,
                (short)SpawnTileY,
                WorldName,
                UniqueId.ToByteArray(),
                [],
                _Time: NoonTime,
                _DayAndMoonInfo: new BitsByte(true, false, false, false, false, false, false, false),
                _WorldID: WorldId,
                _WorldGeneratorVersion: WorldFileData.GUID_IN_WORLD_FILE_VERSION,
                _EventInfo1: new BitsByte(false, false, false, false, false, false, true, false),
                _WorldSurface: (short)WorldSurface,
                _RockLayer: (short)RockLayer);
        }

        private static TileData[] LoadPrototypeTiles() {
            if (Unsafe.SizeOf<TileData>() != TileDataBinarySize) {
                throw new InvalidOperationException($"Unexpected {nameof(TileData)} size. Expected {TileDataBinarySize}, got {Unsafe.SizeOf<TileData>()}.");
            }

            var bytes = Resource.lobby_building;
            if (bytes.Length != StructureWidth * StructureHeight * TileDataBinarySize) {
                throw new InvalidOperationException($"Lobby structure has {bytes.Length} bytes, expected {StructureWidth * StructureHeight * TileDataBinarySize}.");
            }

            var tiles = new TileData[StructureWidth * StructureHeight];
            MemoryMarshal.Cast<byte, TileData>(bytes).CopyTo(tiles);
            return tiles;
        }

        private static Point16 FindWeaponRackPosition(TileData[] tiles) {
            for (int x = 0; x < StructureWidth; x++) {
                for (int y = 0; y < StructureHeight; y++) {
                    ref var tile = ref tiles[x * StructureHeight + y];
                    if (tile.active() && (tile.type is TileID.WeaponsRack or TileID.WeaponsRack2) && tile.frameX == 0 && tile.frameY == 0) {
                        return ToWorldPosition(x, y);
                    }
                }
            }

            throw new InvalidOperationException("Lobby structure does not contain a weapon rack anchor tile.");
        }

        private static Point16 ToWorldPosition(int localX, int localY) => new(BuildingOriginX + localX, BuildingOriginY + localY);

        private static TileSection CreateRackPlacedSection(TileData[] tiles) {
            int localX = RackPosition.X - BuildingOriginX;
            int localY = RackPosition.Y - BuildingOriginY;
            var sectionTiles = new ComplexTileData[9];
            for (int x = 0; x < 3; x++) {
                for (int y = 0; y < 3; y++) {
                    sectionTiles[y * 3 + x] = CreateComplexTileData(in tiles[(localX + x) * StructureHeight + localY + y]);
                }
            }

            return new TileSection(new SectionData(
                RackPosition.X,
                RackPosition.Y,
                3,
                3,
                sectionTiles,
                0,
                [],
                0,
                [],
                1,
                [CreateRackEntity()]));
        }

        private static TEWeaponsRack CreateRackEntity() {
            return new TEWeaponsRack(RackEntityId, RackPosition, new() {
                ItemID = ItemID.LargeEmerald,
                Stack = 1,
                Prefix = 0
            });
        }

        // Mirrors vanilla TileSection tile flag encoding so the cached 3x3 packet can be sent without real TileEntity state.
        private static ComplexTileData CreateComplexTileData(ref readonly TileData tile) {
            byte flags1 = 0;
            byte flags2 = 0;
            byte flags3 = 0;
            byte flags4 = 0;

            if (tile.active()) {
                flags1 |= 0x02;
                if (tile.type > byte.MaxValue) {
                    flags1 |= 0x20;
                }

                if (tile.color() != 0) {
                    flags3 |= 0x08;
                }
            }

            if (tile.wall != 0) {
                flags1 |= 0x04;
                if (tile.wallColor() != 0) {
                    flags3 |= 0x10;
                }

                if (tile.wall > byte.MaxValue) {
                    flags3 |= 0x40;
                }
            }

            if (tile.liquid != 0) {
                if (tile.shimmer()) {
                    flags1 |= 0x08;
                    flags3 |= 0x80;
                }
                else if (tile.lava()) {
                    flags1 |= 0x10;
                }
                else if (tile.honey()) {
                    flags1 |= 0x18;
                }
                else {
                    flags1 |= 0x08;
                }
            }

            if (tile.wire()) {
                flags2 |= 0x02;
            }
            if (tile.wire2()) {
                flags2 |= 0x04;
            }
            if (tile.wire3()) {
                flags2 |= 0x08;
            }
            if (tile.halfBrick()) {
                flags2 |= 0x10;
            }
            else if (tile.slope() != 0) {
                flags2 |= (byte)((tile.slope() + 1) << 4);
            }

            if (tile.actuator()) {
                flags3 |= 0x02;
            }
            if (tile.inActive()) {
                flags3 |= 0x04;
            }
            if (tile.wire4()) {
                flags3 |= 0x20;
            }

            if (tile.invisibleBlock()) {
                flags4 |= 0x02;
            }
            if (tile.invisibleWall()) {
                flags4 |= 0x04;
            }
            if (tile.fullbrightBlock()) {
                flags4 |= 0x08;
            }
            if (tile.fullbrightWall()) {
                flags4 |= 0x10;
            }

            if (flags4 != 0) {
                flags3 |= 0x01;
            }
            if (flags3 != 0) {
                flags2 |= 0x01;
            }
            if (flags2 != 0) {
                flags1 |= 0x01;
            }

            return new ComplexTileData(
                (ComplexTileFlags1)flags1,
                (ComplexTileFlags2)flags2,
                (ComplexTileFlags3)flags3,
                (ComplexTileFlags4)flags4,
                (byte)tile.type,
                tile.type,
                tile.frameX,
                tile.frameY,
                tile.color(),
                (byte)tile.wall,
                tile.wallColor(),
                tile.liquid,
                (byte)(tile.wall >> 8),
                0,
                0);
        }

        private sealed class LobbyTileProvider : TileCollection
        {
            private const nint TileByteSize = TileDataBinarySize;
            private readonly TileData[] buildingTiles = new TileData[StructureWidth * StructureHeight];
            private TileData emptyTile;

            unsafe static LobbyTileProvider() {
                delegate*<object, nint, ref TileData> fptr = &RefTileData_GetTileRef;
                fptr_GetTileRef = (nint)fptr;
            }

            public LobbyTileProvider() {
                PrototypeTiles.AsSpan().CopyTo(buildingTiles);
            }

            public override ref TileData this[int x, int y] {
                get {
                    x -= BuildingOriginX;
                    y -= BuildingOriginY;
                    if ((uint)x < StructureWidth && (uint)y < StructureHeight) {
                        return ref Unsafe.AddByteOffset(
                            ref MemoryMarshal.GetArrayDataReference(buildingTiles),
                            (x * StructureHeight + y) * TileByteSize);
                    }

                    return ref emptyTile;
                }
            }

            public sealed override int Width => WorldWidth;
            public sealed override int Height => WorldHeight;
            public override void Dispose() { }

            private static readonly nint fptr_GetTileRef;

            private static ref TileData RefTileData_GetTileRef(object self, nint unmanagedData) {
                return ref Unsafe.As<LobbyTileProvider>(self)[(short)((uint)unmanagedData >> 16), (short)((int)unmanagedData & 0xFFFF)];
            }

            public unsafe sealed override RefTileData GetRefTile(int x, int y) => new(
                this,
                (nint)(uint)(((ushort)x << 16) | (ushort)y),
                (delegate*<object?, nint, ref TileData>)fptr_GetTileRef);
        }
    }
}
