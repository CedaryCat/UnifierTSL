using NuGet.Common;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using Terraria;
using Terraria.Localization;
using Terraria.Net.Sockets;
using TrProtocol;
using TrProtocol.NetPackets;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Extensions;
using UnifierTSL.Logging;
using UnifierTSL.Network;
using UnifierTSL.Servers;

namespace UnifierTSL
{
    public static class UnifiedServerCoordinator
    {
        unsafe class PendingConnection(int index)
        {
            public readonly int Index = index;
            public Player player = new() { whoAmI = index };
            public byte[] readBuffer = new byte[1024 * 8];
            public int totalData = 0;
            public string? RecievedUUID = "";
            public void Reset(ISocket socket) {
                var client = globalClients[Index];
                client.ClientUUID = RecievedUUID = null;
                client.Name = player.name = string.Empty;

                totalData = 0;
                client.Data.Clear();
                client.TimeOutTimer = 0;
                client.StatusCount = 0;
                client.StatusMax = 0;
                client.StatusText2 = "";
                client.StatusText = "";
                client.State = 0;
                client._isReading = false;
                client.PendingTermination = false;
                client.PendingTerminationApproved = false;
                client.SpamClear();
                client.IsActive = false;

                client.Socket?.Close();
                client.Socket = socket;
            }
            public void Update(RemoteClient client) {
                if (!client.IsActive) {
                    client.State = 0;
                    client.IsActive = true;
                }

                if (client._isReading) {
                    return;
                }

                try {
                    if (client.Socket.IsDataAvailable()) {
                        client._isReading = true;
                        client.Socket.AsyncReceive(client.ReadBuffer, 0, client.ReadBuffer.Length, delegate (object state, int length) {
                            if (length == 0) {
                                client.PendingTermination = true;
                            }
                            else {
                                try {
                                    Buffer.BlockCopy(client.ReadBuffer, 0, readBuffer, totalData, length);
                                    totalData += length;
                                    ProcessBytes(client);
                                }
                                catch {
                                    client.PendingTermination = true;
                                }
                            }

                            client._isReading = false;
                        });
                    }
                }
                catch {
                    client.PendingTermination = true;
                }
            }
            void ProcessBytes(RemoteClient client) {

                if (client.PendingTermination) {
                    client.PendingTerminationApproved = true;
                    return;
                }
                int readPosition = 0;
                int unreadLength = totalData;
                try {
                    while (unreadLength >= 2) {
                        int packetLength = BitConverter.ToUInt16(readBuffer, readPosition);
                        if (unreadLength >= packetLength && packetLength != 0) {

                            ProcessPacket(readPosition + 2, packetLength - 2);

                            if (client.PendingTermination) {
                                client.PendingTerminationApproved = true;
                                break;
                            }

                            unreadLength -= packetLength;
                            readPosition += packetLength;

                            var currentServer = GetClientCurrentlyServer(Index);
                            if (currentServer != null) {
                                var buffer = globalMsgBuffers[Index];
                                // If there is unprocessed data remaining, copy it to the beginning of the buffer for the next processing round.
                                // Then update buffer.totalData with the remaining byte count to prevent reprocessing of this data.
                                for (int i = 0; i < unreadLength; i++) {
                                    buffer.readBuffer[i] = readBuffer[readPosition + i];
                                }
                                buffer.totalData = unreadLength;
                                // Make sure remaining data will be processed in the next round on the next server
                                buffer.checkBytes = true;
                                return;
                            }

                            continue;
                        }
                        break;
                    }
                }
                catch (Exception exception) {
                    if (readPosition < readBuffer.Length - 100) {
                        Console.WriteLine(Language.GetTextValue("Error.NetMessageError", readBuffer[readPosition + 2]));
                    }
                    unreadLength = 0;
                    readPosition = 0;

                    Logger.LogHandledException(
                        category: "PendingConnection",
                        message: Language.GetTextValue("Error.NetMessageError", readBuffer[readPosition + 2]),
                        ex: exception);
                }
                if (unreadLength != totalData) {
                    for (int i = 0; i < unreadLength; i++) {
                        readBuffer[i] = readBuffer[i + readPosition];
                    }
                    totalData = unreadLength;
                }
            }

            void ProcessPacket(int packetStart, int contentLength) {
                var sender = clientSenders[Index];
                var client = globalClients[Index];
                var type = (MessageID)readBuffer[packetStart];
                fixed (byte* buffer = readBuffer) {
                    void* readPtr = buffer + packetStart + 1;
                    switch (type) {
                        case MessageID.ClientHello: {
                                var msg = new ClientHello(ref readPtr);
                                if (msg.Version != "Terraria" + 279) {
                                    sender.Kick(Lang.mp[4].ToNetworkText());
                                    break;
                                }
                                if (!string.IsNullOrEmpty(ServerPassword)) {
                                    client.State = -1;
                                    sender.SendFixedPacket(new RequestPassword());
                                    break;
                                }
                                client.State = 1;
                                sender.SendFixedPacket(new LoadPlayer((byte)Index, false));
                                break;
                            }
                        case MessageID.SendPassword: {
                                var msg = new SendPassword(ref readPtr);
                                if (msg.Password == ServerPassword) {
                                    client.State = 1;
                                    sender.SendFixedPacket(new LoadPlayer((byte)Index, true));
                                }
                                else {
                                    sender.Kick(Lang.mp[1].ToNetworkText());
                                }
                                break;
                            }
                        case MessageID.SyncPlayer: {
                                var msg = new SyncPlayer(ref readPtr);
                                player.ApplySyncPlayerPacket(msg);
                                client.Name = msg.Name;
                                break;
                            }
                        case MessageID.ClientUUID: {
                                var msg = new ClientUUID(ref readPtr);
                                client.ClientUUID = RecievedUUID = msg.UUID;
                                var joinServer = SwitchJoinServer?.Invoke(player, client);
                                if (joinServer is null) {
                                    sender.Kick(NetworkText.FromLiteral("Unable to locate an available server to join."));

                                    Logger.Warning(
                                        category: "PendingConnection",
                                        message: $"No available server found for player '{player.name}' ({client.ClientUUID}); connection aborted.");
                                }
                                else {
                                    SetClientCurrentlyServer(Index, joinServer);
                                    var playerData = player.CreateSyncPacket(Index);
                                    joinServer.Main.player[Index].ApplySyncPlayerPacket(in playerData, false);
                                    globalClients[Index].ResetSections(joinServer);

                                    UnifierApi.UpdateTitle();

                                    Logger.Info(
                                        category: "PendingConnection",
                                        message: $"Player '{player.name}' ({client.ClientUUID}) routed to server '{joinServer.Name}'.");
                                }

                                break;
                            }
                        default: {
                                sender.Kick(Lang.mp[2].ToNetworkText());

                                Logger.Warning(
                                    category: "PendingConnection",
                                    message: $"'{client.Name}' ({client.Socket.GetRemoteAddress()}) sent invalid packet {type} before name-uuid anthentication. Kicked.");
                                break;
                            }
                    }
                }
            }
        }
        class LoggerHost : ILoggerHost
        {
            public string Name => "UnifierTSL";
            public string? CurrentLogCategory { get; set; }
        }
        static readonly ILoggerHost LogHost = new LoggerHost();
        static readonly RoleLogger logger;
        public static RoleLogger Logger => logger;
        public static int ListenPort { get; private set; }
        public static string ServerPassword { get; private set; } = "";
        public static readonly RemoteClient[] globalClients = new RemoteClient[Netplay.MaxConnections];
        public static readonly LocalClientSender[] clientSenders = new LocalClientSender[Netplay.MaxConnections];
        public static readonly MessageBuffer[] globalMsgBuffers = new MessageBuffer[Netplay.MaxConnections + 1];
        static readonly ServerContext?[] clientCurrentlyServers = new ServerContext?[Netplay.MaxConnections];
        static readonly PendingConnection[] pendingConnects = new PendingConnection[Netplay.MaxConnections];

        private static ImmutableArray<ServerContext> servers = [];
        public static ImmutableArray<ServerContext> Servers => servers;
        public static void AddServer(ServerContext server) {
            ImmutableInterlocked.Update(ref servers, arr => arr.Add(server));
        }
        public static void RemoveServer(ServerContext server) {
            ImmutableInterlocked.Update(ref servers, arr => arr.Remove(server));
        }
        public static ServerContext? GetClientCurrentlyServer(int clientIndex) => Volatile.Read(ref clientCurrentlyServers[clientIndex]);
        static void SetClientCurrentlyServer(int clientIndex, ServerContext? server) => Volatile.Write(ref clientCurrentlyServers[clientIndex], server);


        static TcpListener listener;
        static volatile bool isListening;
        static readonly UdpClient broadcastClient;
        static volatile bool keepBroadcasting;

        public static event Func<TcpClient, ISocket>? CreateSocket;
        public static event Func<Player, RemoteClient, ServerContext?>? SwitchJoinServer;
        public static event Action? Started;

        public static IPEndPoint ListeningEndpoint { get; private set; }

        static UnifiedServerCoordinator() {
            logger = UnifierApi.CreateLogger(LogHost);

            On.Terraria.NetMessageSystemContext.mfwh_CheckBytes += ProcessBytes;
            On.Terraria.NetplaySystemContext.mfwh_UpdateServerInMainThread += UpdateServerInMainThread;
            On.Terraria.NetMessageSystemContext.CheckCanSend += NetMessageSystemContext_CheckCanSend;

            for (int i = 0; i < Netplay.MaxConnections; i++) {
                globalClients[i] = new RemoteClient() {
                    Id = i,
                    ReadBuffer = new byte[1024]
                };
                pendingConnects[i] = new(i);
                clientSenders[i] = new(i);
            }
            for (int i = 0; i < globalMsgBuffers.Length; i++) {
                globalMsgBuffers[i] = new MessageBuffer() {
                    whoAmI = i
                };
            }
            listener = new TcpListener(ListeningEndpoint = new IPEndPoint(IPAddress.Any, 7777));
            broadcastClient = new UdpClient();
        }
        static Thread? ServerLoopThread;
        public static void Launch(int listenPort, string password = "") {
            ListenPort = listenPort;
            ServerPassword = password;
            listener.Dispose();
            listener = new TcpListener(ListeningEndpoint = new IPEndPoint(IPAddress.Any, listenPort));
            
            broadcastClient.EnableBroadcast = true;

            if (ServerLoopThread is null) {
                ServerLoopThread = new Thread(ServerLoop) {
                    IsBackground = true,
                    Name = "Server Thread"
                };
                ServerLoopThread.Start();
            }
        }

        static bool NetMessageSystemContext_CheckCanSend(On.Terraria.NetMessageSystemContext.orig_CheckCanSend orig, NetMessageSystemContext self, int clientIndex) {
            return GetClientCurrentlyServer(clientIndex) == self.root && globalClients[clientIndex].IsConnected();
        }

        static void UpdateServerInMainThread(On.Terraria.NetplaySystemContext.orig_mfwh_UpdateServerInMainThread orig, NetplaySystemContext self) {
            var server = self.root;
            for (int i = 0; i < Netplay.MaxConnections; i++) {
                if (server != GetClientCurrentlyServer(i)) {
                    continue;
                }
                if (server.NetMessage.buffer[i].checkBytes) {
                    server.NetMessage.CheckBytes(i);
                }
            }
        }

        static void ProcessBytes(On.Terraria.NetMessageSystemContext.orig_mfwh_CheckBytes _, NetMessageSystemContext netMsg, int clientIndex) {
            var server = netMsg.root;
            var buffer = globalMsgBuffers[clientIndex];
            lock (buffer) {
                if (server.Main.dedServ && server.Netplay.Clients[clientIndex].PendingTermination) {
                    server.Netplay.Clients[clientIndex].PendingTerminationApproved = true;
                    buffer.checkBytes = false;
                    return;
                }
                int readPosition = 0;
                int unreadLength = buffer.totalData;
                try {
                    while (unreadLength >= 2) {
                        int packetLength = BitConverter.ToUInt16(buffer.readBuffer, readPosition);
                        if (unreadLength >= packetLength && packetLength != 0) {

                            // buffer.GetData(server, readPosition + 2, packetLength - 2, out var _);
                            NetPacketRegister.ProcessBytes(server, buffer, readPosition + 2, packetLength - 2);

                            if (server.Main.dedServ && server.Netplay.Clients[clientIndex].PendingTermination) {
                                server.Netplay.Clients[clientIndex].PendingTerminationApproved = true;
                                buffer.checkBytes = false;
                                break;
                            }

                            unreadLength -= packetLength;
                            readPosition += packetLength;

                            if (GetClientCurrentlyServer(clientIndex) != server) {
                                // If there is unprocessed data remaining, copy it to the beginning of the buffer for the next processing round.
                                // Then update buffer.totalData with the remaining byte count to prevent reprocessing of this data.
                                for (int i = 0; i < unreadLength; i++) {
                                    buffer.readBuffer[i] = buffer.readBuffer[readPosition + i];
                                }
                                buffer.totalData = unreadLength;
                                // Make sure remaining data will be processed in the next round on the next server
                                buffer.checkBytes = true;
                                return;
                            }

                            continue;
                        }
                        break;
                    }
                }
                catch (Exception exception) {
                    if (server.Main.dedServ && readPosition < globalMsgBuffers.Length - 100) {
                        server.Console.WriteLine(Language.GetTextValue("Error.NetMessageError", globalMsgBuffers[readPosition + 2]));
                    }
                    unreadLength = 0;
                    readPosition = 0;
                    server.Hooks.NetMessage.InvokeCheckBytesException(exception);
                }
                if (unreadLength != buffer.totalData) {
                    for (int i = 0; i < unreadLength; i++) {
                        buffer.readBuffer[i] = buffer.readBuffer[i + readPosition];
                    }
                    buffer.totalData = unreadLength;
                }
                buffer.checkBytes = false;
            }
        }


        static void ServerLoop() {
            int sleepStep = 0;
            Started?.Invoke();
            while (true) {
                StartListeningIfNeeded();
                UpdateConnectedClients();
                sleepStep = (sleepStep + 1) % 10;
                Thread.Sleep(sleepStep == 0 ? 1 : 0);
            }
        }

        static void UpdateConnectedClients() {
            try {
                for (int i = 0; i < Netplay.MaxConnections; i++) {
                    var client = globalClients[i];
                    var server = GetClientCurrentlyServer(i);
                    if (server is not null) {
                        if (client.PendingTermination) {
                            if (client.PendingTerminationApproved) {
                                // client.Reset(main);
                                server.NetMessage.SyncDisconnectedPlayer(i);

                                bool active = server.Main.player[i].active;
                                server.Main.player[i].active = false;
                                if (active) {
                                    server.Player.Hooks.PlayerDisconnect(i);
                                }

                                // SetClientCurrentlyServer(i, main);
                            }
                            continue;
                        }
                        if (client.IsConnected()) {
                            lock (client) {
                                client.Update(server);
                                server.Netplay.HasClients = true;
                            }
                            continue;
                        }
                    }
                    else {
                        if (client.IsConnected()) {
                            lock (client) {
                                pendingConnects[i].Update(client);
                            }
                            continue;
                        }
                    }
                    if (client.IsActive) {
                        client.PendingTermination = true;
                        client.PendingTerminationApproved = true;
                        continue;
                    }
                    client.StatusText2 = "";
                }
            }
            catch (Exception ex) {
                Console.WriteLine(ex);
            }
        }

        public static int GetClientSpace() {
            int space = Main.maxPlayers;
            for (int i = 0; i < Main.maxPlayers; i++) {
                if (globalClients[i].IsActive) {
                    space -= 1;
                }
            }
            return space;
        }
        public static int GetActiveClientCount() {
            int count = 0;
            for (int i = 0; i < Main.maxPlayers; i++) {
                if (globalClients[i].IsActive) {
                    count += 1;
                }
            }
            return count;
        }

        static void StartListeningIfNeeded() {
            if (isListening || !servers.Any(s => s.IsRunning) || GetClientSpace() <= 0) {
                return;
            }
            try {
                isListening = true;
                listener.Start();
            }
            catch {
                isListening = false;
                return;
            }
            Task.Run(ListenLoop);
            Task.Run(LaunchBroadcast);
        }
        static void ListenLoop() {
            while (servers.Any(s => s.IsRunning) && GetClientSpace() > 0) {
                try {
                    if (listener.Pending()) {
                        var client = listener.AcceptTcpClient();
                        var socket = CreateSocket?.Invoke(client) ?? new TcpSocket(client);
                        OnConnectionAccepted(socket);
                    }
                }
                catch {
                }
            }
            listener.Stop();
            isListening = false;
            keepBroadcasting = false;
        }
        static void LaunchBroadcast() {
            try {
                keepBroadcasting = true;
                int playerCountPosInStream = 0;
                byte[] data;
                using (MemoryStream memoryStream = new MemoryStream()) {
                    using (BinaryWriter bw = new BinaryWriter(memoryStream)) {
                        int value = 1010;
                        bw.Write(value);
                        bw.Write(ListenPort);
                        bw.Write("Unified-Server-UpdateDependencies");
                        string text = Dns.GetHostName();
                        if (text == "localhost") {
                            text = Environment.MachineName;
                        }
                        bw.Write(text);
                        // maxTilesX
                        bw.Write(ushort.MaxValue);
                        // HasCrimson
                        bw.Write(true);
                        // GameMode
                        bw.Write(2);
                        bw.Write(255);
                        playerCountPosInStream = (int)memoryStream.Position;
                        bw.Write((byte)0);
                        // hardMode
                        bw.Write(true);
                        bw.Flush();
                        data = memoryStream.ToArray();
                    }
                }
                do {
                    data[playerCountPosInStream] = (byte)GetActiveClientCount();
                    try {
                        broadcastClient.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, 8888));
                    }
                    catch {
                    }
                    Thread.Sleep(1000);
                }
                while (keepBroadcasting);
            }
            catch {
                keepBroadcasting = false;
            }
        }

        static void OnConnectionAccepted(ISocket client) {
            int id = FindNextEmptyClientSlot();
            if (id != -1) {
                SetClientCurrentlyServer(id, null);
                pendingConnects[id].Reset(client);

                Logger.Info(
                    category: "ConnectionAccept",
                    message: $"Accepted connection: {client.GetRemoteAddress()}");
            }
            else {
                var sender = new TmpSocketSender(client);
                sender.Kick(NetworkText.FromKey("CLI.ServerIsFull"), true);

                Logger.Info(
                    category: "ConnectionAccept",
                    message: "Server is full");
            }
            if (FindNextEmptyClientSlot() == -1) {
                listener.Stop();
                isListening = false;

                Logger.Info(
                    category: "ConnectionAccept",
                    message: "No more slots available, stopping listener");
            }
        }
        static int FindNextEmptyClientSlot() {
            for (int i = 0; i < Main.maxPlayers; i++) {
                if (!globalClients[i].IsConnected()) {
                    return i;
                }
            }
            return -1;
        }

        public static void TransferPlayerToServer(byte plr, ServerContext to) {
            var from = GetClientCurrentlyServer(plr);

            if (from is null) {
                return;
            }
            if (from == to) {
                return;
            }
            if (!to.IsRunning) {
                return;
            }

            var client = globalClients[plr];
            lock (client) {

                // Leave data sync
                from.SyncPlayerLeaveToOthers(plr);
                from.SyncServerOfflineToPlayer(plr);
                from.Console.WriteLine($"[USP] Player '{from.Main.player[plr].name}' transferred to {to.Name}, current players: {to.NPC.GetActivePlayerCount()}");

                // Player state swap
                var inactivePlayer = to.Main.player[plr];
                var activePlayer = from.Main.player[plr];
                from.Main.player[plr] = inactivePlayer;
                to.Main.player[plr] = activePlayer;
                inactivePlayer.active = false;
                activePlayer.active = true;

                // Update current server
                SetClientCurrentlyServer(plr, to);
                client.ResetSections(to);

                // Join data sync
                to.SyncServerOnlineToPlayer(plr);
                to.SyncPlayerJoinToOthers(plr);
                to.Console.WriteLine($"[USP] Player '{to.Main.player[plr].name}' joined from {from.Name}, current players: {to.NPC.GetActivePlayerCount()}");

                // Log
                Logger.Info(
                    category: "TransferPlayerToServer",
                    message: $"Player '{to.Main.player[plr].name}' {from.Name} → {to.Name} transferred.");
            }
        }
    }
}
