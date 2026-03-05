using UnifierTSL.CLI.Sessions;

namespace UnifierTSL.CLI.CommandHint
{
    public sealed class ConsoleCommandParameterAnnotation
    {
        public string Name { get; init; } = string.Empty;

        public ReadLineTargetKey Target { get; init; } = ReadLineTargetKeys.Plain;

        public bool Optional { get; init; }

        public bool Variadic { get; init; }

        public IReadOnlyList<string> EnumCandidates { get; init; } = [];
    }
}
