using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Terraria;
using Terraria.ID;
using TShockAPI.Commanding;
using TShockAPI.DB;
using TShockAPI.Localization;
using UnifierTSL;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Bindings;
using UnifierTSL.Commanding.Prompting;
using UnifierTSL.Servers;

namespace TShockAPI.ConsolePrompting
{
    /*
        This file is the TShock-specific prompt/runtime bridge.

        The prompt layer in UTSL core intentionally stays generic, so any TShock binder behavior
        that is richer than plain prefix matching must be mirrored here explicitly. The critical
        examples are UserAccountRef, TSPlayerRef, and TSItemRef.

        UserAccountRef delegates to UserManager.GetUserAccountsByName, which uses SQL LIKE prefix
        semantics. "_" is not just a literal underscore in the search token, and prompt candidate
        filtering must respect that or ghost text falls behind execution truth.

        TSPlayerRef delegates to TSPlayer.FindByNameOrID, which reserves the "tsn:" and "tsi:"
        disambiguation prefixes. Those prefixes are not generic CLI syntax, and "tsi:" still keeps
        a legacy fallback to name-prefix matching when no active exact index match exists. Mirror
        that here by overriding the shared PlayerRef semantic key rather than forking a TShock-only
        player semantic key.

        TSItemRef now does the same for item slots that need legacy TShock command semantics while
        still participating in the shared item prompt surface. Exact-vs-soft lookup now rides on
        the shared typed route fields, and the remaining slot metadata only declares whether the
        binder is using core ItemRef lookup or legacy Utils.GetItemByIdOrName lookup.

        Edit this file carefully. If a TShock binder or lookup rule changes, update the prompt
        matcher and its comments in the same change. Otherwise future refactors can "simplify"
        the prompt side back to StartsWith and quietly reintroduce regressions.

        Preserve existing local comments unless the underlying mechanism changes. If you replace
        a mechanism, add the replacement rationale and the expected failure mode when removed.
    */

    internal sealed class TSCommandPromptParamKeys
    {
        private TSCommandPromptParamKeys() { }

        public static SemanticKey BanTicket { get; } = new("tshock.ban-ticket", "ban ticket");
        public static SemanticKey NpcRef { get; } = new("tshock.npc-ref", "npc");
        public static SemanticKey WarpRef { get; } = new("tshock.warp-ref", "warp");
        public static SemanticKey RegionRef { get; } = new("tshock.region-ref", "region");
        public static SemanticKey GroupRef { get; } = new("tshock.group-ref", "group");
        public static SemanticKey UserAccountRef { get; } = new("tshock.user-account-ref", "user account");
        public static SemanticKey PageRef { get; } = new("tshock.page-ref", "page");
        public static SemanticKey CommandRef { get; } = new("tshock.command-ref", "command");
    }

    internal static class TSPromptSlotMetadata
    {
        private const string ItemLookupModeKey = "tshock.item-lookup-mode";
        private const string PageRefSourceTypeKey = "tshock.page-ref-source-type";
        private const string PageRefUpperBoundBehaviorKey = "tshock.page-ref-upper-bound-behavior";
        private const string CommandRefRecursiveKey = "tshock.command-ref-recursive";
        private const string CommandRefAcceptOptionalPrefixKey = "tshock.command-ref-accept-optional-prefix";
        private const string CommandRefInsertPrefixKey = "tshock.command-ref-insert-prefix";

        public static ImmutableArray<PromptSlotMetadataEntry> CreateItemLookupMode(TSItemLookupMode mode) {
            return [new(ItemLookupModeKey, mode.ToString())];
        }

        public static ImmutableArray<PromptSlotMetadataEntry> CreatePageRef(PageRefAttribute attribute) {
            return [
                new(PageRefSourceTypeKey, attribute.SourceType.AssemblyQualifiedName ?? attribute.SourceType.FullName ?? attribute.SourceType.Name),
                new(PageRefUpperBoundBehaviorKey, attribute.UpperBoundBehavior.ToString()),
            ];
        }

        public static bool TryGetPageRefSourceType(PromptSlotSegmentSpec slot, out Type sourceType) {
            sourceType = null!;
            if (!slot.TryGetMetadataValue(PageRefSourceTypeKey, out var rawTypeName)) {
                return false;
            }

            sourceType = Type.GetType(rawTypeName, throwOnError: false)!;
            return sourceType is not null;
        }

        public static PageRefUpperBoundBehavior GetPageRefUpperBoundBehavior(
            PromptSlotSegmentSpec slot,
            PageRefUpperBoundBehavior fallback = PageRefUpperBoundBehavior.AllowOverflow) {
            return slot.TryGetMetadataValue(PageRefUpperBoundBehaviorKey, out var rawBehavior)
                && Enum.TryParse(rawBehavior, ignoreCase: true, out PageRefUpperBoundBehavior behavior)
                    ? behavior
                    : fallback;
        }

        public static ImmutableArray<PromptSlotMetadataEntry> CreateCommandRef(CommandRefAttribute attribute) {
            return [
                new(CommandRefRecursiveKey, attribute.Recursive.ToString()),
                new(CommandRefAcceptOptionalPrefixKey, attribute.AcceptOptionalPrefix.ToString()),
                new(CommandRefInsertPrefixKey, attribute.InsertPrefix.ToString()),
            ];
        }

        public static TSItemLookupMode GetItemLookupMode(PromptSlotSegmentSpec slot, TSItemLookupMode fallback = TSItemLookupMode.PromptDefault) {
            if (slot.TryGetMetadataValue(ItemLookupModeKey, out var rawMode)
                && Enum.TryParse(rawMode, ignoreCase: true, out TSItemLookupMode mode)) {
                return mode;
            }

            return fallback;
        }

        public static bool IsRecursiveCommandRef(PromptSlotSegmentSpec slot, bool fallback = false) {
            return slot.TryGetMetadataValue(CommandRefRecursiveKey, out var rawValue)
                && bool.TryParse(rawValue, out var value)
                    ? value
                    : fallback;
        }

        public static bool AcceptsOptionalCommandRefPrefix(PromptSlotSegmentSpec slot, bool fallback = true) {
            return slot.TryGetMetadataValue(CommandRefAcceptOptionalPrefixKey, out var rawValue)
                && bool.TryParse(rawValue, out var value)
                    ? value
                    : fallback;
        }

        public static bool InsertCommandRefPrefix(PromptSlotSegmentSpec slot, bool fallback = false) {
            return slot.TryGetMetadataValue(CommandRefInsertPrefixKey, out var rawValue)
                && bool.TryParse(rawValue, out var value)
                    ? value
                    : fallback;
        }
    }

    internal static class TSConsoleParameterExplainers
    {
        private const int LegacyItemExplainCacheLimit = 2048;
        private static readonly Lock LegacyItemExplainCacheLock = new();
        private static readonly Dictionary<string, PromptParamExplainResult> LegacyItemExplainCache =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly IReadOnlyDictionary<SemanticKey, IParamValueExplainer> All =
            new Dictionary<SemanticKey, IParamValueExplainer> {
                [CommandPromptParamKeys.PlayerRef] = new DelegateParamExplainer(
                    ExplainTSPlayer,
                    GetTSPlayerRevision),
                [CommandPromptParamKeys.ItemRef] = new DelegateParamExplainer(
                    ExplainItem,
                    static _ => 0),
                [TSCommandPromptParamKeys.BanTicket] = new DelegateParamExplainer(
                    ExplainBanTicket,
                    GetBanTicketRevision),
                [TSCommandPromptParamKeys.NpcRef] = new DelegateParamExplainer(
                    ExplainNpc,
                    GetNpcRevision),
                [TSCommandPromptParamKeys.WarpRef] = new DelegateParamExplainer(
                    ExplainWarp,
                    GetWarpRevision),
                [TSCommandPromptParamKeys.RegionRef] = new DelegateParamExplainer(
                    ExplainRegion,
                    GetRegionRevision),
                [TSCommandPromptParamKeys.GroupRef] = new DelegateParamExplainer(
                    ExplainGroup,
                    GetGroupRevision),
                [TSCommandPromptParamKeys.UserAccountRef] = new DelegateParamExplainer(
                    ExplainUserAccount,
                    GetUserAccountRevision),
                [TSCommandPromptParamKeys.PageRef] = new DelegateParamExplainer(
                    TSPageRefResolver.Explain,
                    GetPageRefRevision),
                [TSCommandPromptParamKeys.CommandRef] = new DelegateParamExplainer(
                    TSCommandRefResolver.Explain,
                    static _ => 0),
            };

        private static readonly IReadOnlyDictionary<SemanticKey, IParamValueCandidateProvider> AllCandidateProviders =
            new Dictionary<SemanticKey, IParamValueCandidateProvider> {
                [CommandPromptParamKeys.PlayerRef] = new DelegateParamCandidateProvider(
                    GetTSPlayerCandidates,
                    GetTSPlayerCandidateRevision),
                [TSCommandPromptParamKeys.NpcRef] = new DelegateParamCandidateProvider(
                    GetNpcCandidates,
                    GetNpcCandidateRevision),
                [TSCommandPromptParamKeys.WarpRef] = new DelegateParamCandidateProvider(
                    GetWarpCandidates,
                    GetWarpCandidateRevision,
                    ResolveWarpCandidateMatchWeight),
                [TSCommandPromptParamKeys.RegionRef] = new DelegateParamCandidateProvider(
                    GetRegionCandidates,
                    GetRegionCandidateRevision),
                [TSCommandPromptParamKeys.GroupRef] = new DelegateParamCandidateProvider(
                    GetGroupCandidates,
                    GetGroupCandidateRevision,
                    ResolveGroupCandidateMatchWeight),
                [TSCommandPromptParamKeys.UserAccountRef] = new DelegateParamCandidateProvider(
                    GetUserAccountCandidates,
                    GetUserAccountCandidateRevision,
                    ResolveUserAccountCandidateMatchWeight),
                [TSCommandPromptParamKeys.PageRef] = new DelegateParamCandidateProvider(
                    TSPageRefResolver.GetCandidates,
                    GetPageRefRevision,
                    TSPageRefResolver.ResolveCandidateMatchWeight),
                [TSCommandPromptParamKeys.CommandRef] = new CommandRefPromptProvider(),
            };

        public static IDisposable RegisterDefaults() {
            List<IDisposable> registrations = [];
            foreach ((var semanticKey, var explainer) in All) {
                // PlayerRef is a shared core semantic key, so TShock must override it rather than
                // minting a parallel semantic namespace that prompt consumers would need to learn.
                registrations.Add((semanticKey.Equals(CommandPromptParamKeys.PlayerRef) || semanticKey.Equals(CommandPromptParamKeys.ItemRef))
                    ? PromptRegistry.RegisterParameterExplainerOverride(semanticKey, explainer)
                    : PromptRegistry.RegisterParameterExplainer(semanticKey, explainer));
            }

            foreach ((var semanticKey, var provider) in AllCandidateProviders) {
                registrations.Add(semanticKey.Equals(CommandPromptParamKeys.PlayerRef)
                    ? PromptRegistry.RegisterParameterCandidateProviderOverride(semanticKey, provider)
                    : PromptRegistry.RegisterParameterCandidateProvider(semanticKey, provider));
            }

            return CompositeDisposable.Create(registrations);
        }

        private static PromptParamExplainResult ExplainBanTicket(PromptParamExplainContext context) {
            if (!int.TryParse(context.RawToken, out var ticketNumber)) {
                return Invalid();
            }

            var ban = TShock.Bans.GetBanById(ticketNumber);
            return ban is null
                ? Invalid()
                : Resolved($"#{ban.TicketNumber} {ban.Identifier}");
        }

        private static PromptParamExplainResult ExplainNpc(PromptParamExplainContext context) {
            return IsLiveNpcCommand(context.ActiveAlternative)
                ? ExplainLiveNpc(context)
                : ExplainNpcCatalog(context);
        }

        private static PromptParamExplainResult ExplainWarp(PromptParamExplainContext context) {
            var rawSearch = context.RawToken ?? string.Empty;
            var search = rawSearch.Trim();
            if (search.Length == 0) {
                return Invalid();
            }

            if (UsesExactLookup(context.ActiveSlot)) {
                var exactWarp = TShock.Warps.Warps.FirstOrDefault(warp =>
                    warp.Name.Equals(rawSearch, StringComparison.OrdinalIgnoreCase));
                return exactWarp is null
                    ? Invalid()
                    : Resolved(exactWarp.Name);
            }

            List<Warp> exactMatches = [.. TShock.Warps.Warps
                .Where(warp => warp.Name.Equals(search, StringComparison.OrdinalIgnoreCase))];
            if (exactMatches.Count == 1) {
                return Resolved(exactMatches[0].Name);
            }

            var matches = exactMatches.Count > 1
                ? exactMatches.Select(static warp => warp.Name)
                : TShock.Warps.Warps
                    .Where(warp => warp.Name.StartsWith(search, StringComparison.OrdinalIgnoreCase))
                    .Select(static warp => warp.Name);
            return BuildExactOrAmbiguous(matches);
        }

        private static PromptParamExplainResult ExplainItem(PromptParamExplainContext context) {
            return TSPromptSlotMetadata.GetItemLookupMode(context.ActiveSlot) == TSItemLookupMode.LegacyCommand
                ? ExplainLegacyItem(context)
                : ExplainPromptItem(context);
        }

        private static PromptParamExplainResult ExplainPromptItem(PromptParamExplainContext context) {
            var rawSearch = context.RawToken ?? string.Empty;
            var search = rawSearch.Trim();
            if (search.Length == 0) {
                return Invalid();
            }

            List<string> continuationMatches = HasSemanticContinuationSearch(rawSearch)
                ? [.. CommandPromptCommonObjects.GetItemCandidates()
                    .Where(candidate => candidate.StartsWith(rawSearch, StringComparison.OrdinalIgnoreCase))]
                : [];
            if (continuationMatches.Count == 1) {
                return Resolved(continuationMatches[0]);
            }

            if (continuationMatches.Count > 1) {
                return Ambiguous(continuationMatches);
            }

            if (int.TryParse(search, out var itemId)) {
                var displayName = CommandPromptCommonObjects.GetItemDisplayName(itemId);
                if (!string.IsNullOrWhiteSpace(displayName)) {
                    return Resolved(displayName);
                }
            }

            var matches = CommandPromptCommonObjects.ResolveItemIds(search);
            if (matches.Count == 0) {
                return Invalid();
            }

            if (matches.Count == 1) {
                return Resolved(CommandPromptCommonObjects.GetItemDisplayName(matches[0]));
            }

            return Ambiguous(matches
                .Select(static id => CommandPromptCommonObjects.GetItemDisplayName(id) ?? id.ToString()));
        }

        private static PromptParamExplainResult ExplainLegacyItem(PromptParamExplainContext context) {
            var rawSearch = context.RawToken ?? string.Empty;
            if (rawSearch.Trim().Length == 0) {
                return Invalid();
            }

            if (TryGetLegacyItemExplainResult(rawSearch, out var cached)) {
                return cached;
            }

            var result = ResolveLegacyItemExplainResult(rawSearch);
            CacheLegacyItemExplainResult(rawSearch, result);
            return result;
        }

        private static PromptParamExplainResult ResolveLegacyItemExplainResult(string rawSearch) {
            if (int.TryParse(rawSearch, out _)) {
                return ResolveLegacyItemExplainResultViaUtils(rawSearch);
            }

            if (Utils.GetItemFromTag(rawSearch) is Item tagItem) {
                return Resolved(tagItem.Name);
            }

            var exactMatches = CommandPromptCommonObjects.GetItemIdsByExactName(rawSearch);
            if (exactMatches.Count > 0) {
                return Resolved(ResolveLegacyItemDisplayName(exactMatches[0]));
            }

            var prefixMatches = CommandPromptCommonObjects.ResolveItemIdsByPrefix(rawSearch);
            if (prefixMatches.Count == 1) {
                return Resolved(ResolveLegacyItemDisplayName(prefixMatches[0]));
            }

            if (prefixMatches.Count > 1) {
                // Multiple prefix hits are already enough to make the legacy command ambiguous.
                // Prompt routing only needs that ambiguity state plus a representative preview,
                // so scanning the much larger contains-set here just burns CPU while editing.
                return Ambiguous(prefixMatches.Select(FormatLegacyItemAmbiguousDisplay));
            }

            var containsMatches = CommandPromptCommonObjects.ResolveItemIdsByContains(rawSearch);
            return containsMatches.Count switch {
                0 => Invalid(),
                1 => Resolved(ResolveLegacyItemDisplayName(containsMatches[0])),
                _ => Ambiguous(containsMatches.Select(FormatLegacyItemAmbiguousDisplay)),
            };
        }

        private static PromptParamExplainResult ResolveLegacyItemExplainResultViaUtils(string rawSearch) {
            var matches = Utils.GetItemByIdOrName(rawSearch);
            return matches.Count switch {
                0 => Invalid(),
                1 => Resolved(matches[0].Name),
                _ => Ambiguous(matches.Select(static item => $"{item.Name}({item.type})")),
            };
        }

        private static string ResolveLegacyItemDisplayName(int itemId) {
            return CommandPromptCommonObjects.GetItemDisplayName(itemId) ?? Utils.GetItemById(itemId).Name;
        }

        private static string FormatLegacyItemAmbiguousDisplay(int itemId) {
            return $"{ResolveLegacyItemDisplayName(itemId)}({itemId})";
        }

        private static bool TryGetLegacyItemExplainResult(string rawSearch, out PromptParamExplainResult result) {
            lock (LegacyItemExplainCacheLock) {
                return LegacyItemExplainCache.TryGetValue(rawSearch, out result);
            }
        }

        private static void CacheLegacyItemExplainResult(string rawSearch, PromptParamExplainResult result) {
            lock (LegacyItemExplainCacheLock) {
                if (LegacyItemExplainCache.Count >= LegacyItemExplainCacheLimit) {
                    LegacyItemExplainCache.Clear();
                }

                LegacyItemExplainCache[rawSearch] = result;
            }
        }

        private static PromptParamExplainResult ExplainRegion(PromptParamExplainContext context) {
            if (context.Server is null) {
                return PromptParamExplainResult.None;
            }

            var rawSearch = context.RawToken ?? string.Empty;
            var search = rawSearch.Trim();
            if (search.Length == 0) {
                return Invalid();
            }

            var worldId = context.Server.Main.worldID.ToString();
            if (UsesExactLookup(context.ActiveSlot)) {
                var exactRegion = TShock.Regions.Regions.FirstOrDefault(region =>
                    region.WorldID == worldId
                    && region.Name.Equals(rawSearch, StringComparison.OrdinalIgnoreCase));
                return exactRegion is null
                    ? Invalid()
                    : Resolved(exactRegion.Name);
            }

            List<Region> exactMatches = [.. TShock.Regions.Regions
                .Where(region => region.WorldID == worldId
                    && region.Name.Equals(search, StringComparison.OrdinalIgnoreCase))];
            if (exactMatches.Count == 1) {
                return Resolved(exactMatches[0].Name);
            }

            var matches = exactMatches.Count > 1
                ? exactMatches.Select(static region => region.Name)
                : TShock.Regions.Regions
                    .Where(region => region.WorldID == worldId
                        && region.Name.StartsWith(search, StringComparison.OrdinalIgnoreCase))
                    .Select(static region => region.Name);
            return BuildExactOrAmbiguous(matches);
        }

        private static PromptParamExplainResult ExplainGroup(PromptParamExplainContext context) {
            var rawSearch = context.RawToken ?? string.Empty;
            var search = rawSearch.Trim();
            if (search.Length == 0) {
                return Invalid();
            }

            if (UsesExactLookup(context.ActiveSlot)) {
                var exact = TShock.Groups.GetGroupByName(rawSearch);
                return exact is null
                    ? Invalid()
                    : Resolved(exact.Name);
            }

            List<string> exactMatches = [.. TShock.Groups
                .Select(static group => group.Name)
                .Where(groupName => groupName.Equals(search, StringComparison.OrdinalIgnoreCase))];
            if (exactMatches.Count == 1) {
                return Resolved(exactMatches[0]);
            }

            var matches = exactMatches.Count > 1
                ? exactMatches
                : TShock.Groups
                    .Select(static group => group.Name)
                    .Where(groupName => groupName.StartsWith(search, StringComparison.OrdinalIgnoreCase));
            return BuildExactOrAmbiguous(matches);
        }

        private static PromptParamExplainResult ExplainUserAccount(PromptParamExplainContext context) {
            var rawSearch = context.RawToken ?? string.Empty;
            var search = rawSearch.Trim();
            if (search.Length == 0) {
                return Invalid();
            }

            if (UsesExactLookup(context.ActiveSlot)) {
                var exactAccount = TShock.UserAccounts.GetUserAccountByName(rawSearch);
                var exactResult = exactAccount is null
                    ? Invalid()
                    : Resolved(exactAccount.Name);
                return exactResult;
            }

            List<UserAccount> exactMatches = [.. TShock.UserAccounts.GetUserAccounts()
                .Where(account => account.Name.Equals(search, StringComparison.OrdinalIgnoreCase))];
            if (exactMatches.Count == 1) {
                var exactMatchResult = Resolved(exactMatches[0].Name);
                return exactMatchResult;
            }

            var matches = exactMatches.Count > 1
                ? exactMatches.Select(static account => account.Name)
                : TShock.UserAccounts.GetUserAccountsByName(search)
                    .Select(static account => account.Name);
            var result = BuildExactOrAmbiguous(matches);
            return result;
        }

        private static PromptParamExplainResult ExplainTSPlayer(PromptParamExplainContext context) {
            var rawSearch = context.RawToken ?? string.Empty;
            var normalizedSearch = rawSearch.Trim();
            if (normalizedSearch.Length == 0) {
                return Invalid();
            }

            List<TSPlayerPromptCandidate> candidates = [.. EnumerateTSPlayerPromptCandidates(context.Server)];
            if (candidates.Count == 0) {
                return Invalid();
            }

            var syntax = ResolveTSPlayerSearchSyntax(normalizedSearch, out _);
            List<TSPlayerPromptCandidate> continuationMatches = [.. candidates
                .Where(candidate => EnumerateTSPlayerCandidateTexts(candidate, syntax)
                    .Any(text => text.StartsWith(rawSearch, StringComparison.OrdinalIgnoreCase)))];
            if (continuationMatches.Count == 1) {
                return Resolved(FormatResolvedTSPlayer(continuationMatches[0], syntax));
            }

            if (continuationMatches.Count > 1) {
                return Ambiguous(continuationMatches.Select(FormatAmbiguousTSPlayer));
            }

            var matches = FindMatchingTSPlayers(candidates, normalizedSearch);
            return matches.Count switch {
                0 => Invalid(),
                1 => Resolved(FormatResolvedTSPlayer(matches[0], syntax)),
                _ => Ambiguous(matches.Select(FormatAmbiguousTSPlayer)),
            };
        }

        private static long GetBanTicketRevision(PromptParamExplainContext context) {
            HashCode hash = new();
            foreach (var ban in TShock.Bans.Bans.Values.OrderBy(static ban => ban.TicketNumber)) {
                hash.Add(ban.TicketNumber);
                hash.Add(ban.Identifier ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            }

            return hash.ToHashCode();
        }

        private static long GetTSPlayerRevision(PromptParamExplainContext context) {
            return ComputeTSPlayerPromptRevision(context.Server);
        }

        private static long GetNpcRevision(PromptParamExplainContext context) {
            if (!IsLiveNpcCommand(context.ActiveAlternative)
                || context.Server is null) {
                return 0;
            }

            HashCode hash = new();
            foreach (var npc in context.Server.Main.npc.Where(static npc => npc.active)) {
                hash.Add(npc.whoAmI);
                hash.Add(npc.netID);
                hash.Add(npc.FullName ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            }

            return hash.ToHashCode();
        }

        private static long GetWarpRevision(PromptParamExplainContext context) {
            HashCode hash = new();
            foreach (var warp in TShock.Warps.Warps.OrderBy(static warp => warp.Name, StringComparer.OrdinalIgnoreCase)) {
                hash.Add(warp.Name, StringComparer.OrdinalIgnoreCase);
                hash.Add(warp.WorldID ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                hash.Add(warp.IsPrivate);
            }

            return hash.ToHashCode();
        }

        private static long GetRegionRevision(PromptParamExplainContext context) {
            if (context.Server is null) {
                return 0;
            }

            var worldId = context.Server.Main.worldID.ToString();
            HashCode hash = new();
            foreach (var region in TShock.Regions.Regions
                .Where(region => region.WorldID == worldId)
                .OrderBy(static region => region.Name, StringComparer.OrdinalIgnoreCase)) {
                hash.Add(region.Name, StringComparer.OrdinalIgnoreCase);
                hash.Add(region.WorldID ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                hash.Add(region.Z);
            }

            return hash.ToHashCode();
        }

        private static long GetGroupRevision(PromptParamExplainContext context) {
            HashCode hash = new();
            foreach (var group in TShock.Groups.OrderBy(static group => group.Name, StringComparer.OrdinalIgnoreCase)) {
                hash.Add(group.Name, StringComparer.OrdinalIgnoreCase);
            }

            return hash.ToHashCode();
        }

        private static long GetUserAccountRevision(PromptParamExplainContext context) {
            HashCode hash = new();
            foreach (var account in TShock.UserAccounts.GetUserAccounts().OrderBy(static account => account.Name, StringComparer.OrdinalIgnoreCase)) {
                hash.Add(account.ID);
                hash.Add(account.Name, StringComparer.OrdinalIgnoreCase);
            }

            return hash.ToHashCode();
        }

        private static long GetPageRefRevision(PromptParamExplainContext context) {
            return TSPageRefResolver.GetPageCount(context.ActiveSlot, context.Server) ?? 0;
        }

        private static IReadOnlyList<string> GetNpcCandidates(PromptParamCandidateContext context) {
            if (IsLiveNpcCommand(context.ActiveAlternative)) {
                return context.Server is null
                    ? []
                    : [.. context.Server.Main.npc
                        .Where(static npc => npc.active)
                        .Select(static npc => npc.FullName)
                        .Where(static name => !string.IsNullOrWhiteSpace(name))
                        .Select(static name => name.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
            }

            return [.. Enumerable.Range(1, NPCID.Count - 1)
                    .Select(EnglishLanguage.GetNpcNameById)
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Select(static name => name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
        }

        private static IReadOnlyList<string> GetWarpCandidates(PromptParamCandidateContext context) {
            return [.. TShock.Warps.Warps
                .Select(static warp => warp.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
        }

        private static int? ResolveWarpCandidateMatchWeight(
            PromptParamCandidateContext context,
            string candidate,
            int baseWeight) {
            var rawSearch = context.RawToken ?? string.Empty;
            if (rawSearch.Length == 0) {
                return baseWeight;
            }

            if (UsesExactLookup(context.ActiveSlot)) {
                var exactWarp = TShock.Warps.Warps.FirstOrDefault(warp =>
                    warp.Name.Equals(rawSearch, StringComparison.OrdinalIgnoreCase));
                return exactWarp is not null
                    && exactWarp.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                        ? baseWeight + 1000
                        : null;
            }

            return candidate.StartsWith(rawSearch, StringComparison.OrdinalIgnoreCase)
                ? ResolvePrefixMatchWeight(candidate, rawSearch, baseWeight)
                : null;
        }

        private static IReadOnlyList<string> GetRegionCandidates(PromptParamCandidateContext context) {
            if (context.Server is null) {
                return [];
            }

            var worldId = context.Server.Main.worldID.ToString();
            return [.. TShock.Regions.Regions
                .Where(region => region.WorldID == worldId)
                .Select(static region => region.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
        }

        private static IReadOnlyList<string> GetGroupCandidates(PromptParamCandidateContext context) {
            return [.. TShock.Groups
                .Select(static group => group.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
        }

        private static IReadOnlyList<string> GetTSPlayerCandidates(PromptParamCandidateContext context) {
            List<TSPlayerPromptCandidate> candidates = [.. EnumerateTSPlayerPromptCandidates(context.Server)];
            if (candidates.Count == 0) {
                return [];
            }

            var syntax = ResolveTSPlayerSearchSyntax(context.RawToken?.Trim() ?? string.Empty, out _);
            HashSet<string> results = new(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates) {
                foreach (var candidateText in EnumerateTSPlayerCandidateTexts(candidate, syntax)) {
                    if (!string.IsNullOrWhiteSpace(candidateText)) {
                        results.Add(candidateText);
                    }
                }
            }

            return [.. results.OrderBy(static candidate => candidate, StringComparer.OrdinalIgnoreCase)];
        }

        private static IReadOnlyList<string> GetUserAccountCandidates(PromptParamCandidateContext context) {
            List<string> canonicalNames = [.. TShock.UserAccounts.GetUserAccounts()
                .Select(static account => account.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
            if (UsesExactLookup(context.ActiveSlot)
                || !(context.RawToken?.Contains('_') ?? false)) {
                return canonicalNames;
            }

            HashSet<string> canonicalLookup = new(canonicalNames, StringComparer.OrdinalIgnoreCase);
            List<string> results = [.. canonicalNames];
            foreach (var canonicalName in canonicalNames) {
                var wildcardAlias = BuildUserAccountWhitespaceWildcardAlias(canonicalName);
                // Only synthesize underscore aliases when they do not collide with a real account
                // name. Otherwise ghost acceptance could silently change which exact account the
                // runtime binder resolves.
                if (string.Equals(wildcardAlias, canonicalName, StringComparison.OrdinalIgnoreCase)
                    || canonicalLookup.Contains(wildcardAlias)) {
                    continue;
                }

                results.Add(wildcardAlias);
            }
            return results;
        }

        private static long GetNpcCandidateRevision(PromptParamCandidateContext context) {
            if (!IsLiveNpcCommand(context.ActiveAlternative)
                || context.Server is null) {
                return 0;
            }

            HashCode hash = new();
            foreach (var npc in context.Server.Main.npc.Where(static npc => npc.active)) {
                hash.Add(npc.whoAmI);
                hash.Add(npc.netID);
                hash.Add(npc.FullName ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            }

            return hash.ToHashCode();
        }

        private static long GetWarpCandidateRevision(PromptParamCandidateContext context) {
            HashCode hash = new();
            foreach (var warp in TShock.Warps.Warps.OrderBy(static warp => warp.Name, StringComparer.OrdinalIgnoreCase)) {
                hash.Add(warp.Name, StringComparer.OrdinalIgnoreCase);
                hash.Add(warp.WorldID ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                hash.Add(warp.IsPrivate);
            }

            return hash.ToHashCode();
        }

        private static long GetRegionCandidateRevision(PromptParamCandidateContext context) {
            if (context.Server is null) {
                return 0;
            }

            var worldId = context.Server.Main.worldID.ToString();
            HashCode hash = new();
            foreach (var region in TShock.Regions.Regions
                .Where(region => region.WorldID == worldId)
                .OrderBy(static region => region.Name, StringComparer.OrdinalIgnoreCase)) {
                hash.Add(region.Name, StringComparer.OrdinalIgnoreCase);
                hash.Add(region.WorldID ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                hash.Add(region.Z);
            }

            return hash.ToHashCode();
        }

        private static long GetGroupCandidateRevision(PromptParamCandidateContext context) {
            HashCode hash = new();
            foreach (var group in TShock.Groups.OrderBy(static group => group.Name, StringComparer.OrdinalIgnoreCase)) {
                hash.Add(group.Name, StringComparer.OrdinalIgnoreCase);
            }

            return hash.ToHashCode();
        }

        private static long GetTSPlayerCandidateRevision(PromptParamCandidateContext context) {
            return ComputeTSPlayerPromptRevision(context.Server);
        }

        private static long GetUserAccountCandidateRevision(PromptParamCandidateContext context) {
            HashCode hash = new();
            foreach (var account in TShock.UserAccounts.GetUserAccounts().OrderBy(static account => account.Name, StringComparer.OrdinalIgnoreCase)) {
                hash.Add(account.ID);
                hash.Add(account.Name, StringComparer.OrdinalIgnoreCase);
            }

            return hash.ToHashCode();
        }

        private static long GetPageRefRevision(PromptParamCandidateContext context) {
            return TSPageRefResolver.GetPageCount(context.ActiveSlot, context.Server) ?? 0;
        }

        private static int? ResolveGroupCandidateMatchWeight(
            PromptParamCandidateContext context,
            string candidate,
            int baseWeight) {
            var rawSearch = context.RawToken ?? string.Empty;
            if (rawSearch.Length == 0) {
                return baseWeight;
            }

            if (UsesExactLookup(context.ActiveSlot)) {
                var exact = TShock.Groups.GetGroupByName(rawSearch);
                return exact is not null && exact.Name.Equals(candidate, StringComparison.Ordinal)
                    ? baseWeight + 1000
                    : null;
            }

            return candidate.StartsWith(rawSearch, StringComparison.OrdinalIgnoreCase)
                ? ResolvePrefixMatchWeight(candidate, rawSearch, baseWeight)
                : null;
        }

        private static int? ResolveUserAccountCandidateMatchWeight(
            PromptParamCandidateContext context,
            string candidate,
            int baseWeight) {
            var rawSearch = context.RawToken ?? string.Empty;
            if (rawSearch.Length == 0) {
                return baseWeight;
            }

            if (UsesExactLookup(context.ActiveSlot)) {
                var exactAccount = TShock.UserAccounts.GetUserAccountByName(rawSearch);
                int? exactWeight = exactAccount is not null
                    && exactAccount.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                        ? baseWeight + 1000
                        : null;
                return exactWeight;
            }

            if (candidate.StartsWith(rawSearch, StringComparison.OrdinalIgnoreCase)) {
                var prefixWeight = ResolvePrefixMatchWeight(candidate, rawSearch, baseWeight);
                return prefixWeight;
            }

            // Mirror UserManager.GetUserAccountsByName(search) rather than reinterpreting the raw
            // token as a literal string. This keeps prompt candidates aligned with binder behavior
            // for SQL LIKE prefix matching, including "_" as a single-character wildcard.
            int? likeWeight = MatchesSqlLikePrefix(candidate, rawSearch)
                ? baseWeight
                : null;
            return likeWeight;
        }

        private static int ResolvePrefixMatchWeight(string candidate, string rawText, int baseWeight) {
            if (string.IsNullOrEmpty(rawText)) {
                return baseWeight;
            }

            if (candidate.Equals(rawText, StringComparison.OrdinalIgnoreCase)) {
                return baseWeight + 1000;
            }

            var remainingLength = Math.Max(0, candidate.Length - rawText.Length);
            return baseWeight + Math.Max(1, 256 - Math.Min(255, remainingLength));
        }

        private static bool UsesExactLookup(PromptSlotSegmentSpec slot) {
            return CommandPromptRoutes.IsSemanticLookupExact(slot.RouteMatchKind, slot.RouteSpecificity);
        }

        private static bool MatchesSqlLikePrefix(string candidate, string search) {
            // Keep the prefix "%" outside the caller so the intent remains obvious at call sites:
            // the binder matches "search%" rather than an unconstrained contains pattern.
            return MatchesSqlLikePattern(candidate ?? string.Empty, (search ?? string.Empty) + "%");
        }

        private static string BuildUserAccountWhitespaceWildcardAlias(string candidate) {
            if (string.IsNullOrEmpty(candidate)) {
                return string.Empty;
            }

            var buffer = candidate.ToCharArray();
            var changed = false;
            for (var index = 0; index < buffer.Length; index++) {
                if (!char.IsWhiteSpace(buffer[index])) {
                    continue;
                }

                buffer[index] = '_';
                changed = true;
            }

            return changed
                ? new string(buffer)
                : candidate;
        }

        private static long ComputeTSPlayerPromptRevision(ServerContext? scope) {
            HashCode hash = new();
            foreach (var candidate in EnumerateTSPlayerPromptCandidates(scope)) {
                hash.Add(candidate.ClientIndex);
                hash.Add(candidate.Name, StringComparer.OrdinalIgnoreCase);
                hash.Add(candidate.Server?.UniqueId ?? Guid.Empty);
            }

            return hash.ToHashCode();
        }

        private static IEnumerable<TSPlayerPromptCandidate> EnumerateTSPlayerPromptCandidates(ServerContext? scope) {
            foreach (var player in TShock.Players) {
                if (player is null
                    || !player.Active
                    || string.IsNullOrWhiteSpace(player.Name)) {
                    continue;
                }

                var currentServer = player.GetCurrentServer();
                if (scope is not null && !ReferenceEquals(currentServer, scope)) {
                    continue;
                }

                yield return new TSPlayerPromptCandidate(player.Index, currentServer, player.Name.Trim());
            }
        }

        private static List<TSPlayerPromptCandidate> FindMatchingTSPlayers(
            IReadOnlyList<TSPlayerPromptCandidate> candidates,
            string rawSearch) {
            var syntax = ResolveTSPlayerSearchSyntax(rawSearch, out var search);
            if (search.Length == 0) {
                return [];
            }

            return syntax switch {
                TSPlayerSearchSyntax.NameOnly => FindTSPlayerNameMatches(candidates, search),
                TSPlayerSearchSyntax.IndexOrName => FindTSPlayerIndexDisambiguatedMatches(candidates, search),
                _ => FindTSPlayerDefaultMatches(candidates, search),
            };
        }

        private static List<TSPlayerPromptCandidate> FindTSPlayerNameMatches(
            IReadOnlyList<TSPlayerPromptCandidate> candidates,
            string search) {
            // Mirror TSPlayer.FindByNameOrID("tsn:..."): exact name short-circuits to one player,
            // otherwise it falls back to ordinary name-prefix matching on the stripped suffix.
            if (TryFindTSPlayerByExactName(candidates, search, out var exactMatch)) {
                return [exactMatch];
            }

            return [.. candidates
                .Where(candidate => candidate.Name.StartsWith(search, StringComparison.OrdinalIgnoreCase))];
        }

        private static List<TSPlayerPromptCandidate> FindTSPlayerIndexDisambiguatedMatches(
            IReadOnlyList<TSPlayerPromptCandidate> candidates,
            string search) {
            // Preserve the legacy TShock quirk: "tsi:" is not a strict ID-only namespace. It
            // first attempts an exact active index match, but if that fails it continues into the
            // same name-prefix search path as the unprefixed lookup.
            if (int.TryParse(search, out var clientIndex)
                && TryFindTSPlayerByIndex(candidates, clientIndex, out var exactIndexMatch)) {
                return [exactIndexMatch];
            }

            return [.. candidates
                .Where(candidate => candidate.Name.StartsWith(search, StringComparison.OrdinalIgnoreCase))];
        }

        private static List<TSPlayerPromptCandidate> FindTSPlayerDefaultMatches(
            IReadOnlyList<TSPlayerPromptCandidate> candidates,
            string search) {
            List<TSPlayerPromptCandidate> matches = [];
            if (int.TryParse(search, out var clientIndex)
                && TryFindTSPlayerByIndex(candidates, clientIndex, out var exactIndexMatch)) {
                matches.Add(exactIndexMatch);
            }

            foreach (var candidate in candidates) {
                if (candidate.Name.StartsWith(search, StringComparison.OrdinalIgnoreCase)
                    && matches.All(existing => existing.ClientIndex != candidate.ClientIndex)) {
                    matches.Add(candidate);
                }
            }

            return matches;
        }

        private static bool TryFindTSPlayerByExactName(
            IReadOnlyList<TSPlayerPromptCandidate> candidates,
            string search,
            out TSPlayerPromptCandidate match) {
            foreach (var candidate in candidates) {
                if (candidate.Name.Equals(search, StringComparison.OrdinalIgnoreCase)) {
                    match = candidate;
                    return true;
                }
            }

            match = default;
            return false;
        }

        private static bool TryFindTSPlayerByIndex(
            IReadOnlyList<TSPlayerPromptCandidate> candidates,
            int clientIndex,
            out TSPlayerPromptCandidate match) {
            foreach (var candidate in candidates) {
                if (candidate.ClientIndex == clientIndex) {
                    match = candidate;
                    return true;
                }
            }

            match = default;
            return false;
        }

        private static TSPlayerSearchSyntax ResolveTSPlayerSearchSyntax(string rawSearch, out string search) {
            if (rawSearch.StartsWith("tsn:", StringComparison.OrdinalIgnoreCase)) {
                search = rawSearch[4..];
                return TSPlayerSearchSyntax.NameOnly;
            }

            if (rawSearch.StartsWith("tsi:", StringComparison.OrdinalIgnoreCase)) {
                search = rawSearch[4..];
                return TSPlayerSearchSyntax.IndexOrName;
            }

            search = rawSearch;
            return TSPlayerSearchSyntax.Default;
        }

        private static IEnumerable<string> EnumerateTSPlayerCandidateTexts(
            TSPlayerPromptCandidate candidate,
            TSPlayerSearchSyntax syntax) {
            switch (syntax) {
                case TSPlayerSearchSyntax.NameOnly:
                    yield return "tsn:" + candidate.Name;
                    yield break;
                case TSPlayerSearchSyntax.IndexOrName:
                    // Expose both forms for "tsi:" because the runtime binder still falls back to
                    // name-prefix matching on the stripped suffix when no active exact index match
                    // exists. Removing the name alias here would make prompt ghost stricter than
                    // command execution.
                    yield return "tsi:" + candidate.ClientIndex;
                    yield return "tsi:" + candidate.Name;
                    yield break;
                default:
                    yield return candidate.Name;
                    yield break;
            }
        }

        private static string FormatResolvedTSPlayer(TSPlayerPromptCandidate candidate, TSPlayerSearchSyntax syntax) {
            return syntax == TSPlayerSearchSyntax.Default
                ? candidate.Name
                : FormatAmbiguousTSPlayer(candidate);
        }

        private static string FormatAmbiguousTSPlayer(TSPlayerPromptCandidate candidate) {
            return $"{candidate.Name}({candidate.ClientIndex})";
        }

        private static bool MatchesSqlLikePattern(string candidate, string pattern) {
            var candidateIndex = 0;
            var patternIndex = 0;
            var starPatternIndex = -1;
            var starCandidateIndex = -1;

            while (candidateIndex < candidate.Length) {
                if (patternIndex < pattern.Length) {
                    var patternChar = pattern[patternIndex];
                    if (patternChar == '%') {
                        starPatternIndex = patternIndex++;
                        starCandidateIndex = candidateIndex;
                        continue;
                    }

                    if (patternChar == '_'
                        || char.ToUpperInvariant(patternChar) == char.ToUpperInvariant(candidate[candidateIndex])) {
                        patternIndex += 1;
                        candidateIndex += 1;
                        continue;
                    }
                }

                if (starPatternIndex >= 0) {
                    patternIndex = starPatternIndex + 1;
                    starCandidateIndex += 1;
                    candidateIndex = starCandidateIndex;
                    continue;
                }

                return false;
            }

            while (patternIndex < pattern.Length && pattern[patternIndex] == '%') {
                patternIndex += 1;
            }

            return patternIndex == pattern.Length;
        }

        private static PromptParamExplainResult ExplainNpcCatalog(PromptParamExplainContext context) {
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

        private static PromptParamExplainResult ExplainLiveNpc(PromptParamExplainContext context) {
            if (context.Server is null) {
                return PromptParamExplainResult.None;
            }

            var search = context.RawToken;
            List<NPC> matches = [];

            foreach (var npc in context.Server.Main.npc.Where(static npc => npc.active)) {
                var englishName = EnglishLanguage.GetNpcNameById(npc.netID);
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

        private static PromptParamExplainResult Resolved(string? displayText) {
            return string.IsNullOrWhiteSpace(displayText)
                ? Invalid()
                : new PromptParamExplainResult(PromptParamExplainState.Resolved, displayText.Trim());
        }

        private static PromptParamExplainResult Invalid()
            => new(PromptParamExplainState.Invalid, "invalid");

        private static PromptParamExplainResult Ambiguous(IEnumerable<string> displayValues) {
            List<string> candidates = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var displayValue in displayValues) {
                if (string.IsNullOrWhiteSpace(displayValue)) {
                    continue;
                }

                var normalized = displayValue.Trim();
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

            var preview = string.Join(", ", candidates.Take(3));
            if (candidates.Count > 3) {
                preview += ", ...";
            }

            return new PromptParamExplainResult(
                PromptParamExplainState.Ambiguous,
                "ambiguous: " + preview);
        }

        private static PromptParamExplainResult BuildExactOrAmbiguous(IEnumerable<string> matches) {
            List<string> normalized = [.. matches
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)];

            return normalized.Count switch {
                0 => Invalid(),
                1 => Resolved(normalized[0]),
                _ => Ambiguous(normalized),
            };
        }

        private static bool HasSemanticContinuationSearch(string rawSearch) {
            return !string.IsNullOrWhiteSpace(rawSearch)
                && rawSearch.Length > 0
                && char.IsWhiteSpace(rawSearch[^1])
                && rawSearch.Trim().Length > 0;
        }

        private static bool IsLiveNpcCommand(PromptAlternativeSpec alternative) {
            return alternative.Metadata is CommandPromptAlternativeMetadata metadata
                && metadata.RootName.Equals("tpnpc", StringComparison.OrdinalIgnoreCase);
        }

        private readonly record struct TSPlayerPromptCandidate(
            int ClientIndex,
            ServerContext? Server,
            string Name);

        private enum TSPlayerSearchSyntax : byte
        {
            Default,
            NameOnly,
            IndexOrName,
        }

        private sealed class DelegateParamExplainer(
            Func<PromptParamExplainContext, PromptParamExplainResult> handler,
            Func<PromptParamExplainContext, long> revisionProvider) : IParamValueExplainer
        {
            public long GetRevision(PromptParamExplainContext context) {
                return revisionProvider(context);
            }

            public bool TryExplain(PromptParamExplainContext context, out PromptParamExplainResult result) {
                result = handler(context);
                return result.State != PromptParamExplainState.None;
            }
        }

        private sealed class DelegateParamCandidateProvider(
            Func<PromptParamCandidateContext, IReadOnlyList<string>> handler,
            Func<PromptParamCandidateContext, long> revisionProvider,
            Func<PromptParamCandidateContext, string, int, int?>? matchWeightResolver = null) : IParamValueCandidateProvider, IParamValueCandidateMatcher
        {
            public long GetRevision(PromptParamCandidateContext context) {
                return revisionProvider(context);
            }

            public IReadOnlyList<string> GetCandidates(PromptParamCandidateContext context) {
                return handler(context) ?? [];
            }

            public int? ResolveMatchWeight(PromptParamCandidateContext context, string candidate, int baseWeight) {
                if (matchWeightResolver is not null) {
                    return matchWeightResolver(context, candidate, baseWeight);
                }

                // The fallback stays as plain prefix matching on purpose. Only the domains that
                // have a documented runtime mismatch should opt into a custom resolver above.
                var rawText = context.RawToken ?? string.Empty;
                return candidate.StartsWith(rawText, StringComparison.OrdinalIgnoreCase)
                    ? ResolvePrefixMatchWeight(candidate, rawText, baseWeight)
                    : null;
            }
        }

        private sealed class CommandRefPromptProvider : IParamValueCandidateProvider, IParamValueCandidateMatcher, IParamValueNestedPromptProvider
        {
            public long GetRevision(PromptParamCandidateContext context) {
                return 0;
            }

            public IReadOnlyList<string> GetCandidates(PromptParamCandidateContext context) {
                return TSCommandRefResolver.GetCandidates(context);
            }

            public int? ResolveMatchWeight(PromptParamCandidateContext context, string candidate, int baseWeight) {
                return TSCommandRefResolver.ResolveCandidateMatchWeight(context, candidate, baseWeight);
            }

            public bool TryCreateNestedPrompt(PromptParamCandidateContext context, [NotNullWhen(true)] out PromptSemanticSpec? prompt) {
                return TSCommandRefResolver.TryCreateNestedPrompt(context, out prompt);
            }
        }
    }
}
