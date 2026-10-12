using System;
using System.Collections.Generic;
using Engine;
using Engine.Graphics;
using Engine.Input;

namespace Game {
    public sealed class PSPConsoleDialog : Dialog {
        readonly IInventory m_inventory;
        readonly int m_slotIndex;
        readonly int m_activeSlotAtOpen;
        readonly ComponentPlayer m_player;
        readonly int m_contents;
        readonly PSPConsoleState m_state;
        readonly bool[] m_held = new bool[(int)PSPConsoleControl.Count];
        readonly List<PSPConsoleControlWidget> m_controls = new();
        readonly List<(Widget, PSPConsoleControlDefinition)> m_controlWidgets = new();
        readonly PSPConsoleJoystickWidget m_joystick = new();
        readonly RectangleWidget m_background;
        readonly PSPConsoleModelWidget m_model;
        readonly PSPConsoleScreenWidget m_screen;
        readonly BevelledButtonWidget m_closeButton;
        readonly LabelWidget m_caption;
        readonly LabelWidget m_hint;
        readonly List<(LabelWidget, Vector2)> m_controlLabels = new();
        bool m_closed;
        PSPConsolePage m_lastPage;

        public PSPConsoleDialog(IInventory inventory, int slotIndex, ComponentPlayer player, PSPConsoleProgress progress = null) {
            m_state = new PSPConsoleState(progress, player.PlayerData.Name);
            m_inventory = inventory;
            m_slotIndex = slotIndex;
            m_activeSlotAtOpen = inventory.ActiveSlotIndex;
            m_player = player;
            m_contents = BlocksManager.GetBlockIndex<PSPBlock>(true);
            ClampToBounds = true;
            m_background = new RectangleWidget { FillColor = new Color(10, 16, 24), OutlineColor = Color.Transparent };
            m_model = new PSPConsoleModelWidget(m_state);
            m_screen = new PSPConsoleScreenWidget(m_state);
            m_caption = MakeLabel("PSP", new Color(180, 194, 207), 0.7f);
            m_hint = MakeLabel("L / R 切换游戏 · 方向键选择 · X 确认 · O 返回", new Color(151, 165, 179), 0.6f);
            m_closeButton = new BevelledButtonWidget {
                Text = "关闭",
                FontScale = 0.65f,
                CenterColor = new Color(24, 35, 47),
                BevelColor = new Color(52, 68, 83),
                BevelSize = 1f
            };
            Children.Add(m_background);
            Children.Add(m_model);
            Children.Add(m_screen);
            foreach (PSPConsoleControlDefinition definition in PSPConsoleControls.Definitions) {
                Widget control;
                if (definition.Control == PSPConsoleControl.Analog) {
                    control = m_joystick;
                }
                else {
                    PSPConsoleControlWidget button = new(definition);
                    m_controls.Add(button);
                    control = button;
                }
                m_controlWidgets.Add((control, definition));
                Children.Add(control);
            }
            AddControlLabel("HOME", new Vector2(0.04f, -0.0342f));
            AddControlLabel("SELECT", new Vector2(0.02f, -0.0342f));
            AddControlLabel("START", new Vector2(-0.02f, -0.0342f));
            AddControlLabel("L", new Vector2(0.067f, 0.0353f));
            AddControlLabel("R", new Vector2(-0.067f, 0.0353f));
            Children.Add(m_caption);
            Children.Add(m_hint);
            Children.Add(m_closeButton);
        }

        void AddControlLabel(string text, Vector2 position) {
            LabelWidget label = MakeLabel(text, new Color(222, 231, 237), 0.4f);
            m_controlLabels.Add((label, position));
            Children.Add(label);
        }

        public override void ChangeParent(ContainerWidget parentWidget) {
            bool changed = parentWidget != ParentWidget;
            base.ChangeParent(parentWidget);
            if (changed) {
                // Drop the movement/aim gesture that was held when opening,
                // and discard touches from the interface before returning to play.
                PSPConsoleInputGuard.ClearPlayerControls(m_player.ComponentInput);
                m_joystick.Reset();
                m_state.ResetAnalog();
                if (parentWidget == null) { m_state.Dispose(); m_screen.EndTextInput(); }
            }
        }

        internal static LabelWidget MakeLabel(string text, Color color, float fontScale) => new() {
            Text = text,
            Color = color,
            FontScale = fontScale,
            TextAnchor = TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter,
            DropShadow = true,
            IsHitTestVisible = false
        };

        internal static void Place(Widget widget, Vector2 position, Vector2 size) {
            widget.Measure(size);
            widget.Arrange(position, size);
        }

        public override void MeasureOverride(Vector2 parentAvailableSize) {
            foreach (Widget child in Children) child.Measure(parentAvailableSize);
            DesiredSize = parentAvailableSize;
        }

        public override void ArrangeOverride() {
            Place(m_background, Vector2.Zero, ActualSize);
            float pixelsPerMeter = MathUtils.Max(1f, MathUtils.Min(
                MathUtils.Max(ActualSize.X - 32f, 1f) / PSPConsoleControls.ModelWidth,
                MathUtils.Max(ActualSize.Y - 88f, 1f) / PSPConsoleControls.ModelHeight
            ));
            Vector2 center = ActualSize / 2f + new Vector2(0f, 6f);
            Vector2 modelSize = new Vector2(PSPConsoleControls.ModelWidth, PSPConsoleControls.ModelHeight) * pixelsPerMeter;
            Place(m_model, center - modelSize / 2f, modelSize);
            // LCD image bounds from the GLB: X ±0.048, Y -0.0255..0.0285.
            // This rectangle becomes the live screen, inside the original bezel.
            Place(m_screen, center + new Vector2(-0.048f, -0.0285f) * pixelsPerMeter, new Vector2(0.096f, 0.054f) * pixelsPerMeter);
            foreach ((Widget control, PSPConsoleControlDefinition definition) in m_controlWidgets) {
                Vector2 controlCenter = center - definition.ModelCenter * pixelsPerMeter;
                Vector2 hitSize = definition.HitSize * pixelsPerMeter;
                Place(control, controlCenter - hitSize / 2f, hitSize);
            }
            foreach ((LabelWidget label, Vector2 position) in m_controlLabels) {
                Vector2 labelSize = new(0.013f * pixelsPerMeter, 0.0034f * pixelsPerMeter);
                label.FontScale = 0.36f * pixelsPerMeter / 5500f;
                Place(label, center - position * pixelsPerMeter - labelSize / 2f, labelSize);
            }
            Place(m_caption, new Vector2(16f, 8f), new Vector2(70f, 28f));
            Place(m_closeButton, new Vector2(MathUtils.Max(ActualSize.X - 102f, 0f), 8f), new Vector2(86f, 30f));
            Place(m_hint, new Vector2(12f, MathUtils.Max(ActualSize.Y - 30f, 0f)), new Vector2(MathUtils.Max(ActualSize.X - 24f, 1f), 24f));
        }

        public override void Update() {
            if (m_closed) return;
            bool invalidItem = m_inventory.ActiveSlotIndex != m_activeSlotAtOpen
                || m_inventory.GetSlotCount(m_slotIndex) <= 0
                || Terrain.ExtractContents(m_inventory.GetSlotValue(m_slotIndex)) != m_contents;
            if (invalidItem || !m_player.IsAddedToProject || m_player.ComponentHealth.Health <= 0f || m_closeButton.IsClicked) {
                Close();
                return;
            }
            if (Input.Back || Input.Cancel) {
                if (m_screen.IsTextInputActive) m_screen.EndTextInput();
                else m_state.Back();
            }
            Array.Clear(m_held);
            foreach (PSPConsoleControlWidget control in m_controls) {
                m_held[(int)control.Control] = control.IsPressed;
                if (control.IsTapped) m_state.Pulse(control.Control);
                if (control.IsClicked) {
                    if (m_screen.IsTextInputActive) {
                        m_screen.EndTextInput();
                        if (control.Control == PSPConsoleControl.Circle) continue;
                    }
                    m_state.Pulse(control.Control);
                    m_state.Activate(control.Control);
                }
            }
            m_held[(int)PSPConsoleControl.Analog] = m_joystick.IsHeld;
            m_state.SetAnalogInput(m_joystick.Value, m_joystick.IsHeld);
            if (!m_screen.IsTextInputActive) {
                UpdateKeyboard(Key.LeftArrow, PSPConsoleControl.Left);
                UpdateKeyboard(Key.RightArrow, PSPConsoleControl.Right);
                UpdateKeyboard(Key.UpArrow, PSPConsoleControl.Up);
                UpdateKeyboard(Key.DownArrow, PSPConsoleControl.Down);
                UpdateKeyboard(Key.Enter, PSPConsoleControl.Cross);
                UpdateKeyboard(Key.Space, PSPConsoleControl.Cross);
                UpdateKeyboard(Key.A, PSPConsoleControl.Left);
                UpdateKeyboard(Key.D, PSPConsoleControl.Right);
                UpdateKeyboard(Key.P, PSPConsoleControl.Start);
                UpdateKeyboard(Key.Home, PSPConsoleControl.Home);
                UpdateKeyboard(Key.Z, PSPConsoleControl.Cross);
                UpdateKeyboard(Key.X, PSPConsoleControl.Circle);
                UpdateKeyboard(Key.W, PSPConsoleControl.Up);
                UpdateKeyboard(Key.S, PSPConsoleControl.Down);
                UpdateKeyboard(Key.C, PSPConsoleControl.Square);
                UpdateKeyboard(Key.V, PSPConsoleControl.Triangle);
                UpdateKeyboard(Key.Q, PSPConsoleControl.LeftShoulder);
                UpdateKeyboard(Key.E, PSPConsoleControl.RightShoulder);
            }
            Widget pressedWidget = Input.Press.HasValue ? HitTestGlobal(Input.Press.Value) : null;
            if (!m_screen.IsTextInputActive && Input.Ok && !Input.IsKeyDown(Key.Enter) && !Input.IsKeyDown(Key.Space) && !m_joystick.IsHeld
                && pressedWidget is not ClickableWidget) {
                m_state.Pulse(PSPConsoleControl.Cross);
                m_state.Activate(PSPConsoleControl.Cross);
            }
            if (m_state.RequestClose) { Close(); return; }
            if (m_lastPage != m_state.Page) {
                m_joystick.Reset();
                m_state.ResetAnalog();
                m_held[(int)PSPConsoleControl.Analog] = false;
                m_lastPage = m_state.Page;
            }
            m_state.UpdateAnimation(Time.FrameDuration, m_held);
            m_hint.Text = m_state.Page switch {
                PSPConsolePage.Game when m_state.Game.IsOnline => "摇杆 / 方向键左右移动 · O 离开 · 联机比赛持续进行",
                PSPConsolePage.Game when m_state.Game is PSPTetrisGame => "左右移动 · 上/X 旋转 · 下 软降 · □/△ 硬降 · START 暂停 · O 返回",
                PSPConsolePage.Game => "摇杆 / 方向键左右移动 · X 发球 · O 返回 · START 暂停",
                PSPConsolePage.Home => "方向键选择 · X 进入合集 · O 关闭",
                PSPConsolePage.Collection => "L / R 或方向键切换游戏 · X 开始 · O 返回主页",
                PSPConsolePage.Trophies => "X / O 返回主页",
                _ => "方向键选择 · X 确认 · O 返回"
            };
            m_screen.Refresh();
        }

        void UpdateKeyboard(Key key, PSPConsoleControl control) {
            m_held[(int)control] |= Input.IsKeyDown(key);
            if (Input.IsKeyDownOnce(key)) {
                m_state.Pulse(control);
                m_state.Activate(control);
            }
        }

        void Close() {
            m_closed = true;
            m_state.Dispose();
            m_screen.EndTextInput();
            m_joystick.Reset();
            m_state.ResetAnalog();
            Input.Clear();
            DialogsManager.HideDialog(this);
        }
    }

}
