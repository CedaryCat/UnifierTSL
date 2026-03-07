using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.ClientToHost
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct CONFIRM_BEGIN_READ(ReadFlags flags, long Order) : IUnmanagedPacket<CONFIRM_BEGIN_READ>
    {
        public const int id = 0x0A;
        public static int ID => 0x0A;
        public ReadFlags Flags = flags;
        public long Order = Order;
        public readonly override string ToString() {
            return nameof(CONFIRM_BEGIN_READ) + ':' + Flags.ToString();
        }
    }
}
