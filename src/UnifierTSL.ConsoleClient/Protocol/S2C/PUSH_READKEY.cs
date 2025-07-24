using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.S2C
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PUSH_READKEY(ConsoleKeyInfo key, long order) : IUnmanagedPacket<PUSH_READKEY>
    {
        public const int id = 0x04;
        public static int ID => id;
        public ConsoleKeyInfo KeyInfo = key;
        public long Order = order;
        public readonly override string ToString() {
            return nameof(PUSH_READKEY) + $":[{Order}]" + KeyInfo;
        }
    }
}
