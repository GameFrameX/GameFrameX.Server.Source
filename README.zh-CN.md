<div align="center">

<img src="https://download.alianblank.com/gameframex/gameframex_logo_320.png" alt="Game Frame X Logo" width="160" />

# GameFrameX Server

[![License](https://img.shields.io/badge/license-blue.svg)](LICENSE)
[![Version](https://img.shields.io/github/v/release/GameFrameX/GameFrameX.Server.Source)](https://github.com/GameFrameX/GameFrameX.Server.Source/releases)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Documentation](https://img.shields.io/badge/docs-gameframex.doc.alianblank.com-brightgreen.svg)](https://gameframex.doc.alianblank.com)

[![Discord](https://img.shields.io/badge/-5865F2?logo=discord&logoColor=white)](https://discord.gg/VDWUjWMDw9)
[![GitHub](https://img.shields.io/badge/-181717?logo=github&logoColor=white)](https://github.com/GameFrameX/gameframex)
[![Bilibili](https://img.shields.io/badge/-00A1D6?logo=bilibili&logoColor=white)](https://www.bilibili.com/video/BV1yrpeepEn7)
[![Gitee](https://img.shields.io/badge/-C71D23?logo=gitee&logoColor=white)](https://gitee.com/GameFrameX/gameframex)

**独立游戏前后端一体化解决方案 · 独立游戏开发者的圆梦大使**

<br />

[文档](https://gameframex.doc.alianblank.com) · [快速开始](#快速开始) · QQ群: 467608841 / 233840761

<br />

[English](README.md) | **简体中文** | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [한국어](README.ko.md)

</div>

## 目录

- [项目简介](#项目简介)
  - [功能特性](#功能特性)
- [快速开始](#快速开始)
  - [环境要求](#环境要求)
  - [安装](#安装)
- [使用示例](#使用示例)
  - [配置管理](#配置管理)
  - [业务逻辑开发](#业务逻辑开发)
  - [热更新机制](#热更新机制)
  - [Docker 部署](#docker-部署)
  - [多进程跨进程联调](#多进程跨进程联调)
  - [监控与可观测性](#监控与可观测性)
  - [测试](#测试)
- [架构概览](#架构概览)
  - [项目结构](#项目结构)
- [进程拓扑同构](#进程拓扑同构)
  - [多角色启动](#多角色启动)
  - [Docker Compose 文件](#docker-compose-文件)
- [依赖](#依赖)
- [文档与资源](#文档与资源)
- [社区与支持](#社区与支持)
  - [贡献指南](#贡献指南)
- [更新日志](#更新日志)
- [开源协议](#开源协议)

---

## 项目简介

GameFrameX Server 是基于 C# .NET 10.0 开发的高性能、跨平台游戏服务器框架，采用 Actor 模型设计，支持热更新机制。专为多人在线游戏开发而设计，支持 Unity3D、Godot、LayaBox 等多种客户端平台集成。

**设计理念**：大道至简，以简化繁

---

### 功能特性

#### 高性能架构

- **Actor 模型**：基于 TPL DataFlow 构建的无锁高并发系统，通过消息传递机制避免传统锁性能损耗
- **全异步编程**：完整的 async/await 异步编程模型
- **零锁设计**：Actor 内部状态通过消息队列串行化访问，无需加锁
- **批量持久化**：支持批量数据库写入，可配置批量大小和超时时间
- **雪花 ID 生成**：内置分布式唯一 ID 生成器，支持工作节点和数据中心配置

#### 热更新系统

- **零停机更新**：运行时加载新逻辑程序集，无需停止服务
- **状态逻辑分离**：持久化状态数据（Apps 层）与可热更业务逻辑（Hotfix 层）严格分离
- **优雅过渡**：旧程序集保留 10 分钟宽限期，等待进行中请求完成后卸载
- **版本管理**：支持通过 HTTP 端点指定版本号加载

#### 多协议网络通信

- **TCP**：基于 SuperSocket 的高性能 TCP 服务器，主要游戏通信协议
- **UDP**：可选的 UDP 协议支持
- **WebSocket**：基于 SuperSocket WebSocket 的双向通信
- **HTTP/HTTPS**：基于 Kestrel 的 HTTP 服务，支持 Swagger 文档、CORS、健康检查
- **KCP**：升级为正式支持（opt-in）的 UDP 可靠传输；`KcpPort` 为 `0` 时与 TCP 共用端口
- **跨进程消息**：内置 RemoteMessaging 模块，支持断路器、重试策略、一致性哈希分片

#### 数据库与持久化

- **双数据库 Provider**：通过 `DatabaseProvider` 选择 `Mongo`（默认）或 `PostgreSql`，两个 Provider 共享同一套 `GameFrameX.DataBase` 抽象层
- **MongoDB Provider**：完整的 MongoDB 集成，支持健康状态机（Healthy → Degraded → Unhealthy → Recovering）
- **PostgreSQL Provider**：基于 Npgsql，具备与 MongoDB Provider 相同的重试 / 恢复 / 可用性 / 健康检查能力
- **透明持久化**：StateComponent 自动序列化/反序列化，通过定时批量 upsert 操作持久化
- **连接池管理**：可配置的连接池和重试策略
- **OpenTelemetry 集成**：数据库操作指标（延迟、重试次数、健康状态）

#### 监控与可观测性

- **OpenTelemetry**：全面的指标（Metrics）、追踪（Tracing）和日志（Logging）
- **Prometheus**：原生指标导出端点
- **Grafana Loki**：日志聚合输出支持
- **Serilog**：结构化日志，支持控制台、文件、Loki 多输出

---

## 快速开始

### 环境要求

- 仅支持 [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，不支持 .NET 8/9。
- [MongoDB 4.x+](https://www.mongodb.com/try/download/community)（默认 Provider；也可通过 `--DatabaseProvider=PostgreSql` 使用 PostgreSQL）
- Visual Studio 2022 或 JetBrains Rider（推荐）

### 安装

1. **克隆仓库**
   ```bash
   git clone https://github.com/GameFrameX/GameFrameX.Server.Source.git
   cd GameFrameX.Server.Source
   ```

2. **还原依赖**
   ```bash
   dotnet restore
   ```

3. **构建项目**
   ```bash
   dotnet build
   ```

4. **启动 MongoDB**
   ```bash
   # 本地安装方式
   mongod --dbpath /path/to/data

   # 或使用 Docker
   docker run -d -p 27017:27017 --name mongo mongo:8.2
   ```

5. **运行服务器**
   ```bash
   dotnet run --project GameFrameX.Launcher -- \
       --ServerType=Game \
       --ServerId=1000 \
       --OuterPort=29100 \
       --HttpPort=28080 \
       --DataBaseUrl=mongodb://127.0.0.1:27017 \
       --DataBaseName=gameframex
   ```

6. **验证启动**
   - 健康检查：`http://localhost:28080/health`（存活探针：`/alive`）
   - 查看控制台日志确认启动成功

---

## 使用示例

以下示例覆盖从配置、业务逻辑、热更新到部署与调试的完整开发流程。

---

### 配置管理

GameFrameX 使用命令行参数 (`--Key=Value`) 进行配置，所有配置项定义在 `StartupOptions` 类中。

#### 服务器配置

| 配置项 | 说明 | 默认值 | 示例 |
|:------|:-----|:------|:----|
| `ServerType` | 服务器类型（必填） | 无 | `Game`、`Social` |
| `ServerId` | 服务器唯一标识 ID | 无 | `1000` |
| `ServerInstanceId` | 服务器实例 ID（区分同类型不同实例） | `0` | `1001` |
| `IsSingleMode` | 是否单进程模式 | `false` | `true` |
| `MinModuleId` | 业务模块起始 ID（模块分片） | `0` | `100` |
| `MaxModuleId` | 业务模块结束 ID（模块分片） | `0` | `1000` |
| `TimeZone` | 服务器时区 | `Asia/Shanghai` | `UTC` |
| `IsUseTimeZone` | 是否启用自定义时区 | `false` | `true` |
| `Language` | 语言设置 | 无 | `zh-CN` |

#### 网络配置

| 配置项 | 说明 | 默认值 | 示例 |
|:------|:-----|:------|:----|
| `InnerHost` | 内部通信 IP（集群间） | `0.0.0.0` | `0.0.0.0` |
| `InnerPort` | 内部通信端口 | `8888` | `29100` |
| `OuterHost` | 外部通信 IP（面向客户端） | `0.0.0.0` | `0.0.0.0` |
| `OuterPort` | 外部通信端口 | 无 | `29100` |
| `IsEnableTcp` | 是否启用 TCP 服务 | `true` | `true` |
| `IsEnableUdp` | 是否启用 UDP 服务 | `false` | `true` |
| `IsEnableKcp` | 是否启用 KCP 服务（仅配置文件） | `false` | `true` |
| `KcpPort` | KCP 端口（`0` 时与 TCP 共用端口） | `0` | `29120` |
| `IsEnableWebSocket` | 是否启用 WebSocket | `false` | `true` |
| `WsPort` | WebSocket 端口 | `8889` | `29300` |
| `IsEnableHttp` | 是否启用 HTTP 服务 | `true` | `true` |
| `HttpPort` | HTTP 服务端口 | `8080` | `28080` |
| `HttpsPort` | HTTPS 服务端口 | 无 | `443` |
| `HttpUrl` | API 接口根路径 | `/game/api/` | `/game/api/` |
| `HttpIsDevelopment` | HTTP 开发模式（启用 Swagger） | `false` | `true` |
| `IsEnableOnlineAdmin` | 是否在进程内托管 Online Runtime 与管理 API | `false` | `true` |
| `OnlineAdminPort` | Online 管理 API 监听端口 | `28090` | `28090` |
| `OnlineAdminApiPrefix` | Online 管理 API 路由前缀 | `online/admin` | `online/admin` |
| `OnlineTenantId` | Online Runtime 授权租户 ID（作用域三元组） | `0` | `1` |
| `OnlineAppId` | Online Runtime 授权应用 ID（作用域三元组） | `0` | `1` |

#### 数据库配置

| 配置项 | 说明 | 默认值 | 示例 |
|:------|:-----|:------|:----|
| `DatabaseProvider` | 数据库 Provider | `Mongo` | `PostgreSql` |
| `DataBaseUrl` | 数据库连接字符串 | 无 | `mongodb://localhost:27017` |
| `DataBaseName` | 数据库名称 | 无 | `gameframex` |
| `DataBasePassword` | 数据库密码 | 无 | `your_password` |

#### Actor 配置

| 配置项 | 说明 | 默认值 | 示例 |
|:------|:-----|:------|:----|
| `ActorTimeOut` | Actor 任务执行超时（毫秒） | `30000` | `60000` |
| `ActorQueueTimeOut` | Actor 队列超时（毫秒） | `30000` | `60000` |
| `ActorRecycleTime` | Actor 空闲回收时间（分钟） | `15` | `30` |
| `SaveDataInterval` | 数据保存间隔（毫秒） | `30000` | `60000` |
| `SaveDataBatchCount` | 批量保存数量 | `500` | `1000` |
| `SaveDataBatchTimeOut` | 批量保存超时（毫秒） | `30000` | `60000` |

#### 日志配置

| 配置项 | 说明 | 默认值 | 示例 |
|:------|:-----|:------|:----|
| `IsDebug` | 调试日志总开关 | `false` | `true` |
| `LogIsConsole` | 输出到控制台 | `true` | `false` |
| `LogIsWriteToFile` | 输出到文件 | `true` | `false` |
| `LogEventLevel` | 日志级别 | `Debug` | `Information` |
| `LogRollingInterval` | 日志滚动间隔 | `Day` | `Hour` |
| `LogIsFileSizeLimit` | 限制单个文件大小 | `true` | `false` |
| `LogFileSizeLimitBytes` | 文件大小限制 | `104857600` (100MB) | `52428800` |
| `LogRetainedFileCountLimit` | 保留文件数量 | `31` | `90` |
| `LogIsGrafanaLoki` | 输出到 Grafana Loki | `false` | `true` |
| `LogGrafanaLokiUrl` | Grafana Loki 地址 | `http://localhost:3100` | — |

#### 监控配置

| 配置项 | 说明 | 默认值 | 示例 |
|:------|:-----|:------|:----|
| `IsOpenTelemetry` | 启用 OpenTelemetry | `false` | `true` |
| `IsOpenTelemetryMetrics` | 启用指标收集 | `false` | `true` |
| `IsOpenTelemetryTracing` | 启用分布式追踪 | `false` | `true` |
| `MetricsPort` | Prometheus 指标端口 | `0`（复用 HTTP 端口） | `9090` |
| `IsMonitorMessageTimeOut` | 监控消息处理超时 | `false` | `true` |
| `MonitorMessageTimeOutSeconds` | 超时阈值（秒） | `1` | `5` |

#### ID 生成配置

| 配置项 | 说明 | 默认值 | 示例 |
|:------|:-----|:------|:----|
| `WorkerId` | 雪花 ID 工作节点 ID | `1` | `2` |
| `DataCenterId` | 雪花 ID 数据中心 ID | `1` | `2` |

#### 启动命令示例

```bash
# 最小启动参数
dotnet GameFrameX.Launcher.dll \
    --ServerType=Game \
    --ServerId=1000 \
    --DataBaseUrl=mongodb://127.0.0.1:27017 \
    --DataBaseName=game_db

# 完整启动参数
dotnet GameFrameX.Launcher.dll \
    --ServerType=Game \
    --ServerId=1000 \
    --ServerInstanceId=1 \
    --InnerHost=0.0.0.0 \
    --InnerPort=29100 \
    --OuterHost=0.0.0.0 \
    --OuterPort=29100 \
    --HttpPort=28080 \
    --IsEnableHttp=true \
    --HttpIsDevelopment=true \
    --IsEnableWebSocket=false \
    --DataBaseUrl=mongodb://127.0.0.1:27017 \
    --DataBaseName=gameframex \
    --IsDebug=true \
    --IsOpenTelemetry=true \
    --IsOpenTelemetryMetrics=true \
    --LogIsConsole=true \
    --LogIsWriteToFile=true
```

---

### 业务逻辑开发

#### 组件-代理模式

框架的核心设计模式是**状态-逻辑分离**，将持久化状态（Apps 层，不可热更）与业务逻辑（Hotfix 层，可热更）严格分离。

**1. 定义状态（Apps 层）**

```csharp
// GameFrameX.Apps/Player/BagState.cs
public class BagState : BaseCacheState
{
    public List<ItemData> Items { get; set; } = new List<ItemData>();
    public int MaxSlots { get; set; } = 50;
}
```

**2. 创建组件（Apps 层）**

```csharp
// GameFrameX.Apps/Player/BagComponent.cs
public class BagComponent : StateComponent<BagState>
{
    protected override async Task OnInit()
    {
        await base.OnInit();
        // 初始化组件状态
    }
}
```

**3. 实现业务逻辑（Hotfix 层）**

```csharp
// GameFrameX.Hotfix/Logic/Player/BagComponentAgent.cs
public class BagComponentAgent : StateComponentAgent<BagComponent, BagState>
{
    public async Task<bool> AddItem(int itemId, int count)
    {
        if (State.Items.Count >= State.MaxSlots)
        {
            return false;
        }

        var item = new ItemData { Id = itemId, Count = count };
        State.Items.Add(item);

        await Save();
        return true;
    }
}
```

**4. 访问组件代理**

```csharp
// 通过 ActorManager 获取组件代理
var bagAgent = await ActorManager.GetComponentAgent<BagComponentAgent>(playerId);
var result = await bagAgent.AddItem(1001, 10);
```

#### HTTP 处理器

HTTP 处理器继承 `BaseHttpHandler`，使用 `[HttpMessageMapping]` 特性注册路由。第 2/3 个构造参数为可选位置参数（request/response 类型，分别须继承 `HttpMessageRequestBase` / `HttpMessageResponseBase`）；仅声明响应类型时第二参数用 `null` 占位。未提供时走普通 JSON 路径，Swagger 的 data 使用通用对象。

```csharp
[HttpMessageMapping(typeof(GetPlayerInfoHttpHandler), typeof(GetPlayerInfoRequest), typeof(GetPlayerInfoResponse))]
[Description("获取玩家信息")]
public sealed class GetPlayerInfoHttpHandler : BaseHttpHandler
{
    public override async Task<MessageObject> ActionMessageObject(HttpActionContext context)
    {
        var playerRequest = (GetPlayerInfoRequest)context.MessageObject;
        var response = new GetPlayerInfoResponse();

        var agent = await ActorManager.GetComponentAgent<PlayerComponentAgent>(playerRequest.PlayerId);
        if (agent == null)
        {
            response.ErrorCode = (int)ResultCode.PlayerNotFound;
            return response;
        }

        response.PlayerInfo = await agent.GetPlayerInfo();
        return response;
    }
}
```

#### TCP/RPC 消息处理器

TCP 消息处理器负责处理客户端通过 TCP 连接发送的游戏消息。

**单向消息处理器：**

```csharp
[MessageMapping(typeof(ReqChatMessage))]
internal sealed class ChatMessageHandler : PlayerComponentHandler<ChatComponentAgent, ReqChatMessage>
{
    protected override async Task ActionAsync(ReqChatMessage request)
    {
        await ComponentAgent.ProcessChatMessage(request);
    }
}
```

**RPC 处理器（请求-响应）：**

```csharp
[MessageMapping(typeof(ReqAddItem))]
internal sealed class AddItemHandler : PlayerRpcComponentHandler<BagComponentAgent, ReqAddItem, RespAddItem>
{
    protected override async Task ActionAsync(ReqAddItem request, RespAddItem response)
    {
        try
        {
            // ComponentAgent 由基类自动注入
            await ComponentAgent.AddItem(request, response);
        }
        catch (Exception e)
        {
            LogHelper.Fatal(e);
            response.ErrorCode = (int)OperationStatusCode.InternalServerError;
        }
    }
}
```

#### 事件处理器

事件系统用于 Actor 之间的松耦合通信。

```csharp
[Event(typeof(PlayerLoginEventArgs))]
internal sealed class PlayerLoginEventHandler : EventListener<PlayerComponentAgent>
{
    protected override Task HandleEvent(PlayerComponentAgent agent, GameEventArgs gameEventArgs)
    {
        if (agent == null)
        {
            return Task.CompletedTask;
        }

        // 处理玩家登录事件
        return agent.OnLogin();
    }
}
```

---

### 热更新机制

#### 架构原理

热更新系统通过 `AssemblyLoadContext`（可回收）实现程序集的运行时加载和卸载：

```
┌───────────────────────────────────────────────────────┐
│  Apps 层（不可热更）                                     │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │ StateComponent│  │ StateComponent│  │ StateComponent│  │
│  │   持久化状态   │  │   持久化状态   │  │   持久化状态   │  │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘   │
│         │                │                │           │
├─────────┼────────────────┼────────────────┼───────────┤
│         ▼                ▼                ▼           │
│  Hotfix 层（可热更）— 通过 AssemblyLoadContext 加载     │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │ComponentAgent│  │ComponentAgent│  │ComponentAgent│  │
│  │   业务逻辑    │  │   业务逻辑    │  │   业务逻辑    │  │
│  └─────────────┘  └─────────────┘  └─────────────┘   │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │ Msg Handler  │  │ EventHandler│  │ HttpHandler  │   │
│  └─────────────┘  └─────────────┘  └─────────────┘   │
└───────────────────────────────────────────────────────┘
```

#### 热更新流程

1. **编译新逻辑**：构建更新后的 `GameFrameX.Hotfix.dll`
2. **部署程序集**：复制到服务器指定目录
3. **触发重载**：通过 HTTP 端点发起热更新请求
4. **程序集加载**：`HotfixManager` 使用可回收的 `AssemblyLoadContext` 加载新 DLL
5. **类型扫描**：`HotfixModule` 扫描新程序集中的代理、处理器、事件监听器
6. **代理切换**：`ActorManager.ClearAgent()` 清除缓存的代理实例
7. **优雅过渡**：旧程序集保留 10 分钟宽限期，等待进行中请求完成后卸载

#### 热更新 API

```bash
# 触发热更新（指定版本号）
curl -X POST "http://localhost:28080/game/api/Reload?version=1.7.2"
```

> 注：`Reload` 处理器已标记 `[RequireHttpSignature]`，请求必须通过 HTTP 签名校验后才会被接受。

---

### Docker 部署

#### 单实例部署

使用 `docker-compose.yml` 启动 MongoDB + Game 的最小环境（Social 后续补回）：

```bash
# 构建并启动
docker compose up -d --build

# 查看运行状态
docker compose ps

# 查看日志
docker compose logs -f game

# 停止
docker compose down
```

服务端口映射：

| 服务 | 容器内端口 | 宿主机端口 | 说明 |
|:----|:---------|:---------|:----|
| MongoDB | 27017 | 37017 | 数据库 |
| Game TCP | 29100 | 39100 | 游戏服务器 |
| Game HTTP | 28080 | 38080 | 游戏服务器 HTTP API |
| Online Admin | 28090 | 28090 | Online 平台管理 API（见下文） |

#### Online 平台管理 API

当 `IsEnableOnlineAdmin=true` 时，Game 进程会组装 Online Runtime（进程内能力库：基于内存存储的资产 / 会话 / 匹配 / 社交 / LiveOps 服务），并在独立的 Kestrel 监听器上暴露管理 HTTP API：

- 端点：`POST http://<server-host>:28090/online/admin/{action}`（全部 29 个管理动作，HTTP 状态码恒为 200；业务结果通过内层信封 `Code` 返回）
- 作用域三元组：每个请求携带 `TenantId` / `AppId` / `ServerId`；与授权的 `OnlineTenantId` / `OnlineAppId` / `ServerId` 不匹配的请求分别以 3002/3003/3004 拒绝，缺失三元组则以 3005 拒绝
- 在 GameFrameX Admin 控制台注册服务器：将大区的 `HttpManageUrl` 指向 `http://<server-host>:28090`（`online/admin` 前缀与动作名由 Admin 客户端按其通信协议自动追加）

#### 多实例部署

使用 `docker-compose.multi.yml` 启动包含 1 个 MongoDB + 2 个 Social + 10 个 Game 的集群环境：

```bash
# 构建并启动
docker compose -f docker-compose.multi.yml up -d --build

# 查看运行状态
docker compose -f docker-compose.multi.yml ps

# 停止
docker compose -f docker-compose.multi.yml down
```

集群拓扑：

| 组件 | 实例数 | 说明 |
|:----|:------|:----|
| MongoDB | 1 | 共享数据库 |
| Social | 2 | 社交服务器（social-1, social-2） |
| Game | 10 | 游戏服务器（game-1 ~ game-10） |

所有实例通过 Aspire 风格的环境变量进行服务发现：

```yaml
environment:
  services__Social_2001__tcp__0: "tcp://social-1:29400"
  services__Social_2002__tcp__0: "tcp://social-2:29401"
  services__Game_1001__tcp__0: "tcp://game-1:29100"
  # ...
```

#### 自定义构建

```bash
# 构建镜像
docker build -t gameframex/server:custom .

# 运行
docker run -d \
    --name my-game-server \
    -p 29100:29100 \
    -p 28080:28080 \
    gameframex/server:custom \
    --ServerType=Game \
    --ServerId=2000 \
    --DataBaseUrl=mongodb://mongo-host:27017 \
    --DataBaseName=my_game
```

---

### 多进程跨进程联调

#### 跨进程 Smoke 测试

```bash
# 确保多实例环境已启动
docker compose -f docker-compose.multi.yml up -d --build

# 执行跨进程冒烟测试
./scripts/multi/smoke-cross-process.sh
```

脚本验证内容：
- `game-1` → `social` 跨进程调用
- `game-2` → `social` 跨进程调用
- 返回 `code=0` 且 `FriendCount >= 1`

#### 机器人压测

模拟真实客户端反复"登录 → 在线 → 主动断开 → 重连登录"：

```bash
# 默认参数运行
./scripts/multi/run-bots-rpc.sh

# 自定义参数
BOT_COUNT=100 \
TCP_HOST=127.0.0.1 \
TCP_PORT=49100 \
LOGIN_URL=http://127.0.0.1:48080/game/api/ \
DISCONNECT_AFTER_LOGIN_SECONDS=15 \
RUN_SECONDS=600 \
./scripts/multi/run-bots-rpc.sh
```

可选环境变量：

| 变量 | 说明 | 默认值 |
|:----|:-----|:------|
| `BOT_COUNT` | 机器人数量 | `100` |
| `TCP_HOST` | TCP 连接主机 | `127.0.0.1` |
| `TCP_PORT` | TCP 连接端口 | `49100` |
| `LOGIN_URL` | 登录接口地址 | `http://127.0.0.1:48080/game/api/` |
| `SCENARIO` | 机器人场景 | `login` |
| `DISCONNECT_LOOP` | 是否循环执行断开/重连 | `true` |
| `DISCONNECT_AFTER_LOGIN_SECONDS` | 登录后断开延迟（秒） | `15` |
| `CONNECT_STAGGER_MS` | 机器人连接间隔（毫秒） | `10` |
| `RUN_SECONDS` | 总运行时长（秒） | `180` |

#### 常用排查命令

```bash
# 查看所有服务日志
docker compose -f docker-compose.multi.yml logs -f

# 查看指定服务日志
docker compose -f docker-compose.multi.yml logs -f game-1 game-2 social-1 social-2

# 重建并启动（代码变更后）
docker compose -f docker-compose.multi.yml up -d --build
```

---

### 监控与可观测性

#### 端点

| 端点 | 说明 |
|:----|:-----|
| `http://<host>:<HttpPort>/health` | 健康检查（Aspire 默认；另有 `/alive` 存活探针） |
| `http://<host>:<MetricsPort>/metrics` | Prometheus 指标 |

#### 指标分类

- **数据库**：操作延迟（`db_operation_latency_ms`）、重试次数（`db_open_retry_total`）、健康状态（`db_health_status`）
- **网络**：连接数、消息吞吐量、字节传输量
- **业务**：玩家登录数、活跃会话数
- **系统**：GC 性能、线程池状态

---

### 测试

#### 运行测试

```bash
# 运行所有测试
dotnet test

# 运行指定测试项目
dotnet test Tests/GameFrameX.Tests/GameFrameX.Tests.csproj

# 运行并显示详细输出
dotnet test --logger "console;verbosity=detailed"
```

#### 测试覆盖范围

测试套件基于 **xUnit** —— `Tests/GameFrameX.Tests` 覆盖框架层，`Tests/GameFrameX.Hotfix.Tests` 覆盖按 Role 划分的业务规则：

| 测试目录 | 说明 |
|:--------|:-----|
| `StartUp/` | 启动编排、多角色选择、All-in-One 选项、配置启动校验器、HTTP 路由注册 |
| `Architecture/` | Roslyn 架构分析器测试（分层规则、代理密封） |
| `Core/` | Actor 与会话管理测试（重复登录） |
| `NetWork/` | SuperSocket KCP 监听 / 鉴权 / 端到端测试、HTTP 与会话鉴权中间件测试 |
| `DataBase/` | MongoDB 与 PostgreSQL Provider 测试（查询、连接、多数据库、Provider 解析器） |
| `Discovery/` | 服务发现端点 / 路由集成测试（MongoDB 与 PostgreSQL） |
| `RemoteMessaging/` | 跨进程消息测试（编解码、传输） |
| `UnifiedMessaging/` | 统一跨进程消息测试 |
| `Online/` | Online Runtime 测试（管理 API、匹配、排行榜、赛季、锦标赛、LiveOps、在线状态、会话、惩罚、审计等） |
| `Topology/` | 进程拓扑同构等价性测试 |
| `Proto/` | ServerRole 消息域测试 |
| `ProtoBuff/` | Protobuf 序列化和对象池测试 |
| `Localization/` | 本地化键值解析测试 |
| `Client/` | 机器人客户端运行选项 / 传输分发测试 |
| `Utility/` | 数学/定点数测试、压缩、随机数、ID 生成、单例、设置 |
| `GameFrameX.Hotfix.Tests/` | 按 Role 划分的业务规则测试（Account / Login / Auth / Gateway / Chat / Mail / Friend / Team / Guild / Match / Room / Scene / World / Battle / Trade / Auction / Gm）及事件绑定测试 |

---

## 架构概览

```
┌─────────────────────────────────────────────────────────────────┐
│                          客户端层                                │
│         Unity3D / Godot / LayaBox / Cocos Creator               │
├─────────────────────────────────────────────────────────────────┤
│                          网络层                                  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │   TCP    │ │WebSocket │ │   HTTP   │ │   KCP    │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
├─────────────────────────────────────────────────────────────────┤
│                       消息处理层                                  │
│  ┌────────────────┐ ┌────────────────┐ ┌────────────────┐      │
│  │ TCP 消息处理器  │ │  HTTP 处理器   │ │ 跨进程消息路由  │      │
│  └────────────────┘ └────────────────┘ └────────────────┘      │
├─────────────────────────────────────────────────────────────────┤
│                       Actor 层                                   │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │ 玩家     │ │ 服务器   │ │  账户    │ │ 全局     │           │
│  │ Actor    │ │ Actor    │ │  Actor   │ │ Actor    │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
├─────────────────────────────────────────────────────────────────┤
│                    组件-代理层（热更新边界）                        │
│  ┌─────────────────────┐  ┌─────────────────────────────┐      │
│  │ Apps 层 (不可热更)   │  │ Hotfix 层 (可热更)           │      │
│  │ StateComponent<T>   │←→│ StateComponentAgent<T,TState>│      │
│  │ BaseCacheState          │  │ ComponentAgent               │      │
│  └─────────────────────┘  └─────────────────────────────┘      │
├─────────────────────────────────────────────────────────────────┤
│                       数据库层                                    │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │             MongoDB (default) / PostgreSQL              │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

---

### 项目结构

```
GameFrameX.Server.Source/
├── GameFrameX.Launcher/              # 应用入口点（Game + 18 个标准 Role 启动入口）
├── GameFrameX.StartUp/               # 启动编排和初始化
├── GameFrameX.Core/                  # 核心框架（Actor 系统、组件、事件、热更新管理）
├── GameFrameX.Apps/                  # 状态数据层（Account、Player、Game、ServerRole 模块）— 不可热更
├── GameFrameX.Hotfix/                # 业务逻辑层（HTTP、Player、Server 处理器）— 可热更
├── GameFrameX.Config/                # 游戏配置表（JSON 格式，LuBan 生成）
├── GameFrameX.Proto/                 # ProtoBuf 协议定义
├── GameFrameX.ProtoBuf.Net/          # ProtoBuf 序列化实现
├── GameFrameX.NetWork/               # 网络核心（TCP/UDP/KCP/WebSocket 通道、消息对象、发送器）
├── GameFrameX.NetWork.Abstractions/  # 网络接口（IMessage、IMessageHandler、消息映射）
├── GameFrameX.NetWork.HTTP/          # HTTP 服务器（Swagger、Kestrel、BaseHttpHandler）
├── GameFrameX.NetWork.RemoteMessaging/ # 跨进程远程消息（断路器、重试、一致性哈希）
├── GameFrameX.Discovery/             # Aspire 风格服务发现（services__{Role}__tcp__0 引导映射）
├── GameFrameX.DataBase/              # 数据库抽象层（多 Provider 注册表、GameDb 查询/更新/删除）
├── GameFrameX.DataBase.Mongo/        # MongoDB Provider（健康监控、重试、批量操作）
├── GameFrameX.DataBase.PostgreSql/   # PostgreSQL Provider（Npgsql，与 MongoDB Provider 同等容错能力）
├── GameFrameX.Online/                # Online 平台能力库（匹配、排行榜、社交、赛季、锦标赛、时间线、聊天审计、资产、LiveOps 等）
├── GameFrameX.Online.Runtime/        # 进程内 Online Runtime 宿主 + 管理 API
├── GameFrameX.Localization/          # 本地化系统（Keys.*.cs + .resx 资源文件）
├── GameFrameX.Utility/               # 工具集（设置、压缩、随机数、雪花 ID、对象池、Mapster、Harmony）
├── GameFrameX.Client/                # 测试客户端（TCP/KCP 机器人压测客户端）
├── GameFrameX.Architecture.Analyzers/         # Roslyn 架构分析器
├── GameFrameX.Hotfix.WrapperGenerator/ # Roslyn 源码生成器（热更新代理包装类）
└── Tests/
    ├── GameFrameX.Tests/             # xUnit 测试套件（框架层）
    └── GameFrameX.Hotfix.Tests/      # xUnit 测试套件（按 Role 划分的热更业务规则）
```

---

## 进程拓扑同构

服务器支持*进程拓扑同构*模型：同一组 Role 既可以按“每个 Role 一个进程”运行，也可以合并为单个
All-in-One 进程运行，而无需修改 Role 代码。

### 多角色启动

- `--ServerType=Game,Social` —— 在一个进程中启动多个已注册的 Role（按优先级顺序）。
- `--AllInOne` —— 在一个进程中启动所有已注册的 Role。
- `Configs/app_config.json` —— 为每个预定义 Role 提供一节配置（共 19 节：Game、Social 及其余 17 个标准 Role）。
  每个 Role 解析自己的配置节；进程级字段在各节之间必须一致。

启动时校验器（`ConfigStartupValidator`）会在以下情况快速失败（fail-fast）——并逐一列出冲突字段及其
来源配置节：

1. 通过 `--AllInOne` 或复数形式 `--ServerType` 选择的 Role 在 `app_config.json` 中没有对应配置节；
2. 同一进程中的两个 Role 会绑定相同的已启用、非零监听端点 —— 端点比较覆盖各端口字段与传输协议
   （TCP 与 UDP），因此例如 `Game.InnerPort == Social.HttpPort` 同样会快速失败；同一 Role 的
   `InnerPort`/`OuterPort` 共用是合法的同 Role 形式；
3. 所选配置节之间的进程级字段（`SettingFieldLevel(ProcessLevel)`，如 `DataBaseUrl`）不一致
   （按运行时归一化规则处理后），确保共享内核不会收到相互矛盾的进程配置；
4. 命令行显式传入的字段与正在使用的文件配置节不一致（文件配置节是唯一事实来源；命令行的 Role 级
   取值仅在无配置节的回退形式下生效）。“是否显式传入”由原始参数词元判定，因此传入与默认值相同的值
   （如 `--HttpPort=0`）也会参与比较。`ServerType`（配置节键）及 `IsAllInOne` / `IsSingleMode`
   开关豁免。

单 Role 与默认启动命令保持现有行为不变（缺失配置节时回退到启动器默认值）。

### Docker Compose 文件

| 文件 | 用途 |
|:--|:--|
| `docker-compose.development.yml` | 本地开发：仅一个 MongoDB 服务（宿主机端口 `127.0.0.1:37017`，与示例 `app_config.json` 匹配） |
| `docker-compose.multi.yml` | 多实例拓扑形态，由 `scripts/multi/generate-docker-compose-multi.py` **生成**；每个实例注入 `GameFrameX__AdvertiseHost` / `GameFrameX__AdvertisePort` / `GameFrameX__RoleInstanceId` 及 `services__{Role}__tcp__0` 静态引导映射，并将各自的 `Configs/multi/{service}.json` 配置节（取值与 `command` 参数一致）挂载为 `/app/Configs/app_config.json` |
| `docker-compose.multi.legacy.yml` | 先前的静态形态，作为过渡保留 |

修改生成器中的 `ROLES` 定义并重新运行 `python3 scripts/multi/generate-docker-compose-multi.py`
即可变更拓扑（幂等；`--check` 用于校验已提交的 compose 文件与各实例配置是否与生成器输出一致）。
未启用认证的 MongoDB 服务仅发布在 `127.0.0.1`；如需暴露给其他主机，请先启用 MongoDB 认证。

---

## 依赖

| 包 | 说明 |
|:--|:--|
| `GameFrameX.Foundation.*` | 本地化、日志、命令行配置、ORM 特性、哈希、HTTP 响应规范化、通用工具 |
| `GameFrameX.SuperSocket.Server` / `.ClientEngine` / `.Udp` / `.Kcp` / `.WebSocket.Server` | TCP、UDP、KCP、WebSocket 网络传输 |
| `MongoDB.Driver` | MongoDB 持久化驱动 |
| `Npgsql` | PostgreSQL 持久化驱动 |
| `OpenTelemetry.*` + `Grafana.OpenTelemetry` | 指标、分布式链路追踪、运行时插桩 |
| `OpenTelemetry.Exporter.Prometheus.AspNetCore` | Prometheus `/metrics` 抓取端点 |
| `Microsoft.Extensions.ServiceDiscovery` | Aspire 风格服务发现 |
| `Mapster` | 对象映射 |
| `Lib.Harmony` | 运行时方法补丁 |
| `Quartz` | 定时任务调度 |
| `Swashbuckle.AspNetCore.SwaggerGen` | HTTP API 的 Swagger 文档 |
| `xunit` | 单元测试框架 |

---

## 文档与资源

- [官方文档](https://gameframex.doc.alianblank.com/)
- [GitHub 仓库](https://github.com/GameFrameX)
- [Gitee 仓库](https://gitee.com/GameFrameX)
- [CNB 仓库](https://cnb.cool/GameFrameX)
- [Unity 客户端](https://github.com/GameFrameX/GameFrameX.Unity)
- [问题反馈](https://github.com/GameFrameX/GameFrameX/issues)
- [社区讨论](https://github.com/GameFrameX/GameFrameX/discussions)

---

## 社区与支持

![QQ](https://img.shields.io/badge/QQ-467608841%2F233840761-EB1923?style=for-the-badge&logo=qq&logoColor=white)
[![Bilibili](https://img.shields.io/badge/Bilibili-00A1D6?style=for-the-badge&logo=bilibili&logoColor=white)](https://www.bilibili.com/video/BV1yrpeepEn7)
[![Gitee](https://img.shields.io/badge/Gitee-C71D23?style=for-the-badge&logo=gitee&logoColor=white)](https://gitee.com/GameFrameX/gameframex)
[![GitHub](https://img.shields.io/badge/GitHub-181717?style=for-the-badge&logo=github&logoColor=white)](https://github.com/GameFrameX/gameframex)
[![Discord](https://img.shields.io/badge/Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/VDWUjWMDw9)
[<img src="https://cdn.jsdelivr.net/npm/devicon@2/icons/linkedin/linkedin-original.svg" height="28" alt="LinkedIn" />](https://www.linkedin.com/in/alianblank)
[![Reddit](https://img.shields.io/badge/Reddit-FF4500?style=for-the-badge&logo=reddit&logoColor=white)](https://www.reddit.com/r/GameFrameX/)
[![X](https://img.shields.io/badge/X-000000?style=for-the-badge&logo=x&logoColor=white)](https://x.com/alian_blank)
[![YouTube](https://img.shields.io/badge/YouTube-FF0000?style=for-the-badge&logo=youtube&logoColor=white)](https://www.youtube.com/channel/UCD9QhSFJ5xZkn5NTSV-DVAw)
[![Bluesky](https://img.shields.io/badge/Bluesky-0285FF?style=for-the-badge&logo=bluesky&logoColor=white)](https://bsky.app/profile/alianblank.bsky.social)

---

### 贡献指南

我们欢迎任何形式的贡献！请遵循以下步骤：

1. Fork 本仓库
2. 创建功能分支（`git checkout -b feature/amazing-feature`）
3. 提交更改（`git commit -m 'feat: 添加某个功能'`）
4. 推送到分支（`git push origin feature/amazing-feature`）
5. 创建 Pull Request

提交信息请遵循 [Angular 提交规范](https://www.conventionalcommits.org/zh-hans/)。

---

## 更新日志

GameFrameX Server 的版本更新历史请参阅 [CHANGELOG.md](CHANGELOG.md)。

---

## 开源协议

详见 [LICENSE](LICENSE) 文件。

<!--
EN: See [LICENSE](LICENSE) for license information.
zh-CN: 详见 [LICENSE](LICENSE) 文件。
zh-TW: 詳見 [LICENSE](LICENSE) 檔案。
ja: 詳しくは [LICENSE](LICENSE) をご参照ください。
ko: 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.
-->

---

<div align="center">

**如果这个项目对你有帮助，请给我们一个 Star**

**Made by GameFrameX Team**

</div>
