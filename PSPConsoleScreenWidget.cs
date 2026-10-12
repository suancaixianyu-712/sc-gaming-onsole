using System;
using System.Collections.Generic;
using Engine;
using Engine.Graphics;

namespace Game {
    public sealed class PSPConsoleScreenWidget : CanvasWidget {
        readonly PSPConsoleState m_state;
        readonly PSPConsoleSceneWidget m_scene;
        readonly PSPTetrisBoardWidget m_board;
        readonly PSPPongMenuWidget m_pongMenu;
        public bool IsTextInputActive => m_pongMenu.IsTextInputActive;
        public void EndTextInput() => m_pongMenu.EndTextInput();
        readonly PSPGameTileWidget[] m_tiles = new PSPGameTileWidget[PSPConsoleState.GameCount];
        readonly PSPGameTileWidget m_collectionIcon;
        readonly List<Widget> m_homeWidgets = new(), m_collectionWidgets = new(), m_trophyWidgets = new(), m_gameWidgets = new();
        readonly LabelWidget m_header, m_clock, m_tag, m_title, m_description, m_record;
        readonly LabelWidget m_collectionTag, m_collectionTitle, m_collectionDescription, m_collectionRecord;
        readonly PSPConsoleActionWidget m_play, m_trophies, m_collectionPlay, m_collectionBack, m_trophyBack;
        readonly LabelWidget m_trophyTitle, m_trophyCount;
        readonly LabelWidget[] m_trophyNames = new LabelWidget[3], m_trophyDescriptions = new LabelWidget[3], m_trophyStatuses = new LabelWidget[3];
        readonly LabelWidget m_gameTitle, m_score, m_status, m_gameHelp, m_message, m_messageHint, m_notification;
        readonly PSPConsoleActionWidget m_gameBack, m_gameAction;

        public PSPConsoleScreenWidget(PSPConsoleState state) {
            m_state = state;
            ClampToBounds = true;
            IsHitTestVisible = false;
            m_scene = new PSPConsoleSceneWidget(state);
            Children.Add(m_scene);
            m_board = new PSPTetrisBoardWidget(state);
            Children.Add(m_board);
            m_header = AddLabel("游戏", m_homeWidgets);
            m_clock = AddLabel("", m_homeWidgets, TextAnchor.Right | TextAnchor.VerticalCenter);
            m_tag = AddLabel("", m_homeWidgets);
            m_title = AddLabel("", m_homeWidgets);
            m_description = AddLabel("", m_homeWidgets);
            m_record = AddLabel("", m_homeWidgets);
            m_play = new PSPConsoleActionWidget("进入合集") { Primary = true };
            m_trophies = new PSPConsoleActionWidget($"奖杯 0/{PSPConsoleState.GameCount * 3}");
            Add(m_play, m_homeWidgets);
            Add(m_trophies, m_homeWidgets);
            m_collectionTag = AddLabel("", m_collectionWidgets);
            m_collectionTitle = AddLabel("", m_collectionWidgets);
            m_collectionDescription = AddLabel("", m_collectionWidgets);
            m_collectionRecord = AddLabel("", m_collectionWidgets);
            for (int i = 0; i < m_tiles.Length; i++) {
                m_tiles[i] = new PSPGameTileWidget(i);
                Add(m_tiles[i], m_collectionWidgets);
            }
            m_collectionPlay = new PSPConsoleActionWidget("开始游戏") { Primary = true };
            m_collectionBack = new PSPConsoleActionWidget("O 返回");
            Add(m_collectionPlay, m_collectionWidgets);
            Add(m_collectionBack, m_collectionWidgets);
            m_collectionIcon = new PSPGameTileWidget(-1);
            Add(m_collectionIcon, m_homeWidgets);
            m_trophyTitle = AddLabel("", m_trophyWidgets);
            m_trophyCount = AddLabel("", m_trophyWidgets, TextAnchor.Right | TextAnchor.VerticalCenter);
            for (int i = 0; i < 3; i++) {
                m_trophyNames[i] = AddLabel("", m_trophyWidgets);
                m_trophyDescriptions[i] = AddLabel("", m_trophyWidgets);
                m_trophyStatuses[i] = AddLabel("", m_trophyWidgets, TextAnchor.Right | TextAnchor.VerticalCenter);
            }
            m_trophyBack = new PSPConsoleActionWidget("O 返回") { Focused = true };
            Add(m_trophyBack, m_trophyWidgets);
            m_gameTitle = AddLabel("", m_gameWidgets);
            m_score = AddLabel("", m_gameWidgets, TextAnchor.Center);
            m_status = AddLabel("", m_gameWidgets, TextAnchor.Right | TextAnchor.VerticalCenter);
            m_gameHelp = AddLabel("左右移动接球 · X 发球 · START 暂停", m_gameWidgets, TextAnchor.Center);
            m_message = AddLabel("", m_gameWidgets, TextAnchor.Center);
            m_messageHint = AddLabel("", m_gameWidgets, TextAnchor.Center);
            m_gameBack = new PSPConsoleActionWidget("O 返回");
            m_gameAction = new PSPConsoleActionWidget("X 发球") { Primary = true };
            Add(m_gameBack, m_gameWidgets);
            Add(m_gameAction, m_gameWidgets);
            m_pongMenu = new PSPPongMenuWidget(m_state);
            Children.Add(m_pongMenu);
            m_notification = AddLabel("", null, TextAnchor.Center);
            Refresh();
        }

        void Add(Widget widget, List<Widget> page) { page?.Add(widget); Children.Add(widget); }
        LabelWidget AddLabel(string text, List<Widget> page, TextAnchor anchor = TextAnchor.Left | TextAnchor.VerticalCenter) {
            LabelWidget label = PSPConsoleDialog.MakeLabel(text, Color.White, 0.6f);
            label.TextAnchor = anchor;
            Add(label, page);
            return label;
        }

        public void Refresh() {
            m_pongMenu.Refresh();
            bool home = m_state.Page == PSPConsolePage.Home;
            bool collection = m_state.Page == PSPConsolePage.Collection;
            bool trophies = m_state.Page == PSPConsolePage.Trophies;
            bool gaming = m_state.Page == PSPConsolePage.Game;
            foreach (Widget widget in m_homeWidgets) widget.IsVisible = home;
            foreach (Widget widget in m_collectionWidgets) widget.IsVisible = collection;
            foreach (Widget widget in m_trophyWidgets) widget.IsVisible = trophies;
            foreach (Widget widget in m_gameWidgets) widget.IsVisible = gaming;
            int selected = m_state.SelectedGame;
            m_clock.Text = DateTime.Now.ToString("HH:mm");
            int trophyTotal = m_state.Progress.TotalTrophyCount();
            int trophyMax = PSPConsoleState.GameCount * 3;
            m_title.Text = PSPConsoleState.CollectionName;
            m_description.Text = PSPConsoleState.CollectionDescription;
            m_tag.Text = PSPConsoleState.CollectionTag;
            m_record.Text = $"奖杯 {trophyTotal}/{trophyMax} · 乒乓胜 {m_state.Progress.PongWins} · 砖块最高 {m_state.Progress.BreakoutBestScore} · 方块最高 {m_state.Progress.TetrisBestScore}";
            m_trophies.Text = $"奖杯 {trophyTotal}/{trophyMax}";
            m_play.Focused = m_state.HomeFocus == PSPHomeFocus.Play;
            m_trophies.Focused = m_state.HomeFocus == PSPHomeFocus.Trophies;
            m_collectionIcon.Selected = true;
            m_collectionIcon.Focused = m_state.HomeFocus == PSPHomeFocus.Play;
            m_collectionTag.Text = selected == 0 ? "单人 AI  /  双人联机" : selected == 1 ? "单人游玩  /  街机挑战" : "单人游玩  /  原版方块消行";
            m_collectionTitle.Text = PSPConsoleState.GameNames[selected];
            m_collectionDescription.Text = PSPConsoleState.GameDescriptions[selected];
            m_collectionRecord.Text = selected == 0
                ? $"获胜 {m_state.Progress.PongWins} 次 · 最长回合 {m_state.Progress.PongBestRally} 次接球"
                : selected == 1 ? $"最高得分  {m_state.Progress.BreakoutBestScore}" : $"最高得分  {m_state.Progress.TetrisBestScore}";
            m_collectionPlay.Focused = true;
            foreach (PSPGameTileWidget tile in m_tiles) {
                tile.Selected = tile.GameIndex == selected;
                tile.Focused = false;
            }
            m_trophyTitle.Text = PSPConsoleState.GameNames[selected] + "  /  奖杯";
            m_trophyCount.Text = $"已获得 {m_state.Progress.TrophyCount(selected)} / 3";
            for (int i = 0; i < 3; i++) {
                int index = selected * 3 + i;
                bool earned = m_state.Progress.HasTrophy(index);
                m_trophyNames[i].Text = PSPConsoleProgress.TrophyNames[index];
                m_trophyDescriptions[i].Text = PSPConsoleProgress.TrophyDescriptions[index];
                m_trophyDescriptions[i].Color = new Color(160, 178, 200);
                m_trophyStatuses[i].Text = earned ? "已获得" : "未获得";
                m_trophyStatuses[i].Color = earned ? new Color(246, 216, 149) : new Color(120, 138, 162);
            }
            if (gaming) {
                PSPPaddleGame game = m_state.Game;
                m_gameTitle.Text = PSPConsoleState.GameNames[game.GameIndex];
                m_score.Text = game.GameIndex == 0 ? $"{game.PlayerScore} : {game.OpponentScore}" : $"得分 {game.Score}";
                m_status.Text = game.GameIndex == 0 ? "玩家 / AI · 先到 5 分" : $"生命 {game.Lives} · 砖块 {game.RemainingBricks}";
                bool overlay = game.IsPaused || game.Phase == PSPGamePhase.Ready || game.Phase == PSPGamePhase.Finished;
                m_message.IsVisible = m_messageHint.IsVisible = overlay;
                m_message.Text = game.Phase == PSPGamePhase.Finished ? game.Won ? "胜利！" : "比赛结束" : game.IsPaused ? "已暂停" : "准备发球";
                m_messageHint.Text = game.Phase == PSPGamePhase.Finished ? "X 再来一局 · O 返回主页"
                    : game.IsPaused ? "X 或 START 继续 · O 返回主页" : "X 发球 · 摇杆 / 方向键左右移动";
                m_gameAction.Text = game.Phase == PSPGamePhase.Finished ? "再来一局" : game.IsPaused ? "继续" : game.Phase == PSPGamePhase.Ready ? "X 发球" : "暂停";
                m_gameAction.Focused = overlay;
                m_gameHelp.IsVisible = m_state.NotificationTime <= 0f;
                if (game is PSPTetrisGame tetris) {
                    m_score.Text = $"得分 {tetris.Points}";
                    m_status.Text = $"等级 {tetris.Level} · 行数 {tetris.Lines}";
                    overlay = game.IsPaused || game.Phase == PSPGamePhase.Finished;
                    m_message.IsVisible = m_messageHint.IsVisible = overlay;
                    m_message.Text = game.Phase == PSPGamePhase.Finished ? "游戏结束" : "已暂停";
                    m_messageHint.Text = game.Phase == PSPGamePhase.Finished ? $"得分 {tetris.Points} · X 再来一局 · O 返回" : "X 或 START 继续 · O 返回";
                    m_gameAction.Text = game.Phase == PSPGamePhase.Finished ? "再来一局" : game.IsPaused ? "继续" : "暂停";
                    m_gameAction.Focused = overlay;
                    m_gameHelp.Text = "左右移动 · 上/X 旋转 · 下软降 · □/△ 硬降 · START 暂停";
                    m_gameAction.IsEnabled = true;
                    m_gameAction.IsVisible = true;
                }
                else if (game is PSPOnlinePongGame online) {
                    m_gameTitle.Text = "乒乓对决 / 联机";
                    m_status.Text = online.Status;
                    overlay = online.IsSynchronizing || game.Phase is PSPGamePhase.Ready or PSPGamePhase.Finished;
                    m_message.IsVisible = m_messageHint.IsVisible = overlay;
                    m_message.Text = online.OverlayTitle;
                    m_messageHint.Text = online.OverlayHint;
                    m_gameHelp.Text = game.Phase == PSPGamePhase.RoundDelay ? "本回合结束，稍后自动发球。" : "摇杆 / 左右方向键接球 · O 离开对局";
                    m_gameAction.Text = game.Phase == PSPGamePhase.Finished ? "X 返回房间" : "进行中";
                    m_gameAction.IsEnabled = game.Phase == PSPGamePhase.Finished;
                    m_gameAction.IsVisible = game.Phase == PSPGamePhase.Finished;
                }
                else {
                    m_gameHelp.Text = "左右移动接球 · X 发球 · START 暂停";
                    m_gameAction.IsEnabled = true;
                }
            }
            m_notification.Text = m_state.NotificationText;
            m_notification.IsVisible = m_state.NotificationTime > 0f;
        }

        public override void MeasureOverride(Vector2 size) {
            foreach (Widget child in Children) child.Measure(size);
            DesiredSize = size;
        }

        void Place(Widget widget, float x, float y, float width, float height) => PSPConsoleDialog.Place(widget,
            new Vector2(x / 960f * ActualSize.X, y / 540f * ActualSize.Y), new Vector2(width / 960f * ActualSize.X, height / 540f * ActualSize.Y));

        void Label(LabelWidget label, float x, float y, float width, float height, float fontScale) {
            label.FontScale = fontScale * MathUtils.Max(0.18f, ActualSize.X / 640f);
            Place(label, x, y, width, height);
        }
        void Button(PSPConsoleActionWidget button, float x, float y, float width, float height, float fontScale = 0.55f) {
            button.FontScale = fontScale * MathUtils.Max(0.18f, ActualSize.X / 640f);
            Place(button, x, y, width, height);
        }

        public override void ArrangeOverride() {
            Place(m_scene, 0f, 0f, 960f, 540f);
            Place(m_board, 0f, 0f, 960f, 540f);
            Place(m_pongMenu, 0f, 0f, 960f, 540f);
            Label(m_header, 40f, 13f, 110f, 36f, 0.65f);
            Label(m_clock, 775f, 17f, 108f, 31f, 0.48f);
            Label(m_tag, 42f, 298f, 485f, 32f, 0.43f);
            Label(m_title, 40f, 331f, 500f, 60f, 1.05f);
            Label(m_description, 42f, 401f, 520f, 31f, 0.44f);
            Label(m_record, 42f, 439f, 520f, 28f, 0.38f);
            m_tag.Color = new Color(173, 208, 234);
            m_description.Color = new Color(204, 215, 231);
            m_record.Color = new Color(157, 174, 195);
            Button(m_play, 565f, 435f, 211f, 56f);
            Button(m_trophies, 791f, 435f, 131f, 56f, 0.42f);
            Place(m_collectionIcon, 40f, 75f, 104f, 104f);
            float tileX = 40f;
            foreach (PSPGameTileWidget tile in m_tiles) {
                float size = tile.Selected ? 104f : 78f;
                Place(tile, tileX, 75f, size, size);
                tileX += size + 16f;
            }
            Label(m_collectionTag, 42f, 298f, 485f, 32f, 0.43f);
            Label(m_collectionTitle, 40f, 331f, 500f, 60f, 1.05f);
            Label(m_collectionDescription, 42f, 401f, 485f, 31f, 0.44f);
            Label(m_collectionRecord, 42f, 439f, 482f, 28f, 0.38f);
            m_collectionTag.Color = new Color(173, 208, 234);
            m_collectionDescription.Color = new Color(204, 215, 231);
            m_collectionRecord.Color = new Color(157, 174, 195);
            Button(m_collectionPlay, 565f, 435f, 211f, 56f);
            Button(m_collectionBack, 791f, 435f, 131f, 56f, 0.42f);
            Label(m_trophyTitle, 40f, 41f, 610f, 55f, 0.83f);
            Label(m_trophyCount, 675f, 63f, 245f, 32f, 0.45f);
            for (int i = 0; i < 3; i++) {
                float y = 158f + i * 94f;
                Label(m_trophyNames[i], 128f, y + 10f, 515f, 29f, 0.58f);
                Label(m_trophyDescriptions[i], 128f, y + 44f, 570f, 26f, 0.4f);
                Label(m_trophyStatuses[i], 733f, y + 22f, 158f, 31f, 0.46f);
            }
            Button(m_trophyBack, 751f, 465f, 169f, 47f, 0.48f);
            Label(m_gameTitle, 28f, 14f, 300f, 35f, 0.54f);
            Label(m_score, 330f, 9f, 300f, 46f, 0.78f);
            Label(m_status, 656f, 18f, 274f, 31f, 0.4f);
            Label(m_message, 250f, 245f, 460f, 43f, 0.87f);
            Label(m_messageHint, 250f, 303f, 460f, 31f, 0.39f);
            Label(m_gameHelp, 147f, 505f, 624f, 26f, 0.33f);
            Button(m_gameBack, 28f, 505f, 112f, 30f, 0.3f);
            Button(m_gameAction, 785f, 505f, 147f, 30f, 0.32f);
            Label(m_notification, 298f, 505f, 436f, 28f, 0.38f);
        }

        public override void Update() {
            if (m_state.Page == PSPConsolePage.Home) {
                if (m_play.IsClicked || m_collectionIcon.IsClicked) {
                    m_state.Focus(PSPHomeFocus.Play);
                    m_state.Pulse(PSPConsoleControl.Cross);
                    m_state.OpenCollection();
                }
                if (m_trophies.IsClicked) {
                    m_state.Focus(PSPHomeFocus.Trophies);
                    m_state.OpenTrophies();
                }
            }
            else if (m_state.Page == PSPConsolePage.Collection) {
                foreach (PSPGameTileWidget tile in m_tiles) if (tile.IsClicked) m_state.SelectGame(tile.GameIndex);
                if (m_collectionPlay.IsClicked) {
                    m_state.Pulse(PSPConsoleControl.Cross);
                    m_state.StartGame();
                }
                if (m_collectionBack.IsClicked) {
                    m_state.Pulse(PSPConsoleControl.Circle);
                    m_state.Back();
                }
            }
            else if (m_state.Page == PSPConsolePage.Trophies) {
                if (m_trophyBack.IsClicked) { m_state.Pulse(PSPConsoleControl.Circle); m_state.Back(); }
            }
            else if (m_state.Page == PSPConsolePage.Game) {
                if (m_gameBack.IsClicked) { m_state.Pulse(PSPConsoleControl.Circle); m_state.Back(); }
                else if (m_gameAction.IsClicked) {
                    if (m_state.Game.Phase != PSPGamePhase.Ready && m_state.Game.Phase != PSPGamePhase.Finished && !m_state.Game.IsPaused) m_state.Game.TogglePause();
                    else { m_state.Pulse(PSPConsoleControl.Cross); m_state.Confirm(); }
                }
            }
            Refresh();
        }
    }
}
