using UnifierTSL.Contracts.Display;

namespace UnifierTSL.Terminal.Shell
{
    internal readonly record struct StatusRenderLine(StyledTextLine? StyledText, string Text, string LineStyleId)
    {
        public static StatusRenderLine FromStyled(StyledTextLine? line) {
            return new(line, string.Empty, line?.LineStyleId ?? string.Empty);
        }

        public static StatusRenderLine FromText(string text, string lineStyleId = "") {
            return new(null, text ?? string.Empty, lineStyleId ?? string.Empty);
        }
    }
}
