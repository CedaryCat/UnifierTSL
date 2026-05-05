namespace UnifierTSL.Commanding.Jobs
{
    public interface ICommandJobUpdateSink
    {
        void SetStatus(string? statusText);

        void SetProgress(long current, long? total = null);

        void ClearProgress();
    }

    public interface ICommandJobExecutor
    {
        ValueTask<CommandJobExecutionResult> ExecuteAsync(
            CommandJobExecutionContext context,
            CancellationToken cancellationToken = default);
    }

    public interface ICommandJobRunner
    {
        ValueTask<CommandJobStartResult> StartAsync(
            CommandJobRequest request,
            CancellationToken cancellationToken = default);

        ValueTask<CommandJobSnapshot?> GetAsync(
            CommandJobId jobId,
            CancellationToken cancellationToken = default);

        ValueTask<bool> TryCancelAsync(
            CommandJobId jobId,
            CancellationToken cancellationToken = default);
    }
}
