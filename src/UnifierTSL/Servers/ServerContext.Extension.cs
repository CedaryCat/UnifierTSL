using System.Collections.Immutable;
using System.Runtime.CompilerServices;

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
        private static readonly List<WeakReference<ServerContext>> s_liveContexts = [];

        private IExtensionData[] _slots = [];
        private bool extensionDisposed;
        private void DisposeExtension() {
            if (extensionDisposed) return;
            extensionDisposed = true;
            foreach (IExtensionData _slots in _slots) {
                _slots?.Dispose();
            }
        }

        protected virtual void InitializeExtension() {
            int requiredSlots = Volatile.Read(ref s_nextSlot);
            _slots = new IExtensionData[requiredSlots];

            lock (s_registrationLock) {
                s_liveContexts.Add(new WeakReference<ServerContext>(this));

                foreach (IRegistration reg in s_registrations) {
                    reg.Inject(this, _slots);
                }
            }
        }

        public static void RegisterExtension<TExtension>(Func<ServerContext, TExtension> factory) where TExtension : class, IExtensionData {
            ArgumentNullException.ThrowIfNull(factory);

            lock (s_registrationLock) {
                if (ExtensionSlotKey<TExtension>.Key != -1)
                    throw new InvalidOperationException($"Type {typeof(TExtension).FullName} has already been registered.");

                int assignedSlot = s_nextSlot++;
                ExtensionSlotKey<TExtension>.Key = assignedSlot;

                Registration<TExtension> newReg = new(factory, assignedSlot);
                s_registrations = s_registrations.Add(newReg);

                for (int i = s_liveContexts.Count - 1; i >= 0; i--) {
                    if (s_liveContexts[i].TryGetTarget(out ServerContext? ctx)) {
                        ctx.EnsureCapacity(assignedSlot + 1);
                        newReg.Inject(ctx, ctx._slots);
                    }
                    else {
                        s_liveContexts.RemoveAt(i);
                    }
                }
            }
        }

        public static void UnregisterExtension<TExtra>() where TExtra : class, IExtensionData {
            lock (s_registrationLock) {
                IRegistration? reg = s_registrations.FirstOrDefault(r => r.Slot == ExtensionSlotKey<TExtra>.Key);
                ExtensionSlotKey<TExtra>.Key = -1;
                if (reg == null) return;
                s_registrations = s_registrations.Remove(reg);

                for (int i = s_liveContexts.Count - 1; i >= 0; i--) {
                    if (s_liveContexts[i].TryGetTarget(out ServerContext? ctx)) {
                        if (reg.Slot < ctx._slots.Length)
                            ctx._slots[reg.Slot] = null!;
                    }
                    else {
                        s_liveContexts.RemoveAt(i);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TExtension GetExtension<TExtension>() where TExtension : class, IExtensionData {
            return (TExtension)_slots[ExtensionSlotKey<TExtension>.Key];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetExtension<TExtension>(TExtension value) where TExtension : class, IExtensionData {
            _slots[ExtensionSlotKey<TExtension>.Key] = value;
        }

        private void EnsureCapacity(int capacity) {
            if (_slots.Length < capacity) {
                Array.Resize(ref _slots, capacity);
            }
        }

        private interface IRegistration
        {
            int Slot { get; }
            void Inject(ServerContext ctx, object[] slots);
        }

        private class Registration<TExtension>(Func<ServerContext, TExtension> factory, int slot) : IRegistration where TExtension : class, IExtensionData
        {
            public int Slot { get; } = slot;

            public void Inject(ServerContext ctx, object[] slots) {
                if (slots.Length <= Slot)
                    ctx.EnsureCapacity(Slot + 1);

                if (slots[Slot] == null)
                    slots[Slot] = factory(ctx);
            }
        }
    }

    public interface IExtensionData : IDisposable
    {
        virtual void ExtensionsInjected() { }
    }
}