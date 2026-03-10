using System.Diagnostics;
using UnifierTSL.CLI;

namespace UnifierTSL
{
    internal static class WorkRunner
    {
        public static void RunConsoleActivity(string category, string message, Action work) {
            RunConsoleActivityCore(category, message, () => {
                work();
                return 0;
            });
        }
        public static TOut RunConsoleActivity<TOut>(string category, string message, WorkDelegate<TOut> work) {
            return RunConsoleActivityCore(category, message, () => work());
        }
        public static void RunConsoleActivityAsync(string category, string message, Func<TaskCompletionSource> work, Action? endAction = null) {
            RunConsoleActivityCore(category, message, () => {
                work().Task.Wait();
                return 0;
            });
            endAction?.Invoke();
        }

        private static TOut RunConsoleActivityCore<TOut>(string category, string message, Func<TOut> work)
        {
            string logCategory = $"ConsoleActivity:{category}";
            UnifierApi.Logger.Info(
                category: logCategory,
                message: message);

            Stopwatch stopwatch = Stopwatch.StartNew();
            using IDisposable statusScope = ConsoleInput.BeginConsoleActivityStatus(category, message);
            try {
                return work();
            }
            finally {
                stopwatch.Stop();
                UnifierApi.Logger.Info(
                    category: logCategory,
                    message: GetParticularString("{0} is elapsed milliseconds (number format with 2 decimal places)", $"Done. (used {stopwatch.ElapsedMilliseconds:.00}ms)"));
            }
        }

        public delegate TOut WorkDelegate<TIn, TOut>(TIn input);
        public delegate TOut WorkDelegate<TOut>();
    }
}
