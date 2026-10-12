# 模组客户端网络层

`GameConsoleClient` 使用同一套协议连接后端，已经编译进 `GameConsoleMod.dll`；LiteNetLib DLL 和许可证包含在模组包里。网络 I/O 在后台进行，`Pump()` 才在调用线程触发界面/游戏事件。

每个 `PSPConsoleState` 提供独立 `Network` 客户端，动画更新时自动 `Pump()`；关闭 PSP 或对话框移除时释放连接。1.4.0 已提供服务器地址输入、房间码、创建/加入、准备/开局和双人乒乓界面。具体游戏也可以自己创建 `GameConsoleClient`。

乒乓接入使用 `PSPConsoleOnlineState.cs` 管理房间，`PSPOnlinePongGame.cs` 管理预测和插值；后台请求完成后排队到游戏主线程更新界面。完整同步说明见 [PONG-SYNC.md](PONG-SYNC.md)。

## 连接

```csharp
using GameConsole.Networking;

GameConsoleClient network = new();
network.MessageReceived += message => {
    // 在调用 Pump 的线程中处理 room_changed / match_started / game_message 等。
};
network.RealtimeReceived += packet => {
    // 读取整帧输入或快照。SenderId 为空 GUID 表示服务器状态。
};
network.Disconnected += reason => { /* 回到大厅，显示断线原因。 */ };

await network.ConnectAsync(new GameConsoleConnectionOptions {
    Host = "服务器 IP 或域名",
    TcpPort = 28886,
    PlayerName = "玩家昵称",
    EnableRealtime = true,
    UseTls = false // 与后端证书设置一致
});
```

`ConnectAsync` 成功表示 TCP 会话就绪，UDP 在后台连接。PVP 可以等待 `WaitForRealtimeAsync(cancellationToken)` 或检查 `IsRealtimeConnected`；为等待传入有超时的 CancellationToken。UDP 失败时仍可使用 TCP，掉线后会自动尝试重新连接 UDP。

如果不是使用 PSPConsoleState 提供的实例，每帧调用 `network.Pump()`。任何游戏/界面操作应在事件回调或主线程进行；异步请求后的继续执行线程不保证是游戏主线程。

## 房间与可靠事件

```csharp
// 房主；PVP 设 requiresUdp: true，TCP 打牌可以设为 false。
RoomSnapshot room = await network.CreateRoomAsync("cards", "朋友的房间", 4, requiresUdp: false);

// 其他玩家可查询房间或直接输入房间码。
RoomSummary[] rooms = await network.ListRoomsAsync("cards");
// await network.JoinRoomAsync("ABC123");

await network.SetReadyAsync(true); // 房主与每个成员都要准备。
// 所有人准备后，由房主调用：
await network.StartMatchAsync();

// 自定义 opcode 与负载，例如 JSON 出牌命令。
byte[] payload = System.Text.Encoding.UTF8.GetBytes("{\"actionId\":1,\"card\":7}");
await network.SendReliableAsync(100, payload);
// 指定一名成员：await network.SendReliableAsync(100, payload, targetPlayerId);
```

捕获 `NetworkRequestException` 可读取后端 `Code`，例如 `players_not_ready`、`udp_required`、`room_full`、`host_only`。请求默认 10 秒超时，超时本身不撤销已被服务器接收的行动；具体游戏需要行动 ID 和结果去重。

`network.Room` 是最新服务器房间快照，游戏应将它作为只读对象。`SendReliableAsync` 的 ACK 表示服务器接收/分发完成，实际行动结果由后端游戏处理器通过 `game_message` 返回。

## 实时消息

```csharp
// 每个游戏帧或固定 30/60 Hz 发送完整输入，不必等待响应。
byte[] input = new byte[4];
System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(input, horizontalAxis);
bool queued = network.SendRealtime(1, input);
```

实时负载最多 900 字节，单个玩家的所有实时字段应合并在同一帧。旧的未发送数据会被替换，游戏事件则使用 TCP。离开旧对局后滞留的 UDP 帧根据 matchId 丢弃。

## 关闭

```csharp
await network.LeaveRoomAsync(); // 留在服务器上，可再加入其他房间。
await network.DisposeAsync();   // 关闭连接并等待后台任务退出。
```

游戏 UI 关闭时可调用不阻塞的 `Dispose()`，后台自行退出。TCP 完整断开后，新建客户端实例重新连接。重连目前会生成新游客身份，不恢复原房间或对局。
