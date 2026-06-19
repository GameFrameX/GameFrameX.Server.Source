# 服务端可靠 FIFO 协议升级说明

> 适用仓库：`GameFrameX.Server.Source`
> 结论：**需要同步修改服务器协议与数据包格式**，不是只改客户端就能跑通。

## 1. 先说结论

当前服务端网络包头仍是旧格式：

```text
PacketLength  uint32
OperationType byte
ZipFlag       byte
UniqueId      int32
MessageId     int32
```

总长 14 字节。

而 Unity 客户端已升级为：

```text
PacketLength  uint32
OperationType byte
ZipFlag       byte
HeaderFlags   uint16
UniqueId      int32
MessageId     int32
SessionId     uint64   (可靠扩展)
ReliableSeq   uint64   (可靠扩展)
AckSeq        uint64   (可靠扩展)
```

基础头 16 字节，可靠业务包完整头 40 字节。

所以服务端必须改：

1. 包头编解码
2. 接收状态机
3. 业务消息路由
4. ACK / Duplicate / Resume / 踢人语义
5. 会话表和响应缓存

否则新客户端发来的 `HeaderFlags` 会被旧服务端当成 `UniqueId` 的一部分，后续字段整体错位，包会直接解析坏掉。

---

## 2. 需要改的模块

### 2.1 `GameFrameX.NetWork.Message`

重点文件：

- `GameFrameX.NetWork.Message/DefaultMessageEncoderHandler.cs`
- `GameFrameX.NetWork.Message/DefaultMessageDecoderHandler.cs`
- `GameFrameX.NetWork.Message/MessageObjectPipelineFilter.cs`

#### 要改什么

1. 发送头从 14B 改成 16B。
2. 接收头从 14B 改成 16B 起步。
3. 新增 `HeaderFlags` 解析。
4. 新增可靠扩展头解析：
   - `SessionId`
   - `ReliableSequence`
   - `AckSequence`
5. 严格拒绝旧协议，不做兼容兜底。

#### 具体改法

`DefaultMessageEncoderHandler` 目前写的是：

```text
length + operationType + zipFlag + uniqueId + messageId
```

要改成：

```text
length + operationType + zipFlag + headerFlags + uniqueId + messageId
```

如果消息是业务可靠消息，再追加 24 字节扩展头。

`DefaultMessageDecoderHandler` 目前只读 14B。
要先读 16B 基础头，再根据 `HeaderFlags.Reliable` 判断是否继续读 24B 扩展头。

#### 需要新增的协议常量

建议在服务端新增一个和客户端一致的常量类，例如：

- `PacketHeaderLayout.BaseHeaderLength = 16`
- `PacketHeaderLayout.ReliableExtensionLength = 24`
- `PacketHeaderLayout.ReliableHeaderLength = 40`
- 字段偏移：
  - `PacketLengthOffset = 0`
  - `OperationTypeOffset = 4`
  - `ZipFlagOffset = 5`
  - `HeaderFlagsOffset = 6`
  - `UniqueIdOffset = 8`
  - `MessageIdOffset = 12`
  - `SessionIdOffset = 16`
  - `ReliableSequenceOffset = 24`
  - `AckSequenceOffset = 32`

---

### 2.2 `GameFrameX.NetWork.Abstractions`

重点文件：

- `GameFrameX.NetWork.Abstractions/INetworkMessageHeader.cs`
- `GameFrameX.NetWork.Abstractions/INetworkMessagePackage.cs`
- `GameFrameX.NetWork.Abstractions/MessageProtoHelper.cs`

#### 要改什么

服务端头对象不能只保留：

- `MessageId`
- `UniqueId`
- `OperationType`
- `ZipFlag`

还要能表达：

- `HeaderFlags`
- `ProtocolVersion`
- `SessionId`
- `ReliableSequence`
- `AckSequence`
- `IsDuplicate`
- `HasReliableExtension`

#### 具体改法

1. 扩展 `INetworkMessageHeader`，补上协议标记字段。
2. `MessageObjectHeader` 增加可靠扩展字段。
3. `NetworkMessagePackage.DeserializeMessageObject()` 保留原 `UniqueId` / `OperationType` 的回填逻辑，但要先让 header 具备可靠语义。
4. 消息类型映射逻辑保持不变，包头升级不应改业务 MessageId 体系。

---

### 2.3 `GameFrameX.NetWork.Kcp`

重点文件：

- `GameFrameX.NetWork.Kcp/KcpMessagePipelineFilter.cs`
- `GameFrameX.NetWork.Kcp/KcpServer.cs`
- `GameFrameX.NetWork.Kcp/KcpSession.cs`
- `GameFrameX.NetWork.Kcp/KcpNetWorkChannel.cs`

#### 要改什么

KCP 这里只负责字节传输，不代表业务层可靠。

必须确保：

1. KCP 收到整包后按新 16B/40B 协议解析。
2. 不能再把 KCP 自己的可靠性当成业务可靠性。
3. 控制包和业务包要分流。

#### 具体改法

- `ParseMessage()` 继续按整包长度切片，但切片后交给新 `DefaultMessageDecoderHandler`。
- 对 `Resume`、`ACK`、`Kick` 这类控制包，先进入控制消息处理，不走业务 FIFO。
- 如果底层 KCP 已连上，但服务端会话未恢复成功，业务仍不能放行。

---

### 2.4 `GameFrameX.Apps`

重点位置大概率在：

- `GameFrameX.Apps/Server`
- `GameFrameX.Hotfix`
- `GameFrameX.Core/BaseHandler`

#### 要改什么

服务端业务处理要加一层会话协商。

#### 你需要实现的状态

每个连接或会话至少维护：

```text
SessionId
LastProcessedClientSequence
LastAckedClientSequence
PendingResponseCache
LoginResponseCache
ExpireAt
```

#### 业务规则

1. `ReliableSequence == LastProcessedClientSequence + 1`
   - 正常执行业务
   - 推进序号
   - 返回响应 / ACK

2. `ReliableSequence <= LastProcessedClientSequence`
   - 判定为重复包
   - 不重复执行业务
   - 返回缓存响应或 ACK
   - 可标记 `Duplicate`

3. `ReliableSequence > LastProcessedClientSequence + 1`
   - 判定为缺口
   - 不执行业务
   - 回 `AckSequence = LastProcessedClientSequence`

---

## 3. 协议怎么改

### 3.1 基础头

服务端发送和接收都统一改成 16 字节。

```text
PacketLength   uint32
OperationType  byte
ZipFlag        byte
HeaderFlags    uint16
UniqueId       int32
MessageId      int32
```

### 3.2 HeaderFlags

建议和客户端完全一致：

- `ProtocolVersion = 1`
- `Reliable`
- `Ack`
- `Control`
- `Resume`
- `LatestOnly`
- `NoRetry`
- `Duplicate`

服务端必须能读懂这些位，但不一定每条消息都用。

### 3.3 可靠扩展头

仅业务可靠消息携带：

```text
SessionId        uint64
ReliableSequence uint64
AckSequence      uint64
```

控制包不进入业务 FIFO，但仍应携带会话与 ACK 信息，便于恢复和对齐。

---

## 4. 服务端处理流程

### 4.1 收包流程

1. 先读 `PacketLength`
2. 读取 16B 基础头
3. 校验 `ProtocolVersion`
4. 解析 `HeaderFlags`
5. 如果是可靠业务包，再读 24B 扩展头
6. 校验 `SessionId`
7. 按 `ReliableSequence` 做去重 / 补洞 / 顺序处理
8. 再进入业务分发

### 4.2 ACK 流程

服务端对客户端 ACK 应采用累计确认：

```text
AckSequence = N
```

含义是：`<= N` 的业务包都已经处理完。

### 4.3 Resume 流程

短闪断后客户端会先发 Resume 控制包。

服务端需要：

1. 查 `SessionId`
2. 校验会话是否仍有效
3. 返回当前已处理到的 `AckSequence`
4. 允许客户端补发剩余 pending

失败时：

- 返回拒绝
- 让客户端进入业务重连

### 4.4 Duplicate / 重复登录

如果同一业务消息重发：

- 不要重复执行业务
- 返回原响应或 ACK
- 可打 `Duplicate` 标记

如果是重复登录：

- 返回原登录响应
- 复用或替换 `SessionId`
- 按业务策略踢旧连接或保留新连接

---

## 5. 建议的代码改动顺序

1. 先改协议常量和头结构
2. 再改编码 / 解码
3. 再改 KCP / TCP 接收管线
4. 再改会话表、ACK、Resume、Duplicate
5. 最后补测试

不要先改业务逻辑再改包头，不然中间态不好排。

---

## 6. 具体文件清单

### 必改

- `GameFrameX.NetWork.Message/DefaultMessageEncoderHandler.cs`
- `GameFrameX.NetWork.Message/DefaultMessageDecoderHandler.cs`
- `GameFrameX.NetWork.Message/MessageObjectPipelineFilter.cs`
- `GameFrameX.NetWork.Abstractions/INetworkMessageHeader.cs`
- `GameFrameX.NetWork.Abstractions/MessageProtoHelper.cs`
- `GameFrameX.NetWork.Kcp/KcpMessagePipelineFilter.cs`
- `GameFrameX.NetWork.Kcp/KcpServer.cs`
- `GameFrameX.NetWork.Kcp/KcpSession.cs`

### 可能要跟着改

- `GameFrameX.Core/BaseHandler`
- `GameFrameX.Hotfix`
- `GameFrameX.Apps/Server`
- `GameFrameX.Client/Bot/BotTcpClient.cs`

`BotTcpClient` 不是生产服务端，但如果它连接的是同一套服务端协议，也必须同步升级，否则测试工具会先坏。

---

## 7. 测试清单

### 7.1 单元测试

1. 16B 基础头编码 / 解码
2. 40B 可靠头编码 / 解码
3. 旧 14B 包头拒绝
4. `HeaderFlags` 版本位解析
5. `ReliableSequence` 重复包判定
6. `AckSequence` 累计确认
7. `Resume` 成功 / 失败

### 7.2 集成测试

1. 客户端发业务包，服务端正常回包
2. 客户端断线后 10 秒内恢复，服务端返回同一会话 ACK
3. 客户端重发同一可靠序号，服务端不重复执行业务
4. 客户端重复登录，服务端返回原登录结果或执行替换策略
5. 旧客户端连新服务端，必须明确失败，不可半兼容

---

## 8. 上线前必须确认的配置

- `ProtocolVersion = 1`
- `SilentResumeWindowSeconds = 10`
- `ServerSessionTtlSeconds = 30`
- `ResponseCacheTtlSeconds = 30`
- `ReliableRetryLimit = 5`
- `PendingQueueMaxCount = 1024`
- `PendingQueueMaxBytes = 4 MB`
- `PendingMessageMaxBytes = 256 KB`

---

## 9. 风险

### 风险 1：包头错位

如果服务端还按 14B 读，`HeaderFlags` 会污染后续字段，消息类型和 `UniqueId` 会一起错。

### 风险 2：只改客户端

客户端新协议发上来，服务端不认，等于整条链路都断。

### 风险 3：只改业务不改会话

即使能收包，没有 `SessionId`、ACK、响应缓存，Resume 也恢复不了。

### 风险 4：把 KCP 当业务可靠

KCP 只能保证传输层，不保证业务 FIFO、补发和会话恢复。

---

## 10. 推荐落地方式

建议服务端单独起一个协议升级 change，拆成四步：

1. 冻结协议常量和包头布局
2. 升级编码 / 解码和接收管线
3. 加会话表、ACK、Resume、Duplicate
4. 补齐集成测试和回归

---

## 11. 一句话总结

这次不是“客户端网络模块完成了，服务器看看要不要跟一下”，而是**协议已经破坏性升级了，服务器必须同步改包头和会话语义**，否则新旧端无法互通。
