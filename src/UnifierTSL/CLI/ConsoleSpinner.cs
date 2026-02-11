namespace UnifierTSL.CLI
{
    public class ConsoleSpinner
    {
        private const string PrefixText = "Running... ";

        private int _counter;
        private readonly int _delay;
        private bool _active;
        private readonly Thread _thread;

        private static int activeFrameTop = -1;
        private static int activeFrameLength;
        private static bool hasActiveFrame;

        public int LastLeft { get; private set; }
        public int LastTop { get; private set; }

        public ConsoleSpinner(int delay = 150) {
            _delay = delay;
            _thread = new Thread(Spin);
        }

        private void Spin() {
            char[] sequence = ['-', '\\', '|', '/'];

            while (_active) {
                lock (SynchronizedGuard.ConsoleLock) {
                    int left = Console.CursorLeft;
                    int top = Console.CursorTop;

                    Console.CursorVisible = false;
                    if (left != 0) {
                        Console.WriteLine();
                        left = Console.CursorLeft;
                        top = Console.CursorTop;
                    }

                    LastLeft = left;
                    LastTop = top;

                    string frameText = $"{PrefixText}{sequence[_counter++ % sequence.Length]}";
                    Console.SetCursorPosition(0, top);
                    Console.Write(frameText);
                    activeFrameTop = top;
                    activeFrameLength = frameText.Length;
                    hasActiveFrame = true;
                    Console.SetCursorPosition(left, top);
                }
                Thread.Sleep(_delay);
            }

            lock (SynchronizedGuard.ConsoleLock) {
                Console.CursorVisible = true;
                hasActiveFrame = false;
            }
        }

        internal static void ClearActiveFrameUnsafe() {
            if (!hasActiveFrame || activeFrameTop < 0 || activeFrameLength <= 0) {
                return;
            }

            Console.SetCursorPosition(0, activeFrameTop);
            Console.Write(new string(' ', activeFrameLength));
            Console.SetCursorPosition(0, activeFrameTop);
            hasActiveFrame = false;
        }

        public void Start() {
            _active = true;
            if (!_thread.IsAlive) _thread.Start();
        }

        public void Stop() {
            _active = false;
            _thread.Join();
        }
    }
}
