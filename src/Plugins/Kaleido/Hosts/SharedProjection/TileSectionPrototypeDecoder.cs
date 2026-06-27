using System.Runtime.CompilerServices;
using Terraria;
using Terraria.ID;
using UnifierTSL.Servers;

namespace Kaleido.Hosting.SharedProjection
{
    public static class TileSectionPrototypeDecoder
    {
        public static void ReadSection(BinaryReader reader, ref TileData destination, int width, int height) {
            ReadSection(reader, ref destination, width, height, ReadOnlySpan<bool>.Empty);
        }

        public static void ReadSection(BinaryReader reader, global::UnifierTSL.Servers.ServerContext server, ref TileData destination, int width, int height) {
            ArgumentNullException.ThrowIfNull(server);
            ReadSection(reader, ref destination, width, height, server.Main.tileSolid);
        }

        public static void ReadSection(BinaryReader reader, ref TileData destination, int width, int height, ReadOnlySpan<bool> solidTiles) {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

            for (int x = 0; x < width; x++) {
                for (int y = 0; y < height; y++) {
                    byte flags4 = 0;
                    byte flags3 = 0;
                    byte flags2 = 0;
                    var tile = default(TileData);
                    byte flags1 = reader.ReadByte();
                    bool hasFlags2 = false;
                    if ((flags1 & 1) == 1) {
                        hasFlags2 = true;
                        flags2 = reader.ReadByte();
                    }
                    bool hasFlags3 = false;
                    if (hasFlags2 && (flags2 & 1) == 1) {
                        hasFlags3 = true;
                        flags3 = reader.ReadByte();
                    }
                    if (hasFlags3 && (flags3 & 1) == 1) {
                        flags4 = reader.ReadByte();
                    }

                    if ((flags1 & 2) == 2) {
                        tile.active(true);
                        int type;
                        if ((flags1 & 0x20) == 0x20) {
                            type = reader.ReadByte();
                            type = reader.ReadByte() << 8 | type;
                        }
                        else {
                            type = reader.ReadByte();
                        }

                        tile.type = (ushort)type;
                        if (Main.tileFrameImportant[type]) {
                            tile.frameX = reader.ReadInt16();
                            tile.frameY = reader.ReadInt16();
                            if (tile.type == TileID.MagicalIceBlock) {
                                tile.frameY = 0;
                            }
                        }
                        else {
                            tile.frameX = -1;
                            tile.frameY = -1;
                        }

                        if ((flags3 & 0x08) == 0x08) {
                            tile.color(reader.ReadByte());
                        }
                    }

                    if ((flags1 & 4) == 4) {
                        tile.wall = reader.ReadByte();
                        if (tile.wall >= WallID.Count) {
                            tile.wall = 0;
                        }
                        if ((flags3 & 0x10) == 0x10) {
                            tile.wallColor(reader.ReadByte());
                        }
                    }

                    byte liquidType = (byte)((flags1 & 0x18) >> 3);
                    if (liquidType != 0) {
                        tile.liquid = reader.ReadByte();
                        if ((flags3 & 0x80) == 0x80) {
                            tile.shimmer(true);
                        }
                        else if (liquidType == 2) {
                            tile.lava(true);
                        }
                        else if (liquidType == 3) {
                            tile.honey(true);
                        }
                    }

                    if (flags2 > 1) {
                        if ((flags2 & 0x02) == 0x02) {
                            tile.wire(true);
                        }
                        if ((flags2 & 0x04) == 0x04) {
                            tile.wire2(true);
                        }
                        if ((flags2 & 0x08) == 0x08) {
                            tile.wire3(true);
                        }

                        byte shape = (byte)((flags2 & 0x70) >> 4);
                        if (shape != 0 && (IsSolidTile(tile.type, solidTiles) || TileID.Sets.NonSolidSaveSlopes[tile.type])) {
                            if (shape == 1) {
                                tile.halfBrick(true);
                            }
                            else {
                                tile.slope((byte)(shape - 1));
                            }
                        }
                    }

                    if (flags3 > 1) {
                        if ((flags3 & 0x02) == 0x02) {
                            tile.actuator(true);
                        }
                        if ((flags3 & 0x04) == 0x04) {
                            tile.inActive(true);
                        }
                        if ((flags3 & 0x20) == 0x20) {
                            tile.wire4(true);
                        }
                        if ((flags3 & 0x40) == 0x40) {
                            tile.wall = (ushort)(reader.ReadByte() << 8 | tile.wall);
                            if (tile.wall >= WallID.Count) {
                                tile.wall = 0;
                            }
                        }
                    }

                    if (flags4 > 1) {
                        if ((flags4 & 0x02) == 0x02) {
                            tile.invisibleBlock(true);
                        }
                        if ((flags4 & 0x04) == 0x04) {
                            tile.invisibleWall(true);
                        }
                        if ((flags4 & 0x08) == 0x08) {
                            tile.fullbrightBlock(true);
                        }
                        if ((flags4 & 0x10) == 0x10) {
                            tile.fullbrightWall(true);
                        }
                    }

                    int sameCount = (byte)((flags1 & 0xC0) >> 6) switch {
                        0 => 0,
                        1 => reader.ReadByte(),
                        _ => reader.ReadInt16(),
                    };

                    PlaceTile(ref destination, width, in tile, x, y);
                    while (sameCount-- > 0) {
                        y++;
                        if (y >= height) {
                            throw new InvalidDataException("Tile section run length exceeded the destination height.");
                        }

                        var copy = default(TileData);
                        copy.CopyFrom(tile);
                        PlaceTile(ref destination, width, in copy, x, y);
                    }
                }
            }
        }

        private static bool IsSolidTile(ushort type, ReadOnlySpan<bool> solidTiles)
            => type < solidTiles.Length && solidTiles[type];

        private static void PlaceTile(ref TileData destination, int width, ref readonly TileData tile, int x, int y) {
            if (tile.active() || tile.wall > 0) {
                Unsafe.Add(ref destination, x + y * width) = tile;
            }
        }
    }
}
