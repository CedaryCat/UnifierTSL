using UnifierTSL;
using UnifierTSL.CLI;
using UnifierTSL.CLI.Prompting;
using UnifierTSL.Servers;

namespace TShockAPI.ConsolePrompting
{
    internal static class TShockConsolePromptInstaller
    {
        public static void Install()
        {
            TShockConsoleParameterExplainers.RegisterDefaults();
            ConsolePromptRegistry.SetDefaultCommandPromptSpecFactory(BuildPromptSpec);
        }

        private static ConsolePromptSpec BuildPromptSpec(ServerContext? server)
        {
            AnnotatedConsolePromptOptions options = new() {
                CommandPrefixResolver = BuildCommandPrefixes,
                CommandSpecResolver = () => BuildCommandSpecs(server),
                PlayerCandidateResolver = () => ConsolePromptCommonObjects.GetPlayerCandidates(server),
                ParameterExplainerResolver = TShockConsoleParameterExplainers.CreateSnapshot,
            };

            return new AnnotatedConsolePromptProvider(options).BuildContextSpec();
        }

        private static List<string> BuildCommandPrefixes()
        {
            List<string> prefixes = [];
            if (!string.IsNullOrWhiteSpace(TShock.Config?.GlobalSettings.CommandSpecifier)) {
                prefixes.Add(TShock.Config.GlobalSettings.CommandSpecifier);
            }
            if (!string.IsNullOrWhiteSpace(TShock.Config?.GlobalSettings.CommandSilentSpecifier)) {
                prefixes.Add(TShock.Config.GlobalSettings.CommandSilentSpecifier);
            }
            if (prefixes.Count == 0) {
                prefixes.Add("/");
            }

            return prefixes;
        }

        private static List<ConsoleCommandSpec> BuildCommandSpecs(ServerContext? server)
        {
            Dictionary<string, ConsoleCommandSpec> runtimeSpecs = new(StringComparer.OrdinalIgnoreCase);

            foreach (Command command in Commands.ChatCommands) {
                if (command.Names.Count == 0) {
                    continue;
                }

                if (!command.AllowServer) {
                    continue;
                }

                if (server is null && !command.AllowCoord) {
                    continue;
                }

                string primary = command.Names[0];
                runtimeSpecs[primary] = new ConsoleCommandSpec {
                    PrimaryName = primary,
                    Aliases = [.. command.Names
                        .Skip(1)
                        .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)],
                    HelpText = command.HelpText ?? string.Empty,
                };
            }

            Dictionary<string, ConsoleCommandSpec> builtins = new(StringComparer.OrdinalIgnoreCase);
            foreach (ConsoleCommandSpec builtin in BuiltinConsoleCommandSpecs.All) {
                builtins[builtin.PrimaryName] = builtin;
            }

            foreach (ConsoleCommandSpec external in ConsolePromptRegistry.GetRegisteredCommandSpecs()) {
                builtins[external.PrimaryName] = external;
            }

            List<ConsoleCommandSpec> merged = [];

            foreach (ConsoleCommandSpec runtimeSpec in runtimeSpecs.Values.OrderBy(static spec => spec.PrimaryName, StringComparer.OrdinalIgnoreCase)) {
                ConsoleCommandSpec? builtin = ResolveBuiltin(runtimeSpec, builtins);
                merged.Add(Merge(runtimeSpec, builtin));
            }

            foreach (ConsoleCommandSpec builtin in builtins.Values.OrderBy(static spec => spec.PrimaryName, StringComparer.OrdinalIgnoreCase)) {
                if (ResolveRuntime(builtin, runtimeSpecs) is not null) {
                    continue;
                }

                merged.Add(builtin);
            }

            return merged;
        }

        private static ConsoleCommandSpec Merge(ConsoleCommandSpec runtimeSpec, ConsoleCommandSpec? builtin)
        {
            if (builtin is null) {
                return runtimeSpec;
            }

            HashSet<string> aliasSet = new(StringComparer.OrdinalIgnoreCase);
            foreach (string alias in runtimeSpec.Aliases) {
                if (!string.IsNullOrWhiteSpace(alias) && !alias.Equals(runtimeSpec.PrimaryName, StringComparison.OrdinalIgnoreCase)) {
                    aliasSet.Add(alias.Trim());
                }
            }

            foreach (string alias in builtin.Aliases) {
                if (!string.IsNullOrWhiteSpace(alias) && !alias.Equals(runtimeSpec.PrimaryName, StringComparison.OrdinalIgnoreCase)) {
                    aliasSet.Add(alias.Trim());
                }
            }

            return runtimeSpec with {
                Aliases = [.. aliasSet.OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)],
                HelpText = string.IsNullOrWhiteSpace(runtimeSpec.HelpText) ? builtin.HelpText : runtimeSpec.HelpText,
                Patterns = builtin.Patterns,
            };
        }

        private static ConsoleCommandSpec? ResolveBuiltin(
            ConsoleCommandSpec runtimeSpec,
            Dictionary<string, ConsoleCommandSpec> builtins)
        {
            if (builtins.TryGetValue(runtimeSpec.PrimaryName, out ConsoleCommandSpec? direct)) {
                return direct;
            }

            foreach (string alias in runtimeSpec.Aliases) {
                if (builtins.TryGetValue(alias, out ConsoleCommandSpec? byAlias)) {
                    return byAlias;
                }
            }

            return null;
        }

        private static ConsoleCommandSpec? ResolveRuntime(
            ConsoleCommandSpec builtin,
            Dictionary<string, ConsoleCommandSpec> runtimeSpecs)
        {
            if (runtimeSpecs.TryGetValue(builtin.PrimaryName, out ConsoleCommandSpec? direct)) {
                return direct;
            }

            foreach (string alias in builtin.Aliases) {
                if (runtimeSpecs.TryGetValue(alias, out ConsoleCommandSpec? byAlias)) {
                    return byAlias;
                }
            }

            return null;
        }
    }
}
