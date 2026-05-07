using UnifierTSL.Contracts.Display;

namespace UnifierTSL.Contracts.Terminal {
    public enum EditorAuthority : byte {
        ClientBuffered,
        HostBuffered,
        Readonly,
    }

    public enum EditorPaneKind : byte {
        SingleLine,
        MultiLine,
        ReadonlyBuffer,
    }

    public enum EditorKeyDispatchPolicy : byte {
        Standard,
    }

    public enum MultilineSubmitMode : byte {
        AlwaysSubmit,
        UseReadiness,
    }

    public enum StreamChannel : byte {
        Output,
        Diagnostics,
        Transcript,
        Status,
    }

    public sealed class KeyChord {
        public ConsoleKey Key { get; init; }
        public ConsoleModifiers Modifiers { get; init; }
    }

    public sealed class EditorKeymap {
        public EditorKeyDispatchPolicy DispatchPolicy { get; init; } = EditorKeyDispatchPolicy.Standard;
        public KeyChord[] Submit { get; init; } = [];
        public KeyChord[] AltSubmit { get; init; } = [];
        public KeyChord[] NewLine { get; init; } = [];
        public KeyChord[] ManualCompletion { get; init; } = [];
        public KeyChord[] AcceptCompletion { get; init; } = [];
        public KeyChord[] AcceptPreview { get; init; } = [];
        public KeyChord[] NextCompletion { get; init; } = [];
        public KeyChord[] PrevCompletion { get; init; } = [];
        public KeyChord[] DismissAssist { get; init; } = [];
        public KeyChord[] NextInterpretation { get; init; } = [];
        public KeyChord[] PrevInterpretation { get; init; } = [];
        public KeyChord[] PrevActivity { get; init; } = [];
        public KeyChord[] NextActivity { get; init; } = [];
        public KeyChord[] ScrollStatusUp { get; init; } = [];
        public KeyChord[] ScrollStatusDown { get; init; } = [];
    }

    public sealed class EditorAuthoringBehavior {
        public bool OpensCompletionAutomatically { get; init; } = true;
        public bool CapturesRawKeys { get; init; }
        public MultilineSubmitMode MultilineSubmitMode { get; init; } = MultilineSubmitMode.AlwaysSubmit;
    }

    public static class KeyChordOps {
        public static bool ContentEquals(KeyChord? left, KeyChord? right) {
            return ReferenceEquals(left, right)
                || left is not null
                && right is not null
                && left.Key == right.Key
                && left.Modifiers == right.Modifiers;
        }

        public static bool SequenceEqual(IReadOnlyList<KeyChord>? left, IReadOnlyList<KeyChord>? right) {
            if (ReferenceEquals(left, right)) {
                return true;
            }

            if (left is null || right is null || left.Count != right.Count) {
                return false;
            }

            for (var index = 0; index < left.Count; index++) {
                if (!ContentEquals(left[index], right[index])) {
                    return false;
                }
            }

            return true;
        }
    }

    public static class EditorKeymapOps {
        public static bool ContentEquals(EditorKeymap? left, EditorKeymap? right) {
            return ReferenceEquals(left, right)
                || left is not null
                && right is not null
                && left.DispatchPolicy == right.DispatchPolicy
                && KeyChordOps.SequenceEqual(left.Submit, right.Submit)
                && KeyChordOps.SequenceEqual(left.AltSubmit, right.AltSubmit)
                && KeyChordOps.SequenceEqual(left.NewLine, right.NewLine)
                && KeyChordOps.SequenceEqual(left.ManualCompletion, right.ManualCompletion)
                && KeyChordOps.SequenceEqual(left.AcceptCompletion, right.AcceptCompletion)
                && KeyChordOps.SequenceEqual(left.AcceptPreview, right.AcceptPreview)
                && KeyChordOps.SequenceEqual(left.NextCompletion, right.NextCompletion)
                && KeyChordOps.SequenceEqual(left.PrevCompletion, right.PrevCompletion)
                && KeyChordOps.SequenceEqual(left.DismissAssist, right.DismissAssist)
                && KeyChordOps.SequenceEqual(left.NextInterpretation, right.NextInterpretation)
                && KeyChordOps.SequenceEqual(left.PrevInterpretation, right.PrevInterpretation)
                && KeyChordOps.SequenceEqual(left.PrevActivity, right.PrevActivity)
                && KeyChordOps.SequenceEqual(left.NextActivity, right.NextActivity)
                && KeyChordOps.SequenceEqual(left.ScrollStatusUp, right.ScrollStatusUp)
                && KeyChordOps.SequenceEqual(left.ScrollStatusDown, right.ScrollStatusDown);
        }
    }
}
