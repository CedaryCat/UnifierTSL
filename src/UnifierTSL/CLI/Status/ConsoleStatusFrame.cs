using UnifierTSL.Servers;

namespace UnifierTSL.CLI.Status
{
    public readonly record struct ConsoleStatusResolveContext(
        ServerContext? Server,
        DateTimeOffset SampleUtc);

    public readonly record struct ConsoleStatusFrame(
        string Text,
        int IndicatorFrameIntervalMs = 0,
        string IndicatorStylePrefix = "",
        string IndicatorFrames = "");
}
