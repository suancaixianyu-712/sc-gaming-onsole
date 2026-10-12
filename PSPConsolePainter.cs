using System;
using Engine;
using Engine.Graphics;

namespace Game {
    internal readonly struct PSPConsolePainter {
        readonly Widget m_widget;
        readonly FlatBatch2D m_batch;
        readonly float m_width, m_height;

        public PSPConsolePainter(Widget widget, FlatBatch2D batch, float width = 960f, float height = 540f) {
            m_widget = widget;
            m_batch = batch;
            m_width = width;
            m_height = height;
        }

        Vector2 Point(float x, float y) => m_widget.WidgetToScreen(new Vector2(x / m_width * m_widget.ActualSize.X, y / m_height * m_widget.ActualSize.Y));
        Color Tint(Color color) => color * m_widget.GlobalColorTransform;

        public void Rect(float x, float y, float width, float height, Color color) =>
            Quad(new Vector2(x, y), new Vector2(x + width, y), new Vector2(x + width, y + height), new Vector2(x, y + height), color);

        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color) =>
            m_batch.QueueQuad(Point(a.X, a.Y), Point(b.X, b.Y), Point(c.X, c.Y), Point(d.X, d.Y), 0f, Tint(color));

        public void Gradient(float x, float y, float width, float height, Color topLeft, Color topRight, Color bottomRight, Color bottomLeft) =>
            m_batch.QueueQuad(Point(x, y), Point(x + width, y), Point(x + width, y + height), Point(x, y + height), 0f,
                Tint(topLeft), Tint(topRight), Tint(bottomRight), Tint(bottomLeft));

        public void Disc(float x, float y, float radiusX, float radiusY, Color color) {
            const int segments = 24;
            for (int i = 0; i < segments; i++) {
                float a = i * MathF.Tau / segments, b = (i + 1) * MathF.Tau / segments;
                m_batch.QueueTriangle(Point(x, y), Point(x + MathF.Cos(a) * radiusX, y + MathF.Sin(a) * radiusY),
                    Point(x + MathF.Cos(b) * radiusX, y + MathF.Sin(b) * radiusY), 0f, Tint(color));
            }
        }

        public void RoundedRect(float x, float y, float width, float height, float radius, Color color) {
            radius = MathF.Min(radius, MathF.Min(width, height) / 2f);
            Rect(x + radius, y, width - radius * 2f, height, color);
            Rect(x, y + radius, width, height - radius * 2f, color);
            Disc(x + radius, y + radius, radius, radius, color);
            Disc(x + width - radius, y + radius, radius, radius, color);
            Disc(x + radius, y + height - radius, radius, radius, color);
            Disc(x + width - radius, y + height - radius, radius, radius, color);
        }

        public void Line(float x1, float y1, float x2, float y2, float thickness, Color color) {
            Vector2 direction = new(x2 - x1, y2 - y1);
            if (direction.LengthSquared() <= 0f) return;
            Vector2 normal = new Vector2(-direction.Y, direction.X) * (thickness / (2f * direction.Length()));
            Quad(new Vector2(x1, y1) + normal, new Vector2(x2, y2) + normal,
                new Vector2(x2, y2) - normal, new Vector2(x1, y1) - normal, color);
        }

        public void Trophy(float x, float y, float scale, Color color) {
            Quad(new Vector2(x - 14f * scale, y), new Vector2(x + 14f * scale, y),
                new Vector2(x + 8f * scale, y + 18f * scale), new Vector2(x - 8f * scale, y + 18f * scale), color);
            Line(x - 14f * scale, y + 3f * scale, x - 22f * scale, y + 3f * scale, 3f * scale, color);
            Line(x - 22f * scale, y + 3f * scale, x - 20f * scale, y + 15f * scale, 3f * scale, color);
            Line(x - 20f * scale, y + 15f * scale, x - 8f * scale, y + 18f * scale, 3f * scale, color);
            Line(x + 14f * scale, y + 3f * scale, x + 22f * scale, y + 3f * scale, 3f * scale, color);
            Line(x + 22f * scale, y + 3f * scale, x + 20f * scale, y + 15f * scale, 3f * scale, color);
            Line(x + 20f * scale, y + 15f * scale, x + 8f * scale, y + 18f * scale, 3f * scale, color);
            Rect(x - 3f * scale, y + 18f * scale, 6f * scale, 11f * scale, color);
            RoundedRect(x - 15f * scale, y + 29f * scale, 30f * scale, 5f * scale, 2f * scale, color);
        }
    }

    public sealed class PSPConsoleActionWidget : CanvasWidget {
        readonly LabelWidget m_label;
        readonly ClickableWidget m_clickable = new() { SoundName = "Audio/UI/ButtonClick" };
        public bool Focused { get; set; }
        public bool Primary { get; set; }
        public bool IsClicked => m_clickable.IsClicked;
        public string Text { get => m_label.Text; set => m_label.Text = value; }
        public float FontScale { get => m_label.FontScale; set => m_label.FontScale = value; }

        public PSPConsoleActionWidget(string text) {
            IsHitTestVisible = false;
            m_label = PSPConsoleDialog.MakeLabel(text, Color.White, 0.55f);
            Children.Add(m_label);
            Children.Add(m_clickable);
        }
        public override void MeasureOverride(Vector2 size) {
            DesiredSize = size;
            IsDrawRequired = true;
            m_label.Color = Primary ? new Color(18, 25, 35) : Color.White;
            foreach (Widget child in Children) child.Measure(size);
        }
        public override void ArrangeOverride() {
            foreach (Widget child in Children) child.Arrange(Vector2.Zero, ActualSize);
        }
        public override void Draw(DrawContext dc) {
            if (ActualSize.X <= 0f || ActualSize.Y <= 0f) return;
            PSPConsolePainter p = new(this, dc.PrimitivesRenderer2D.FlatBatch(0, DepthStencilState.None), 200f, 54f);
            if (Focused) p.RoundedRect(0f, 0f, 200f, 54f, 27f, Color.White);
            Color fill = Primary ? new Color(239, 242, 247) : new Color(37, 47, 64);
            if (m_clickable.IsPressed) fill = Primary ? new Color(193, 205, 218) : new Color(62, 79, 101);
            p.RoundedRect(Focused ? 3f : 0f, Focused ? 3f : 0f, Focused ? 194f : 200f, Focused ? 48f : 54f, 24f, fill);
        }
    }

    public sealed class PSPGameTileWidget : ClickableWidget {
        static readonly Color[] BrickColors = [new(254, 191, 99), new(239, 100, 135), new(144, 124, 235)];
        public int GameIndex { get; }
        public bool Selected { get; set; }
        public bool Focused { get; set; }
        public PSPGameTileWidget(int index) { GameIndex = index; SoundName = "Audio/UI/ButtonClick"; }
        public override void MeasureOverride(Vector2 size) { DesiredSize = size; IsDrawRequired = true; }
        public override void Draw(DrawContext dc) {
            PSPConsolePainter p = new(this, dc.PrimitivesRenderer2D.FlatBatch(0, DepthStencilState.None), 100f, 100f);
            if (Selected) p.RoundedRect(0f, 0f, 100f, 100f, 16f, Focused ? Color.White : new Color(152, 169, 191));
            p.RoundedRect(Selected ? 3f : 0f, Selected ? 3f : 0f, Selected ? 94f : 100f, Selected ? 94f : 100f, 13f,
                GameIndex == 0 ? new Color(19, 72, 109) : new Color(66, 28, 95));
            if (GameIndex == 0) {
                p.RoundedRect(19f, 21f, 48f, 7f, 3f, new Color(252, 182, 97));
                p.RoundedRect(32f, 74f, 48f, 7f, 3f, new Color(114, 232, 248));
                for (int i = 0; i < 5; i++) p.Rect(14f + i * 16f, 49f, 7f, 2f, new Color(107, 150, 183));
                p.Disc(59f, 42f, 7f, 7f, Color.White);
            }
            else {
                for (int row = 0; row < 3; row++) for (int column = 0; column < 3; column++)
                    p.Rect(15f + column * 24f, 17f + row * 13f, 21f, 9f, BrickColors[row]);
                p.Disc(58f, 66f, 5f, 5f, Color.White);
                p.RoundedRect(24f, 81f, 51f, 6f, 3f, new Color(115, 221, 247));
            }
        }
    }
}
