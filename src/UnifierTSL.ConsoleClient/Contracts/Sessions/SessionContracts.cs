
using MemoryPack;

namespace UnifierTSL.Contracts.Sessions
{
    public enum InteractionScopeState : byte
    {
        Inactive,
        Active,
        Completing,
        Completed,
        Cancelled,
    }

    public enum InputEventKind : byte
    {
        None,
        Text,
        Key,
        EditorStateSync,
        Scroll,
        Select,
        Submit,
        Cancel,
        Command,
    }

    [MemoryPackable]
    public readonly partial record struct InteractionScopeId(string Value)
    {
        public static InteractionScopeId Empty { get; } = new(string.Empty);
        public static InteractionScopeId New() => new(Guid.NewGuid().ToString("N"));
        public override string ToString() => Value;
    }

    [MemoryPackable]
    public readonly partial record struct SurfaceKeyInfo(
        char KeyChar,
        ConsoleKey Key,
        bool Shift,
        bool Alt,
        bool Control)
    {
        public static SurfaceKeyInfo FromConsoleKeyInfo(ConsoleKeyInfo keyInfo) {
            return new SurfaceKeyInfo(
                keyInfo.KeyChar,
                keyInfo.Key,
                keyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift),
                keyInfo.Modifiers.HasFlag(ConsoleModifiers.Alt),
                keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control));
        }

        public ConsoleKeyInfo ToConsoleKeyInfo() {
            return new ConsoleKeyInfo(KeyChar, Key, Shift, Alt, Control);
        }
    }

    [MemoryPackable]
    public sealed partial class InteractionScope
    {
        public InteractionScopeId Id { get; init; } = InteractionScopeId.Empty;
        public InteractionScopeState State { get; init; } = InteractionScopeState.Inactive;
        public string Kind { get; init; } = string.Empty;
        public bool IsTransient { get; init; } = true;
    }

    [MemoryPackable]
    public sealed partial class InputEvent
    {
        public InputEventKind Kind { get; init; }
        public string Text { get; init; } = string.Empty;
        public SurfaceKeyInfo? KeyInfo { get; init; }
        public int Delta { get; init; }
        public string Command { get; init; } = string.Empty;
    }
}
