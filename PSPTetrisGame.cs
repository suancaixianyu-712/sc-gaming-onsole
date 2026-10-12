using System;
using Engine;
using Engine.Graphics;

namespace Game {
    /// <summary>俄罗斯方块（单人）。复用 PSPPaddleGame 的 Phase/IsPaused/Won 外壳，GameIndex = 2。</summary>
    public sealed class PSPTetrisGame : PSPPaddleGame {
        public const int Columns = 10, Rows = 20;
        // 7 种经典形状，每种 4 个旋转状态，每状态 4 格 (x, y)，4x4 盒内。
        static readonly Point2[][][] Shapes = BuildShapes();
        readonly System.Random m_random = new();
        readonly PSPConsoleProgress m_progress;
        readonly Action<int> m_unlock;
        float m_fallTimer, m_moveTimer, m_softTimer;
        int m_moveDirection;

        /// <summary>每格的方块种类：-1 为空，否则为白名单下标。</summary>
        public int[,] Cells { get; } = new int[Columns, Rows];
        public int PieceShape { get; private set; }
        public int PieceRotation { get; private set; }
        public int PieceX { get; private set; }
        public int PieceY { get; private set; }
        public int PieceMaterial { get; private set; }
        public int NextShape { get; private set; }
        public int NextMaterial { get; private set; }
        public int Points { get; private set; }
        public int Lines { get; private set; }
        public int Level => 1 + Lines / 10;
        public bool SoftDrop { get; set; }

        public PSPTetrisGame(PSPConsoleProgress progress, Action<int> unlock) : base(2, progress, unlock) {
            m_progress = progress;
            m_unlock = unlock;
            Reset();
        }

        public static Point2[] GetCells(int shape, int rotation) => Shapes[shape][rotation & 3];

        void Reset() {
            for (int x = 0; x < Columns; x++) for (int y = 0; y < Rows; y++) Cells[x, y] = -1;
            Points = Lines = 0;
            IsPaused = Won = false;
            m_fallTimer = m_moveTimer = m_softTimer = 0f;
            NextShape = m_random.Next(7);
            NextMaterial = m_random.Next(PSPTetrisBlocks.MaterialCount);
            Phase = PSPGamePhase.Playing;
            Spawn();
        }

        void Spawn() {
            PieceShape = NextShape;
            PieceMaterial = NextMaterial;
            NextShape = m_random.Next(7);
            NextMaterial = m_random.Next(PSPTetrisBlocks.MaterialCount);
            PieceRotation = 0;
            PieceX = 3;
            PieceY = 0;
            if (!Fits(PieceX, PieceY, PieceRotation)) Finish();
        }

        bool Fits(int px, int py, int rotation) {
            foreach (Point2 c in GetCells(PieceShape, rotation)) {
                int x = px + c.X, y = py + c.Y;
                if (x < 0 || x >= Columns || y >= Rows) return false;
                if (y >= 0 && Cells[x, y] >= 0) return false;
            }
            return true;
        }

        public int GhostY {
            get {
                int y = PieceY;
                while (Fits(PieceX, y + 1, PieceRotation)) y++;
                return y;
            }
        }

        public override void Confirm() {
            if (Phase == PSPGamePhase.Finished) Reset();
            else if (IsPaused) IsPaused = false;
            else HardDrop();
        }

        public override void TogglePause() {
            if (Phase != PSPGamePhase.Finished) IsPaused = !IsPaused;
        }

        public override void HandleButton(PSPConsoleControl control) {
            if (control == PSPConsoleControl.Start) { TogglePause(); return; }
            if (IsPaused || Phase != PSPGamePhase.Playing) return;
            switch (control) {
                case PSPConsoleControl.Up:
                case PSPConsoleControl.Circle:
                case PSPConsoleControl.Triangle: Rotate(); break;
            }
        }

        void Rotate() {
            if (Phase != PSPGamePhase.Playing || IsPaused) return;
            int rotation = (PieceRotation + 1) & 3;
            foreach (int kick in new[] { 0, -1, 1, -2, 2 }) {
                if (Fits(PieceX + kick, PieceY, rotation)) { PieceX += kick; PieceRotation = rotation; return; }
            }
        }

        void Shift(int direction) {
            if (Fits(PieceX + direction, PieceY, PieceRotation)) PieceX += direction;
        }

        void HardDrop() {
            int target = GhostY;
            Points += 2 * (target - PieceY);
            PieceY = target;
            Lock();
        }

        void StepDown(bool soft) {
            if (Fits(PieceX, PieceY + 1, PieceRotation)) {
                PieceY++;
                if (soft) Points++;
            }
            else Lock();
            m_fallTimer = 0f;
        }

        void Lock() {
            foreach (Point2 c in GetCells(PieceShape, PieceRotation)) {
                int x = PieceX + c.X, y = PieceY + c.Y;
                if (y < 0) { Finish(); return; }
                Cells[x, y] = PieceMaterial;
            }
            int cleared = 0;
            for (int y = Rows - 1; y >= 0; y--) {
                bool full = true;
                for (int x = 0; x < Columns && full; x++) full = Cells[x, y] >= 0;
                if (!full) continue;
                cleared++;
                for (int yy = y; yy > 0; yy--) for (int x = 0; x < Columns; x++) Cells[x, yy] = Cells[x, yy - 1];
                for (int x = 0; x < Columns; x++) Cells[x, 0] = -1;
                y++;
            }
            if (cleared > 0) {
                int level = Level;
                Points += (cleared switch { 1 => 100, 2 => 300, 3 => 500, _ => 800 }) * level;
                Lines += cleared;
                m_unlock(6);
                if (cleared >= 4) m_unlock(8);
            }
            SaveBest();
            Spawn();
        }

        void SaveBest() {
            m_progress.TetrisBestScore = Math.Max(m_progress.TetrisBestScore, Points);
            if (Points >= 10000) m_unlock(7);
        }

        void Finish() {
            Phase = PSPGamePhase.Finished;
            Won = false;
            SaveBest();
        }

        public override void Update(float deltaTime, float movement) {
            if (IsPaused || Phase != PSPGamePhase.Playing) return;
            float dt = Math.Clamp(deltaTime, 0f, 0.1f);
            int direction = movement > 0.4f ? 1 : movement < -0.4f ? -1 : 0;
            if (direction != m_moveDirection) {
                m_moveDirection = direction;
                if (direction != 0) { Shift(direction); m_moveTimer = 0.17f; }
            }
            else if (direction != 0) {
                m_moveTimer -= dt;
                if (m_moveTimer <= 0f) { Shift(direction); m_moveTimer = 0.05f; }
            }
            if (SoftDrop) {
                m_softTimer -= dt;
                if (m_softTimer <= 0f) { m_softTimer = 0.045f; StepDown(true); if (Phase != PSPGamePhase.Playing) return; }
            }
            else m_softTimer = 0.12f;
            float interval = MathF.Max(0.06f, 0.8f * MathF.Pow(0.85f, Level - 1));
            m_fallTimer += dt;
            if (m_fallTimer >= interval) StepDown(false);
        }

        static Point2[][][] BuildShapes() {
            // 基础形状（旋转 0），之后按 4x4 / 3x3 盒顺时针旋转生成。
            (int size, string[] rows)[] defs = [
                (4, ["....", "IIII", "....", "...."]),
                (2, ["OO", "OO"]),
                (3, [".T.", "TTT", "..."]),
                (3, [".SS", "SS.", "..."]),
                (3, ["ZZ.", ".ZZ", "..."]),
                (3, ["J..", "JJJ", "..."]),
                (3, ["..L", "LLL", "..."])
            ];
            Point2[][][] result = new Point2[7][][];
            for (int s = 0; s < 7; s++) {
                (int size, string[] rows) = defs[s];
                result[s] = new Point2[4][];
                for (int r = 0; r < 4; r++) {
                    var list = new System.Collections.Generic.List<Point2>();
                    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) {
                        if (rows[y][x] == '.') continue;
                        int rx = x, ry = y;
                        for (int k = 0; k < r; k++) (rx, ry) = (size - 1 - ry, rx);
                        list.Add(new Point2(rx + (size == 2 ? 1 : 0), ry));
                    }
                    result[s][r] = list.ToArray();
                }
            }
            return result;
        }
    }

    /// <summary>白名单原版方块，固定 data，保证场地与预览朝向一致。</summary>
    public static class PSPTetrisBlocks {
        public const int MaterialCount = 3;
        static int[] s_values;
        static int s_border;

        static void Ensure() {
            if (s_values != null) return;
            s_values = [
                Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<GraniteBlock>(), 0, 0),
                Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<FurnaceBlock>(), 0, 0),
                Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<OakWoodBlock>(), 0, 0)
            ];
            s_border = Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<DirtBlock>(), 0, 0);
        }

        public static int Material(int index) { Ensure(); return s_values[index]; }
        public static int Border { get { Ensure(); return s_border; } }
    }

    /// <summary>在一个 Widget 里循环 DrawBlock（UI 模式）绘制场地、边框与预览。放在场景背景之后（独立绘制层）。</summary>
    public sealed class PSPTetrisBoardWidget : Widget {
        // Cell 正方形；场地尽量铺满 540 高，左右只留窄侧栏。边框占 (Rows+1) 格高。
        public const float Cell = 24f, FieldX = 360f, FieldY = 18f, PreviewX = 660f, PreviewY = 88f;
        readonly PSPConsoleState m_state;
        readonly DrawBlockEnvironmentData m_environment = new() { DrawBlockMode = DrawBlockMode.UI, Light = 15 };

        public PSPTetrisBoardWidget(PSPConsoleState state) { m_state = state; IsHitTestVisible = false; }
        public override void MeasureOverride(Vector2 size) { DesiredSize = size; IsDrawRequired = true; }

        public override void Draw(DrawContext dc) {
            if (m_state.Page != PSPConsolePage.Game || m_state.Game is not PSPTetrisGame game) return;
            if (ActualSize.X <= 0f || ActualSize.Y <= 0f) return;
            Viewport viewport = Display.Viewport;
            Matrix view = Matrix.CreateLookAt(new Vector3(0f, 0f, 1f), Vector3.Zero, Vector3.UnitY);
            Matrix projection = Matrix.CreateOrthographic(960f, 540f, -1000f, 1000f);
            Matrix pixelsToClip = MatrixUtils.CreateScaleTranslation(0.5f * ActualSize.X, -0.5f * ActualSize.Y, ActualSize.X / 2f, ActualSize.Y / 2f)
                * GlobalTransform
                * MatrixUtils.CreateScaleTranslation(2f / viewport.Width, -2f / viewport.Height, -1f, 1f);
            m_environment.ViewProjectionMatrix = view * projection * pixelsToClip;
            PrimitivesRenderer3D r = dc.PrimitivesRenderer3D;
            Color tint = Color.White * GlobalColorTransform;
            int border = PSPTetrisBlocks.Border;
            for (int y = 0; y <= PSPTetrisGame.Rows; y++) {
                DrawCell(r, border, -1, y, tint);
                DrawCell(r, border, PSPTetrisGame.Columns, y, tint);
            }
            for (int x = 0; x < PSPTetrisGame.Columns; x++) DrawCell(r, border, x, PSPTetrisGame.Rows, tint);
            // 暂停/结束仍画场地，不缩小格子；仅压暗，提示框叠在场地中央。
            bool dim = game.IsPaused || game.Phase == PSPGamePhase.Finished;
            Color cellTint = dim ? Color.MultiplyColorOnly(tint, 0.45f) : tint;
            for (int x = 0; x < PSPTetrisGame.Columns; x++)
                for (int y = 0; y < PSPTetrisGame.Rows; y++)
                    if (game.Cells[x, y] >= 0) DrawCell(r, PSPTetrisBlocks.Material(game.Cells[x, y]), x, y, cellTint);
            int value = PSPTetrisBlocks.Material(game.PieceMaterial);
            int ghost = game.GhostY;
            if (!dim) {
                foreach (Point2 c in PSPTetrisGame.GetCells(game.PieceShape, game.PieceRotation)) {
                    if (ghost != game.PieceY && game.PieceY + c.Y >= 0) DrawCell(r, value, game.PieceX + c.X, ghost + c.Y, Color.MultiplyColorOnly(tint, 0.35f), 0.6f);
                }
                foreach (Point2 c in PSPTetrisGame.GetCells(game.PieceShape, game.PieceRotation))
                    if (game.PieceY + c.Y >= 0) DrawCell(r, value, game.PieceX + c.X, game.PieceY + c.Y, tint);
            }
            else {
                foreach (Point2 c in PSPTetrisGame.GetCells(game.PieceShape, game.PieceRotation))
                    if (game.PieceY + c.Y >= 0) DrawCell(r, value, game.PieceX + c.X, game.PieceY + c.Y, cellTint);
            }
            int next = PSPTetrisBlocks.Material(game.NextMaterial);
            foreach (Point2 c in PSPTetrisGame.GetCells(game.NextShape, 0))
                DrawAt(r, next, PreviewX + c.X * Cell, PreviewY + c.Y * Cell, tint, 1f);
        }

        void DrawCell(PrimitivesRenderer3D r, int value, int x, int y, Color tint, float scale = 1f)
            => DrawAt(r, value, FieldX + x * Cell, FieldY + y * Cell, tint, scale);

        void DrawAt(PrimitivesRenderer3D r, int value, float px, float py, Color tint, float scale) {
            Matrix matrix = Matrix.CreateTranslation(px + Cell / 2f - 480f, 270f - (py + Cell / 2f), 0f);
            Block block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
            block.DrawBlock(r, value, tint, Cell * scale, ref matrix, m_environment);
        }
    }
}
