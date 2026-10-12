using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;

namespace GameConsole.Networking {
    public sealed class GameConsoleConnectionOptions {
        public string Host { get; set; } = "127.0.0.1";
        public int TcpPort { get; set; } = ConsoleProtocol.DefaultTcpPort;
        public string PlayerName { get; set; } = "玩家";
        public bool UseTls { get; set; }
        public bool EnableRealtime { get; set; } = true;
        public int ConnectTimeoutSeconds { get; set; } = 10;
        public int RequestTimeoutSeconds { get; set; } = 10;
    }

    // Socket I/O runs independently of the game. Only Pump() invokes gameplay/UI callbacks.
    // Create a fresh client after disconnecting; room membership belongs to the current TCP session.
    public sealed class GameConsoleClient : IDisposable, IAsyncDisposable {
        readonly CancellationTokenSource m_stop = new();
        readonly ConcurrentDictionary<Guid, TaskCompletionSource<ControlMessage>> m_pending = new();
        readonly Channel<ControlMessage> m_inbound = Channel.CreateBounded<ControlMessage>(new BoundedChannelOptions(512) {
            SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
        });
        readonly Channel<ControlMessage> m_notifications = Channel.CreateBounded<ControlMessage>(new BoundedChannelOptions(512) {
            SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
        });
        readonly Channel<RealtimePacket> m_outboundRealtime = Channel.CreateBounded<RealtimePacket>(new BoundedChannelOptions(1) {
            SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest
        });
        readonly ConcurrentDictionary<Guid, RealtimePacket> m_latestRealtime = new();
        readonly Dictionary<Guid, uint> m_realtimeSequences = new();
        readonly TaskCompletionSource<ControlMessage> m_welcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly object m_readyLock = new();
        TaskCompletionSource<bool> m_realtimeReady = NewReadySource();
        Task m_worker = Task.CompletedTask;
        TcpClient m_socket;
        TcpMessageConnection m_tcp;
        NetManager m_udp;
        NetPeer m_peer;
        GameConsoleConnectionOptions m_options;
        volatile RoomSnapshot m_room;
        volatile bool m_connected, m_udpConnected;
        volatile string m_lastError = "";
        string m_token;
        bool m_serverUdpBound;
        int m_udpPort;
        string m_udpHost;
        long m_nextUdpAttempt;
        uint m_sendSequence;
        int m_started, m_closed;

        public Guid PlayerId { get; private set; }
        public bool IsConnected => m_connected;
        public bool IsRealtimeConnected => m_udpConnected;
        public RoomSnapshot Room => m_room;
        public string LastError => m_lastError;
        public int RealtimeLatencyMilliseconds { get; private set; }
        public event Action<ControlMessage> MessageReceived;
        public event Action<RealtimePacket> RealtimeReceived;
        public event Action<string> Disconnected;
        bool m_disconnectDispatched;

        public async Task ConnectAsync(GameConsoleConnectionOptions options, CancellationToken cancellationToken = default) {
            if (options == null || string.IsNullOrWhiteSpace(options.Host) || options.TcpPort is < 1 or > 65535
                || options.ConnectTimeoutSeconds is < 1 or > 120 || options.RequestTimeoutSeconds is < 1 or > 120)
                throw new ArgumentException("Invalid connection options.");
            if (Volatile.Read(ref m_closed) != 0 || Interlocked.CompareExchange(ref m_started, 1, 0) != 0)
                throw new InvalidOperationException("Create a new client for each connection.");
            // Copy options so external edits cannot race the networking loop.
            m_options = new GameConsoleConnectionOptions {
                Host = options.Host, TcpPort = options.TcpPort, PlayerName = options.PlayerName, UseTls = options.UseTls,
                EnableRealtime = options.EnableRealtime, ConnectTimeoutSeconds = options.ConnectTimeoutSeconds,
                RequestTimeoutSeconds = options.RequestTimeoutSeconds
            };
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(m_options.ConnectTimeoutSeconds));
            try {
                m_socket = new TcpClient { NoDelay = true };
                await m_socket.ConnectAsync(m_options.Host, m_options.TcpPort, timeout.Token).ConfigureAwait(false);
                System.IO.Stream stream = m_socket.GetStream();
                if (m_options.UseTls) {
                    SslStream tls = new(stream, false);
                    await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = m_options.Host }, timeout.Token)
                        .ConfigureAwait(false);
                    stream = tls;
                }
                m_tcp = new TcpMessageConnection(m_socket, stream);
                if (!m_tcp.TrySend(new ControlMessage { Type = MessageType.Hello, PlayerName = m_options.PlayerName }))
                    throw new IOException("Cannot send handshake.");
                m_worker = RunAsync();
                await m_welcome.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) {
                m_lastError = exception.Message;
                Dispose();
                m_tcp?.Dispose();
                m_socket?.Dispose();
                throw;
            }
        }

        public async Task WaitForRealtimeAsync(CancellationToken cancellationToken = default) {
            if (m_options?.EnableRealtime == false) throw new InvalidOperationException("Realtime transport is disabled.");
            Task ready;
            lock (m_readyLock) ready = m_realtimeReady.Task;
            await ready.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<RoomSummary[]> ListRoomsAsync(string gameId = null, CancellationToken cancellationToken = default) =>
            (await RequestAsync(new ControlMessage { Type = MessageType.ListRooms, GameId = gameId }, cancellationToken).ConfigureAwait(false)).Rooms;

        public async Task<RoomSnapshot> CreateRoomAsync(string gameId, string name, int capacity = 2, bool requiresUdp = false,
            CancellationToken cancellationToken = default) => (await RequestAsync(new ControlMessage {
                Type = MessageType.CreateRoom, GameId = gameId, RoomName = name, Capacity = capacity, RequiresUdp = requiresUdp
            }, cancellationToken).ConfigureAwait(false)).Room;

        public async Task<RoomSnapshot> JoinRoomAsync(string roomId, CancellationToken cancellationToken = default) =>
            (await RequestAsync(new ControlMessage { Type = MessageType.JoinRoom, RoomId = roomId }, cancellationToken).ConfigureAwait(false)).Room;

        public Task<ControlMessage> LeaveRoomAsync(CancellationToken cancellationToken = default) =>
            RequestAsync(new ControlMessage { Type = MessageType.LeaveRoom }, cancellationToken);

        public Task<ControlMessage> SetReadyAsync(bool ready, CancellationToken cancellationToken = default) =>
            RequestAsync(new ControlMessage { Type = MessageType.SetReady, Ready = ready }, cancellationToken);

        public Task<ControlMessage> StartMatchAsync(CancellationToken cancellationToken = default) =>
            RequestAsync(new ControlMessage { Type = MessageType.StartMatch }, cancellationToken);

        public Task<ControlMessage> EndMatchAsync(CancellationToken cancellationToken = default) =>
            RequestAsync(new ControlMessage { Type = MessageType.EndMatch }, cancellationToken);

        public Task<ControlMessage> SendReliableAsync(ushort opcode, byte[] payload, Guid targetPlayerId = default,
            CancellationToken cancellationToken = default) {
            if (payload == null || payload.Length > ConsoleProtocol.MaxReliablePayloadBytes) throw new ArgumentException("Reliable payload limit: 16 KiB.");
            RoomSnapshot room = m_room;
            if (room?.Started != true) throw new InvalidOperationException("No active match.");
            return RequestAsync(new ControlMessage {
                Type = MessageType.Reliable, Opcode = opcode, Payload = payload, MatchId = room.MatchId, TargetPlayerId = targetPlayerId
            }, cancellationToken);
        }

        // A complete input/state sample per call. Older unsent samples are replaced by the newest one.
        public bool SendRealtime(ushort opcode, byte[] payload) {
            if (payload == null || payload.Length > ConsoleProtocol.MaxRealtimePayloadBytes) throw new ArgumentException("Realtime payload limit: 900 bytes.");
            RoomSnapshot room = m_room;
            if (!m_connected || !m_udpConnected || room?.Started != true) return false;
            return m_outboundRealtime.Writer.TryWrite(new RealtimePacket {
                Opcode = opcode, Payload = (byte[])payload.Clone(), MatchId = room.MatchId, SenderId = PlayerId
            });
        }

        async Task<ControlMessage> RequestAsync(ControlMessage request, CancellationToken cancellationToken) {
            if (!m_connected) throw new InvalidOperationException("Not connected.");
            request.RequestId = Guid.NewGuid();
            TaskCompletionSource<ControlMessage> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
            m_pending.TryAdd(request.RequestId, response);
            try {
                if (!m_tcp.TrySend(request)) throw new IOException("Connection closed or send queue full.");
                return await response.Task.WaitAsync(TimeSpan.FromSeconds(m_options.RequestTimeoutSeconds), cancellationToken).ConfigureAwait(false);
            }
            finally { m_pending.TryRemove(request.RequestId, out _); }
        }

        public void Pump(int maxMessages = 128) {
            if (maxMessages <= 0) return;
            for (int i = 0; i < maxMessages && m_notifications.Reader.TryRead(out ControlMessage message); i++) MessageReceived?.Invoke(message);
            foreach (Guid sender in m_latestRealtime.Keys) {
                if (m_latestRealtime.TryRemove(sender, out RealtimePacket packet) && m_room?.MatchId == packet.MatchId)
                    RealtimeReceived?.Invoke(packet);
            }
            if (Volatile.Read(ref m_started) != 0 && m_worker.IsCompleted && !m_disconnectDispatched) {
                m_disconnectDispatched = true;
                Disconnected?.Invoke(m_lastError);
            }
        }

        async Task RunAsync() {
            Task reader = ReadLoopAsync();
            long nextPing = Environment.TickCount64;
            long lastTcpPacket = nextPing;
            try {
                while (!m_stop.IsCancellationRequested) {
                    for (int i = 0; i < 256 && m_inbound.Reader.TryRead(out ControlMessage message); i++) {
                        lastTcpPacket = Environment.TickCount64;
                        HandleControl(message);
                    }
                    m_udp?.PollEvents();
                    long now = Environment.TickCount64;
                    if (m_connected && m_udp != null && m_peer == null && now >= m_nextUdpAttempt) ConnectUdp();
                    while (m_outboundRealtime.Reader.TryRead(out RealtimePacket packet)) {
                        if (m_udpConnected && m_peer?.ConnectionState == ConnectionState.Connected && m_room?.MatchId == packet.MatchId) {
                            packet.Sequence = ++m_sendSequence;
                            m_peer.Send(RealtimePacketCodec.Encode(packet), (byte)0, DeliveryMethod.Sequenced);
                        }
                    }
                    if (m_connected && now >= nextPing) {
                        nextPing = now + 5000;
                        if (!m_tcp.TrySend(new ControlMessage { Type = MessageType.Ping })) throw new IOException("TCP send queue full.");
                    }
                    if (now - lastTcpPacket > 30000) throw new IOException("Server heartbeat timed out.");
                    if (reader.IsCompleted && !m_inbound.Reader.TryPeek(out _)) { await reader.ConfigureAwait(false); break; }
                    await Task.Delay(10, m_stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (m_stop.IsCancellationRequested) { }
            catch (Exception exception) { m_lastError = exception.Message; }
            finally {
                m_stop.Cancel();
                m_tcp.Dispose();
                m_udp?.Stop();
                m_connected = false;
                m_room = null;
                m_latestRealtime.Clear();
                SetUdpReady(false);
                IOException disconnected = new(string.IsNullOrEmpty(m_lastError) ? "Disconnected." : m_lastError);
                m_welcome.TrySetException(disconnected);
                lock (m_readyLock) m_realtimeReady.TrySetCanceled(m_stop.Token);
                foreach (TaskCompletionSource<ControlMessage> response in m_pending.Values) response.TrySetException(disconnected);
                try { await reader.ConfigureAwait(false); } catch (Exception) { }
            }
        }

        async Task ReadLoopAsync() {
            while (!m_stop.IsCancellationRequested) {
                ControlMessage message = await m_tcp.ReadAsync(m_stop.Token).ConfigureAwait(false);
                if (message == null) return;
                await m_inbound.Writer.WriteAsync(message, m_stop.Token).ConfigureAwait(false);
            }
        }

        void HandleControl(ControlMessage message) {
            if (!m_connected && message.Type != MessageType.Welcome) throw new IOException("Invalid server handshake.");
            if (message.Type == MessageType.Welcome) {
                if (m_connected || message.ProtocolVersion != ConsoleProtocol.Version || message.PlayerId == Guid.Empty
                    || message.SessionToken?.Length != 64 || message.UdpPort is < 1 or > 65535) throw new IOException("Invalid welcome message.");
                PlayerId = message.PlayerId;
                m_token = message.SessionToken;
                m_udpPort = message.UdpPort;
                m_udpHost = string.IsNullOrWhiteSpace(message.UdpHost) ? m_options.Host : message.UdpHost.Trim();
                m_connected = true;
                if (m_options.EnableRealtime) InitializeUdp();
                m_welcome.TrySetResult(message);
            }
            if (message.Type is MessageType.RoomChanged or MessageType.MatchStarted or MessageType.MatchEnded) {
                Guid previousMatch = m_room?.MatchId ?? Guid.Empty;
                m_room = message.Room;
                if (previousMatch != (m_room?.MatchId ?? Guid.Empty)) { m_realtimeSequences.Clear(); m_latestRealtime.Clear(); }
            }
            if (message.Type == MessageType.UdpStatus) {
                m_serverUdpBound = message.UdpConnected;
                SetUdpReady(m_serverUdpBound && m_peer?.ConnectionState == ConnectionState.Connected);
            }
            if (message.RequestId != Guid.Empty && m_pending.TryRemove(message.RequestId, out TaskCompletionSource<ControlMessage> response)) {
                if (message.Type == MessageType.Error) response.TrySetException(new NetworkRequestException(message.Code, message.Message));
                else response.TrySetResult(message);
            }
            if (message.Type != MessageType.Pong && !m_notifications.Writer.TryWrite(message)) throw new IOException("Call Pump() regularly: notification queue full.");
        }

        void InitializeUdp() {
            EventBasedNetListener listener = new();
            m_udp = new NetManager(listener) {
                AutoRecycle = true, IPv6Enabled = false, ChannelsCount = ConsoleProtocol.RealtimeChannelCount,
                DisconnectTimeout = 10000, MaxConnectAttempts = 5, ReconnectDelay = 1000, PingInterval = 1000
            };
            listener.PeerConnectedEvent += peer => {
                m_peer = peer;
                SetUdpReady(m_serverUdpBound);
            };
            listener.PeerDisconnectedEvent += (peer, _) => {
                if (m_peer != peer) return;
                m_peer = null;
                m_serverUdpBound = false;
                m_nextUdpAttempt = Environment.TickCount64 + 3000;
                SetUdpReady(false);
            };
            listener.NetworkReceiveEvent += (peer, reader, channel, delivery) => {
                if (peer != m_peer || delivery != DeliveryMethod.Sequenced
                    || !RealtimePacketCodec.TryDecode(reader.GetRemainingBytesSpan(), out RealtimePacket packet)
                    || m_room?.Started != true || packet.MatchId != m_room.MatchId
                    || !(packet.SenderId == Guid.Empty && channel == ConsoleProtocol.ServerRealtimeChannel
                        || m_room.Players.Any(player => player.PlayerId == packet.SenderId && player.Seat == channel))) return;
                if (m_realtimeSequences.TryGetValue(packet.SenderId, out uint previous) && !RealtimePacketCodec.IsNewer(packet.Sequence, previous)) return;
                m_realtimeSequences[packet.SenderId] = packet.Sequence;
                m_latestRealtime[packet.SenderId] = packet;
            };
            listener.NetworkLatencyUpdateEvent += (_, latency) => RealtimeLatencyMilliseconds = latency;
            if (!m_udp.Start()) {
                m_udp.Stop();
                m_udp = null;
                return;
            }
            ConnectUdp();
        }

        void ConnectUdp() {
            NetDataWriter credentials = new();
            credentials.Put(ConsoleProtocol.ApplicationKey);
            credentials.Put(PlayerId.ToString());
            credentials.Put(m_token);
            m_nextUdpAttempt = Environment.TickCount64 + 8000;
            m_peer = m_udp.Connect(m_udpHost ?? m_options.Host, m_udpPort, credentials);
        }

        void SetUdpReady(bool ready) {
            lock (m_readyLock) {
                if (m_udpConnected == ready) return;
                m_udpConnected = ready;
                if (ready) m_realtimeReady.TrySetResult(true);
                else m_realtimeReady = NewReadySource();
            }
        }

        static TaskCompletionSource<bool> NewReadySource() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() {
            if (Interlocked.Exchange(ref m_closed, 1) != 0) return;
            m_stop.Cancel();
            m_welcome.TrySetCanceled(m_stop.Token);
            lock (m_readyLock) m_realtimeReady.TrySetCanceled(m_stop.Token);
            m_tcp?.Dispose();
            m_socket?.Dispose();
        }

        public async ValueTask DisposeAsync() {
            Dispose();
            await m_worker.ConfigureAwait(false);
        }
    }
}
