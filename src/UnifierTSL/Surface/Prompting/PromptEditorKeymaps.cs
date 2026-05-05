using System.Collections.Immutable;
using System.Diagnostics;
using UnifierTSL.Contracts.Terminal;
using static UnifierTSL.I18n;

namespace UnifierTSL.Surface.Prompting
{
    internal static class PromptEditorKeymaps
    {
        public static EditorKeymap CreateSingleLine() {
            return new EditorKeymap {
                Submit = [Chord(ConsoleKey.Enter)],
                ManualCompletion = [Chord(ConsoleKey.Tab)],
                AcceptPreview = [Chord(ConsoleKey.RightArrow)],
                NextCompletion = [Chord(ConsoleKey.Tab), Chord(ConsoleKey.DownArrow)],
                PrevCompletion = [Chord(ConsoleKey.UpArrow)],
                DismissAssist = [Chord(ConsoleKey.Escape)],
                NextInterpretation = [Chord(ConsoleKey.Tab, ConsoleModifiers.Shift)],
                PrevActivity = [Chord(ConsoleKey.UpArrow, ConsoleModifiers.Shift)],
                NextActivity = [Chord(ConsoleKey.DownArrow, ConsoleModifiers.Shift)],
                ScrollStatusUp = [Chord(ConsoleKey.UpArrow, ConsoleModifiers.Control)],
                ScrollStatusDown = [Chord(ConsoleKey.DownArrow, ConsoleModifiers.Control)],
            };
        }

        public static EditorKeymap CreateMultiLine() {
            return new EditorKeymap {
                Submit = [Chord(ConsoleKey.Enter)],
                NewLine = [
                    Chord(ConsoleKey.Enter, ConsoleModifiers.Shift),
                    Chord(ConsoleKey.Enter, ConsoleModifiers.Control),
                ],
                ManualCompletion = [Chord(ConsoleKey.Tab)],
                AcceptPreview = [Chord(ConsoleKey.RightArrow)],
                NextCompletion = [Chord(ConsoleKey.Tab), Chord(ConsoleKey.DownArrow)],
                PrevCompletion = [Chord(ConsoleKey.UpArrow)],
                DismissAssist = [Chord(ConsoleKey.Escape)],
                NextInterpretation = [Chord(ConsoleKey.Tab, ConsoleModifiers.Shift)],
                PrevActivity = [Chord(ConsoleKey.UpArrow, ConsoleModifiers.Shift)],
                NextActivity = [Chord(ConsoleKey.DownArrow, ConsoleModifiers.Shift)],
                ScrollStatusUp = [Chord(ConsoleKey.UpArrow, ConsoleModifiers.Control)],
                ScrollStatusDown = [Chord(ConsoleKey.DownArrow, ConsoleModifiers.Control)],
            };
        }

        public static ImmutableArray<string> CreateCommandStatusBodyLines(
            bool multiLine,
            bool activityActive,
            bool includeInterpretationSwitchHint = true) {
            var keymap = multiLine ? CreateMultiLine() : CreateSingleLine();
            return [
                CreateCommandAssistHintLine(keymap, includeInterpretationSwitchHint),
                CreateCommandStatusActionLine(keymap, multiLine, activityActive),
            ];
        }

        public static string CreateCommandAssistHintLine(bool includeInterpretationSwitchHint) {
            return CreateCommandAssistHintLine(CreateSingleLine(), includeInterpretationSwitchHint);
        }

        private static string CreateCommandAssistHintLine(EditorKeymap keymap, bool includeInterpretationSwitchHint) {
            List<string> parts = [GetString("{0} rotates suggestions", FormatFirst(keymap.ManualCompletion))];
            if (includeInterpretationSwitchHint) {
                parts.Add(GetString("{0} switches interpretation", FormatFirst(keymap.NextInterpretation)));
            }

            parts.Add(GetString("{0} accepts ghost completion", FormatFirst(keymap.AcceptPreview)));
            return string.Join("; ", parts);
        }

        private static string CreateCommandStatusActionLine(EditorKeymap keymap, bool multiLine, bool activityActive) {
            List<string> parts = [GetString("{0}/{1} scroll status", FormatFirst(keymap.ScrollStatusUp), FormatFirst(keymap.ScrollStatusDown))];
            if (activityActive) {
                parts.Add(GetString("{0}/{1} select task", FormatFirst(keymap.PrevActivity), FormatFirst(keymap.NextActivity)));
            }

            if (multiLine) {
                parts.Add(GetString("{0} submits", FormatFirst(keymap.Submit)));
                parts.Add(GetString("{0} inserts newline", FormatFirst(keymap.NewLine)));
            }

            return string.Join("; ", parts);
        }

        public static KeyChord Chord(ConsoleKey key, ConsoleModifiers modifiers = 0) {
            return new KeyChord {
                Key = key,
                Modifiers = modifiers,
            };
        }

        public static string FormatFirst(IReadOnlyList<KeyChord>? chords) {
            return chords is { Count: > 0 }
                ? Format(chords[0])
                : "(unbound)";
        }

        public static string Format(KeyChord chord) {
            List<string> parts = [];
            if ((chord.Modifiers & ConsoleModifiers.Control) != 0) {
                parts.Add("Ctrl");
            }

            if ((chord.Modifiers & ConsoleModifiers.Shift) != 0) {
                parts.Add("Shift");
            }

            if ((chord.Modifiers & ConsoleModifiers.Alt) != 0) {
                parts.Add("Alt");
            }

            parts.Add(chord.Key switch {
                ConsoleKey.RightArrow => "Right",
                ConsoleKey.LeftArrow => "Left",
                ConsoleKey.UpArrow => "Up",
                ConsoleKey.DownArrow => "Down",
                ConsoleKey.Spacebar => "Space",
                _ => chord.Key.ToString(),
            });
            return string.Join("+", parts);
        }
    }
}
