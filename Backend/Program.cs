using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameConsole.Networking;

namespace GameConsole.Backend {
    internal static class Program {
        static async Task<int> Main(string[] args) {
            if (args.Length > 0 && args[0] is "--help" or "-h") {
                Console.WriteLine("dotnet GameConsoleServer.dll [--config server-settings.json]");
                return 0;
            }
            if (args.Length != 0 && (args.Length != 2 || args[0] != "--config")) {
                Console.Error.WriteLine("Use --config <path> or --help.");
                return 2;
            }
            using CancellationTokenSource stop = new();
            Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
            using PosixSignalRegistration signal = OperatingSystem.IsWindows() ? null
                : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; stop.Cancel(); });
            try {
                string path = args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(AppContext.BaseDirectory, "server-settings.json");
                ServerOptions options = JsonSerializer.Deserialize<ServerOptions>(await File.ReadAllTextAsync(path), ConsoleProtocol.Json)
                    ?? throw new InvalidDataException("Empty server settings.");
                if (!string.IsNullOrEmpty(options.TlsCertificatePath) && !Path.IsPathRooted(options.TlsCertificatePath))
                    options.TlsCertificatePath = Path.Combine(Path.GetDirectoryName(path), options.TlsCertificatePath);
                GameConsoleServer server = new(options);
                server.RegisterGame(PongProtocol.GameId, context => new PongRoomHandler(context));
                await server.RunAsync(stop.Token).ConfigureAwait(false);
                return 0;
            }
            catch (Exception exception) {
                Console.Error.WriteLine($"后端启动或运行失败：{exception.Message}");
                return 1;
            }
        }
    }
}
