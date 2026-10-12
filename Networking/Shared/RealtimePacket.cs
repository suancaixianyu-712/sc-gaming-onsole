using System;
using System.Buffers.Binary;
using System.IO;

namespace GameConsole.Networking {
    public sealed class RealtimePacket {
        public Guid MatchId { get; set; }
        public Guid SenderId { get; set; }
        public ushort Opcode { get; set; }
        public uint Sequence { get; set; }
        public byte[] Payload { get; set; } = [];
    }

    public static class RealtimePacketCodec {
        public const int HeaderSize = 43;
        const uint Magic = 0x50535047;

        public static byte[] Encode(RealtimePacket packet) {
            if (packet.Payload == null || packet.Payload.Length > ConsoleProtocol.MaxRealtimePayloadBytes)
                throw new InvalidDataException("Realtime payload exceeds 900 bytes.");
            byte[] bytes = new byte[HeaderSize + packet.Payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
            bytes[4] = ConsoleProtocol.Version;
            packet.MatchId.TryWriteBytes(bytes.AsSpan(5, 16));
            packet.SenderId.TryWriteBytes(bytes.AsSpan(21, 16));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(37), packet.Opcode);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(39), packet.Sequence);
            packet.Payload.CopyTo(bytes, HeaderSize);
            return bytes;
        }

        public static bool TryDecode(ReadOnlySpan<byte> bytes, out RealtimePacket packet) {
            packet = null;
            if (bytes.Length < HeaderSize || bytes.Length > HeaderSize + ConsoleProtocol.MaxRealtimePayloadBytes
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic || bytes[4] != ConsoleProtocol.Version) return false;
            packet = new RealtimePacket {
                MatchId = new Guid(bytes.Slice(5, 16)), SenderId = new Guid(bytes.Slice(21, 16)),
                Opcode = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(37)),
                Sequence = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(39)), Payload = bytes.Slice(HeaderSize).ToArray()
            };
            return true;
        }

        public static bool IsNewer(uint sequence, uint previous) => unchecked((int)(sequence - previous)) > 0;
    }
}
