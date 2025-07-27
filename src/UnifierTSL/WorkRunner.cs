using UnifierTSL.CLI;
using System.Diagnostics;

namespace UnifierTSL
{
    internal static class WorkRunner
    {
        public static void RunTimedWork(string category, string message, Action work) {
            UnifierApi.Logger.Info(
                category: $"TimedWork:{category}",
                message: message);

            var spinner = new ConsoleSpinner(100);
            spinner.Start();
            Stopwatch stopwatch = new();
            stopwatch.Start();
            work();
            stopwatch.Stop();
            spinner.Stop();
            Console.SetCursorPosition(spinner.LastLeft, spinner.LastTop);

            UnifierApi.Logger.Info(
                category: $"TimedWork:{category}",
                message: $"- Done. (used {stopwatch.ElapsedMilliseconds:.00}ms)");
        }
        public static TOut RunTimedWork<TOut>(string category, string message, WorkDelegate<TOut> work) {
            UnifierApi.Logger.Info(
                category: $"TimedWork:{category}",
                message: message);

            var spinner = new ConsoleSpinner(100);
            spinner.Start();
            Stopwatch stopwatch = new();
            stopwatch.Start();
            var output = work();
            stopwatch.Stop();
            spinner.Stop();
            Console.SetCursorPosition(spinner.LastLeft, spinner.LastTop);

            UnifierApi.Logger.Info(
                category: $"TimedWork:{category}",
                message: $"- Done. (used {stopwatch.ElapsedMilliseconds:.00}ms)");

            return output;
        }
        public static void RunTimedWorkAsync(string category, string message, Func<TaskCompletionSource> work, Action? endAction = null) {
            UnifierApi.Logger.Info(
                category: $"TimedWork:{category}",
                message: message);

            var spinner = new ConsoleSpinner(100);
            spinner.Start();
            Stopwatch stopwatch = new();
            stopwatch.Start();
            work().Task.Wait();
            stopwatch.Stop();
            spinner.Stop();
            Console.SetCursorPosition(spinner.LastLeft, spinner.LastTop);

            UnifierApi.Logger.Info(
                category: $"TimedWork:{category}",
                message: $"- Done. (used {stopwatch.ElapsedMilliseconds:.00}ms)");

            endAction?.Invoke();
        }

        public delegate TOut WorkDelegate<TIn, TOut>(TIn input);
        public delegate TOut WorkDelegate<TOut>();
    }
}