using System;
using System.Collections.Generic;
using System.Linq;
using Engine;
using GameConsole.Networking;

namespace Game {
    public sealed class PSPOnlinePongGame : PSPPaddleGame {
        readonly record struct PredictedInput(uint Sequence, float Axis);
        readonly GameConsoleClient m_network;
        readonly Action m_returnToRoom;
        readonly Action<string> m_notice;
        readonly int m_seat;
        readonly List<PredictedInput> m_pending = new();
        readonly List<PongSnapshot> m_snapshots = new();
        PongSnapshot m_latest;
        float m_inputAccumulator, m_predictedX = Width / 2f, m_visualOffset, m_trailAccumulator;
        uint m_inputSequence;
        double m_latestArrival;
        double m_renderTick;

        public Guid MatchId { get; }
        public string OpponentName { get; }
        public override bool IsOnline => true;
        public override float PlayerPaddleY => PongProtocol.BottomY;
        public override float OpponentPaddleY => PongProtocol.TopY;
        public bool IsSynchronizing => Phase != PSPGamePhase.Finished
            && (m_latest == null || Now - m_latestArrival > 0.5 || m_pending.Count > 30 || !m_network.IsRealtimeConnected);
        public string Status => $"{OpponentName} · {m_network.RealtimeLatencyMilliseconds} ms";
        public string OverlayTitle => Phase == PSPGamePhase.Finished ? Won ? "胜利！" : "本局落败"
            : IsSynchronizing ? "等待同步" : $"{Math.Max(1, (int)Math.Ceiling(m_latest.RemainingSeconds))}";
        public string OverlayHint => Phase == PSPGamePhase.Finished ? "X / O 返回房间，双方可重新准备"
            : IsSynchronizing ? "正在等待服务器 · O 离开对局" : "双方已准备 · 倒计时结束自动发球";
        static double Now => Environment.TickCount64 / 1000d;

        public PSPOnlinePongGame(GameConsoleClient network, RoomSnapshot room, Action returnToRoom, Action<string> notice)
            : base(0, new PSPConsoleProgress(), _ => { }) {
            m_network = network;
            m_returnToRoom = returnToRoom;
            m_notice = notice;
            MatchId = room.MatchId;
            PlayerSummary[] players = room.Players.OrderBy(player => player.Seat).ToArray();
            m_seat = Array.FindIndex(players, player => player.PlayerId == network.PlayerId);
            if (players.Length != 2 || m_seat < 0) throw new InvalidOperationException("Invalid pong room.");
            OpponentName = players[1 - m_seat].Name;
            Ball = new Vector2(Width / 2f, Height / 2f);
            Array.Fill(Trail, Ball);
        }

        public void Receive(PongSnapshot state) {
            if (m_latest != null && !RealtimePacketCodec.IsNewer(state.Tick, m_latest.Tick)) return;
            bool discontinuity = m_latest == null || state.Round != m_latest.Round || state.Phase != m_latest.Phase;
            float previousVisual = PlayerX;
            uint acknowledged = m_seat == 0 ? state.BottomInputSequence : state.TopInputSequence;
            m_pending.RemoveAll(input => !RealtimePacketCodec.IsNewer(input.Sequence, acknowledged));
            m_predictedX = ViewX(m_seat == 0 ? state.BottomX : state.TopX);
            if (state.Phase == PongPhase.Finished) m_pending.Clear();
            else foreach (PredictedInput input in m_pending) m_predictedX = PongProtocol.MovePaddle(m_predictedX, input.Axis, PongProtocol.FrameDuration);
            float correction = previousVisual - m_predictedX;
            m_visualOffset = MathF.Abs(correction) > 32f || m_latest == null || state.Phase == PongPhase.Finished ? 0f : correction;
            m_latest = state;
            m_latestArrival = Now;
            PlayerScore = m_seat == 0 ? state.BottomScore : state.TopScore;
            OpponentScore = m_seat == 0 ? state.TopScore : state.BottomScore;
            Rally = (int)state.Rally;
            Phase = state.Phase switch {
                PongPhase.Countdown => PSPGamePhase.Ready,
                PongPhase.PointDelay => PSPGamePhase.RoundDelay,
                PongPhase.Finished => PSPGamePhase.Finished,
                _ => PSPGamePhase.Playing
            };
            Won = Phase == PSPGamePhase.Finished && PlayerScore >= PongProtocol.WinningScore;
            if (discontinuity) {
                m_snapshots.Clear();
                m_renderTick = state.Tick - PongProtocol.InterpolationSeconds * PongProtocol.SimulationRate;
                Ball = ViewBall(state.BallX, state.BallY);
                Array.Fill(Trail, Ball);
            }
            m_snapshots.Add(state);
            if (m_snapshots.Count > 32) m_snapshots.RemoveAt(0);
            OpponentX = ViewX(m_seat == 0 ? state.TopX : state.BottomX);
            PlayerX = Math.Clamp(m_predictedX + m_visualOffset, 98f, 862f);
        }

        float ViewX(float x) => m_seat == 0 ? x : Width - x;
        Vector2 ViewBall(float x, float y) => m_seat == 0 ? new Vector2(x, y) : new Vector2(Width - x, Height - y);

        public override void Confirm() { if (Phase == PSPGamePhase.Finished) m_returnToRoom(); }
        public override void TogglePause() => m_notice("联机对战持续进行，不能单人暂停。");
        public override void HandleButton(PSPConsoleControl control) {
            if (control == PSPConsoleControl.Cross) Confirm();
            else if (control == PSPConsoleControl.Start) TogglePause();
        }

        public override void Update(float deltaTime, float movement) {
            float dt = Math.Clamp(deltaTime, 0f, 0.1f);
            if (Phase != PSPGamePhase.Finished) {
                m_inputAccumulator += dt;
                short axis = PongProtocol.QuantizeAxis(movement);
                float quantized = PongProtocol.Axis(axis);
                while (m_inputAccumulator >= PongProtocol.FrameDuration) {
                    m_inputAccumulator -= PongProtocol.FrameDuration;
                    uint sequence = ++m_inputSequence;
                    m_pending.Add(new PredictedInput(sequence, quantized));
                    if (m_pending.Count > 180) m_pending.RemoveAt(0);
                    m_predictedX = PongProtocol.MovePaddle(m_predictedX, quantized, PongProtocol.FrameDuration);
                    m_network.SendRealtime(PongProtocol.Input, PongProtocol.EncodeInput(sequence, axis));
                }
            }
            m_visualOffset *= MathF.Exp(-20f * dt);
            PlayerX = Math.Clamp(m_predictedX + m_visualOffset, 98f, 862f);
            UpdateRemoteView();
            m_trailAccumulator += dt;
            if (m_trailAccumulator >= 0.018f) {
                m_trailAccumulator %= 0.018f;
                for (int i = Trail.Length - 1; i > 0; i--) Trail[i] = Trail[i - 1];
                Trail[0] = Ball;
            }
        }

        void UpdateRemoteView() {
            if (m_latest == null) return;
            double targetTick = m_latest.Tick + ((Now - m_latestArrival) - PongProtocol.InterpolationSeconds) * PongProtocol.SimulationRate;
            // Keep the display clock monotonic, but stop it when the extrapolation budget runs out.
            targetTick = Math.Min(Math.Max(m_renderTick, targetTick),
                m_latest.Tick + PongProtocol.MaxExtrapolationSeconds * PongProtocol.SimulationRate);
            m_renderTick = targetTick;
            PongSnapshot before = m_snapshots[0], after = before;
            foreach (PongSnapshot snapshot in m_snapshots) {
                after = snapshot;
                if (snapshot.Tick >= targetTick) break;
                before = snapshot;
            }
            float fraction = after.Tick == before.Tick ? 0f : Math.Clamp((float)((targetTick - before.Tick) / (after.Tick - before.Tick)), 0f, 1f);
            float x = before.BallX + (after.BallX - before.BallX) * fraction;
            float y = before.BallY + (after.BallY - before.BallY) * fraction;
            float opponent = m_seat == 0 ? before.TopX + (after.TopX - before.TopX) * fraction
                : before.BottomX + (after.BottomX - before.BottomX) * fraction;
            if (targetTick > m_latest.Tick) {
                float extra = Math.Min((float)((targetTick - m_latest.Tick) / PongProtocol.SimulationRate), PongProtocol.MaxExtrapolationSeconds);
                x = m_latest.BallX + m_latest.VelocityX * extra;
                y = Math.Clamp(m_latest.BallY + m_latest.VelocityY * extra, PongProtocol.TopGoal, PongProtocol.BottomGoal);
                float left = PongProtocol.WallLeft + PongProtocol.BallRadius, right = PongProtocol.WallRight - PongProtocol.BallRadius;
                if (x < left) x = left + (left - x);
                else if (x > right) x = right - (x - right);
            }
            OpponentX = ViewX(opponent);
            Ball = ViewBall(x, y);
            Velocity = m_seat == 0 ? new Vector2(m_latest.VelocityX, m_latest.VelocityY) : -new Vector2(m_latest.VelocityX, m_latest.VelocityY);
        }
    }
}
