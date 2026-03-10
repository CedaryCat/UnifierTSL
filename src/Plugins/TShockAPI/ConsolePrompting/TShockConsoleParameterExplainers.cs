using UnifierTSL.CLI;
using UnifierTSL.CLI.Prompting;
using Terraria;
using TShockAPI.DB;
using TShockAPI.Localization;

namespace TShockAPI.ConsolePrompting
{
    internal static class TShockConsoleParameterSemanticKeys
    {
        public const string PlayerRef = "tshock.player-ref";
        public const string ItemRef = "tshock.item-ref";
        public const string BuffRef = "tshock.buff-ref";
        public const string PrefixRef = "tshock.prefix-ref";
        public const string BanTicket = "tshock.ban-ticket";
        public const string NpcRef = "tshock.npc-ref";
    }

    internal static class TShockConsoleParameterExplainers
    {
        private static readonly IReadOnlyDictionary<string, IConsoleParameterValueExplainer> All =
            new Dictionary<string, IConsoleParameterValueExplainer>(StringComparer.Ordinal) {
                [TShockConsoleParameterSemanticKeys.PlayerRef] = new DelegateConsoleParameterValueExplainer(ExplainPlayer),
                [TShockConsoleParameterSemanticKeys.ItemRef] = new DelegateConsoleParameterValueExplainer(ExplainItem),
                [TShockConsoleParameterSemanticKeys.BuffRef] = new DelegateConsoleParameterValueExplainer(ExplainBuff),
                [TShockConsoleParameterSemanticKeys.PrefixRef] = new DelegateConsoleParameterValueExplainer(ExplainPrefix),
                [TShockConsoleParameterSemanticKeys.BanTicket] = new DelegateConsoleParameterValueExplainer(ExplainBanTicket),
                [TShockConsoleParameterSemanticKeys.NpcRef] = new DelegateConsoleParameterValueExplainer(ExplainNpc),
            };

        private static int defaultsRegistered;

        public static IReadOnlyDictionary<string, IConsoleParameterValueExplainer> CreateSnapshot() => All;

        public static void RegisterDefaults() {
            if (Interlocked.Exchange(ref defaultsRegistered, 1) != 0) {
                return;
            }

            foreach ((string semanticKey, IConsoleParameterValueExplainer explainer) in All) {
                _ = ConsolePromptRegistry.RegisterParameterExplainer(semanticKey, explainer);
            }
        }

        private static ConsoleParameterExplainResult ExplainPlayer(ConsoleParameterExplainContext context) {
            IEnumerable<TSPlayer> filteredPlayers = TSPlayer.FindByNameOrID(context.RawToken)
                .Where(static player => player is not null)
                .GroupBy(static player => player.Index)
                .Select(static group => group.First());

            if (context.Server is not null) {
                filteredPlayers = filteredPlayers.Where(player => player.GetCurrentServer() == context.Server);
            }

            List<TSPlayer> players = [.. filteredPlayers];
            if (players.Count == 0) {
                return Invalid();
            }

            if (players.Count == 1) {
                return Resolved(players[0].Name);
            }

            return Ambiguous(players.Select(static player => player.Name));
        }

        private static ConsoleParameterExplainResult ExplainItem(ConsoleParameterExplainContext context) {
            List<Item> items = [.. Utils.GetItemByIdOrName(context.RawToken)
                .Where(static item => item is not null)
                .GroupBy(static item => item.type)
                .Select(static group => group.First())];

            if (items.Count == 0) {
                return Invalid();
            }

            if (items.Count == 1) {
                return Resolved(items[0].Name);
            }

            return Ambiguous(items.Select(static item => item.Name));
        }

        private static ConsoleParameterExplainResult ExplainBuff(ConsoleParameterExplainContext context) {
            if (int.TryParse(context.RawToken, out int buffId)) {
                string? buffName = Utils.GetBuffName(buffId);
                return string.IsNullOrWhiteSpace(buffName) ? Invalid() : Resolved(buffName);
            }

            List<int> buffIds = [.. Utils.GetBuffByName(context.RawToken).Distinct()];
            if (buffIds.Count == 0) {
                return Invalid();
            }

            if (buffIds.Count == 1) {
                string? buffName = Utils.GetBuffName(buffIds[0]);
                return string.IsNullOrWhiteSpace(buffName) ? Invalid() : Resolved(buffName);
            }

            return Ambiguous(buffIds
                .Select(Utils.GetBuffName)
                .Where(static buffName => !string.IsNullOrWhiteSpace(buffName))!);
        }

        private static ConsoleParameterExplainResult ExplainPrefix(ConsoleParameterExplainContext context) {
            List<int> prefixIds = [.. Utils.GetPrefixByIdOrName(context.RawToken).Distinct()];
            if (prefixIds.Count == 0) {
                return Invalid();
            }

            List<string> prefixNames = [.. prefixIds
                .Select(Utils.GetPrefixById)
                .Where(static prefixName => !string.IsNullOrWhiteSpace(prefixName))
                .Distinct(StringComparer.OrdinalIgnoreCase)];

            if (prefixNames.Count == 0) {
                return Invalid();
            }

            if (prefixNames.Count == 1) {
                return Resolved(prefixNames[0]);
            }

            return Ambiguous(prefixNames);
        }

        private static ConsoleParameterExplainResult ExplainBanTicket(ConsoleParameterExplainContext context) {
            if (!int.TryParse(context.RawToken, out int ticketNumber)) {
                return Invalid();
            }

            Ban? ban = TShock.Bans.GetBanById(ticketNumber);
            return ban is null
                ? Invalid()
                : Resolved($"#{ban.TicketNumber} {ban.Identifier}");
        }

        private static ConsoleParameterExplainResult ExplainNpc(ConsoleParameterExplainContext context) {
            return context.ActiveCommand.PrimaryName.Equals("tpnpc", StringComparison.OrdinalIgnoreCase)
                ? ExplainLiveNpc(context)
                : ExplainNpcCatalog(context);
        }

        private static ConsoleParameterExplainResult ExplainNpcCatalog(ConsoleParameterExplainContext context) {
            List<NPC> npcs = [.. Utils.GetNPCByIdOrName(context.RawToken)
                .Where(static npc => npc is not null)
                .GroupBy(static npc => npc.netID)
                .Select(static group => group.First())];

            if (npcs.Count == 0) {
                return Invalid();
            }

            if (npcs.Count == 1) {
                return Resolved(npcs[0].FullName);
            }

            return Ambiguous(npcs.Select(static npc => npc.FullName));
        }

        private static ConsoleParameterExplainResult ExplainLiveNpc(ConsoleParameterExplainContext context) {
            if (context.Server is null) {
                return ConsoleParameterExplainResult.None;
            }

            string search = context.RawToken;
            List<NPC> matches = [];

            foreach (NPC npc in context.Server.Main.npc.Where(static npc => npc.active)) {
                string? englishName = EnglishLanguage.GetNpcNameById(npc.netID);
                if (string.Equals(npc.FullName, search, StringComparison.InvariantCultureIgnoreCase)
                    || string.Equals(englishName, search, StringComparison.InvariantCultureIgnoreCase)) {
                    matches = [npc];
                    break;
                }

                if (npc.FullName.StartsWith(search, StringComparison.InvariantCultureIgnoreCase)
                    || englishName?.StartsWith(search, StringComparison.InvariantCultureIgnoreCase) == true) {
                    matches.Add(npc);
                }
            }

            if (matches.Count == 0) {
                return Invalid();
            }

            if (matches.Count == 1) {
                return Resolved(matches[0].FullName);
            }

            return Ambiguous(matches.Select(static npc => $"{npc.FullName}({npc.whoAmI})"));
        }

        private static ConsoleParameterExplainResult Resolved(string? displayText) {
            return string.IsNullOrWhiteSpace(displayText)
                ? Invalid()
                : new ConsoleParameterExplainResult(ConsoleParameterExplainState.Resolved, displayText.Trim());
        }

        private static ConsoleParameterExplainResult Invalid()
            => new(ConsoleParameterExplainState.Invalid, "invalid");

        private static ConsoleParameterExplainResult Ambiguous(IEnumerable<string> displayValues) {
            List<string> candidates = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            foreach (string? displayValue in displayValues) {
                if (string.IsNullOrWhiteSpace(displayValue)) {
                    continue;
                }

                string normalized = displayValue.Trim();
                if (!seen.Add(normalized)) {
                    continue;
                }

                candidates.Add(normalized);
            }

            if (candidates.Count == 0) {
                return Invalid();
            }

            if (candidates.Count == 1) {
                return Resolved(candidates[0]);
            }

            string preview = string.Join(", ", candidates.Take(3));
            if (candidates.Count > 3) {
                preview += ", ...";
            }

            return new ConsoleParameterExplainResult(
                ConsoleParameterExplainState.Ambiguous,
                "ambiguous: " + preview);
        }

        private sealed class DelegateConsoleParameterValueExplainer(
            Func<ConsoleParameterExplainContext, ConsoleParameterExplainResult> handler) : IConsoleParameterValueExplainer
        {
            public bool TryExplain(ConsoleParameterExplainContext context, out ConsoleParameterExplainResult result) {
                result = handler(context);
                return result.State != ConsoleParameterExplainState.None;
            }
        }
    }
}
