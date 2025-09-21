using System.ComponentModel;

namespace UnifierTSL.Events.Core
{
    public class ReferenceEventArgs<TEvent>(TEvent content) : HandledEventArgs where TEvent : class, IEventContent
    {
        public TEvent Content { get; protected set; } = content;
        public bool StopPropagation { get; set; }
    }
    public ref struct ValueEventArgs<TEvent>(ref TEvent content) where TEvent : struct, IEventContent
    {
        public readonly ref TEvent Content = ref content;
        public bool StopPropagation;
        public bool Handled;
    }
    public ref struct ValueEventNoCancelArgs<TEvent>(ref TEvent content) where TEvent : struct, IEventContent
    {
        public readonly ref TEvent Content = ref content;
        public bool StopPropagation;
    }
    public ref struct ReadonlyEventArgs<TEvent>(in TEvent content) where TEvent : struct, IEventContent
    {
        public readonly ref readonly TEvent Content = ref content;
        public bool StopPropagation;
        public bool Handled;
        public static explicit operator ReadonlyEventArgs<TEvent>(in TEvent args)
            => new(args);
    }
    public ref struct ReadonlyNoCancelEventArgs<TEvent>(in TEvent content) where TEvent : struct, IEventContent
    {
        public readonly ref readonly TEvent Content = ref content;
        public bool StopPropagation;
        public static explicit operator ReadonlyNoCancelEventArgs<TEvent>(in TEvent args)
            => new(args);
    }
}
