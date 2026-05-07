using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding.Prompting;

namespace UnifierTSL.Commanding.Bindings
{
    public static class CommonParamPrompts
    {
        public static CommandParamPromptData PlayerRef { get; } = new() {
            SemanticKey = CommandPromptParamKeys.PlayerRef,
            RouteMatchKind = CommandPromptRoutes.SemanticLookupSoft,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteSpecificity = CommandPromptRoutes.SemanticLookupSoftSpecificity,
        };

        public static CommandParamPromptData BuffRef { get; } = new() {
            SemanticKey = CommandPromptParamKeys.BuffRef,
            RouteMatchKind = CommandPromptRoutes.SemanticLookupSoft,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteSpecificity = CommandPromptRoutes.SemanticLookupSoftSpecificity,
        };

        public static CommandParamPromptData ItemRef { get; } = new() {
            SemanticKey = CommandPromptParamKeys.ItemRef,
            RouteMatchKind = CommandPromptRoutes.SemanticLookupSoft,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteSpecificity = CommandPromptRoutes.SemanticLookupSoftSpecificity,
        };

        public static CommandParamPromptData PrefixRef { get; } = new() {
            SemanticKey = CommandPromptParamKeys.PrefixRef,
            RouteMatchKind = CommandPromptRoutes.SemanticLookupSoft,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteSpecificity = CommandPromptRoutes.SemanticLookupSoftSpecificity,
        };

        public static CommandParamPromptData IntegerValue { get; } = new() {
            ValidationMode = PromptSlotValidationMode.Integer,
            RouteMatchKind = CommandPromptRoutes.Integer,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteSpecificity = CommandPromptRoutes.IntegerSpecificity,
        };

        public static CommandParamPromptData BooleanValue { get; } = new() {
            RouteMatchKind = CommandPromptRoutes.Boolean,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteSpecificity = CommandPromptRoutes.BooleanSpecificity,
        };

        public static CommandParamPromptData FreeText { get; } = new() {
            RouteMatchKind = CommandPromptRoutes.FreeText,
            RouteConsumptionMode = CommandPromptRoutes.SingleToken,
            RouteSpecificity = CommandPromptRoutes.FreeTextSpecificity,
        };

        public static CommandParamPromptData FreeTextRemaining { get; } = new() {
            RouteMatchKind = CommandPromptRoutes.FreeText,
            RouteConsumptionMode = CommandPromptRoutes.RemainingTokens,
            RouteSpecificity = CommandPromptRoutes.FreeTextSpecificity,
        };
    }
}
