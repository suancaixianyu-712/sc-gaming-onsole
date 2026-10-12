using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameConsole.Networking {
    public static class ConsoleProtocol {
        public const int Version = 1;
        public const string ApplicationKey = "codex.GameConsole/v1";
        public const int DefaultTcpPort = 28886;
        public const int DefaultUdpPort = 28887;
        public const int MaxFrameBytes = 65536;
        public const int MaxReliablePayloadBytes = 16384;
        public const int MaxRealtimePayloadBytes = 900;
        public const int MaxRoomPlayers = 8;
        public const byte ServerRealtimeChannel = MaxRoomPlayers;
        public const byte RealtimeChannelCount = MaxRoomPlayers + 1;
        public static readonly JsonSerializerOptions Json = new() {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 16
        };
    }

    public static class MessageType {
        public const string Hello = "hello", Welcome = "welcome", Error = "error", Ack = "ack";
        public const string Ping = "ping", Pong = "pong", UdpStatus = "udp_status";
        public const string ListRooms = "list_rooms", RoomList = "room_list", CreateRoom = "create_room", JoinRoom = "join_room";
        public const string LeaveRoom = "leave_room", SetReady = "set_ready", RoomChanged = "room_changed";
        public const string StartMatch = "start_match", MatchStarted = "match_started", EndMatch = "end_match", MatchEnded = "match_ended";
        public const string Reliable = "reliable", GameMessage = "game_message";
    }

    // One envelope for requests, responses and notifications. The server always stamps the sender identity.
    public sealed class ControlMessage {
        public string Type { get; set; } = "";
        public Guid RequestId { get; set; }
        public int ProtocolVersion { get; set; } = ConsoleProtocol.Version;
        public Guid PlayerId { get; set; }
        public string PlayerName { get; set; }
        public string SessionToken { get; set; }
        public int UdpPort { get; set; }
        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string GameId { get; set; }
        public int Capacity { get; set; }
        public bool RequiresUdp { get; set; }
        public bool Ready { get; set; }
        public bool UdpConnected { get; set; }
        public Guid MatchId { get; set; }
        public Guid TargetPlayerId { get; set; }
        public ushort Opcode { get; set; }
        public long Sequence { get; set; }
        public byte[] Payload { get; set; }
        public RoomSnapshot Room { get; set; }
        public RoomSummary[] Rooms { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
    }

    public sealed class PlayerSummary {
        public Guid PlayerId { get; set; }
        public string Name { get; set; } = "";
        public int Seat { get; set; }
        public bool Ready { get; set; }
        public bool UdpConnected { get; set; }
    }

    public sealed class RoomSummary {
        public string RoomId { get; set; } = "";
        public string Name { get; set; } = "";
        public string GameId { get; set; } = "";
        public int Capacity { get; set; }
        public int PlayerCount { get; set; }
        public bool RequiresUdp { get; set; }
        public bool Started { get; set; }
    }

    public sealed class RoomSnapshot {
        public string RoomId { get; set; } = "";
        public string Name { get; set; } = "";
        public string GameId { get; set; } = "";
        public Guid HostPlayerId { get; set; }
        public int Capacity { get; set; }
        public bool RequiresUdp { get; set; }
        public bool Started { get; set; }
        public Guid MatchId { get; set; }
        public long Revision { get; set; }
        public PlayerSummary[] Players { get; set; } = [];
    }

    public sealed class NetworkRequestException : Exception {
        public string Code { get; }
        public NetworkRequestException(string code, string message) : base(message) => Code = code;
    }
}
