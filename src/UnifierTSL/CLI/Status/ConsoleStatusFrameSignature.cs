namespace UnifierTSL.CLI.Status
{
    internal static class ConsoleStatusFrameSignature
    {
        public static string Build(bool hasFrame, ConsoleStatusFrame frame) {
            if (!hasFrame) {
                return "<none>";
            }

            return (frame.Text ?? string.Empty)
                + "\0" + frame.IndicatorFrameIntervalMs
                + "\0" + (frame.IndicatorStylePrefix ?? string.Empty)
                + "\0" + (frame.IndicatorFrames ?? string.Empty);
        }
    }
}
