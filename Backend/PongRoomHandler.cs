using System;
using System.Linq;
using GameConsole.Networking;

namespace GameConsole.Backend {
    public sealed class PongRoomHandler : GameRoomHandler {
        readonly Guid[] m_players;
        readonly PongSimulation m_simulation = new();
        float m_snapshotTimer;
        bool m_ended;

        public PongRoomHandler(GameRoomContext context) : base(context) {
            RoomSnapshot room = context.Room;
            if (room.Capacity != 2 || room.Players.Length != 2 || !room.RequiresUdp)
                throw new ArgumentException("Online pong requires a two-player room with UDP enabled.");
            m_players = room.Players.OrderBy(player => player.Seat).Select(player => player.PlayerId).ToArray();
        }

        public override void OnStarted() => Context.SendReliable(PongProtocol.InitialState, PongProtocol.EncodeSnapshot(m_simulation.Snapshot()));

        public override void OnRealtime(Guid senderId, ushort opcode, uint sequence, byte[] payload) {
            if (opcode != PongProtocol.Input || !PongProtocol.TryDecodeInput(payload, out uint inputSequence, out short axis)) return;
            int player = Array.IndexOf(m_players, senderId);
            if (player >= 0) m_simulation.SetInput(player, inputSequence, PongProtocol.Axis(axis));
        }

        public override void Tick(float deltaTime) {
            if (m_ended) return;
            m_simulation.Update(deltaTime, () => {
                PongSnapshot state = m_simulation.Snapshot();
                ushort opcode = state.Phase == PongPhase.Finished ? PongProtocol.Result : PongProtocol.PhaseChanged;
                Context.SendReliable(opcode, PongProtocol.EncodeSnapshot(state));
            });
            if (m_simulation.Phase == PongPhase.Finished) {
                m_ended = true;
                Context.EndMatch("pong_finished");
                return;
            }
            m_snapshotTimer += deltaTime;
            if (m_snapshotTimer >= 1f / 30f) {
                m_snapshotTimer %= 1f / 30f;
                Context.SendRealtime(PongProtocol.Snapshot, PongProtocol.EncodeSnapshot(m_simulation.Snapshot()));
            }
        }
    }

    // Physics has no game-engine dependency and only runs on the server.
    internal sealed class PongSimulation {
        readonly Random m_random = new();
        readonly float[] m_inputs = new float[2];
        readonly uint[] m_receivedSequence = new uint[2], m_processedSequence = new uint[2];
        readonly long[] m_lastInputAt = new long[2];
        readonly bool[] m_hasInput = new bool[2];
        float m_accumulator, m_remaining = 3f, m_speed = 330f;
        float m_bottomX = PongProtocol.Width / 2f, m_topX = PongProtocol.Width / 2f;
        float m_ballX = PongProtocol.Width / 2f, m_ballY = PongProtocol.Height / 2f;
        float m_velocityX, m_velocityY;
        uint m_tick, m_round, m_rally;
        byte m_bottomScore, m_topScore;
        int m_serveDirection;
        public PongPhase Phase { get; private set; } = PongPhase.Countdown;

        public PongSimulation() { m_serveDirection = m_random.Next(2) == 0 ? -1 : 1; }

        public void SetInput(int player, uint sequence, float localAxis) {
            if (m_hasInput[player] && !RealtimePacketCodec.IsNewer(sequence, m_receivedSequence[player])) return;
            m_hasInput[player] = true;
            m_receivedSequence[player] = sequence;
            m_inputs[player] = player == 0 ? localAxis : -localAxis;
            m_lastInputAt[player] = Environment.TickCount64;
        }

        public void Update(float deltaTime, Action phaseChanged) {
            m_accumulator += Math.Clamp(deltaTime, 0f, 0.1f);
            while (m_accumulator >= PongProtocol.FrameDuration && Phase != PongPhase.Finished) {
                m_accumulator -= PongProtocol.FrameDuration;
                m_tick++;
                PongPhase previousPhase = Phase;
                for (int player = 0; player < 2; player++) {
                    if (Environment.TickCount64 - m_lastInputAt[player] > 250) m_inputs[player] = 0f;
                    m_processedSequence[player] = m_receivedSequence[player];
                }
                for (int substep = 0; substep < 4 && Phase != PongPhase.Finished; substep++) Step(PongProtocol.FrameDuration / 4f);
                if (previousPhase != Phase) phaseChanged();
            }
        }

        void Step(float dt) {
            m_bottomX = PongProtocol.MovePaddle(m_bottomX, m_inputs[0], dt);
            m_topX = PongProtocol.MovePaddle(m_topX, m_inputs[1], dt);
            if (Phase is PongPhase.Countdown or PongPhase.PointDelay) {
                m_remaining = MathF.Max(0f, m_remaining - dt);
                if (m_remaining <= 0f) Serve();
                return;
            }
            float previousX = m_ballX, previousY = m_ballY;
            m_ballX += m_velocityX * dt;
            m_ballY += m_velocityY * dt;
            float left = PongProtocol.WallLeft + PongProtocol.BallRadius, right = PongProtocol.WallRight - PongProtocol.BallRadius;
            if (m_ballX < left) { m_ballX = left; m_velocityX = MathF.Abs(m_velocityX); }
            else if (m_ballX > right) { m_ballX = right; m_velocityX = -MathF.Abs(m_velocityX); }
            float bottomContact = PongProtocol.BottomY - PongProtocol.BallRadius;
            float topContact = PongProtocol.TopY + PongProtocol.PaddleHeight + PongProtocol.BallRadius;
            if (m_velocityY > 0f && previousY <= bottomContact && m_ballY >= bottomContact) {
                float contactX = CrossingX(previousX, previousY, bottomContact);
                if (MathF.Abs(contactX - m_bottomX) <= PongProtocol.PaddleWidth / 2f + PongProtocol.BallRadius)
                    Bounce(m_bottomX, contactX, bottomContact, -1);
            }
            else if (m_velocityY < 0f && previousY >= topContact && m_ballY <= topContact) {
                float contactX = CrossingX(previousX, previousY, topContact);
                if (MathF.Abs(contactX - m_topX) <= PongProtocol.PaddleWidth / 2f + PongProtocol.BallRadius)
                    Bounce(m_topX, contactX, topContact, 1);
            }
            if (m_ballY < PongProtocol.TopGoal) Point(true);
            else if (m_ballY > PongProtocol.BottomGoal) Point(false);
        }

        float CrossingX(float x, float y, float contactY) => x + (m_ballX - x) * Math.Clamp((contactY - y) / (m_ballY - y), 0f, 1f);

        void Bounce(float paddleX, float contactX, float contactY, int direction) {
            float angle = Math.Clamp((contactX - paddleX) / (PongProtocol.PaddleWidth / 2f), -1f, 1f) * 1.02f;
            m_speed = MathF.Min(550f, m_speed + 9f);
            m_velocityX = MathF.Sin(angle) * m_speed;
            m_velocityY = direction * MathF.Cos(angle) * m_speed;
            m_ballX = contactX;
            m_ballY = contactY;
            m_rally++;
        }

        void Point(bool bottomWon) {
            if (bottomWon) m_bottomScore++; else m_topScore++;
            m_round++;
            m_rally = 0;
            m_ballX = PongProtocol.Width / 2f;
            m_ballY = PongProtocol.Height / 2f;
            m_velocityX = m_velocityY = 0f;
            m_speed = 330f;
            m_serveDirection = bottomWon ? 1 : -1;
            Phase = m_bottomScore >= PongProtocol.WinningScore || m_topScore >= PongProtocol.WinningScore ? PongPhase.Finished : PongPhase.PointDelay;
            m_remaining = Phase == PongPhase.Finished ? 0f : 1.2f;
        }

        void Serve() {
            float horizontal = (float)(m_random.NextDouble() * 0.7 - 0.35);
            m_velocityY = m_serveDirection * m_speed / MathF.Sqrt(1f + horizontal * horizontal);
            m_velocityX = horizontal * MathF.Abs(m_velocityY);
            Phase = PongPhase.Playing;
        }

        public PongSnapshot Snapshot() => new() {
            Tick = m_tick, Round = m_round, Phase = Phase, BottomScore = m_bottomScore, TopScore = m_topScore,
            BottomInputSequence = m_processedSequence[0], TopInputSequence = m_processedSequence[1],
            BottomX = m_bottomX, TopX = m_topX, BallX = m_ballX, BallY = m_ballY,
            VelocityX = m_velocityX, VelocityY = m_velocityY, RemainingSeconds = m_remaining, Rally = m_rally
        };
    }
}
