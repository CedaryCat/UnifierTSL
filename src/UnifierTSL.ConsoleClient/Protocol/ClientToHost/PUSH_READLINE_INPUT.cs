using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.ClientToHost
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PUSH_READLINE_INPUT(
        long order,
        string inputText,
        int cursorIndex,
        int completionIndex,
        int completionCount,
        int candidateWindowOffset) : IPacket<PUSH_READLINE_INPUT>
    {
        public const int id = 0x05;
        public static int ID => id;

        public long Order = order;
        public string InputText = inputText;
        public int CursorIndex = cursorIndex;
        public int CompletionIndex = completionIndex;
        public int CompletionCount = completionCount;
        public int CandidateWindowOffset = candidateWindowOffset;

        public static PUSH_READLINE_INPUT Read(Span<byte> content) {
            int offset = IPacket.ReadString(content, out string inputText);

            fixed (byte* ptr = content) {
                int index = offset;
                long order = *(long*)(ptr + index);
                index += sizeof(long);
                int cursorIndex = *(int*)(ptr + index);
                index += sizeof(int);
                int completionIndex = *(int*)(ptr + index);
                index += sizeof(int);
                int completionCount = *(int*)(ptr + index);
                index += sizeof(int);
                int candidateWindowOffset = *(int*)(ptr + index);
                return new(order, inputText, cursorIndex, completionIndex, completionCount, candidateWindowOffset);
            }
        }

        public static int WriteContent(Span<byte> buffer, PUSH_READLINE_INPUT packet) {
            int offset = IPacket.WriteString(buffer, packet.InputText);
            fixed (byte* ptr = buffer) {
                int index = offset;
                *(long*)(ptr + index) = packet.Order;
                index += sizeof(long);
                *(int*)(ptr + index) = packet.CursorIndex;
                index += sizeof(int);
                *(int*)(ptr + index) = packet.CompletionIndex;
                index += sizeof(int);
                *(int*)(ptr + index) = packet.CompletionCount;
                index += sizeof(int);
                *(int*)(ptr + index) = packet.CandidateWindowOffset;
                index += sizeof(int);
                return index;
            }
        }

        public readonly int GetBufferSize() {
            return IPacket.PacketHeaderSize
                + IPacket.GetStringBufferSize(InputText)
                + sizeof(long)
                + sizeof(int)
                + sizeof(int)
                + sizeof(int)
                + sizeof(int);
        }

        public readonly override string ToString() {
            return nameof(PUSH_READLINE_INPUT)
                + $":[{Order}]text='{InputText}',cursor={CursorIndex},comp={CompletionIndex}/{CompletionCount},window={CandidateWindowOffset}";
        }
    }
}
