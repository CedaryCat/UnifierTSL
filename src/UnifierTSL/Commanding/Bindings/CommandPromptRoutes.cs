using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;

namespace UnifierTSL.Commanding.Bindings
{
    public static class CommandPromptRoutes
    {
        public static PromptRouteMatchKind FreeText => PromptRouteMatchKind.FreeText;

        public static PromptRouteMatchKind ExactTokenSet => PromptRouteMatchKind.ExactTokenSet;

        public static PromptRouteMatchKind Integer => PromptRouteMatchKind.Integer;

        public static PromptRouteMatchKind Boolean => PromptRouteMatchKind.Boolean;

        public static PromptRouteMatchKind TimeOnly => PromptRouteMatchKind.TimeOnly;

        public static PromptRouteMatchKind SemanticLookupSoft => PromptRouteMatchKind.SemanticLookupSoft;

        public static PromptRouteMatchKind SemanticLookupExact => PromptRouteMatchKind.SemanticLookupExact;

        public static PromptRouteConsumptionMode SingleToken => PromptRouteConsumptionMode.SingleToken;

        public static PromptRouteConsumptionMode GreedyPhrase => PromptRouteConsumptionMode.GreedyPhrase;

        public static PromptRouteConsumptionMode RemainingText => PromptRouteConsumptionMode.RemainingText;

        public static PromptRouteConsumptionMode Variadic => PromptRouteConsumptionMode.Variadic;

        internal static PromptRouteConsumptionMode RemainingTokens => Variadic;

        public const int ExactTokenSetSpecificity = 500;
        public const int SemanticLookupExactSpecificity = 420;
        public const int TimeOnlySpecificity = 340;
        public const int BooleanSpecificity = 330;
        public const int IntegerSpecificity = 320;
        public const int SemanticLookupSoftSpecificity = 260;
        public const int FreeTextSpecificity = 100;

        public static bool IsSemanticLookupExact(PromptRouteMatchKind kind, int specificity) {
            return kind == PromptRouteMatchKind.SemanticLookupExact;
        }

        public static bool IsSemanticLookupSoft(PromptRouteMatchKind kind, int specificity) {
            return kind == PromptRouteMatchKind.SemanticLookupSoft;
        }
    }
}
