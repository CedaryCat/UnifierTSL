using System.Text;

namespace UnifierTSL.Extensions;

public static class StringBuilderExt
{
    public static void RemoveLastNewLine(this StringBuilder sb) {
        if (sb.Length == 0) return;

        if (sb.Length >= 2 && sb[^2] == '\r' && sb[^1] == '\n') {
            sb.Length -= 2;
        }
        else if (sb[^1] == '\n') {
            sb.Length -= 1;
        }
    }
}
