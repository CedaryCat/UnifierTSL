using Microsoft.Xna.Framework;
using UnifiedServerProcess;
using UnifierTSL.Events.Core;
using UnifierTSL.Extensions;
using UnifierTSL.Servers;

namespace UnifierTSL.Events.Handlers
{
    public class GameEventBridge
    {
        public readonly ReadonlyEventNoCancelProvider<ServerEvent> GameInitialize = new();
        public readonly ReadonlyEventNoCancelProvider<ServerEvent> GamePostInitialize = new();
        public readonly ReadonlyEventNoCancelProvider<ServerEvent> PreUpdate = new();
        public readonly ReadonlyEventNoCancelProvider<ServerEvent> PostUpdate = new();
        public readonly ReadonlyEventProvider<GameHardmodeTileUpdateEvent> GameHardmodeTileUpdate = new();
        public GameEventBridge() {
            On.Terraria.Main.Update += OnUpdate;
            On.Terraria.Main.Initialize += OnInitialize;
            On.Terraria.NetplaySystemContext.StartServer += OnStartServer;

            On.OTAPI.HooksSystemContext.WorldGenSystemContext.InvokeHardmodeTilePlace += OnHardmodeTilePlace;
            On.OTAPI.HooksSystemContext.WorldGenSystemContext.InvokeHardmodeTileUpdate += OnHardmodeTileUpdate;
        }

        private bool OnHardmodeTileUpdate(On.OTAPI.HooksSystemContext.WorldGenSystemContext.orig_InvokeHardmodeTileUpdate orig,
            OTAPI.HooksSystemContext.WorldGenSystemContext self, int x, int y, ushort type) {
            GameHardmodeTileUpdateEvent data = new(x, y, type, self.root.ToServer());
            GameHardmodeTileUpdate.Invoke(data, out bool handled);
            return !handled;
        }

        private bool OnHardmodeTilePlace(On.OTAPI.HooksSystemContext.WorldGenSystemContext.orig_InvokeHardmodeTilePlace orig,
            OTAPI.HooksSystemContext.WorldGenSystemContext self, int x, int y, int type, bool mute, bool forced, int plr, int style) {
            GameHardmodeTileUpdateEvent data = new(x, y, type, self.root.ToServer());
            GameHardmodeTileUpdate.Invoke(data, out bool handled);
            return !handled;
        }

        private void OnStartServer(On.Terraria.NetplaySystemContext.orig_StartServer orig, Terraria.NetplaySystemContext self) {
            orig(self);
            ServerEvent data = new(self.root.ToServer());
            GamePostInitialize.Invoke(data);
        }

        private void OnInitialize(On.Terraria.Main.orig_Initialize orig, Terraria.Main self, RootContext root) {
            ServerEvent data = new(root.ToServer());
            GameInitialize.Invoke(data);
            orig(self, root);
        }

        private void OnUpdate(On.Terraria.Main.orig_Update orig, Terraria.Main self, RootContext root, GameTime gameTime) {
            ServerEvent data = new(root.ToServer());
            PreUpdate.Invoke(data);
            orig(self, root, gameTime);
            PostUpdate.Invoke(data);
        }
    }
    public readonly struct ServerEvent(ServerContext server) : IServerEventContent
    {
        public readonly ServerContext Server { get; } = server;
    }
    public readonly struct GameHardmodeTileUpdateEvent(int x, int y, int type, ServerContext server) : IServerEventContent
    {
        public readonly int X = x;
        public readonly int Y = y;
        public readonly int Type = type;
        public readonly ServerContext Server { get; } = server;
    }
}
