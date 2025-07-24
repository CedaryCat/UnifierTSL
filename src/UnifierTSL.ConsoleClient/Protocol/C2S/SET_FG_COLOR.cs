using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.C2S
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SET_FG_COLOR(ConsoleColor color) : IUnmanagedPacket<SET_FG_COLOR>
    {
        public const int id = 0x05;
        public static int ID => id;
        public ConsoleColor Color = color;
        public readonly override string ToString() {
            return nameof(SET_FG_COLOR) + ':' + Color;
        }
    }
}
