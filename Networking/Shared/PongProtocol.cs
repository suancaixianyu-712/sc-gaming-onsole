using System;
using System.Buffers.Binary;

namespace GameConsole.Networking {
    public enum PongPhase : byte { Countdown, Playing, PointDelay, Finished }

    public static class PongProtocol {
        public const string GameId = "pong-online";
        public const byte Version = 1;
        public const ushort Input = 1, Snapshot = 2, InitialState = 100, PhaseChanged = 101, Result = 102;
        public const int SimulationRate = 60;
        public const float FrameDuration = 1f / SimulationRate;
        public const float Width = 960f, Height = 540f;
        public const float WallLeft = 28f, WallRight = 932f;
        public const float PaddleWidth = 140f, PaddleHeight = 12f, PaddleSpeed = 640f;
        // Symmetrical court allows player two to view the match rotated by 180 degrees.
        public const float BottomY = 437f, TopY = 91f, BallRadius = 8f;
        public const float TopGoal = 58f, BottomGoal = Height - TopGoal;
        public const int WinningScore = 5;
        public const float InterpolationSeconds = 0.075f;
        public const float MaxExtrapolationSeconds = 0.05f;
        public const int SnapshotSize = 52;

        public static short QuantizeAxis(float value) => (short)MathF.Round(Math.Clamp(value, -1f, 1f) * 32767f);
        public static float Axis(short value) => value / 32767f;
        public static float MovePaddle(float x, float axis, float dt) => Math.Clamp(x + axis * PaddleSpeed * dt,
            WallLeft + PaddleWidth / 2f, WallRight - PaddleWidth / 2f);

        public static byte[] EncodeInput(uint sequence, short axis) {
            byte[] data = new byte[7];
            data[0] = Version;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(1), sequence);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(5), axis);
            return data;
        }

        public static bool TryDecodeInput(ReadOnlySpan<byte> data, out uint sequence, out short axis) {
            sequence = 0;
            axis = 0;
            if (data.Length != 7 || data[0] != Version) return false;
            sequence = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(1));
            axis = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(5));
            return axis != short.MinValue;
        }

        public static byte[] EncodeSnapshot(PongSnapshot state) {
            byte[] data = new byte[SnapshotSize];
            data[0] = Version;
            data[1] = (byte)state.Phase;
            data[2] = state.BottomScore;
            data[3] = state.TopScore;
            WriteUInt(data, 4, state.Tick);
            WriteUInt(data, 8, state.Round);
            WriteUInt(data, 12, state.BottomInputSequence);
            WriteUInt(data, 16, state.TopInputSequence);
            WriteFloat(data, 20, state.BottomX);
            WriteFloat(data, 24, state.TopX);
            WriteFloat(data, 28, state.BallX);
            WriteFloat(data, 32, state.BallY);
            WriteFloat(data, 36, state.VelocityX);
            WriteFloat(data, 40, state.VelocityY);
            WriteFloat(data, 44, state.RemainingSeconds);
            WriteUInt(data, 48, state.Rally);
            return data;
        }

        public static bool TryDecodeSnapshot(ReadOnlySpan<byte> data, out PongSnapshot state) {
            state = null;
            if (data.Length != SnapshotSize || data[0] != Version || data[1] > (byte)PongPhase.Finished
                || data[2] > WinningScore || data[3] > WinningScore) return false;
            PongSnapshot value = new() {
                Phase = (PongPhase)data[1], BottomScore = data[2], TopScore = data[3],
                Tick = ReadUInt(data, 4), Round = ReadUInt(data, 8),
                BottomInputSequence = ReadUInt(data, 12), TopInputSequence = ReadUInt(data, 16),
                BottomX = ReadFloat(data, 20), TopX = ReadFloat(data, 24),
                BallX = ReadFloat(data, 28), BallY = ReadFloat(data, 32),
                VelocityX = ReadFloat(data, 36), VelocityY = ReadFloat(data, 40),
                RemainingSeconds = ReadFloat(data, 44), Rally = ReadUInt(data, 48)
            };
            if (!float.IsFinite(value.BottomX) || !float.IsFinite(value.TopX) || !float.IsFinite(value.BallX)
                || !float.IsFinite(value.BallY) || !float.IsFinite(value.VelocityX) || !float.IsFinite(value.VelocityY)
                || !float.IsFinite(value.RemainingSeconds) || value.RemainingSeconds is < 0f or > 10f
                || value.BottomX < WallLeft + PaddleWidth / 2f || value.BottomX > WallRight - PaddleWidth / 2f
                || value.TopX < WallLeft + PaddleWidth / 2f || value.TopX > WallRight - PaddleWidth / 2f
                || value.BallX is < WallLeft or > WallRight || value.BallY is < 0f or > Height
                || MathF.Abs(value.VelocityX) > 600f || MathF.Abs(value.VelocityY) > 600f) return false;
            state = value;
            return true;
        }

        static void WriteUInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        static void WriteFloat(byte[] bytes, int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
        static uint ReadUInt(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset));
        static float ReadFloat(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset));
    }

    public sealed class PongSnapshot {
        public uint Tick, Round, BottomInputSequence, TopInputSequence, Rally;
        public PongPhase Phase;
        public byte BottomScore, TopScore;
        public float BottomX, TopX, BallX, BallY, VelocityX, VelocityY, RemainingSeconds;
    }
}
