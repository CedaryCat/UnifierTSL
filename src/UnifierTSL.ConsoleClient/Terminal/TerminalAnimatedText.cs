using UnifierTSL.Contracts.Display;

namespace UnifierTSL.Terminal {
    internal sealed class TerminalAnimatedText {
        public int FrameStepTicks { get; init; }
        public StyledTextLine[] Frames { get; init; } = [];

        public bool HasVisibleContent => Frames.Any(StyledTextLineOps.HasVisibleText);
    }
}
