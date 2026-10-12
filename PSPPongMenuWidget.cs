using System;
using System.Collections.Generic;
using System.Linq;
using Engine;
using Engine.Graphics;
using Engine.Input;
using GameConsole.Networking;

namespace Game {
    public sealed class PSPPongMenuWidget : CanvasWidget {
        readonly PSPConsoleState m_state;
        readonly List<Widget> m_modeWidgets = new(), m_lobbyWidgets = new();
        readonly LabelWidget m_modeTitle, m_modeDescription, m_title, m_serverCaption, m_status, m_roomTitle, m_roomHelp, m_codeCaption, m_footer;
        readonly LabelWidget[] m_players = new LabelWidget[2];
        readonly PSPConsoleActionWidget[] m_modes = new PSPConsoleActionWidget[2], m_actions = new PSPConsoleActionWidget[4];
        readonly TextBoxWidget m_server = new() { MaximumLength = 200, AutoSize = false, Title = "服务器地址", Description = "IP:28886 或 tls://域名:28886" };
        readonly TextBoxWidget m_code = new() { MaximumLength = 6, AutoSize = false, Title = "房间码", Description = "输入朋友分享的 6 位房间码" };
        readonly PSPConsoleActionWidget m_back = new("O 返回");
        const int VisibleRows = 4;
        readonly List<Widget> m_browseWidgets = new();
        readonly PSPConsoleActionWidget[] m_rows = new PSPConsoleActionWidget[VisibleRows];
        // Order matches PSPConsoleState.Browse* indices; BrowseCodeField has no button (it is the text box).
        readonly PSPConsoleActionWidget[] m_browse = new PSPConsoleActionWidget[PSPConsoleState.BrowseActionCount];
        readonly LabelWidget m_listTitle, m_empty;
        int m_rowOffset;
        int m_editGuardFrames;
        public bool IsTextInputActive => m_server.HasFocus || m_code.HasFocus || Keyboard.IsKeyboardVisible || m_editGuardFrames > 0;

        public PSPPongMenuWidget(PSPConsoleState state) {
            m_state = state;
            IsHitTestVisible = false;
            ClampToBounds = true;
            m_modeTitle = Label("乒乓对决", m_modeWidgets);
            m_modeDescription = Label("选择对战模式", m_modeWidgets);
            m_modes[0] = new PSPConsoleActionWidget("单人  /  AI 对战") { Primary = true };
            m_modes[1] = new PSPConsoleActionWidget("双人  /  联机对战");
            foreach (PSPConsoleActionWidget button in m_modes) Add(button, m_modeWidgets);
            m_title = Label("乒乓对决  /  联机大厅", m_lobbyWidgets);
            m_serverCaption = Label("服务器地址", m_lobbyWidgets);
            m_status = Label("", m_lobbyWidgets);
            m_roomTitle = Label("", m_lobbyWidgets);
            m_roomHelp = Label("", m_lobbyWidgets);
            m_codeCaption = Label("房间码", m_lobbyWidgets);
            for (int i = 0; i < m_players.Length; i++) m_players[i] = Label("", m_lobbyWidgets);
            Add(m_server, m_lobbyWidgets);
            Add(m_code, m_lobbyWidgets);
            for (int i = 0; i < m_actions.Length; i++) { m_actions[i] = new PSPConsoleActionWidget(""); Add(m_actions[i], m_lobbyWidgets); }
            m_listTitle = Label("等待中的房间", m_browseWidgets);
            m_empty = Label("", m_browseWidgets);
            m_empty.TextAnchor = TextAnchor.Center;
            for (int i = 0; i < VisibleRows; i++) { m_rows[i] = new PSPConsoleActionWidget(""); Add(m_rows[i], m_browseWidgets); }
            string[] browseTexts = ["刷新", "创建房间", "", "", "按码加入", "断开连接"];
            for (int i = 0; i < m_browse.Length; i++) {
                if (i == PSPConsoleState.BrowseCodeField) continue;
                m_browse[i] = new PSPConsoleActionWidget(browseTexts[i]) { Primary = i == PSPConsoleState.BrowseCreate };
                Add(m_browse[i], m_browseWidgets);
            }
            m_footer = Label("方向键选择 · X 确认 · O 返回", null);
            Children.Add(m_back);
            m_server.TextChanged += widget => m_state.ServerAddress = widget.Text;
            m_code.TextChanged += widget => m_state.RoomCode = widget.Text;
            m_server.Enter += _ => EndTextInput();
            m_code.Enter += _ => EndTextInput();
            m_state.EditOnlineField += BeginTextInput;
            Refresh();
        }

        void Add(Widget widget, List<Widget> group) { group?.Add(widget); Children.Add(widget); }
        LabelWidget Label(string text, List<Widget> group) {
            LabelWidget label = PSPConsoleDialog.MakeLabel(text, Color.White, 0.5f);
            label.TextAnchor = TextAnchor.Left | TextAnchor.VerticalCenter;
            Add(label, group);
            return label;
        }

        public void EndTextInput() {
            if (m_server.HasFocus || m_code.HasFocus) m_editGuardFrames = 3;
            m_server.HasFocus = m_code.HasFocus = false;
            TextBoxWidget.CloseInputMethod();
        }

        void BeginTextInput(PSPLobbyField field) {
            TextBoxWidget widget = field == PSPLobbyField.Server ? m_server : m_code;
            if (!widget.IsEnabled || !widget.IsVisible) return;
            EndTextInput();
            widget.HasFocus = true;
            widget.Caret = widget.Text.Length;
            if (OperatingSystem.IsAndroid() || OperatingSystem.IsBrowser()) {
                Keyboard.ShowKeyboard(widget.Title, widget.Description, widget.Text, false, text => {
                    widget.Text = text;
                    EndTextInput();
                }, EndTextInput);
            }
            else TextBoxWidget.ShowInputMethod();
        }

        public void Refresh() {
            bool mode = m_state.Page == PSPConsolePage.PongMode, lobby = m_state.Page == PSPConsolePage.OnlineLobby;
            IsVisible = mode || lobby;
            foreach (Widget widget in m_modeWidgets) widget.IsVisible = mode;
            foreach (Widget widget in m_lobbyWidgets) widget.IsVisible = lobby;
            if (!lobby) EndTextInput();
            for (int i = 0; i < m_modes.Length; i++) m_modes[i].Focused = i == m_state.ModeSelection;
            if (!m_server.HasFocus && m_server.Text != m_state.ServerAddress) m_server.Text = m_state.ServerAddress;
            if (!m_code.HasFocus && m_code.Text != m_state.RoomCode) m_code.Text = m_state.RoomCode;
            m_server.IsEnabled = !m_state.Network.IsConnected && !m_state.OnlineBusy;
            if (m_server.HasFocus && !m_server.IsEnabled) EndTextInput();
            string status = m_state.OnlineStatus;
            m_status.Text = status.Length > 62 ? status[..62] + "…" : status;
            m_status.Color = m_state.OnlineBusy ? new Color(244, 208, 129) : new Color(156, 201, 225);
            RoomSnapshot room = m_state.OnlineRoom;
            PSPLobbyView view = m_state.LobbyView;
            bool offline = lobby && view == PSPLobbyView.Offline, browse = lobby && view == PSPLobbyView.Browse, inRoom = lobby && view == PSPLobbyView.InRoom;
            m_server.IsVisible = m_serverCaption.IsVisible = offline;
            m_roomTitle.IsVisible = m_roomHelp.IsVisible = offline || inRoom;
            m_code.IsVisible = m_codeCaption.IsVisible = browse;
            foreach (Widget widget in m_browseWidgets) widget.IsVisible = browse;
            m_roomTitle.Text = room == null ? "连接服务器后进入联机大厅，直接选房间加入。" : $"房间码  {room.RoomId}  ·  {room.Players.Length}/2 人";
            m_roomHelp.Text = room == null ? "服务器默认 TCP 28886 / UDP 28887" : "双方准备、UDP 就绪后，由房主开始游戏。";
            for (int i = 0; i < 2; i++) {
                m_players[i].IsVisible = inRoom;
                PlayerSummary player = room?.Players.OrderBy(member => member.Seat).ElementAtOrDefault(i);
                m_players[i].Text = player == null ? "等待另一位玩家加入…" :
                    $"{player.Name}  {(player.PlayerId == room.HostPlayerId ? "[房主]" : "")}{(player.PlayerId == m_state.Network.PlayerId ? "[你]" : "")}\n"
                    + $"{(player.Ready ? "已准备" : "未准备")}  ·  UDP {(player.UdpConnected ? "就绪" : "连接中")}";
                m_players[i].Color = player?.Ready == true ? new Color(146, 226, 199) : new Color(209, 224, 239);
            }
            if (browse) RefreshBrowse();
            if (!inRoom) {
                m_actions[0].Text = "连接服务器";
                m_actions[0].IsVisible = offline;
                for (int i = 1; i < m_actions.Length; i++) m_actions[i].IsVisible = false;
                m_actions[0].Focused = m_state.LobbySelection == 1;
            }
            else {
                bool ready = room.Players.Any(player => player.PlayerId == m_state.Network.PlayerId && player.Ready);
                m_actions[0].Text = ready ? "取消准备" : "准备";
                m_actions[1].Text = room.HostPlayerId == m_state.Network.PlayerId ? "开始游戏" : "等待房主开局";
                m_actions[2].Text = "离开房间";
                m_actions[3].Text = "断开连接";
                for (int i = 0; i < m_actions.Length; i++) { m_actions[i].Focused = i == m_state.LobbySelection; m_actions[i].IsVisible = true; }
            }
            foreach (PSPConsoleActionWidget button in m_actions) button.IsEnabled = !m_state.OnlineBusy;
            m_footer.Text = m_state.OnlineBusy ? "正在处理请求… · O 返回" : "方向键选择 · X 确认 · O 返回";
        }

        void RefreshBrowse() {
            IReadOnlyList<RoomSummary> rooms = m_state.LobbyRooms;
            int selection = m_state.LobbySelection;
            if (selection < rooms.Count) {
                if (selection < m_rowOffset) m_rowOffset = selection;
                if (selection >= m_rowOffset + VisibleRows) m_rowOffset = selection - VisibleRows + 1;
            }
            m_rowOffset = Math.Clamp(m_rowOffset, 0, Math.Max(0, rooms.Count - VisibleRows));
            m_listTitle.Text = rooms.Count > VisibleRows ? $"等待中的房间  ({m_rowOffset + 1}-{m_rowOffset + VisibleRows} / {rooms.Count})" : "等待中的房间";
            for (int i = 0; i < VisibleRows; i++) {
                RoomSummary summary = m_rowOffset + i < rooms.Count ? rooms[m_rowOffset + i] : null;
                m_rows[i].IsVisible = summary != null;
                if (summary == null) continue;
                string host = string.IsNullOrWhiteSpace(summary.HostName) ? summary.Name : summary.HostName;
                string game = summary.GameId == PongProtocol.GameId ? "乒乓对决" : summary.GameId;
                m_rows[i].Text = $"{host}    ·    {game}    ·    {summary.PlayerCount}/{summary.Capacity} 人";
                m_rows[i].Focused = selection == m_rowOffset + i;
                m_rows[i].IsEnabled = !m_state.OnlineBusy;
            }
            m_empty.IsVisible = rooms.Count == 0;
            m_empty.Text = m_state.LobbyListLoaded ? "暂无房间，点「创建房间」开一局" : "正在获取房间列表…";
            m_browse[PSPConsoleState.BrowsePrivacy].Text = m_state.CreatePrivate ? "房间：不公开" : "房间：公开";
            for (int i = 0; i < m_browse.Length; i++) {
                if (m_browse[i] == null) continue;
                m_browse[i].Focused = selection == rooms.Count + i;
                m_browse[i].IsEnabled = !m_state.OnlineBusy;
            }
        }

        public override void MeasureOverride(Vector2 size) {
            foreach (Widget child in Children) child.Measure(size);
            DesiredSize = size;
            IsDrawRequired = true;
        }

        void Place(Widget widget, float x, float y, float width, float height, float fontScale = 0.45f) {
            Vector2 size = new(width / 960f * ActualSize.X, height / 540f * ActualSize.Y);
            float scale = fontScale * MathUtils.Max(0.18f, ActualSize.X / 640f);
            if (widget is LabelWidget label) label.FontScale = scale;
            if (widget is PSPConsoleActionWidget button) button.FontScale = scale;
            if (widget is TextBoxWidget text) { text.FontScale = scale; text.Size = size; }
            PSPConsoleDialog.Place(widget, new Vector2(x / 960f * ActualSize.X, y / 540f * ActualSize.Y), size);
        }

        public override void ArrangeOverride() {
            Place(m_modeTitle, 70f, 73f, 650f, 62f, 1f);
            Place(m_modeDescription, 73f, 157f, 680f, 42f, 0.55f);
            Place(m_modes[0], 145f, 257f, 300f, 73f, 0.59f);
            Place(m_modes[1], 515f, 257f, 300f, 73f, 0.59f);
            Place(m_title, 42f, 22f, 800f, 51f, 0.72f);
            Place(m_serverCaption, 45f, 83f, 250f, 27f, 0.38f);
            Place(m_server, 56f, 115f, 630f, 41f, 0.5f);
            Place(m_status, 45f, 172f, 870f, 36f, 0.4f);
            Place(m_roomTitle, 45f, 224f, 870f, 40f, 0.56f);
            Place(m_roomHelp, 45f, 270f, 870f, 28f, 0.37f);
            Place(m_codeCaption, 45f, 327f, 195f, 28f, 0.4f);
            Place(m_code, 57f, 368f, 370f, 38f, 0.56f);
            for (int i = 0; i < m_players.Length; i++) Place(m_players[i], 58f + i * 440f, 333f, 405f, 70f, 0.44f);
            PSPLobbyView view = m_state.LobbyView;
            if (view == PSPLobbyView.Browse) {
                Place(m_status, 45f, 72f, 870f, 30f, 0.4f);
                Place(m_listTitle, 45f, 104f, 600f, 26f, 0.36f);
                for (int i = 0; i < VisibleRows; i++) Place(m_rows[i], 45f, 134f + i * 54f, 870f, 48f, 0.44f);
                Place(m_empty, 45f, 190f, 870f, 60f, 0.55f);
                Place(m_codeCaption, 45f, 365f, 110f, 40f, 0.4f);
                Place(m_code, 160f, 366f, 200f, 38f, 0.5f);
                Place(m_browse[PSPConsoleState.BrowseCodeJoin], 380f, 361f, 170f, 48f, 0.42f);
                Place(m_browse[PSPConsoleState.BrowsePrivacy], 570f, 361f, 190f, 48f, 0.42f);
                Place(m_browse[PSPConsoleState.BrowseRefresh], 45f, 427f, 200f, 48f, 0.44f);
                Place(m_browse[PSPConsoleState.BrowseCreate], 265f, 427f, 240f, 48f, 0.44f);
                Place(m_browse[PSPConsoleState.BrowseDisconnect], 525f, 427f, 200f, 48f, 0.44f);
            }
            else if (view == PSPLobbyView.Offline) {
                Place(m_actions[0], 713f, 111f, 200f, 50f, 0.45f);
            }
            else for (int i = 0; i < m_actions.Length; i++) Place(m_actions[i], 43f + i * 222f, 438f, 206f, 46f, 0.41f);
            Place(m_footer, 42f, 497f, 680f, 29f, 0.34f);
            Place(m_back, 772f, 498f, 143f, 31f, 0.34f);
        }

        public override void Draw(DrawContext dc) {
            PSPConsolePainter p = new(this, dc.PrimitivesRenderer2D.FlatBatch(0, DepthStencilState.None));
            p.Rect(0f, 0f, 960f, 540f, new Color(5, 12, 24, 230));
            if (m_state.Page != PSPConsolePage.OnlineLobby) return;
            PSPLobbyView view = m_state.LobbyView;
            if (view == PSPLobbyView.Offline) {
                p.RoundedRect(43f, 110f, 652f, 51f, 8f, m_state.LobbySelection == 0 ? new Color(143, 196, 225) : new Color(47, 73, 103));
                p.RoundedRect(46f, 113f, 646f, 45f, 6f, new Color(14, 29, 47));
            }
            else if (view == PSPLobbyView.Browse) {
                p.RoundedRect(40f, 130f, 880f, 220f, 10f, new Color(14, 26, 42));
                bool codeFocused = m_state.LobbySelection == m_state.LobbyRooms.Count + PSPConsoleState.BrowseCodeField;
                p.RoundedRect(150f, 360f, 220f, 50f, 7f, codeFocused ? new Color(143, 196, 225) : new Color(47, 73, 103));
                p.RoundedRect(153f, 363f, 214f, 44f, 5f, new Color(14, 29, 47));
            }
            else for (int i = 0; i < 2; i++) p.RoundedRect(43f + i * 440f, 321f, 425f, 93f, 10f, new Color(24, 43, 64));
        }

        public override void Update() {
            if (m_editGuardFrames > 0) m_editGuardFrames--;
            if (m_state.Page == PSPConsolePage.PongMode) {
                for (int i = 0; i < m_modes.Length; i++) if (m_modes[i].IsClicked) m_state.ChooseMode(i);
            }
            else if (m_state.Page == PSPConsolePage.OnlineLobby) {
                PSPLobbyView view = m_state.LobbyView;
                for (int i = 0; i < m_actions.Length; i++) if (m_actions[i].IsVisible && m_actions[i].IsClicked) {
                    EndTextInput();
                    m_state.ChooseLobbyAction(view == PSPLobbyView.Offline ? 1 : i);
                }
                if (view == PSPLobbyView.Browse) {
                    int count = m_state.LobbyRooms.Count;
                    for (int i = 0; i < VisibleRows; i++) if (m_rows[i].IsVisible && m_rows[i].IsClicked && m_rowOffset + i < count) {
                        EndTextInput();
                        m_state.ChooseLobbyAction(m_rowOffset + i);
                    }
                    for (int i = 0; i < m_browse.Length; i++) if (m_browse[i] != null && m_browse[i].IsVisible && m_browse[i].IsClicked) {
                        EndTextInput();
                        m_state.ChooseLobbyAction(count + i);
                    }
                }
            }
            if (m_back.IsClicked) { EndTextInput(); m_state.Back(); }
        }
    }
}
