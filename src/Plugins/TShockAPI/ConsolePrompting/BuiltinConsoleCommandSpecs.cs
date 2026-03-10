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
                        Pattern(["color"], P("group", Kind.Plain), P("rgb", Kind.Plain, optional: true)),
                        Pattern(["del"], P("group", Kind.Plain)),
                        Pattern(["delperm"], P("group", Kind.Plain), P("permissions", Kind.Plain, variadic: true)),
                        Pattern(["list"], P("page", Kind.Plain, optional: true)),
                        Pattern(["listperm"], P("group", Kind.Plain), P("page", Kind.Plain, optional: true)),
                        Pattern(["parent"], P("group", Kind.Plain), P("parent", Kind.Plain, optional: true, variadic: true)),
                        Pattern(["prefix"], P("group", Kind.Plain), P("prefix", Kind.Plain, variadic: true)),
                        Pattern(["rename"], P("group", Kind.Plain), P("new-name", Kind.Plain)),
                        Pattern(["suffix"], P("group", Kind.Plain), P("suffix", Kind.Plain, variadic: true)),
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "ban",
                    Patterns = [
                        Pattern(["help"], P("topic", Kind.Enum, optional: true, enumCandidates: ["add", "del", "list", "details", "identifiers", "examples"])),
                        Pattern(["add"], P("target", Kind.Plain), P("reason", Kind.Plain, optional: true), P("duration", Kind.Plain, optional: true), P("flags", Kind.Enum, optional: true, variadic: true, enumCandidates: ["-a", "-u", "-n", "-ip", "-e"])),
                        Pattern(["del"], P("ticket", Kind.Plain, semanticKey: TShockConsoleParameterSemanticKeys.BanTicket)),
                        Pattern(["list"], P("page", Kind.Plain, optional: true)),
                        Pattern(["details"], P("ticket", Kind.Plain, semanticKey: TShockConsoleParameterSemanticKeys.BanTicket)),
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "user",
                    Patterns = [
                        Pattern(["help"]),
                        Pattern(["add"], P("username", Kind.Plain), P("password", Kind.Plain), P("group", Kind.Plain)),
                        Pattern(["del"], P("username", Kind.Plain)),
                        Pattern(["password"], P("username", Kind.Plain), P("new-password", Kind.Plain)),
                        Pattern(["group"], P("username", Kind.Plain), P("group", Kind.Plain)),
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "accountinfo",
                    Aliases = ["ai"],
                    Patterns = [
                        Pattern([], P("username", Kind.Plain, variadic: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "userinfo",
                    Aliases = ["ui"],
                    Patterns = [
                        Pattern([], P("player", Kind.Player))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "password",
                    Patterns = [
                        Pattern([], P("old-password", Kind.Plain), P("new-password", Kind.Plain))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "worldevent",
                    Patterns = [
                        Pattern([], P("event", Kind.Enum, enumCandidates: ["meteor", "fullmoon", "bloodmoon", "eclipse", "invasion", "sandstorm", "rain", "lanterns", "lanternsnight", "meteorshower"]), P("detail", Kind.Plain, optional: true), P("detail2", Kind.Plain, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "worldmode",
                    Aliases = ["gamemode"],
                    Patterns = [
                        Pattern([], P("mode", Kind.Enum, enumCandidates: ["normal", "expert", "master", "journey", "creative", "0", "1", "2", "3"]))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "time",
                    Patterns = [
                        Pattern([], P("time-or-hh:mm", Kind.Enum, optional: true, enumCandidates: ["day", "night", "noon", "midnight"]))
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
                        Pattern([], P("item", Kind.Item), P("amount", Kind.Plain, optional: true), P("prefix", Kind.Plain, optional: true, semanticKey: TShockConsoleParameterSemanticKeys.PrefixRef))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "give",
                    Aliases = ["g"],
                    Patterns = [
                        Pattern([], P("item", Kind.Item), P("player", Kind.Player), P("amount", Kind.Plain, optional: true), P("prefix", Kind.Plain, optional: true, semanticKey: TShockConsoleParameterSemanticKeys.PrefixRef))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "buff",
                    Patterns = [
                        Pattern([], P("buff", Kind.Plain, semanticKey: TShockConsoleParameterSemanticKeys.BuffRef), P("duration", Kind.Plain, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "gbuff",
                    Aliases = ["buffplayer"],
                    Patterns = [
                        Pattern([], P("player", Kind.Player), P("buff", Kind.Plain, semanticKey: TShockConsoleParameterSemanticKeys.BuffRef), P("duration", Kind.Plain, optional: true))
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
                    PrimaryName = "broadcast",
                    Aliases = ["bc", "say"],
                    Patterns = [
                        Pattern([], P("message", Kind.Plain, variadic: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "off",
                    Aliases = ["exit", "stop"],
                    Patterns = [
                        Pattern([], P("reason", Kind.Plain, optional: true, variadic: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "off-nosave",
                    Aliases = ["exit-nosave", "stop-nosave"],
                    Patterns = [
                        Pattern([], P("reason", Kind.Plain, optional: true, variadic: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "whitelist",
                    Patterns = [
                        Pattern([], P("ip-or-range", Kind.Plain))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "serverpassword",
                    Patterns = [
                        Pattern([], P("password", Kind.Plain))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "overridessc",
                    Aliases = ["ossc"],
                    Patterns = [
                        Pattern([], P("player", Kind.Player))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "uploadssc",
                    Patterns = [
                        Pattern([], P("player", Kind.Player, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "tempgroup",
                    Patterns = [
                        Pattern([], P("player", Kind.Player), P("group", Kind.Plain), P("time", Kind.Plain, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "sudo",
                    Patterns = [
                        Pattern([], P("command", Kind.Plain, variadic: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "rest",
                    Patterns = [
                        Pattern(["listusers"], P("page", Kind.Plain, optional: true)),
                        Pattern(["destroytokens"]),
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "maxspawns",
                    Patterns = [
                        Pattern([], P("max-or-default", Kind.Plain, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "spawnrate",
                    Patterns = [
                        Pattern([], P("rate-or-default", Kind.Plain, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "spawnboss",
                    Aliases = ["sb"],
                    Patterns = [
                        Pattern([], P("boss", Kind.Enum, enumCandidates: ["*", "all", "brain", "boc", "destroyer", "duke", "fishron", "eater", "eow", "eye", "eoc", "golem", "king", "ks", "plantera", "prime", "qb", "skeletron", "twins", "wof", "moon", "ml", "empress", "eol", "qs", "lunatic", "cultist", "lc", "betsy", "flying", "dutchman", "pumpking", "everscream", "santa", "martian", "solar", "nebula", "vortex", "stardust", "deerclops"]), P("amount", Kind.Plain, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "spawnmob",
                    Aliases = ["sm"],
                    Patterns = [
                        Pattern([], P("mob", Kind.Plain, semanticKey: TShockConsoleParameterSemanticKeys.NpcRef), P("amount", Kind.Plain, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "tpnpc",
                    Patterns = [
                        Pattern([], P("npc", Kind.Plain, variadic: true, semanticKey: TShockConsoleParameterSemanticKeys.NpcRef))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "tppos",
                    Patterns = [
                        Pattern([], P("x", Kind.Plain), P("y", Kind.Plain))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "pos",
                    Patterns = [
                        Pattern([], P("player", Kind.Player, optional: true, variadic: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "region",
                    Patterns = [
                        Pattern(["help"], P("page", Kind.Plain, optional: true)),
                        Pattern(["set"], P("point", Kind.Enum, enumCandidates: ["1", "2"])),
                        Pattern(["clear"]),
                        Pattern(["define"], P("name", Kind.Plain, variadic: true)),
                        Pattern(["protect"], P("name", Kind.Plain), P("enabled", Kind.Boolean)),
                        Pattern(["delete"], P("name", Kind.Plain, variadic: true)),
                        Pattern(["allow"], P("user", Kind.Plain), P("region", Kind.Plain, variadic: true)),
                        Pattern(["remove"], P("user", Kind.Plain), P("region", Kind.Plain, variadic: true)),
                        Pattern(["allowg"], P("group", Kind.Plain), P("region", Kind.Plain, variadic: true)),
                        Pattern(["removeg"], P("group", Kind.Plain), P("region", Kind.Plain, variadic: true)),
                        Pattern(["list"], P("page", Kind.Plain, optional: true)),
                        Pattern(["info"], P("region", Kind.Plain), P("flag", Kind.Enum, optional: true, enumCandidates: ["-d"]), P("page", Kind.Plain, optional: true)),
                        Pattern(["z"], P("region", Kind.Plain), P("z", Kind.Plain)),
                        Pattern(["resize"], P("region", Kind.Plain), P("direction", Kind.Enum, enumCandidates: ["u", "up", "r", "right", "d", "down", "l", "left"]), P("amount", Kind.Plain)),
                        Pattern(["expand"], P("region", Kind.Plain), P("direction", Kind.Enum, enumCandidates: ["u", "up", "r", "right", "d", "down", "l", "left"]), P("amount", Kind.Plain)),
                        Pattern(["rename"], P("region", Kind.Plain), P("new-name", Kind.Plain)),
                        Pattern(["tp"], P("region", Kind.Plain, variadic: true)),
                        Pattern(["name"], P("flags", Kind.Enum, optional: true, variadic: true, enumCandidates: ["-u", "-z", "-p"])),
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "itemban",
                    Patterns = [
                        Pattern(["help"], P("page", Kind.Plain, optional: true)),
                        Pattern(["add"], P("item", Kind.Item)),
                        Pattern(["allow"], P("item", Kind.Item), P("group", Kind.Plain)),
                        Pattern(["del"], P("item", Kind.Item)),
                        Pattern(["disallow"], P("item", Kind.Item), P("group", Kind.Plain)),
                        Pattern(["list"], P("page", Kind.Plain, optional: true)),
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "projban",
                    Patterns = [
                        Pattern(["help"], P("page", Kind.Plain, optional: true)),
                        Pattern(["add"], P("projectile-id", Kind.Plain)),
                        Pattern(["allow"], P("projectile-id", Kind.Plain), P("group", Kind.Plain)),
                        Pattern(["del"], P("projectile-id", Kind.Plain)),
                        Pattern(["disallow"], P("projectile-id", Kind.Plain), P("group", Kind.Plain)),
                        Pattern(["list"], P("page", Kind.Plain, optional: true)),
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "tileban",
                    Patterns = [
                        Pattern(["help"], P("page", Kind.Plain, optional: true)),
                        Pattern(["add"], P("tile-id", Kind.Plain)),
                        Pattern(["allow"], P("tile-id", Kind.Plain), P("group", Kind.Plain)),
                        Pattern(["del"], P("tile-id", Kind.Plain)),
                        Pattern(["disallow"], P("tile-id", Kind.Plain), P("group", Kind.Plain)),
                        Pattern(["list"], P("page", Kind.Plain, optional: true)),
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "clear",
                    Patterns = [
                        Pattern([], P("target", Kind.Enum, enumCandidates: ["item", "items", "i", "npc", "npcs", "n", "projectile", "projectiles", "proj", "p"]), P("radius", Kind.Plain, optional: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "respawn",
                    Patterns = [
                        Pattern([], P("player", Kind.Player, optional: true, variadic: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "butcher",
                    Patterns = [
                        Pattern([], P("npc", Kind.Plain, optional: true, semanticKey: TShockConsoleParameterSemanticKeys.NpcRef))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "godmode",
                    Aliases = ["god"],
                    Patterns = [
                        Pattern([], P("player", Kind.Player, optional: true, variadic: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "me",
                    Patterns = [
                        Pattern([], P("message", Kind.Plain, variadic: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "party",
                    Aliases = ["p"],
                    Patterns = [
                        Pattern([], P("message", Kind.Plain, variadic: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "reply",
                    Aliases = ["r"],
                    Patterns = [
                        Pattern([], P("message", Kind.Plain, variadic: true))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "wallow",
                    Aliases = ["wa"],
                    Patterns = [],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "annoy",
                    Patterns = [
                        Pattern([], P("player", Kind.Player), P("seconds", Kind.Plain))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "rocket",
                    Patterns = [
                        Pattern([], P("player", Kind.Player))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "firework",
                    Patterns = [
                        Pattern([], P("player", Kind.Player), P("style", Kind.Enum, optional: true, enumCandidates: ["r", "g", "b", "y", "red", "green", "blue", "yellow", "r2", "g2", "b2", "y2", "star", "spiral", "rings", "flower"]))
                    ],
                },
                new ConsoleCommandSpec {
                    PrimaryName = "sync",
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
            IReadOnlyList<string>? enumCandidates = null,
            string? semanticKey = null)
        {
            return new ConsoleCommandParameterSpec {
                Name = name,
                Kind = kind,
                SemanticKey = semanticKey ?? InferSemanticKey(kind),
                Optional = optional,
                Variadic = variadic,
                EnumCandidates = [.. enumCandidates ?? []],
            };
        }

        private static string? InferSemanticKey(Kind kind)
        {
            return kind switch {
                Kind.Player => TShockConsoleParameterSemanticKeys.PlayerRef,
                Kind.Item => TShockConsoleParameterSemanticKeys.ItemRef,
                _ => null,
            };
        }
    }
}
