using System.Collections.Immutable;
using UnifierTSL.Commanding.Endpoints;

namespace UnifierTSL.Commanding.Jobs
{
    public readonly record struct CommandJobId
    {
        public CommandJobId(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                throw new ArgumentException(GetString("Command job id must not be empty."), nameof(value));
            }

            Value = value.Trim();
        }

        public string Value { get; }

        public static CommandJobId CreateNew() {
            return new(Guid.NewGuid().ToString("n"));
        }

        public override string ToString() {
            return Value;
        }
    }

    public enum CommandJobState : byte
    {
        Pending,
        Running,
        CancelRequested,
        Succeeded,
        Failed,
        Canceled,
    }

    public readonly record struct CommandJobProgressSnapshot(long Current, long? Total)
    {
        public bool IsIndeterminate => !Total.HasValue;
    }

    public sealed record CommandJobRequest
    {
        public required CommandDispatchRequest DispatchRequest { get; init; }

        public string? Title { get; init; }

        public ImmutableDictionary<string, string?> Metadata { get; init; } = ImmutableDictionary<string, string?>.Empty;
    }

    public sealed record CommandJobSnapshot
    {
        public required CommandJobId Id { get; init; }

        public required CommandEndpointId EndpointId { get; init; }

        public required string RawInput { get; init; }

        public string? Title { get; init; }

        public CommandJobState State { get; init; }

        public string? StatusText { get; init; }

        public CommandJobProgressSnapshot? Progress { get; init; }

        public required DateTimeOffset CreatedUtc { get; init; }

        public required DateTimeOffset UpdatedUtc { get; init; }

        public DateTimeOffset? StartedUtc { get; init; }

        public DateTimeOffset? CompletedUtc { get; init; }

        public bool CancellationRequested { get; init; }

        public bool Handled { get; init; }

        public bool Matched { get; init; }

        public CommandOutcome? Outcome { get; init; }

        public ImmutableDictionary<string, string?> Metadata { get; init; } = ImmutableDictionary<string, string?>.Empty;

        public bool Completed => State is CommandJobState.Succeeded or CommandJobState.Failed or CommandJobState.Canceled;

        public bool Succeeded => State == CommandJobState.Succeeded;

        public bool CanCancel => State is CommandJobState.Pending or CommandJobState.Running;
    }

    public sealed record CommandJobStartResult
    {
        public required CommandJobId JobId { get; init; }

        public required CommandJobSnapshot Snapshot { get; init; }
    }

    public sealed record CommandJobExecutionContext
    {
        public required CommandJobId JobId { get; init; }

        public required CommandJobRequest Request { get; init; }

        public required ICommandJobUpdateSink UpdateSink { get; init; }

        public required CancellationToken CancellationToken { get; init; }

        public CommandDispatchRequest DispatchRequest => Request.DispatchRequest;
    }

    public sealed record CommandJobExecutionResult
    {
        public bool Handled { get; init; }

        public bool Matched { get; init; }

        public CommandOutcome? Outcome { get; init; }

        public bool Succeeded => Handled && Matched && (Outcome?.Succeeded ?? true);

        public static CommandJobExecutionResult FromDispatch(CommandDispatchResult result) {
            ArgumentNullException.ThrowIfNull(result);

            return new CommandJobExecutionResult {
                Handled = result.Handled,
                Matched = result.Matched,
                Outcome = result.Outcome,
            };
        }
    }

    public sealed class CommandJobRunnerOptions
    {
        public int MaximumRetainedJobs { get; init; } = 256;

        public TimeSpan CompletedJobRetention { get; init; } = TimeSpan.FromHours(1);
    }
}
