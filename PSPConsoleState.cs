using System;
using Engine;
using GameConsole.Networking;

namespace Game {
    public enum PSPConsolePage { Home, Collection, Trophies, Game, PongMode, OnlineLobby }
    public enum PSPHomeFocus { Play, Trophies }

    public sealed partial class PSPConsoleState : IDisposable {
        public const int GameCount = 3;
        public static readonly string[] GameNames = ["乒乓对决", "乒乓打砖块", "俄罗斯方块"];
        public static readonly string[] GameDescriptions = ["单人 AI 或双人联机，率先取得 5 分。", "左右移动挡板，击破所有砖块。", "原版方块堆叠消行，等级越高下落越快。"];
        public const string CollectionName = "小游戏合集";
        public const string CollectionTag = "合集  /  现有小游戏";
        public const string CollectionDescription = "乒乓对决、乒乓打砖块、俄罗斯方块。进入合集后选择要玩的游戏。";
        readonly float[] m_releaseDelays = new float[(int)PSPConsoleControl.Count];
        Vector2 m_analogTarget;

        public PSPConsoleProgress Progress { get; }
        public GameConsoleClient Network { get; private set; } = new();
        public PSPConsolePage Page { get; private set; }
        public PSPHomeFocus HomeFocus { get; private set; }
        public PSPPaddleGame Game { get; private set; }
        public float[] PressAmounts { get; } = new float[(int)PSPConsoleControl.Count];
        public int SelectedGame { get; private set; }
        public bool RequestClose { get; private set; }
        public Vector2 AnalogInput { get; private set; }
        public Vector2 AnalogOffset { get; private set; }
        public bool IsAnalogHeld { get; private set; }
        public float BackgroundBlend { get; private set; }
        public float NotificationTime { get; private set; }
        public string NotificationText { get; private set; } = "";
        public bool NotificationIsTrophy { get; private set; }

        public PSPConsoleState(PSPConsoleProgress progress = null, string playerName = "玩家") {
            Progress = progress ?? new PSPConsoleProgress();
            m_playerName = string.IsNullOrWhiteSpace(playerName) ? "玩家" : playerName;
            AttachNetwork(Network);
        }

        public void SetAnalogInput(Vector2 value, bool held) {
            IsAnalogHeld = held;
            m_analogTarget = held ? value : Vector2.Zero;
            if (m_analogTarget.LengthSquared() > 1f) m_analogTarget = Vector2.Normalize(m_analogTarget);
            float length = m_analogTarget.Length();
            const float deadZone = 0.12f;
            AnalogInput = length > deadZone ? m_analogTarget * ((length - deadZone) / ((1f - deadZone) * length)) : Vector2.Zero;
        }

        public void ResetAnalog() {
            m_analogTarget = AnalogInput = AnalogOffset = Vector2.Zero;
            IsAnalogHeld = false;
            PressAmounts[(int)PSPConsoleControl.Analog] = 0f;
            m_releaseDelays[(int)PSPConsoleControl.Analog] = 0f;
        }

        public void Activate(PSPConsoleControl control) {
            if (control == PSPConsoleControl.Circle) { Back(); return; }
            if (control == PSPConsoleControl.Home) { ReturnHome(); return; }
            if (control == PSPConsoleControl.Cross) { Confirm(); return; }
            if (Page == PSPConsolePage.Game) {
                Game?.HandleButton(control);
                return;
            }
            if (Page is PSPConsolePage.PongMode or PSPConsolePage.OnlineLobby) {
                int direction = control is PSPConsoleControl.Left or PSPConsoleControl.Up ? -1
                    : control is PSPConsoleControl.Right or PSPConsoleControl.Down ? 1 : 0;
                if (direction != 0) {
                    if (Page == PSPConsolePage.PongMode) ModeSelection = (ModeSelection + 1) % 2;
                    else LobbySelection = (LobbySelection + direction + LobbyActionCount) % LobbyActionCount;
                }
                return;
            }
            if (Page == PSPConsolePage.Collection) {
                int direction = control is PSPConsoleControl.Left or PSPConsoleControl.Up or PSPConsoleControl.LeftShoulder ? -1
                    : control is PSPConsoleControl.Right or PSPConsoleControl.Down or PSPConsoleControl.RightShoulder ? 1 : 0;
                if (direction != 0) SwitchGame(direction);
                return;
            }
            if (Page != PSPConsolePage.Home) return;
            // Home is the collection shell; shoulder buttons do not switch games here.
            switch (control) {
                case PSPConsoleControl.Left:
                case PSPConsoleControl.Up:
                    HomeFocus = PSPHomeFocus.Play;
                    break;
                case PSPConsoleControl.Right:
                case PSPConsoleControl.Down:
                    HomeFocus = PSPHomeFocus.Trophies;
                    break;
                // Square and Triangle deliberately have no shell action.
            }
        }

        public void SelectGame(int index) {
            if (Page is PSPConsolePage.Game or PSPConsolePage.PongMode or PSPConsolePage.OnlineLobby) return;
            SelectedGame = Math.Clamp(index, 0, GameCount - 1);
        }

        public void SwitchGame(int direction) {
            if (Page is PSPConsolePage.Game or PSPConsolePage.PongMode or PSPConsolePage.OnlineLobby) return;
            SelectedGame = (SelectedGame + direction % GameCount + GameCount) % GameCount;
        }

        public void Focus(PSPHomeFocus focus) => HomeFocus = focus;

        public void Confirm() {
            if (Page == PSPConsolePage.Game) Game.Confirm();
            else if (Page == PSPConsolePage.PongMode) { if (ModeSelection == 0) StartAiPong(); else OpenOnlineLobby(); }
            else if (Page == PSPConsolePage.OnlineLobby) ActivateLobbySelection();
            else if (Page == PSPConsolePage.Collection) StartGame();
            else if (Page == PSPConsolePage.Trophies) ReturnHome();
            else if (HomeFocus == PSPHomeFocus.Trophies) OpenTrophies();
            else OpenCollection();
        }

        public void OpenCollection() {
            if (Page != PSPConsolePage.Home) return;
            Page = PSPConsolePage.Collection;
            ResetAnalog();
        }

        public void StartGame() {
            if (SelectedGame == 0) {
                Page = PSPConsolePage.PongMode;
                ModeSelection = 0;
                ResetAnalog();
                return;
            }
            Game = SelectedGame == 2 ? new PSPTetrisGame(Progress, UnlockTrophy) : new PSPPaddleGame(SelectedGame, Progress, UnlockTrophy);
            Page = PSPConsolePage.Game;
            ResetAnalog();
        }

        public void OpenTrophies() {
            if (Page != PSPConsolePage.Home) return;
            Page = PSPConsolePage.Trophies;
            ResetAnalog();
        }

        public void Back() {
            if (Page == PSPConsolePage.Home) RequestClose = true;
            else if (Page == PSPConsolePage.Game && Game.IsOnline) ReturnToOnlineRoom(Game.Phase != PSPGamePhase.Finished);
            else if (Page == PSPConsolePage.OnlineLobby) {
                if (OnlineRoom != null) LeaveOnlineRoom();
                else { DisconnectOnline(); Page = PSPConsolePage.PongMode; ModeSelection = 1; }
            }
            else if (Page is PSPConsolePage.Collection or PSPConsolePage.Trophies) ReturnHome();
            else ReturnToCollection();
        }

        public void ReturnHome() {
            if (Network.IsConnected || OnlineBusy) DisconnectOnline();
            Page = PSPConsolePage.Home;
            Game = null;
            ResetAnalog();
        }

        public void ReturnToCollection() {
            if (Network.IsConnected || OnlineBusy) DisconnectOnline();
            Page = PSPConsolePage.Collection;
            Game = null;
            ResetAnalog();
        }

        void UnlockTrophy(int index) {
            if (!Progress.Unlock(index)) return;
            NotificationText = "奖杯已解锁 · " + PSPConsoleProgress.TrophyNames[index];
            NotificationIsTrophy = true;
            NotificationTime = 3f;
        }

        public void Pulse(PSPConsoleControl control) => m_releaseDelays[(int)control] = 0.09f;

        public void UpdateAnimation(float deltaTime, bool[] held) {
            PumpOnlineActions();
            try { Network.Pump(); }
            catch (Exception exception) { Log.Error($"游戏机网络消息处理失败：{exception.Message}"); }
            UpdateLobbyRefresh();
            float dt = Math.Clamp(deltaTime, 0f, 0.1f);
            float blendTarget = Page == PSPConsolePage.Home ? 0.35f : SelectedGame == 0 ? 0f : SelectedGame == 1 ? 1f : 0.6f;
            BackgroundBlend += (blendTarget - BackgroundBlend) * (1f - MathF.Exp(-9f * dt));
            NotificationTime = MathF.Max(0f, NotificationTime - dt);
            AnalogOffset += (m_analogTarget - AnalogOffset) * (1f - MathF.Exp(-(IsAnalogHeld ? 45f : 24f) * dt));
            if (Vector2.Distance(AnalogOffset, m_analogTarget) < 0.003f) AnalogOffset = m_analogTarget;
            for (int i = 1; i < (int)PSPConsoleControl.DPadCenter; i++) {
                bool down = held[i] || m_releaseDelays[i] > 0f;
                m_releaseDelays[i] = Math.Max(0f, m_releaseDelays[i] - dt);
                float target = down ? 1f : 0f;
                PressAmounts[i] += (target - PressAmounts[i]) * (1f - MathF.Exp(-(down ? 35f : 20f) * dt));
                if (MathF.Abs(PressAmounts[i] - target) < 0.002f) PressAmounts[i] = target;
            }
            PressAmounts[(int)PSPConsoleControl.DPadCenter] = 0.5f * Math.Max(
                Math.Max(PressAmounts[(int)PSPConsoleControl.Up], PressAmounts[(int)PSPConsoleControl.Down]),
                Math.Max(PressAmounts[(int)PSPConsoleControl.Left], PressAmounts[(int)PSPConsoleControl.Right]));
            if (Page == PSPConsolePage.Game && (Window.IsActive || Game.IsOnline)) {
                float digital = (held[(int)PSPConsoleControl.Right] ? 1f : 0f) - (held[(int)PSPConsoleControl.Left] ? 1f : 0f);
                bool usingDpad = held[(int)PSPConsoleControl.Right] || held[(int)PSPConsoleControl.Left];
                if (Game is PSPTetrisGame tetris) tetris.SoftDrop = Window.IsActive && (held[(int)PSPConsoleControl.Down] || AnalogInput.Y > 0.6f);
                Game.Update(dt, Window.IsActive ? usingDpad ? digital : AnalogInput.X : 0f);
            }
        }
    }
}
