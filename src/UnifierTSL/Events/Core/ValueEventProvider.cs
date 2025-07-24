using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnifierTSL.Events.Core
{
    public class ValueEventProvider<TEvent>() : ValueEventBaseProvider<ValueEventProvider<TEvent>.PriorityItem> where TEvent : struct, IEventContent
    {
        [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly struct PriorityItem(ValueEventDelegate<TEvent> handler, HandlerPriority priority, FilterEventOption option) : IPriorityHandler
        {
            public readonly ValueEventDelegate<TEvent> Handler = handler;
            public readonly HandlerPriority Priority = priority;
            public readonly FilterEventOption Option = option;

            HandlerPriority IPriorityHandler.Priority {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Priority;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Register(ValueEventDelegate<TEvent> handler, HandlerPriority priority = HandlerPriority.Normal, FilterEventOption option = FilterEventOption.Normal)
            => Register(new PriorityItem(handler, priority, option));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unregister(ValueEventDelegate<TEvent> handler) 
            => Unregister(x => x.Handler == handler);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Invoke(ref TEvent data, out bool handled) {
            var handlers = _snapshot.AsSpan();
            if (handlers.Length == 0) {
                handled = false;
                return;
            }
            var args = new ValueEventArgs<TEvent>(ref data);
            ref var r0 = ref MemoryMarshal.GetReference(handlers);
            for (int i = 0; i < handlers.Length; i++) {
                var handler = Unsafe.Add(ref r0, i);
                if (((args.Handled ? FilterEventOption.Handled : FilterEventOption.Normal) & handler.Option) != 0) {
                    handler.Handler(ref args);
                    if (args.StopMovementUp) {
                        break;
                    }
                }
            }
            handled = args.Handled;
        }
    }
}
