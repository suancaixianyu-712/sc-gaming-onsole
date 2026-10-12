using System;
using Engine;

namespace Game {
    public enum PSPGamePhase { Ready, Playing, RoundDelay, Finished }

    public sealed class PSPBrick {
        public float X, Y;
        public int Row;
        public bool Destroyed;
        public const float Width = 103f;
        public const float Height = 24f;
    }

    public class PSPPaddleGame {
        public const float Width = 960f;
        public const float Height = 540f;
        public const float BallRadius = 8f;
        public const float PaddleWidth = 140f;
        public const float PaddleHeight = 12f;
        public const float PlayerY = 476f;
        public const float OpponentY = 91f;
        public const int WinningScore = 5;
        const float LeftWall = 28f;
        const float RightWall = Width - LeftWall;
        const float StepDuration = 1f / 240f;
        readonly System.Random m_random = new();
        readonly PSPConsoleProgress m_progress;
        readonly Action<int> m_unlock;
        float m_accumulator, m_roundDelay, m_aiReaction, m_aiTarget = Width / 2f;
        float m_ballSpeed = 330f, m_trailTimer;
        int m_serveDirection = -1;

        public int GameIndex { get; }
        public float PlayerX { get; protected set; } = Width / 2f;
        public float OpponentX { get; protected set; } = Width / 2f;
        public Vector2 Ball { get; protected set; }
        public Vector2 Velocity { get; protected set; }
        public Vector2[] Trail { get; } = new Vector2[10];
        public PSPBrick[] Bricks { get; private set; } = [];
        public PSPGamePhase Phase { get; protected set; }
        public bool IsPaused { get; protected set; }
        public bool Won { get; protected set; }
        public int PlayerScore { get; protected set; }
        public int OpponentScore { get; protected set; }
        public int Score { get; private set; }
        public int Lives { get; private set; } = 3;
        public int RemainingBricks { get; private set; }
        public int Rally { get; protected set; }
        public virtual bool IsOnline => false;
        public virtual float PlayerPaddleY => PlayerY;
        public virtual float OpponentPaddleY => OpponentY;

        public PSPPaddleGame(int gameIndex, PSPConsoleProgress progress, Action<int> unlock) {
            GameIndex = gameIndex;
            m_progress = progress;
            m_unlock = unlock;
            Restart();
        }

        public void Restart() {
            PlayerX = OpponentX = m_aiTarget = Width / 2f;
            PlayerScore = OpponentScore = Score = Rally = 0;
            Lives = 3;
            Won = IsPaused = false;
            m_accumulator = m_roundDelay = m_aiReaction = m_trailTimer = 0f;
            m_ballSpeed = GameIndex == 0 ? 330f : 300f;
            m_serveDirection = -1;
            Phase = PSPGamePhase.Ready;
            if (GameIndex == 1) {
                Bricks = new PSPBrick[40];
                for (int row = 0; row < 5; row++) {
                    for (int column = 0; column < 8; column++) {
                        Bricks[row * 8 + column] = new PSPBrick { X = 40f + column * 111f, Y = 98f + row * 32f, Row = row };
                    }
                }
                RemainingBricks = Bricks.Length;
            }
            else {
                Bricks = [];
                RemainingBricks = 0;
            }
            PlaceServeBall();
        }

        public virtual void Confirm() {
            if (Phase == PSPGamePhase.Finished) Restart();
            else if (IsPaused) IsPaused = false;
            else if (Phase == PSPGamePhase.Ready) Serve();
        }

        public virtual void TogglePause() {
            if (Phase != PSPGamePhase.Finished) IsPaused = !IsPaused;
        }

        public virtual void HandleButton(PSPConsoleControl control) {
            // Square and Triangle are reserved for each game's own actions.
            if (control == PSPConsoleControl.Cross) Confirm();
            else if (control == PSPConsoleControl.Start) TogglePause();
        }

        void PlaceServeBall() {
            Ball = GameIndex == 1 ? new Vector2(PlayerX, PlayerY - BallRadius - 4f) : new Vector2(Width / 2f, 286f);
            Velocity = Vector2.Zero;
            Array.Fill(Trail, Ball);
        }

        void Serve() {
            Phase = PSPGamePhase.Playing;
            float horizontal = (float)(m_random.NextDouble() * 0.7 - 0.35);
            Velocity = new Vector2(horizontal, m_serveDirection) * (m_ballSpeed / MathF.Sqrt(1f + horizontal * horizontal));
            m_aiReaction = 0f;
        }

        public virtual void Update(float deltaTime, float movement) {
            if (IsPaused || Phase == PSPGamePhase.Finished) {
                m_accumulator = 0f;
                return;
            }
            m_accumulator += Math.Clamp(deltaTime, 0f, 0.1f);
            while (m_accumulator >= StepDuration) {
                Step(StepDuration, Math.Clamp(movement, -1f, 1f));
                m_accumulator -= StepDuration;
                if (Phase == PSPGamePhase.Finished) {
                    m_accumulator = 0f;
                    break;
                }
            }
        }

        void Step(float dt, float movement) {
            PlayerX = Math.Clamp(PlayerX + movement * 640f * dt, LeftWall + PaddleWidth / 2f, RightWall - PaddleWidth / 2f);
            if (Phase == PSPGamePhase.Ready) {
                PlaceServeBall();
                return;
            }
            if (Phase == PSPGamePhase.RoundDelay) {
                m_roundDelay -= dt;
                if (m_roundDelay <= 0f) Serve();
                return;
            }
            if (GameIndex == 0) UpdateOpponent(dt);
            Vector2 previous = Ball;
            Ball += Velocity * dt;
            if (Ball.X < LeftWall + BallRadius) {
                Ball = new Vector2(LeftWall + BallRadius, Ball.Y);
                Velocity = new Vector2(MathF.Abs(Velocity.X), Velocity.Y);
            }
            else if (Ball.X > RightWall - BallRadius) {
                Ball = new Vector2(RightWall - BallRadius, Ball.Y);
                Velocity = new Vector2(-MathF.Abs(Velocity.X), Velocity.Y);
            }
            float playerContact = PlayerY - BallRadius;
            if (Velocity.Y > 0f && previous.Y <= playerContact && Ball.Y >= playerContact) {
                float x = CrossingX(previous, playerContact);
                if (MathF.Abs(x - PlayerX) <= PaddleWidth / 2f + BallRadius) {
                    BouncePaddle(PlayerX, playerContact, -1, x);
                    Rally++;
                    if (GameIndex == 0) m_progress.PongBestRally = Math.Max(m_progress.PongBestRally, Rally);
                }
            }
            if (GameIndex == 0) {
                float opponentContact = OpponentY + PaddleHeight + BallRadius;
                if (Velocity.Y < 0f && previous.Y >= opponentContact && Ball.Y <= opponentContact) {
                    float x = CrossingX(previous, opponentContact);
                    if (MathF.Abs(x - OpponentX) <= PaddleWidth / 2f + BallRadius) BouncePaddle(OpponentX, opponentContact, 1, x);
                }
                if (Ball.Y < 58f) ScorePoint(true);
                else if (Ball.Y > Height + BallRadius) ScorePoint(false);
            }
            else {
                if (Ball.Y < 66f + BallRadius) {
                    Ball = new Vector2(Ball.X, 66f + BallRadius);
                    Velocity = new Vector2(Velocity.X, MathF.Abs(Velocity.Y));
                }
                CollideBricks(previous);
                if (Ball.Y > Height + BallRadius) {
                    Lives--;
                    if (Lives <= 0) Finish(false);
                    else {
                        Phase = PSPGamePhase.Ready;
                        PlaceServeBall();
                    }
                }
            }
            m_trailTimer += dt;
            if (m_trailTimer >= 0.018f) {
                m_trailTimer = 0f;
                for (int i = Trail.Length - 1; i > 0; i--) Trail[i] = Trail[i - 1];
                Trail[0] = Ball;
            }
        }

        float CrossingX(Vector2 previous, float contactY) {
            float fraction = Math.Clamp((contactY - previous.Y) / (Ball.Y - previous.Y), 0f, 1f);
            return previous.X + (Ball.X - previous.X) * fraction;
        }

        void BouncePaddle(float paddleX, float y, int verticalDirection, float contactX) {
            float hit = Math.Clamp((contactX - paddleX) / (PaddleWidth / 2f), -1f, 1f);
            float angle = hit * 1.02f;
            m_ballSpeed = MathF.Min(m_ballSpeed + 9f, GameIndex == 0 ? 550f : 470f);
            Velocity = new Vector2(MathF.Sin(angle), verticalDirection * MathF.Cos(angle)) * m_ballSpeed;
            Ball = new Vector2(contactX, y);
        }

        void UpdateOpponent(float dt) {
            m_aiReaction -= dt;
            if (m_aiReaction <= 0f) {
                m_aiReaction = 0.16f;
                if (Velocity.Y < -1f) {
                    float seconds = MathF.Max(0f, (OpponentY + PaddleHeight + BallRadius - Ball.Y) / Velocity.Y);
                    float prediction = Ball.X + Velocity.X * seconds;
                    float min = LeftWall + BallRadius;
                    float span = RightWall - BallRadius - min;
                    float folded = (prediction - min) % (2f * span);
                    if (folded < 0f) folded += 2f * span;
                    prediction = min + (folded <= span ? folded : 2f * span - folded);
                    m_aiTarget = prediction + (float)(m_random.NextDouble() * 76.0 - 38.0);
                }
                else m_aiTarget = Width / 2f;
                m_aiTarget = Math.Clamp(m_aiTarget, LeftWall + PaddleWidth / 2f, RightWall - PaddleWidth / 2f);
            }
            OpponentX += Math.Clamp(m_aiTarget - OpponentX, -320f * dt, 320f * dt);
        }

        void ScorePoint(bool playerWonPoint) {
            if (playerWonPoint) {
                PlayerScore++;
                m_unlock(0);
            }
            else OpponentScore++;
            if (PlayerScore >= WinningScore || OpponentScore >= WinningScore) {
                Finish(PlayerScore >= WinningScore);
                return;
            }
            Rally = 0;
            m_ballSpeed = 330f;
            m_serveDirection = playerWonPoint ? -1 : 1;
            Phase = PSPGamePhase.RoundDelay;
            m_roundDelay = 0.9f;
            PlaceServeBall();
        }

        void CollideBricks(Vector2 previous) {
            foreach (PSPBrick brick in Bricks) {
                if (brick.Destroyed) continue;
                float nearestX = Math.Clamp(Ball.X, brick.X, brick.X + PSPBrick.Width);
                float nearestY = Math.Clamp(Ball.Y, brick.Y, brick.Y + PSPBrick.Height);
                if (Vector2.DistanceSquared(Ball, new Vector2(nearestX, nearestY)) > BallRadius * BallRadius) continue;
                bool vertical = previous.Y + BallRadius <= brick.Y || previous.Y - BallRadius >= brick.Y + PSPBrick.Height;
                bool horizontal = previous.X + BallRadius <= brick.X || previous.X - BallRadius >= brick.X + PSPBrick.Width;
                if (!vertical && !horizontal) {
                    float penetrationX = MathF.Min(Ball.X + BallRadius - brick.X, brick.X + PSPBrick.Width - Ball.X + BallRadius);
                    float penetrationY = MathF.Min(Ball.Y + BallRadius - brick.Y, brick.Y + PSPBrick.Height - Ball.Y + BallRadius);
                    vertical = penetrationY <= penetrationX;
                }
                if (vertical) {
                    Ball = new Vector2(Ball.X, Velocity.Y > 0f ? brick.Y - BallRadius : brick.Y + PSPBrick.Height + BallRadius);
                    Velocity = new Vector2(Velocity.X, -Velocity.Y);
                }
                else {
                    Ball = new Vector2(Velocity.X > 0f ? brick.X - BallRadius : brick.X + PSPBrick.Width + BallRadius, Ball.Y);
                    Velocity = new Vector2(-Velocity.X, Velocity.Y);
                }
                brick.Destroyed = true;
                RemainingBricks--;
                Score += (5 - brick.Row) * 10;
                m_progress.BreakoutBestScore = Math.Max(m_progress.BreakoutBestScore, Score);
                m_unlock(3);
                if (Score >= 500) m_unlock(4);
                if (RemainingBricks == 0) Finish(true);
                break;
            }
        }

        void Finish(bool won) {
            Phase = PSPGamePhase.Finished;
            Won = won;
            Velocity = Vector2.Zero;
            if (won && GameIndex == 0) {
                m_progress.PongWins++;
                m_unlock(1);
                if (OpponentScore == 0) m_unlock(2);
            }
            else if (won) m_unlock(5);
        }
    }
}
