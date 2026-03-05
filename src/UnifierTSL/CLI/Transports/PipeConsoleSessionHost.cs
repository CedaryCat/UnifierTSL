using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using UnifierTSL.ConsoleClient.Protocol;
using UnifierTSL.FileSystem;
using UnifierTSL.Servers;

namespace UnifierTSL.CLI.Transports
{
    internal sealed class PipeConsoleSessionHost : IConsoleSessionTransport
    {
        private readonly string pipeName;
        private readonly byte[] readBuffer = new byte[1024 * 1024];
        private readonly Lock stateLock = new();

        private int bufferWritePosition;
        private bool started;
        private bool disposed;

        private Thread? communicationThread;
        private NamedPipeServerStream? pipeServer;
        private Process? clientProcess;

        public event Action<byte, byte[]>? PacketReceived;

        public event Action? Reconnected;

        public bool IsConnected {
            get {
                lock (stateLock) {
                    return pipeServer?.IsConnected == true;
                }
            }
        }

        public PipeConsoleSessionHost(ServerContext ownerServer)
        {
            pipeName = $"USP_Console_{ownerServer.Name}_{ownerServer.UniqueId}";
        }

        public void Start()
        {
            lock (stateLock) {
                if (started || disposed) {
                    return;
                }

                started = true;
                communicationThread = new Thread(CommunicationLoop) {
                    IsBackground = true,
                    Name = $"ConsolePipe:{pipeName}"
                };
                communicationThread.Start();
            }
        }

        public void Send<TPacket>(TPacket packet) where TPacket : unmanaged, IPacket<TPacket>
        {
            NamedPipeServerStream? stream = GetActivePipeServer();
            if (stream is null || !stream.IsConnected) {
                return;
            }

            try {
                IPacket.Write(stream, packet);
            }
            catch {
            }
        }

        public void SendManaged<TPacket>(TPacket packet) where TPacket : struct, IPacket<TPacket>
        {
            NamedPipeServerStream? stream = GetActivePipeServer();
            if (stream is null || !stream.IsConnected) {
                return;
            }

            try {
                IPacket.WriteManaged(stream, packet);
            }
            catch {
            }
        }

        public void Dispose()
        {
            lock (stateLock) {
                if (disposed) {
                    return;
                }

                disposed = true;
            }

            DisposePipeServer();
            DisposeClientProcess(killIfRunning: true);
        }

        private void CommunicationLoop()
        {
            while (true) {
                if (disposed) {
                    return;
                }

                try {
                    StartClientProcess();
                    InitializePipeServer();
                    ListenForMessages();
                }
                catch {
                    if (disposed) {
                        return;
                    }
                }
                finally {
                    DisposePipeServer();
                    DisposeClientProcess(killIfRunning: true);
                    bufferWritePosition = 0;
                }

                if (disposed) {
                    return;
                }

                Thread.Sleep(3000);
            }
        }

        private NamedPipeServerStream? GetActivePipeServer()
        {
            lock (stateLock) {
                return pipeServer;
            }
        }

        private void InitializePipeServer()
        {
            if (disposed) {
                return;
            }

            NamedPipeServerStream stream = new(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            lock (stateLock) {
                pipeServer = stream;
            }

            stream.WaitForConnection();
            Reconnected?.Invoke();
        }

        private void StartClientProcess()
        {
            DisposeClientProcess(killIfRunning: true);
            if (disposed) {
                return;
            }

            string clientExePath = Path.Combine("app", $"{nameof(UnifierTSL)}.{nameof(ConsoleClient)}{FileSystemHelper.GetExecutableExtension()}");
            ProcessStartInfo startInfo = new() {
                FileName = clientExePath,
                Arguments = pipeName,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            Process process = new() {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            process.Start();

            lock (stateLock) {
                clientProcess = process;
            }
        }

        private unsafe void ListenForMessages()
        {
            NamedPipeServerStream? stream = GetActivePipeServer();
            if (stream is null) {
                return;
            }

            const int packetLenSize = sizeof(int);
            const int packetIdSize = 1;
            const int packetHeaderSize = packetLenSize + packetIdSize;
            const int maxPacketSize = 1024 * 1024;

            while (!disposed && stream.IsConnected) {
                int count = stream.Read(readBuffer, bufferWritePosition, readBuffer.Length - bufferWritePosition);
                if (count == 0) {
                    continue;
                }

                bufferWritePosition += count;
                int currentReadPosition = 0;
                int restLen = bufferWritePosition - currentReadPosition;

                fixed (byte* beginPtr = readBuffer) {
                    while (restLen >= packetLenSize) {
                        int packetLen = Unsafe.Read<int>(beginPtr + currentReadPosition);
                        if (packetLen < packetHeaderSize || packetLen > maxPacketSize) {
                            throw new InvalidDataException($"Invalid packet length: {packetLen}");
                        }

                        if (restLen < packetLen) {
                            break;
                        }

                        int contentLength = packetLen - packetHeaderSize;
                        byte packetId = readBuffer[currentReadPosition + packetLenSize];
                        byte[] content = new byte[contentLength];
                        Buffer.BlockCopy(readBuffer, currentReadPosition + packetHeaderSize, content, 0, contentLength);
                        PacketReceived?.Invoke(packetId, content);

                        currentReadPosition += packetLen;
                        restLen -= packetLen;
                    }
                }

                if (restLen > 0) {
                    Buffer.BlockCopy(readBuffer, currentReadPosition, readBuffer, 0, restLen);
                }
                bufferWritePosition = restLen;
            }
        }

        private void DisposePipeServer()
        {
            NamedPipeServerStream? stream;
            lock (stateLock) {
                stream = pipeServer;
                pipeServer = null;
            }

            try {
                stream?.Dispose();
            }
            catch {
            }
        }

        private void DisposeClientProcess(bool killIfRunning)
        {
            Process? process;
            lock (stateLock) {
                process = clientProcess;
                clientProcess = null;
            }

            if (process is null) {
                return;
            }

            try {
                if (killIfRunning && !process.HasExited) {
                    process.Kill();
                }
            }
            catch {
            }
            finally {
                try {
                    process.Dispose();
                }
                catch {
                }
            }
        }
    }
}
