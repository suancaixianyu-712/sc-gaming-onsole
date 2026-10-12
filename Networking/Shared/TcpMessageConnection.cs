using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace GameConsole.Networking {
    // TCP is a byte stream: a four-byte little-endian length precedes every UTF-8 JSON message.
    // A single bounded writer prevents interleaved sends and unbounded buffering on slow connections.
    public sealed class TcpMessageConnection : IDisposable {
        readonly TcpClient m_socket;
        readonly Stream m_stream;
        readonly CancellationTokenSource m_stop = new();
        readonly Channel<byte[]> m_outbound = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(128) {
            SingleReader = true, FullMode = BoundedChannelFullMode.Wait
        });
        readonly Task m_writer;
        int m_closed;

        public TcpMessageConnection(TcpClient socket, Stream stream) {
            m_socket = socket;
            m_stream = stream;
            m_writer = WriteLoopAsync();
        }

        public bool TrySend(ControlMessage message) {
            if (Volatile.Read(ref m_closed) != 0) return false;
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, ConsoleProtocol.Json);
            if (body.Length > ConsoleProtocol.MaxFrameBytes) return false;
            byte[] frame = new byte[4 + body.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
            body.CopyTo(frame, 4);
            return m_outbound.Writer.TryWrite(frame);
        }

        public async Task<ControlMessage> ReadAsync(CancellationToken cancellationToken) {
            byte[] header = new byte[4];
            int first = await m_stream.ReadAsync(header.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            if (first == 0) return null;
            if (first < 4) await m_stream.ReadExactlyAsync(header.AsMemory(first), cancellationToken).ConfigureAwait(false);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length < 2 || length > ConsoleProtocol.MaxFrameBytes) throw new InvalidDataException("Invalid TCP frame size.");
            byte[] body = new byte[length];
            await m_stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ControlMessage>(body, ConsoleProtocol.Json)
                ?? throw new InvalidDataException("Empty TCP message.");
        }

        async Task WriteLoopAsync() {
            try {
                await foreach (byte[] frame in m_outbound.Reader.ReadAllAsync(m_stop.Token).ConfigureAwait(false)) {
                    await m_stream.WriteAsync(frame, m_stop.Token).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
            finally { Dispose(); }
        }

        public void Dispose() {
            if (Interlocked.Exchange(ref m_closed, 1) != 0) return;
            m_outbound.Writer.TryComplete();
            m_stop.Cancel();
            m_stream.Dispose();
            m_socket.Dispose();
        }
    }
}
