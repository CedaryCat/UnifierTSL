using System.Collections.Immutable;
using System.Globalization;
using Terraria;
using Terraria.ID;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Commanding.Prompting;
using UnifierTSL.Servers;

namespace UnifierTSL.Commanding.Bindings
{
    public static class CommandCommonParameterRules
    {
        public static void Configure(ICommandBindingRegistry builder) {

            builder.AddBindingRule<PlayerSelectorAttribute, CommandPlayerSelector>(
                BindPlayerSelector,
                static _ => CommonParamPrompts.PlayerRef,
                supportedModifiers: CommandParamModifiers.All | CommandParamModifiers.ServerScope,
                defaultName: "player");
            builder.AddBindingRule<ItemRefAttribute, int>(
                BindItemId,
                ResolveItemPrompt,
                defaultName: "item");
            builder.AddBindingRule<ItemRefAttribute, int?>(
                BindNullableItemId,
                ResolveItemPrompt,
                defaultName: "item");
            builder.AddBindingRule<BuffRefAttribute, int>(
                BindBuffId,
                static _ => CommonParamPrompts.BuffRef,
                defaultName: "buff");
            builder.AddBindingRule<BuffRefAttribute, int?>(
                BindNullableBuffId,
                static _ => CommonParamPrompts.BuffRef,
                defaultName: "buff");
            builder.AddBindingRule<PrefixRefAttribute, int>(
                BindPrefixId,
                static _ => CommonParamPrompts.PrefixRef,
                defaultName: "prefix");
            builder.AddBindingRule<PrefixRefAttribute, int?>(
                BindNullablePrefixId,
                static _ => CommonParamPrompts.PrefixRef,
                defaultName: "prefix");
            builder.AddBindingRule<Int32ValueAttribute, int>(
                BindInt32Value,
                static _ => CommonParamPrompts.IntegerValue);
            builder.AddBindingRule<Int32ValueAttribute, int?>(
                BindNullableInt32Value,
                static _ => CommonParamPrompts.IntegerValue);
            builder.AddBindingRule<BooleanValueAttribute, bool>(
                BindBooleanValue,
                static _ => CommonParamPrompts.BooleanValue);
            builder.AddBindingRule<BooleanValueAttribute, bool?>(
                BindNullableBooleanValue,
                static _ => CommonParamPrompts.BooleanValue);
            builder.AddBindingRule<ItemAmountAttribute, int>(
                BindItemAmount,
                static _ => CommonParamPrompts.IntegerValue);
            builder.AddBindingRule<ItemAmountAttribute, int?>(
                BindNullableItemAmount,
                static _ => CommonParamPrompts.IntegerValue);
            builder.AddBindingRule<RemainingArgsAttribute, string[]>(
                BindRemainingArgs,
                static _ => CommonParamPrompts.FreeTextRemaining,
                defaultName: "args");
        }

        private static CommandParamPromptData ResolveItemPrompt(ItemRefAttribute attribute) {
            return CommonParamPrompts.ItemRef with {
                RouteConsumptionMode = attribute.ConsumptionMode == ItemConsumptionMode.GreedyPhrase
                    ? CommandPromptRoutes.GreedyPhrase
                    : CommandPromptRoutes.SingleToken,
            };
        }

        private static CommandParamBindingResult BindPlayerSelector(
            CommandParamBindingContext context,
            PlayerSelectorAttribute attribute) {
            _ = attribute;

            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var token = context.UserArguments[context.UserIndex].Trim();
            if (token.Length == 0) {
                return CommandParamBindingResult.Mismatch();
            }

            if (token.Equals("*", StringComparison.OrdinalIgnoreCase)) {
                if ((context.Modifiers & CommandParamModifiers.All) == 0) {
                    return CommandParamBindingResult.Mismatch();
                }

                if (!TryResolveScope(context, out var scope, out var failure)) {
                    return CommandParamBindingResult.Failure(failure!);
                }

                return CommandParamBindingResult.Success(new CommandPlayerSelector {
                    IsAll = true,
                    Scope = scope,
                });
            }

            if (!TryResolveScope(context, out var resolvedScope, out var scopeFailure)) {
                return CommandParamBindingResult.Failure(scopeFailure!);
            }

            List<Player> exactCaseSensitiveMatches = [.. EnumeratePlayers(resolvedScope)
                .Where(player => player.name.Equals(token, StringComparison.Ordinal))];
            if (exactCaseSensitiveMatches.Count == 1) {
                return CommandParamBindingResult.Success(new CommandPlayerSelector {
                    IsAll = false,
                    Single = exactCaseSensitiveMatches[0],
                    Scope = resolvedScope,
                });
            }

            var exactMatches = exactCaseSensitiveMatches.Count > 1
                ? exactCaseSensitiveMatches
                : [.. EnumeratePlayers(resolvedScope)
                    .Where(player => player.name.Equals(token, StringComparison.OrdinalIgnoreCase))];
            if (exactMatches.Count == 1) {
                return CommandParamBindingResult.Success(new CommandPlayerSelector {
                    IsAll = false,
                    Single = exactMatches[0],
                    Scope = resolvedScope,
                });
            }

            var matches = exactMatches.Count > 1
                ? exactMatches
                : [.. EnumeratePlayers(resolvedScope)
                    .Where(player => player.name.StartsWith(token, StringComparison.OrdinalIgnoreCase))];
            if (matches.Count == 0) {
                return CommandParamBindingResult.Failure(CommandOutcome.Error(GetString("Invalid player.")));
            }

            if (matches.Count > 1) {
                var preview = string.Join(", ", matches
                    .Select(static player => player.name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .Take(8));
                return CommandParamBindingResult.Failure(CommandOutcome.Error(GetString("Multiple players matched: {0}", preview)));
            }

            return CommandParamBindingResult.Success(new CommandPlayerSelector {
                IsAll = false,
                Single = matches[0],
                Scope = resolvedScope,
            });
        }

        private static CommandParamBindingResult BindItemId(
            CommandParamBindingContext context,
            ItemRefAttribute attribute) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            return attribute.ConsumptionMode == ItemConsumptionMode.GreedyPhrase
                ? BindGreedyItemId(context)
                : BindSingleItemId(
                    context.UserArguments[context.UserIndex].Trim(),
                    consumedTokens: 1);
        }

        private static CommandParamBindingResult BindNullableItemId(
            CommandParamBindingContext context,
            ItemRefAttribute attribute) {
            return LiftNullableValue<int>(BindItemId(context, attribute));
        }

        private static CommandParamBindingResult BindBuffId(
            CommandParamBindingContext context,
            BuffRefAttribute attribute) {
            _ = attribute;

            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var token = context.UserArguments[context.UserIndex].Trim();
            if (token.Length == 0) {
                return CommandParamBindingResult.Mismatch();
            }

            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var buffId)) {
                if (buffId < 1 || buffId >= BuffID.Count) {
                    return CommandParamBindingResult.Failure(
                        CommandOutcome.Error(GetString("\"{0}\" is not a valid buff ID!", token)),
                        consumedTokens: 1);
                }

                return CommandParamBindingResult.Success(buffId);
            }

            var matches = CommandPromptCommonObjects.ResolveBuffIds(token);
            if (matches.Count == 0) {
                return CommandParamBindingResult.Failure(CommandOutcome.Error(
                    GetString("Unable to find any buffs named \"{0}\"", token)),
                    consumedTokens: 1);
            }

            if (matches.Count > 1) {
                var preview = string.Join(", ", matches
                    .Select(static id => CommandPromptCommonObjects.GetBuffDisplayName(id) ?? id.ToString(CultureInfo.InvariantCulture))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .Take(8));
                return CommandParamBindingResult.Failure(CommandOutcome.Error(
                    GetString("Multiple buffs matched: {0}", preview)),
                    consumedTokens: 1);
            }

            return CommandParamBindingResult.Success(matches[0]);
        }

        private static CommandParamBindingResult BindNullableBuffId(
            CommandParamBindingContext context,
            BuffRefAttribute attribute) {
            return LiftNullableValue<int>(BindBuffId(context, attribute));
        }

        private static CommandParamBindingResult BindPrefixId(
            CommandParamBindingContext context,
            PrefixRefAttribute attribute) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var token = context.UserArguments[context.UserIndex].Trim();
            if (token.Length == 0) {
                return CommandParamBindingResult.Mismatch();
            }

            var matches = CommandPromptCommonObjects.ResolvePrefixIds(token);
            if (matches.Count == 0) {
                return attribute.ResolveMode == PrefixResolveMode.Lenient
                    ? CommandParamBindingResult.Success(0)
                    : CommandParamBindingResult.Failure(CommandOutcome.Error(
                        GetString("No prefix matched \"{0}\".", token)));
            }

            if (matches.Count > 1) {
                if (attribute.ResolveMode == PrefixResolveMode.Lenient) {
                    return CommandParamBindingResult.Success(0);
                }

                var preview = string.Join(", ", matches
                    .Select(static id => CommandPromptCommonObjects.GetPrefixDisplayName(id) ?? id.ToString(CultureInfo.InvariantCulture))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .Take(8));
                return CommandParamBindingResult.Failure(CommandOutcome.Error(
                    GetString("Multiple prefixes matched: {0}", preview)));
            }

            return CommandParamBindingResult.Success(matches[0]);
        }

        private static CommandParamBindingResult BindNullablePrefixId(
            CommandParamBindingContext context,
            PrefixRefAttribute attribute) {
            return LiftNullableValue<int>(BindPrefixId(context, attribute));
        }

        private static CommandParamBindingResult BindInt32Value(
            CommandParamBindingContext context,
            Int32ValueAttribute attribute) {
            return BindInt32Core(context, attribute, nullable: false);
        }

        private static CommandParamBindingResult BindNullableInt32Value(
            CommandParamBindingContext context,
            Int32ValueAttribute attribute) {
            return BindInt32Core(context, attribute, nullable: true);
        }

        private static CommandParamBindingResult BindBooleanValue(
            CommandParamBindingContext context,
            BooleanValueAttribute attribute) {
            return BindBooleanCore(context, attribute, nullable: false);
        }

        private static CommandParamBindingResult BindNullableBooleanValue(
            CommandParamBindingContext context,
            BooleanValueAttribute attribute) {
            return BindBooleanCore(context, attribute, nullable: true);
        }

        private static CommandParamBindingResult BindItemAmount(
            CommandParamBindingContext context,
            ItemAmountAttribute attribute) {
            return BindItemAmountCore(context, attribute, nullable: false);
        }

        private static CommandParamBindingResult BindNullableItemAmount(
            CommandParamBindingContext context,
            ItemAmountAttribute attribute) {
            return BindItemAmountCore(context, attribute, nullable: true);
        }

        private static CommandParamBindingResult BindRemainingArgs(
            CommandParamBindingContext context,
            RemainingArgsAttribute attribute) {
            _ = attribute;

            string[] remaining = [.. context.UserArguments.Skip(context.UserIndex)];
            return CommandParamBindingResult.Success(remaining, consumedTokens: remaining.Length);
        }

        private static CommandParamBindingResult LiftNullableValue<T>(CommandParamBindingResult result)
            where T : struct {
            return !result.IsSuccess
                ? result
                : CommandParamBindingResult.SuccessMany(
                    result.Candidates.Select(static candidate =>
                        new BindingCandidate((T?)candidate.Value!, candidate.ConsumedTokens)),
                    result.FailureOutcome,
                    result.FailureConsumedTokens);
        }

        private static CommandParamBindingResult BindInt32Core(
            CommandParamBindingContext context,
            Int32ValueAttribute attribute,
            bool nullable) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var raw = context.UserArguments[context.UserIndex].Trim();
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) {
                return attribute.InvalidTokenBehavior switch {
                    InvalidTokenBehavior.UseDefault when context.Parameter.DefaultValue is not null =>
                        CommandParamBindingResult.Success(context.Parameter.DefaultValue),
                    _ => Failure(attribute, nameof(Int32ValueAttribute.InvalidTokenMessage), attribute.InvalidTokenMessage, raw),
                };
            }

            if (value < attribute.Minimum || value > attribute.Maximum) {
                return attribute.OutOfRangeBehavior switch {
                    OutOfRangeBehavior.UseDefault when context.Parameter.DefaultValue is not null =>
                        CommandParamBindingResult.Success(context.Parameter.DefaultValue),
                    OutOfRangeBehavior.Clamp => CommandParamBindingResult.Success(nullable
                        ? (int?)Math.Clamp(value, attribute.Minimum, attribute.Maximum)
                        : Math.Clamp(value, attribute.Minimum, attribute.Maximum)),
                    _ => attribute.OutOfRangeMessage is null
                        ? Failure(attribute, nameof(Int32ValueAttribute.InvalidTokenMessage), attribute.InvalidTokenMessage, raw)
                        : Failure(attribute, nameof(Int32ValueAttribute.OutOfRangeMessage), attribute.OutOfRangeMessage, raw),
                };
            }

            return CommandParamBindingResult.Success(nullable ? (int?)value : value);
        }

        private static CommandParamBindingResult BindBooleanCore(
            CommandParamBindingContext context,
            BooleanValueAttribute attribute,
            bool nullable) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var raw = context.UserArguments[context.UserIndex].Trim();
            if (!TryParseBoolean(raw, out var value)) {
                return Failure(attribute, nameof(BooleanValueAttribute.InvalidTokenMessage), attribute.InvalidTokenMessage, raw);
            }

            return CommandParamBindingResult.Success(nullable ? (bool?)value : value);
        }

        private static bool TryResolveScope(
            CommandParamBindingContext context,
            out ServerContext? scope,
            out CommandOutcome? failure) {
            scope = null;
            failure = null;

            if ((context.Modifiers & CommandParamModifiers.ServerScope) == 0) {
                return true;
            }

            scope = context.InvocationContext.Server;
            if (scope is not null) {
                return true;
            }

            failure = CommandOutcome.Error(GetString("This command requires a specific server context."));
            return false;
        }

        private static IEnumerable<Player> EnumeratePlayers(ServerContext? scope) {
            for (var i = 0; i < UnifiedServerCoordinator.globalClients.Length; i++) {
                var client = UnifiedServerCoordinator.globalClients[i];
                if (!client.IsActive) {
                    continue;
                }

                var currentServer = UnifiedServerCoordinator.GetClientCurrentlyServer(i);
                if (scope is not null && !ReferenceEquals(currentServer, scope)) {
                    continue;
                }

                var player = UnifiedServerCoordinator.GetPlayer(i);
                if (string.IsNullOrWhiteSpace(player.name)) {
                    continue;
                }

                yield return player;
            }
        }

        private static CommandParamBindingResult BindGreedyItemId(CommandParamBindingContext context) {
            var maxSpan = GetGreedyItemLimit(context.UserArguments, context.UserIndex);
            if (maxSpan <= 0) {
                return CommandParamBindingResult.Mismatch();
            }

            List<BindingCandidate> candidates = [];
            CommandOutcome? fallbackFailure = null;
            var fallbackConsumedTokens = 0;

            for (var tokenCount = maxSpan; tokenCount >= 1; tokenCount--) {
                var search = string.Join(' ', context.UserArguments.Skip(context.UserIndex).Take(tokenCount)).Trim();
                var result = BindSingleItemId(search, tokenCount);
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

        private static CommandParamBindingResult BindSingleItemId(string token, int consumedTokens) {
            if (token.Length == 0) {
                return CommandParamBindingResult.Mismatch();
            }

            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)) {
                if (itemId < 1 || itemId >= ItemID.Count) {
                    return CommandParamBindingResult.Failure(
                        CommandOutcome.Error(GetString("Invalid item type!")),
                        consumedTokens);
                }

                return CommandParamBindingResult.Success(itemId, consumedTokens);
            }

            var matches = CommandPromptCommonObjects.ResolveItemIds(token);
            if (matches.Count == 0) {
                return CommandParamBindingResult.Failure(CommandOutcome.Error(
                    GetString("Unable to find any items named \"{0}\"", token)),
                    consumedTokens);
            }

            if (matches.Count > 1) {
                var preview = string.Join(", ", matches
                    .Select(static id => CommandPromptCommonObjects.GetItemDisplayName(id) ?? id.ToString(CultureInfo.InvariantCulture))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .Take(8));
                return CommandParamBindingResult.Failure(CommandOutcome.Error(
                    GetString("Multiple items matched: {0}", preview)),
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

        private static CommandParamBindingResult BindItemAmountCore(
            CommandParamBindingContext context,
            ItemAmountAttribute attribute,
            bool nullable) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var raw = context.UserArguments[context.UserIndex].Trim();
            var defaultValue = GetItemAmountDefault(context, attribute, nullable);
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount)) {
                return attribute.InvalidTokenBehavior switch {
                    InvalidTokenBehavior.UseDefault => CommandParamBindingResult.Success(defaultValue),
                    _ => CommandParamBindingResult.Failure(CommandOutcome.Usage(GetString("Invalid command syntax."))),
                };
            }

            if (!TryGetMaxStack(context, attribute, out var maxStack)) {
                maxStack = 0;
            }

            var treatAsDefault = attribute.TreatZeroAsDefault && amount == 0;
            var overflow = maxStack > 0 && amount > maxStack;
            if (!treatAsDefault && !overflow) {
                return CommandParamBindingResult.Success(nullable ? (int?)amount : amount);
            }

            if (treatAsDefault) {
                return attribute.OutOfRangeBehavior switch {
                    OutOfRangeBehavior.UseDefault => CommandParamBindingResult.Success(defaultValue),
                    OutOfRangeBehavior.Clamp when maxStack > 0 =>
                        CommandParamBindingResult.Success(nullable ? (int?)maxStack : maxStack),
                    _ => CommandParamBindingResult.Failure(CommandOutcome.Usage(GetString("Invalid command syntax."))),
                };
            }

            return attribute.OutOfRangeBehavior switch {
                OutOfRangeBehavior.Clamp when maxStack > 0 =>
                    CommandParamBindingResult.Success(nullable ? (int?)maxStack : maxStack),
                OutOfRangeBehavior.UseDefault => CommandParamBindingResult.Success(defaultValue),
                _ => CommandParamBindingResult.Failure(CommandOutcome.Usage(GetString("Invalid command syntax."))),
            };
        }

        private static object? GetItemAmountDefault(
            CommandParamBindingContext context,
            ItemAmountAttribute attribute,
            bool nullable) {
            var value = attribute.DefaultSource switch {
                ItemAmountDefaultSource.ItemMaxStack when TryGetMaxStack(context, attribute, out var maxStack)
                    => maxStack,
                _ => context.Parameter.DefaultValue,
            };

            if (value is null) {
                value = nullable ? (int?)0 : 0;
            }

            if (nullable && value is int defaultInt) {
                return (int?)defaultInt;
            }

            return value;
        }

        private static bool TryGetMaxStack(
            CommandParamBindingContext context,
            ItemAmountAttribute attribute,
            out int maxStack) {
            maxStack = 0;
            if (!TryGetItemId(context, attribute, out var itemId)) {
                return false;
            }

            var resolved = CommandPromptCommonObjects.GetItemMaxStack(itemId);
            if (resolved is null || resolved <= 0) {
                return false;
            }

            maxStack = resolved.Value;
            return true;
        }

        private static bool TryGetItemId(
            CommandParamBindingContext context,
            ItemAmountAttribute attribute,
            out int itemId) {
            itemId = 0;

            foreach (var bound in context.BoundParameters.Reverse()) {
                if (attribute.ItemParameterName is not null
                    && !string.Equals(
                        bound.Parameter.Name,
                        attribute.ItemParameterName,
                        StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                if (attribute.ItemParameterName is null
                    && bound.Parameter.SemanticKey != CommandPromptParamKeys.ItemRef) {
                    continue;
                }

                if (bound.Value is int typedItemId) {
                    itemId = typedItemId;
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseBoolean(string raw, out bool value) {
            if (bool.TryParse(raw, out value)) {
                return true;
            }

            switch (raw.Trim().ToLowerInvariant()) {
                case "on":
                case "yes":
                case "y":
                case "1":
                    value = true;
                    return true;
                case "off":
                case "no":
                case "n":
                case "0":
                    value = false;
                    return true;
                default:
                    value = false;
                    return false;
            }
        }

        private static CommandParamBindingResult Failure(Attribute attribute, string propertyName, string memberName, string rawToken) {
            return CommandParamBindingResult.Failure(CommandOutcome.Error(
                CommandAttributeText.Invoke(attribute, propertyName, memberName, rawToken)));
        }
    }
}
