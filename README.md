<div align="center">

![GameFrameX Logo](https://download.alianblank.com/gameframex/gameframex_logo_320.png)

# GameFrameX Server

[![Version](https://img.shields.io/github/v/release/GameFrameX/GameFrameX.Server.Source?label=version&color=green)](https://github.com/GameFrameX/GameFrameX.Server.Source/releases)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License](https://img.shields.io/badge/license-MIT%20%7C%20Apache%202.0-blue.svg)](LICENSE)
[![Documentation](https://img.shields.io/badge/docs-gameframex.doc.alianblank.com-brightgreen.svg)](https://gameframex.doc.alianblank.com)

**High-Performance, Cross-Platform Game Server Framework**

[📖 Documentation](https://gameframex.doc.alianblank.com) • [🚀 Quick Start](#quick-start) • [💬 QQ Group: 467608841](https://qm.qq.com/cgi-bin/qm/qr?k=sYFd1nv6m2KZIWFLorZ5pBR0AE5ZhbuL&jump_from=webapi&authKey=oCu+uoL3n35fT5SEt7iLgGtROPxh31n/rHUxRlp0w1f+j38W4tKBuWyRH3KEdwHN)

---

🌐 **Language**: **English** | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [한국어](README.ko.md)

---

</div>

## Table of Contents

- [Introduction](#introduction)
- [Core Features](#core-features)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Quick Start](#quick-start)
- [Configuration Management](#configuration-management)
- [Business Logic Development](#business-logic-development)
- [Hot Update Mechanism](#hot-update-mechanism)
- [Docker Deployment](#docker-deployment)
- [Multi-Process Cross-Process Debugging](#multi-process-cross-process-debugging)
- [Monitoring & Observability](#monitoring--observability)
- [Testing](#testing)
- [Contributing](#contributing)
- [License](#license)
- [Related Links](#related-links)

---

## Introduction

GameFrameX Server is a high-performance, cross-platform game server framework built with C# .NET 10.0, designed with the Actor model and supporting hot update mechanisms. Designed for multiplayer online game development, it supports integration with various client platforms including Unity3D, Godot, and LayaBox.

**Design Philosophy**: Simplicity is the ultimate sophistication

## Core Features

### High-Performance Architecture

- **Actor Model**: Lock-free high-concurrency system built on TPL DataFlow, avoiding traditional lock performance overhead through message passing
- **Full Asynchronous Programming**: Complete async/await asynchronous programming model
- **Zero-Lock Design**: Actor internal state is accessed through message queue serialization, no locking required
- **Batch Persistence**: Supports batch database writes with configurable batch size and timeout
- **Snowflake ID Generation**: Built-in distributed unique ID generator with worker node and data center configuration

### Hot Update System

- **Zero-Downtime Updates**: Runtime loading of new logic assemblies without stopping the service
- **State-Logic Separation**: Strict separation between persistent state data (Apps layer) and hot-updatable business logic (Hotfix layer)
- **Graceful Transition**: Old assemblies retain a 10-minute grace period, waiting for in-progress requests to complete before unloading
- **Version Management**: Supports loading specific versions via HTTP endpoint

### Multi-Protocol Network Communication

- **TCP**: High-performance TCP server based on SuperSocket, primary game communication protocol
- **UDP**: Optional UDP protocol support
- **WebSocket**: Bidirectional communication based on SuperSocket WebSocket
- **HTTP/HTTPS**: HTTP service based on Kestrel, supporting Swagger documentation, CORS, health checks
- **KCP**: UDP reliable transport based on KCP protocol (experimental)
- **Cross-Process Messaging**: Built-in RemoteMessaging module with circuit breaker, retry strategy, and consistent hashing sharding

### Database & Persistence

- **MongoDB Primary Database**: Complete MongoDB integration with health state machine (Healthy → Degraded → Unhealthy → Recovering)
- **Transparent Persistence**: StateComponent automatic serialization/deserialization, persisted through timed batch ReplaceOne operations
- **Connection Pool Management**: Configurable connection pool and retry strategy
- **OpenTelemetry Integration**: Database operation metrics (latency, retry count, health status)

### Monitoring & Observability

- **OpenTelemetry**: Comprehensive metrics, tracing, and logging
- **Prometheus**: Native metrics export endpoint
- **Grafana Loki**: Log aggregation output support
- **Serilog**: Structured logging with console, file, and Loki multi-output

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         Client Layer                             │
│         Unity3D / Godot / LayaBox / Cocos Creator               │
├─────────────────────────────────────────────────────────────────┤
│                        Network Layer                             │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │   TCP    │ │WebSocket │ │   HTTP   │ │   KCP    │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
├─────────────────────────────────────────────────────────────────┤
│                     Message Processing Layer                     │
│  ┌────────────────┐ ┌────────────────┐ ┌────────────────┐      │
│  │TCP Msg Handlers│ │  HTTP Handlers │ │Cross-Proc Router│     │
│  └────────────────┘ └────────────────┘ └────────────────┘      │
├─────────────────────────────────────────────────────────────────┤
│                        Actor Layer                               │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │ Player   │ │  Server  │ │ Account  │ │  Global  │           │
│  │  Actor   │ │  Actor   │ │  Actor   │ │  Actor   │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
├─────────────────────────────────────────────────────────────────┤
│              Component-Agent Layer (Hot Update Boundary)          │
│  ┌─────────────────────┐  ┌─────────────────────────────┐      │
│  │  Apps Layer (Static) │  │ Hotfix Layer (Hot-updatable) │     │
│  │ StateComponent<T>   │←→│ StateComponentAgent<T,TState>│      │
│  │ CacheState          │  │ ComponentAgent               │      │
│  └─────────────────────┘  └─────────────────────────────┘      │
├─────────────────────────────────────────────────────────────────┤
│                       Database Layer                             │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                    MongoDB                               │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

---

## Project Structure

```
Server/
├── GameFrameX.Launcher/              # Application entry point
├── GameFrameX.StartUp/               # Startup orchestration and initialization
├── GameFrameX.Core/                  # Core framework (Actor system, components, events, hot update management)
├── GameFrameX.Apps/                  # State data layer (Account, Player, Server modules) — not hot-updatable
├── GameFrameX.Hotfix/                # Business logic layer (HTTP, Player, Server handlers) — hot-updatable
├── GameFrameX.Config/                # Game configuration tables (JSON format, generated by LuBan)
├── GameFrameX.Core.Config/           # Core configuration management
├── GameFrameX.Proto/                 # ProtoBuf protocol definitions
├── GameFrameX.ProtoBuf.Net/          # ProtoBuf serialization implementation
├── GameFrameX.NetWork/               # Network core (message objects, sender, WebSocket)
├── GameFrameX.NetWork.Abstractions/  # Network interfaces (IMessage, IMessageHandler, message mapping)
├── GameFrameX.NetWork.HTTP/          # HTTP server (Swagger, Kestrel, BaseHttpHandler)
├── GameFrameX.NetWork.Kcp/           # KCP protocol support (UDP-based reliable transport)
├── GameFrameX.NetWork.Message/       # Message pipeline and codec
├── GameFrameX.NetWork.RemoteMessaging/ # Cross-process remote messaging (circuit breaker, retry, consistent hashing)
├── GameFrameX.DataBase/              # Database abstraction layer
├── GameFrameX.DataBase.Mongo/        # MongoDB implementation (health monitoring, retry, batch operations)
├── GameFrameX.Localization/          # Localization system (Keys.*.cs + .resx resource files)
├── GameFrameX.Monitor/               # OpenTelemetry + Prometheus metrics integration
├── GameFrameX.Utility/               # Utilities (logging, compression, object pool, Mapster, Harmony)
├── GameFrameX.Client/                # Test client (TCP connection)
├── GameFrameX.CodeGenerator/         # Roslyn source generator (hot update proxy wrapper classes)
├── GameFrameX.AppHost/               # .NET Aspire application host
├── GameFrameX.AppHost.ServiceDefaults/ # Aspire shared defaults (OTel, service discovery)
└── Tests/
    └── GameFrameX.Tests/             # xUnit test suite
```

---

## Quick Start

### Requirements

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) only. .NET 8/9 are not supported.
- [MongoDB 4.x+](https://www.mongodb.com/try/download/community)
- Visual Studio 2022 or JetBrains Rider (recommended)

### Installation Steps

1. **Clone the Repository**
   ```bash
   git clone https://github.com/GameFrameX/GameFrameX.git
   cd GameFrameX/Server
   ```

2. **Restore Dependencies**
   ```bash
   dotnet restore
   ```

3. **Build the Project**
   ```bash
   dotnet build
   ```

4. **Start MongoDB**
   ```bash
   # Local installation
   mongod --dbpath /path/to/data

   # Or use Docker
   docker run -d -p 27017:27017 --name mongo mongo:8.2
   ```

5. **Run the Server**
   ```bash
   dotnet run --project GameFrameX.Launcher -- \
       --ServerType=Game \
       --ServerId=1000 \
       --OuterPort=29100 \
       --HttpPort=28080 \
       --DataBaseUrl=mongodb://127.0.0.1:27017 \
       --DataBaseName=gameframex
   ```

6. **Verify Startup**
   - Health check: `http://localhost:28080/game/api/health`
   - Check console logs to confirm successful startup

---

## Configuration Management

GameFrameX uses command-line arguments (`--Key=Value`) for configuration. All configuration items are defined in the `StartupOptions` class.

### Server Configuration

| Option | Description | Default | Example |
|:------|:------------|:--------|:--------|
| `ServerType` | Server type (required) | None | `Game`, `Social` |
| `ServerId` | Unique server ID | None | `1000` |
| `ServerInstanceId` | Server instance ID (distinguishes different instances of the same type) | `0` | `1001` |
| `IsSingleMode` | Single process mode | `false` | `true` |
| `MinModuleId` | Business module start ID (module sharding) | `0` | `100` |
| `MaxModuleId` | Business module end ID (module sharding) | `0` | `1000` |
| `TimeZone` | Server timezone | `Asia/Shanghai` | `UTC` |
| `IsUseTimeZone` | Enable custom timezone | `false` | `true` |
| `Language` | Language setting | None | `zh-CN` |

### Network Configuration

| Option | Description | Default | Example |
|:------|:------------|:--------|:--------|
| `InnerHost` | Internal communication IP (inter-cluster) | `0.0.0.0` | `0.0.0.0` |
| `InnerPort` | Internal communication port | `8888` | `29100` |
| `OuterHost` | External communication IP (client-facing) | `0.0.0.0` | `0.0.0.0` |
| `OuterPort` | External communication port | None | `29100` |
| `IsEnableTcp` | Enable TCP service | `true` | `true` |
| `IsEnableUdp` | Enable UDP service | `false` | `true` |
| `IsEnableWebSocket` | Enable WebSocket | `false` | `true` |
| `WsPort` | WebSocket port | `8889` | `29300` |
| `IsEnableHttp` | Enable HTTP service | `true` | `true` |
| `HttpPort` | HTTP service port | `8080` | `28080` |
| `HttpsPort` | HTTPS service port | None | `443` |
| `HttpUrl` | API root path | `/game/api/` | `/game/api/` |
| `HttpIsDevelopment` | HTTP development mode (enables Swagger) | `false` | `true` |

### Database Configuration

| Option | Description | Default | Example |
|:------|:------------|:--------|:--------|
| `DataBaseUrl` | MongoDB connection string | None | `mongodb://localhost:27017` |
| `DataBaseName` | Database name | None | `gameframex` |
| `DataBasePassword` | Database password | None | `your_password` |

### Actor Configuration

| Option | Description | Default | Example |
|:------|:------------|:--------|:--------|
| `ActorTimeOut` | Actor task execution timeout (ms) | `30000` | `60000` |
| `ActorQueueTimeOut` | Actor queue timeout (ms) | `30000` | `60000` |
| `ActorRecycleTime` | Actor idle recycle time (minutes) | `15` | `30` |
| `SaveDataInterval` | Data save interval (ms) | `30000` | `60000` |
| `SaveDataBatchCount` | Batch save count | `500` | `1000` |
| `SaveDataBatchTimeOut` | Batch save timeout (ms) | `30000` | `60000` |

### Logging Configuration

| Option | Description | Default | Example |
|:------|:------------|:--------|:--------|
| `IsDebug` | Debug logging master switch | `false` | `true` |
| `LogIsConsole` | Output to console | `true` | `false` |
| `LogIsWriteToFile` | Output to file | `true` | `false` |
| `LogEventLevel` | Log level | `Debug` | `Information` |
| `LogRollingInterval` | Log rolling interval | `Day` | `Hour` |
| `LogIsFileSizeLimit` | Limit single file size | `true` | `false` |
| `LogFileSizeLimitBytes` | File size limit | `104857600` (100MB) | `52428800` |
| `LogRetainedFileCountLimit` | Retained file count | `31` | `90` |
| `LogIsGrafanaLoki` | Output to Grafana Loki | `false` | `true` |
| `LogGrafanaLokiUrl` | Grafana Loki URL | `http://localhost:3100` | — |

### Monitoring Configuration

| Option | Description | Default | Example |
|:------|:------------|:--------|:--------|
| `IsOpenTelemetry` | Enable OpenTelemetry | `false` | `true` |
| `IsOpenTelemetryMetrics` | Enable metrics collection | `false` | `true` |
| `IsOpenTelemetryTracing` | Enable distributed tracing | `false` | `true` |
| `MetricsPort` | Prometheus metrics port | `0` (reuses HTTP port) | `9090` |
| `IsMonitorMessageTimeOut` | Monitor message processing timeout | `false` | `true` |
| `MonitorMessageTimeOutSeconds` | Timeout threshold (seconds) | `1` | `5` |

### ID Generation Configuration

| Option | Description | Default | Example |
|:------|:------------|:--------|:--------|
| `WorkerId` | Snowflake ID worker node ID | `1` | `2` |
| `DataCenterId` | Snowflake ID data center ID | `1` | `2` |

### Startup Command Examples

```bash
# Minimal startup parameters
dotnet GameFrameX.Launcher.dll \
    --ServerType=Game \
    --ServerId=1000 \
    --DataBaseUrl=mongodb://127.0.0.1:27017 \
    --DataBaseName=game_db

# Full startup parameters
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

## Business Logic Development

### Component-Agent Pattern

The core design pattern of the framework is **state-logic separation**, strictly separating persistent state (Apps layer, not hot-updatable) from business logic (Hotfix layer, hot-updatable).

**1. Define State (Apps Layer)**

```csharp
// GameFrameX.Apps/Player/BagState.cs
public class BagState : BaseCacheState
{
    public List<ItemData> Items { get; set; } = new List<ItemData>();
    public int MaxSlots { get; set; } = 50;
}
```

**2. Create Component (Apps Layer)**

```csharp
// GameFrameX.Apps/Player/BagComponent.cs
public class BagComponent : StateComponent<BagState>
{
    protected override async Task OnInit()
    {
        await base.OnInit();
        // Initialize component state
    }
}
```

**3. Implement Business Logic (Hotfix Layer)**

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

**4. Access Component Agent**

```csharp
// Get component agent via ActorManager
var bagAgent = await ActorManager.GetComponentAgent<BagComponentAgent>(playerId);
var result = await bagAgent.AddItem(1001, 10);
```

### HTTP Handler

HTTP handlers inherit `BaseHttpHandler` and use the `[HttpMessageMapping]` attribute to register routes.

```csharp
[HttpMessageMapping(typeof(GetPlayerInfoHandler))]
[Description("Get player info")]
public sealed class GetPlayerInfoHandler : BaseHttpHandler
{
    public override async Task<MessageObject> Action(
        string ip, string url,
        Dictionary<string, object> parameters,
        MessageObject messageObject)
    {
        var request = (GetPlayerInfoRequest)messageObject;
        var response = new GetPlayerInfoResponse();

        var agent = await ActorManager.GetComponentAgent<PlayerComponentAgent>(request.PlayerId);
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

### TCP/RPC Message Handler

TCP message handlers process game messages sent by clients via TCP connections.

**One-way Message Handler:**

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

**RPC Handler (Request-Response):**

```csharp
[MessageMapping(typeof(ReqAddItem))]
internal sealed class AddItemHandler : PlayerRpcComponentHandler<BagComponentAgent, ReqAddItem, RespAddItem>
{
    protected override async Task ActionAsync(ReqAddItem request, RespAddItem response)
    {
        try
        {
            // ComponentAgent is automatically injected by the base class
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

### Event Handler

The event system is used for loosely coupled communication between Actors.

```csharp
[Event(EventId.PlayerLogin)]
internal sealed class PlayerLoginEventHandler : EventListener<PlayerComponentAgent>
{
    protected override Task HandleEvent(PlayerComponentAgent agent, GameEventArgs gameEventArgs)
    {
        if (agent == null)
        {
            return Task.CompletedTask;
        }

        // Handle player login event
        return agent.OnLogin();
    }
}
```

---

## Hot Update Mechanism

### Architecture Principle

The hot update system implements runtime loading and unloading of assemblies through `AssemblyLoadContext` (collectible):

```
┌───────────────────────────────────────────────────────┐
│  Apps Layer (Not hot-updatable)                        │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │StateComponent│  │StateComponent│  │StateComponent│   │
│  │ Persist State│  │ Persist State│  │ Persist State│   │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘   │
│         │                │                │           │
├─────────┼────────────────┼────────────────┼───────────┤
│         ▼                ▼                ▼           │
│  Hotfix Layer (Hot-updatable) — Loaded via             │
│  AssemblyLoadContext                                    │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │ComponentAgent│  │ComponentAgent│  │ComponentAgent│   │
│  │Business Logic│  │Business Logic│  │Business Logic│   │
│  └─────────────┘  └─────────────┘  └─────────────┘   │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │ Msg Handler  │  │ EventHandler│  │ HttpHandler  │   │
│  └─────────────┘  └─────────────┘  └─────────────┘   │
└───────────────────────────────────────────────────────┘
```

### Hot Update Process

1. **Compile New Logic**: Build the updated `GameFrameX.Hotfix.dll`
2. **Deploy Assembly**: Copy to the server's specified directory
3. **Trigger Reload**: Initiate a hot update request via HTTP endpoint
4. **Assembly Loading**: `HotfixManager` loads the new DLL using a collectible `AssemblyLoadContext`
5. **Type Scanning**: `HotfixModule` scans the new assembly for agents, handlers, and event listeners
6. **Agent Switching**: `ActorManager.ClearAgent()` clears cached agent instances
7. **Graceful Transition**: Old assemblies retain a 10-minute grace period, waiting for in-progress requests to complete before unloading

### Hot Update API

```bash
# Trigger hot update (with specified version)
curl -X POST "http://localhost:28080/game/api/Reload?version=1.7.2"
```

---

## Docker Deployment

### Single Instance Deployment

Use `docker-compose.yml` to start a complete environment with MongoDB + Game + Social:

```bash
# Build and start
docker compose up -d --build

# View running status
docker compose ps

# View logs
docker compose logs -f game social

# Stop
docker compose down
```

Service port mapping:

| Service | Container Port | Host Port | Description |
|:--------|:--------------|:----------|:------------|
| MongoDB | 27017 | 37017 | Database |
| Game TCP | 29100 | 39100 | Game server |
| Game HTTP | 28080 | 38080 | Game server HTTP API |
| Social TCP | 29400 | 39400 | Social server |
| Social HTTP | 28081 | 38081 | Social server HTTP API |

### Multi-Instance Deployment

Use `docker-compose.multi.yml` to start a cluster with 1 MongoDB + 2 Social + 10 Game instances:

```bash
# Build and start
docker compose -f docker-compose.multi.yml up -d --build

# View running status
docker compose -f docker-compose.multi.yml ps

# Stop
docker compose -f docker-compose.multi.yml down
```

Cluster topology:

| Component | Instances | Description |
|:----------|:----------|:------------|
| MongoDB | 1 | Shared database |
| Social | 2 | Social servers (social-1, social-2) |
| Game | 10 | Game servers (game-1 ~ game-10) |

All instances use Aspire-style environment variables for service discovery:

```yaml
environment:
  services__Social_2001__tcp__0: "tcp://social-1:29400"
  services__Social_2002__tcp__0: "tcp://social-2:29401"
  services__Game_1001__tcp__0: "tcp://game-1:29100"
  # ...
```

### Custom Build

```bash
# Build image
docker build -t gameframex/server:custom .

# Run
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

## Multi-Process Cross-Process Debugging

### Cross-Process Smoke Test

```bash
# Ensure multi-instance environment is running
docker compose -f docker-compose.multi.yml up -d --build

# Execute cross-process smoke test
./scripts/multi/smoke-cross-process.sh
```

Script verification:
- `game-1` → `social` cross-process call
- `game-2` → `social` cross-process call
- Returns `code=0` and `FriendCount >= 1`

### Bot Stress Testing

Simulate real clients performing repeated "login → online → active disconnect → reconnect":

```bash
# Run with default parameters
./scripts/multi/run-bots-rpc.sh

# Custom parameters
BOT_COUNT=200 \
TCP_PORT=49100 \
LOGIN_URL=http://127.0.0.1:48080/game/api/ \
DISCONNECT_AFTER_LOGIN_SECONDS=20 \
RUN_SECONDS=300 \
./scripts/multi/run-bots-rpc.sh
```

Available environment variables:

| Variable | Description | Default |
|:---------|:------------|:--------|
| `BOT_COUNT` | Number of bots | — |
| `TCP_PORT` | TCP connection port | `49100` |
| `LOGIN_URL` | Login API URL | `http://127.0.0.1:48080/game/api/` |
| `DISCONNECT_AFTER_LOGIN_SECONDS` | Disconnect delay after login (seconds) | `20` |
| `RUN_SECONDS` | Total run duration (seconds) | `300` |

### Common Troubleshooting Commands

```bash
# View all service logs
docker compose -f docker-compose.multi.yml logs -f

# View specific service logs
docker compose -f docker-compose.multi.yml logs -f game-1 game-2 social-1 social-2

# Rebuild and start (after code changes)
docker compose -f docker-compose.multi.yml up -d --build
```

---

## Monitoring & Observability

### Endpoints

| Endpoint | Description |
|:---------|:------------|
| `http://<host>:<HttpPort>/game/api/health` | Health check |
| `http://<host>:<MetricsPort>/metrics` | Prometheus metrics |

### Metrics Categories

- **Database**: Operation latency (`db_operation_latency_ms`), retry count (`db_open_retry_total`), health status (`db_health_status`)
- **Network**: Connection count, message throughput, byte transfer volume
- **Business**: Player login count, active session count
- **System**: GC performance, thread pool status

---

## Testing

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test Tests/GameFrameX.Tests/GameFrameX.Tests.csproj

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"
```

### Test Coverage

The test project is based on **xUnit**, covering the following modules:

| Test Directory | Description |
|:--------------|:------------|
| `Utility/` | Math/fixed-point tests, compression, random, ID generation, singleton |
| `NetWork/Kcp/` | KCP pipeline filter, session management, server integration tests |
| `DataBase/` | MongoDB connection and query tests |
| `ProtoBuff/` | Protobuf serialization and object pool tests |
| `Localization/` | Localization key-value parsing tests |
| `RemoteMessaging/` | Cross-process messaging tests |
| `UnifiedMessaging/` | Unified cross-process messaging tests |
| `StartUp/` | HTTP server route registration tests |

---

## Contributing

We welcome all forms of contributions! Please follow these steps:

1. Fork this repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'feat: add some feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Create a Pull Request

Please follow the [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) specification for commit messages.

---

## License

This project is dual-licensed under the **MIT License** and **Apache License 2.0**. See the [LICENSE](LICENSE) file for details.

---

## Related Links

- [Official Documentation](https://gameframex.doc.alianblank.com/)
- [GitHub Repository](https://github.com/GameFrameX)
- [Gitee Repository](https://gitee.com/GameFrameX)
- [CNB Repository](https://cnb.cool/GameFrameX)
- [Unity Client](https://github.com/GameFrameX/GameFrameX.Unity)
- [Issue Tracker](https://github.com/GameFrameX/GameFrameX/issues)
- [Community Discussions](https://github.com/GameFrameX/GameFrameX/discussions)

---

<div align="center">

**If this project helps you, please give us a Star**

**Made by GameFrameX Team**

</div>
