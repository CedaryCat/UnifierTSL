using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnifierTSL.Events.Core
{
    public class ReadonlyEventNoCancelProvider<TEvent>() : ValueEventBaseProvider<ReadonlyEventNoCancelProvider<TEvent>.PriorityItem> where TEvent : struct, IEventContent
    {
        [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly struct PriorityItem(ReadonlyEventNoCancelDelegate<TEvent> handler, HandlerPriority priority) : IPriorityHandler
        {
            public readonly ReadonlyEventNoCancelDelegate<TEvent> Handler = handler;
            public readonly HandlerPriority Priority = priority;

            HandlerPriority IPriorityHandler.Priority {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Priority;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Register(ReadonlyEventNoCancelDelegate<TEvent> handler, HandlerPriority priority) 
            => Register(new PriorityItem(handler, priority));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnRegister(ReadonlyEventNoCancelDelegate<TEvent> handler) =>
            Unregister(x => x.Handler == handler);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Invoke(in TEvent data) {
            var handlers = _snapshot.AsSpan();
            if (handlers.Length == 0) {
                return;
            }
            var args = new ReadonlyNoCancelEventArgs<TEvent>(data);
            ref var r0 = ref MemoryMarshal.GetReference(handlers);
            for (int i = 0; i < handlers.Length; i++) {
                Unsafe.Add(ref r0, i).Handler(ref args);
                if (args.StopMovementUp) {
                    break;
                }
            }
        }
    }
}
