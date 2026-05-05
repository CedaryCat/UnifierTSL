using UnifierTSL.Surface.Activities;
using System.Diagnostics;
using UnifierTSL.Surface.Hosting;
using UnifierTSL.Surface.Status;
using UnifierTSL.Logging;
using UnifierTSL.Servers;

namespace UnifierTSL
{
    internal static class WorkRunner
    {
        public static void RunSurfaceActivity(string category, string message, Action work) {
            RunSurfaceActivityCore(category, message, () => {
                work();
                return 0;
            });
        }
        public static TOut RunSurfaceActivity<TOut>(string category, string message, WorkDelegate<TOut> work) {
            return RunSurfaceActivityCore(category, message, () => work());
        }
        public static void RunSurfaceActivityAsync(string category, string message, Func<TaskCompletionSource> work, Action? endAction = null) {
            RunSurfaceActivityCore(category, message, () => {
                work().Task.Wait();
                return 0;
            });
            endAction?.Invoke();
        }

        public static Task RunSurfaceActivityAsync(
            ServerContext server,
            string category,
            string message,
            Func<ActivityHandle, CancellationToken, Task> work,
            ActivityDisplayOptions display = default,
            CancellationToken cancellationToken = default) {

            return RunSurfaceActivityAsyncCore(server, category, message, async (handle, token) => {
                await work(handle, token);
                return 0;
            }, display, cancellationToken);
        }

        public static Task<TOut> RunSurfaceActivityAsync<TOut>(
            ServerContext server,
            string category,
            string message,
            Func<ActivityHandle, CancellationToken, Task<TOut>> work,
            ActivityDisplayOptions display = default,
            CancellationToken cancellationToken = default) {

            return RunSurfaceActivityAsyncCore(server, category, message, work, display, cancellationToken);
        }

        private static TOut RunSurfaceActivityCore<TOut>(string category, string message, Func<TOut> work)
        {
            string logCategory = $"SurfaceActivity:{category}";
            UnifierApi.Logger.Info(
                category: logCategory,
                message: message);

            Stopwatch stopwatch = Stopwatch.StartNew();
            IDisposable? statusScope = null;
            try {
                statusScope = Console.BeginSurfaceActivity(category, message);
                return work();
            }
            finally {
                stopwatch.Stop();
                try {
                    statusScope?.Dispose();
                }
                catch {
                }

                UnifierApi.Logger.Info(
                    category: logCategory,
                    message: GetParticularString("{0} is elapsed milliseconds (number format with 2 decimal places)", $"Done. (used {stopwatch.ElapsedMilliseconds:.00}ms)"));
            }
        }

        private static Task<TOut> RunSurfaceActivityAsyncCore<TOut>(
            ServerContext server,
            string category,
            string message,
            Func<ActivityHandle, CancellationToken, Task<TOut>> work,
            ActivityDisplayOptions display,
            CancellationToken cancellationToken) {
            return server.Dispatcher.InvokeAsync(async () => {
                string logCategory = $"SurfaceActivity:{category}";
                server.Log.Info(
                    category: logCategory,
                    message: message);

                Stopwatch stopwatch = Stopwatch.StartNew();
                ActivityOutcome outcome = ActivityOutcome.Completed;
                ActivityHandle? statusScope = null;
                try {
                    statusScope = server.Console.BeginSurfaceActivity(category, message, display, cancellationToken);
                    return await work(statusScope, statusScope.CancellationToken);
                }
                catch (OperationCanceledException) when ((statusScope?.IsCancellationRequested ?? false) || cancellationToken.IsCancellationRequested) {
                    outcome = ActivityOutcome.Canceled;
                    throw;
                }
                catch {
                    outcome = ActivityOutcome.Failed;
                    throw;
                }
                finally {
                    stopwatch.Stop();
                    if (statusScope is not null) {
                        try {
                            await statusScope.DisposeAsync();
                        }
                        catch {
                        }
                    }

                    server.Log.Log(
                        outcome switch {
                            ActivityOutcome.Completed => LogLevel.Info,
                            ActivityOutcome.Canceled => LogLevel.Warning,
                            _ => LogLevel.Error,
                        },
                        outcome switch {
                            ActivityOutcome.Completed => GetParticularString("{0} is elapsed milliseconds (number format with 2 decimal places)", $"Done. (used {stopwatch.ElapsedMilliseconds:.00}ms)"),
                            ActivityOutcome.Canceled => GetParticularString("{0} is elapsed milliseconds (number format with 2 decimal places)", $"Canceled. (used {stopwatch.ElapsedMilliseconds:.00}ms)"),
                            _ => GetParticularString("{0} is elapsed milliseconds (number format with 2 decimal places)", $"Failed. (used {stopwatch.ElapsedMilliseconds:.00}ms)"),
                        },
                        overwriteCategory: logCategory);
                }
            }, cancellationToken);
        }

        public delegate TOut WorkDelegate<TIn, TOut>(TIn input);
        public delegate TOut WorkDelegate<TOut>();

        private enum ActivityOutcome
        {
            Completed,
            Canceled,
            Failed,
        }
    }
}
