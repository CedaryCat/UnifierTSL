using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.S2C
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct CONFIRM_READ_FLAG(ReadFlags mode, long Order) : IUnmanagedPacket<CONFIRM_READ_FLAG>
    {
        public const int id = 0x0A;
        public static int ID => 0x0A;
        public ReadFlags Flag = mode;
        public long Order = Order;
        public readonly override string ToString() {
            return nameof(CONFIRM_READ_FLAG) + ':' + Flag.ToString();
        }
    }
}
