# 服务器间链路（ServerLink / RemoteMessaging）

> 2.0 基础设施收敛后的定位：跨服玩法真正需要的服间通信 = **点对点 RPC + 一张心跳表 + 一张玩家路由表**。
> 推荐入口是 `GameFrameX.ServerLink` 薄门面封装包；`GameFrameX.Network.RemoteMessaging` 原名保留、原位收缩。

## ServerLink 薄门面（推荐入口）

`GameFrameX.ServerLink` 把服间链路的内置默认固化在一个薄门面上：

- **直连拨号**：默认 TCP 直连（经心跳注册表路由表解析端点），KCP 可显式叠加（`WithKcpTransport()`，TCP 恒为回落传输）。
- **固定重试上限**：默认 2 次重试（`WithFixedRetryLimit` 可调，0 = 不重试）；非幂等写操作请在业务侧关停重试。
- **玩家路由解析内置默认**：`ServerLinkPlayerRouting.ComposeUnifiedSender(...)` 以发现层玩家路由解析器为内置默认，装配统一消息发送器（`IUnifiedMessageSender`）与本服投递槽——跨服找人（合服后的刚需）开箱即用。
- **信封转发门面**：`ServerLinkEnvelopeForwarder.ForwardAsync(role, message, ...)` 把跨 Role 消息交给进程级路由缝：本进程 Role 走本地投递，跨进程跳打包 `RoleRouteEnvelopeMessage` 经共享传输池转发。

```csharp
// 直连拨号 + 固定重试上限
var client = new ServerLinkBuilder()
    .WithRouteTable(ActiveDiscoveryRuntime.RouteTableProvider)
    .Build();

// 玩家路由投递（统一消息发送器 + 本地投递槽一次装配）
var sender = ServerLinkPlayerRouting.ComposeUnifiedSender(
    client,
    ActiveDiscoveryRuntime.RouteTableProvider,
    PlayerRouteResolverBootstrap.Resolver,
    localPlayerSender,
    localServerType: "Game",
    localInstanceId: PlayerRouteResolverBootstrap.LocalInstanceId);
```

## RemoteMessaging 原位收缩清单

`GameFrameX.Network.RemoteMessaging` 保留原名，以下面为收缩后的边界：

| 面 | 处置 | 说明 |
|---|---|---|
| 协议握手 | **保留（最小版本校验）** | 握手帧交换 + major 不符显式拒绝 + 超时按旧版本节点放行（滚动升级窗口）；`InterNodeAuthentication` HMAC 节点间互信保留（fail-closed） |
| 多版本协商（Versioning） | **已移除** | 消息级 `ProtocolVersionAttribute` 标注与协商器抽象（多版本灰度面）已删除；线缆契约只剩握手帧的最小版本号校验 |
| 韧性（Resilience） | **默认 PassThrough** | 熔断器与端点健康评估默认直通（不熔断、不评分），重试默认关闭；`WithCircuitBreaker` / `WithRetry` 显式开启（自托管进阶配置，接口全部保留） |
| 治理（Unified 治理面） | **冻结** | `MessageGovernancePolicy` 与 `IServerInstanceSelector` / `ConsistentHashServerInstanceSelector` 保留现状供既有消费方使用、不再演进；`IUnifiedMessageSender` 玩家级投递为活跃面 |
| 观测（Observability） | **最小计数** | 调用计数 / 时延 / 错误计数常开（Metrics + 标准 OTel span）；详细观测面板归运营台 |
| 发现（Discovery） | **保留本体** | 心跳注册表 + 角色路由表 + 玩家路由表是合服底座，全部保留 |

环境变量开关（前缀 `RemoteMessaging__`）：`EnableCircuitBreaker`（默认 false）、`EnableRetry`（默认 false）、`MaxRetryCount`、`DefaultTimeoutMs`、`HandshakeTimeoutMs`、`EnableFaultInjection`（默认 false，仅开发环境）。旧键 `EnableUnifiedClient` 已随收缩移除，不再生效。

## 跨服调用准入规则（自 Guides 收编）

所有新增跨服务调用必须遵循以下规范：

1. 【强制】所有跨服务调用必须通过 `IRemoteMessageClient`（推荐经 `ServerLinkBuilder` 构建）发起，禁止在 ComponentAgent 中直接使用 `TcpClient` / `NetworkStream`。
2. 【强制】非幂等操作（写操作）必须设置 `AllowRetry = false`——重试默认关闭，显式开启重试时尤其要注意写操作的幂等性。
3. 【强制】幂等操作（读操作 / 查询）可设置 `AllowRetry = true`，但最大重试不超过 2 次。
4. 【推荐】使用 `CallWithResultAsync` 替代 `CallAsync`，获取结构化结果与状态码。
5. 【推荐】使用 `ServerLinkBuilder` / `RemoteMessagingBuilder` 构建客户端，而非直接实例化 `RemoteMessageClient`。
6. 【推荐】通过 `MessageObjectPoolHelper.Get/Return` 管理请求对象生命周期。

错误码映射参考：

| RemoteStatusCode | 业务错误码 | 说明 |
|---|---|---|
| Success | 0 | 成功 |
| Timeout | -1 | 超时 |
| ConnectionFailed | -2 | 连接失败 |
| EndpointNotFound | -3 | 服务发现失败 |
| Cancelled | -4 | 取消 |
| RetryExhausted | -5 | 重试耗尽 |
| ConnectionClosed | -6 | 连接关闭 |
| UnexpectedResponse | -7 | 响应异常 |
| CircuitOpen | -10 | 熔断开启（显式开启熔断器后才会出现） |
| ServiceUnavailable | -11 | 服务不可用（显式开启健康评估后才会出现） |
| ProtocolVersionIncompatible | 12 | 握手版本不兼容（跨 major 混布被拒绝） |
| UnknownError | -99 | 未知错误 |
