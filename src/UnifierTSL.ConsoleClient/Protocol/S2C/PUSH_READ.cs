using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.S2C
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PUSH_READ(int result, long order) : IUnmanagedPacket<PUSH_READ>
    {
        public const int id = 0x02;
        public static int ID => id;
        public int ReadResult = result;
        public long Order = order;
        public readonly override string ToString() {
            return nameof(PUSH_READ) + $":[{Order}]" + ReadResult;
        }
    }
}
