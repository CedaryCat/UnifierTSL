using System.Collections.Immutable;
using System.Globalization;
using Terraria;
using Terraria.ID;
using TShockAPI.ConsolePrompting;
using TShockAPI.DB;
using TShockAPI.Localization;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Bindings;
using UnifierTSL.Commanding.Prompting;
using UnifierTSL.Servers;

namespace TShockAPI.Commanding
{
    internal enum TSRegionResizeDirection : byte
    {
        Up,
        Right,
        Down,
        Left,
    }

    internal static class TSCommandParamBinders
    {
        private static readonly CommandParamPromptData GroupPrompt = new() {
            SemanticKey = TSCommandPromptParamKeys.GroupRef,
            RouteMatchKind = CommandPromptRoutes.SemanticLookupSoft,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteSpecificity = CommandPromptRoutes.SemanticLookupSoftSpecificity,
        };

        private static readonly CommandParamPromptData WarpPrompt = new() {
            SemanticKey = TSCommandPromptParamKeys.WarpRef,
            RouteMatchKind = CommandPromptRoutes.SemanticLookupSoft,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteSpecificity = CommandPromptRoutes.SemanticLookupSoftSpecificity,
        };

        private static readonly CommandParamPromptData RegionPrompt = new() {
            SemanticKey = TSCommandPromptParamKeys.RegionRef,
            RouteMatchKind = CommandPromptRoutes.SemanticLookupSoft,
            RouteConsumptionMode = CommandPromptRoutes.GreedyPhrase,
            RouteSpecificity = CommandPromptRoutes.SemanticLookupSoftSpecificity,
        };

        private static readonly CommandParamPromptData UserPrompt = new() {
            SemanticKey = TSCommandPromptParamKeys.UserAccountRef,
            RouteMatchKind = CommandPromptRoutes.SemanticLookupSoft,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteSpecificity = CommandPromptRoutes.SemanticLookupSoftSpecificity,
        };

        private static readonly CommandParamPromptData NpcPrompt = new() {
            SemanticKey = TSCommandPromptParamKeys.NpcRef,
            RouteMatchKind = CommandPromptRoutes.SemanticLookupSoft,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteSpecificity = CommandPromptRoutes.SemanticLookupSoftSpecificity,
        };

        private static readonly CommandParamPromptData TimePrompt = new() {
            RouteMatchKind = CommandPromptRoutes.TimeOnly,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteSpecificity = CommandPromptRoutes.TimeOnlySpecificity,
        };

        private static readonly CommandParamPromptData RegionResizeDirectionPrompt = new() {
            SuggestionKindId = PromptSuggestionKindIds.Enum,
            EnumCandidates = ["u", "up", "r", "right", "d", "down", "l", "left"],
            RouteMatchKind = CommandPromptRoutes.ExactTokenSet,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteAcceptedTokens = ["u", "up", "r", "right", "d", "down", "l", "left"],
            RouteSpecificity = CommandPromptRoutes.ExactTokenSetSpecificity,
        };

        private static readonly CommandParamPromptData ProjectilePrompt = new() {
            RouteMatchKind = CommandPromptRoutes.Integer,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteSpecificity = CommandPromptRoutes.IntegerSpecificity,
        };

        private static readonly CommandParamPromptData TilePrompt = new() {
            RouteMatchKind = CommandPromptRoutes.Integer,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteSpecificity = CommandPromptRoutes.IntegerSpecificity,
        };

        public static void Configure(ICommandBindingRegistry builder) {
            ArgumentNullException.ThrowIfNull(builder);

            TSCommandValueBindings.Configure(builder);

            builder.AddImplicitBindingRule<TSPlayerRefAttribute, TSPlayer>(
                BindPlayer,
                ResolvePlayerPrompt,
                supportedModifiers: CommandParamModifiers.All | CommandParamModifiers.ServerScope | CommandParamModifiers.ExcludeCurrentContext,
                defaultName: "player",
                defaultAttribute: new TSPlayerRefAttribute());
            builder.AddBindingRule<TSItemRefAttribute, int>(
                BindItemId,
                ResolveItemPrompt,
                defaultName: "item");
            builder.AddBindingRule<TSItemRefAttribute, int?>(
                BindNullableItemId,
                ResolveItemPrompt,
                defaultName: "item");
            builder.AddImplicitBindingRule<GroupRefAttribute, Group>(
                BindGroup,
                ResolveGroupPrompt,
                supportedModifiers: CommandParamModifiers.None,
                defaultName: "group",
                defaultAttribute: new GroupRefAttribute());
            builder.AddImplicitBindingRule<WarpRefAttribute, Warp>(
                BindWarp,
                ResolveWarpPrompt,
                supportedModifiers: CommandParamModifiers.None,
                defaultName: "warp",
                defaultAttribute: new WarpRefAttribute());
            builder.AddImplicitBindingRule<RegionRefAttribute, Region>(
                BindRegion,
                ResolveRegionPrompt,
                supportedModifiers: CommandParamModifiers.None,
                defaultName: "region",
                defaultAttribute: new RegionRefAttribute());
            builder.AddImplicitBindingRule<UserAccountRefAttribute, UserAccount>(
                BindUserAccount,
                ResolveUserPrompt,
                supportedModifiers: CommandParamModifiers.None,
                defaultName: "user",
                defaultAttribute: new UserAccountRefAttribute());
            builder.AddImplicitBindingRule<NpcRefAttribute, NPC>(
                BindNpc,
                static _ => NpcPrompt,
                supportedModifiers: CommandParamModifiers.None,
                defaultName: "npc",
                defaultAttribute: new NpcRefAttribute());
            builder.AddBindingRule<NpcRefAttribute, int>(
                BindNpcId,
                static _ => NpcPrompt,
                defaultName: "npc");
            builder.AddBindingRule<NpcRefAttribute, int?>(
                BindNullableNpcId,
                static _ => NpcPrompt,
                defaultName: "npc");
            builder.AddImplicitBindingRule<WorldTimeAttribute, TimeOnly>(
                BindWorldTime,
                static _ => TimePrompt,
                supportedModifiers: CommandParamModifiers.None,
                defaultName: "time",
                defaultAttribute: new WorldTimeAttribute());
            builder.AddImplicitBindingRule<RegionResizeDirectionAttribute, TSRegionResizeDirection>(
                BindResizeDir,
                static _ => RegionResizeDirectionPrompt,
                supportedModifiers: CommandParamModifiers.None,
                defaultName: "direction",
                defaultAttribute: new RegionResizeDirectionAttribute());
            builder.AddBindingRule<RegionResizeDirectionAttribute, TSRegionResizeDirection?>(
                BindNullableResizeDir,
                static _ => RegionResizeDirectionPrompt,
                defaultName: "direction");
            builder.AddBindingRule<ProjectileRefAttribute, short>(
                BindProjShort,
                static _ => ProjectilePrompt,
                defaultName: "projectile");
            builder.AddBindingRule<ProjectileRefAttribute, short?>(
                BindNullableProjShort,
                static _ => ProjectilePrompt,
                defaultName: "projectile");
            builder.AddBindingRule<ProjectileRefAttribute, int>(
                BindProjInt,
                static _ => ProjectilePrompt,
                defaultName: "projectile");
            builder.AddBindingRule<ProjectileRefAttribute, int?>(
                BindNullableProjInt,
                static _ => ProjectilePrompt,
                defaultName: "projectile");
            builder.AddBindingRule<TileRefAttribute, short>(
                BindTileShort,
                static _ => TilePrompt,
                defaultName: "tile");
            builder.AddBindingRule<TileRefAttribute, short?>(
                BindNullableTileShort,
                static _ => TilePrompt,
                defaultName: "tile");
            builder.AddBindingRule<TileRefAttribute, int>(
                BindTileInt,
                static _ => TilePrompt,
                defaultName: "tile");
            builder.AddBindingRule<TileRefAttribute, int?>(
                BindNullableTileInt,
                static _ => TilePrompt,
                defaultName: "tile");
            builder.AddBindingRule<PageRefAttribute, int>(
                BindPageRef,
                ResolvePageRefPrompt,
                defaultName: "page");
            builder.AddBindingRule<PageRefAttribute, int?>(
                BindNullablePageRef,
                ResolvePageRefPrompt,
                defaultName: "page");
            builder.AddBindingRule<CommandRefAttribute, string>(
                BindCommandRef,
                ResolveCommandRefPrompt,
                defaultName: "command");
        }

        private static CommandParamPromptData ResolveGroupPrompt(GroupRefAttribute attribute) {
            return GroupPrompt with {
                RouteMatchKind = ResolveLookupRouteMatchKind(attribute.LookupMode),
                RouteSpecificity = ResolveLookupRouteSpecificity(attribute.LookupMode),
            };
        }

        private static CommandParamPromptData ResolveItemPrompt(TSItemRefAttribute attribute) {
            return CommonParamPrompts.ItemRef with {
                RouteConsumptionMode = attribute.ConsumptionMode == ItemConsumptionMode.GreedyPhrase
                    ? CommandPromptRoutes.GreedyPhrase
                    : CommandPromptRoutes.SingleToken,
                Metadata = TSPromptSlotMetadata.CreateItemLookupMode(attribute.LookupMode),
            };
        }

        private static CommandParamPromptData ResolveWarpPrompt(WarpRefAttribute attribute) {
            return WarpPrompt with {
                RouteMatchKind = ResolveLookupRouteMatchKind(attribute.LookupMode),
                RouteSpecificity = ResolveLookupRouteSpecificity(attribute.LookupMode),
            };
        }

        private static CommandParamPromptData ResolveUserPrompt(UserAccountRefAttribute attribute) {
            return UserPrompt with {
                RouteMatchKind = ResolveLookupRouteMatchKind(attribute.LookupMode),
                RouteSpecificity = ResolveLookupRouteSpecificity(attribute.LookupMode),
            };
        }

        private static CommandParamPromptData ResolveRegionPrompt(RegionRefAttribute attribute) {
            return RegionPrompt with {
                RouteMatchKind = ResolveLookupRouteMatchKind(attribute.LookupMode),
                RouteSpecificity = ResolveLookupRouteSpecificity(attribute.LookupMode),
                RouteConsumptionMode = attribute.ConsumptionMode == TSRegionConsumptionMode.GreedyPhrase
                    ? CommandPromptRoutes.GreedyPhrase
                    : CommandPromptRoutes.SingleToken,
            };
        }

        private static CommandParamPromptData ResolvePlayerPrompt(TSPlayerRefAttribute attribute) {
            return CommonParamPrompts.PlayerRef with {
                RouteConsumptionMode = attribute.ConsumptionMode == TSPlayerConsumptionMode.GreedyPhrase
                    ? CommandPromptRoutes.GreedyPhrase
                    : CommandPromptRoutes.SingleToken,
            };
        }

        private static CommandParamPromptData ResolvePageRefPrompt(PageRefAttribute attribute) {
            return new CommandParamPromptData {
                SemanticKey = TSCommandPromptParamKeys.PageRef,
                RouteMatchKind = CommandPromptRoutes.SemanticLookupExact,
                RouteConsumptionMode = CommandPromptRoutes.SingleToken,
                RouteSpecificity = CommandPromptRoutes.SemanticLookupExactSpecificity,
                Metadata = TSPromptSlotMetadata.CreatePageRef(attribute),
            };
        }

        private static CommandParamPromptData ResolveCommandRefPrompt(CommandRefAttribute attribute) {
            return new CommandParamPromptData {
                SemanticKey = TSCommandPromptParamKeys.CommandRef,
                RouteMatchKind = CommandPromptRoutes.SemanticLookupSoft,
                RouteConsumptionMode = attribute.Recursive
                    ? CommandPromptRoutes.GreedyPhrase
                    : CommandPromptRoutes.SingleToken,
                RouteSpecificity = CommandPromptRoutes.SemanticLookupSoftSpecificity,
                Metadata = TSPromptSlotMetadata.CreateCommandRef(attribute),
            };
        }

        private static PromptRouteMatchKind ResolveLookupRouteMatchKind(TSLookupMatchMode lookupMode) {
            return lookupMode == TSLookupMatchMode.ExactOnly
                ? CommandPromptRoutes.SemanticLookupExact
                : CommandPromptRoutes.SemanticLookupSoft;
        }

        private static int ResolveLookupRouteSpecificity(TSLookupMatchMode lookupMode) {
            return lookupMode == TSLookupMatchMode.ExactOnly
                ? CommandPromptRoutes.SemanticLookupExactSpecificity
                : CommandPromptRoutes.SemanticLookupSoftSpecificity;
        }

        private static CommandParamBindingResult BindPageRef(
            CommandParamBindingContext context,
            PageRefAttribute attribute) {
            return BindPageRefCore(context, attribute, nullable: false);
        }

        private static CommandParamBindingResult BindNullablePageRef(
            CommandParamBindingContext context,
            PageRefAttribute attribute) {
            return BindPageRefCore(context, attribute, nullable: true);
        }

        private static CommandParamBindingResult BindPageRefCore(
            CommandParamBindingContext context,
            PageRefAttribute attribute,
            bool nullable) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var raw = context.UserArguments[context.UserIndex].Trim();
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 1) {
                return CommandParamBindingResult.Failure(CommandOutcome.Error(
                    CommandAttributeText.Invoke(attribute, nameof(PageRefAttribute.InvalidTokenMessage), attribute.InvalidTokenMessage, raw)));
            }

            if (attribute.UpperBoundBehavior == PageRefUpperBoundBehavior.ValidateKnownCount) {
                var pageCount = TSPageRefResolver.ResolvePageCount(
                    attribute.SourceType,
                    new PageRefSourceContext(context.InvocationContext.Server, context.InvocationContext));
                if (pageCount is int knownPageCount && value > knownPageCount) {
                    return CommandParamBindingResult.Failure(CommandOutcome.Error(
                        CommandAttributeText.Invoke(attribute, nameof(PageRefAttribute.InvalidTokenMessage), attribute.InvalidTokenMessage, raw)));
                }
            }

            return CommandParamBindingResult.Success(nullable ? (int?)value : value);
        }

        private static CommandParamBindingResult BindCommandRef(
            CommandParamBindingContext context,
            CommandRefAttribute attribute) {
            return TSCommandRefResolver.Bind(context, attribute);
        }

        private static CommandParamBindingResult BindPlayer(
            CommandParamBindingContext context,
            TSPlayerRefAttribute attribute) {

            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var excludedCurrentPlayer = ResolveExcludedCurrentPlayer(context);
            if (context.Parameter.ConsumesRemainingTokens) {
                var variadicToken = ResolveSearchToken(context);
                return variadicToken.Length == 0
                    ? CommandParamBindingResult.Mismatch()
                    : BindPlayerToken(
                        context,
                        attribute,
                        excludedCurrentPlayer,
                        variadicToken,
                        ResolveConsumedTokens(context),
                        failureConsumedTokens: ResolveConsumedTokens(context));
            }

            if (attribute.ConsumptionMode == TSPlayerConsumptionMode.GreedyPhrase) {
                var maxSpan = context.UserArguments.Length - context.UserIndex;
                if (maxSpan <= 0) {
                    return CommandParamBindingResult.Mismatch();
                }

                List<BindingCandidate> candidates = [];
                CommandOutcome? fallbackFailure = null;
                var fallbackConsumedTokens = 0;
                for (var tokenCount = maxSpan; tokenCount >= 1; tokenCount--) {
                    var token = string.Join(' ', context.UserArguments.Skip(context.UserIndex).Take(tokenCount)).Trim();
                    if (token.Length == 0) {
                        continue;
                    }

                    var result = BindPlayerToken(
                        context,
                        attribute,
                        excludedCurrentPlayer,
                        token,
                        tokenCount,
                        failureConsumedTokens: tokenCount);
                    if (result.IsSuccess) {
                        candidates.AddRange(result.Candidates);
                        continue;
                    }

                    if (tokenCount == maxSpan && result.FailureOutcome is not null) {
                        fallbackFailure = result.FailureOutcome;
                        fallbackConsumedTokens = result.FailureConsumedTokens;
                    }
                }

                return candidates.Count > 0
                    ? CommandParamBindingResult.SuccessMany(candidates, fallbackFailure, fallbackConsumedTokens)
                    : fallbackFailure is not null
                        ? CommandParamBindingResult.Failure(fallbackFailure, fallbackConsumedTokens)
                        : CommandParamBindingResult.Mismatch();
            }

            var singleToken = ResolveSearchToken(context);
            return singleToken.Length == 0
                ? CommandParamBindingResult.Mismatch()
                : BindPlayerToken(
                    context,
                    attribute,
                    excludedCurrentPlayer,
                    singleToken,
                    ResolveConsumedTokens(context));
        }

        private static CommandParamBindingResult BindPlayerToken(
            CommandParamBindingContext context,
            TSPlayerRefAttribute attribute,
            TSPlayer? excludedCurrentPlayer,
            string token,
            int consumedTokens,
            int failureConsumedTokens = 0) {
            if ((context.Modifiers & CommandParamModifiers.All) != 0
                && token.Equals("*", StringComparison.OrdinalIgnoreCase)) {
                if ((context.Modifiers & CommandParamModifiers.ServerScope) != 0) {
                    if (!TryRequireCurrentServer(context, out var scopedServer, out var failure)) {
                        return CommandParamBindingResult.Failure(failure!, failureConsumedTokens);
                    }

                    return CommandParamBindingResult.Success(new TSPlayerAll(scopedServer, excludedCurrentPlayer?.Index), consumedTokens);
                }

                return CommandParamBindingResult.Success(new TSPlayerAll(excludedPlayerIndex: excludedCurrentPlayer?.Index), consumedTokens);
            }

            var players = TSPlayer.FindByNameOrID(token);
            if ((context.Modifiers & CommandParamModifiers.ServerScope) != 0) {
                if (!TryRequireCurrentServer(context, out var scope, out var failure)) {
                    return CommandParamBindingResult.Failure(failure!, failureConsumedTokens);
                }

                players = [.. players.Where(player => ReferenceEquals(player.GetCurrentServer(), scope))];
            }

            if (excludedCurrentPlayer is not null) {
                players = [.. players.Where(player => player.Index != excludedCurrentPlayer.Index)];
            }

            if (players.Count == 0) {
                if (excludedCurrentPlayer is not null && IsCurrentPlayerToken(token, excludedCurrentPlayer)) {
                    return CommandParamBindingResult.Failure(CommandOutcome.Error(GetString("You cannot target yourself.")), failureConsumedTokens);
                }

                return CommandParamBindingResult.Failure(BuildInvalidPlayerOutcome(token, attribute), failureConsumedTokens);
            }

            if (players.Count > 1) {
                return CommandParamBindingResult.Failure(BuildPlayerMultiMatch(players, attribute), failureConsumedTokens);
            }

            return CommandParamBindingResult.Success(players[0], consumedTokens);
        }

        private static CommandParamBindingResult BindItemId(
            CommandParamBindingContext context,
            TSItemRefAttribute attribute) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            return attribute.ConsumptionMode == ItemConsumptionMode.GreedyPhrase
                ? BindGreedyItemId(context, attribute)
                : BindItemToken(
                    attribute.LookupMode == TSItemLookupMode.LegacyCommand
                        ? ResolveRawSearchToken(context)
                        : ResolveSearchToken(context),
                    ResolveConsumedTokens(context),
                    attribute);
        }

        private static CommandParamBindingResult BindNullableItemId(
            CommandParamBindingContext context,
            TSItemRefAttribute attribute) {
            return ProjectToNullable<int>(BindItemId(context, attribute));
        }

        private static CommandParamBindingResult BindGreedyItemId(
            CommandParamBindingContext context,
            TSItemRefAttribute attribute) {
            var maxSpan = GetGreedyItemLimit(context.UserArguments, context.UserIndex);
            if (maxSpan <= 0) {
                return CommandParamBindingResult.Mismatch();
            }

            List<BindingCandidate> candidates = [];
            CommandOutcome? fallbackFailure = null;
            var fallbackConsumedTokens = 0;
            for (var tokenCount = maxSpan; tokenCount >= 1; tokenCount--) {
                var token = attribute.LookupMode == TSItemLookupMode.LegacyCommand
                    ? string.Join(' ', context.UserArguments.Skip(context.UserIndex).Take(tokenCount))
                    : string.Join(' ', context.UserArguments.Skip(context.UserIndex).Take(tokenCount)).Trim();
                var result = BindItemToken(token, tokenCount, attribute);
                if (result.IsSuccess) {
                    candidates.AddRange(result.Candidates);
                    continue;
                }

                if (tokenCount == maxSpan && result.FailureOutcome is not null) {
                    fallbackFailure = result.FailureOutcome;
                    fallbackConsumedTokens = result.FailureConsumedTokens;
                }
            }

            return candidates.Count > 0
                ? CommandParamBindingResult.SuccessMany(candidates, fallbackFailure, fallbackConsumedTokens)
                : fallbackFailure is not null
                    ? CommandParamBindingResult.Failure(fallbackFailure, fallbackConsumedTokens)
                    : CommandParamBindingResult.Mismatch();
        }

        private static CommandParamBindingResult BindItemToken(
            string token,
            int consumedTokens,
            TSItemRefAttribute attribute) {
            return attribute.LookupMode == TSItemLookupMode.LegacyCommand
                ? BindLegacyItemToken(token, consumedTokens, attribute)
                : BindPromptItemToken(token, consumedTokens, attribute);
        }

        private static CommandParamBindingResult BindPromptItemToken(
            string token,
            int consumedTokens,
            TSItemRefAttribute attribute) {
            if (token.Length == 0) {
                return CommandParamBindingResult.Mismatch();
            }

            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)) {
                if (itemId < 1 || itemId >= ItemID.Count) {
                    return CommandParamBindingResult.Failure(BuildItemFailureOutcome(
                        token,
                        multipleMatches: null,
                        invalidNumericId: true,
                        attribute), consumedTokens);
                }

                return CommandParamBindingResult.Success(itemId, consumedTokens);
            }

            var matches = CommandPromptCommonObjects.ResolveItemIds(token);
            if (matches.Count == 0) {
                return CommandParamBindingResult.Failure(BuildItemFailureOutcome(
                    token,
                    multipleMatches: null,
                    invalidNumericId: false,
                    attribute), consumedTokens);
            }

            if (matches.Count > 1) {
                return CommandParamBindingResult.Failure(BuildItemFailureOutcome(
                    token,
                    matches.Select(static id => CommandPromptCommonObjects.GetItemDisplayName(id) ?? id.ToString(CultureInfo.InvariantCulture)),
                    invalidNumericId: false,
                    attribute), consumedTokens);
            }

            return CommandParamBindingResult.Success(matches[0], consumedTokens);
        }

        private static CommandParamBindingResult BindLegacyItemToken(
            string token,
            int consumedTokens,
            TSItemRefAttribute attribute) {
            var matches = Utils.GetItemByIdOrName(token);
            if (matches.Count == 0) {
                return CommandParamBindingResult.Failure(BuildItemFailureOutcome(
                    token,
                    multipleMatches: null,
                    invalidNumericId: false,
                    attribute), consumedTokens);
            }

            if (matches.Count > 1) {
                return CommandParamBindingResult.Failure(BuildItemFailureOutcome(
                    token,
                    matches.Select(static item => $"{item.Name}({item.type})"),
                    invalidNumericId: false,
                    attribute), consumedTokens);
            }

            return CommandParamBindingResult.Success(matches[0].type, consumedTokens);
        }

        private static CommandParamBindingResult BindGroup(
            CommandParamBindingContext context,
            GroupRefAttribute attribute) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var token = ResolveSearchToken(context);
            if (attribute.LookupMode == TSLookupMatchMode.ExactOnly) {
                var rawToken = ResolveRawSearchToken(context);
                var exact = TShock.Groups.GetGroupByName(rawToken);
                return exact is not null
                    ? CommandParamBindingResult.Success(exact, ResolveConsumedTokens(context))
                    : CommandParamBindingResult.Failure(BuildLookupFailureOutcome(
                        token: rawToken,
                        attribute: attribute,
                        propertyName: nameof(GroupRefAttribute.InvalidGroupMessage),
                        customMessage: attribute.InvalidGroupMessage,
                        defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid group."))));
            }

            if (token.Length == 0) {
                return CommandParamBindingResult.Mismatch();
            }

            List<Group> exactMatches = [.. TShock.Groups.Where(group => group.Name.Equals(token, StringComparison.OrdinalIgnoreCase))];
            if (exactMatches.Count == 1) {
                return CommandParamBindingResult.Success(exactMatches[0], ResolveConsumedTokens(context));
            }

            var matches = exactMatches.Count > 1
                ? exactMatches
                : [.. TShock.Groups.Where(group => group.Name.StartsWith(token, StringComparison.OrdinalIgnoreCase))];
            if (matches.Count == 0) {
                return CommandParamBindingResult.Failure(BuildLookupFailureOutcome(
                    token: token,
                    attribute: attribute,
                    propertyName: nameof(GroupRefAttribute.InvalidGroupMessage),
                    customMessage: attribute.InvalidGroupMessage,
                    defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid group."))));
            }

            if (matches.Count > 1) {
                return CommandParamBindingResult.Failure(BuildMultipleMatchOutcome(matches.Select(static group => group.Name)));
            }

            return CommandParamBindingResult.Success(matches[0], ResolveConsumedTokens(context));
        }

        private static CommandParamBindingResult BindWarp(
            CommandParamBindingContext context,
            WarpRefAttribute attribute) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            if (!TryRequireCurrentServer(context, out var scope, out var failure)) {
                return CommandParamBindingResult.Failure(failure!);
            }

            var token = ResolveSearchToken(context);
            if (token.Length == 0) {
                return CommandParamBindingResult.Mismatch();
            }

            var worldId = scope!.Main.worldID.ToString();
            var rawToken = attribute.LookupMode == TSLookupMatchMode.ExactOnly
                ? token
                : token.Trim();
            List<Warp> exactMatches = [.. TShock.Warps.Warps
                .Where(warp => warp.WorldID == worldId && warp.Name.Equals(rawToken, StringComparison.OrdinalIgnoreCase))];
            if (exactMatches.Count == 1) {
                return CommandParamBindingResult.Success(exactMatches[0], ResolveConsumedTokens(context));
            }

            if (attribute.LookupMode == TSLookupMatchMode.ExactOnly) {
                return CommandParamBindingResult.Failure(BuildLookupFailureOutcome(
                    token: rawToken,
                    attribute: attribute,
                    propertyName: nameof(WarpRefAttribute.InvalidWarpMessage),
                    customMessage: attribute.InvalidWarpMessage,
                    defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid warp."))));
            }

            var matches = exactMatches.Count > 1
                ? exactMatches
                : [.. TShock.Warps.Warps
                    .Where(warp => warp.WorldID == worldId && warp.Name.StartsWith(rawToken, StringComparison.OrdinalIgnoreCase))];
            if (matches.Count == 0) {
                return CommandParamBindingResult.Failure(BuildLookupFailureOutcome(
                    token: rawToken,
                    attribute: attribute,
                    propertyName: nameof(WarpRefAttribute.InvalidWarpMessage),
                    customMessage: attribute.InvalidWarpMessage,
                    defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid warp."))));
            }

            if (matches.Count > 1) {
                return CommandParamBindingResult.Failure(BuildMultipleMatchOutcome(matches.Select(static warp => warp.Name)));
            }

            return CommandParamBindingResult.Success(matches[0], ResolveConsumedTokens(context));
        }

        private static CommandParamBindingResult BindRegion(
            CommandParamBindingContext context,
            RegionRefAttribute attribute) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            if (!TryRequireCurrentServer(context, out var scope, out var failure)) {
                return CommandParamBindingResult.Failure(failure!);
            }

            if (context.Parameter.ConsumesRemainingTokens || attribute.ConsumptionMode == TSRegionConsumptionMode.SingleToken) {
                var token = attribute.LookupMode == TSLookupMatchMode.ExactOnly
                    ? ResolveRawSearchToken(context)
                    : ResolveSearchToken(context);
                return token.Trim().Length == 0
                    ? CommandParamBindingResult.Mismatch()
                    : BindSingleRegionToken(scope!, token, ResolveConsumedTokens(context), attribute);
            }

            var maxSpan = context.UserArguments.Length - context.UserIndex;
            if (maxSpan <= 0) {
                return CommandParamBindingResult.Mismatch();
            }

            List<BindingCandidate> candidates = [];
            CommandOutcome? fallbackFailure = null;
            var fallbackConsumedTokens = 0;
            for (var tokenCount = maxSpan; tokenCount >= 1; tokenCount--) {
                var token = attribute.LookupMode == TSLookupMatchMode.ExactOnly
                    ? string.Join(' ', context.UserArguments.Skip(context.UserIndex).Take(tokenCount))
                    : string.Join(' ', context.UserArguments.Skip(context.UserIndex).Take(tokenCount)).Trim();
                if (token.Trim().Length == 0) {
                    continue;
                }

                var result = BindSingleRegionToken(scope!, token, tokenCount, attribute);
                if (result.IsSuccess) {
                    candidates.AddRange(result.Candidates);
                    continue;
                }

                if (tokenCount == maxSpan && result.FailureOutcome is not null) {
                    fallbackFailure = result.FailureOutcome;
                    fallbackConsumedTokens = result.FailureConsumedTokens;
                }
            }

            return candidates.Count > 0
                ? CommandParamBindingResult.SuccessMany(candidates, fallbackFailure, fallbackConsumedTokens)
                : fallbackFailure is not null
                    ? CommandParamBindingResult.Failure(fallbackFailure, fallbackConsumedTokens)
                    : CommandParamBindingResult.Mismatch();
        }

        private static CommandParamBindingResult BindUserAccount(
            CommandParamBindingContext context,
            UserAccountRefAttribute attribute) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var token = ResolveSearchToken(context);
            var rawToken = attribute.LookupMode == TSLookupMatchMode.ExactOnly
                ? ResolveRawSearchToken(context)
                : token;
            var exact = TShock.UserAccounts.GetUserAccountByName(rawToken);
            if (exact is not null) {
                return CommandParamBindingResult.Success(exact, ResolveConsumedTokens(context));
            }

            if (attribute.LookupMode == TSLookupMatchMode.ExactOnly) {
                return CommandParamBindingResult.Failure(BuildLookupFailureOutcome(
                    token: rawToken,
                    attribute: attribute,
                    propertyName: nameof(UserAccountRefAttribute.InvalidUserAccountMessage),
                    customMessage: attribute.InvalidUserAccountMessage,
                    defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid user account."))));
            }

            if (token.Length == 0) {
                return CommandParamBindingResult.Mismatch();
            }

            List<UserAccount> matches = [.. TShock.UserAccounts.GetUserAccountsByName(token)];
            if (matches.Count == 0) {
                return CommandParamBindingResult.Failure(BuildLookupFailureOutcome(
                    token: token,
                    attribute: attribute,
                    propertyName: nameof(UserAccountRefAttribute.InvalidUserAccountMessage),
                    customMessage: attribute.InvalidUserAccountMessage,
                    defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid user account."))));
            }

            if (matches.Count > 1) {
                return CommandParamBindingResult.Failure(BuildMultipleMatchOutcome(matches.Select(static account => account.Name)));
            }

            return CommandParamBindingResult.Success(matches[0], ResolveConsumedTokens(context));
        }

        private static CommandParamBindingResult BindWorldTime(
            CommandParamBindingContext context,
            WorldTimeAttribute attribute) {
            _ = attribute;

            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var token = context.UserArguments[context.UserIndex].Trim();
            if (token.Length == 0) {
                return CommandParamBindingResult.Mismatch();
            }

            var parts = token.Split(':');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
                || hours < 0 || hours > 23
                || minutes < 0 || minutes > 59) {
                return CommandParamBindingResult.Failure(CommandOutcome.Error(
                    GetString("Invalid time string. Proper format: hh:mm, in 24-hour time.")));
            }

            return CommandParamBindingResult.Success(new TimeOnly(hours, minutes));
        }

        private static CommandParamBindingResult BindNpcId(
            CommandParamBindingContext context,
            NpcRefAttribute attribute) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var token = ResolveSearchToken(context);
            if (token.Length == 0) {
                return CommandParamBindingResult.Mismatch();
            }

            List<NPC> matches = [.. Utils.GetNPCByIdOrName(token)
                .Where(static npc => npc is not null)
                .GroupBy(static npc => npc.netID)
                .Select(static group => group.First())];
            if (matches.Count == 0) {
                return CommandParamBindingResult.Failure(
                    BuildLookupFailureOutcome(
                        token: token,
                        attribute: attribute,
                        propertyName: nameof(NpcRefAttribute.InvalidNpcMessage),
                        customMessage: attribute.InvalidNpcMessage,
                        defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid mob type!"))),
                    ResolveConsumedTokens(context));
            }

            if (matches.Count > 1) {
                return CommandParamBindingResult.Failure(
                    BuildMultipleMatchOutcome(matches.Select(static npc => $"{npc.FullName}({npc.type})")),
                    ResolveConsumedTokens(context));
            }

            return CommandParamBindingResult.Success(matches[0].netID, ResolveConsumedTokens(context));
        }

        private static CommandParamBindingResult BindNullableNpcId(
            CommandParamBindingContext context,
            NpcRefAttribute attribute) {
            return ProjectToNullable<int>(BindNpcId(context, attribute));
        }

        private static CommandParamBindingResult BindResizeDir(
            CommandParamBindingContext context,
            RegionResizeDirectionAttribute attribute) {
            _ = attribute;

            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var token = context.UserArguments[context.UserIndex].Trim();
            if (token.Length == 0) {
                return CommandParamBindingResult.Mismatch();
            }

            TSRegionResizeDirection? direction = token.ToLowerInvariant() switch {
                "u" or "up" => TSRegionResizeDirection.Up,
                "r" or "right" => TSRegionResizeDirection.Right,
                "d" or "down" => TSRegionResizeDirection.Down,
                "l" or "left" => TSRegionResizeDirection.Left,
                _ => null,
            };
            return direction is null
                ? CommandParamBindingResult.Failure(CommandOutcome.Error(
                    GetString("Invalid region resize direction. Expected one of: u/up, r/right, d/down, l/left.")))
                : CommandParamBindingResult.Success(direction.Value);
        }

        private static CommandParamBindingResult BindNullableResizeDir(
            CommandParamBindingContext context,
            RegionResizeDirectionAttribute attribute) {
            return ProjectToNullable<TSRegionResizeDirection>(BindResizeDir(context, attribute));
        }

        private static CommandParamBindingResult BindProjShort(
            CommandParamBindingContext context,
            ProjectileRefAttribute attribute) {
            return BindBoundedId(
                context,
                minimumInclusive: 1,
                maximumExclusive: ProjectileID.Count,
                attribute: attribute, propertyName: nameof(ProjectileRefAttribute.InvalidProjectileMessage), customMessage: attribute.InvalidProjectileMessage,
                defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid projectile ID.")),
                factory: static id => (short)id);
        }

        private static CommandParamBindingResult BindNullableProjShort(
            CommandParamBindingContext context,
            ProjectileRefAttribute attribute) {
            return ProjectToNullable<short>(BindProjShort(context, attribute));
        }

        private static CommandParamBindingResult BindProjInt(
            CommandParamBindingContext context,
            ProjectileRefAttribute attribute) {
            return BindBoundedId(
                context,
                minimumInclusive: 1,
                maximumExclusive: ProjectileID.Count,
                attribute: attribute, propertyName: nameof(ProjectileRefAttribute.InvalidProjectileMessage), customMessage: attribute.InvalidProjectileMessage,
                defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid projectile ID.")),
                factory: static id => id);
        }

        private static CommandParamBindingResult BindNullableProjInt(
            CommandParamBindingContext context,
            ProjectileRefAttribute attribute) {
            return ProjectToNullable<int>(BindProjInt(context, attribute));
        }

        private static CommandParamBindingResult BindTileShort(
            CommandParamBindingContext context,
            TileRefAttribute attribute) {
            return BindBoundedId(
                context,
                minimumInclusive: 0,
                maximumExclusive: TileID.Count,
                attribute: attribute, propertyName: nameof(TileRefAttribute.InvalidTileMessage), customMessage: attribute.InvalidTileMessage,
                defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid tile ID.")),
                factory: static id => (short)id);
        }

        private static CommandParamBindingResult BindNullableTileShort(
            CommandParamBindingContext context,
            TileRefAttribute attribute) {
            return ProjectToNullable<short>(BindTileShort(context, attribute));
        }

        private static CommandParamBindingResult BindTileInt(
            CommandParamBindingContext context,
            TileRefAttribute attribute) {
            return BindBoundedId(
                context,
                minimumInclusive: 0,
                maximumExclusive: TileID.Count,
                attribute: attribute, propertyName: nameof(TileRefAttribute.InvalidTileMessage), customMessage: attribute.InvalidTileMessage,
                defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid tile ID.")),
                factory: static id => id);
        }

        private static CommandParamBindingResult BindNullableTileInt(
            CommandParamBindingContext context,
            TileRefAttribute attribute) {
            return ProjectToNullable<int>(BindTileInt(context, attribute));
        }

        private static CommandParamBindingResult BindNpc(
            CommandParamBindingContext context,
            NpcRefAttribute attribute) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            if (!TryRequireCurrentServer(context, out var scope, out var failure)) {
                return CommandParamBindingResult.Failure(failure!);
            }

            var search = ResolveSearchToken(context);
            if (search.Length == 0) {
                return CommandParamBindingResult.Mismatch();
            }

            List<NPC> matches = [];
            foreach (var npc in scope!.Main.npc.Where(static npc => npc.active)) {
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
                return CommandParamBindingResult.Failure(BuildLookupFailureOutcome(
                    token: search,
                    attribute: attribute,
                    propertyName: nameof(NpcRefAttribute.InvalidNpcMessage),
                    customMessage: attribute.InvalidNpcMessage,
                    defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid destination server.NPC."))));
            }

            if (matches.Count > 1) {
                return CommandParamBindingResult.Failure(BuildMultipleMatchOutcome(matches.Select(static npc => $"{npc.FullName}({npc.whoAmI})")));
            }

            return CommandParamBindingResult.Success(matches[0], ResolveConsumedTokens(context));
        }

        private static CommandParamBindingResult BindSingleRegionToken(
            ServerContext scope,
            string token,
            int consumedTokens,
            RegionRefAttribute attribute) {
            var worldId = scope.Main.worldID.ToString();
            var rawToken = attribute.LookupMode == TSLookupMatchMode.ExactOnly
                ? token
                : token.Trim();
            List<Region> exactMatches = [.. TShock.Regions.Regions
                .Where(region => region.WorldID == worldId
                    && region.Name.Equals(rawToken, StringComparison.OrdinalIgnoreCase))];
            if (exactMatches.Count == 1) {
                return CommandParamBindingResult.Success(exactMatches[0], consumedTokens);
            }

            if (attribute.LookupMode == TSLookupMatchMode.ExactOnly) {
                return CommandParamBindingResult.Failure(
                    BuildLookupFailureOutcome(
                        token: rawToken,
                        attribute: attribute,
                        propertyName: nameof(RegionRefAttribute.InvalidRegionMessage),
                        customMessage: attribute.InvalidRegionMessage,
                        defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid region."))),
                    consumedTokens);
            }

            var matches = exactMatches.Count > 1
                ? exactMatches
                : [.. TShock.Regions.Regions
                    .Where(region => region.WorldID == worldId
                        && region.Name.StartsWith(rawToken, StringComparison.OrdinalIgnoreCase))];
            if (matches.Count == 0) {
                return CommandParamBindingResult.Failure(
                    BuildLookupFailureOutcome(
                        token: rawToken,
                        attribute: attribute,
                        propertyName: nameof(RegionRefAttribute.InvalidRegionMessage),
                        customMessage: attribute.InvalidRegionMessage,
                        defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid region."))),
                    consumedTokens);
            }

            if (matches.Count > 1) {
                return CommandParamBindingResult.Failure(
                    BuildMultipleMatchOutcome(matches.Select(static region => region.Name)),
                    consumedTokens);
            }

            return CommandParamBindingResult.Success(matches[0], consumedTokens);
        }

        private static int GetGreedyItemLimit(ImmutableArray<string> userArguments, int userIndex) {
            var remainingCount = userArguments.Length - userIndex;
            if (remainingCount <= 0) {
                return 0;
            }

            for (var offset = 1; offset < remainingCount; offset++) {
                if (int.TryParse(userArguments[userIndex + offset], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) {
                    return offset;
                }
            }

            return remainingCount;
        }

        private static CommandParamBindingResult BindBoundedId<TValue>(
            CommandParamBindingContext context,
            int minimumInclusive,
            int maximumExclusive,
            Attribute attribute,
            string propertyName,
            string? customMessage,
            Func<string, CommandOutcome> defaultOutcome,
            Func<int, TValue> factory) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var token = context.UserArguments[context.UserIndex].Trim();
            if (token.Length == 0) {
                return CommandParamBindingResult.Mismatch();
            }

            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                || id < minimumInclusive
                || id >= maximumExclusive) {
                return CommandParamBindingResult.Failure(
                    BuildLookupFailureOutcome(token, attribute, propertyName, customMessage, defaultOutcome),
                    consumedTokens: 1);
            }

            return CommandParamBindingResult.Success(factory(id));
        }

        private static CommandParamBindingResult ProjectToNullable<TValue>(CommandParamBindingResult result)
            where TValue : struct {
            return !result.IsSuccess
                ? result
                : CommandParamBindingResult.SuccessMany(
                    result.Candidates.Select(static candidate =>
                        new BindingCandidate((TValue?)candidate.Value!, candidate.ConsumedTokens)),
                    result.FailureOutcome,
                    result.FailureConsumedTokens);
        }

        private static bool TryRequireCurrentServer(
            CommandParamBindingContext context,
            out ServerContext? scope,
            out CommandOutcome? failure) {
            failure = null;

            scope = context.InvocationContext.Server;
            if (scope is not null) {
                return true;
            }

            failure = CommandOutcome.Error(GetString("You must use this command in sepcific server."));
            return false;
        }

        private static string ResolveSearchToken(CommandParamBindingContext context) {
            return context.Parameter.ConsumesRemainingTokens
                ? string.Join(' ', context.UserArguments.Skip(context.UserIndex)).Trim()
                : context.UserArguments[context.UserIndex].Trim();
        }

        private static string ResolveRawSearchToken(CommandParamBindingContext context) {
            return context.Parameter.ConsumesRemainingTokens
                ? string.Join(' ', context.UserArguments.Skip(context.UserIndex))
                : context.UserArguments[context.UserIndex];
        }

        private static int ResolveConsumedTokens(CommandParamBindingContext context) {
            return context.Parameter.ConsumesRemainingTokens
                ? context.UserArguments.Length - context.UserIndex
                : 1;
        }

        private static CommandOutcome BuildMultipleMatchOutcome(IEnumerable<string> matches) {
            return CommandOutcome.MultipleMatches(matches.Cast<object>());
        }

        private static CommandOutcome BuildInvalidPlayerOutcome(string token, TSPlayerRefAttribute attribute) {
            return BuildLookupFailureOutcome(
                token: token,
                attribute: attribute,
                propertyName: nameof(TSPlayerRefAttribute.InvalidPlayerMessage),
                customMessage: attribute.InvalidPlayerMessage,
                defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid player.")));
        }

        private static CommandOutcome BuildPlayerMultiMatch(
            IEnumerable<TSPlayer> players,
            TSPlayerRefAttribute attribute) {
            return BuildMultipleMatchOutcome(players.Select(player => ResolvePlayerMatchDisplay(player, attribute)));
        }

        private static CommandOutcome BuildLookupFailureOutcome(
            string token,
            Attribute attribute,
            string propertyName,
            string? customMessage,
            Func<string, CommandOutcome> defaultOutcome) {
            if (string.IsNullOrWhiteSpace(customMessage)) {
                return defaultOutcome(token);
            }

            return CommandOutcome.Error(CommandAttributeText.Invoke(attribute, propertyName, customMessage, token));
        }

        private static CommandOutcome BuildItemFailureOutcome(
            string token,
            IEnumerable<string>? multipleMatches,
            bool invalidNumericId,
            TSItemRefAttribute attribute) {
            if (multipleMatches is not null) {
                return attribute.FailureMode is TSItemFailureMode.LegacyItemBan or TSItemFailureMode.LegacyItemCommand
                    ? BuildMultipleMatchOutcome(multipleMatches)
                    : CommandOutcome.Error(GetString("Multiple items matched: {0}", string.Join(", ", multipleMatches
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                        .Take(8))));
            }

            if (attribute.FailureMode == TSItemFailureMode.LegacyItemBan) {
                return BuildLookupFailureOutcome(
                    token: token,
                    attribute: attribute,
                    propertyName: nameof(TSItemRefAttribute.InvalidItemMessage),
                    customMessage: attribute.InvalidItemMessage,
                    defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid item.")));
            }

            if (attribute.FailureMode == TSItemFailureMode.LegacyItemCommand) {
                return BuildLookupFailureOutcome(
                    token: token,
                    attribute: attribute,
                    propertyName: nameof(TSItemRefAttribute.InvalidItemMessage),
                    customMessage: attribute.InvalidItemMessage,
                    defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid item type!")));
            }

            return invalidNumericId
                ? BuildLookupFailureOutcome(
                    token: token,
                    attribute: attribute,
                    propertyName: nameof(TSItemRefAttribute.InvalidItemMessage),
                    customMessage: attribute.InvalidItemMessage,
                    defaultOutcome: static _ => CommandOutcome.Error(GetString("Invalid item type!")))
                : BuildLookupFailureOutcome(
                    token: token,
                    attribute: attribute,
                    propertyName: nameof(TSItemRefAttribute.InvalidItemMessage),
                    customMessage: attribute.InvalidItemMessage,
                    defaultOutcome: static searchToken => CommandOutcome.Error(GetString("Unable to find any items named \"{0}\"", searchToken)));
        }

        private static string ResolvePlayerMatchDisplay(TSPlayer player, TSPlayerRefAttribute attribute) {
            return attribute.MultipleMatchDisplay == TSPlayerMatchDisplay.AccountNameOrName
                ? player.Account?.Name ?? player.Name
                : player.Name;
        }

        private static TSPlayer? ResolveExcludedCurrentPlayer(CommandParamBindingContext context) {
            if ((context.Modifiers & CommandParamModifiers.ExcludeCurrentContext) == 0) {
                return null;
            }

            return context.InvocationContext.ExecutionContext is TSExecutionContext tsContext
                ? tsContext.Player
                : null;
        }

        private static bool IsCurrentPlayerToken(string token, TSPlayer player) {
            if (int.TryParse(token, out var playerIndex) && playerIndex == player.Index) {
                return true;
            }

            return player.Name.Equals(token, StringComparison.OrdinalIgnoreCase);
        }
    }
}
