using Terraria;
using Terraria.ID;
using TShockAPI.Localization;
using UnifierTSL.Commanding;

namespace TShockAPI.Commanding.V2
{
    [CommandController("item", Summary = nameof(ControllerSummary))]
    [Aliases("i")]
    internal static class ItemCommand
    {
        private static string ItemInvalidItemMessage => GetString("Invalid item type!");
        private static string ItemInvalidItemMessage2 => GetString("Invalid item type!");
        private static string ItemInvalidItemMessage3 => GetString("Invalid item type!");

        private static string ControllerSummary => GetString("Gives yourself an item.");
        private static string ExecuteSummary => GetString("Gives yourself an item by id or name.");
        private static string ExecuteAmountSummary => GetString("Gives yourself a stack of an item by id or name.");
        private static string ExecuteAmountPrefixSummary => GetString("Gives yourself a prefixed stack of an item by id or name.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.item), PlayerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [TSItemRef(
                nameof(ItemInvalidItemMessage),
                ConsumptionMode = ItemConsumptionMode.GreedyPhrase,
                LookupMode = TSItemLookupMode.LegacyCommand,
                FailureMode = TSItemFailureMode.LegacyItemCommand)]
            int item) {

            var itemObj = Utils.GetItemById(item);
            return CommandHelpers.GrantItemToSelf(context.Player!, itemObj, itemObj.maxStack, prefixId: 0);
        }

        [CommandAction(Summary = nameof(ExecuteAmountSummary))]
        [TShockCommand(nameof(Permissions.item), PlayerScope = true)]
        public static CommandOutcome ExecuteAmount(
            [FromAmbientContext] TSExecutionContext context,
            [TSItemRef(
                nameof(ItemInvalidItemMessage2),
                ConsumptionMode = ItemConsumptionMode.GreedyPhrase,
                LookupMode = TSItemLookupMode.LegacyCommand,
                FailureMode = TSItemFailureMode.LegacyItemCommand)]
            int item,
            [ItemAmount(
                // Keep invalid amount tokens failing here. Together with the greedy item binder's
                // longest-phrase fallback and dispatcher best-failure ranking, this lets
                // `/item dirt warding` surface the legacy "invalid item" path instead of
                // silently treating `warding` as a default stack amount.
                InvalidTokenBehavior = InvalidTokenBehavior.Fail,
                OutOfRangeBehavior = OutOfRangeBehavior.UseDefault,
                DefaultSource = ItemAmountDefaultSource.ItemMaxStack,
                TreatZeroAsDefault = true)]
            int itemAmount) {
            return CommandHelpers.GrantItemToSelf(context.Player!, Utils.GetItemById(item), itemAmount, prefixId: 0);
        }

        [CommandAction(Summary = nameof(ExecuteAmountPrefixSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.item), PlayerScope = true)]
        public static CommandOutcome ExecuteAmountPrefix(
            [FromAmbientContext] TSExecutionContext context,
            [TSItemRef(
                nameof(ItemInvalidItemMessage3),
                ConsumptionMode = ItemConsumptionMode.GreedyPhrase,
                LookupMode = TSItemLookupMode.LegacyCommand,
                FailureMode = TSItemFailureMode.LegacyItemCommand)]
            int item,
            [ItemAmount(
                // Keep invalid amount tokens failing here. Together with the greedy item binder's
                // longest-phrase fallback and dispatcher best-failure ranking, this lets
                // `/item dirt foo 5` fall back to the legacy "invalid item name" path instead
                // of letting the shorter `dirt` candidate swallow `foo` as a default amount.
                InvalidTokenBehavior = InvalidTokenBehavior.Fail,
                OutOfRangeBehavior = OutOfRangeBehavior.UseDefault,
                DefaultSource = ItemAmountDefaultSource.ItemMaxStack,
                TreatZeroAsDefault = true)]
            int itemAmount,
            [PrefixRef(Name = "prefix")] int prefixId) {

            var itemObj = Utils.GetItemById(item);
            return CommandHelpers.GrantItemToSelf(context.Player!, itemObj, itemAmount, CommandHelpers.NormalizeResolvedPrefix(itemObj, prefixId));
        }
    }

    [CommandController("give", Summary = nameof(ControllerSummary))]
    [Aliases("g")]
    internal static class GiveCommand
    {
        private static string ExecuteErrorMessage => GetString("Missing item name/id.");
        private static string ExecuteErrorMessage2 => GetString("Missing player name.");
        private static string ItemInvalidItemMessage => GetString("Invalid item type!");
        private static string TargetInvalidPlayerMessage => GetString("Invalid player!");
        private static string ExecuteAmountErrorMessage => GetString("Missing item name/id.");
        private static string ExecuteAmountErrorMessage2 => GetString("Missing player name.");
        private static string ItemInvalidItemMessage2 => GetString("Invalid item type!");
        private static string TargetInvalidPlayerMessage2 => GetString("Invalid player!");
        private static string ExecuteAmountPrefixErrorMessage => GetString("Missing item name/id.");
        private static string ExecuteAmountPrefixErrorMessage2 => GetString("Missing player name.");
        private static string ItemInvalidItemMessage3 => GetString("Invalid item type!");
        private static string TargetInvalidPlayerMessage3 => GetString("Invalid player!");
        private static string ExecuteIgnoringUnsupportedTailErrorMessage => GetString("Missing item name/id.");
        private static string ExecuteIgnoringUnsupportedTailErrorMessage2 => GetString("Missing player name.");
        private static string ItemInvalidItemMessage4 => GetString("Invalid item type!");
        private static string TargetInvalidPlayerMessage4 => GetString("Invalid player!");

        private static string ControllerSummary => GetString("Gives another player an item.");
        private static string ExecuteSummary => GetString("Gives another player an item by id or name.");
        private static string ExecuteAmountSummary => GetString("Gives another player a stack of an item by id or name.");
        private static string ExecuteAmountPrefixSummary => GetString("Gives another player a prefixed stack of an item by id or name.");
        private static string ExecuteIgnoringUnsupportedTailSummary => GetString("Ignores unsupported trailing arguments and falls back to the base give behavior.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [RequireUserArgumentCount(2)]
        [RequireNonEmptyUserArgumentPreBind(0, nameof(ExecuteErrorMessage))]
        [RequireNonEmptyUserArgumentPreBind(1, nameof(ExecuteErrorMessage2))]
        [TShockCommand(nameof(Permissions.give))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [TSItemRef(
                nameof(ItemInvalidItemMessage),
                LookupMode = TSItemLookupMode.LegacyCommand,
                FailureMode = TSItemFailureMode.LegacyItemCommand)]
            int item,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage))] TSPlayer target) {

            var itemObj = Utils.GetItemById(item);
            return CommandHelpers.GiveItemToTarget(context.Executor, target, itemObj, itemObj.maxStack, prefixId: 0);
        }

        [CommandAction(Summary = nameof(ExecuteAmountSummary))]
        [RequireUserArgumentCount(3)]
        [RequireNonEmptyUserArgumentPreBind(0, nameof(ExecuteAmountErrorMessage))]
        [RequireNonEmptyUserArgumentPreBind(1, nameof(ExecuteAmountErrorMessage2))]
        [TShockCommand(nameof(Permissions.give))]
        public static CommandOutcome ExecuteAmount(
            [FromAmbientContext] TSExecutionContext context,
            [TSItemRef(
                nameof(ItemInvalidItemMessage2),
                LookupMode = TSItemLookupMode.LegacyCommand,
                FailureMode = TSItemFailureMode.LegacyItemCommand)]
            int item,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage2))] TSPlayer target,
            [ItemAmount(
                InvalidTokenBehavior = InvalidTokenBehavior.UseDefault,
                OutOfRangeBehavior = OutOfRangeBehavior.UseDefault,
                DefaultSource = ItemAmountDefaultSource.ItemMaxStack,
                TreatZeroAsDefault = true)]
            int itemAmount) {

            var itemObj = Utils.GetItemById(item);
            return CommandHelpers.GiveItemToTarget(context.Executor, target, itemObj, itemAmount, prefixId: 0);
        }

        [CommandAction(Summary = nameof(ExecuteAmountPrefixSummary))]
        [RequireUserArgumentCount(4)]
        [RequireNonEmptyUserArgumentPreBind(0, nameof(ExecuteAmountPrefixErrorMessage))]
        [RequireNonEmptyUserArgumentPreBind(1, nameof(ExecuteAmountPrefixErrorMessage2))]
        [TShockCommand(nameof(Permissions.give))]
        public static CommandOutcome ExecuteAmountPrefix(
            [FromAmbientContext] TSExecutionContext context,
            [TSItemRef(
                nameof(ItemInvalidItemMessage3),
                LookupMode = TSItemLookupMode.LegacyCommand,
                FailureMode = TSItemFailureMode.LegacyItemCommand)]
            int item,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage3))] TSPlayer target,
            [ItemAmount(
                InvalidTokenBehavior = InvalidTokenBehavior.UseDefault,
                OutOfRangeBehavior = OutOfRangeBehavior.UseDefault,
                DefaultSource = ItemAmountDefaultSource.ItemMaxStack,
                TreatZeroAsDefault = true)]
            int itemAmount,
            [PrefixRef(ResolveMode = PrefixResolveMode.Lenient, Name = "prefix")] int prefixId) {

            var itemObj = Utils.GetItemById(item);
            return CommandHelpers.GiveItemToTarget(
                context.Executor,
                target,
                itemObj,
                itemAmount,
                CommandHelpers.NormalizeResolvedPrefix(itemObj, prefixId));
        }

        [CommandAction(Summary = nameof(ExecuteIgnoringUnsupportedTailSummary))]
        [RequireUserArgumentCount(5, int.MaxValue)]
        [RequireNonEmptyUserArgumentPreBind(0, nameof(ExecuteIgnoringUnsupportedTailErrorMessage))]
        [RequireNonEmptyUserArgumentPreBind(1, nameof(ExecuteIgnoringUnsupportedTailErrorMessage2))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.give))]
        public static CommandOutcome ExecuteIgnoringUnsupportedTail(
            [FromAmbientContext] TSExecutionContext context,
            [TSItemRef(
                nameof(ItemInvalidItemMessage4),
                LookupMode = TSItemLookupMode.LegacyCommand,
                FailureMode = TSItemFailureMode.LegacyItemCommand)]
            int item,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage4))] TSPlayer target) {

            var itemObj = Utils.GetItemById(item);
            return CommandHelpers.GiveItemToTarget(context.Executor, target, itemObj, itemObj.maxStack, prefixId: 0);
        }
    }

    public static class CommandHelpers
    {
        public static CommandOutcome GrantItemToSelf(TSPlayer player, Item item, int itemAmount, int prefixId) {
            if (!HasInventoryCapacity(player, item)) {
                return CommandOutcome.Error(GetString("Your inventory seems full."));
            }

            if (!player.GiveItemCheck(item.type, EnglishLanguage.GetItemNameById(item.type), itemAmount, prefixId)) {
                return CommandOutcome.Error(GetString("You cannot spawn banned items."));
            }

            var displayItem = Utils.GetItemById(item.type);
            displayItem.prefix = (byte)prefixId;
            return CommandOutcome.Success(GetPluralString(
                "Gave {0} {1}.",
                "Gave {0} {1}s.",
                itemAmount,
                itemAmount,
                displayItem.AffixName()));
        }

        public static CommandOutcome GiveItemToTarget(CommandExecutor executor, TSPlayer target, Item item, int itemAmount, int prefixId) {

            if (!HasInventoryCapacity(target, item)) {
                return CommandOutcome.Error(GetString("Player does not have free slots!"));
            }

            if (!target.GiveItemCheck(item.type, EnglishLanguage.GetItemNameById(item.type), itemAmount, prefixId)) {
                return CommandOutcome.Error(GetString("You cannot spawn banned items."));
            }

            var builder = CommandOutcome.SuccessBuilder(GetPluralString(
                "Gave {0} {1} {2}.",
                "Gave {0} {1} {2}s.",
                itemAmount,
                target.Name,
                itemAmount,
                item.Name));
            builder.AddPlayerSuccess(
                target,
                GetPluralString(
                    "{0} gave you {1} {2}.",
                    "{0} gave you {1} {2}s.",
                    itemAmount,
                    executor.Name,
                    itemAmount,
                    item.Name));
            return builder.Build();
        }

        public static bool HasInventoryCapacity(TSPlayer player, Item item) {
            return player.InventorySlotAvailable
                || (item.type > 70 && item.type < 75)
                || item.ammo > 0
                || item.type == 58
                || item.type == 184;
        }

        public static int NormalizeResolvedPrefix(Item item, int prefixId) {
            List<int> prefixIds = [prefixId];
            NormalizeQuickPrefix(item, prefixIds);
            return prefixIds.Count == 0 ? 0 : prefixIds[0];
        }

        public static void NormalizeQuickPrefix(Item item, List<int> prefixIds) {
            if (item.accessory && prefixIds.Contains(PrefixID.Quick)) {
                prefixIds.Remove(PrefixID.Quick);
                prefixIds.Remove(PrefixID.Quick2);
                prefixIds.Add(PrefixID.Quick2);
            }
            else if (!item.accessory && prefixIds.Contains(PrefixID.Quick)) {
                prefixIds.Remove(PrefixID.Quick2);
            }
        }
    }
}
