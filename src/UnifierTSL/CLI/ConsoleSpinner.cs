namespace UnifierTSL.CLI
{
    public class ConsoleSpinner
    {
        private int _counter;
        private readonly int _delay;
        private bool _active;
        private readonly Thread _thread;

        public ConsoleSpinner(int delay = 150) {
            _delay = delay;
            _thread = new Thread(Spin);
        }

        private void Spin() {
            char[] sequence = { '-', '\\', '|', '/' };

            int left = Console.CursorLeft;
            int top = Console.CursorTop;
            Console.CursorVisible = false;

            while (_active) {
                Console.SetCursorPosition(left, top);
                Console.Write(sequence[_counter++ % sequence.Length]);
                Thread.Sleep(_delay);
            }

            Console.SetCursorPosition(left, top);
            Console.SetCursorPosition(left, top);
            Console.CursorVisible = true;
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
