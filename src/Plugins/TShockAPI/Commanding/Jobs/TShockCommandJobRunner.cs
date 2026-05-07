using UnifierTSL.Commanding.Jobs;

namespace TShockAPI.Commanding.Jobs
{
    internal sealed class TShockCommandJobRunner(
        CommandJobRunnerOptions? options = null,
        TimeProvider? timeProvider = null,
        Func<CommandJobId>? idFactory = null) : ICommandJobRunner, IDisposable
    {
        private readonly InMemoryCommandJobRunner inner = new(
            new CommandDispatchJobExecutor(),
            options,
            timeProvider,
            idFactory);

        public ValueTask<CommandJobStartResult> StartAsync(
            TShockCommandJobRequest request,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(request);

            return inner.StartAsync(new CommandJobRequest {
                DispatchRequest = TSCommandBridge.CreateDispatchRequest(
                    request.Executor,
                    request.EndpointId,
                    request.RawInput,
                    request.Silent),
                Title = request.Title,
                Metadata = request.Metadata,
            }, cancellationToken);
        }

        public ValueTask<CommandJobStartResult> StartAsync(
            CommandJobRequest request,
            CancellationToken cancellationToken = default) {
            return inner.StartAsync(request, cancellationToken);
        }

        public ValueTask<CommandJobSnapshot?> GetAsync(
            CommandJobId jobId,
            CancellationToken cancellationToken = default) {
            return inner.GetAsync(jobId, cancellationToken);
        }

        public ValueTask<bool> TryCancelAsync(
            CommandJobId jobId,
            CancellationToken cancellationToken = default) {
            return inner.TryCancelAsync(jobId, cancellationToken);
        }

        public void Dispose() {
            inner.Dispose();
        }
    }
}
