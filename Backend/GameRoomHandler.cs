using System;
using GameConsole.Networking;

namespace GameConsole.Backend {
    // One instance per match. All callbacks and context methods run on the server loop.
    // A registered game consumes incoming messages and publishes only its validated results.
    public abstract class GameRoomHandler {
        protected GameRoomContext Context { get; }
        protected GameRoomHandler(GameRoomContext context) => Context = context;
        public virtual void OnStarted() { }
        public virtual void OnEnded(string reason) { }
        public virtual void OnReliable(Guid senderId, ushort opcode, byte[] payload) { }
        public virtual void OnRealtime(Guid senderId, ushort opcode, uint sequence, byte[] payload) { }
        public virtual void Tick(float deltaTime) { }
    }

    public sealed class GameRoomContext {
        readonly Func<RoomSnapshot> m_snapshot;
        readonly Action<ushort, byte[], Guid> m_reliable;
        readonly Action<ushort, byte[], Guid> m_realtime;
        readonly Action<string> m_end;
        public RoomSnapshot Room => m_snapshot();
        internal GameRoomContext(Func<RoomSnapshot> snapshot, Action<ushort, byte[], Guid> reliable,
            Action<ushort, byte[], Guid> realtime, Action<string> end) {
            m_snapshot = snapshot;
            m_reliable = reliable;
            m_realtime = realtime;
            m_end = end;
        }
        // Guid.Empty broadcasts; a player ID sends privately (for example, that player's own cards).
        public void SendReliable(ushort opcode, byte[] payload, Guid targetPlayerId = default) => m_reliable(opcode, payload, targetPlayerId);
        public void SendRealtime(ushort opcode, byte[] payload, Guid targetPlayerId = default) => m_realtime(opcode, payload, targetPlayerId);
        public void EndMatch(string reason = "game_finished") => m_end(reason);
    }
}
