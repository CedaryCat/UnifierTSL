namespace UnifierTSL.CLI
{
    internal static class ConsoleInput
    {
        public static string ReadLine(string? prompt = null, bool trim = false) {
            lock (SynchronizedGuard.ConsoleLock) {
                ConsoleSpinner.ClearActiveFrameUnsafe();
                Console.CursorVisible = true;

                if (!string.IsNullOrEmpty(prompt)) {
                    Console.Write(prompt);
                }

                string input = Console.ReadLine() ?? string.Empty;
                return trim ? input.Trim() : input;
            }
        }
    }
}
