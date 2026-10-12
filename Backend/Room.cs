using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using GameConsole.Networking;
using LiteNetLib;

namespace GameConsole.Backend {
    internal sealed class ClientSession {
        public Guid PlayerId { get; } = Guid.NewGuid();
        public string Token { get; set; } = "";
        public string Name { get; set; } = "";
        public TcpClient Socket { get; init; }
        public TcpMessageConnection Connection { get; set; }
        public long ConnectedAt { get; } = Environment.TickCount64;
        public long LastSeen { get; set; } = Environment.TickCount64;
        public bool Authenticated { get; set; }
        public NetPeer Peer { get; set; }
        public Room Room { get; set; }
        public bool Ready { get; set; }
        public int Seat { get; set; }
        public bool HasRealtimeSequence { get; set; }
        public uint RealtimeSequence { get; set; }
        long m_rateWindow = Environment.TickCount64;
        int m_tcpCount, m_udpCount;

        public bool AllowPacket(bool udp, int limit) {
            long now = Environment.TickCount64;
            if (now - m_rateWindow >= 1000) { m_rateWindow = now; m_tcpCount = m_udpCount = 0; }
            return (udp ? ++m_udpCount : ++m_tcpCount) <= limit;
        }
    }

    internal sealed class Room {
        public string Id { get; init; }
        public string Name { get; init; }
        public string GameId { get; init; }
        public int Capacity { get; init; }
        public bool RequiresUdp { get; init; }
        public Guid HostId { get; set; }
        public Guid MatchId { get; set; }
        public long Revision { get; set; }
        public long MessageSequence { get; set; }
        public uint ServerRealtimeSequence { get; set; }
        public GameRoomHandler Handler { get; set; }
        public List<ClientSession> Players { get; } = new();
        public bool Started => MatchId != Guid.Empty;

        public RoomSnapshot Snapshot() => new() {
            RoomId = Id, Name = Name, GameId = GameId, Capacity = Capacity, RequiresUdp = RequiresUdp,
            HostPlayerId = HostId, Started = Started, MatchId = MatchId, Revision = Revision,
            Players = Players.Select(player => new PlayerSummary {
                PlayerId = player.PlayerId, Name = player.Name, Seat = player.Seat,
                Ready = player.Ready, UdpConnected = player.Peer?.ConnectionState == ConnectionState.Connected
            }).ToArray()
        };

        public RoomSummary Summary() => new() {
            RoomId = Id, Name = Name, GameId = GameId, Capacity = Capacity,
            PlayerCount = Players.Count, RequiresUdp = RequiresUdp, Started = Started
        };
    }
}
