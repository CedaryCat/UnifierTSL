namespace UnifierTSL.Events.Core
{
    public delegate void ReferenceEventDelegate<TEvent>(ReferenceEventArgs<TEvent> args) where TEvent : class, IEventContent;
    public delegate void ValueEventDelegate<TEvent>(ref ValueEventArgs<TEvent> args) where TEvent : struct, IEventContent;
    public delegate void ValueNoCancelEventDelegate<TEvent>(ref ValueEventNoCancelArgs<TEvent> args) where TEvent : struct, IEventContent;
    public delegate void ReadonlyEventDelegate<TEvent>(ref ReadonlyEventArgs<TEvent> args) where TEvent : struct, IEventContent;
    public delegate void ReadonlyEventNoCancelDelegate<TEvent>(ref ReadonlyNoCancelEventArgs<TEvent> args) where TEvent : struct, IEventContent;
}
