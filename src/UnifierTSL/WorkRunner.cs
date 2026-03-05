using System.Diagnostics;
using UnifierTSL.CLI;

namespace UnifierTSL
{
    internal static class WorkRunner
    {
        public static void RunTimedWork(string category, string message, Action work) {
            UnifierApi.Logger.Info(
                category: $"TimedWork:{category}",
                message: message);

            Stopwatch stopwatch = new();
            stopwatch.Start();
            using IDisposable statusScope = ConsoleInput.BeginTimedWorkStatus(category, message);
            try {
                work();
            }
            finally {
                stopwatch.Stop();
            }

            UnifierApi.Logger.Info(
                category: $"TimedWork:{category}",
                message: GetParticularString("{0} is elapsed milliseconds (number format with 2 decimal places)", $"Done. (used {stopwatch.ElapsedMilliseconds:.00}ms)"));
        }
        public static TOut RunTimedWork<TOut>(string category, string message, WorkDelegate<TOut> work) {
            UnifierApi.Logger.Info(
                category: $"TimedWork:{category}",
                message: message);

            Stopwatch stopwatch = new();
            stopwatch.Start();
            using IDisposable statusScope = ConsoleInput.BeginTimedWorkStatus(category, message);
            TOut? output;
            try {
                output = work();
            }
            finally {
                stopwatch.Stop();
            }

            UnifierApi.Logger.Info(
                category: $"TimedWork:{category}",
                message: GetParticularString("{0} is elapsed milliseconds (number format with 2 decimal places)", $"Done. (used {stopwatch.ElapsedMilliseconds:.00}ms)"));

            return output;
        }
        public static void RunTimedWorkAsync(string category, string message, Func<TaskCompletionSource> work, Action? endAction = null) {
            UnifierApi.Logger.Info(
                category: $"TimedWork:{category}",
                message: message);

            Stopwatch stopwatch = new();
            stopwatch.Start();
            using IDisposable statusScope = ConsoleInput.BeginTimedWorkStatus(category, message);
            try {
                work().Task.Wait();
            }
            finally {
                stopwatch.Stop();
            }

            UnifierApi.Logger.Info(
                category: $"TimedWork:{category}",
                message: GetParticularString("{0} is elapsed milliseconds (number format with 2 decimal places)", $"Done. (used {stopwatch.ElapsedMilliseconds:.00}ms)"));

            endAction?.Invoke();
        }

        public delegate TOut WorkDelegate<TIn, TOut>(TIn input);
        public delegate TOut WorkDelegate<TOut>();
    }
}
