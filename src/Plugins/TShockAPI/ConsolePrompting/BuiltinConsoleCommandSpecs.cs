using UnifierTSL.CLI.Prompting;
using Kind = UnifierTSL.CLI.Prompting.ConsoleSuggestionKind;

namespace TShockAPI.ConsolePrompting
{
    internal static class BuiltinConsoleCommandSpecs
    {
        public static IReadOnlyList<ConsoleCommandSpec> All { get; } = Build();

        private static IReadOnlyList<ConsoleCommandSpec> Build()
        {
            return [
                new ConsoleCommandSpec {
                    PrimaryName = "tp",
                    Patterns = [
                        Pattern([], P("player", Kind.Player), P("player2", Kind.Player, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "tphere",
                    Patterns = [
                        Pattern([], P("player", Kind.Player))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "kick",
                    Patterns = [
                        Pattern([], P("player", Kind.Player), P("reason", Kind.Plain, optional: true, variadic: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "mute",
                    Aliases = ["unmute"],
                    Patterns = [
                        Pattern([], P("player", Kind.Player), P("reason", Kind.Plain, optional: true, variadic: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "heal",
                    Patterns = [
                        Pattern([], P("player", Kind.Player), P("amount", Kind.Plain, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "kill",
                    Aliases = ["slay"],
                    Patterns = [
                        Pattern([], P("player", Kind.Player))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "slap",
                    Patterns = [
                        Pattern([], P("player", Kind.Player), P("damage", Kind.Plain, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "whisper",
                    Aliases = ["w", "tell", "pm", "dm"],
                    Patterns = [
                        Pattern([], P("player", Kind.Player), P("message", Kind.Plain, variadic: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "warp",
                    Patterns = [
                        Pattern(["list"], P("page", Kind.Plain, optional: true)),
                        Pattern(["add"], P("name", Kind.Plain)),
                        Pattern(["del"], P("name", Kind.Plain)),
                        Pattern(["hide"], P("name", Kind.Plain), P("enabled", Kind.Boolean)),
                        Pattern(["send"], P("player", Kind.Player), P("name", Kind.Plain)),
                        Pattern([], P("name", Kind.Plain)),
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "group",
                    Patterns = [
                        Pattern(["help"], P("page", Kind.Plain, optional: true)),
                        Pattern(["add"], P("group", Kind.Plain), P("permissions", Kind.Plain, optional: true, variadic: true)),
                        Pattern(["addperm"], P("group", Kind.Plain), P("permissions", Kind.Plain, variadic: true)),
                        Pattern(["del"], P("group", Kind.Plain)),
                        Pattern(["delperm"], P("group", Kind.Plain), P("permissions", Kind.Plain, variadic: true)),
                        Pattern(["list"], P("page", Kind.Plain, optional: true)),
                        Pattern(["listperm"], P("group", Kind.Plain), P("page", Kind.Plain, optional: true)),
                        Pattern(["parent"], P("group", Kind.Plain), P("parent", Kind.Plain, optional: true)),
                        Pattern(["prefix"], P("group", Kind.Plain), P("prefix", Kind.Plain, variadic: true)),
                        Pattern(["suffix"], P("group", Kind.Plain), P("suffix", Kind.Plain, variadic: true)),
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "worldevent",
                    Patterns = [
                        Pattern([], P("event", Kind.Enum, enumCandidates: ["meteor", "fullmoon", "bloodmoon", "eclipse", "invasion", "sandstorm", "rain", "lanternsnight", "meteorshower"]), P("detail", Kind.Plain, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "worldmode",
                    Aliases = ["gamemode"],
                    Patterns = [
                        Pattern([], P("mode", Kind.Enum, enumCandidates: ["normal", "expert", "master", "journey", "creative"]))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "time",
                    Patterns = [
                        Pattern([], P("time", Kind.Enum, optional: true, enumCandidates: ["day", "night", "noon", "midnight"]))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "wind",
                    Patterns = [
                        Pattern([], P("mph", Kind.Plain))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "item",
                    Aliases = ["i"],
                    Patterns = [
                        Pattern([], P("item", Kind.Item), P("amount", Kind.Plain, optional: true), P("prefix", Kind.Plain, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "give",
                    Aliases = ["g"],
                    Patterns = [
                        Pattern([], P("item", Kind.Item), P("player", Kind.Player), P("amount", Kind.Plain, optional: true), P("prefix", Kind.Plain, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "help",
                    Patterns = [
                        Pattern([], P("target", Kind.Plain, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "aliases",
                    Patterns = [
                        Pattern([], P("command", Kind.Plain))
                    ],
                },
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
