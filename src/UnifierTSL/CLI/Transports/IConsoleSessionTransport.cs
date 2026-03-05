using UnifierTSL.ConsoleClient.Protocol;

namespace UnifierTSL.CLI.Transports
{
    internal interface IConsoleSessionTransport : IDisposable
    {
        bool IsConnected { get; }

        event Action<byte, byte[]>? PacketReceived;

        event Action? Reconnected;

        void Start();

        void Send<TPacket>(TPacket packet) where TPacket : unmanaged, IPacket<TPacket>;

        void SendManaged<TPacket>(TPacket packet) where TPacket : struct, IPacket<TPacket>;
    }
}
