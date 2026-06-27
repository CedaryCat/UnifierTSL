using Terraria;

namespace Kaleido.Hosting.SharedProjection
{
    internal sealed class ProjectionEntities
    {
        public ProjectionEntities() {
            for (int i = 0; i < Items.Length; i++) {
                Items[i] = new WorldItem { whoAmI = i };
            }
            for (int i = 0; i < Players.Length; i++) {
                Players[i] = new Player { whoAmI = i };
            }
            for (int i = 0; i < Projectiles.Length; i++) {
                Projectiles[i] = new Projectile { whoAmI = i, identity = i };
            }
            for (int i = 0; i < Npcs.Length; i++) {
                Npcs[i] = new NPC { whoAmI = i };
            }

            var dust = new Dust();
            for (int i = 0; i < Dusts.Length; i++) {
                Dusts[i] = dust;
            }

            var gore = new Gore();
            for (int i = 0; i < Gores.Length; i++) {
                Gores[i] = gore;
            }
        }

        public readonly WorldItem[] Items = new WorldItem[Terraria.Main.maxItems + 1];
        public readonly Player[] Players = new Player[Terraria.Main.maxPlayers + 1];
        public readonly Projectile[] Projectiles = new Projectile[Terraria.Main.maxProjectiles + 1];
        public readonly NPC[] Npcs = new NPC[Terraria.Main.maxNPCs + 1];
        public readonly Dust[] Dusts = new Dust[Terraria.Main.maxDust + 1];
        public readonly Gore[] Gores = new Gore[Terraria.Main.maxGore + 1];
    }
}
