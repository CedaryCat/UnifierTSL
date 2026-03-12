using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.Diagnostics;
using Terraria;
using Terraria.Net;
using Terraria.Net.Sockets;
using Terraria.Testing;
using UnifiedServerProcess;
using UnifierTSL.Extensions;

namespace UnifierTSL.Performance
{
    public partial class ServerPerformance
    {
        public static class Initializer
        {
            public static void Load() { }
            static Initializer() {
                IL.Terraria.Main.mfwh_DedServ += ILDetour_Main_DedServ;

                On.Terraria.Testing.DetailedFPSSystemContext.StartNextFrame += Detour_DetailedFPSSystemContext_StartNextFrame;
                IL.OTAPI.HooksSystemContext.NetMessageSystemContext.InvokeSendBytes += ILDetour_NetMessageSystemContext_InvokeSendBytes;

                IL.Terraria.Net.NetManager.mfwh_Broadcast_NetPacket_BroadcastCondition_int += ILDetour_NetManager_SendData;
                IL.Terraria.Net.NetManager.mfwh_Broadcast_NetPacket_int += ILDetour_NetManager_SendData;
                IL.Terraria.Net.NetManager.mfwh_SendToClient += ILDetour_NetManager_SendData;
            }
            const System.Reflection.BindingFlags BF_NonPub_Static = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            private static void ILDetour_Main_DedServ(ILContext il) {
                var serverStart = il.Instrs.Single(
                    inst => inst is {
                        OpCode.Code: Code.Callvirt or Code.Call,
                        Operand: MethodReference {
                            Name: nameof(NetplaySystemContext.StartServer), DeclaringType.Name: nameof(NetplaySystemContext)
                        }
                    }
                );
                var cursor = new ILCursor(il);
                cursor.Goto(serverStart, MoveType.After);
                cursor.Emit(OpCodes.Ldarg_0); // this (Main)
                cursor.Emit(OpCodes.Ldarg_1); // root (RootContext)
                cursor.Emit(OpCodes.Call, il.Import(typeof(Initializer).GetMethod(nameof(DedServLoop), BF_NonPub_Static) ?? throw new InvalidOperationException()));
            }
            private static void DedServLoop(Main mainInstance, RootContext root) {
                var server = root.ToServer();
                var Main = server.Main;
                var Netplay = server.Netplay;
                var DetailedFPS = server.DetailedFPS;
                var data = server.Performance;

                // loop timer
                var frameTimer = Stopwatch.StartNew();

                const double idealFrameTimeMs = 1000 / 60d;

                // Timing drift piles up over time because Sleep() isn't exact
                // and frame cost isn't perfectly stable either.
                // We keep this around so later frames can nudge things back into place.
                double accumulatedTimingDriftMs = 0.0;

                // Dedicated server doesn't need menus or any of that
                Main.gameMenu = false;

                // Main server loop, runs until we're told to disconnect
                while (!Netplay.Disconnect) {

                    // Time spent since the last frame started
                    double timeSinceLastFrameStartMs = frameTimer.Elapsed.TotalMilliseconds;

                    // Our frame target after applying drift correction.
                    // If we ran too long before, this helps us shave a bit off and catch up.
                    double adjustedFrameTimeMs = idealFrameTimeMs - accumulatedTimingDriftMs;

                    if (timeSinceLastFrameStartMs >= adjustedFrameTimeMs) {

                        // Record how far off we were from the ideal frame time
                        // so the next frames can balance it out a little
                        accumulatedTimingDriftMs += timeSinceLastFrameStartMs - idealFrameTimeMs;

                        // New frame, fresh timer
                        frameTimer.Reset();
                        frameTimer.Start();

                        // Only print status text when it actually changes,
                        // no need to spam the console for fun
                        if (Main.oldStatusText != Main.statusText) {
                            Main.oldStatusText = Main.statusText;
                            server.Console.WriteLine(Main.statusText); // Push the updated status line
                        }

                        bool anyConnections = Netplay.HasClients;

                        // Only bother running game logic if somebody's actually connected
                        if (anyConnections) {
                            DetailedFPS.StartNextFrame();
                            try {
                                mainInstance.Update(server, new GameTime()); // Run one full logic tick
                            }
                            catch (Exception ex) {
                                server.Log.Warning("", ex: ex);
                            }
                        }

                        // See how much time this frame has used up so far
                        double frameElapsedMs = frameTimer.Elapsed.TotalMilliseconds;

                        // Re-evaluate the target using the latest drift value
                        adjustedFrameTimeMs = idealFrameTimeMs - accumulatedTimingDriftMs;
                        double budgetedSleepMs = Math.Max(0d, adjustedFrameTimeMs - frameElapsedMs);

                        if (anyConnections) {
                            ref ServerPerformance.FrameData currentFrameData = ref data.CurrentFrameData;
                            currentFrameData.SetBudget(accumulatedTimingDriftMs, adjustedFrameTimeMs, budgetedSleepMs);
                        }

                        // Still got time left this frame? Cool, let the thread chill a bit
                        if (frameElapsedMs < adjustedFrameTimeMs) {
                            // Sleep most of the remaining time,
                            // but leave a tiny bit at the end so we don't overshoot too hard
                            int requestedSleepMs = (int)(adjustedFrameTimeMs - frameElapsedMs) - 1;

                            if (requestedSleepMs > 1) {
                                Thread.Sleep(requestedSleepMs - 1);

                                // Nobody online?
                                // Reset the timing drift and nap a little longer to save some pointless work
                                if (!anyConnections) {

                                    accumulatedTimingDriftMs = 0;

                                    Thread.Sleep(10);
                                }
                            }
                        }
                    }

                    // Give up the rest of the current timeslice
                    Thread.Sleep(0);
                }
            }

            private static void ILDetour_NetManager_SendData(MonoMod.Cil.ILContext il) {
                var call = il.Instrs.First(i => i.Operand is MethodReference { Name: nameof(NetManager.SendData) });
                if (call.Previous.Previous.Previous.OpCode.Code is not Code.Ldelem_Ref ||
                    call.Previous.Previous.Operand is not FieldReference { Name: nameof(Terraria.RemoteClient.Socket) }) {
                    throw new InvalidOperationException();
                }

                var inst = call.Previous.Previous;
                inst.OpCode = OpCodes.Nop;
                inst.Operand = null;
                inst = inst.Previous;
                inst.OpCode = OpCodes.Nop;
                inst.Operand = null;

                call.OpCode = OpCodes.Call;
                call.Operand = il.Import(typeof(Initializer).GetMethod(nameof(SendPacket), BF_NonPub_Static) ?? throw new InvalidOperationException());
            }
            static void SendPacket(NetManager netmanager, RemoteClient[] clients, int clientId, NetPacket packet) {
                netmanager.SendData(clients[clientId].Socket, packet);
                UnifiedServerCoordinator.clientSenders[clientId].CountSentBytes((uint)packet.Writer.BaseStream.Position);
            }


            private static void ILDetour_NetMessageSystemContext_InvokeSendBytes(MonoMod.Cil.ILContext il) {
                var call = il.Instrs.First(i => i.Operand is MethodReference { Name: nameof(ISocket.AsyncSend) });
                il.IL.InsertBefore(call, Instruction.Create(OpCodes.Ldarg_S, il.Method.Parameters.First(p => p.Name is "remoteClient")));
                call.OpCode = OpCodes.Call;
                call.Operand = il.Import(typeof(Initializer).GetMethod(nameof(AsyncSend), BF_NonPub_Static) ?? throw new InvalidOperationException());
            }

            static void AsyncSend(ISocket socket, byte[] data, int offset, int size, SocketSendCallback callback, object state, int clientId) {
                socket.AsyncSend(data, offset, size, callback, state);
                UnifiedServerCoordinator.clientSenders[clientId].CountSentBytes((uint)size);
            }

            static void Detour_DetailedFPSSystemContext_StartNextFrame(On.Terraria.Testing.DetailedFPSSystemContext.orig_StartNextFrame orig, DetailedFPSSystemContext self) {
                var server = self.root.ToServer();
                var perf = server.Performance;

                perf.FramesData[self.newest].Finish(server);
                orig(self);
                perf.FramesData[self.newest].Begin(server);
            }
        }
    }
}
