using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.C2S
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SEND_READ_FLAG(ReadFlags flags, long order) : IUnmanagedPacket<SEND_READ_FLAG>
    {
        public const int id = 0x0B;
        public static int ID => id;
        public ReadFlags Flags = flags;
        public long Order = order;
        public readonly override string ToString() {
            return nameof(SEND_READ_FLAG) + $":[{Order}]" + Flags.ToString();
        }
    }
}
