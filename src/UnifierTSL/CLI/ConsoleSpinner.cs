namespace UnifierTSL.CLI
{
    public class ConsoleSpinner
    {
        private int _counter;
        private readonly int _delay;
        private bool _active;
        private readonly Thread _thread;

        public int LastLeft { get; private set; }
        public int LastTop { get; private set; }

        public ConsoleSpinner(int delay = 150) {
            _delay = delay;
            _thread = new Thread(Spin);
        }

        private void Spin() {
            char[] sequence = { '-', '\\', '|', '/' };

            while (_active) {
                lock (SynchronizedGuard.ConsoleLock) {
                    int left = Console.CursorLeft;
                    int top = Console.CursorTop;
                    Console.CursorVisible = false;
                    Console.SetCursorPosition(0, left != 0 ? top + 1 : top);
                    Console.Write($"Running... {sequence[_counter++ % sequence.Length]}");
                    Console.SetCursorPosition(left, top);
                    LastLeft = left;
                    LastTop = top;
                }
                Thread.Sleep(_delay);
            }

            lock (SynchronizedGuard.ConsoleLock) {
                Console.CursorVisible = true;
            }
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
