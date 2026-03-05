using UnifierTSL.CLI.CommandHint;
using UnifierTSL.CLI.Sessions;

namespace TShockAPI.CommandHint
{
    internal static class BuiltinCommandAnnotationCatalog
    {
        public static IReadOnlyList<ConsoleCommandAnnotation> All { get; } = Build();

        private static IReadOnlyList<ConsoleCommandAnnotation> Build()
        {
            return [
                new ConsoleCommandAnnotation {
                    PrimaryName = "tp",
                    ParameterHint = "<player> [player2]",
                    Patterns = [
                        Pattern([], P("player", ReadLineTargetKeys.Player), P("player2", ReadLineTargetKeys.Player, optional: true))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "tphere",
                    ParameterHint = "<player|*>",
                    Patterns = [
                        Pattern([], P("player", ReadLineTargetKeys.Player))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "kick",
                    ParameterHint = "<player> [reason]",
                    Patterns = [
                        Pattern([], P("player", ReadLineTargetKeys.Player), P("reason", ReadLineTargetKeys.Plain, optional: true, variadic: true))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "mute",
                    Aliases = ["unmute"],
                    ParameterHint = "<player> [reason]",
                    Patterns = [
                        Pattern([], P("player", ReadLineTargetKeys.Player), P("reason", ReadLineTargetKeys.Plain, optional: true, variadic: true))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "heal",
                    ParameterHint = "<player> [amount]",
                    Patterns = [
                        Pattern([], P("player", ReadLineTargetKeys.Player), P("amount", ReadLineTargetKeys.Plain, optional: true))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "kill",
                    Aliases = ["slay"],
                    ParameterHint = "<player>",
                    Patterns = [
                        Pattern([], P("player", ReadLineTargetKeys.Player))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "slap",
                    ParameterHint = "<player> [damage]",
                    Patterns = [
                        Pattern([], P("player", ReadLineTargetKeys.Player), P("damage", ReadLineTargetKeys.Plain, optional: true))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "whisper",
                    Aliases = ["w", "tell", "pm", "dm"],
                    ParameterHint = "<player> <message>",
                    Patterns = [
                        Pattern([], P("player", ReadLineTargetKeys.Player), P("message", ReadLineTargetKeys.Plain, variadic: true))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "warp",
                    ParameterHint = "<name|subcommand>",
                    Patterns = [
                        Pattern(["list"], P("page", ReadLineTargetKeys.Plain, optional: true)),
                        Pattern(["add"], P("name", ReadLineTargetKeys.Plain)),
                        Pattern(["del"], P("name", ReadLineTargetKeys.Plain)),
                        Pattern(["hide"], P("name", ReadLineTargetKeys.Plain), P("enabled", ReadLineTargetKeys.Boolean)),
                        Pattern(["send"], P("player", ReadLineTargetKeys.Player), P("name", ReadLineTargetKeys.Plain)),
                        Pattern([], P("name", ReadLineTargetKeys.Plain)),
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "group",
                    ParameterHint = "<subcommand> ...",
                    Patterns = [
                        Pattern(["help"], P("page", ReadLineTargetKeys.Plain, optional: true)),
                        Pattern(["add"], P("group", ReadLineTargetKeys.Plain), P("permissions", ReadLineTargetKeys.Plain, optional: true, variadic: true)),
                        Pattern(["addperm"], P("group", ReadLineTargetKeys.Plain), P("permissions", ReadLineTargetKeys.Plain, variadic: true)),
                        Pattern(["del"], P("group", ReadLineTargetKeys.Plain)),
                        Pattern(["delperm"], P("group", ReadLineTargetKeys.Plain), P("permissions", ReadLineTargetKeys.Plain, variadic: true)),
                        Pattern(["list"], P("page", ReadLineTargetKeys.Plain, optional: true)),
                        Pattern(["listperm"], P("group", ReadLineTargetKeys.Plain), P("page", ReadLineTargetKeys.Plain, optional: true)),
                        Pattern(["parent"], P("group", ReadLineTargetKeys.Plain), P("parent", ReadLineTargetKeys.Plain, optional: true)),
                        Pattern(["prefix"], P("group", ReadLineTargetKeys.Plain), P("prefix", ReadLineTargetKeys.Plain, variadic: true)),
                        Pattern(["suffix"], P("group", ReadLineTargetKeys.Plain), P("suffix", ReadLineTargetKeys.Plain, variadic: true)),
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "worldevent",
                    ParameterHint = "<event> [detail]",
                    Patterns = [
                        Pattern([], P("event", ReadLineTargetKeys.Enum, enumCandidates: ["meteor", "fullmoon", "bloodmoon", "eclipse", "invasion", "sandstorm", "rain", "lanternsnight", "meteorshower"]), P("detail", ReadLineTargetKeys.Plain, optional: true))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "worldmode",
                    Aliases = ["gamemode"],
                    ParameterHint = "<normal|expert|master|journey|creative>",
                    Patterns = [
                        Pattern([], P("mode", ReadLineTargetKeys.Enum, enumCandidates: ["normal", "expert", "master", "journey", "creative"]))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "time",
                    ParameterHint = "<day|night|noon|midnight|hh:mm>",
                    Patterns = [
                        Pattern([], P("time", ReadLineTargetKeys.Enum, optional: true, enumCandidates: ["day", "night", "noon", "midnight"]))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "wind",
                    ParameterHint = "<mph>",
                    Patterns = [
                        Pattern([], P("mph", ReadLineTargetKeys.Plain))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "item",
                    Aliases = ["i"],
                    ParameterHint = "<item> [amount] [prefix]",
                    Patterns = [
                        Pattern([], P("item", ReadLineTargetKeys.Item), P("amount", ReadLineTargetKeys.Plain, optional: true), P("prefix", ReadLineTargetKeys.Plain, optional: true))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "give",
                    Aliases = ["g"],
                    ParameterHint = "<item> <player> [amount] [prefix]",
                    Patterns = [
                        Pattern([], P("item", ReadLineTargetKeys.Item), P("player", ReadLineTargetKeys.Player), P("amount", ReadLineTargetKeys.Plain, optional: true), P("prefix", ReadLineTargetKeys.Plain, optional: true))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "help",
                    ParameterHint = "[command|page]",
                    Patterns = [
                        Pattern([], P("target", ReadLineTargetKeys.Plain, optional: true))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "aliases",
                    ParameterHint = "<command>",
                    Patterns = [
                        Pattern([], P("command", ReadLineTargetKeys.Plain))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "transfer",
                    Aliases = ["connect", "tr", "worldwarp", "ww"],
                    ParameterHint = "<server>",
                    Patterns = [
                        Pattern([], P("server", ReadLineTargetKeys.Server))
                    ],
                },
                new ConsoleCommandAnnotation {
                    PrimaryName = "servers",
                    Aliases = ["serverlist"],
                    ParameterHint = string.Empty,
                    Patterns = [],
                },
            ];
        }

        private static ConsoleCommandPatternAnnotation Pattern(
            IReadOnlyList<string> subCommands,
            params ConsoleCommandParameterAnnotation[] parameters)
        {
            return new ConsoleCommandPatternAnnotation {
                SubCommands = subCommands,
                Parameters = parameters,
            };
        }

        private static ConsoleCommandParameterAnnotation P(
            string name,
            ReadLineTargetKey target,
            bool optional = false,
            bool variadic = false,
            IReadOnlyList<string>? enumCandidates = null)
        {
            return new ConsoleCommandParameterAnnotation {
                Name = name,
                Target = target,
                Optional = optional,
                Variadic = variadic,
                EnumCandidates = enumCandidates ?? [],
            };
        }
    }
}

