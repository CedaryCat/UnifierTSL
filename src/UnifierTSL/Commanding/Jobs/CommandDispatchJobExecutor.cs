namespace UnifierTSL.Commanding.Jobs
{
    public sealed class CommandDispatchJobExecutor : ICommandJobExecutor
    {
        public async ValueTask<CommandJobExecutionResult> ExecuteAsync(
            CommandJobExecutionContext context,
            CancellationToken cancellationToken = default) {

            var result = await CommandDispatchCoordinator.DispatchAsync(context.DispatchRequest, cancellationToken);
            return CommandJobExecutionResult.FromDispatch(result);
        }
    }
}
