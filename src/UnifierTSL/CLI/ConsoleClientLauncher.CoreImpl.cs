using System.Text;
using UnifierTSL.ConsoleClient.Protocol.C2S;
using UnifierTSL.ConsoleClient.Shell;

namespace UnifierTSL.CLI
{
    public partial class ConsoleClientLauncher
    {
        #region Command Implementation

        private ConsoleColor cachedBackgroundColor = Console.BackgroundColor;
        private ConsoleColor cachedForegroundColor = Console.ForegroundColor;
        private Encoding cachedInputEncoding = Console.InputEncoding;
        private Encoding cachedOutputEncoding = Console.OutputEncoding;
        private int cachedWindowHeight = Console.WindowHeight;
        private int cachedWindowLeft = Console.WindowLeft;
        private int cachedWindowTop = Console.WindowTop;
        private int cachedWindowWidth = Console.WindowWidth;
        private string cachedTitle = string.Empty;

        public override ConsoleColor BackgroundColor {
            get => cachedBackgroundColor;
            set => cachedBackgroundColor = value;
        }

        public override ConsoleColor ForegroundColor {
            get => cachedForegroundColor;
            set => cachedForegroundColor = value;
        }

        public override Encoding InputEncoding {
            get => cachedInputEncoding;
            set {
                cachedInputEncoding = value;
                transport.Send(new SET_INPUT_ENCODING(value));
            }
        }

        public override Encoding OutputEncoding {
            get => cachedOutputEncoding;
            set {
                cachedOutputEncoding = value;
                transport.Send(new SET_OUTPUT_ENCODING(value));
            }
        }

        public override int WindowWidth {
            get => cachedWindowWidth;
            set {
                cachedWindowWidth = value;
                transport.Send(new SET_WINDOW_SIZE(value, 0));
            }
        }

        public override int WindowHeight {
            get => cachedWindowHeight;
            set {
                cachedWindowHeight = value;
                transport.Send(new SET_WINDOW_SIZE(0, value));
            }
        }

        public override int WindowLeft {
            get => cachedWindowLeft;
            set {
                cachedWindowLeft = value;
                transport.Send(new SET_WINDOW_POS(value, 0));
            }
        }

        public override int WindowTop {
            get => cachedWindowTop;
            set {
                cachedWindowTop = value;
                transport.Send(new SET_WINDOW_POS(0, value));
            }
        }

        public override string Title {
            get => cachedTitle;
            set {
                cachedTitle = value;
                transport.SendManaged(new SET_TITLE(value));
            }
        }

        public void WriteAnsi(string? value)
        {
            if (string.IsNullOrEmpty(value)) {
                return;
            }

            transport.SendManaged(new SEND_WRITE_ANSI(value));
        }

        public void WriteLineAnsi(string? value) {

            if (string.IsNullOrEmpty(value)) {
                transport.SendManaged(new SEND_WRITE_LINE(""));
                return;
            }

            transport.SendManaged(new SEND_WRITE_LINE_ANSI(value));
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value)) {
                return;
            }

            string sanitized = AnsiSanitizer.SanitizeEscapes(value);
            string ansi = AnsiColorCodec.Wrap(sanitized, cachedForegroundColor, cachedBackgroundColor);
            transport.SendManaged(new SEND_WRITE_ANSI(ansi));
        }

        public override void WriteLine(string? value)
        {
            if (string.IsNullOrEmpty(value)) {
                transport.SendManaged(new SEND_WRITE_LINE(""));
                return;
            }

            string sanitized = AnsiSanitizer.SanitizeEscapes(value);
            string ansi = AnsiColorCodec.Wrap(sanitized, cachedForegroundColor, cachedBackgroundColor);
            transport.SendManaged(new SEND_WRITE_LINE_ANSI(ansi));
        }

        public override void Clear()
        {
            transport.Send(new CLEAR());
        }

        private void HandleTransportReconnected()
        {
            ReplayCachedConsoleState();
        }

        private void ReplayCachedConsoleState()
        {
            transport.Send(new SET_INPUT_ENCODING(cachedInputEncoding));
            transport.Send(new SET_OUTPUT_ENCODING(cachedOutputEncoding));
            transport.Send(new SET_WINDOW_SIZE(cachedWindowWidth, cachedWindowHeight));
            transport.Send(new SET_WINDOW_POS(cachedWindowLeft, cachedWindowTop));
            transport.SendManaged(new SET_TITLE(cachedTitle));
        }

        #endregion

        #region Read Implementation

        public override string? ReadLine()
        {
            return readSessionBroker.ReadLine();
        }

        public override ConsoleKeyInfo ReadKey()
        {
            return readSessionBroker.ReadKey(intercept: false);
        }

        public override ConsoleKeyInfo ReadKey(bool intercept)
        {
            return readSessionBroker.ReadKey(intercept);
        }

        public override int Read()
        {
            return readSessionBroker.Read();
        }

        #endregion
    }
}
