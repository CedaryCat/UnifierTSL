using System.Collections.ObjectModel;

namespace UnifierTSL.Events.Core
{
    public abstract class ValueEventBaseProvider<THandler> : EventProvider where THandler : struct, IPriorityHandler
    {
        protected volatile THandler[] _snapshot = [];
        protected readonly Lock _sync = new();
        private static readonly Comparer<THandler> _comparer = Comparer<THandler>.Create((a, b) => a.Priority.CompareTo(b.Priority));
        public void Register(THandler handler) {
            lock (_sync) {
                THandler[] old = _snapshot;
                int len = old.Length;
                THandler[] tmp = new THandler[len + 1];
                int idx = Array.BinarySearch(old, handler, _comparer);
                if (idx < 0) idx = ~idx;
                Array.Copy(old, 0, tmp, 0, idx);
                tmp[idx] = handler;
                Array.Copy(old, idx, tmp, idx + 1, len - idx);
                _snapshot = tmp;
            }
        }

        protected void Unregister(Predicate<THandler> predicate) {
            lock (_sync) {
                THandler[] old = _snapshot;
                int len = old.Length;
                if (len == 0) return;
                int idx = Array.FindIndex(old, predicate);
                if (idx < 0) return;
                THandler[] tmp = new THandler[len - 1];
                Array.Copy(old, 0, tmp, 0, idx);
                Array.Copy(old, idx + 1, tmp, idx, len - idx - 1);
                _snapshot = tmp;
            }
        }
        public sealed override int HandlerCount => _snapshot.Length;
    }

    public abstract class EventProvider
    {
        public EventProvider() {
            allEvents.Add(this);
        }
        private static List<EventProvider> allEvents = [];
        public static ReadOnlyCollection<EventProvider> AllEvents => allEvents.AsReadOnly();
        public abstract int HandlerCount { get; }
    }
}
