using UnifierTSL.ConsoleClient.Protocol;
using UnifierTSL.ConsoleClient.Protocol.C2S;
using UnifierTSL.ConsoleClient.Protocol.S2C;
using System.Threading.Channels;

namespace UnifierTSL.ConsoleClient
{
    public static class ConsoleClientLogic
    {
        static readonly Channel<SEND_READ_FLAG> settedFlags = Channel.CreateUnbounded<SEND_READ_FLAG>();
        public static unsafe void ProcessData(ConsoleClient client, byte id, Span<byte> content) {
            switch (id) {
                case SET_BG_COLOR.id:
                    Console.BackgroundColor = IPacket.ReadUnmanaged<SET_BG_COLOR>(content).Color;
                    break;
                case SET_FG_COLOR.id:
                    Console.ForegroundColor = IPacket.ReadUnmanaged<SET_FG_COLOR>(content).Color;
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
                    Console.Clear();
                    break;
                case SEND_WRITE.id:
                    Console.Write(IPacket.Read<SEND_WRITE>(content).Text);
                    break;
                case SEND_WRITE_LINE.id:
                    Console.WriteLine(IPacket.Read<SEND_WRITE_LINE>(content).Text);
                    break;
                case SEND_READ_FLAG.id:
                    var flag = IPacket.ReadUnmanaged<SEND_READ_FLAG>(content);
                    client.Send(new CONFIRM_READ_FLAG(flag.Flags, flag.Order));
                    settedFlags.Writer.TryWrite(flag);
                    break;
            }
        }
        public static async Task Run(ConsoleClient client) {
            await foreach (var item in settedFlags.Reader.ReadAllAsync()) {
                switch (item.Flags) {
                    case ReadFlags.Read:
                        client.Send(new PUSH_READ(Console.Read(), item.Order));
                        break;
                    case ReadFlags.ReadLine:
                        client.SendManaged(new PUSH_READLINE(Console.ReadLine() ?? "", item.Order));
                        break;
                    case ReadFlags.ReadKey:
                        client.SendManaged(new PUSH_READKEY(Console.ReadKey(), item.Order));
                        break;
                    case ReadFlags.ReadKeyIntercept:
                        client.SendManaged(new PUSH_READKEY(Console.ReadKey(true), item.Order));
                        break;
                }
            }
        }
    }
}