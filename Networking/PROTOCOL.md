# 联机协议 v1

## 传输分工

| 通道 | 库 | 用途 | 上限 |
| --- | --- | --- | --- |
| TCP | .NET `System.Net.Sockets` | 会话、房间、准备/开局、可靠游戏事件 | 单帧 64 KiB；游戏负载 16 KiB |
| UDP | LiteNetLib 2.1.4 `DeliveryMethod.Sequenced` | 实时输入/整帧快照 | 游戏负载 900 字节 |

TCP 默认端口 28886，UDP 默认端口 28887。UDP 不是裸数据报端口，外部客户端也必须使用 LiteNetLib 的连接和数据帧格式。

## TCP 格式

每帧：4 字节无符号意义的长度（实际读取为正的 Int32，小端，**只计算 JSON 字节数**），随后是 UTF-8 JSON。按长度读取，支持分包、粘包和多个连续消息。字段名为 camelCase，`payload` 是 .NET JSON 默认的 Base64 字符串。

所有请求支持 `requestId`（GUID），响应回传相同 ID；事件通知不带请求 ID。客户端每个请求生成独立 ID。`error` 包含 `code` 和 `message`。

### 会话

1. 连接 TCP，发送 `hello`，包含 `protocolVersion: 1` 和 `playerName`。
2. 收到 `welcome`：`playerId`、`sessionToken`、`udpPort`、`protocolVersion`。
3. TCP 每 5 秒发送 `ping`，后端返回 `pong`，默认 30 秒没有 TCP 消息后关闭会话。
4. UDP 用 `NetDataWriter` 依次 `Put` 三个字符串作为连接数据：应用键 `codex.GameConsole/v1`、playerId 的 GUID 文本、64 字符 sessionToken。
5. UDP 校验成功后通过 TCP 发送 `udp_status`，`udpConnected` 表示绑定状态。

sessionToken 只用于当前游客会话，不是账户登录令牌。TCP 断开会使它失效，UDP 不能单独进入房间。

### 房间请求

| type | 请求字段 | 响应 |
| --- | --- | --- |
| `list_rooms` | 可选 gameId | `room_list` + rooms（最多 100 个） |
| `create_room` | gameId、roomName、capacity、requiresUdp | `ack` + room |
| `join_room` | roomId | `ack` + room |
| `leave_room` | 无 | `ack` |
| `set_ready` | ready | `ack` + room |
| `start_match` | 无，仅房主 | `ack` + room |
| `end_match` | 无，仅房主 | `ack` + room |

`gameId` 支持小写英文字母、数字、点、下划线和连字符，1–32 字符。人数 2–8，房间码 6 位十六进制文本，加入时不区分大小写。房主也需要准备；至少两人，所有人准备后才能开局。`requiresUdp = true` 还要求每个玩家的 UDP 已绑定。

`room_changed`、`match_started`、`match_ended` 事件携带完整 `room` 快照。离开者收到 `room_changed` 且 `room = null`。快照包含房间、游戏、房主、人数、UDP 要求、revision、matchId、started 和玩家数组；玩家包含 playerId、name、seat、ready、udpConnected。

每次开局生成新 `matchId`，过期对局的游戏消息被拒绝。对局中有人离开会结束本局。等待时房主离开会移交给剩余首位玩家，空房间被删除。

### 可靠游戏数据

发送 `reliable`：`requestId`、`matchId`、`opcode`（UInt16）、`payload`（最多 16384 字节）、可选 `targetPlayerId`。

未注册服务器游戏处理器时，后端转发 `game_message`，携带服务器确定的 `playerId`、roomId、matchId、opcode、payload、targetPlayerId、单局递增 sequence。空目标 GUID 广播给全体，包含发送者；指定目标只发给该房间成员。接收者分别收到 ACK 和/或游戏事件。

注册处理器后，该消息只交给处理器，由其返回经过规则验证的结果，不直接转发。服务器发布的 `game_message` 的 playerId 是空 GUID。opcode 和负载内部格式由每款游戏约定；行动 ID、回合号和重复行动处理也由游戏规则层定义。

## UDP 游戏帧

通过 LiteNetLib 发送以下二进制数据，所有整数为小端：

| 偏移 | 长度 | 字段 |
| --- | --- | --- |
| 0 | 4 | magic，UInt32 `0x50535047` |
| 4 | 1 | protocolVersion，1 |
| 5 | 16 | matchId，.NET Guid.TryWriteBytes 格式 |
| 21 | 16 | senderId，.NET Guid.TryWriteBytes 格式 |
| 37 | 2 | opcode，UInt16 |
| 39 | 4 | sequence，UInt32 |
| 43 | 0–900 | payload |

最大应用帧 943 字节。发送方按帧递增 sequence，按有符号差值比较新旧，支持 UInt32 环回。后端覆盖客户端填写的 senderId，校验当前房间、matchId、顺序及频率。

客户端发往服务器使用 LiteNetLib channel 0。房间转发到客户端时，channel 是发送玩家的 seat（0–7），每名玩家独立排序，避免两名玩家的快照互相覆盖。服务器处理器发布的状态使用 channel 8、senderId 空 GUID。双方 `ChannelsCount = 9`。

客户端发送队列只保留最新的一个未发送帧，接收缓存每个来源只保留最新帧，`Pump()` 时交给游戏。一次实时消息应包含该来源的**完整当帧输入或快照**，不能把必须完整接收的一串增量操作放进此通道。可靠事件使用 TCP。

TCP 与 UDP 没有跨通道顺序保证。客户端以 TCP 的 matchId 为依据，在收到开局事件前忽略 UDP 游戏帧。游戏自行约定帧号、输入时间戳、插值和预测方式。

## 后端边界

目前是单进程、内存房间及游客身份。默认限速每人 TCP 60 帧/秒、UDP 120 帧/秒。TCP 超限会断开，UDP 超限丢弃；配置可以调整。通信层不解释游戏负载，实际规则由注册的 `GameRoomHandler` 实现。

内置 `pong-online` 的输入、状态及结算格式见 [PONG-SYNC.md](PONG-SYNC.md)。
