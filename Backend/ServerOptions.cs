using System;
using GameConsole.Networking;

namespace GameConsole.Backend {
    public sealed class ServerOptions {
        public string BindAddress { get; set; } = "0.0.0.0";
        public int TcpPort { get; set; } = ConsoleProtocol.DefaultTcpPort;
        public int UdpPort { get; set; } = ConsoleProtocol.DefaultUdpPort;
        // 走 frp 等转发时填写：客户端实际连接的 UDP 公网地址和端口。留空/0 时使用 TCP 主机和 UdpPort。
        public string PublicUdpHost { get; set; } = "";
        public int PublicUdpPort { get; set; }
        public int TickRate { get; set; } = 60;
        public int MaxClients { get; set; } = 256;
        public int MaxRooms { get; set; } = 128;
        public int HandshakeTimeoutSeconds { get; set; } = 10;
        public int IdleTimeoutSeconds { get; set; } = 30;
        public int TcpMessagesPerSecond { get; set; } = 60;
        public int UdpMessagesPerSecond { get; set; } = 120;
        public string TlsCertificatePath { get; set; } = "";
        public string TlsPasswordEnvironmentVariable { get; set; } = "GAMECONSOLE_CERT_PASSWORD";

        public void Validate() {
            if (TcpPort is < 1 or > 65535 || UdpPort is < 1 or > 65535) throw new ArgumentException("Ports must be in 1..65535.");
            if (PublicUdpPort is < 0 or > 65535) throw new ArgumentException("PublicUdpPort must be 0 or 1..65535.");
            if (TickRate is < 10 or > 120 || MaxClients is < 2 or > 4096 || MaxRooms is < 1 or > 4096)
                throw new ArgumentException("Invalid tick rate or server capacity.");
            if (HandshakeTimeoutSeconds is < 2 or > 60 || IdleTimeoutSeconds is < 15 or > 300
                || TcpMessagesPerSecond is < 5 or > 1000 || UdpMessagesPerSecond is < 20 or > 1000)
                throw new ArgumentException("Invalid timeouts or packet limits.");
        }
    }
}
