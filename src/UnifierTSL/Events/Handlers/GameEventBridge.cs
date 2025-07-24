using Microsoft.Xna.Framework;
using UnifiedServerProcess;
using UnifierTSL.Events.Core;

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
            var data = new GameHardmodeTileUpdateEvent(x, y, type, self.root);
            GameHardmodeTileUpdate.Invoke(data, out var handled);
            return !handled;
        }

        private bool OnHardmodeTilePlace(On.OTAPI.HooksSystemContext.WorldGenSystemContext.orig_InvokeHardmodeTilePlace orig, 
            OTAPI.HooksSystemContext.WorldGenSystemContext self, int x, int y, int type, bool mute, bool forced, int plr, int style) {
            var data = new GameHardmodeTileUpdateEvent(x, y, type, self.root);
            GameHardmodeTileUpdate.Invoke(data, out var handled);
            return !handled;
        }

        private void OnStartServer(On.Terraria.NetplaySystemContext.orig_StartServer orig, Terraria.NetplaySystemContext self) {
            var data = new ServerEvent(self.root);
            GamePostInitialize.Invoke(data);
            orig(self);
        }

        private void OnInitialize(On.Terraria.Main.orig_Initialize orig, Terraria.Main self, RootContext root) {
            var data = new ServerEvent(root);
            GameInitialize.Invoke(data);
            orig(self, root);
        }

        private void OnUpdate(On.Terraria.Main.orig_Update orig, Terraria.Main self, RootContext root, GameTime gameTime) {
            var data = new ServerEvent(root);
            PreUpdate.Invoke(data);
            orig(self, root, gameTime);
            PostUpdate.Invoke(data);
        }
    }
    public readonly struct ServerEvent(RootContext server) : IServerEventContent<RootContext> {
        public readonly RootContext Server { get; } = server;
    }
    public readonly struct GameHardmodeTileUpdateEvent(int x, int y, int type, RootContext server) : IServerEventContent<RootContext>
    {
        public readonly int X = x;
        public readonly int Y = y;
        public readonly int Type = type;
        public readonly RootContext Server { get; } = server;
    }
}
