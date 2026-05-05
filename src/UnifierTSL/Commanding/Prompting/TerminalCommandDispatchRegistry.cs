using System.Collections.Immutable;
using UnifierTSL.Events.Handlers;

namespace UnifierTSL.Commanding.Prompting
{
    public interface ITerminalCommandDispatchAdapter
    {
        int Priority => 0;

        bool CanHandle(MessageSender sender);

        Task<CommandDispatchResult> DispatchAsync(
            MessageSender sender,
            string rawInput,
            IReadOnlyList<string> commandPrefixes,
            CancellationToken cancellationToken = default);
    }

    public static class TerminalCommandDispatchRegistry
    {
        private static readonly Lock SyncLock = new();
        private static ImmutableArray<Registration> registrations = [];
        private static long nextRegistrationId;

        public static IDisposable Register<TAdapter>() where TAdapter : ITerminalCommandDispatchAdapter, new() {
            return Register(new TAdapter());
        }

        public static IDisposable Register(ITerminalCommandDispatchAdapter adapter) {
            ArgumentNullException.ThrowIfNull(adapter);

            long registrationId;
            lock (SyncLock) {
                registrationId = checked(++nextRegistrationId);
                registrations = [.. registrations, new Registration(registrationId, adapter)];
            }

            return new RegistrationHandle(registrationId);
        }

        public static ITerminalCommandDispatchAdapter? Resolve(MessageSender sender) {
            var snapshot = registrations;
            foreach (var registration in snapshot
                .OrderBy(static registration => registration.Adapter.Priority)
                .ThenBy(static registration => registration.Id)) {
                if (registration.Adapter.CanHandle(sender)) {
                    return registration.Adapter;
                }
            }

            return null;
        }

        private static void Unregister(long registrationId) {
            lock (SyncLock) {
                var index = -1;
                for (var i = 0; i < registrations.Length; i++) {
                    if (registrations[i].Id != registrationId) {
                        continue;
                    }

                    index = i;
                    break;
                }
                if (index < 0) {
                    return;
                }

                registrations = registrations.RemoveAt(index);
            }
        }

        private sealed record Registration(long Id, ITerminalCommandDispatchAdapter Adapter);

        private sealed class RegistrationHandle(long registrationId) : IDisposable
        {
            private int disposed;

            public void Dispose() {
                if (Interlocked.Exchange(ref disposed, 1) != 0) {
                    return;
                }

                Unregister(registrationId);
            }
        }
    }
}
