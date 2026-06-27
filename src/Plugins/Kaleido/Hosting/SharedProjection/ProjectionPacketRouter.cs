using System.Runtime.CompilerServices;
using TrProtocol;
using TrProtocol.Models;
using TrProtocol.NetPackets;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Network;

namespace Kaleido.Hosting.SharedProjection
{
    internal sealed class ProjectionPacketRouter
    {
        private readonly ProjectionHandshakeHandler handshake = new();

        public unsafe void OnPacket(ref ReadonlyEventArgs<ProcessPacketEvent> args) {
            if (args.Content.EventType is not ProcessPacketEventType.BeforeAllLogic
                || args.Content.LocalReceiver.Server is not SharedProjectionContext context) {
                return;
            }

            args.Handled = true;
            args.StopPropagation = true;

            ref readonly var info = ref args.Content.Info;
            var remote = args.Content.ReceiveFrom;
            var packetType = (MessageID)args.Content.RawData[0];

            if (!ProjectionPacketValidation.ValidateHandshakeState(remote, packetType)) {
                return;
            }

            if (handshake.TryHandle(context, remote, packetType)) {
                return;
            }

            if (packetType == MessageID.Ping) {
                remote.SendFixedPacket(new Ping());
                return;
            }

            var result = packetType == MessageID.NetModules
                ? RouteModule(context, remote, in info)
                : context.Input.RoutePacket(packetType, remote, in info);
            if (result == ProjectionInputResult.Forward
                || result == ProjectionInputResult.Unhandled && IsSafeToForward(packetType)) {

                ProjectionPacketReader.ForwardOriginal(args.Content, in info);
            }
        }

        private static unsafe ProjectionInputResult RouteModule(SharedProjectionContext context, LocalClientSender remote, ref readonly ReceiveBytesInfo info) {
            var moduleType = (NetModuleType)Unsafe.Read<short>(Unsafe.Add<byte>(info.rawDataBegin, 1));
            return context.Input.RouteModule(moduleType, remote, in info);
        }

        private static bool IsSafeToForward(MessageID packetType) {
            return packetType is
                MessageID.SyncEquipment or
                MessageID.PlayerControls or
                MessageID.PlayerHealth or
                MessageID.PlayerMana or
                MessageID.PlayerBuffs;
        }
    }
}
