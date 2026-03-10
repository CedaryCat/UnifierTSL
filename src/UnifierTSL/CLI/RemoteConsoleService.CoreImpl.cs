using System.Text;
using System.Text.Json;
using UnifierTSL.CLI.Prompting;
using UnifierTSL.CLI.Status;
using UnifierTSL.ConsoleClient.Protocol.HostToClient;
using UnifierTSL.ConsoleClient.Shell;

namespace UnifierTSL.CLI
{
    public partial class RemoteConsoleService
    {
        #region Command Implementation

        private string cachedThemeJson = string.Empty;
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
                transport.SendManaged(new SEND_WRITE_LINE_ANSI(""));
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
                transport.SendManaged(new SEND_WRITE_LINE_ANSI(""));
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
            statusController.ResetChangeTracking();
            statusController.ReplayCurrent();
        }

        private void HandleConsoleAppearanceChanged()
        {
            if (!transport.IsConnected) {
                return;
            }

            PublishThemeIfChanged(force: true);
            statusController.ResetChangeTracking();
            statusController.ReplayCurrent();
        }

        private void ReplayCachedConsoleState()
        {
            transport.Send(new SET_INPUT_ENCODING(cachedInputEncoding));
            transport.Send(new SET_OUTPUT_ENCODING(cachedOutputEncoding));
            transport.Send(new SET_WINDOW_SIZE(cachedWindowWidth, cachedWindowHeight));
            transport.Send(new SET_WINDOW_POS(cachedWindowLeft, cachedWindowTop));
            transport.SendManaged(new SET_TITLE(cachedTitle));
            PublishThemeIfChanged(force: true);
        }

        private void PublishStatusFrame(ConsoleStatusPublication publication)
        {
            if (!transport.IsConnected) {
                return;
            }

            PublishThemeIfChanged(force: false);
            ConsoleStatusFrame frame = publication.Frame;
            transport.SendManaged(new SET_STATUS_BAR(
                publication.Sequence,
                publication.HasFrame ? frame.Text ?? string.Empty : string.Empty,
                publication.HasFrame ? frame.IndicatorFrameIntervalMs : 0,
                publication.HasFrame ? frame.IndicatorStylePrefix ?? string.Empty : string.Empty,
                publication.HasFrame ? frame.IndicatorFrames ?? string.Empty : string.Empty));
        }

        private void PublishThemeIfChanged(bool force)
        {
            string themeJson = JsonSerializer.Serialize(UnifierApi.GetConsolePromptTheme());
            if (!force && string.Equals(cachedThemeJson, themeJson, StringComparison.Ordinal)) {
                return;
            }

            cachedThemeJson = themeJson;
            transport.SendManaged(new SET_CONSOLE_THEME(themeJson));
        }

        #endregion

        #region Read Implementation

        public string? ReadLine(ConsolePromptSpec prompt, bool trim = false)
        {
            return ReadLineWithPrompt(prompt, trim);
        }

        public override string? ReadLine()
        {
            if (ConsolePromptOverride.TryResolvePendingReadLineOverride(this, out ConsolePromptSpec? prompt)) {
                return ReadLineWithPrompt(prompt);
            }

            return readCoordinator.ReadLine();
        }

        public override ConsoleKeyInfo ReadKey()
        {
            return readCoordinator.ReadKey(intercept: false);
        }

        public override ConsoleKeyInfo ReadKey(bool intercept)
        {
            return readCoordinator.ReadKey(intercept);
        }

        public override int Read()
        {
            return readCoordinator.Read();
        }

        private string? ReadLineWithPrompt(ConsolePromptSpec prompt, bool trim = false)
        {
            ArgumentNullException.ThrowIfNull(prompt);
            string? line = readCoordinator.ReadLine(prompt);
            return trim && line is not null ? line.Trim() : line;
        }

        #endregion
    }
}
