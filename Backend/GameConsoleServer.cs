using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GameConsole.Networking;
using LiteNetLib;

namespace GameConsole.Backend {
    public sealed class GameConsoleServer {
        sealed record ServerEvent(ClientSession Session, ControlMessage Message, bool Connected);
        readonly ServerOptions m_options;
        readonly Dictionary<Guid, ClientSession> m_clients = new();
        readonly Dictionary<string, Room> m_rooms = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Func<GameRoomContext, GameRoomHandler>> m_games = new(StringComparer.Ordinal);
        readonly ConcurrentDictionary<Guid, ClientSession> m_sockets = new();
        readonly ConcurrentDictionary<Guid, Task> m_workers = new();
        readonly Channel<ServerEvent> m_events = Channel.CreateBounded<ServerEvent>(new BoundedChannelOptions(2048) {
            SingleReader = true, FullMode = BoundedChannelFullMode.Wait
        });
        readonly SemaphoreSlim m_slots;
        readonly NetManager m_udp;
        readonly TcpListener m_tcp;
        X509Certificate2 m_certificate;
        bool m_running;

        public void RegisterGame(string gameId, Func<GameRoomContext, GameRoomHandler> factory) {
            if (m_running) throw new InvalidOperationException("Register games before RunAsync().");
            if (gameId == null || !Regex.IsMatch(gameId, "\\A[a-z0-9._-]{1,32}\\z") || factory == null)
                throw new ArgumentException("Invalid game registration.");
            m_games.Add(gameId, factory);
        }

        public GameConsoleServer(ServerOptions options) {
            options.Validate();
            m_options = options;
            IPAddress address = IPAddress.Parse(options.BindAddress);
            if (address.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("Use an IPv4 bind address.");
            m_slots = new SemaphoreSlim(options.MaxClients);
            m_tcp = new TcpListener(address, options.TcpPort);
            EventBasedNetListener listener = new();
            m_udp = new NetManager(listener) {
                AutoRecycle = true, IPv6Enabled = false, ChannelsCount = ConsoleProtocol.RealtimeChannelCount,
                DisconnectTimeout = 10000, PingInterval = 1000, UpdateTime = 15
            };
            listener.ConnectionRequestEvent += OnUdpConnectionRequest;
            listener.PeerConnectedEvent += OnUdpConnected;
            listener.PeerDisconnectedEvent += (peer, _) => OnUdpDisconnected(peer);
            listener.NetworkReceiveEvent += (peer, reader, channel, delivery) => {
                if (channel == 0 && delivery == DeliveryMethod.Sequenced)
                    OnRealtime(peer, reader.GetRemainingBytesSpan());
            };
            listener.NetworkErrorEvent += (endpoint, error) => Console.WriteLine($"UDP socket error: {error} ({endpoint})");
        }

        public async Task RunAsync(CancellationToken cancellationToken) {
            if (m_running) throw new InvalidOperationException("Server instance has already been started.");
            m_running = true;
            using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task accept = Task.CompletedTask;
            try {
                if (!string.IsNullOrEmpty(m_options.TlsCertificatePath)) {
                    m_certificate = X509CertificateLoader.LoadPkcs12FromFile(m_options.TlsCertificatePath,
                        Environment.GetEnvironmentVariable(m_options.TlsPasswordEnvironmentVariable));
                    if (!m_certificate.HasPrivateKey) throw new InvalidOperationException("TLS certificate needs a private key.");
                }
                m_tcp.Start(m_options.MaxClients);
                if (!m_udp.Start(m_options.BindAddress, "::", m_options.UdpPort)) throw new IOException("Cannot bind UDP port.");
                Console.WriteLine($"游戏机后端协议 v{ConsoleProtocol.Version}，TCP {m_options.BindAddress}:{m_options.TcpPort}, "
                    + $"UDP :{m_options.UdpPort}, TLS {(m_certificate != null ? "on" : "off")}");
                accept = AcceptLoopAsync(stop.Token);
                using PeriodicTimer timer = new(TimeSpan.FromSeconds(1d / m_options.TickRate));
                long lastSweep = 0;
                long lastTick = Environment.TickCount64;
                while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false)) {
                    for (int i = 0; i < 1024 && m_events.Reader.TryRead(out ServerEvent item); i++) {
                        if (item.Connected) m_clients.Add(item.Session.PlayerId, item.Session);
                        else if (item.Message == null) RemoveClient(item.Session);
                        else if (m_clients.ContainsKey(item.Session.PlayerId)) HandleMessage(item.Session, item.Message);
                    }
                    m_udp.PollEvents();
                    long now = Environment.TickCount64;
                    float deltaTime = Math.Min(0.1f, (now - lastTick) / 1000f);
                    lastTick = now;
                    foreach (Room room in m_rooms.Values.ToArray()) {
                        if (room.Started && room.Handler != null) RunGameAction(room, () => room.Handler.Tick(deltaTime));
                    }
                    if (now - lastSweep >= 1000) {
                        lastSweep = now;
                        foreach (ClientSession session in m_clients.Values.ToArray()) {
                            long deadline = session.Authenticated ? m_options.IdleTimeoutSeconds * 1000L : m_options.HandshakeTimeoutSeconds * 1000L;
                            long last = session.Authenticated ? session.LastSeen : session.ConnectedAt;
                            if (now - last > deadline) Disconnect(session);
                        }
                        foreach (Guid id in m_workers.Where(entry => entry.Value.IsCompleted).Select(entry => entry.Key).ToArray())
                            m_workers.TryRemove(id, out _);
                    }
                    if (accept.IsCompleted) await accept.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally {
                stop.Cancel();
                m_tcp.Stop();
                foreach (Room room in m_rooms.Values.ToArray()) {
                    if (room.Started) FinishMatch(room, "server_shutdown");
                }
                foreach (ClientSession session in m_sockets.Values) {
                    session.Connection?.Dispose();
                    session.Socket.Dispose();
                }
                m_udp.Stop();
                try { await accept.ConfigureAwait(false); }
                catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException) { }
                await Task.WhenAll(m_workers.Values).ConfigureAwait(false);
                m_certificate?.Dispose();
                Console.WriteLine("后端已停止。");
            }
        }

        async Task AcceptLoopAsync(CancellationToken cancellationToken) {
            while (!cancellationToken.IsCancellationRequested) {
                TcpClient socket = await m_tcp.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                if (!m_slots.Wait(0)) { socket.Dispose(); continue; }
                socket.NoDelay = true;
                ClientSession session = new() { Socket = socket };
                m_sockets.TryAdd(session.PlayerId, session);
                m_workers[session.PlayerId] = ServeClientAsync(session, cancellationToken);
            }
        }

        async Task ServeClientAsync(ClientSession session, CancellationToken cancellationToken) {
            try {
                Stream stream = session.Socket.GetStream();
                if (m_certificate != null) {
                    SslStream tls = new(stream, false);
                    using CancellationTokenSource handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    handshake.CancelAfter(TimeSpan.FromSeconds(m_options.HandshakeTimeoutSeconds));
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
                        ServerCertificate = m_certificate, ClientCertificateRequired = false
                    }, handshake.Token).ConfigureAwait(false);
                    stream = tls;
                }
                session.Connection = new TcpMessageConnection(session.Socket, stream);
                await m_events.Writer.WriteAsync(new ServerEvent(session, null, true), cancellationToken).ConfigureAwait(false);
                while (!cancellationToken.IsCancellationRequested) {
                    ControlMessage message = await session.Connection.ReadAsync(cancellationToken).ConfigureAwait(false);
                    if (message == null) break;
                    await m_events.Writer.WriteAsync(new ServerEvent(session, message, false), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException
                or ObjectDisposedException or System.Text.Json.JsonException or System.Security.Authentication.AuthenticationException) { }
            finally {
                session.Connection?.Dispose();
                session.Socket.Dispose();
                m_sockets.TryRemove(session.PlayerId, out _);
                m_slots.Release();
                try { await m_events.Writer.WriteAsync(new ServerEvent(session, null, false), cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }

        void HandleMessage(ClientSession session, ControlMessage message) {
            if (!session.AllowPacket(false, m_options.TcpMessagesPerSecond)) { Disconnect(session); return; }
            session.LastSeen = Environment.TickCount64;
            if (!session.Authenticated) {
                if (message.Type != MessageType.Hello || message.ProtocolVersion != ConsoleProtocol.Version) {
                    Disconnect(session);
                    return;
                }
                string name = CleanName(message.PlayerName, 24);
                if (name.Length == 0) name = "玩家";
                session.Name = name;
                session.Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                session.Authenticated = true;
                Send(session, new ControlMessage {
                    Type = MessageType.Welcome, RequestId = message.RequestId, PlayerId = session.PlayerId,
                    PlayerName = name, SessionToken = session.Token, UdpPort = m_options.UdpPort
                });
                return;
            }
            switch (message.Type) {
                case MessageType.Ping:
                    Send(session, new ControlMessage { Type = MessageType.Pong, RequestId = message.RequestId });
                    break;
                case MessageType.ListRooms:
                    Send(session, new ControlMessage {
                        Type = MessageType.RoomList, RequestId = message.RequestId,
                        Rooms = m_rooms.Values.Where(room => string.IsNullOrEmpty(message.GameId) || room.GameId == message.GameId)
                            .Take(100).Select(room => room.Summary()).ToArray()
                    });
                    break;
                case MessageType.CreateRoom: CreateRoom(session, message); break;
                case MessageType.JoinRoom: JoinRoom(session, message); break;
                case MessageType.LeaveRoom:
                    LeaveRoom(session);
                    Ack(session, message);
                    break;
                case MessageType.SetReady:
                    if (!RequireRoom(session, message, out Room room)) break;
                    if (room.Started) { Error(session, message, "match_running", "对局已经开始。"); break; }
                    session.Ready = message.Ready;
                    PublishRoom(room);
                    Ack(session, message, room);
                    break;
                case MessageType.StartMatch: StartMatch(session, message); break;
                case MessageType.EndMatch:
                    if (!RequireHost(session, message, out Room endingRoom)) break;
                    if (!endingRoom.Started) { Error(session, message, "no_match", "尚未开局。"); break; }
                    FinishMatch(endingRoom, "host_ended");
                    Ack(session, message, endingRoom);
                    break;
                case MessageType.Reliable: RelayReliable(session, message); break;
                default: Error(session, message, "unknown_message", "不支持的消息类型。"); break;
            }
        }

        void CreateRoom(ClientSession session, ControlMessage request) {
            if (session.Room != null) { Error(session, request, "already_in_room", "请先离开当前房间。"); return; }
            if (m_rooms.Count >= m_options.MaxRooms) { Error(session, request, "server_full", "房间数量已达上限。"); return; }
            if (request.GameId == null || !Regex.IsMatch(request.GameId, "\\A[a-z0-9._-]{1,32}\\z")
                || request.Capacity is < 2 or > ConsoleProtocol.MaxRoomPlayers) {
                Error(session, request, "invalid_room", "游戏标识或人数无效（支持 2–8 人）。"); return;
            }
            string id;
            do { id = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)); } while (m_rooms.ContainsKey(id));
            Room room = new() {
                Id = id, Name = CleanName(request.RoomName, 40), GameId = request.GameId,
                Capacity = request.Capacity, RequiresUdp = request.RequiresUdp, HostId = session.PlayerId
            };
            m_rooms.Add(id, room);
            AddPlayer(room, session);
            Ack(session, request, room);
        }

        void JoinRoom(ClientSession session, ControlMessage request) {
            if (session.Room != null) { Error(session, request, "already_in_room", "请先离开当前房间。"); return; }
            if (request.RoomId == null || request.RoomId.Length > 16 || !m_rooms.TryGetValue(request.RoomId, out Room room)) {
                Error(session, request, "room_not_found", "房间不存在。"); return;
            }
            if (room.Started || room.Players.Count >= room.Capacity) {
                Error(session, request, room.Started ? "match_running" : "room_full", "当前房间不能加入。"); return;
            }
            AddPlayer(room, session);
            Ack(session, request, room);
        }

        void AddPlayer(Room room, ClientSession session) {
            session.Room = room;
            session.Ready = false;
            session.Seat = Enumerable.Range(0, room.Capacity).First(seat => room.Players.All(player => player.Seat != seat));
            room.Players.Add(session);
            PublishRoom(room);
        }

        void StartMatch(ClientSession session, ControlMessage request) {
            if (!RequireHost(session, request, out Room room)) return;
            if (room.Started) { Error(session, request, "match_running", "对局已经开始。"); return; }
            if (room.Players.Count < 2 || room.Players.Any(player => !player.Ready)) {
                Error(session, request, "players_not_ready", "至少两名玩家，且所有玩家（含房主）都需要准备。"); return;
            }
            if (room.RequiresUdp && room.Players.Any(player => player.Peer?.ConnectionState != ConnectionState.Connected)) {
                Error(session, request, "udp_required", "即时游戏需要所有玩家的 UDP 连接就绪。"); return;
            }
            room.MatchId = Guid.NewGuid();
            room.MessageSequence = 0;
            room.ServerRealtimeSequence = 0;
            Guid matchId = room.MatchId;
            if (m_games.TryGetValue(room.GameId, out Func<GameRoomContext, GameRoomHandler> factory)) {
                try {
                    room.Handler = factory(new GameRoomContext(room.Snapshot,
                        (opcode, payload, target) => {
                            if (room.MatchId == matchId && room.Handler != null) PublishReliable(room, Guid.Empty, opcode, payload, target);
                        },
                        (opcode, payload, target) => {
                            if (room.MatchId == matchId && room.Handler != null) PublishRealtime(room, opcode, payload, target);
                        },
                        reason => { if (room.MatchId == matchId && room.Handler != null) FinishMatch(room, reason); }))
                        ?? throw new InvalidOperationException("Game factory returned null.");
                }
                catch (Exception exception) {
                    Console.Error.WriteLine($"Game initialization failed ({room.GameId}): {exception.Message}");
                    room.MatchId = Guid.Empty;
                    Error(session, request, "game_error", "游戏服务初始化失败。");
                    return;
                }
            }
            foreach (ClientSession player in room.Players) player.HasRealtimeSequence = false;
            PublishRoom(room, MessageType.MatchStarted);
            Ack(session, request, room);
            if (room.Handler != null) RunGameAction(room, room.Handler.OnStarted);
        }

        void FinishMatch(Room room, string reason) {
            GameRoomHandler handler = room.Handler;
            room.Handler = null;
            room.MatchId = Guid.Empty;
            try { handler?.OnEnded(reason); }
            catch (Exception exception) { Console.Error.WriteLine($"Game cleanup failed ({room.GameId}): {exception.Message}"); }
            foreach (ClientSession player in room.Players) { player.Ready = false; player.HasRealtimeSequence = false; }
            PublishRoom(room, MessageType.MatchEnded, reason);
        }

        void LeaveRoom(ClientSession session) {
            Room room = session.Room;
            if (room == null) return;
            session.Room = null;
            session.Ready = false;
            room.Players.Remove(session);
            Send(session, new ControlMessage { Type = MessageType.RoomChanged });
            if (room.Players.Count == 0) {
                if (room.Started) FinishMatch(room, "room_empty");
                m_rooms.Remove(room.Id);
                return;
            }
            if (room.HostId == session.PlayerId) room.HostId = room.Players[0].PlayerId;
            if (room.Started) FinishMatch(room, "player_left");
            else PublishRoom(room);
        }

        void RelayReliable(ClientSession session, ControlMessage request) {
            if (!RequireRoom(session, request, out Room room)) return;
            if (!room.Started || request.MatchId != room.MatchId) {
                Error(session, request, "stale_match", "对局不存在或已经改变。"); return;
            }
            if (request.Payload == null || request.Payload.Length > ConsoleProtocol.MaxReliablePayloadBytes) {
                Error(session, request, "invalid_payload", "可靠消息最多 16 KiB。"); return;
            }
            if (request.TargetPlayerId != Guid.Empty && room.Players.All(player => player.PlayerId != request.TargetPlayerId)) {
                Error(session, request, "invalid_target", "接收者不在当前房间。"); return;
            }
            if (room.Handler != null) RunGameAction(room, () => room.Handler.OnReliable(session.PlayerId, request.Opcode, request.Payload));
            else PublishReliable(room, session.PlayerId, request.Opcode, request.Payload, request.TargetPlayerId);
            Ack(session, request);
        }

        void OnUdpConnectionRequest(ConnectionRequest request) {
            try {
                if (request.Data.GetString(64) != ConsoleProtocol.ApplicationKey
                    || !Guid.TryParse(request.Data.GetString(36), out Guid id)
                    || !m_clients.TryGetValue(id, out ClientSession session) || !session.Authenticated) { request.Reject(); return; }
                string token = request.Data.GetString(64);
                if (token != session.Token) { request.Reject(); return; }
                NetPeer peer = request.Accept();
                if (peer != null) peer.Tag = session;
            }
            catch (Exception exception) when (exception is ArgumentException or IndexOutOfRangeException or InvalidOperationException) { request.Reject(); }
        }

        void OnUdpConnected(NetPeer peer) {
            if (peer.Tag is not ClientSession session || !m_clients.ContainsKey(session.PlayerId)) { m_udp.DisconnectPeer(peer); return; }
            NetPeer previous = session.Peer;
            session.Peer = peer;
            if (previous != null && previous != peer) m_udp.DisconnectPeer(previous);
            Send(session, new ControlMessage { Type = MessageType.UdpStatus, UdpConnected = true });
            if (session.Room != null) PublishRoom(session.Room);
        }

        void OnUdpDisconnected(NetPeer peer) {
            if (peer.Tag is not ClientSession session || session.Peer != peer) return;
            session.Peer = null;
            Send(session, new ControlMessage { Type = MessageType.UdpStatus, UdpConnected = false });
            if (session.Room != null) PublishRoom(session.Room);
        }

        void OnRealtime(NetPeer peer, ReadOnlySpan<byte> bytes) {
            if (peer.Tag is not ClientSession session || session.Peer != peer
                || !session.AllowPacket(true, m_options.UdpMessagesPerSecond)
                || !RealtimePacketCodec.TryDecode(bytes, out RealtimePacket packet)) return;
            Room room = session.Room;
            if (room == null || !room.Started || packet.MatchId != room.MatchId) return;
            if (session.HasRealtimeSequence && !RealtimePacketCodec.IsNewer(packet.Sequence, session.RealtimeSequence)) return;
            session.HasRealtimeSequence = true;
            session.RealtimeSequence = packet.Sequence;
            if (room.Handler != null) {
                RunGameAction(room, () => room.Handler.OnRealtime(session.PlayerId, packet.Opcode, packet.Sequence, packet.Payload));
                return;
            }
            packet.SenderId = session.PlayerId;
            byte[] forwarded = RealtimePacketCodec.Encode(packet);
            foreach (ClientSession recipient in room.Players) {
                if (recipient != session && recipient.Peer?.ConnectionState == ConnectionState.Connected)
                    recipient.Peer.Send(forwarded, (byte)session.Seat, DeliveryMethod.Sequenced);
            }
        }

        void PublishReliable(Room room, Guid senderId, ushort opcode, byte[] payload, Guid targetPlayerId) {
            if (!room.Started) return;
            if (payload == null || payload.Length > ConsoleProtocol.MaxReliablePayloadBytes) throw new ArgumentException("Reliable payload limit: 16 KiB.");
            ControlMessage forwarded = new() {
                Type = MessageType.GameMessage, PlayerId = senderId, RoomId = room.Id, MatchId = room.MatchId,
                Opcode = opcode, Payload = payload, TargetPlayerId = targetPlayerId, Sequence = ++room.MessageSequence
            };
            foreach (ClientSession recipient in room.Players.ToArray()) {
                if (targetPlayerId == Guid.Empty || targetPlayerId == recipient.PlayerId) Send(recipient, forwarded);
            }
        }

        void PublishRealtime(Room room, ushort opcode, byte[] payload, Guid targetPlayerId) {
            if (!room.Started) return;
            byte[] bytes = RealtimePacketCodec.Encode(new RealtimePacket {
                MatchId = room.MatchId, SenderId = Guid.Empty, Sequence = ++room.ServerRealtimeSequence, Opcode = opcode, Payload = payload
            });
            foreach (ClientSession recipient in room.Players) {
                if ((targetPlayerId == Guid.Empty || targetPlayerId == recipient.PlayerId) && recipient.Peer?.ConnectionState == ConnectionState.Connected)
                    recipient.Peer.Send(bytes, ConsoleProtocol.ServerRealtimeChannel, DeliveryMethod.Sequenced);
            }
        }

        void RunGameAction(Room room, Action action) {
            try { action(); }
            catch (Exception exception) {
                Console.Error.WriteLine($"Game handler failed ({room.GameId}): {exception.Message}");
                if (room.Started) FinishMatch(room, "game_error");
            }
        }

        bool RequireRoom(ClientSession session, ControlMessage request, out Room room) {
            room = session.Room;
            if (room != null) return true;
            Error(session, request, "not_in_room", "尚未加入房间。");
            return false;
        }

        bool RequireHost(ClientSession session, ControlMessage request, out Room room) {
            if (!RequireRoom(session, request, out room)) return false;
            if (room.HostId == session.PlayerId) return true;
            Error(session, request, "host_only", "此操作需要房主执行。");
            return false;
        }

        void PublishRoom(Room room, string type = MessageType.RoomChanged, string reason = null) {
            room.Revision++;
            ControlMessage message = new() { Type = type, Room = room.Snapshot(), Message = reason };
            foreach (ClientSession player in room.Players.ToArray()) Send(player, message);
        }

        void Ack(ClientSession session, ControlMessage request, Room room = null) => Send(session,
            new ControlMessage { Type = MessageType.Ack, RequestId = request.RequestId, Room = room?.Snapshot() });

        void Error(ClientSession session, ControlMessage request, string code, string message) => Send(session,
            new ControlMessage { Type = MessageType.Error, RequestId = request.RequestId, Code = code, Message = message });

        // State cleanup occurs on the next queued disconnect event, avoiding changes during a room broadcast.
        static void Send(ClientSession session, ControlMessage message) {
            if (session.Connection != null && !session.Connection.TrySend(message)) session.Connection.Dispose();
        }

        void Disconnect(ClientSession session) {
            session.Connection?.Dispose();
            RemoveClient(session);
        }

        void RemoveClient(ClientSession session) {
            if (!m_clients.Remove(session.PlayerId)) return;
            LeaveRoom(session);
            if (session.Peer != null) { NetPeer peer = session.Peer; session.Peer = null; m_udp.DisconnectPeer(peer); }
        }

        static string CleanName(string text, int length) {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return new string(text.Trim().Where(character => !char.IsControl(character)).Take(length).ToArray());
        }
    }
}
