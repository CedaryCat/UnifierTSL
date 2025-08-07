using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace UnifierTSL.Servers
{
    public partial class ServerContext
    {
        private static class ExtensionSlotKey<T> where T : class
        {
            public static int Key = -1;
        }

        private static readonly Lock s_registrationLock = new();
        private static int s_nextSlot = 0;
        private static ImmutableArray<IRegistration> s_registrations = [];

        private object[] _slots;
        private BitArray _initializedFlags;

        [MemberNotNull(nameof(_slots), nameof(_initializedFlags))]
        private void InitializeExtension() {

            var snapshot = s_registrations;
            int requiredSlots = Volatile.Read(ref s_nextSlot);

            _slots = new object[requiredSlots];
            _initializedFlags = new BitArray(requiredSlots);

            foreach (var reg in snapshot) {
                reg.InvokeFactory(this, ref _slots, _initializedFlags);
            }
        }

        public static void RegisterExtension<TExtra>(Func<ServerContext, TExtra> factory) where TExtra : class {
            ArgumentNullException.ThrowIfNull(factory);

            lock (s_registrationLock) {
                if (ExtensionSlotKey<TExtra>.Key != -1)
                    throw new InvalidOperationException($"Type {typeof(TExtra).FullName} has already been registered.");

                int assignedSlot = s_nextSlot++;
                ExtensionSlotKey<TExtra>.Key = assignedSlot;

                var newReg = new Registration<TExtra>(factory, assignedSlot);
                s_registrations = s_registrations.Add(newReg);
            }
        }
        public static void UnregisterExtension<TExtra>() where TExtra : class {
            lock (s_registrationLock) {
                var reg = s_registrations.FirstOrDefault(r => r.Slot == ExtensionSlotKey<TExtra>.Key);
                ExtensionSlotKey<TExtra>.Key = -1;
                if (reg == null) return;
                s_registrations = s_registrations.Remove(reg);
            }
        }

        public TExtra GetExtension<TExtra>() where TExtra : class {
            int slot = ExtensionSlotKey<TExtra>.Key;
            if (slot == -1)
                throw new InvalidOperationException($"Type {typeof(TExtra).FullName} has not been registered.");

            // Fast path: already initialized and has array

            if (slot >= _slots.Length) {
                // Possibly because of later registration, the array is not big enough: expand
                EnsureCapacity(slot + 1);
            }

            // Deferred initialization of this slot (thread-safe: multiple threads may call the factory, but the semantics should be idempotent or tolerable)
            if (_initializedFlags == null) {
                // This branch should almost never be taken: because it is assigned during initialization; but defensive recovery
                lock (s_registrationLock) {
                    _initializedFlags ??= new BitArray(_slots.Length);
                }
            }

            if (!_initializedFlags![slot]) {
                var snapshot = s_registrations;
                foreach (var reg in snapshot) {
                    if (reg.Slot == slot) {
                        reg.InvokeFactory(this, ref _slots!, _initializedFlags);
                        break;
                    }
                }
            }

            return (TExtra)_slots[slot];
        }

        public void SetExtension<TExtra>(TExtra value) where TExtra : class {
            int slot = ExtensionSlotKey<TExtra>.Key;
            if (slot == -1)
                throw new InvalidOperationException($"Type {typeof(TExtra).FullName} has not been registered.");

            if (slot >= _slots.Length) {
                EnsureCapacity(slot + 1);
            }

            _slots[slot] = value;

            if (_initializedFlags == null) {
                lock (s_registrationLock) {
                    _initializedFlags ??= new BitArray(_slots.Length);
                }
            }

            _initializedFlags![slot] = true;
        }

        public void CatchUpNewRegistrations() {
            var snapshot = s_registrations;
            foreach (var reg in snapshot) {
                reg.InvokeFactory(this, ref _slots, _initializedFlags);
            }
        }

        private void EnsureCapacity(int capacity) {

            if (_slots == null) {
                _slots = new object[capacity];
            }
            else if (_slots.Length < capacity) {
                Array.Resize(ref _slots, capacity);
            }
            
            if (_initializedFlags.Length < capacity) {
                var newFlags = new BitArray(capacity);
                for (int i = 0; i < _initializedFlags.Length; i++)
                    newFlags[i] = _initializedFlags[i];
                _initializedFlags = newFlags;
            }
        }

        private interface IRegistration
        {
            int Slot { get; }
            void InvokeFactory(ServerContext ctx, ref object[] slots, BitArray initializedFlags);
        }

        private class Registration<TExtra> : IRegistration where TExtra : class
        {
            private readonly Func<ServerContext, TExtra> _factory;
            public int Slot { get; }

            public Registration(Func<ServerContext, TExtra> factory, int slot) {
                _factory = factory;
                Slot = slot;
            }

            public void InvokeFactory(ServerContext ctx, ref object[] slots, BitArray initializedFlags) {
                if (slots == null || slots.Length <= Slot) {
                    ctx.EnsureCapacity(Slot + 1);
                }

                if (initializedFlags != null && initializedFlags.Length > Slot && initializedFlags[Slot])
                    return;

                var value = _factory(ctx);
                slots![Slot] = value;

                if (initializedFlags != null)
                    initializedFlags[Slot] = true;
            }
        }
    }

}
