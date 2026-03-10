using UnifierTSL.CLI.Prompting;
using Kind = UnifierTSL.CLI.Prompting.ConsoleSuggestionKind;

namespace CommandTeleport
{
    internal static class CommandTeleportConsoleCommandSpecs
    {
        public static IReadOnlyList<ConsoleCommandSpec> All { get; } = Build();

        private static IReadOnlyList<ConsoleCommandSpec> Build()
        {
            return [
                new ConsoleCommandSpec {
                    PrimaryName = "transfer",
                    Aliases = ["connect", "tr", "worldwarp", "ww"],
                    Patterns = [
                        Pattern([], P("server", Kind.Server))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "servers",
                    Aliases = ["serverlist"],
                    Patterns = [],
                },
            ];
        }

        private static ConsoleCommandPatternSpec Pattern(
            IReadOnlyList<string> subCommands,
            params ConsoleCommandParameterSpec[] parameters)
        {
            return new ConsoleCommandPatternSpec {
                SubCommands = [.. subCommands],
                Parameters = [.. parameters],
            };
        }

        private static ConsoleCommandParameterSpec P(
            string name,
            Kind kind,
            bool optional = false,
            bool variadic = false,
            IReadOnlyList<string>? enumCandidates = null)
        {
            return new ConsoleCommandParameterSpec {
                Name = name,
                Kind = kind,
                Optional = optional,
                Variadic = variadic,
                EnumCandidates = [.. enumCandidates ?? []],
            };
        }
    }
}
