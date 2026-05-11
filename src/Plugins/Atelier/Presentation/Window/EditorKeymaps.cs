using UnifierTSL.Contracts.Terminal;

namespace Atelier.Presentation.Window
{
    internal enum EditorSubmitKeyMode : byte
    {
        Enter,
        CtrlEnter,
    }

    internal static class EditorKeymaps
    {
        public static EditorKeymap Create(EditorSubmitKeyMode submitKeyMode = EditorSubmitKeyMode.Enter) {
            KeyChord[] submitChords = submitKeyMode == EditorSubmitKeyMode.CtrlEnter
                ? [Chord(ConsoleKey.Enter, ConsoleModifiers.Control)]
                : [Chord(ConsoleKey.Enter)];
            KeyChord[] altSubmitChords = submitKeyMode == EditorSubmitKeyMode.CtrlEnter
                ? []
                : [Chord(ConsoleKey.Enter, ConsoleModifiers.Control)];
            KeyChord[] newLineChords = submitKeyMode == EditorSubmitKeyMode.CtrlEnter
                ? [Chord(ConsoleKey.Enter), Chord(ConsoleKey.Enter, ConsoleModifiers.Shift)]
                : [Chord(ConsoleKey.Enter, ConsoleModifiers.Shift)];
            return new EditorKeymap {
                Submit = submitChords,
                AltSubmit = altSubmitChords,
                NewLine = newLineChords,
                ManualCompletion = [Chord(ConsoleKey.Spacebar, ConsoleModifiers.Control)],
                AcceptCompletion = [Chord(ConsoleKey.Tab)],
                AcceptPreview = [Chord(ConsoleKey.Tab)],
                NextCompletion = [Chord(ConsoleKey.DownArrow)],
                PrevCompletion = [Chord(ConsoleKey.UpArrow)],
                DismissAssist = [Chord(ConsoleKey.Escape)],
                NextInterpretation = [Chord(ConsoleKey.DownArrow)],
                PrevInterpretation = [Chord(ConsoleKey.UpArrow)],
                PrevActivity = [Chord(ConsoleKey.UpArrow, ConsoleModifiers.Shift)],
                NextActivity = [Chord(ConsoleKey.DownArrow, ConsoleModifiers.Shift)],
            };
        }

        private static KeyChord Chord(ConsoleKey key, ConsoleModifiers modifiers = 0) {
            return new KeyChord {
                Key = key,
                Modifiers = modifiers,
            };
        }
    }
}
