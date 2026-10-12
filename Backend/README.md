# 游戏机联机后端

独立的 .NET 10 服务，不依赖 Survivalcraft、图形界面或 Blender。可以部署在 Linux 或 Windows。

TCP 使用 .NET 自带的 `System.Net.Sockets`，UDP 使用 [LiteNetLib 2.1.4](https://github.com/RevenantX/LiteNetLib)。共享协议和客户端在上级 `Networking/` 目录。

## 已提供的能力

- 游客会话、TCP 握手，以及通过会话凭据绑定的 UDP 连接。
- 创建、查询、加入和离开房间，2–8 人，按 `gameId` 区分游戏。
- 玩家准备、房主开局/结束、等待房间中的房主移交。
- TCP 可靠消息：广播或指定房间内某个玩家，适合出牌、回合事件和结算。
- UDP 实时消息：有序的最新输入/状态，适合挡板移动、位置和瞄准等连续数据。
- 心跳、消息长度限制、收发队列限制、频率限制、断线清理。
- 服务器游戏处理器：每局独立实例，可接管规则验证和状态广播。
- 内置 `pong-online` 双人乒乓：服务器固定 60 Hz 计算挡板、小球、碰撞和比分，30 Hz 广播状态，率先取得 5 分获胜。

模组内已接入联机大厅和双人乒乓，AI 乒乓与打砖块仍可离线游玩。未注册处理器的 `gameId` 使用房间消息转发；注册后只交给游戏处理器，客户端不能直接广播未经处理的游戏消息。

## 启动已发布的后端

解压后端发布包，安装 [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)，在解压目录运行：

```sh
dotnet GameConsoleServer.dll
```

默认加载程序旁的 `server-settings.json`，监听 **TCP 26051** 和 **UDP 26052**。服务器防火墙和云安全组需要开放对应端口；客户端填写服务器 IP 或域名及 TCP 端口，UDP 端口由后端下发。

指定配置文件：

```sh
dotnet GameConsoleServer.dll --config /path/to/server-settings.json
```

Ctrl+C 或 Linux SIGTERM 会关闭服务和连接。默认最大 256 个连接、128 个房间、60 次/秒的服务器更新；可在配置中调整。

## 联机乒乓操作

1. 两名玩家在 PSP 中选择“乒乓对决 → 开始游戏 → 双人 / 联机对战”。
2. 输入本服务器的 IPv4 地址或域名及 TCP 端口，点击“连接服务器”。
3. 一方创建房间，把 6 位房间码发给另一方；另一方输入房间码加入。
4. 双方 UDP 就绪后准备，由房主开局。倒计时 3 秒后自动发球，先到 5 分获胜。
5. 结算后 X / O 返回房间，双方重新准备即可再玩。比赛中 O 离开会结束本局，HOME 或关闭 PSP 会断开连接。

摇杆或左右方向键移动自己的下方挡板。联机不允许单人暂停。球、碰撞和比分仅由服务器裁决；客户端没有提交球位置或比分的接口。

同步参数及二进制格式见 `Networking/PONG-SYNC.md`，发布包中的文件为 `PONG-SYNC.md`。

## 从源码构建

在“游戏机”项目根目录运行：

```sh
dotnet build Backend/GameConsoleServer.csproj -c Release
dotnet publish Backend/GameConsoleServer.csproj -c Release -o artifacts/server
dotnet artifacts/server/GameConsoleServer.dll
```

后端和模组可分别构建。后端只依赖 .NET 和 LiteNetLib；NuGet 源使用项目根目录的 `nuget.config`。

## Docker

从项目根目录构建，使用根目录作为构建上下文：

```sh
docker build -f Backend/Dockerfile -t gameconsole-server .
docker run --rm --name gameconsole-server -p 26051:26051/tcp -p 26052:26052/udp gameconsole-server
```

自定义配置可挂载到 `/app/server-settings.json`。如果修改配置中的端口，也需要调整 Docker 映射。

## TCP TLS

配置 `tlsCertificatePath` 为带私钥的 PFX 证书路径（相对路径以配置文件目录为基准），证书密码通过 `tlsPasswordEnvironmentVariable` 指定的环境变量读取，默认 `GAMECONSOLE_CERT_PASSWORD`。

客户端设置 `UseTls = true`，`Host` 使用证书对应的域名。客户端使用系统默认的证书校验，不跳过域名或信任链检查。证书路径留空时使用普通 TCP。TLS 只覆盖 TCP，LiteNetLib UDP 数据没有加密。

## 后续游戏接入

在 `Program.cs` 创建后端实例后、调用 `RunAsync` 前注册游戏处理器工厂：

```csharp
GameConsoleServer server = new(options);
server.RegisterGame("cards", context => new CardsRoom(context));
await server.RunAsync(stop.Token);
```

`CardsRoom` 继承 `GameRoomHandler`，每局创建一个实例。以下是接入骨架，出牌规则由具体游戏实现：

```csharp
sealed class CardsRoom : GameRoomHandler {
    public CardsRoom(GameRoomContext context) : base(context) { }

    public override void OnStarted() {
        // 在服务器创建牌堆/手牌。
        // Context.SendReliable(opcode, privateHand, playerId) 只发送给该玩家。
    }

    public override void OnReliable(Guid senderId, ushort opcode, byte[] payload) {
        // 校验行动者、回合、手牌及行动 ID，更新服务器状态。
        // Context.SendReliable(opcode, publicState) 广播经过校验的结果。
    }

    public override void Tick(float deltaTime) { }
    public override void OnEnded(string reason) { /* 释放本局资源。 */ }
}
```

即时游戏通过 `OnRealtime` 接收输入，在 `Tick` 中推进服务器状态，通过 `Context.SendRealtime` 广播整帧快照。需要所有玩家的 UDP 都可用时，创建房间设置 `requiresUdp = true`。TCP 类游戏设置为 `false`，玩家没有 UDP 也可以开局。

处理器及 `GameRoomContext` 的方法都在服务器主循环调用；异步计算结果应通过自己维护的队列在 `Tick` 中取出后使用。`OnEnded` 用于清理资源，结算数据应在调用 `Context.EndMatch()` **之前**发送。处理器异常会结束该局，不影响其他房间。

服务器发出的游戏消息中 `PlayerId` / `SenderId` 为 `Guid.Empty`，客户端据此区分服务器裁决和转发的玩家消息。可靠请求的 ACK 表示后端已经接收和分发，游戏是否接受某次行动由处理器另行返回。

## 生命周期与数据

- 房间、游客身份及对局均在内存中；进程重启后清空，目前没有账户数据库。
- TCP 断开后立即退出房间。对局中有人离开会结束本局；剩余玩家回到未准备状态。
- UDP 临时断开保留 TCP/房间，客户端每隔几秒尝试重新绑定。游戏处理器可根据房间快照处理 UDP 掉线。
- 完整断线后创建新的客户端连接；当前不提供会话恢复或中途重新加入对局。
- UDP 是整帧最新状态通道，旧帧可能被跳过。出牌、伤害、击杀及结算等不能丢失的事件走 TCP。

详细字段与字节格式见源码中的 `Networking/PROTOCOL.md`，客户端使用方式见 `Networking/CLIENT.md`，乒乓实现见 `Networking/PONG-SYNC.md`；发布包也包含这些文档。本次交付已编译，尚未进行设备联机或弱网实测。
