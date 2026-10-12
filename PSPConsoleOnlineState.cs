using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameConsole.Networking;

namespace Game {
    public enum PSPLobbyField { Server, RoomCode }
    public enum PSPLobbyView { Offline, Browse, InRoom }

    public sealed partial class PSPConsoleState {
        readonly ConcurrentQueue<Action> m_onlineActions = new();
        string m_playerName;
        bool m_disposed;
        int m_operationVersion;
        public int ModeSelection { get; private set; }
        public int LobbySelection { get; private set; }
        public const int BrowseActionCount = 6;
        public const int BrowseRefresh = 0, BrowseCreate = 1, BrowsePrivacy = 2, BrowseCodeField = 3, BrowseCodeJoin = 4, BrowseDisconnect = 5;
        public bool CreatePrivate { get; private set; }
        const long LobbyRefreshIntervalMs = 3000;
        public PSPLobbyView LobbyView => OnlineRoom != null ? PSPLobbyView.InRoom : Network.IsConnected ? PSPLobbyView.Browse : PSPLobbyView.Offline;
        public int LobbyActionCount => LobbyView switch {
            PSPLobbyView.InRoom => 4, PSPLobbyView.Browse => LobbyRooms.Count + BrowseActionCount, _ => 2
        };
        // Waiting, joinable rooms for the current game. Only touched on the UI thread.
        public IReadOnlyList<RoomSummary> LobbyRooms { get; private set; } = Array.Empty<RoomSummary>();
        public bool LobbyListLoaded { get; private set; }
        bool m_listing;
        long m_nextListAt;
        public string ServerAddress { get => Progress.ServerAddress; set => Progress.ServerAddress = value ?? ""; }
        public string RoomCode { get; set; } = "";
        public RoomSnapshot OnlineRoom { get; private set; }
        public bool OnlineBusy { get; private set; }
        public string OnlineStatus { get; private set; } = "输入服务器地址，然后连接。";
        public event Action<PSPLobbyField> EditOnlineField;

        public void ChooseMode(int index) { ModeSelection = Math.Clamp(index, 0, 1); Confirm(); }
        public void ChooseLobbyAction(int index) { LobbySelection = Math.Clamp(index, 0, LobbyActionCount - 1); ActivateLobbySelection(); }

        void StartAiPong() {
            Game = new PSPPaddleGame(0, Progress, UnlockTrophy);
            Page = PSPConsolePage.Game;
            ResetAnalog();
        }

        public void OpenOnlineLobby() {
            Page = PSPConsolePage.OnlineLobby;
            LobbySelection = OnlineRoom != null ? 0 : Network.IsConnected ? DefaultBrowseSelection() : 0;
            m_nextListAt = 0;
            ResetAnalog();
        }

        void ReturnToOnlineRoom(bool leaveMatch) {
            Game = null;
            OpenOnlineLobby();
            if (leaveMatch) LeaveOnlineRoom();
        }

        public void Notice(string text) {
            OnlineStatus = text;
            NotificationText = text;
            NotificationIsTrophy = false;
            NotificationTime = 2.5f;
        }

        void ActivateLobbySelection() {
            if (Page != PSPConsolePage.OnlineLobby || OnlineBusy) return;
            if (LobbyView == PSPLobbyView.Offline) {
                if (LobbySelection == 0) EditOnlineField?.Invoke(PSPLobbyField.Server);
                else ConnectOnline();
            }
            else if (LobbyView == PSPLobbyView.Browse) {
                int count = LobbyRooms.Count;
                if (LobbySelection < count) { JoinOnlineRoom(LobbyRooms[LobbySelection].RoomId); return; }
                switch (LobbySelection - count) {
                    case BrowseRefresh: RefreshRoomList(true); break;
                    case BrowsePrivacy: CreatePrivate = !CreatePrivate; break;
                    case BrowseCreate:
                        bool makePrivate = CreatePrivate;
                        BeginOnlineOperation(async () => {
                            await Network.CreateRoomAsync(PongProtocol.GameId, m_playerName + "的乒乓房间", 2, requiresUdp: true, isPrivate: makePrivate).ConfigureAwait(false);
                        }, "正在创建房间…", () => OnlineStatus = makePrivate ? "不公开房间已创建，分享房间码邀请朋友。" : "房间已创建，等待其他玩家从大厅加入。");
                        break;
                    case BrowseCodeField: EditOnlineField?.Invoke(PSPLobbyField.RoomCode); break;
                    case BrowseCodeJoin:
                        string code = RoomCode.Trim().ToUpperInvariant();
                        if (code.Length != 6) { Notice("请输入 6 位房间码。"); break; }
                        JoinOnlineRoom(code);
                        break;
                    case BrowseDisconnect: DisconnectOnline(); break;
                }
            }
            else {
                switch (LobbySelection) {
                    case 0:
                        if (!Network.IsRealtimeConnected) { Notice("UDP 尚未就绪，正在重试连接。"); break; }
                        bool ready = false;
                        foreach (PlayerSummary player in OnlineRoom.Players) if (player.PlayerId == Network.PlayerId) ready = player.Ready;
                        BeginOnlineOperation(async () => { await Network.SetReadyAsync(!ready).ConfigureAwait(false); }, "正在更新准备状态…");
                        break;
                    case 1:
                        if (OnlineRoom.HostPlayerId != Network.PlayerId) { Notice("等待房主开始游戏。"); break; }
                        BeginOnlineOperation(async () => { await Network.StartMatchAsync().ConfigureAwait(false); }, "正在请求开局…");
                        break;
                    case 2: LeaveOnlineRoom(); break;
                    case 3: DisconnectOnline(); break;
                }
            }
        }

        int DefaultBrowseSelection() => LobbyRooms.Count > 0 ? 0 : LobbyRooms.Count + BrowseCreate;

        void JoinOnlineRoom(string roomId) {
            GameConsoleClient joiningClient = Network;
            BeginOnlineOperation(async () => {
                RoomSnapshot joined;
                try { joined = await joiningClient.JoinRoomAsync(roomId).ConfigureAwait(false); }
                catch (NetworkRequestException exception) {
                    throw new NetworkRequestException(exception.Code, exception.Code switch {
                        "room_full" => "加入失败：房间已满。", "room_not_found" => "加入失败：房间已解散。",
                        "match_running" => "加入失败：对局已开始。", _ => "加入失败：" + exception.Message
                    });
                }
                if (joined.GameId != PongProtocol.GameId) {
                    await joiningClient.LeaveRoomAsync().ConfigureAwait(false);
                    throw new NetworkRequestException("wrong_game", "这不是乒乓房间。");
                }
            }, "正在加入房间…", () => OnlineStatus = "加入成功，双方准备后由房主开局。");
        }

        // Fetches the room list without blocking other lobby actions. Results are applied on the UI thread.
        public void RefreshRoomList(bool manual = false) {
            if (m_listing || m_disposed || LobbyView != PSPLobbyView.Browse) return;
            m_listing = true;
            m_nextListAt = Environment.TickCount64 + LobbyRefreshIntervalMs;
            if (manual) OnlineStatus = "正在刷新房间列表…";
            GameConsoleClient client = Network;
            _ = Task.Run(async () => {
                RoomSummary[] rooms = null;
                string error = null;
                try { rooms = await client.ListRoomsAsync(PongProtocol.GameId).ConfigureAwait(false); }
                catch (Exception exception) { error = exception is TimeoutException ? "刷新超时。" : exception.Message; }
                m_onlineActions.Enqueue(() => {
                    m_listing = false;
                    if (m_disposed || !ReferenceEquals(Network, client)) return;
                    if (rooms != null) ApplyRoomList(rooms);
                    if (manual && !OnlineBusy) OnlineStatus = error != null ? "刷新失败：" + error : $"已刷新，共 {LobbyRooms.Count} 个房间等待加入。";
                });
            });
        }

        void ApplyRoomList(RoomSummary[] rooms) {
            int oldCount = LobbyRooms.Count;
            string selectedId = LobbySelection < oldCount ? LobbyRooms[LobbySelection].RoomId : null;
            List<RoomSummary> list = rooms.Where(room => room != null && room.Joinable && room.GameId == PongProtocol.GameId)
                .OrderBy(room => room.HostName ?? room.Name, StringComparer.Ordinal).ThenBy(room => room.RoomId, StringComparer.Ordinal).ToList();
            bool first = !LobbyListLoaded;
            LobbyRooms = list;
            LobbyListLoaded = true;
            if (LobbyView != PSPLobbyView.Browse) return;
            if (first) { LobbySelection = DefaultBrowseSelection(); return; }
            if (selectedId != null) {
                int index = list.FindIndex(room => room.RoomId == selectedId);
                LobbySelection = index >= 0 ? index : list.Count > 0 ? Math.Min(LobbySelection, list.Count - 1) : list.Count + BrowseRefresh;
            }
            else LobbySelection = Math.Clamp(LobbySelection - oldCount + list.Count, 0, LobbyActionCount - 1);
        }

        void UpdateLobbyRefresh() {
            if (Page == PSPConsolePage.OnlineLobby && LobbyView == PSPLobbyView.Browse && !OnlineBusy
                && Environment.TickCount64 >= m_nextListAt) RefreshRoomList();
        }

        void ResetLobbyList() {
            LobbyRooms = Array.Empty<RoomSummary>();
            LobbyListLoaded = false;
            m_nextListAt = 0;
        }

        void ConnectOnline() {
            string text = ServerAddress.Trim();
            if (text.Length == 0) { Notice("请输入服务器 IP 或域名。"); return; }
            string normalized = text.Contains("://", StringComparison.Ordinal) ? text : "tcp://" + text;
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri address) || address.Scheme is not ("tcp" or "tls")
                || string.IsNullOrEmpty(address.Host) || !string.IsNullOrEmpty(address.UserInfo)
                || address.AbsolutePath is not ("" or "/") || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment)
                || address.HostNameType == UriHostNameType.IPv6 || address.Port is 0 or > 65535) {
                Notice("地址格式：IP:28886 或 tls://域名:28886。"); return;
            }
            Network.Dispose();
            Network = new GameConsoleClient();
            AttachNetwork(Network);
            GameConsoleConnectionOptions options = new() {
                Host = address.Host, TcpPort = address.Port < 0 ? ConsoleProtocol.DefaultTcpPort : address.Port,
                PlayerName = m_playerName, UseTls = address.Scheme == "tls", EnableRealtime = true
            };
            BeginOnlineOperation(async () => { await Network.ConnectAsync(options).ConfigureAwait(false); }, "正在连接服务器…",
                () => {
                    OnlineStatus = Network.IsRealtimeConnected ? "已连接，选择房间加入或创建房间。" : "TCP 已连接，正在建立 UDP…";
                    ResetLobbyList();
                    LobbySelection = DefaultBrowseSelection();
                    RefreshRoomList();
                });
        }

        public void LeaveOnlineRoom() {
            if (!Network.IsConnected) return;
            if (OnlineBusy) { m_operationVersion++; OnlineBusy = false; }
            BeginOnlineOperation(async () => { await Network.LeaveRoomAsync().ConfigureAwait(false); }, "正在离开房间…",
                () => OnlineStatus = "已离开，选择房间加入或创建房间。");
        }

        void BeginOnlineOperation(Func<Task> operation, string status, Action completed = null) {
            if (OnlineBusy || m_disposed) return;
            int version = ++m_operationVersion;
            OnlineBusy = true;
            OnlineStatus = status;
            _ = RunOperationAsync(operation, version, completed);
        }

        async Task RunOperationAsync(Func<Task> operation, int version, Action completed) {
            try {
                await operation().ConfigureAwait(false);
                m_onlineActions.Enqueue(() => {
                    if (version != m_operationVersion || m_disposed) return;
                    OnlineBusy = false;
                    completed?.Invoke();
                });
            }
            catch (Exception exception) {
                m_onlineActions.Enqueue(() => {
                    if (version != m_operationVersion || m_disposed) return;
                    OnlineBusy = false;
                    OnlineStatus = exception is TimeoutException ? "请求超时，请确认服务器地址和端口。" : exception.Message;
                    m_nextListAt = 0; // refresh the lobby so stale rooms disappear after a failed join
                });
            }
        }

        void PumpOnlineActions() {
            for (int i = 0; i < 32 && m_onlineActions.TryDequeue(out Action action); i++) action();
        }

        void AttachNetwork(GameConsoleClient network) {
            network.MessageReceived += message => {
                if (!m_disposed && ReferenceEquals(Network, network)) HandleOnlineMessage(message);
            };
            network.RealtimeReceived += packet => {
                if (!m_disposed && ReferenceEquals(Network, network) && packet.SenderId == Guid.Empty
                    && packet.Opcode == PongProtocol.Snapshot && Game is PSPOnlinePongGame game && game.MatchId == packet.MatchId
                    && PongProtocol.TryDecodeSnapshot(packet.Payload, out PongSnapshot state)) game.Receive(state);
            };
            network.Disconnected += reason => {
                if (m_disposed || !ReferenceEquals(Network, network)) return;
                m_operationVersion++;
                OnlineBusy = false;
                OnlineRoom = null;
                ResetLobbyList();
                LobbySelection = 0;
                if (Game?.IsOnline == true) { Game = null; OpenOnlineLobby(); }
                if (Page == PSPConsolePage.OnlineLobby) OnlineStatus = "连接已断开" + (string.IsNullOrEmpty(reason) ? "。" : "：" + reason);
            };
        }

        void HandleOnlineMessage(ControlMessage message) {
            switch (message.Type) {
                case MessageType.RoomChanged:
                    bool changedRoom = OnlineRoom?.RoomId != message.Room?.RoomId;
                    OnlineRoom = message.Room;
                    if (changedRoom) {
                        if (OnlineRoom == null && message.Message == "host_left") Notice("房主已离开，房间已解散。");
                        if (OnlineRoom == null) { m_nextListAt = 0; LobbySelection = DefaultBrowseSelection(); }
                        else LobbySelection = 0;
                    }
                    break;
                case MessageType.UdpStatus:
                    if (!OnlineBusy && Page == PSPConsolePage.OnlineLobby) OnlineStatus = message.UdpConnected
                        ? "UDP 已连接，双方准备后可以开局。" : "UDP 暂时断开，正在重新连接…";
                    break;
                case MessageType.MatchStarted:
                    OnlineRoom = message.Room;
                    if (OnlineRoom?.GameId != PongProtocol.GameId) break;
                    Game = new PSPOnlinePongGame(Network, OnlineRoom, () => ReturnToOnlineRoom(false), Notice);
                    Page = PSPConsolePage.Game;
                    ResetAnalog();
                    break;
                case MessageType.GameMessage:
                    if (message.PlayerId == Guid.Empty && Game is PSPOnlinePongGame game && game.MatchId == message.MatchId
                        && message.Opcode is PongProtocol.InitialState or PongProtocol.PhaseChanged or PongProtocol.Result
                        && PongProtocol.TryDecodeSnapshot(message.Payload, out PongSnapshot state)) game.Receive(state);
                    break;
                case MessageType.MatchEnded:
                    OnlineRoom = message.Room;
                    if (message.Message == "pong_finished" && Game is PSPOnlinePongGame finished && finished.Phase == PSPGamePhase.Finished) break;
                    if (Game?.IsOnline == true) { Game = null; OpenOnlineLobby(); }
                    OnlineStatus = message.Message == "player_left" ? "对方已离开，本局结束。" : "本局结束，可以重新准备。";
                    break;
            }
        }

        void DisconnectOnline() {
            m_operationVersion++;
            OnlineBusy = false;
            Network.Dispose();
            Network = new GameConsoleClient();
            AttachNetwork(Network);
            OnlineRoom = null;
            ResetLobbyList();
            LobbySelection = 0;
            OnlineStatus = "已断开，可以重新连接服务器。";
        }

        public void Dispose() {
            m_disposed = true;
            m_operationVersion++;
            Network.Dispose();
        }
    }
}
