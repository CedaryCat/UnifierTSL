using System.Runtime.CompilerServices;
using Terraria.Net.Sockets;
using TrProtocol;
using TrProtocol.Interfaces;
using TrProtocol.Models;
using TrProtocol.NetPackets;
using TrProtocol.NetPackets.Mobile;
using TrProtocol.NetPackets.Modules;

namespace UnifierTSL.Network
{
    public delegate void SentPacketHandler(PacketSender self, MessageID packetType);
    public abstract unsafe class PacketSender
    {
        public static event SentPacketHandler? SentPacket;
        public abstract void SendData(byte[] data, int index, int size);
        public abstract void SendData(byte[] data, int index, int size, SocketSendCallback callback, object? state = null);
        protected abstract void SendDataAndFreeBuffer(byte[] buffer, int index, int size);
        protected abstract void SendDataAndFreeBuffer(byte[] buffer, int index, int size, SocketSendCallback callback, object? state = null);
        protected abstract byte[] AllocateBuffer(int size);
        private void SendPacketInner<TNetPacket>(scoped in TNetPacket packet, int bufferSize, SocketSendCallback? callback, object? state) where TNetPacket : struct, INetPacket {
            byte[] buffer = AllocateBuffer(bufferSize);
            WriteData(buffer, in packet, out int size);
            if (callback is null) {
                SendDataAndFreeBuffer(buffer, 0, size);
            }
            else {
                SendDataAndFreeBuffer(buffer, 0, size, callback, state);
            }
            SentPacket?.Invoke(this, packet.Type);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected unsafe void WriteData<TNetPacket>(byte[] buffer, scoped in TNetPacket packet, out int size) where TNetPacket : struct, INetPacket {
            fixed (void* ptr = buffer) {
                void* ptr_current = Unsafe.Add<short>(ptr, 1);
                packet.WriteContent(ref ptr_current);
                short size_short = (short)((long)ptr_current - (long)ptr);
                Unsafe.Write(ptr, size_short);
                size = size_short;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendFixedPacket<TNetPacket>(scoped in TNetPacket packet) where TNetPacket : unmanaged, INonSideSpecific, INetPacket
            => SendPacketInner(in packet, sizeof(TNetPacket) + 4, null, null);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendFixedPacket<TNetPacket>(scoped in TNetPacket packet, SocketSendCallback callback, object? state = null) where TNetPacket : unmanaged, INonSideSpecific, INetPacket
            => SendPacketInner(in packet, sizeof(TNetPacket) + 4, callback, state);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendDynamicPacket<TNetPacket>(scoped in TNetPacket packet) where TNetPacket : struct, IManagedPacket, INonSideSpecific, INetPacket
            => SendPacketInner(in packet, packet.Type == MessageID.TileSection ? 1024 * 16 : 1024, null, null);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendDynamicPacket<TNetPacket>(scoped in TNetPacket packet, SocketSendCallback callback, object? state = null) where TNetPacket : struct, IManagedPacket, INonSideSpecific, INetPacket
            => SendPacketInner(in packet, packet.Type == MessageID.TileSection ? 1024 * 16 : 1024, callback, state);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendFixedPacket_S<TNetPacket>(scoped in TNetPacket packet) where TNetPacket : unmanaged, ISideSpecific, INetPacket {
            ref TNetPacket p = ref Unsafe.AsRef(in packet);
            p.IsServerSide = true;
            SendPacketInner(in packet, sizeof(TNetPacket) + 4, null, null);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendFixedPacket_S<TNetPacket>(scoped in TNetPacket packet, SocketSendCallback callback, object? state = null) where TNetPacket : unmanaged, ISideSpecific, INetPacket {
            ref TNetPacket p = ref Unsafe.AsRef(in packet);
            p.IsServerSide = true;
            SendPacketInner(in packet, sizeof(TNetPacket) + 4, callback, state);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendDynamicPacket_S<TNetPacket>(scoped in TNetPacket packet) where TNetPacket : struct, IManagedPacket, ISideSpecific, INetPacket {
            ref TNetPacket p = ref Unsafe.AsRef(in packet);
            p.IsServerSide = true;
            SendPacketInner(in packet, packet.Type == MessageID.TileSection ? 1024 * 16 : 1024, null, null);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendDynamicPacket_S<TNetPacket>(scoped in TNetPacket packet, SocketSendCallback callback, object? state = null) where TNetPacket : struct, IManagedPacket, ISideSpecific, INetPacket {
            ref TNetPacket p = ref Unsafe.AsRef(in packet);
            p.IsServerSide = true;
            SendPacketInner(in packet, packet.Type == MessageID.TileSection ? 1024 * 16 : 1024, callback, state);
        }
        public void SendUnknownPacket<TNetPacket>(scoped in TNetPacket packet) where TNetPacket : struct, INetPacket
            => SendUnknownPacket(in packet, null!, null);
        public void SendUnknownPacket<TNetPacket>(scoped in TNetPacket packet, SocketSendCallback callback, object? state = null) where TNetPacket : struct, INetPacket {
            // D - F
            // D is Dynamic, means the packet has no max length
            // F is Fixed, means the packet has a max length

            // L is LengthAware, means the packet requires knowledge of its total serialized length when deserializing.
            // S is SideSpecific, means the packet requires side-specific (client/server) handling during packet serialization/deserialization.
            switch (packet.Type) {
                case MessageID.ClientHello: SendDynamicPacket(in Unsafe.As<TNetPacket, ClientHello>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.Kick: SendDynamicPacket(in Unsafe.As<TNetPacket, Kick>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.LoadPlayer: SendFixedPacket(in Unsafe.As<TNetPacket, LoadPlayer>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncPlayer: SendDynamicPacket(in Unsafe.As<TNetPacket, SyncPlayer>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncEquipment: SendFixedPacket(in Unsafe.As<TNetPacket, SyncEquipment>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.RequestWorldInfo: SendFixedPacket(in Unsafe.As<TNetPacket, RequestWorldInfo>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.WorldData: SendDynamicPacket(in Unsafe.As<TNetPacket, WorldData>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.RequestTileData: SendFixedPacket(in Unsafe.As<TNetPacket, RequestTileData>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.StatusText: SendDynamicPacket(in Unsafe.As<TNetPacket, StatusText>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TileSection: SendDynamicPacket(in Unsafe.As<TNetPacket, TileSection>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.FrameSection: SendFixedPacket(in Unsafe.As<TNetPacket, FrameSection>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SpawnPlayer: SendFixedPacket(in Unsafe.As<TNetPacket, SpawnPlayer>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayerControls: SendFixedPacket(in Unsafe.As<TNetPacket, PlayerControls>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayerHealth: SendFixedPacket(in Unsafe.As<TNetPacket, PlayerHealth>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TileChange: SendFixedPacket(in Unsafe.As<TNetPacket, TileChange>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.MenuSunMoon: SendFixedPacket(in Unsafe.As<TNetPacket, MenuSunMoon>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ChangeDoor: SendFixedPacket(in Unsafe.As<TNetPacket, ChangeDoor>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TileSquare: SendDynamicPacket(in Unsafe.As<TNetPacket, TileSquare>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncItem: SendFixedPacket(in Unsafe.As<TNetPacket, SyncItem>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncNPC: SendDynamicPacket(in Unsafe.As<TNetPacket, SyncNPC>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.UnusedStrikeNPC: SendFixedPacket(in Unsafe.As<TNetPacket, UnusedStrikeNPC>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncProjectile: SendFixedPacket(in Unsafe.As<TNetPacket, SyncProjectile>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.StrikeNPC: SendFixedPacket(in Unsafe.As<TNetPacket, StrikeNPC>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.KillProjectile: SendFixedPacket(in Unsafe.As<TNetPacket, KillProjectile>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayerPvP: SendFixedPacket(in Unsafe.As<TNetPacket, PlayerPvP>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.RequestChestOpen: SendFixedPacket(in Unsafe.As<TNetPacket, RequestChestOpen>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncChestItem: SendFixedPacket(in Unsafe.As<TNetPacket, SyncChestItem>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncPlayerChest: SendDynamicPacket(in Unsafe.As<TNetPacket, SyncPlayerChest>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ChestUpdates: SendFixedPacket(in Unsafe.As<TNetPacket, ChestUpdates>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.HealEffect: SendFixedPacket(in Unsafe.As<TNetPacket, HealEffect>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayerZone: SendDynamicPacket(in Unsafe.As<TNetPacket, PlayerZone>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.RequestPassword: SendFixedPacket(in Unsafe.As<TNetPacket, RequestPassword>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SendPassword: SendDynamicPacket(in Unsafe.As<TNetPacket, SendPassword>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ResetItemOwner: SendFixedPacket(in Unsafe.As<TNetPacket, ResetItemOwner>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayerTalkingNPC: SendFixedPacket(in Unsafe.As<TNetPacket, PlayerTalkingNPC>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ItemAnimation: SendFixedPacket(in Unsafe.As<TNetPacket, ItemAnimation>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ManaEffect: SendFixedPacket(in Unsafe.As<TNetPacket, ManaEffect>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.RequestReadSign: SendFixedPacket(in Unsafe.As<TNetPacket, RequestReadSign>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ReadSign: SendDynamicPacket(in Unsafe.As<TNetPacket, ReadSign>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.LiquidUpdate: SendFixedPacket(in Unsafe.As<TNetPacket, LiquidUpdate>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.StartPlaying: SendFixedPacket(in Unsafe.As<TNetPacket, StartPlaying>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayerBuffs: SendDynamicPacket(in Unsafe.As<TNetPacket, PlayerBuffs>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.Assorted1: SendFixedPacket(in Unsafe.As<TNetPacket, Assorted1>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.Unlock: SendFixedPacket(in Unsafe.As<TNetPacket, Unlock>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.AddNPCBuff: SendFixedPacket(in Unsafe.As<TNetPacket, AddNPCBuff>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SendNPCBuffs: SendDynamicPacket(in Unsafe.As<TNetPacket, SendNPCBuffs>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.AddPlayerBuff: SendFixedPacket(in Unsafe.As<TNetPacket, AddPlayerBuff>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncNPCName: SendDynamicPacket_S(in Unsafe.As<TNetPacket, SyncNPCName>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TileCounts: SendFixedPacket(in Unsafe.As<TNetPacket, TileCounts>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayNote: SendFixedPacket(in Unsafe.As<TNetPacket, PlayNote>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.NPCHome: SendFixedPacket(in Unsafe.As<TNetPacket, NPCHome>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SpawnBoss: SendFixedPacket(in Unsafe.As<TNetPacket, SpawnBoss>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.Dodge: SendFixedPacket(in Unsafe.As<TNetPacket, Dodge>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SpiritHeal: SendFixedPacket(in Unsafe.As<TNetPacket, SpiritHeal>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.BugCatching: SendFixedPacket(in Unsafe.As<TNetPacket, BugCatching>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.BugReleasing: SendFixedPacket(in Unsafe.As<TNetPacket, BugReleasing>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TravelMerchantItems: SendDynamicPacket(in Unsafe.As<TNetPacket, TravelMerchantItems>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.AnglerQuestFinished: SendFixedPacket(in Unsafe.As<TNetPacket, AnglerQuestFinished>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.AnglerQuestCountSync: SendFixedPacket(in Unsafe.As<TNetPacket, AnglerQuestCountSync>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TemporaryAnimation: SendFixedPacket(in Unsafe.As<TNetPacket, TemporaryAnimation>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.InvasionProgressReport: SendFixedPacket(in Unsafe.As<TNetPacket, InvasionProgressReport>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.CombatTextInt: SendFixedPacket(in Unsafe.As<TNetPacket, CombatTextInt>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.NetModules: {
                        switch ((NetModuleType)(TNetPacket.GlobalID - NetLiquidModule.GlobalID)) {
                            case NetModuleType.NetLiquidModule: SendDynamicPacket(in Unsafe.As<TNetPacket, NetLiquidModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            case NetModuleType.NetTextModule: SendDynamicPacket_S(in Unsafe.As<TNetPacket, NetTextModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            case NetModuleType.NetPingModule: SendFixedPacket(in Unsafe.As<TNetPacket, NetPingModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            case NetModuleType.NetAmbienceModule: SendFixedPacket(in Unsafe.As<TNetPacket, NetAmbienceModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            case NetModuleType.NetBestiaryModule: SendFixedPacket(in Unsafe.As<TNetPacket, NetBestiaryModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            case NetModuleType.NetCreativePowersModule: SendDynamicPacket(in Unsafe.As<TNetPacket, NetCreativePowersModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            case NetModuleType.NetCreativeUnlocksPlayerReportModule: SendFixedPacket(in Unsafe.As<TNetPacket, NetCreativeUnlocksPlayerReportModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            case NetModuleType.NetTeleportPylonModule: SendFixedPacket(in Unsafe.As<TNetPacket, NetTeleportPylonModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            case NetModuleType.NetParticlesModule: SendFixedPacket(in Unsafe.As<TNetPacket, NetParticlesModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            case NetModuleType.NetCreativePowerPermissionsModule: SendFixedPacket(in Unsafe.As<TNetPacket, NetCreativePowerPermissionsModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            case NetModuleType.NetBannersModule: SendDynamicPacket(in Unsafe.As<TNetPacket, NetBannersModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            case NetModuleType.NetCraftingRequestsModule: SendDynamicPacket_S(in Unsafe.As<TNetPacket, NetCraftingRequestsModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            case NetModuleType.NetTagEffectStateModule: SendDynamicPacket_S(in Unsafe.As<TNetPacket, NetTagEffectStateModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            case NetModuleType.NetLeashedEntityModule: SendDynamicPacket(in Unsafe.As<TNetPacket, NetLeashedEntityModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            case NetModuleType.NetUnbreakableWallScanModule: SendFixedPacket(in Unsafe.As<TNetPacket, NetUnbreakableWallScanModule>(ref Unsafe.AsRef(in packet)), callback, state); return;
                            default: return;
                        }
                    }
                case MessageID.NPCKillCountDeathTally: SendFixedPacket(in Unsafe.As<TNetPacket, NPCKillCountDeathTally>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.QuickStackChests: SendDynamicPacket_S(in Unsafe.As<TNetPacket, QuickStackChests>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TileEntitySharing: SendDynamicPacket(in Unsafe.As<TNetPacket, TileEntitySharing>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TileEntityPlacement: SendFixedPacket(in Unsafe.As<TNetPacket, TileEntityPlacement>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ItemTweaker: SendFixedPacket(in Unsafe.As<TNetPacket, ItemTweaker>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ItemFrameTryPlacing: SendFixedPacket(in Unsafe.As<TNetPacket, ItemFrameTryPlacing>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.InstancedItem: SendFixedPacket(in Unsafe.As<TNetPacket, InstancedItem>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncEmoteBubble: SendDynamicPacket(in Unsafe.As<TNetPacket, SyncEmoteBubble>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.MurderSomeoneElsesProjectile: SendFixedPacket(in Unsafe.As<TNetPacket, MurderSomeoneElsesProjectile>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TeleportPlayerThroughPortal: SendFixedPacket(in Unsafe.As<TNetPacket, TeleportPlayerThroughPortal>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.AchievementMessageNPCKilled: SendFixedPacket(in Unsafe.As<TNetPacket, AchievementMessageNPCKilled>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.AchievementMessageEventHappened: SendFixedPacket(in Unsafe.As<TNetPacket, AchievementMessageEventHappened>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.MinionRestTargetUpdate: SendFixedPacket(in Unsafe.As<TNetPacket, MinionRestTargetUpdate>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TeleportNPCThroughPortal: SendFixedPacket(in Unsafe.As<TNetPacket, TeleportNPCThroughPortal>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.UpdateTowerShieldStrengths: SendDynamicPacket(in Unsafe.As<TNetPacket, UpdateTowerShieldStrengths>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.NebulaLevelupRequest: SendFixedPacket(in Unsafe.As<TNetPacket, NebulaLevelupRequest>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.MoonlordCountdown: SendFixedPacket(in Unsafe.As<TNetPacket, MoonlordCountdown>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ShopOverride: SendFixedPacket(in Unsafe.As<TNetPacket, ShopOverride>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SpecialFX: SendFixedPacket(in Unsafe.As<TNetPacket, SpecialFX>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.CrystalInvasionWipeAllTheThings: SendFixedPacket(in Unsafe.As<TNetPacket, CrystalInvasionWipeAllTheThingsss>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.CombatTextString: SendDynamicPacket(in Unsafe.As<TNetPacket, CombatTextString>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TEDisplayDollItemSync: SendFixedPacket(in Unsafe.As<TNetPacket, TEDisplayDollItemSync>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TEHatRackItemSync: SendFixedPacket(in Unsafe.As<TNetPacket, TEHatRackItemSync>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayerActive: SendFixedPacket(in Unsafe.As<TNetPacket, PlayerActive>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ItemOwner: SendFixedPacket(in Unsafe.As<TNetPacket, ItemOwner>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayerMana: SendFixedPacket(in Unsafe.As<TNetPacket, PlayerMana>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayerTeam: SendFixedPacket(in Unsafe.As<TNetPacket, PlayerTeam>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.HitSwitch: SendFixedPacket(in Unsafe.As<TNetPacket, HitSwitch>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PaintTile: SendFixedPacket(in Unsafe.As<TNetPacket, PaintTile>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PaintWall: SendFixedPacket(in Unsafe.As<TNetPacket, PaintWall>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.Teleport: SendFixedPacket(in Unsafe.As<TNetPacket, Teleport>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ClientUUID: SendDynamicPacket(in Unsafe.As<TNetPacket, ClientUUID>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ChestName: SendDynamicPacket(in Unsafe.As<TNetPacket, ChestName>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TeleportationPotion: SendFixedPacket(in Unsafe.As<TNetPacket, TeleportationPotion>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.AnglerQuest: SendFixedPacket(in Unsafe.As<TNetPacket, AnglerQuest>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlaceObject: SendFixedPacket(in Unsafe.As<TNetPacket, PlaceObject>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncPlayerChestIndex: SendFixedPacket(in Unsafe.As<TNetPacket, SyncPlayerChestIndex>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayerStealth: SendFixedPacket(in Unsafe.As<TNetPacket, PlayerStealth>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncExtraValue: SendFixedPacket(in Unsafe.As<TNetPacket, SyncExtraValue>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SocialHandshake: SendDynamicPacket(in Unsafe.As<TNetPacket, SocialHandshake>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.GemLockToggle: SendFixedPacket(in Unsafe.As<TNetPacket, GemLockToggle>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PoofOfSmoke: SendFixedPacket(in Unsafe.As<TNetPacket, PoofOfSmoke>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SmartTextMessage: SendDynamicPacket(in Unsafe.As<TNetPacket, SmartTextMessage>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.WiredCannonShot: SendFixedPacket(in Unsafe.As<TNetPacket, WiredCannonShot>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.MassWireOperation: SendFixedPacket(in Unsafe.As<TNetPacket, MassWireOperation>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.MassWireOperationPay: SendFixedPacket(in Unsafe.As<TNetPacket, MassWireOperationPay>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ToggleParty: SendFixedPacket(in Unsafe.As<TNetPacket, ToggleParty>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.CrystalInvasionStart: SendFixedPacket(in Unsafe.As<TNetPacket, CrystalInvasionStart>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.MinionAttackTargetUpdate: SendFixedPacket(in Unsafe.As<TNetPacket, MinionAttackTargetUpdate>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.CrystalInvasionSendWaitTime: SendFixedPacket(in Unsafe.As<TNetPacket, CrystalInvasionSendWaitTime>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayerHurtV2: SendDynamicPacket(in Unsafe.As<TNetPacket, PlayerHurtV2>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayerDeathV2: SendDynamicPacket(in Unsafe.As<TNetPacket, PlayerDeathV2>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.Emoji: SendFixedPacket(in Unsafe.As<TNetPacket, Emoji>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.RequestTileEntityInteraction: SendFixedPacket(in Unsafe.As<TNetPacket, RequestTileEntityInteraction>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.WeaponsRackTryPlacing: SendFixedPacket(in Unsafe.As<TNetPacket, WeaponsRackTryPlacing>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncTilePicking: SendFixedPacket(in Unsafe.As<TNetPacket, SyncTilePicking>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncRevengeMarker: SendFixedPacket(in Unsafe.As<TNetPacket, SyncRevengeMarker>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.RemoveRevengeMarker: SendFixedPacket(in Unsafe.As<TNetPacket, RemoveRevengeMarker>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.LandGolfBallInCup: SendFixedPacket(in Unsafe.As<TNetPacket, LandGolfBallInCup>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.FinishedConnectingToServer: SendFixedPacket(in Unsafe.As<TNetPacket, FinishedConnectingToServer>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.FishOutNPC: SendFixedPacket(in Unsafe.As<TNetPacket, FishOutNPC>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TamperWithNPC: SendFixedPacket(in Unsafe.As<TNetPacket, TamperWithNPC>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayLegacySound: SendFixedPacket(in Unsafe.As<TNetPacket, PlayLegacySound>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.FoodPlatterTryPlacing: SendFixedPacket(in Unsafe.As<TNetPacket, FoodPlatterTryPlacing>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.UpdatePlayerLuckFactors: SendFixedPacket(in Unsafe.As<TNetPacket, UpdatePlayerLuckFactors>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.DeadPlayer: SendFixedPacket(in Unsafe.As<TNetPacket, DeadPlayer>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncCavernMonsterType: SendDynamicPacket(in Unsafe.As<TNetPacket, SyncCavernMonsterType>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.RequestNPCBuffRemoval: SendFixedPacket(in Unsafe.As<TNetPacket, RequestNPCBuffRemoval>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ClientSyncedInventory: SendFixedPacket(in Unsafe.As<TNetPacket, ClientSyncedInventory>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SetCountsAsHostForGameplay: SendFixedPacket(in Unsafe.As<TNetPacket, SetCountsAsHostForGameplay>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SetMiscEventValues: SendFixedPacket(in Unsafe.As<TNetPacket, SetMiscEventValues>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.RequestLucyPopup: SendFixedPacket(in Unsafe.As<TNetPacket, RequestLucyPopup>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncProjectileTrackers: SendFixedPacket(in Unsafe.As<TNetPacket, SyncProjectileTrackers>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.CrystalInvasionRequestedToSkipWaitTime: SendFixedPacket(in Unsafe.As<TNetPacket, CrystalInvasionRequestedToSkipWaitTime>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.RequestQuestEffect: SendFixedPacket(in Unsafe.As<TNetPacket, RequestQuestEffect>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncItemsWithShimmer: SendFixedPacket(in Unsafe.As<TNetPacket, SyncItemsWithShimmer>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ShimmerActions: SendFixedPacket(in Unsafe.As<TNetPacket, ShimmerActions>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncLoadout: SendFixedPacket(in Unsafe.As<TNetPacket, SyncLoadout>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncItemCannotBeTakenByEnemies: SendFixedPacket(in Unsafe.As<TNetPacket, SyncItemCannotBeTakenByEnemies>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.DeadCellsDisplayJarTryPlacing: SendFixedPacket(in Unsafe.As<TNetPacket, DeadCellsDisplayJarTryPlacing>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SpectatePlayer: SendFixedPacket(in Unsafe.As<TNetPacket, SpectatePlayer>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncItemDespawn: SendFixedPacket(in Unsafe.As<TNetPacket, SyncItemDespawn>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ItemUseSound: SendFixedPacket(in Unsafe.As<TNetPacket, ItemUseSound>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.NPCDebuffDamage: SendFixedPacket(in Unsafe.As<TNetPacket, NPCDebuffDamage>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.Ping: SendFixedPacket(in Unsafe.As<TNetPacket, Ping>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.SyncChestSize: SendFixedPacket(in Unsafe.As<TNetPacket, SyncChestSize>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TELeashedEntityAnchorPlaceItem: SendFixedPacket(in Unsafe.As<TNetPacket, TELeashedEntityAnchorPlaceItem>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.TeamChangeFromUI: SendFixedPacket(in Unsafe.As<TNetPacket, TeamChangeFromUI>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ExtraSpawnSectionLoaded: SendFixedPacket(in Unsafe.As<TNetPacket, ExtraSpawnSectionLoaded>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.RequestSection: SendFixedPacket(in Unsafe.As<TNetPacket, RequestSection>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ItemPosition: SendFixedPacket(in Unsafe.As<TNetPacket, ItemPosition>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.HostToken: SendDynamicPacket(in Unsafe.As<TNetPacket, HostToken>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.ServerInfo: SendDynamicPacket(in Unsafe.As<TNetPacket, ServerInfo>(ref Unsafe.AsRef(in packet)), callback, state); return;
                case MessageID.PlayerPlatformInfo: SendFixedPacket(in Unsafe.As<TNetPacket, PlayerPlatformInfo>(ref Unsafe.AsRef(in packet)), callback, state); return;
                default: return;
            }
        }
    }
}
