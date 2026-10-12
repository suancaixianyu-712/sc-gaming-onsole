using Engine;
using Engine.Input;

namespace Game {
    public sealed class PSPConsoleJoystickWidget : Widget {
        enum CaptureDevice { None, Touch, Mouse, Gamepad }

        CaptureDevice m_device;
        int m_touchId;
        Vector2 m_origin;

        // X points right and Y points up. The circular gate limits length to 1.
        public Vector2 Value { get; private set; }
        public bool IsHeld => m_device != CaptureDevice.None;

        public override void MeasureOverride(Vector2 parentAvailableSize) => DesiredSize = parentAvailableSize;

        public override bool HitTest(Vector2 point) {
            if (!base.HitTest(point) || ActualSize.X <= 0f || ActualSize.Y <= 0f) return false;
            Vector2 local = ScreenToWidget(point) - ActualSize / 2f;
            return (local / (ActualSize / 2f)).LengthSquared() <= 1f;
        }

        public override void Update() {
            if (!Window.IsActive) {
                Reset();
                return;
            }
            WidgetInput input = Input;
            if (m_device == CaptureDevice.None) {
                foreach (TouchLocation touch in input.TouchLocations) {
                    if (touch.State == TouchLocationState.Pressed && HitTestGlobal(touch.Position) == this) {
                        m_touchId = touch.Id;
                        Begin(CaptureDevice.Touch, touch.Position);
                        break;
                    }
                }
                if (m_device == CaptureDevice.None && input.IsMouseButtonDownOnce(MouseButton.Left)
                    && input.MousePosition.HasValue && HitTestGlobal(input.MousePosition.Value) == this) {
                    Begin(CaptureDevice.Mouse, input.MousePosition.Value);
                }
                if (m_device == CaptureDevice.None && input.IsPadButtonDownOnce(GamePadButton.A)
                    && HitTestGlobal(input.PadCursorPosition) == this) {
                    Begin(CaptureDevice.Gamepad, input.PadCursorPosition);
                }
            }
            switch (m_device) {
                case CaptureDevice.Touch:
                    // Track this finger independently of WidgetInput.Press so a
                    // second finger can operate buttons without stealing the stick.
                    foreach (TouchLocation touch in input.TouchLocations) {
                        if (touch.Id != m_touchId) continue;
                        if (touch.State == TouchLocationState.Released) Reset();
                        else Move(touch.Position);
                        return;
                    }
                    Reset();
                    break;
                case CaptureDevice.Mouse:
                    if (input.IsMouseButtonDown(MouseButton.Left) && input.MousePosition.HasValue) Move(input.MousePosition.Value);
                    else Reset();
                    break;
                case CaptureDevice.Gamepad:
                    if (input.IsPadButtonDown(GamePadButton.A)) Move(input.PadCursorPosition);
                    else Reset();
                    break;
            }
        }

        void Begin(CaptureDevice device, Vector2 position) {
            m_device = device;
            m_origin = position;
            Value = Vector2.Zero;
        }

        void Move(Vector2 position) {
            // Use displacement from the initial press to avoid a jump when the
            // finger lands off-center. ScreenToWidget also handles UI scaling.
            Vector2 displacement = ScreenToWidget(position) - ScreenToWidget(m_origin);
            float radius = MathUtils.Max(1f, MathUtils.Min(ActualSize.X, ActualSize.Y)
                * PSPConsoleControls.AnalogTravel / PSPConsoleControls.AnalogHitDiameter);
            Vector2 value = new(displacement.X / radius, -displacement.Y / radius);
            Value = value.LengthSquared() > 1f ? Vector2.Normalize(value) : value;
        }

        public void Reset() {
            m_device = CaptureDevice.None;
            Value = Vector2.Zero;
        }

        public override void UpdateCeases() {
            Reset();
            base.UpdateCeases();
        }
    }
}
