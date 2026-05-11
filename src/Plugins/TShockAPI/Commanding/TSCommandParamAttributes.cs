using UnifierTSL.Commanding;

namespace TShockAPI.Commanding
{
    public enum TSPlayerMatchDisplay
    {
        Name,
        AccountNameOrName,
    }

    public enum TSPlayerConsumptionMode : byte
    {
        SingleToken,
        GreedyPhrase,
    }

    public enum TSLookupMatchMode
    {
        ExactOrUniquePrefix,
        ExactOnly,
    }

    public enum TSItemLookupMode
    {
        PromptDefault,
        LegacyCommand,
    }

    public enum TSItemFailureMode
    {
        PromptDefault,
        LegacyItemBan,
        LegacyItemCommand,
    }

    public enum TSRegionConsumptionMode : byte
    {
        SingleToken,
        GreedyPhrase,
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class TSPlayerRefAttribute(string invalidPlayerMessage = "") : CommandBindingAttribute
    {
        public string InvalidPlayerMessage { get; } = invalidPlayerMessage;

        public TSPlayerMatchDisplay MultipleMatchDisplay { get; set; } = TSPlayerMatchDisplay.Name;

        public TSPlayerConsumptionMode ConsumptionMode { get; set; } = TSPlayerConsumptionMode.SingleToken;
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class GroupRefAttribute(string invalidGroupMessage = "") : CommandBindingAttribute
    {
        public string InvalidGroupMessage { get; } = invalidGroupMessage;

        public TSLookupMatchMode LookupMode { get; set; } = TSLookupMatchMode.ExactOrUniquePrefix;
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class TSItemRefAttribute(string invalidItemMessage = "") : CommandBindingAttribute
    {
        public string InvalidItemMessage { get; } = invalidItemMessage;

        public ItemConsumptionMode ConsumptionMode { get; set; } = ItemConsumptionMode.SingleToken;

        public TSItemLookupMode LookupMode { get; set; } = TSItemLookupMode.PromptDefault;

        public TSItemFailureMode FailureMode { get; set; } = TSItemFailureMode.PromptDefault;
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class WarpRefAttribute(string invalidWarpMessage = "") : CommandBindingAttribute
    {
        public string InvalidWarpMessage { get; } = invalidWarpMessage;

        public TSLookupMatchMode LookupMode { get; set; } = TSLookupMatchMode.ExactOrUniquePrefix;
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class RegionRefAttribute(string invalidRegionMessage = "") : CommandBindingAttribute
    {
        public string InvalidRegionMessage { get; } = invalidRegionMessage;

        public TSLookupMatchMode LookupMode { get; set; } = TSLookupMatchMode.ExactOrUniquePrefix;

        public TSRegionConsumptionMode ConsumptionMode { get; set; } = TSRegionConsumptionMode.GreedyPhrase;
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class NpcRefAttribute(string invalidNpcMessage = "") : CommandBindingAttribute
    {
        public string InvalidNpcMessage { get; } = invalidNpcMessage;
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class ProjectileRefAttribute(string invalidProjectileMessage = "") : CommandBindingAttribute
    {
        public string InvalidProjectileMessage { get; } = invalidProjectileMessage;
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class TileRefAttribute(string invalidTileMessage = "") : CommandBindingAttribute
    {
        public string InvalidTileMessage { get; } = invalidTileMessage;
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class UserAccountRefAttribute(string invalidUserAccountMessage = "") : CommandBindingAttribute
    {
        public string InvalidUserAccountMessage { get; } = invalidUserAccountMessage;

        public TSLookupMatchMode LookupMode { get; set; } = TSLookupMatchMode.ExactOrUniquePrefix;
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class WorldTimeAttribute : CommandBindingAttribute { }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class RegionResizeDirectionAttribute : CommandBindingAttribute { }
}
