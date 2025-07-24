using System.Runtime.InteropServices;
using System.Text;

namespace UnifierTSL.ConsoleClient.Protocol.C2S
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SET_INPUT_ENCODING(Encoding encoding) : IUnmanagedPacket<SET_INPUT_ENCODING>
    {
        public const int id = 0x06;
        public static int ID => id;
        int encodingCodePage = encoding.CodePage;
        public Encoding Encoding {
            readonly get {
                return Encoding.GetEncoding(encodingCodePage);
            }
            set {
                encodingCodePage = value.CodePage;
            }
        }
        public readonly override string ToString() {
            return nameof(SET_INPUT_ENCODING) + ':' + Encoding;
        }
    }
}
