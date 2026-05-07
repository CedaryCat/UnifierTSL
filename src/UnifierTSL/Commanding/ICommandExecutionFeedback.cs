namespace UnifierTSL.Commanding
{
    public enum CommandExecutionProgressStyle : byte
    {
        Ratio,
        Percent,
    }

    public interface ICommandExecutionFeedback
    {
        CancellationToken CancellationToken { get; }

        bool SupportsLiveProgress { get; }

        bool SupportsBackgroundExecution { get; }

        void SetStatus(string message);

        void SetProgress(
            long current,
            long total,
            CommandExecutionProgressStyle style = CommandExecutionProgressStyle.Ratio);

        void ClearProgress();
    }
}
