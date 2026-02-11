using Terraria;
using TrProtocol.NetPackets;

namespace UnifierTSL.Extensions
{
    public static class PacketExt
    {
        public static SyncPlayer CreateSyncPacket(this Player player, int whoAmI = -1) {
            BitsByte b1 = 0;
            for (int i = 0; i < 8; i++) {
                b1[i] = player.hideVisibleAccessory[i];
            }
            BitsByte b2 = 0;
            for (int i = 0; i < 2; i++) {
                b2[i] = player.hideVisibleAccessory[i + 8];
            }
            BitsByte b3 = 0;
            if (player.difficulty == 1) {
                b3[0] = true;
            }
            else if (player.difficulty == 2) {
                b3[1] = true;
            }
            else if (player.difficulty == 3) {
                b3[3] = true;
            }
            b3[2] = player.extraAccessory;

            BitsByte b4 = 0;
            b4[0] = player.UsingBiomeTorches;
            b4[1] = player.happyFunTorchTime;
            b4[2] = player.unlockedBiomeTorches;
            b4[3] = player.unlockedSuperCart;
            b4[4] = player.enabledSuperCart;
            BitsByte b5 = 0;
            b5[0] = player.usedAegisCrystal;
            b5[1] = player.usedAegisFruit;
            b5[2] = player.usedArcaneCrystal;
            b5[3] = player.usedGalaxyPearl;
            b5[4] = player.usedGummyWorm;
            b5[5] = player.usedAmbrosia;
            b5[6] = player.ateArtisanBread;

            return new SyncPlayer(
                (byte)(whoAmI == -1 ? player.whoAmI : whoAmI),
                (byte)player.skinVariant,
                (byte)player.voiceVariant,
                player.voicePitchOffset,
                (byte)player.hair,
                player.name.Trim(),
                player.hairDye,
                b1,
                b2,
                player.hideMisc,
                player.hairColor,
                player.skinColor,
                player.eyeColor,
                player.shirtColor,
                player.underShirtColor,
                player.pantsColor,
                player.shoeColor,
                b3,
                b4,
                b5);
        }
        public static void ApplySyncPlayerPacket(this Player player, in SyncPlayer sync, bool ignoreSlot = true) {
            if (!ignoreSlot) {
                player.whoAmI = sync.PlayerSlot;
            }
            player.skinVariant = sync.SkinVariant;
            player.hair = sync.Hair;
            player.name = sync.Name.Trim();
            player.hairDye = sync.HairDye;

            for (int i = 0; i < 8; i++) {
                player.hideVisibleAccessory[i] = sync.Bit1[i];
            }
            for (int i = 0; i < 2; i++) {
                player.hideVisibleAccessory[i + 8] = sync.Bit2[i];
            }

            player.hideMisc = sync.HideMisc;
            player.hairColor = sync.HairColor;
            player.skinColor = sync.SkinColor;
            player.eyeColor = sync.EyeColor;
            player.shirtColor = sync.ShirtColor;
            player.underShirtColor = sync.UnderShirtColor;
            player.pantsColor = sync.PantsColor;
            player.shoeColor = sync.ShoeColor;

            if (sync.Bit3[0]) {
                player.difficulty = 1;
            }
            else if (sync.Bit3[1]) {
                player.difficulty = 2;
            }
            else if (sync.Bit3[3]) {
                player.difficulty = 3;
            }
            else {
                player.difficulty = 0;
            }
            player.extraAccessory = sync.Bit3[2];

            player.UsingBiomeTorches = sync.Bit4[0];
            player.happyFunTorchTime = sync.Bit4[1];
            player.unlockedBiomeTorches = sync.Bit4[2];
            player.unlockedSuperCart = sync.Bit4[3];
            player.enabledSuperCart = sync.Bit4[4];

            player.usedAegisCrystal = sync.Bit5[0];
            player.usedAegisFruit = sync.Bit5[1];
            player.usedArcaneCrystal = sync.Bit5[2];
            player.usedGalaxyPearl = sync.Bit5[3];
            player.usedGummyWorm = sync.Bit5[4];
            player.usedAmbrosia = sync.Bit5[5];
            player.ateArtisanBread = sync.Bit5[6];
        }
    }
}
