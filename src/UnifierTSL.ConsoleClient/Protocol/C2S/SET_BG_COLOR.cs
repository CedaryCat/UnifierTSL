using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.C2S
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SET_BG_COLOR(ConsoleColor color) : IUnmanagedPacket<SET_BG_COLOR>
    {
        public const int id = 0x04;
        public static int ID => id;
        public ConsoleColor Color = color;
        public readonly override string ToString() {
            return nameof(SET_BG_COLOR) + ':' + Color;
        }
    }
}
