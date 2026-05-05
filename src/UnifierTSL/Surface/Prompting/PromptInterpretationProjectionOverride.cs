using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Contracts.Display;

namespace UnifierTSL.Surface.Prompting {
    public sealed class PromptInterpretationProjectionOverride {
        public PromptInterpretationPresentation Presentation { get; init; } = new();
        public string ActiveInterpretationId { get; init; } = string.Empty;
        public InlineSegments Summary { get; init; } = new();
        public InlineSegments[] DetailLines { get; init; } = [];
        public InlineInterpretationOption[] Options { get; init; } = [];
    }
}
