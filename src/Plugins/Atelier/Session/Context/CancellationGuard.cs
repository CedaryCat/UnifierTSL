using System.Reflection;

namespace Atelier.Session.Context
{
    public static class CancellationGuard
    {
#pragma warning disable CA1068
        public static bool IsRequestedBy(CancellationToken cancellationToken, Exception exception) =>
            Unwrap(exception) is OperationCanceledException canceled
                && cancellationToken.IsCancellationRequested
                && canceled.CancellationToken == cancellationToken;

        public static Exception Unwrap(Exception exception) {
            ArgumentNullException.ThrowIfNull(exception);
            while (true) {
                switch (exception) {
                    case TargetInvocationException { InnerException: { } targetInner }:
                        exception = targetInner;
                        continue;

                    case AggregateException { InnerExceptions.Count: 1, InnerException: { } aggregateInner }:
                        exception = aggregateInner;
                        continue;

                    default:
                        return exception;
                }
            }
        }

        public static Action WrapTask(CancellationToken cancellationToken, Action action) =>
            Checked(action, () => RunTask(cancellationToken, action));

        public static Func<TResult> WrapTask<TResult>(CancellationToken cancellationToken, Func<TResult> action) =>
            Checked(action, () => RunTask(cancellationToken, action));

        public static Func<Task> WrapTask(CancellationToken cancellationToken, Func<Task> action) =>
            Checked(action, () => RunTaskAsync(cancellationToken, action));

        public static Func<Task<TResult>> WrapTask<TResult>(CancellationToken cancellationToken, Func<Task<TResult>> action) =>
            Checked(action, () => RunTaskAsync(cancellationToken, action));

        public static Action WrapDetached(CancellationToken cancellationToken, Action action) =>
            Checked(action, () => RunDetached(cancellationToken, action));

        public static Action<T> WrapDetached<T>(CancellationToken cancellationToken, Action<T> action) =>
            Checked(action, state => RunDetached(cancellationToken, () => action(state)));

        public static ThreadStart WrapDetached(CancellationToken cancellationToken, ThreadStart action) =>
            Checked(action, () => RunDetached(cancellationToken, () => action()));

        public static ParameterizedThreadStart WrapDetached(CancellationToken cancellationToken, ParameterizedThreadStart action) =>
            Checked(action, state => RunDetached(cancellationToken, () => action(state)));

        public static WaitCallback WrapDetached(CancellationToken cancellationToken, WaitCallback action) =>
            Checked(action, state => RunDetached(cancellationToken, () => action(state)));

        public static TimerCallback WrapDetached(CancellationToken cancellationToken, TimerCallback action) =>
            Checked(action, state => RunDetached(cancellationToken, () => action(state)));

        public static System.Timers.ElapsedEventHandler WrapDetached(
            CancellationToken cancellationToken,
            System.Timers.ElapsedEventHandler action) =>
            Checked(action, (sender, args) => RunDetached(cancellationToken, () => action(sender, args)));

        private static TDelegate Checked<TDelegate>(TDelegate action, TDelegate wrapper)
            where TDelegate : Delegate {
            return wrapper;
        }

        private static void RunTask(CancellationToken cancellationToken, Action action) {
            cancellationToken.ThrowIfCancellationRequested();
            action();
        }

        private static TResult RunTask<TResult>(CancellationToken cancellationToken, Func<TResult> action) {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }

        private static async Task RunTaskAsync(CancellationToken cancellationToken, Func<Task> action) {
            cancellationToken.ThrowIfCancellationRequested();
            await action().ConfigureAwait(false);
        }

        private static async Task<TResult> RunTaskAsync<TResult>(CancellationToken cancellationToken, Func<Task<TResult>> action) {
            cancellationToken.ThrowIfCancellationRequested();
            return await action().ConfigureAwait(false);
        }

        private static void RunDetached(CancellationToken cancellationToken, Action action) {
            try {
                cancellationToken.ThrowIfCancellationRequested();
                action();
            }
            catch (Exception ex) when (IsRequestedBy(cancellationToken, ex)) {
            }
        }
#pragma warning restore CA1068
    }
}
