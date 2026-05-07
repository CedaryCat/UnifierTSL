using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;

namespace UnifierTSL.Terminal.Runtime {
    internal static class TerminalAnimatedTextAdapter {
        public static TerminalAnimatedText Create(TextNodeState? state) {
            var animation = state?.Animation;
            StyledTextLine[] frames = animation?.Frames is { Length: > 0 } animatedFrames
                ? [.. animatedFrames.Select(ProjectionStyledTextAdapter.ToStyledFirstLine)]
                : [];
            if (frames.Length == 0) {
                var fallback = ProjectionStyledTextAdapter.ToStyledFirstLine(state?.Content);
                if (StyledTextLineOps.HasVisibleText(fallback)) {
                    frames = [fallback];
                }
            }

            return new TerminalAnimatedText {
                FrameStepTicks = frames.Length <= 1
                    ? 0
                    : Math.Max(0, animation?.FrameStepTicks ?? 0),
                Frames = frames,
            };
        }
    }
}
