using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnifierTSL.Events.Core
{
    public class ReadonlyEventProvider<TEvent>() : ValueEventBaseProvider<ReadonlyEventProvider<TEvent>.PriorityItem> where TEvent : struct, IEventContent
    {
        [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly struct PriorityItem(ReadonlyEventDelegate<TEvent> handler, HandlerPriority priority, FilterEventOption option) : IPriorityHandler
        {
            public readonly ReadonlyEventDelegate<TEvent> Handler = handler;
            public readonly HandlerPriority Priority = priority;
            public readonly FilterEventOption Option = option;

            HandlerPriority IPriorityHandler.Priority {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Priority;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Register(ReadonlyEventDelegate<TEvent> handler, HandlerPriority priority, FilterEventOption option = FilterEventOption.Normal) =>
            Register(new PriorityItem(handler, priority, option));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnRegister(ReadonlyEventDelegate<TEvent> handler) =>
            Unregister(x => x.Handler == handler);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Invoke(in TEvent data, out bool handled) {
            Span<PriorityItem> handlers = _snapshot.AsSpan();
            if (handlers.Length == 0) {
                handled = false;
                return;
            }
            ReadonlyEventArgs<TEvent> args = new(data);
            ref PriorityItem r0 = ref MemoryMarshal.GetReference(handlers);
            for (int i = 0; i < handlers.Length; i++) {
                PriorityItem handler = Unsafe.Add(ref r0, i);
                if (((args.Handled ? FilterEventOption.Handled : FilterEventOption.Normal) & handler.Option) != 0) {
                    handler.Handler(ref args);
                    if (args.StopPropagation) {
                        break;
                    }
                }
            }
            handled = args.Handled;
        }
    }
}
