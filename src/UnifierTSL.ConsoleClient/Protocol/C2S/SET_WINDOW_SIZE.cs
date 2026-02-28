using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.C2S
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SET_WINDOW_SIZE(int width, int height) : IUnmanagedPacket<SET_WINDOW_SIZE>
    {
        public const int id = 0x0A;
        public static int ID => id;
        public int Width = width;
        public int Height = height;
        public override string ToString() {
            return nameof(SET_WINDOW_SIZE) + ':' + Width + ',' + Height;
        }
    }
}
