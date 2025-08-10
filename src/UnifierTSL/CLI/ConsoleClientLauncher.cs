using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnifiedServerProcess;
using UnifierTSL.FileSystem;
using UnifierTSL.Servers;

namespace UnifierTSL.CLI
{
    public unsafe partial class ConsoleClientLauncher : ConsoleSystemContext
    {
        private readonly string _pipeName;
        private readonly byte[] readBuffer = new byte[1024 * 1024];
        private int bufferWritePosition = 0;

        private NamedPipeServerStream _pipeServer;
        private Process _clientProcess;
        [MemberNotNullWhen(false, nameof(_clientProcess), nameof(_pipeServer))]
        private bool IsRunning { get; set; } = true;
        private readonly Lock _syncLock = new();
        private void ListenForMessage() {
            try {
                while (IsRunning) {
                    if (!_pipeServer.IsConnected) {
                        Thread.Sleep(100);
                        continue;
                    }

                    const int packetLenSize = sizeof(int);
                    const int packetIdSize = 1;
                    const int packetHeaderSize = packetLenSize + packetIdSize;
                    const int maxPacketSize = 1024 * 1024;

                    while (_pipeServer.IsConnected) {
                        var count = _pipeServer.Read(readBuffer, bufferWritePosition, readBuffer.Length - bufferWritePosition);
                        if (count == 0) continue;

                        bufferWritePosition += count;
                        int currentReadPosition = 0;
                        int restLen = bufferWritePosition - currentReadPosition;

                        fixed (void* beginPtr = readBuffer) {
                            while (restLen >= packetLenSize) {
                                var packetLen = Unsafe.Read<int>((byte*)beginPtr + currentReadPosition);

                                // length check
                                if (packetLen < packetHeaderSize || packetLen > maxPacketSize) {
                                    throw new InvalidDataException($"Invalid packet length: {packetLen}");
                                }

                                if (restLen < packetLen) {
                                    break;
                                }

                                waiting.ProcessData(
                                    readBuffer[currentReadPosition + packetLenSize],
                                    new Span<byte>((byte*)beginPtr + currentReadPosition + packetHeaderSize, packetLen - packetHeaderSize)
                                );

                                currentReadPosition += packetLen;
                                restLen -= packetLen;
                            }
                        }

                        // copy remaining data
                        if (restLen > 0) {
                            for (int i = 0; i < restLen; i++) {
                                readBuffer[i] = readBuffer[currentReadPosition + i];
                            }
                        }
                        bufferWritePosition = restLen;
                    }
                }
            }
            catch {
                if (IsRunning) {
                    // restart pipe server and client process
                    RestartCommunication();
                }
            }
        }
        public ConsoleClientLauncher(ServerContext server) : base(server) {
            _pipeName = $"USP_Console_{server.Name}_{server.UniqueId}";
            waiting = new WaitingData(this);
            RestartCommunication();
        }
        [MemberNotNull(nameof(_pipeServer))]
        private void InitializePipeServer() {
            lock (_syncLock) {
                _pipeServer?.Dispose();

                _pipeServer = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous
                );

                _pipeServer.WaitForConnection();

                try {
                    StartListeningThread();
                }
                catch (ObjectDisposedException) {
                    // Is manually disposed, not need to restart
                }
                catch {
                    if (IsRunning) RestartCommunication();
                }
            }
        }
        [MemberNotNull(nameof(_clientProcess))]
        private void StartClientProcess() {
            lock (_syncLock) {

                // stop old process
                try {
                    if (_clientProcess != null && !_clientProcess.HasExited) {
                        _clientProcess.Kill();
                    }
                }
                catch { }

                // start new process
                var clientExePath = Path.Combine("app", $"{nameof(UnifierTSL)}.{nameof(ConsoleClient)}{FileSystemHelper.GetExecutableExtension()}");
                var startInfo = CreateStartInfo(clientExePath, _pipeName);
                _clientProcess = new() {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                _clientProcess.Exited += (sender, args) => {
                    _pipeServer?.Dispose();
                    if (IsRunning) {
                        Thread.Sleep(1000);
                        RestartCommunication();
                    }
                };
                _clientProcess.Start();
            }
        }
        public static ProcessStartInfo CreateStartInfo(string clientExePath, string pipeName) {
            ProcessStartInfo startInfo;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                // On Windows, UseShellExecute=true will automatically show a console window
                startInfo = new ProcessStartInfo {
                    FileName = clientExePath,
                    Arguments = pipeName,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                var terminal = FindAvailableTerminalLinux();
                if (terminal == null)
                    throw new Exception("No available terminal emulator found (gnome-terminal, xterm, konsole)");

                // The argument format differs depending on the terminal emulator
                string args = terminal switch {
                    "gnome-terminal" => $"--wait -- bash -c \"\\\"{clientExePath}\\\" \\\"{pipeName}\\\"; exit\"",
                    "xterm" => $"-e \"{clientExePath} {pipeName}\"",
                    "konsole" => $"-e \"{clientExePath} {pipeName}\"",
                    _ => throw new Exception("Unknown terminal emulator")
                };

                startInfo = new ProcessStartInfo {
                    FileName = terminal,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                // On macOS, use osascript to open Terminal.app and run the command
                string command = $"{EscapeBashArg(clientExePath)} {EscapeBashArg(pipeName)}";

                string appleScript = $@"
            tell application ""Terminal""
                activate
                do script ""{command}""
            end tell
        ";

                startInfo = new ProcessStartInfo {
                    FileName = "osascript",
                    Arguments = $"-e \"{appleScript.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else {
                throw new PlatformNotSupportedException("The current operating system does not support starting a new terminal");
            }

            return startInfo;
        }

        private static string? FindAvailableTerminalLinux() {
            string[] terminals = { "gnome-terminal", "xterm", "konsole" };
            foreach (var term in terminals) {
                if (IsCommandAvailable(term))
                    return term;
            }
            return null;
        }

        private static bool IsCommandAvailable(string command) {
            try {
                using var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = "which",
                        Arguments = command,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };
                process.Start();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch {
                return false;
            }
        }

        // Escape bash arguments simply by wrapping them in single quotes and escaping existing single quotes
        private static string EscapeBashArg(string arg) {
            return $"'{arg.Replace("'", "'\\''")}'";
        }

        private void StartListeningThread() {
            var listenThread = new Thread(ListenForMessage) {
                IsBackground = true
            };
            listenThread.Start();
        }
        [MemberNotNull(nameof(_clientProcess), nameof(_pipeServer))]
        private void RestartCommunication() {
            lock (_syncLock) {
                if (!IsRunning) return;

                try {
                    _pipeServer?.Dispose();

                    StartClientProcess();
                    InitializePipeServer();

                    if (!string.IsNullOrEmpty(cachedTitle)) {
                        Title = cachedTitle;
                    }
                    waiting.HandleRestart();
                }
                catch {
                    Thread.Sleep(3000);
                    RestartCommunication();
                }
            }
        }
        public override void Dispose(bool disposing) {
            if (disposing) {
                IsRunning = false;
                try {
                    if (_clientProcess != null && !_clientProcess.HasExited) {
                        _clientProcess.Kill();
                    }
                    _clientProcess?.Dispose();
                }
                catch { }
                try {
                    _pipeServer?.Dispose();
                }
                catch { }
            }
            base.Dispose(disposing);
        }
    }
}
