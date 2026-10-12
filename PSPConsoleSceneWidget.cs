using System;
using Engine;
using Engine.Graphics;

namespace Game {
    public sealed class PSPConsoleSceneWidget : Widget {
        static readonly Color[] BrickColors = [new(251, 190, 91), new(245, 135, 104), new(227, 101, 148), new(154, 118, 228), new(91, 196, 222)];
        readonly PSPConsoleState m_state;

        public PSPConsoleSceneWidget(PSPConsoleState state) { m_state = state; IsHitTestVisible = false; }
        public override void MeasureOverride(Vector2 size) { DesiredSize = size; IsDrawRequired = true; }

        public override void Draw(DrawContext dc) {
            PSPConsolePainter p = new(this, dc.PrimitivesRenderer2D.FlatBatch(0, DepthStencilState.None));
            if (m_state.Page == PSPConsolePage.Game) DrawGame(p);
            else DrawHome(p);
            if (m_state.Page == PSPConsolePage.Trophies) DrawTrophies(p);
            if (m_state.NotificationTime > 0f) {
                p.Rect(263f, 504f, 480f, 31f, new Color(18, 29, 44, 242));
                if (m_state.NotificationIsTrophy) p.Trophy(284f, 511f, 0.43f, new Color(250, 212, 133));
            }
        }

        void DrawHome(PSPConsolePainter p) {
            float blend = m_state.BackgroundBlend;
            Color right = Color.Lerp(new Color(23, 73, 112), new Color(86, 38, 96), blend);
            p.Gradient(0f, 0f, 960f, 540f, new Color(10, 17, 29), right, new Color(10, 16, 28), new Color(9, 13, 23));
            DrawPongWallpaper(p, 1f - blend);
            DrawBrickWallpaper(p, blend);
            p.Gradient(0f, 0f, 960f, 540f, new Color(5, 10, 19, 35), new Color(5, 10, 19, 0),
                new Color(5, 10, 19, 80), new Color(5, 10, 19, 220));
            p.Gradient(0f, 0f, 960f, 178f, new Color(4, 9, 17, 145), new Color(4, 9, 17, 75), Color.Transparent, Color.Transparent);
            p.Rect(40f, 53f, 49f, 2f, new Color(234, 241, 252));
            // A subdued profile circle keeps the shell close to the PS5 layout.
            p.Disc(914f, 33f, 12f, 12f, new Color(131, 155, 182));
            p.Disc(914f, 29f, 3.8f, 3.8f, new Color(18, 35, 55));
            p.Disc(914f, 39f, 6f, 4f, new Color(18, 35, 55));
        }

        static Color Fade(Color color, float alpha) => new(color.R, color.G, color.B, (int)(color.A * Math.Clamp(alpha, 0f, 1f)));

        void DrawPongWallpaper(PSPConsolePainter p, float alpha) {
            if (alpha <= 0.005f) return;
            Color cyan = Fade(new Color(113, 206, 243, 130), alpha);
            p.Quad(new Vector2(510f, 217f), new Vector2(879f, 225f), new Vector2(1015f, 472f), new Vector2(395f, 447f),
                Fade(new Color(30, 92, 127, 145), alpha));
            for (int i = 0; i <= 8; i++) {
                float t = i / 8f;
                p.Line(510f + t * 369f, 217f + t * 8f, 395f + t * 620f, 447f + t * 25f, 1.3f, Fade(new Color(114, 176, 202, 65), alpha));
            }
            p.Line(510f, 217f, 879f, 225f, 3f, cyan);
            p.Line(879f, 225f, 1015f, 472f, 3f, cyan);
            p.Line(395f, 447f, 1015f, 472f, 3f, cyan);
            p.Line(510f, 217f, 395f, 447f, 3f, cyan);
            p.Line(457f, 322f, 941f, 338f, 2f, Fade(new Color(206, 230, 244, 130), alpha));
            p.RoundedRect(603f, 251f, 137f, 11f, 5f, Fade(new Color(252, 183, 112), alpha));
            p.RoundedRect(650f, 403f, 169f, 15f, 7f, Fade(new Color(132, 231, 251), alpha));
            for (int i = 6; i >= 1; i--) p.Disc(775f + i * 17f, 310f + i * 9f, 9f - i, 9f - i, Fade(new Color(178, 225, 254, 110 - i * 12), alpha));
            p.Disc(775f, 310f, 26f, 26f, Fade(new Color(154, 211, 255, 24), alpha));
            p.Disc(775f, 310f, 17f, 17f, Fade(new Color(174, 226, 254, 60), alpha));
            p.Disc(775f, 310f, 10f, 10f, Fade(Color.White, alpha));
        }

        void DrawBrickWallpaper(PSPConsolePainter p, float alpha) {
            if (alpha <= 0.005f) return;
            for (int row = 0; row < 4; row++) {
                for (int col = 0; col < 5; col++) {
                    float x = 490f + col * 80f + row * 19f, y = 221f + row * 47f - col * 9f;
                    Color front = Fade(BrickColors[row], alpha);
                    p.Quad(new Vector2(x, y), new Vector2(x + 66f, y - 8f), new Vector2(x + 66f, y + 24f), new Vector2(x, y + 32f), front);
                    p.Quad(new Vector2(x, y), new Vector2(x + 15f, y - 13f), new Vector2(x + 81f, y - 21f), new Vector2(x + 66f, y - 8f),
                        Fade(Color.Lerp(BrickColors[row], Color.White, 0.35f), alpha));
                    p.Quad(new Vector2(x + 66f, y - 8f), new Vector2(x + 81f, y - 21f), new Vector2(x + 81f, y + 11f), new Vector2(x + 66f, y + 24f),
                        Fade(Color.MultiplyColorOnly(BrickColors[row], 0.6f), alpha));
                }
            }
            p.RoundedRect(596f, 424f, 192f, 14f, 7f, Fade(new Color(156, 222, 255), alpha));
            p.Line(765f, 353f, 788f, 412f, 5f, Fade(new Color(210, 176, 245, 120), alpha));
            p.Disc(765f, 353f, 22f, 22f, Fade(new Color(225, 180, 250, 35), alpha));
            p.Disc(765f, 353f, 9f, 9f, Fade(Color.White, alpha));
        }

        void DrawTrophies(PSPConsolePainter p) {
            p.Rect(0f, 0f, 960f, 540f, new Color(5, 11, 21, 224));
            for (int i = 0; i < 3; i++) {
                bool earned = m_state.Progress.HasTrophy(m_state.SelectedGame * 3 + i);
                float y = 158f + i * 94f;
                p.Rect(40f, y, 880f, 80f, earned ? new Color(34, 44, 62) : new Color(20, 29, 43));
                Color cup = earned ? i == 0 ? new Color(212, 145, 93) : i == 1 ? new Color(194, 211, 229) : new Color(251, 209, 114) : new Color(82, 100, 124);
                p.Trophy(83f, y + 22f, 1.05f, cup);
            }
        }

        void DrawGame(PSPConsolePainter p) {
            PSPPaddleGame game = m_state.Game;
            bool pong = game.GameIndex == 0;
            p.Gradient(0f, 0f, 960f, 540f, new Color(10, 18, 34), new Color(17, 29, 47), new Color(7, 15, 27), new Color(9, 18, 34));
            p.Rect(28f, 63f, 904f, 2f, new Color(67, 91, 122));
            p.Rect(28f, 65f, 2f, 433f, new Color(48, 71, 98));
            p.Rect(930f, 65f, 2f, 433f, new Color(48, 71, 98));
            if (pong) {
                p.Gradient(31f, 67f, 898f, 432f, new Color(13, 47, 59), new Color(13, 47, 59), new Color(10, 35, 49), new Color(10, 35, 49));
                for (int i = 0; i < 25; i++) p.Rect(39f + i * 36f, game.IsOnline ? 269f : 279f, 19f, 2f, new Color(90, 148, 163, 155));
                p.Line(480f, 67f, 480f, 498f, 1f, new Color(51, 91, 109));
                p.RoundedRect(game.OpponentX - PSPPaddleGame.PaddleWidth / 2f, game.OpponentPaddleY,
                    PSPPaddleGame.PaddleWidth, PSPPaddleGame.PaddleHeight, 6f, new Color(248, 172, 104));
            }
            else {
                p.Rect(31f, 67f, 898f, 432f, new Color(18, 23, 43));
                foreach (PSPBrick brick in game.Bricks) {
                    if (brick.Destroyed) continue;
                    Color color = BrickColors[brick.Row];
                    p.Rect(brick.X, brick.Y + 4f, PSPBrick.Width, PSPBrick.Height, new Color(7, 13, 27));
                    p.Gradient(brick.X, brick.Y, PSPBrick.Width, PSPBrick.Height, Color.Lerp(color, Color.White, 0.18f),
                        Color.Lerp(color, Color.White, 0.18f), Color.MultiplyColorOnly(color, 0.78f), Color.MultiplyColorOnly(color, 0.78f));
                    p.Rect(brick.X + 4f, brick.Y + 3f, PSPBrick.Width - 8f, 2f, Color.Lerp(color, Color.White, 0.48f));
                }
            }
            p.RoundedRect(game.PlayerX - PSPPaddleGame.PaddleWidth / 2f - 3f, game.PlayerPaddleY - 3f,
                PSPPaddleGame.PaddleWidth + 6f, PSPPaddleGame.PaddleHeight + 6f, 8f, new Color(36, 85, 119));
            p.RoundedRect(game.PlayerX - PSPPaddleGame.PaddleWidth / 2f, game.PlayerPaddleY,
                PSPPaddleGame.PaddleWidth, PSPPaddleGame.PaddleHeight, 6f, new Color(138, 231, 250));
            if (game.Phase == PSPGamePhase.Playing) {
                for (int i = game.Trail.Length - 1; i > 0; i--) {
                    Vector2 trail = game.Trail[i];
                    float radius = MathF.Max(1.4f, 7f - i * 0.5f);
                    p.Disc(trail.X, trail.Y, radius, radius, new Color(125, 201, 238, 100 - i * 8));
                }
            }
            p.Disc(game.Ball.X, game.Ball.Y, 13f, 13f, new Color(154, 217, 250, 35));
            p.Disc(game.Ball.X, game.Ball.Y, PSPPaddleGame.BallRadius, PSPPaddleGame.BallRadius, Color.White);
            if (game.IsPaused || game.Phase == PSPGamePhase.Ready || game.Phase == PSPGamePhase.Finished
                || game is PSPOnlinePongGame online && online.IsSynchronizing) {
                p.Rect(238f, 225f, 484f, 130f, new Color(8, 15, 28, 240));
                p.Rect(238f, 225f, 3f, 130f, game.Won ? new Color(245, 207, 119) : new Color(141, 220, 247));
            }
        }
    }
}
