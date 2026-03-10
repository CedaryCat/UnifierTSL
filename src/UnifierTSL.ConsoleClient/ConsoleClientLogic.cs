using System.Text.Json;
using System.Threading.Channels;
using UnifierTSL.ConsoleClient.Protocol;
using UnifierTSL.ConsoleClient.Protocol.HostToClient;
using UnifierTSL.ConsoleClient.Protocol.ClientToHost;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;
using UnifierTSL.ConsoleClient.Shell;

namespace UnifierTSL.ConsoleClient
{
    public static class ConsoleClientLogic
    {
        private readonly record struct PendingReadRequest(
            BEGIN_READ BeginRead,
            ConsoleRenderSnapshot InitialRender);

        private static readonly Channel<PendingReadRequest> readRequests = Channel.CreateUnbounded<PendingReadRequest>();
        private static readonly ConsoleShell shell = new();
        private static long queuedReadOrder = -1;
        private static long activeReadOrder = -1;
        private static long latestStatusSequence = -1;

        private static ConsoleRenderSnapshot ParseRenderJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) {
                return ConsoleRenderSnapshot.CreateCommandLine();
            }

            try {
                return JsonSerializer.Deserialize<ConsoleRenderSnapshot>(json) ?? ConsoleRenderSnapshot.CreateCommandLine();
            }
            catch {
                return ConsoleRenderSnapshot.CreateCommandLine();
            }
        }

        private static ConsolePromptTheme ParseThemeJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) {
                return ConsolePromptTheme.Default;
            }

            try {
                return JsonSerializer.Deserialize<ConsolePromptTheme>(json) ?? ConsolePromptTheme.Default;
            }
            catch {
                return ConsolePromptTheme.Default;
            }
        }

        public static unsafe void ProcessData(ConsoleClient client, byte id, Span<byte> content)
        {
            switch (id) {
                case SET_INPUT_ENCODING.id:
                    Console.InputEncoding = IPacket.ReadUnmanaged<SET_INPUT_ENCODING>(content).Encoding;
                    break;

                case SET_OUTPUT_ENCODING.id:
                    Console.OutputEncoding = IPacket.ReadUnmanaged<SET_OUTPUT_ENCODING>(content).Encoding;
                    break;

                case SET_WINDOW_SIZE.id:
                    SET_WINDOW_SIZE size = IPacket.ReadUnmanaged<SET_WINDOW_SIZE>(content);
                    if (size.Width != 0) {
                        Console.WindowWidth = size.Width;
                    }
                    if (size.Height != 0) {
                        Console.WindowHeight = size.Height;
                    }
                    break;

                case SET_WINDOW_POS.id:
                    SET_WINDOW_POS pos = IPacket.ReadUnmanaged<SET_WINDOW_POS>(content);
                    if (pos.Left != 0) {
                        Console.WindowLeft = pos.Left;
                    }
                    if (pos.Top != 0) {
                        Console.WindowTop = pos.Top;
                    }
                    break;

                case SET_TITLE.id:
                    Console.Title = IPacket.Read<SET_TITLE>(content).Title;
                    break;

                case CLEAR.id:
                    shell.Clear();
                    break;

                case SEND_WRITE_ANSI.id:
                    shell.AppendLog(IPacket.Read<SEND_WRITE_ANSI>(content).Text, isAnsi: true);
                    break;

                case SEND_WRITE_LINE_ANSI.id:
                    shell.AppendLog(IPacket.Read<SEND_WRITE_LINE_ANSI>(content).Text + Environment.NewLine, isAnsi: true);
                    break;

                case SET_CONSOLE_THEME.id:
                    shell.UpdateTheme(ParseThemeJson(IPacket.Read<SET_CONSOLE_THEME>(content).ThemeJson));
                    break;

                case UPDATE_RENDER.id:
                    UPDATE_RENDER renderPacket = IPacket.Read<UPDATE_RENDER>(content);
                    if (Interlocked.Read(ref activeReadOrder) == renderPacket.Order) {
                        shell.UpdateReadLineContext(ParseRenderJson(renderPacket.RenderJson));
                    }
                    break;

                case SET_STATUS_BAR.id:
                    SET_STATUS_BAR statusFramePacket = IPacket.Read<SET_STATUS_BAR>(content);
                    // Status frames can be replayed after reconnect while older packets are still
                    // in flight. Only move the UI sequence forward; otherwise a stale resend can
                    // overwrite a newer frame or resurrect a bar that was already cleared.
                    if (statusFramePacket.Sequence <= Interlocked.Read(ref latestStatusSequence)) {
                        break;
                    }

                    Interlocked.Exchange(ref latestStatusSequence, statusFramePacket.Sequence);
                    if (string.IsNullOrWhiteSpace(statusFramePacket.Text)) {
                        shell.ClearStatusFrame();
                    }
                    else {
                        shell.UpdateStatusFrame(
                            statusFramePacket.Sequence,
                            statusFramePacket.Text,
                            statusFramePacket.IndicatorFrameIntervalMs,
                            statusFramePacket.IndicatorStylePrefix,
                            statusFramePacket.IndicatorFrames);
                    }
                    break;

                case BEGIN_READ.id:
                    BEGIN_READ beginRead = IPacket.Read<BEGIN_READ>(content);
                    // Host side retries BEGIN_READ until CONFIRM_BEGIN_READ arrives, so duplicates
                    // are expected. Confirm first, then ignore the repeated order to avoid starting
                    // a second local Console.Read* for the same server-side waiting call.
                    client.Send(new CONFIRM_BEGIN_READ(beginRead.Flags, beginRead.Order));
                    if (beginRead.Order == Interlocked.Read(ref activeReadOrder)
                        || beginRead.Order == Interlocked.Read(ref queuedReadOrder)) {
                        break;
                    }

                    Interlocked.Exchange(ref queuedReadOrder, beginRead.Order);
                    readRequests.Writer.TryWrite(new PendingReadRequest(
                        beginRead,
                        ParseRenderJson(beginRead.InitialRenderJson)));
                    break;
            }
        }

        public static async Task Run(ConsoleClient client)
        {
            await foreach (PendingReadRequest item in readRequests.Reader.ReadAllAsync()) {
                try {
                    Interlocked.Exchange(ref queuedReadOrder, -1);
                    Interlocked.Exchange(ref activeReadOrder, item.BeginRead.Order);

                    switch (item.BeginRead.Flags) {
                        case ReadFlags.Read:
                            client.Send(new PUSH_READ(Console.Read(), item.BeginRead.Order));
                            break;

                        case ReadFlags.ReadLine:
                            string line = shell.ReadLine(
                                item.InitialRender,
                                onInputStateChanged: state => client.SendManaged(new PUSH_READLINE_INPUT(
                                    item.BeginRead.Order,
                                    state.InputText,
                                    state.CursorIndex,
                                    state.CompletionIndex,
                                    state.CompletionCount,
                                    state.CandidateWindowOffset)));
                            client.SendManaged(new PUSH_READLINE(line, item.BeginRead.Order));
                            break;

                        case ReadFlags.ReadKey:
                            client.SendManaged(new PUSH_READKEY(Console.ReadKey(), item.BeginRead.Order));
                            break;

                        case ReadFlags.ReadKeyIntercept:
                            client.SendManaged(new PUSH_READKEY(Console.ReadKey(true), item.BeginRead.Order));
                            break;
                    }
                }
                finally {
                    Interlocked.Exchange(ref activeReadOrder, -1);
                }
            }
        }
    }
}
