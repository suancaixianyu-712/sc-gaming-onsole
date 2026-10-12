using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using GameConsole.Networking;

namespace Game {
    public enum PSPLobbyField { Server, RoomCode }

    public sealed partial class PSPConsoleState {
        readonly ConcurrentQueue<Action> m_onlineActions = new();
        string m_playerName;
        bool m_disposed;
        int m_operationVersion;
        public int ModeSelection { get; private set; }
        public int LobbySelection { get; private set; }
        public int LobbyActionCount => OnlineRoom == null ? 5 : 4;
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
            LobbySelection = OnlineRoom != null ? 0 : Network.IsConnected ? 3 : 0;
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
            if (OnlineRoom == null) {
                switch (LobbySelection) {
                    case 0: EditOnlineField?.Invoke(PSPLobbyField.Server); break;
                    case 1: if (Network.IsConnected) DisconnectOnline(); else ConnectOnline(); break;
                    case 2: EditOnlineField?.Invoke(PSPLobbyField.RoomCode); break;
                    case 3:
                        if (!Network.IsConnected) { Notice("请先连接服务器。"); break; }
                        BeginOnlineOperation(async () => {
                            await Network.CreateRoomAsync(PongProtocol.GameId, m_playerName + "的乒乓房间", 2, requiresUdp: true).ConfigureAwait(false);
                        }, "正在创建房间…", () => OnlineStatus = "房间已创建，分享房间码邀请朋友。");
                        break;
                    case 4:
                        if (!Network.IsConnected) { Notice("请先连接服务器。"); break; }
                        string code = RoomCode.Trim().ToUpperInvariant();
                        if (code.Length != 6) { Notice("请输入 6 位房间码。"); break; }
                        GameConsoleClient joiningClient = Network;
                        BeginOnlineOperation(async () => {
                            RoomSnapshot joined = await joiningClient.JoinRoomAsync(code).ConfigureAwait(false);
                            if (joined.GameId != PongProtocol.GameId) {
                                await joiningClient.LeaveRoomAsync().ConfigureAwait(false);
                                throw new NetworkRequestException("wrong_game", "这不是乒乓房间。");
                            }
                        }, "正在加入房间…", () => OnlineStatus = "加入成功，双方准备后由房主开局。");
                        break;
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

        void ConnectOnline() {
            string text = ServerAddress.Trim();
            if (text.Length == 0) { Notice("请输入服务器 IP 或域名。"); return; }
            string normalized = text.Contains("://", StringComparison.Ordinal) ? text : "tcp://" + text;
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri address) || address.Scheme is not ("tcp" or "tls")
                || string.IsNullOrEmpty(address.Host) || !string.IsNullOrEmpty(address.UserInfo)
                || address.AbsolutePath is not ("" or "/") || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment)
                || address.HostNameType == UriHostNameType.IPv6 || address.Port is 0 or > 65535) {
                Notice("地址格式：IP:26051 或 tls://域名:26051。"); return;
            }
            Network.Dispose();
            Network = new GameConsoleClient();
            AttachNetwork(Network);
            GameConsoleConnectionOptions options = new() {
                Host = address.Host, TcpPort = address.Port < 0 ? ConsoleProtocol.DefaultTcpPort : address.Port,
                PlayerName = m_playerName, UseTls = address.Scheme == "tls", EnableRealtime = true
            };
            BeginOnlineOperation(async () => { await Network.ConnectAsync(options).ConfigureAwait(false); }, "正在连接服务器…",
                () => { OnlineStatus = Network.IsRealtimeConnected ? "已连接，可以创建或加入房间。" : "TCP 已连接，正在建立 UDP…"; LobbySelection = 3; });
        }

        public void LeaveOnlineRoom() {
            if (!Network.IsConnected) return;
            if (OnlineBusy) { m_operationVersion++; OnlineBusy = false; }
            BeginOnlineOperation(async () => { await Network.LeaveRoomAsync().ConfigureAwait(false); }, "正在离开房间…",
                () => OnlineStatus = "已离开，可以创建或加入房间。");
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
                if (Game?.IsOnline == true) { Game = null; OpenOnlineLobby(); }
                if (Page == PSPConsolePage.OnlineLobby) OnlineStatus = "连接已断开" + (string.IsNullOrEmpty(reason) ? "。" : "：" + reason);
            };
        }

        void HandleOnlineMessage(ControlMessage message) {
            switch (message.Type) {
                case MessageType.RoomChanged:
                    bool changedRoom = OnlineRoom?.RoomId != message.Room?.RoomId;
                    OnlineRoom = message.Room;
                    if (changedRoom) LobbySelection = OnlineRoom == null ? 3 : 0;
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
