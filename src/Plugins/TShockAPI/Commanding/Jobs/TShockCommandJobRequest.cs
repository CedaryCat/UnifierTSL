using System.Collections.Immutable;
using UnifierTSL.Commanding.Endpoints;

namespace TShockAPI.Commanding.Jobs
{
    internal sealed record TShockCommandJobRequest
    {
        public required CommandExecutor Executor { get; init; }

        public required CommandEndpointId EndpointId { get; init; }

        public required string RawInput { get; init; }

        public bool Silent { get; init; }

        public string? Title { get; init; }

        public ImmutableDictionary<string, string?> Metadata { get; init; } = ImmutableDictionary<string, string?>.Empty;
    }
}
