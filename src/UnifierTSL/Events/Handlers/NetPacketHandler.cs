using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Terraria;
using TrProtocol;
using TrProtocol.Interfaces;
using TrProtocol.Models;
using TrProtocol.NetPackets;
using TrProtocol.NetPackets.Mobile;
using TrProtocol.NetPackets.Modules;
using UnifierTSL.Events.Core;
using UnifierTSL.Network;
using UnifierTSL.Servers;

namespace UnifierTSL.Events.Handlers
{
    public enum PacketHandleMode : byte
    {
        None = 0,
        Cancel = 1,
        Overwrite = 2
    }
    public delegate void PacketProcessedDelegate<TPacket>(in RecievePacketEvent<TPacket> args, PacketHandleMode handleMode) where TPacket : struct, INetPacket;
    public readonly unsafe struct RecieveBytesInfo(LocalClientSender sender, ClientPacketReciever reciever, void* ptr, void* ptr_end) : IEventContent
    {
        public readonly LocalClientSender RecieveFrom = sender;
        public readonly ClientPacketReciever LocalReciever = reciever;
        public readonly void* rawDataBegin = ptr;
        public readonly void* rawDataEnd = ptr_end;
        public readonly ReadOnlySpan<byte> RawData => new(rawDataBegin, (int)((byte*)rawDataEnd - (byte*)rawDataBegin));
    }
    public unsafe ref struct RecievePacketEvent<TPacket>(ref readonly RecieveBytesInfo info) : IPlayerEventContent where TPacket : struct, INetPacket
    {
        public readonly LocalClientSender RecieveFrom = info.RecieveFrom;
        public readonly ClientPacketReciever LocalReciever = info.LocalReciever;
        public readonly void* rawDataBegin = info.rawDataBegin;
        public readonly void* rawDataEnd = info.rawDataEnd;
        public readonly ReadOnlySpan<byte> RawData = info.RawData;
        public TPacket Packet;
        public PacketHandleMode HandleMode;
        public bool StopPropagation;
        public PacketProcessedDelegate<TPacket>? PacketProcessed;
        public readonly int Who => RecieveFrom.ID;
        public readonly ServerContext Server => LocalReciever.Server;
    }

    public delegate void RecievePacket<TPacket>(ref RecievePacketEvent<TPacket> args) where TPacket : struct, INetPacket;
    public static class NetPacketHandler
    {
        public static readonly ReadonlyEventProvider<RecieveBytesInfo> RecievePacketEvent = new();
        private readonly struct PriorityItem<TPacket>(RecievePacket<TPacket> handler, HandlerPriority priority, FilterEventOption option) : IPriorityHandler, IComparable<PriorityItem<TPacket>> where TPacket : struct, INetPacket
        {
            public readonly RecievePacket<TPacket> Handler = handler;
            public readonly HandlerPriority Priority = priority;
            public readonly FilterEventOption Option = option;
            HandlerPriority IPriorityHandler.Priority {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Priority;
            }

            public int CompareTo(PriorityItem<TPacket> other) {
                return Priority - other.Priority;
            }
        }
        private static readonly Array?[] handlers = new Array?[INetPacket.GlobalIDCount];
        private static unsafe PacketHandleMode PrecessPacketAndTryEndEvent<TPacket>(Array boxedHandlers, ref RecievePacketEvent<TPacket> args) where TPacket : struct, INetPacket {
            Span<PriorityItem<TPacket>> handlers = ((PriorityItem<TPacket>[])boxedHandlers).AsSpan();
            if (handlers.IsEmpty) {
                return PacketHandleMode.None;
            }

            ref PriorityItem<TPacket> r0 = ref MemoryMarshal.GetReference(handlers);
            for (int i = 0; i < handlers.Length; i++) {
                PriorityItem<TPacket> handler = Unsafe.Add(ref r0, i);
                if (((args.HandleMode == PacketHandleMode.Cancel ? FilterEventOption.Handled : FilterEventOption.Normal) & handler.Option) != 0) {
                    handler.Handler(ref args);
                    if (args.StopPropagation) {
                        break;
                    }
                }
            }
            switch (args.HandleMode) {
                case PacketHandleMode.Cancel: {
                        args.PacketProcessed?.Invoke(args, PacketHandleMode.Cancel);
                        return PacketHandleMode.Cancel;
                    }
                case PacketHandleMode.Overwrite: {
                        return PacketHandleMode.Overwrite;
                    }
                case PacketHandleMode.None:
                default: {
                        MessageBuffer msgBuffer = UnifiedServerCoordinator.globalMsgBuffers[args.RecieveFrom.ID];
                        fixed (byte* ptr = msgBuffer.readBuffer) {
                            int begin = (int)((byte*)args.rawDataBegin - ptr);
                            int length = (int)((byte*)args.rawDataEnd - (byte*)args.rawDataBegin);
                            msgBuffer.GetData(args.LocalReciever.Server, begin, length, out _);
                        }
                        args.PacketProcessed?.Invoke(args, PacketHandleMode.None);
                        return PacketHandleMode.None;
                    }
            }
        }

        #region PrecessPacket
        private static unsafe void OriginalProcess(ref readonly RecieveBytesInfo info) {
            MessageBuffer msgBuffer = UnifiedServerCoordinator.globalMsgBuffers[info.RecieveFrom.ID];
            int begin, length;
            fixed (byte* ptr = msgBuffer.readBuffer) {
                begin = (int)((byte*)info.rawDataBegin - ptr);
                length = (int)((byte*)info.rawDataEnd - (byte*)info.rawDataBegin);
            }
            msgBuffer.GetData(info.LocalReciever.Server, begin, length, out _);
        }
        private static unsafe void PrecessPacket_F<TPacket>(ref readonly RecieveBytesInfo info, int contentOffset = 1) where TPacket : unmanaged, INonLengthAware, INonSideSpecific, INetPacket {
            Array? boxedHandlers = handlers[TPacket.GlobalID];
            if (boxedHandlers is null) {
                OriginalProcess(in info);
                return;
            }
            RecievePacketEvent<TPacket> args = new(in info);
            void* ptr = Unsafe.Add<byte>(args.rawDataBegin, contentOffset);
            args.Packet.ReadContent(ref ptr);
            if (PrecessPacketAndTryEndEvent(boxedHandlers, ref args) is PacketHandleMode.Overwrite) {
                args.LocalReciever.AsRecieveFromSender_FixedPkt(args.RecieveFrom, args.Packet);
                args.PacketProcessed?.Invoke(args, PacketHandleMode.Overwrite);
            }
        }
        private static unsafe void PrecessPacket_FL<TPacket>(ref readonly RecieveBytesInfo info, int contentOffset = 1) where TPacket : unmanaged, INetPacket, ILengthAware, INonSideSpecific {
            Array? boxedHandlers = handlers[TPacket.GlobalID];
            if (boxedHandlers is null) {
                OriginalProcess(in info);
                return;
            }
            RecievePacketEvent<TPacket> args = new(in info);
            void* ptr = Unsafe.Add<byte>(args.rawDataBegin, contentOffset);
            args.Packet.ReadContent(ref ptr, args.rawDataEnd);
            if (PrecessPacketAndTryEndEvent(boxedHandlers, ref args) is PacketHandleMode.Overwrite) {
                args.LocalReciever.AsRecieveFromSender_FixedPkt(args.RecieveFrom, args.Packet);
                args.PacketProcessed?.Invoke(args, PacketHandleMode.Overwrite);
            }
        }
        private static unsafe void PrecessPacket_FS<TPacket>(ref readonly RecieveBytesInfo info, int contentOffset = 1) where TPacket : unmanaged, INetPacket, INonLengthAware, ISideSpecific {
            Array? boxedHandlers = handlers[TPacket.GlobalID];
            if (boxedHandlers is null) {
                OriginalProcess(in info);
                return;
            }
            RecievePacketEvent<TPacket> args = new(in info);
            void* ptr = Unsafe.Add<byte>(args.rawDataBegin, contentOffset);
            args.Packet.IsServerSide = true;
            args.Packet.ReadContent(ref ptr);
            if (PrecessPacketAndTryEndEvent(boxedHandlers, ref args) is PacketHandleMode.Overwrite) {
                args.LocalReciever.AsRecieveFromSender_FixedPkt_S(args.RecieveFrom, args.Packet);
                args.PacketProcessed?.Invoke(args, PacketHandleMode.Overwrite);
            }
        }
        private static unsafe void PrecessPacket_FLS<TPacket>(ref readonly RecieveBytesInfo info, int contentOffset = 1) where TPacket : unmanaged, INetPacket, ILengthAware, ISideSpecific {
            Array? boxedHandlers = handlers[TPacket.GlobalID];
            if (boxedHandlers is null) {
                OriginalProcess(in info);
                return;
            }
            RecievePacketEvent<TPacket> args = new(in info);
            void* ptr = Unsafe.Add<byte>(args.rawDataBegin, contentOffset);
            args.Packet.IsServerSide = true;
            args.Packet.ReadContent(ref ptr, args.rawDataEnd);
            if (PrecessPacketAndTryEndEvent(boxedHandlers, ref args) is PacketHandleMode.Overwrite) {
                args.LocalReciever.AsRecieveFromSender_FixedPkt_S(args.RecieveFrom, args.Packet);
                args.PacketProcessed?.Invoke(args, PacketHandleMode.Overwrite);
            }
        }
        private static unsafe void PrecessPacket_D<TPacket>(ref readonly RecieveBytesInfo info, int contentOffset = 1) where TPacket : struct, IManagedPacket, INonLengthAware, INonSideSpecific, INetPacket {
            Array? boxedHandlers = handlers[TPacket.GlobalID];
            if (boxedHandlers is null) {
                OriginalProcess(in info);
                return;
            }
            RecievePacketEvent<TPacket> args = new(in info);
            void* ptr = Unsafe.Add<byte>(args.rawDataBegin, contentOffset);
            args.Packet.ReadContent(ref ptr);
            if (PrecessPacketAndTryEndEvent(boxedHandlers, ref args) is PacketHandleMode.Overwrite) {
                args.LocalReciever.AsRecieveFromSender_DynamicPkt(args.RecieveFrom, args.Packet);
                args.PacketProcessed?.Invoke(args, PacketHandleMode.Overwrite);
            }
        }
        private static unsafe void PrecessPacket_DL<TPacket>(ref readonly RecieveBytesInfo info, int contentOffset = 1) where TPacket : struct, IManagedPacket, INetPacket, ILengthAware, INonSideSpecific {
            Array? boxedHandlers = handlers[TPacket.GlobalID];
            if (boxedHandlers is null) {
                OriginalProcess(in info);
                return;
            }
            RecievePacketEvent<TPacket> args = new(in info);
            void* ptr = Unsafe.Add<byte>(args.rawDataBegin, contentOffset);
            args.Packet.ReadContent(ref ptr, args.rawDataEnd);
            if (PrecessPacketAndTryEndEvent(boxedHandlers, ref args) is PacketHandleMode.Overwrite) {
                args.LocalReciever.AsRecieveFromSender_DynamicPkt(args.RecieveFrom, args.Packet);
                args.PacketProcessed?.Invoke(args, PacketHandleMode.Overwrite);
            }
        }
        private static unsafe void PrecessPacket_DS<TPacket>(ref readonly RecieveBytesInfo info, int contentOffset = 1) where TPacket : struct, IManagedPacket, INetPacket, INonLengthAware, ISideSpecific {
            Array? boxedHandlers = handlers[TPacket.GlobalID];
            if (boxedHandlers is null) {
                OriginalProcess(in info);
                return;
            }
            RecievePacketEvent<TPacket> args = new(in info);
            void* ptr = Unsafe.Add<byte>(args.rawDataBegin, contentOffset);
            args.Packet.IsServerSide = true;
            args.Packet.ReadContent(ref ptr);
            if (PrecessPacketAndTryEndEvent(boxedHandlers, ref args) is PacketHandleMode.Overwrite) {
                args.LocalReciever.AsRecieveFromSender_DynamicPkt_S(args.RecieveFrom, args.Packet);
                args.PacketProcessed?.Invoke(args, PacketHandleMode.Overwrite);
            }
        }
        private static unsafe void PrecessPacket_DLS<TPacket>(ref readonly RecieveBytesInfo info, int contentOffset = 1) where TPacket : struct, IManagedPacket, INetPacket, ILengthAware, ISideSpecific {
            Array? boxedHandlers = handlers[TPacket.GlobalID];
            if (boxedHandlers is null) {
                OriginalProcess(in info);
                return;
            }
            RecievePacketEvent<TPacket> args = new(in info);
            void* ptr = Unsafe.Add<byte>(args.rawDataBegin, contentOffset);
            args.Packet.IsServerSide = true;
            args.Packet.ReadContent(ref ptr, args.rawDataEnd);
            if (PrecessPacketAndTryEndEvent(boxedHandlers, ref args) is PacketHandleMode.Overwrite) {
                args.LocalReciever.AsRecieveFromSender_DynamicPkt_S(args.RecieveFrom, args.Packet);
                args.PacketProcessed?.Invoke(args, PacketHandleMode.Overwrite);
            }
        }
        #endregion

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void ProcessBytes(ServerContext server, MessageBuffer buffer, int contentStart, int contentLength) {
            fixed (void* ptr = buffer.readBuffer) {
                PrecessBytes(
                    UnifiedServerCoordinator.clientSenders[buffer.whoAmI],
                    server.PacketReciever,
                    Unsafe.Add<byte>(ptr, contentStart),
                    Unsafe.Add<byte>(ptr, contentStart + contentLength));
            }
        }
        private static unsafe void PrecessBytes(LocalClientSender sender, ClientPacketReciever reciever, void* ptr, void* ptr_end) {
            MessageID id = (MessageID)Unsafe.Read<byte>(ptr);
            RecieveBytesInfo info = new(sender, reciever, ptr, ptr_end);
            RecievePacketEvent.Invoke(in info, out bool handled);
            if (handled) {
                return;
            }
            // D - F
            // D is Dynamic, means the packet has no max length
            // F is Fixed, means the packet has a max length

            // L is LengthAware, means the packet requires knowledge of its total serialized length when deserializing.
            // S is SideSpecific, means the packet requires side-specific (client/server) handling during packet serialization/deserialization.
            switch (id) {
                case MessageID.ClientHello: PrecessPacket_D<ClientHello>(in info); return;
                case MessageID.Kick: PrecessPacket_D<Kick>(in info); return;
                case MessageID.LoadPlayer: PrecessPacket_F<LoadPlayer>(in info); return;
                case MessageID.SyncPlayer: PrecessPacket_D<SyncPlayer>(in info); return;
                case MessageID.SyncEquipment: PrecessPacket_F<SyncEquipment>(in info); return;
                case MessageID.RequestWorldInfo: PrecessPacket_F<RequestWorldInfo>(in info); return;
                case MessageID.WorldData: PrecessPacket_D<WorldData>(in info); return;
                case MessageID.RequestTileData: PrecessPacket_F<RequestTileData>(in info); return;
                case MessageID.StatusText: PrecessPacket_D<StatusText>(in info); return;
                case MessageID.TileSection: PrecessPacket_DL<TileSection>(in info); return;
                case MessageID.FrameSection: PrecessPacket_F<FrameSection>(in info); return;
                case MessageID.SpawnPlayer: PrecessPacket_F<SpawnPlayer>(in info); return;
                case MessageID.PlayerControls: PrecessPacket_F<PlayerControls>(in info); return;
                case MessageID.PlayerHealth: PrecessPacket_F<PlayerHealth>(in info); return;
                case MessageID.TileChange: PrecessPacket_F<TileChange>(in info); return;
                case MessageID.MenuSunMoon: PrecessPacket_F<MenuSunMoon>(in info); return;
                case MessageID.ChangeDoor: PrecessPacket_F<ChangeDoor>(in info); return;
                case MessageID.TileSquare: PrecessPacket_D<TileSquare>(in info); return;
                case MessageID.SyncItem: PrecessPacket_F<SyncItem>(in info); return;
                case MessageID.SyncNPC: PrecessPacket_DL<SyncNPC>(in info); return;
                case MessageID.SyncProjectile: PrecessPacket_F<SyncProjectile>(in info); return;
                case MessageID.StrikeNPC: PrecessPacket_F<StrikeNPC>(in info); return;
                case MessageID.KillProjectile: PrecessPacket_F<KillProjectile>(in info); return;
                case MessageID.PlayerPvP: PrecessPacket_F<PlayerPvP>(in info); return;
                case MessageID.RequestChestOpen: PrecessPacket_F<RequestChestOpen>(in info); return;
                case MessageID.SyncChestItem: PrecessPacket_F<SyncChestItem>(in info); return;
                case MessageID.SyncPlayerChest: PrecessPacket_D<SyncPlayerChest>(in info); return;
                case MessageID.ChestUpdates: PrecessPacket_F<ChestUpdates>(in info); return;
                case MessageID.HealEffect: PrecessPacket_F<HealEffect>(in info); return;
                case MessageID.PlayerZone: PrecessPacket_D<PlayerZone>(in info); return;
                case MessageID.RequestPassword: PrecessPacket_F<RequestPassword>(in info); return;
                case MessageID.SendPassword: PrecessPacket_D<SendPassword>(in info); return;
                case MessageID.ResetItemOwner: PrecessPacket_F<ResetItemOwner>(in info); return;
                case MessageID.PlayerTalkingNPC: PrecessPacket_F<PlayerTalkingNPC>(in info); return;
                case MessageID.ItemAnimation: PrecessPacket_F<ItemAnimation>(in info); return;
                case MessageID.ManaEffect: PrecessPacket_F<ManaEffect>(in info); return;
                case MessageID.RequestReadSign: PrecessPacket_F<RequestReadSign>(in info); return;
                case MessageID.ReadSign: PrecessPacket_D<ReadSign>(in info); return;
                case MessageID.LiquidUpdate: PrecessPacket_F<LiquidUpdate>(in info); return;
                case MessageID.StartPlaying: PrecessPacket_F<StartPlaying>(in info); return;
                case MessageID.PlayerBuffs: PrecessPacket_D<PlayerBuffs>(in info); return;
                case MessageID.Assorted1: PrecessPacket_F<Assorted1>(in info); return;
                case MessageID.Unlock: PrecessPacket_F<Unlock>(in info); return;
                case MessageID.AddNPCBuff: PrecessPacket_F<AddNPCBuff>(in info); return;
                case MessageID.SendNPCBuffs: PrecessPacket_D<SendNPCBuffs>(in info); return;
                case MessageID.AddPlayerBuff: PrecessPacket_F<AddPlayerBuff>(in info); return;
                case MessageID.SyncNPCName: PrecessPacket_DS<SyncNPCName>(in info); return;
                case MessageID.TileCounts: PrecessPacket_F<TileCounts>(in info); return;
                case MessageID.PlayNote: PrecessPacket_F<PlayNote>(in info); return;
                case MessageID.NPCHome: PrecessPacket_F<NPCHome>(in info); return;
                case MessageID.SpawnBoss: PrecessPacket_F<SpawnBoss>(in info); return;
                case MessageID.Dodge: PrecessPacket_F<Dodge>(in info); return;
                case MessageID.SpiritHeal: PrecessPacket_F<SpiritHeal>(in info); return;
                case MessageID.BugCatching: PrecessPacket_F<BugCatching>(in info); return;
                case MessageID.BugReleasing: PrecessPacket_F<BugReleasing>(in info); return;
                case MessageID.TravelMerchantItems: PrecessPacket_D<TravelMerchantItems>(in info); return;
                case MessageID.AnglerQuestFinished: PrecessPacket_F<AnglerQuestFinished>(in info); return;
                case MessageID.AnglerQuestCountSync: PrecessPacket_F<AnglerQuestCountSync>(in info); return;
                case MessageID.TemporaryAnimation: PrecessPacket_F<TemporaryAnimation>(in info); return;
                case MessageID.InvasionProgressReport: PrecessPacket_F<InvasionProgressReport>(in info); return;
                case MessageID.CombatTextInt: PrecessPacket_F<CombatTextInt>(in info); return;
                case MessageID.NetModules: {
                        switch ((NetModuleType)Unsafe.Read<short>(Unsafe.Add<byte>(ptr, 1))) {
                            case NetModuleType.NetLiquidModule: PrecessPacket_D<NetLiquidModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetTextModule: PrecessPacket_DS<NetTextModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetPingModule: PrecessPacket_F<NetPingModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetAmbienceModule: PrecessPacket_F<NetAmbienceModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetBestiaryModule: PrecessPacket_F<NetBestiaryModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetCreativeUnlocksModule: PrecessPacket_F<NetCreativeUnlocksModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetCreativePowersModule: PrecessPacket_DL<NetCreativePowersModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetCreativeUnlocksPlayerReportModule: PrecessPacket_F<NetCreativeUnlocksPlayerReportModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetTeleportPylonModule: PrecessPacket_F<NetTeleportPylonModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetParticlesModule: PrecessPacket_F<NetParticlesModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetCreativePowerPermissionsModule: PrecessPacket_F<NetCreativePowerPermissionsModule>(in info, contentOffset: 3); return;
                            default: return;
                        }
                    }
                case MessageID.NPCKillCountDeathTally: PrecessPacket_F<NPCKillCountDeathTally>(in info); return;
                case MessageID.QuickStackChests: PrecessPacket_F<QuickStackChests>(in info); return;
                case MessageID.TileEntitySharing: PrecessPacket_D<TileEntitySharing>(in info); return;
                case MessageID.TileEntityPlacement: PrecessPacket_F<TileEntityPlacement>(in info); return;
                case MessageID.ItemTweaker: PrecessPacket_F<ItemTweaker>(in info); return;
                case MessageID.ItemFrameTryPlacing: PrecessPacket_F<ItemFrameTryPlacing>(in info); return;
                case MessageID.InstancedItem: PrecessPacket_F<InstancedItem>(in info); return;
                case MessageID.SyncEmoteBubble: PrecessPacket_DL<SyncEmoteBubble>(in info); return;
                case MessageID.MurderSomeoneElsesProjectile: PrecessPacket_F<MurderSomeoneElsesProjectile>(in info); return;
                case MessageID.TeleportPlayerThroughPortal: PrecessPacket_F<TeleportPlayerThroughPortal>(in info); return;
                case MessageID.AchievementMessageNPCKilled: PrecessPacket_F<AchievementMessageNPCKilled>(in info); return;
                case MessageID.AchievementMessageEventHappened: PrecessPacket_F<AchievementMessageEventHappened>(in info); return;
                case MessageID.MinionRestTargetUpdate: PrecessPacket_F<MinionRestTargetUpdate>(in info); return;
                case MessageID.TeleportNPCThroughPortal: PrecessPacket_F<TeleportNPCThroughPortal>(in info); return;
                case MessageID.UpdateTowerShieldStrengths: PrecessPacket_D<UpdateTowerShieldStrengths>(in info); return;
                case MessageID.NebulaLevelupRequest: PrecessPacket_F<NebulaLevelupRequest>(in info); return;
                case MessageID.MoonlordCountdown: PrecessPacket_F<MoonlordCountdown>(in info); return;
                case MessageID.ShopOverride: PrecessPacket_F<ShopOverride>(in info); return;
                case MessageID.SpecialFX: PrecessPacket_F<SpecialFX>(in info); return;
                case MessageID.CrystalInvasionWipeAllTheThings: PrecessPacket_F<CrystalInvasionWipeAllTheThingsss>(in info); return;
                case MessageID.CombatTextString: PrecessPacket_D<CombatTextString>(in info); return;
                case MessageID.TEDisplayDollItemSync: PrecessPacket_F<TEDisplayDollItemSync>(in info); return;
                case MessageID.TEHatRackItemSync: PrecessPacket_F<TEHatRackItemSync>(in info); return;
                case MessageID.PlayerActive: PrecessPacket_F<PlayerActive>(in info); return;
                case MessageID.ItemOwner: PrecessPacket_F<ItemOwner>(in info); return;
                case MessageID.PlayerMana: PrecessPacket_F<PlayerMana>(in info); return;
                case MessageID.PlayerTeam: PrecessPacket_F<PlayerTeam>(in info); return;
                case MessageID.HitSwitch: PrecessPacket_F<HitSwitch>(in info); return;
                case MessageID.PaintTile: PrecessPacket_F<PaintTile>(in info); return;
                case MessageID.PaintWall: PrecessPacket_F<PaintWall>(in info); return;
                case MessageID.Teleport: PrecessPacket_F<Teleport>(in info); return;
                case MessageID.ClientUUID: PrecessPacket_D<ClientUUID>(in info); return;
                case MessageID.ChestName: PrecessPacket_D<ChestName>(in info); return;
                case MessageID.TeleportationPotion: PrecessPacket_F<TeleportationPotion>(in info); return;
                case MessageID.AnglerQuest: PrecessPacket_F<AnglerQuest>(in info); return;
                case MessageID.PlaceObject: PrecessPacket_F<PlaceObject>(in info); return;
                case MessageID.SyncPlayerChestIndex: PrecessPacket_F<SyncPlayerChestIndex>(in info); return;
                case MessageID.PlayerStealth: PrecessPacket_F<PlayerStealth>(in info); return;
                case MessageID.SyncExtraValue: PrecessPacket_F<SyncExtraValue>(in info); return;
                case MessageID.SocialHandshake: PrecessPacket_DL<SocialHandshake>(in info); return;
                case MessageID.GemLockToggle: PrecessPacket_F<GemLockToggle>(in info); return;
                case MessageID.PoofOfSmoke: PrecessPacket_F<PoofOfSmoke>(in info); return;
                case MessageID.SmartTextMessage: PrecessPacket_D<SmartTextMessage>(in info); return;
                case MessageID.WiredCannonShot: PrecessPacket_F<WiredCannonShot>(in info); return;
                case MessageID.MassWireOperation: PrecessPacket_F<MassWireOperation>(in info); return;
                case MessageID.MassWireOperationPay: PrecessPacket_F<MassWireOperationPay>(in info); return;
                case MessageID.ToggleParty: PrecessPacket_F<ToggleParty>(in info); return;
                case MessageID.CrystalInvasionStart: PrecessPacket_F<CrystalInvasionStart>(in info); return;
                case MessageID.MinionAttackTargetUpdate: PrecessPacket_F<MinionAttackTargetUpdate>(in info); return;
                case MessageID.CrystalInvasionSendWaitTime: PrecessPacket_F<CrystalInvasionSendWaitTime>(in info); return;
                case MessageID.PlayerHurtV2: PrecessPacket_D<PlayerHurtV2>(in info); return;
                case MessageID.PlayerDeathV2: PrecessPacket_D<PlayerDeathV2>(in info); return;
                case MessageID.Emoji: PrecessPacket_F<Emoji>(in info); return;
                case MessageID.RequestTileEntityInteraction: PrecessPacket_F<RequestTileEntityInteraction>(in info); return;
                case MessageID.WeaponsRackTryPlacing: PrecessPacket_F<WeaponsRackTryPlacing>(in info); return;
                case MessageID.SyncTilePicking: PrecessPacket_F<SyncTilePicking>(in info); return;
                case MessageID.SyncRevengeMarker: PrecessPacket_F<SyncRevengeMarker>(in info); return;
                case MessageID.RemoveRevengeMarker: PrecessPacket_F<RemoveRevengeMarker>(in info); return;
                case MessageID.LandGolfBallInCup: PrecessPacket_F<LandGolfBallInCup>(in info); return;
                case MessageID.FinishedConnectingToServer: PrecessPacket_F<FinishedConnectingToServer>(in info); return;
                case MessageID.FishOutNPC: PrecessPacket_F<FishOutNPC>(in info); return;
                case MessageID.TamperWithNPC: PrecessPacket_F<TamperWithNPC>(in info); return;
                case MessageID.PlayLegacySound: PrecessPacket_F<PlayLegacySound>(in info); return;
                case MessageID.FoodPlatterTryPlacing: PrecessPacket_F<FoodPlatterTryPlacing>(in info); return;
                case MessageID.UpdatePlayerLuckFactors: PrecessPacket_F<UpdatePlayerLuckFactors>(in info); return;
                case MessageID.DeadPlayer: PrecessPacket_F<DeadPlayer>(in info); return;
                case MessageID.SyncCavernMonsterType: PrecessPacket_D<SyncCavernMonsterType>(in info); return;
                case MessageID.RequestNPCBuffRemoval: PrecessPacket_F<RequestNPCBuffRemoval>(in info); return;
                case MessageID.ClientSyncedInventory: PrecessPacket_F<ClientSyncedInventory>(in info); return;
                case MessageID.SetCountsAsHostForGameplay: PrecessPacket_F<SetCountsAsHostForGameplay>(in info); return;
                case MessageID.SetMiscEventValues: PrecessPacket_F<SetMiscEventValues>(in info); return;
                case MessageID.RequestLucyPopup: PrecessPacket_F<RequestLucyPopup>(in info); return;
                case MessageID.SyncProjectileTrackers: PrecessPacket_F<SyncProjectileTrackers>(in info); return;
                case MessageID.CrystalInvasionRequestedToSkipWaitTime: PrecessPacket_F<CrystalInvasionRequestedToSkipWaitTime>(in info); return;
                case MessageID.RequestQuestEffect: PrecessPacket_F<RequestQuestEffect>(in info); return;
                case MessageID.SyncItemsWithShimmer: PrecessPacket_F<SyncItemsWithShimmer>(in info); return;
                case MessageID.ShimmerActions: PrecessPacket_F<ShimmerActions>(in info); return;
                case MessageID.SyncLoadout: PrecessPacket_F<SyncLoadout>(in info); return;
                case MessageID.SyncItemCannotBeTakenByEnemies: PrecessPacket_F<SyncItemCannotBeTakenByEnemies>(in info); return;
                case MessageID.ServerInfo: PrecessPacket_D<ServerInfo>(in info); return;
                case MessageID.PlayerPlatformInfo: PrecessPacket_D<PlayerPlatformInfo>(in info); return;
                default: return;
            }
        }

        private static readonly Lock _sync = new();
        public static void Register<TPacket>(RecievePacket<TPacket> handler, HandlerPriority priority = HandlerPriority.Normal, FilterEventOption option = FilterEventOption.Normal) where TPacket : struct, INetPacket {
            lock (_sync) {
                PriorityItem<TPacket>[]? handlerItems = (PriorityItem<TPacket>[]?)handlers[TPacket.GlobalID];
                if (handlerItems is null) {
                    handlerItems = [new(handler, priority, option)];
                    handlers[TPacket.GlobalID] = handlerItems;
                }
                else {
                    if (handlerItems.Any(x => x.Handler == handler)) {
                        return;
                    }
                    PriorityItem<TPacket> handlerItem = new(handler, priority, option);
                    int len = handlerItems.Length;
                    PriorityItem<TPacket>[] tmp = new PriorityItem<TPacket>[len + 1];
                    int idx = Array.BinarySearch(handlerItems, handlerItem);
                    if (idx < 0) idx = ~idx;
                    Array.Copy(handlerItems, 0, tmp, 0, idx);
                    tmp[idx] = handlerItem;
                    Array.Copy(handlerItems, idx, tmp, idx + 1, len - idx);
                    handlers[TPacket.GlobalID] = tmp;
                }
            }
        }
        public static void UnRegister<TPacket>(RecievePacket<TPacket> handler) where TPacket : struct, INetPacket {
            PriorityItem<TPacket>[]? boxedHandlers = (PriorityItem<TPacket>[]?)handlers[TPacket.GlobalID];
            if (boxedHandlers is null) {
                return;
            }
            PriorityItem<TPacket>[] newHandlers = boxedHandlers.Where(x => x.Handler != handler).ToArray();
            if (newHandlers.Length == 0) {
                handlers[TPacket.GlobalID] = null;
            }
            else {
                handlers[TPacket.GlobalID] = newHandlers;
            }
        }
    }
}
