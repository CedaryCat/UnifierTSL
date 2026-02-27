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
    public enum ProcessPacketEventType : byte
    {
        BeforeAllLogic = 0,
        BeforeOriginalProcess = 1
    }
    public delegate void PacketProcessedDelegate<TPacket>(in ReceivePacketEvent<TPacket> args, PacketHandleMode handleMode) where TPacket : struct, INetPacket;
    public readonly unsafe struct ReceiveBytesInfo(LocalClientSender sender, ClientPacketReceiver receiver, void* ptr, void* ptr_end) : IEventContent
    {
        public readonly LocalClientSender ReceiveFrom = sender;
        public readonly ClientPacketReceiver LocalReceiver = receiver;
        public readonly void* rawDataBegin = ptr;
        public readonly void* rawDataEnd = ptr_end;
        public readonly ReadOnlySpan<byte> RawData => new(rawDataBegin, (int)((byte*)rawDataEnd - (byte*)rawDataBegin));
    }
    public readonly unsafe struct ProcessPacketEvent : IPlayerEventContent
    {
        public readonly ReceiveBytesInfo Info;
        public readonly ProcessPacketEventType EventType;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ProcessPacketEvent(ref readonly ReceiveBytesInfo info, ProcessPacketEventType eventType) {
            Info = info;
            EventType = eventType;
        }

        public readonly LocalClientSender ReceiveFrom => Info.ReceiveFrom;
        public readonly ClientPacketReceiver LocalReceiver => Info.LocalReceiver;
        public readonly void* rawDataBegin => Info.rawDataBegin;
        public readonly void* rawDataEnd => Info.rawDataEnd;
        public readonly ReadOnlySpan<byte> RawData => Info.RawData;
        public readonly int Who => ReceiveFrom.ID;
        public readonly ServerContext Server => LocalReceiver.Server;
    }
    public unsafe ref struct ReceivePacketEvent<TPacket>(ref readonly ReceiveBytesInfo info) : IPlayerEventContent where TPacket : struct, INetPacket
    {
        public readonly LocalClientSender ReceiveFrom = info.ReceiveFrom;
        public readonly ClientPacketReceiver LocalReceiver = info.LocalReceiver;
        public readonly void* rawDataBegin = info.rawDataBegin;
        public readonly void* rawDataEnd = info.rawDataEnd;
        public readonly ReadOnlySpan<byte> RawData = info.RawData;
        public TPacket Packet;
        public PacketHandleMode HandleMode;
        public bool StopPropagation;
        public PacketProcessedDelegate<TPacket>? PacketProcessed;
        public readonly int Who => ReceiveFrom.ID;
        public readonly ServerContext Server => LocalReceiver.Server;
    }

    public delegate void ReceivePacket<TPacket>(ref ReceivePacketEvent<TPacket> args) where TPacket : struct, INetPacket;
    public static class NetPacketHandler
    {
        public static readonly ReadonlyEventProvider<ProcessPacketEvent> ProcessPacketEvent = new();
        private readonly struct PriorityItem<TPacket>(ReceivePacket<TPacket> handler, HandlerPriority priority, FilterEventOption option) : IPriorityHandler, IComparable<PriorityItem<TPacket>> where TPacket : struct, INetPacket
        {
            public readonly ReceivePacket<TPacket> Handler = handler;
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
        private static unsafe PacketHandleMode ProcessPacketAndTryEndEvent<TPacket>(Array boxedHandlers, ref ReceivePacketEvent<TPacket> args) where TPacket : struct, INetPacket {
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
                        ReceiveBytesInfo info = new(args.ReceiveFrom, args.LocalReceiver, args.rawDataBegin, args.rawDataEnd);
                        if (!OriginalProcess(in info)) {
                            args.PacketProcessed?.Invoke(args, PacketHandleMode.Cancel);
                            return PacketHandleMode.Cancel;
                        }
                        args.PacketProcessed?.Invoke(args, PacketHandleMode.None);
                        return PacketHandleMode.None;
                    }
            }
        }

        #region ProcessPacket
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool InvokeProcessPacketEvent(ref readonly ReceiveBytesInfo info, ProcessPacketEventType type) {
            ProcessPacketEvent eventInfo = new(in info, type);
            ProcessPacketEvent.Invoke(in eventInfo, out bool handled);
            return handled;
        }
        private static unsafe bool OriginalProcess(ref readonly ReceiveBytesInfo info) {
            if (InvokeProcessPacketEvent(in info, ProcessPacketEventType.BeforeOriginalProcess)) {
                return false;
            }
            MessageBuffer msgBuffer = UnifiedServerCoordinator.globalMsgBuffers[info.ReceiveFrom.ID];
            int begin, length;
            fixed (byte* ptr = msgBuffer.readBuffer) {
                begin = (int)((byte*)info.rawDataBegin - ptr);
                length = (int)((byte*)info.rawDataEnd - (byte*)info.rawDataBegin);
            }
            msgBuffer.GetData(info.LocalReceiver.Server, begin, length, out _);
            return true;
        }
        private static unsafe void ProcessPacket_F<TPacket>(ref readonly ReceiveBytesInfo info, int contentOffset = 1) where TPacket : unmanaged, INetPacket, INonSideSpecific {
            Array? boxedHandlers = handlers[TPacket.GlobalID];
            if (boxedHandlers is null) {
                OriginalProcess(in info);
                return;
            }
            ReceivePacketEvent<TPacket> args = new(in info);
            void* ptr = Unsafe.Add<byte>(args.rawDataBegin, contentOffset);
            args.Packet.ReadContent(ref ptr, args.rawDataEnd);
            if (ProcessPacketAndTryEndEvent(boxedHandlers, ref args) is PacketHandleMode.Overwrite) {
                args.LocalReceiver.AsReceiveFromSender_FixedPkt(args.ReceiveFrom, args.Packet);
                args.PacketProcessed?.Invoke(args, PacketHandleMode.Overwrite);
            }
        }
        private static unsafe void ProcessPacket_FS<TPacket>(ref readonly ReceiveBytesInfo info, int contentOffset = 1) where TPacket : unmanaged, INetPacket, ISideSpecific {
            Array? boxedHandlers = handlers[TPacket.GlobalID];
            if (boxedHandlers is null) {
                OriginalProcess(in info);
                return;
            }
            ReceivePacketEvent<TPacket> args = new(in info);
            void* ptr = Unsafe.Add<byte>(args.rawDataBegin, contentOffset);
            args.Packet.IsServerSide = true;
            args.Packet.ReadContent(ref ptr, args.rawDataEnd);
            if (ProcessPacketAndTryEndEvent(boxedHandlers, ref args) is PacketHandleMode.Overwrite) {
                args.LocalReceiver.AsReceiveFromSender_FixedPkt_S(args.ReceiveFrom, args.Packet);
                args.PacketProcessed?.Invoke(args, PacketHandleMode.Overwrite);
            }
        }
        private static unsafe void ProcessPacket_D<TPacket>(ref readonly ReceiveBytesInfo info, int contentOffset = 1) where TPacket : struct, IManagedPacket, INonSideSpecific, INetPacket {
            Array? boxedHandlers = handlers[TPacket.GlobalID];
            if (boxedHandlers is null) {
                OriginalProcess(in info);
                return;
            }
            ReceivePacketEvent<TPacket> args = new(in info);
            void* ptr = Unsafe.Add<byte>(args.rawDataBegin, contentOffset);
            args.Packet.ReadContent(ref ptr, args.rawDataEnd);
            if (ProcessPacketAndTryEndEvent(boxedHandlers, ref args) is PacketHandleMode.Overwrite) {
                args.LocalReceiver.AsReceiveFromSender_DynamicPkt(args.ReceiveFrom, args.Packet);
                args.PacketProcessed?.Invoke(args, PacketHandleMode.Overwrite);
            }
        }
        private static unsafe void ProcessPacket_DS<TPacket>(ref readonly ReceiveBytesInfo info, int contentOffset = 1) where TPacket : struct, IManagedPacket, INetPacket, ISideSpecific {
            Array? boxedHandlers = handlers[TPacket.GlobalID];
            if (boxedHandlers is null) {
                OriginalProcess(in info);
                return;
            }
            ReceivePacketEvent<TPacket> args = new(in info);
            void* ptr = Unsafe.Add<byte>(args.rawDataBegin, contentOffset);
            args.Packet.IsServerSide = true;
            args.Packet.ReadContent(ref ptr, args.rawDataEnd);
            if (ProcessPacketAndTryEndEvent(boxedHandlers, ref args) is PacketHandleMode.Overwrite) {
                args.LocalReceiver.AsReceiveFromSender_DynamicPkt_S(args.ReceiveFrom, args.Packet);
                args.PacketProcessed?.Invoke(args, PacketHandleMode.Overwrite);
            }
        }
        #endregion

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void ProcessBytes(ServerContext server, MessageBuffer buffer, int contentStart, int contentLength) {
            fixed (void* ptr = buffer.readBuffer) {
                ProcessBytesCore(
                    UnifiedServerCoordinator.clientSenders[buffer.whoAmI],
                    server.PacketReceiver,
                    Unsafe.Add<byte>(ptr, contentStart),
                    Unsafe.Add<byte>(ptr, contentStart + contentLength));
            }
        }
        private static unsafe void ProcessBytesCore(LocalClientSender sender, ClientPacketReceiver receiver, void* ptr, void* ptr_end) {
            MessageID id = (MessageID)Unsafe.Read<byte>(ptr);
            ReceiveBytesInfo info = new(sender, receiver, ptr, ptr_end);
            if (InvokeProcessPacketEvent(in info, ProcessPacketEventType.BeforeAllLogic)) {
                return;
            }
            // D - F
            // D is Dynamic, means the packet has no max length
            // F is Fixed, means the packet has a max length

            // S is SideSpecific, means the packet requires side-specific (client/server) handling during packet serialization/deserialization.
            switch (id) {
                case MessageID.ClientHello: ProcessPacket_D<ClientHello>(in info); return;
                case MessageID.Kick: ProcessPacket_D<Kick>(in info); return;
                case MessageID.LoadPlayer: ProcessPacket_F<LoadPlayer>(in info); return;
                case MessageID.SyncPlayer: ProcessPacket_D<SyncPlayer>(in info); return;
                case MessageID.SyncEquipment: ProcessPacket_F<SyncEquipment>(in info); return;
                case MessageID.RequestWorldInfo: ProcessPacket_F<RequestWorldInfo>(in info); return;
                case MessageID.WorldData: ProcessPacket_D<WorldData>(in info); return;
                case MessageID.RequestTileData: ProcessPacket_F<RequestTileData>(in info); return;
                case MessageID.StatusText: ProcessPacket_D<StatusText>(in info); return;
                case MessageID.TileSection: ProcessPacket_D<TileSection>(in info); return;
                case MessageID.FrameSection: ProcessPacket_F<FrameSection>(in info); return;
                case MessageID.SpawnPlayer: ProcessPacket_F<SpawnPlayer>(in info); return;
                case MessageID.PlayerControls: ProcessPacket_F<PlayerControls>(in info); return;
                case MessageID.PlayerHealth: ProcessPacket_F<PlayerHealth>(in info); return;
                case MessageID.TileChange: ProcessPacket_F<TileChange>(in info); return;
                case MessageID.MenuSunMoon: ProcessPacket_F<MenuSunMoon>(in info); return;
                case MessageID.ChangeDoor: ProcessPacket_F<ChangeDoor>(in info); return;
                case MessageID.TileSquare: ProcessPacket_D<TileSquare>(in info); return;
                case MessageID.SyncItem: ProcessPacket_F<SyncItem>(in info); return;
                case MessageID.SyncNPC: ProcessPacket_D<SyncNPC>(in info); return;
                case MessageID.UnusedStrikeNPC: ProcessPacket_F<UnusedStrikeNPC>(in info); return;
                case MessageID.SyncProjectile: ProcessPacket_F<SyncProjectile>(in info); return;
                case MessageID.StrikeNPC: ProcessPacket_F<StrikeNPC>(in info); return;
                case MessageID.KillProjectile: ProcessPacket_F<KillProjectile>(in info); return;
                case MessageID.PlayerPvP: ProcessPacket_F<PlayerPvP>(in info); return;
                case MessageID.RequestChestOpen: ProcessPacket_F<RequestChestOpen>(in info); return;
                case MessageID.SyncChestItem: ProcessPacket_F<SyncChestItem>(in info); return;
                case MessageID.SyncPlayerChest: ProcessPacket_D<SyncPlayerChest>(in info); return;
                case MessageID.ChestUpdates: ProcessPacket_F<ChestUpdates>(in info); return;
                case MessageID.HealEffect: ProcessPacket_F<HealEffect>(in info); return;
                case MessageID.PlayerZone: ProcessPacket_D<PlayerZone>(in info); return;
                case MessageID.RequestPassword: ProcessPacket_F<RequestPassword>(in info); return;
                case MessageID.SendPassword: ProcessPacket_D<SendPassword>(in info); return;
                case MessageID.ResetItemOwner: ProcessPacket_F<ResetItemOwner>(in info); return;
                case MessageID.PlayerTalkingNPC: ProcessPacket_F<PlayerTalkingNPC>(in info); return;
                case MessageID.ItemAnimation: ProcessPacket_F<ItemAnimation>(in info); return;
                case MessageID.ManaEffect: ProcessPacket_F<ManaEffect>(in info); return;
                case MessageID.RequestReadSign: ProcessPacket_F<RequestReadSign>(in info); return;
                case MessageID.ReadSign: ProcessPacket_D<ReadSign>(in info); return;
                case MessageID.LiquidUpdate: ProcessPacket_F<LiquidUpdate>(in info); return;
                case MessageID.StartPlaying: ProcessPacket_F<StartPlaying>(in info); return;
                case MessageID.PlayerBuffs: ProcessPacket_D<PlayerBuffs>(in info); return;
                case MessageID.Assorted1: ProcessPacket_F<Assorted1>(in info); return;
                case MessageID.Unlock: ProcessPacket_F<Unlock>(in info); return;
                case MessageID.AddNPCBuff: ProcessPacket_F<AddNPCBuff>(in info); return;
                case MessageID.SendNPCBuffs: ProcessPacket_D<SendNPCBuffs>(in info); return;
                case MessageID.AddPlayerBuff: ProcessPacket_F<AddPlayerBuff>(in info); return;
                case MessageID.SyncNPCName: ProcessPacket_DS<SyncNPCName>(in info); return;
                case MessageID.TileCounts: ProcessPacket_F<TileCounts>(in info); return;
                case MessageID.PlayNote: ProcessPacket_F<PlayNote>(in info); return;
                case MessageID.NPCHome: ProcessPacket_F<NPCHome>(in info); return;
                case MessageID.SpawnBoss: ProcessPacket_F<SpawnBoss>(in info); return;
                case MessageID.Dodge: ProcessPacket_F<Dodge>(in info); return;
                case MessageID.SpiritHeal: ProcessPacket_F<SpiritHeal>(in info); return;
                case MessageID.BugCatching: ProcessPacket_F<BugCatching>(in info); return;
                case MessageID.BugReleasing: ProcessPacket_F<BugReleasing>(in info); return;
                case MessageID.TravelMerchantItems: ProcessPacket_D<TravelMerchantItems>(in info); return;
                case MessageID.AnglerQuestFinished: ProcessPacket_F<AnglerQuestFinished>(in info); return;
                case MessageID.AnglerQuestCountSync: ProcessPacket_F<AnglerQuestCountSync>(in info); return;
                case MessageID.TemporaryAnimation: ProcessPacket_F<TemporaryAnimation>(in info); return;
                case MessageID.InvasionProgressReport: ProcessPacket_F<InvasionProgressReport>(in info); return;
                case MessageID.CombatTextInt: ProcessPacket_F<CombatTextInt>(in info); return;
                case MessageID.NetModules: {
                        switch ((NetModuleType)Unsafe.Read<short>(Unsafe.Add<byte>(ptr, 1))) {
                            case NetModuleType.NetLiquidModule: ProcessPacket_D<NetLiquidModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetTextModule: ProcessPacket_DS<NetTextModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetPingModule: ProcessPacket_F<NetPingModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetAmbienceModule: ProcessPacket_F<NetAmbienceModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetBestiaryModule: ProcessPacket_F<NetBestiaryModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetCreativePowersModule: ProcessPacket_D<NetCreativePowersModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetCreativeUnlocksPlayerReportModule: ProcessPacket_F<NetCreativeUnlocksPlayerReportModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetTeleportPylonModule: ProcessPacket_F<NetTeleportPylonModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetParticlesModule: ProcessPacket_F<NetParticlesModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetCreativePowerPermissionsModule: ProcessPacket_F<NetCreativePowerPermissionsModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetBannersModule: ProcessPacket_D<NetBannersModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetCraftingRequestsModule: ProcessPacket_DS<NetCraftingRequestsModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetTagEffectStateModule: ProcessPacket_DS<NetTagEffectStateModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetLeashedEntityModule: ProcessPacket_D<NetLeashedEntityModule>(in info, contentOffset: 3); return;
                            case NetModuleType.NetUnbreakableWallScanModule: ProcessPacket_F<NetUnbreakableWallScanModule>(in info, contentOffset: 3); return;
                            default: return;
                        }
                    }
                case MessageID.NPCKillCountDeathTally: ProcessPacket_F<NPCKillCountDeathTally>(in info); return;
                case MessageID.QuickStackChests: ProcessPacket_DS<QuickStackChests>(in info); return;
                case MessageID.TileEntitySharing: ProcessPacket_D<TileEntitySharing>(in info); return;
                case MessageID.TileEntityPlacement: ProcessPacket_F<TileEntityPlacement>(in info); return;
                case MessageID.ItemTweaker: ProcessPacket_F<ItemTweaker>(in info); return;
                case MessageID.ItemFrameTryPlacing: ProcessPacket_F<ItemFrameTryPlacing>(in info); return;
                case MessageID.InstancedItem: ProcessPacket_F<InstancedItem>(in info); return;
                case MessageID.SyncEmoteBubble: ProcessPacket_D<SyncEmoteBubble>(in info); return;
                case MessageID.MurderSomeoneElsesProjectile: ProcessPacket_F<MurderSomeoneElsesProjectile>(in info); return;
                case MessageID.TeleportPlayerThroughPortal: ProcessPacket_F<TeleportPlayerThroughPortal>(in info); return;
                case MessageID.AchievementMessageNPCKilled: ProcessPacket_F<AchievementMessageNPCKilled>(in info); return;
                case MessageID.AchievementMessageEventHappened: ProcessPacket_F<AchievementMessageEventHappened>(in info); return;
                case MessageID.MinionRestTargetUpdate: ProcessPacket_F<MinionRestTargetUpdate>(in info); return;
                case MessageID.TeleportNPCThroughPortal: ProcessPacket_F<TeleportNPCThroughPortal>(in info); return;
                case MessageID.UpdateTowerShieldStrengths: ProcessPacket_D<UpdateTowerShieldStrengths>(in info); return;
                case MessageID.NebulaLevelupRequest: ProcessPacket_F<NebulaLevelupRequest>(in info); return;
                case MessageID.MoonlordCountdown: ProcessPacket_F<MoonlordCountdown>(in info); return;
                case MessageID.ShopOverride: ProcessPacket_F<ShopOverride>(in info); return;
                case MessageID.SpecialFX: ProcessPacket_F<SpecialFX>(in info); return;
                case MessageID.CrystalInvasionWipeAllTheThings: ProcessPacket_F<CrystalInvasionWipeAllTheThingsss>(in info); return;
                case MessageID.CombatTextString: ProcessPacket_D<CombatTextString>(in info); return;
                case MessageID.TEDisplayDollItemSync: ProcessPacket_F<TEDisplayDollItemSync>(in info); return;
                case MessageID.TEHatRackItemSync: ProcessPacket_F<TEHatRackItemSync>(in info); return;
                case MessageID.PlayerActive: ProcessPacket_F<PlayerActive>(in info); return;
                case MessageID.ItemOwner: ProcessPacket_F<ItemOwner>(in info); return;
                case MessageID.PlayerMana: ProcessPacket_F<PlayerMana>(in info); return;
                case MessageID.PlayerTeam: ProcessPacket_F<PlayerTeam>(in info); return;
                case MessageID.HitSwitch: ProcessPacket_F<HitSwitch>(in info); return;
                case MessageID.PaintTile: ProcessPacket_F<PaintTile>(in info); return;
                case MessageID.PaintWall: ProcessPacket_F<PaintWall>(in info); return;
                case MessageID.Teleport: ProcessPacket_F<Teleport>(in info); return;
                case MessageID.ClientUUID: ProcessPacket_D<ClientUUID>(in info); return;
                case MessageID.ChestName: ProcessPacket_D<ChestName>(in info); return;
                case MessageID.TeleportationPotion: ProcessPacket_F<TeleportationPotion>(in info); return;
                case MessageID.AnglerQuest: ProcessPacket_F<AnglerQuest>(in info); return;
                case MessageID.PlaceObject: ProcessPacket_F<PlaceObject>(in info); return;
                case MessageID.SyncPlayerChestIndex: ProcessPacket_F<SyncPlayerChestIndex>(in info); return;
                case MessageID.PlayerStealth: ProcessPacket_F<PlayerStealth>(in info); return;
                case MessageID.SyncExtraValue: ProcessPacket_F<SyncExtraValue>(in info); return;
                case MessageID.SocialHandshake: ProcessPacket_D<SocialHandshake>(in info); return;
                case MessageID.GemLockToggle: ProcessPacket_F<GemLockToggle>(in info); return;
                case MessageID.PoofOfSmoke: ProcessPacket_F<PoofOfSmoke>(in info); return;
                case MessageID.SmartTextMessage: ProcessPacket_D<SmartTextMessage>(in info); return;
                case MessageID.WiredCannonShot: ProcessPacket_F<WiredCannonShot>(in info); return;
                case MessageID.MassWireOperation: ProcessPacket_F<MassWireOperation>(in info); return;
                case MessageID.MassWireOperationPay: ProcessPacket_F<MassWireOperationPay>(in info); return;
                case MessageID.ToggleParty: ProcessPacket_F<ToggleParty>(in info); return;
                case MessageID.CrystalInvasionStart: ProcessPacket_F<CrystalInvasionStart>(in info); return;
                case MessageID.MinionAttackTargetUpdate: ProcessPacket_F<MinionAttackTargetUpdate>(in info); return;
                case MessageID.CrystalInvasionSendWaitTime: ProcessPacket_F<CrystalInvasionSendWaitTime>(in info); return;
                case MessageID.PlayerHurtV2: ProcessPacket_D<PlayerHurtV2>(in info); return;
                case MessageID.PlayerDeathV2: ProcessPacket_D<PlayerDeathV2>(in info); return;
                case MessageID.Emoji: ProcessPacket_F<Emoji>(in info); return;
                case MessageID.RequestTileEntityInteraction: ProcessPacket_F<RequestTileEntityInteraction>(in info); return;
                case MessageID.WeaponsRackTryPlacing: ProcessPacket_F<WeaponsRackTryPlacing>(in info); return;
                case MessageID.SyncTilePicking: ProcessPacket_F<SyncTilePicking>(in info); return;
                case MessageID.SyncRevengeMarker: ProcessPacket_F<SyncRevengeMarker>(in info); return;
                case MessageID.RemoveRevengeMarker: ProcessPacket_F<RemoveRevengeMarker>(in info); return;
                case MessageID.LandGolfBallInCup: ProcessPacket_F<LandGolfBallInCup>(in info); return;
                case MessageID.FinishedConnectingToServer: ProcessPacket_F<FinishedConnectingToServer>(in info); return;
                case MessageID.FishOutNPC: ProcessPacket_F<FishOutNPC>(in info); return;
                case MessageID.TamperWithNPC: ProcessPacket_F<TamperWithNPC>(in info); return;
                case MessageID.PlayLegacySound: ProcessPacket_F<PlayLegacySound>(in info); return;
                case MessageID.FoodPlatterTryPlacing: ProcessPacket_F<FoodPlatterTryPlacing>(in info); return;
                case MessageID.UpdatePlayerLuckFactors: ProcessPacket_F<UpdatePlayerLuckFactors>(in info); return;
                case MessageID.DeadPlayer: ProcessPacket_F<DeadPlayer>(in info); return;
                case MessageID.SyncCavernMonsterType: ProcessPacket_D<SyncCavernMonsterType>(in info); return;
                case MessageID.RequestNPCBuffRemoval: ProcessPacket_F<RequestNPCBuffRemoval>(in info); return;
                case MessageID.ClientSyncedInventory: ProcessPacket_F<ClientSyncedInventory>(in info); return;
                case MessageID.SetCountsAsHostForGameplay: ProcessPacket_F<SetCountsAsHostForGameplay>(in info); return;
                case MessageID.SetMiscEventValues: ProcessPacket_F<SetMiscEventValues>(in info); return;
                case MessageID.RequestLucyPopup: ProcessPacket_F<RequestLucyPopup>(in info); return;
                case MessageID.SyncProjectileTrackers: ProcessPacket_F<SyncProjectileTrackers>(in info); return;
                case MessageID.CrystalInvasionRequestedToSkipWaitTime: ProcessPacket_F<CrystalInvasionRequestedToSkipWaitTime>(in info); return;
                case MessageID.RequestQuestEffect: ProcessPacket_F<RequestQuestEffect>(in info); return;
                case MessageID.SyncItemsWithShimmer: ProcessPacket_F<SyncItemsWithShimmer>(in info); return;
                case MessageID.ShimmerActions: ProcessPacket_F<ShimmerActions>(in info); return;
                case MessageID.SyncLoadout: ProcessPacket_F<SyncLoadout>(in info); return;
                case MessageID.SyncItemCannotBeTakenByEnemies: ProcessPacket_F<SyncItemCannotBeTakenByEnemies>(in info); return;
                case MessageID.DeadCellsDisplayJarTryPlacing: ProcessPacket_F<DeadCellsDisplayJarTryPlacing>(in info); return;
                case MessageID.SpectatePlayer: ProcessPacket_F<SpectatePlayer>(in info); return;
                case MessageID.SyncItemDespawn: ProcessPacket_F<SyncItemDespawn>(in info); return;
                case MessageID.ItemUseSound: ProcessPacket_F<ItemUseSound>(in info); return;
                case MessageID.NPCDebuffDamage: ProcessPacket_F<NPCDebuffDamage>(in info); return;
                case MessageID.Ping: ProcessPacket_F<Ping>(in info); return;
                case MessageID.SyncChestSize: ProcessPacket_F<SyncChestSize>(in info); return;
                case MessageID.TELeashedEntityAnchorPlaceItem: ProcessPacket_F<TELeashedEntityAnchorPlaceItem>(in info); return;
                case MessageID.TeamChangeFromUI: ProcessPacket_F<TeamChangeFromUI>(in info); return;
                case MessageID.ExtraSpawnSectionLoaded: ProcessPacket_F<ExtraSpawnSectionLoaded>(in info); return;
                case MessageID.RequestSection: ProcessPacket_F<RequestSection>(in info); return;
                case MessageID.ItemPosition: ProcessPacket_F<ItemPosition>(in info); return;
                case MessageID.HostToken: ProcessPacket_D<HostToken>(in info); return;
                case MessageID.ServerInfo: ProcessPacket_D<ServerInfo>(in info); return;
                case MessageID.PlayerPlatformInfo: ProcessPacket_F<PlayerPlatformInfo>(in info); return;
                default: return;
            }
        }

        private static readonly Lock _sync = new();
        public static void Register<TPacket>(ReceivePacket<TPacket> handler, HandlerPriority priority = HandlerPriority.Normal, FilterEventOption option = FilterEventOption.Normal) where TPacket : struct, INetPacket {
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
        public static void UnRegister<TPacket>(ReceivePacket<TPacket> handler) where TPacket : struct, INetPacket {
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
