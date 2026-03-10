namespace UnifierTSL.Performance
{
    public readonly record struct PerformanceSnapshot(
        TimeSpan RequestedWindow,
        TimeSpan SampleDuration,
        bool HasFullWindow,
        int SampledFrames,
        TimeSpan UpdateTime,
        TimeSpan DrawTime,
        TimeSpan PresentTime,
        TimeSpan IdleTime,
        TimeSpan GCPauseTime,
        TimeSpan AverageFrameTime,
        TimeSpan MaxFrameTime,
        long AllocatedBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        uint ReceivedBytesCount,
        uint SentBytesCount,
        ushort ReceivedPacketCount,
        ushort SentPacketCount)
    {
        public static PerformanceSnapshot Empty(TimeSpan requestedWindow)
            => new(
                RequestedWindow: requestedWindow,
                SampleDuration: TimeSpan.Zero,
                HasFullWindow: false,
                SampledFrames: 0,
                UpdateTime: TimeSpan.Zero,
                DrawTime: TimeSpan.Zero,
                PresentTime: TimeSpan.Zero,
                IdleTime: TimeSpan.Zero,
                GCPauseTime: TimeSpan.Zero,
                AverageFrameTime: TimeSpan.Zero,
                MaxFrameTime: TimeSpan.Zero,
                AllocatedBytes: 0,
                Gen0Collections: 0,
                Gen1Collections: 0,
                Gen2Collections: 0,
                ReceivedBytesCount:0,
                SentBytesCount:0,
                ReceivedPacketCount: 0,
                SentPacketCount:0);

        public bool HasData => SampleDuration > TimeSpan.Zero;

        public TimeSpan BusyTime => UpdateTime + DrawTime;

        public double BusyUtilization => Divide(BusyTime, SampleDuration);

        public double UpdateUtilization => Divide(UpdateTime, SampleDuration);

        public double DrawUtilization => Divide(DrawTime, SampleDuration);

        public double PresentUtilization => Divide(PresentTime, SampleDuration);

        public double IdleUtilization => Divide(IdleTime, SampleDuration);

        public double GCPauseUtilization => Divide(GCPauseTime, SampleDuration);

        public double FramesPerSecond => Divide(SampledFrames, SampleDuration.TotalSeconds);

        public double TicksPerSecond => FramesPerSecond;

        public double AllocatedBytesPerSecond => Divide(AllocatedBytes, SampleDuration.TotalSeconds);

        private static double Divide(TimeSpan numerator, TimeSpan denominator)
            => Divide(numerator.TotalSeconds, denominator.TotalSeconds);

        private static double Divide(long numerator, double denominator)
            => Divide((double)numerator, denominator);

        private static double Divide(double numerator, double denominator)
            => denominator <= 0d ? 0d : numerator / denominator;
    }
}
