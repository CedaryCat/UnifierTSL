using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.C2S
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SET_WINDOW_POS(int left, int top) : IUnmanagedPacket<SET_WINDOW_POS>
    {
        public const int id = 0x09;
        public static int ID => id;
        public int Left = left;
        public int Top = top;
        public readonly override string ToString() {
            return nameof(SET_WINDOW_POS) + ':' + Left + ',' + Top;
        }
    }
}
