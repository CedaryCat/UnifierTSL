using UnifierTSL;
using UnifierTSL.CLI;
using UnifierTSL.CLI.CommandHint;
using UnifierTSL.CLI.Sessions;
using UnifierTSL.Servers;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using TShockAPI.Localization;

namespace TShockAPI.CommandHint
{
    internal static class AnnotatedConsoleHintProvider
    {
        private static readonly Lazy<IReadOnlyList<string>> ItemCandidates = new(BuildItemCandidatesCore);

        public static void Install()
        {
            ConsoleCommandHintRegistry.RegisterCommandLineContextSpecFactory(BuildHintProvider);
        }

        private static ReadLineContextSpec BuildHintProvider(ServerContext? server)
        {
            AnnotatedCommandHintOptions options = new() {
                CommandPrefixResolver = BuildCommandPrefixes,
                CommandResolver = () => BuildRuntimeCommands(server),
                PlayerCandidateResolver = () => BuildPlayerCandidates(server),
                ServerCandidateResolver = BuildServerCandidates,
                ItemCandidateResolver = BuildItemCandidates,
                AnnotationResolver = () => BuiltinCommandAnnotationCatalog.All,
                AllowAnsiStatusEscapes = true,
            };

            return new AnnotatedCommandLineHintProvider(options).BuildContextSpec();
        }

        private static IReadOnlyList<string> BuildCommandPrefixes()
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

        private static IReadOnlyList<ConsoleCommandRuntimeDescriptor> BuildRuntimeCommands(ServerContext? server)
        {
            List<ConsoleCommandRuntimeDescriptor> commands = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

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
                if (!seen.Add(primary)) {
                    continue;
                }

                commands.Add(new ConsoleCommandRuntimeDescriptor {
                    PrimaryName = primary,
                    Aliases = [.. command.Names
                        .Skip(1)
                        .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)],
                    HelpText = command.HelpText ?? string.Empty,
                });
            }

            return [.. commands.OrderBy(static command => command.PrimaryName, StringComparer.OrdinalIgnoreCase)];
        }

        private static IReadOnlyList<string> BuildPlayerCandidates(ServerContext? server)
        {
            return [.. TShock.Players
                .Where(static player => player is not null && player.Active)
                .Where(player => server is null || player!.GetCurrentServer() == server)
                .Select(player => player!.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
        }

        private static IReadOnlyList<string> BuildServerCandidates()
        {
            return [.. UnifiedServerCoordinator.Servers
                .Where(static server => server.IsRunning)
                .Select(static server => server.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
        }

        private static IReadOnlyList<string> BuildItemCandidates()
        {
            return ItemCandidates.Value;
        }

        private static IReadOnlyList<string> BuildItemCandidatesCore()
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < ItemID.Count; i++) {
                AddItemCandidateName(names, Lang.GetItemNameValue(i));
                AddItemCandidateName(names, EnglishLanguage.GetItemNameById(i));
            }

            return [.. names.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
        }

        private static void AddItemCandidateName(HashSet<string> names, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) {
                return;
            }

            names.Add(value.Trim());
        }
    }
}
