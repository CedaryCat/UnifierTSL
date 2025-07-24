using UnifierTSL.CLI;
using System.Diagnostics;

namespace UnifierTSL
{
    internal static class WorkRunner
    {
        public static void RunTimedWork(string message, Action work) {
            Console.Write($"[USP|Info] {message}");
            var spinner = new ConsoleSpinner(100);
            spinner.Start();
            Stopwatch stopwatch = new();
            stopwatch.Start();
            work();
            stopwatch.Stop();
            spinner.Stop();
            Console.WriteLine($" - done. (used {stopwatch.ElapsedMilliseconds:.00}ms)");
        }
        public static TOut RunTimedWork<TOut>(string message, WorkDelegate<TOut> work) {
            Console.Write($"[USP|Info] {message}");
            var spinner = new ConsoleSpinner(100);
            spinner.Start();
            Stopwatch stopwatch = new();
            stopwatch.Start();
            var output = work();
            stopwatch.Stop();
            spinner.Stop();
            Console.WriteLine($" - done. (used {stopwatch.ElapsedMilliseconds:.00}ms)");
            return output;
        }
        public static void RunTimedWorkAsync(string message, Func<TaskCompletionSource> work, Action? endAction = null) {
            Console.Write($"[USP|Info] {message}");
            var spinner = new ConsoleSpinner(100);
            spinner.Start();
            Stopwatch stopwatch = new();
            stopwatch.Start();
            work().Task.Wait();
            stopwatch.Stop();
            spinner.Stop();
            Console.WriteLine($" - done. (used {stopwatch.ElapsedMilliseconds:.00}ms)");
            endAction?.Invoke();
        }

        public delegate TOut WorkDelegate<TIn, TOut>(TIn input);
        public delegate TOut WorkDelegate<TOut>();
    }
}