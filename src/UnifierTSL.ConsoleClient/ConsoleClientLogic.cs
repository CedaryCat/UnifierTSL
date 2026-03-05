using UnifierTSL.ConsoleClient.Protocol;
using UnifierTSL.ConsoleClient.Protocol.C2S;
using UnifierTSL.ConsoleClient.Protocol.S2C;
using UnifierTSL.ConsoleClient.Shell;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace UnifierTSL.ConsoleClient
{
    public static class ConsoleClientLogic
    {
        static readonly Channel<SEND_READ_FLAG> readFlagsChannel = Channel.CreateUnbounded<SEND_READ_FLAG>();
        static readonly ConcurrentDictionary<long, ReadLineRenderSnapshot> readlineRenders = new();
        static readonly ConcurrentDictionary<long, byte> pendingReadOrders = new();
        static readonly ConsoleShell shell = new();
        static long activeReadLineOrder = -1;
        static ConsoleColor legacyForegroundColor = ConsoleColor.Gray;
        static ConsoleColor legacyBackgroundColor = ConsoleColor.Black;

        private static void WriteLegacyText(string text, bool appendLine) {
            if (string.IsNullOrEmpty(text) && appendLine) {
                shell.AppendLog(Environment.NewLine, isAnsi: false);
                return;
            }
            string sanitized = AnsiSanitizer.SanitizeEscapes(text);
            string ansi = AnsiColorCodec.Wrap(sanitized, legacyForegroundColor, legacyBackgroundColor);
            if (appendLine) {
                ansi += Environment.NewLine;
            }
            shell.AppendLog(ansi, isAnsi: true);
        }

        private static ReadLineRenderSnapshot ParseRenderJson(string json) {
            if (string.IsNullOrWhiteSpace(json)) {
                return ReadLineRenderSnapshot.CreateCommandLine();
            }

            try {
                return JsonSerializer.Deserialize<ReadLineRenderSnapshot>(json) ?? ReadLineRenderSnapshot.CreateCommandLine();
            }
            catch {
                return ReadLineRenderSnapshot.CreateCommandLine();
            }
        }

        public static unsafe void ProcessData(ConsoleClient client, byte id, Span<byte> content) {
            switch (id) {
                case SET_BG_COLOR.id:
                    legacyBackgroundColor = IPacket.ReadUnmanaged<SET_BG_COLOR>(content).Color;
                    break;
                case SET_FG_COLOR.id:
                    legacyForegroundColor = IPacket.ReadUnmanaged<SET_FG_COLOR>(content).Color;
                    break;
                case SET_INPUT_ENCODING.id:
                    Console.InputEncoding = IPacket.ReadUnmanaged<SET_INPUT_ENCODING>(content).Encoding;
                    break;
                case SET_OUTPUT_ENCODING.id:
                    Console.OutputEncoding = IPacket.ReadUnmanaged<SET_OUTPUT_ENCODING>(content).Encoding;
                    break;
                case SET_WINDOW_SIZE.id:
                    var size = IPacket.ReadUnmanaged<SET_WINDOW_SIZE>(content);
                    if (size.Width != 0) {
                        Console.WindowWidth = size.Width;
                    }
                    if (size.Height != 0) {
                        Console.WindowHeight = size.Height;
                    }
                    break;
                case SET_WINDOW_POS.id:
                    var pos = IPacket.ReadUnmanaged<SET_WINDOW_POS>(content);
                    if (pos.Left != 0) {
                        Console.WindowLeft = pos.Left;
                    }
                    if (pos.Top != 0) {
                        Console.WindowTop = pos.Top;
                    }
                    break;
                case SET_TITLE.id:
                    Console.Title = IPacket.Read<SET_TITLE>(content).Title;
                    break;
                case CLEAR.id:
                    shell.Clear();
                    break;
                case SEND_WRITE.id:
                    WriteLegacyText(IPacket.Read<SEND_WRITE>(content).Text, appendLine: false);
                    break;
                case SEND_WRITE_LINE.id:
                    WriteLegacyText(IPacket.Read<SEND_WRITE_LINE>(content).Text, appendLine: true);
                    break;
                case SEND_WRITE_ANSI.id:
                    shell.AppendLog(IPacket.Read<SEND_WRITE_ANSI>(content).Text, isAnsi: true);
                    break;
                case SEND_WRITE_LINE_ANSI.id:
                    shell.AppendLog(IPacket.Read<SEND_WRITE_LINE_ANSI>(content).Text + Environment.NewLine, isAnsi: true);
                    break;
                case SET_READLINE_RENDER.id:
                    var renderPacket = IPacket.Read<SET_READLINE_RENDER>(content);
                    ReadLineRenderSnapshot snapshot = ParseRenderJson(renderPacket.RenderJson);
                    readlineRenders[renderPacket.Order] = snapshot;
                    if (Interlocked.Read(ref activeReadLineOrder) == renderPacket.Order) {
                        shell.UpdateReadLineContext(snapshot);
                    }
                    break;
                case SEND_READ_FLAG.id:
                    var flag = IPacket.ReadUnmanaged<SEND_READ_FLAG>(content);
                    client.Send(new CONFIRM_READ_FLAG(flag.Flags, flag.Order));
                    if (pendingReadOrders.TryAdd(flag.Order, 0)) {
                        readFlagsChannel.Writer.TryWrite(flag);
                    }
                    break;
            }
        }
        public static async Task Run(ConsoleClient client) {
            await foreach (var item in readFlagsChannel.Reader.ReadAllAsync()) {
                try {
                    switch (item.Flags) {
                        case ReadFlags.Read:
                            client.Send(new PUSH_READ(Console.Read(), item.Order));
                            break;
                        case ReadFlags.ReadLine:
                            ReadLineRenderSnapshot render = readlineRenders.TryGetValue(item.Order, out var snapshot)
                                ? snapshot
                                : ReadLineRenderSnapshot.CreateCommandLine();
                            Interlocked.Exchange(ref activeReadLineOrder, item.Order);
                            try {
                                string line = shell.ReadLine(
                                    render,
                                    onInputStateChanged: state => client.SendManaged(new PUSH_READLINE_INPUT(
                                        item.Order,
                                        state.InputText,
                                        state.CursorIndex,
                                        state.CompletionIndex,
                                        state.CompletionCount,
                                        state.CandidateWindowOffset)));
                                client.SendManaged(new PUSH_READLINE(line, item.Order));
                            }
                            finally {
                                Interlocked.Exchange(ref activeReadLineOrder, -1);
                                readlineRenders.TryRemove(item.Order, out _);
                            }
                            break;
                        case ReadFlags.ReadKey:
                            client.SendManaged(new PUSH_READKEY(Console.ReadKey(), item.Order));
                            break;
                        case ReadFlags.ReadKeyIntercept:
                            client.SendManaged(new PUSH_READKEY(Console.ReadKey(true), item.Order));
                            break;
                    }
                }
                finally {
                    pendingReadOrders.TryRemove(item.Order, out _);
                }
            }
        }
    }
}
