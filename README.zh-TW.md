<div align="center">

![GameFrameX Logo](https://download.alianblank.com/gameframex/gameframex_logo_320.png)

# GameFrameX Server

[![Version](https://img.shields.io/github/v/release/GameFrameX/GameFrameX.Server.Source?label=version&color=green)](https://github.com/GameFrameX/GameFrameX.Server.Source/releases)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![Documentation](https://img.shields.io/badge/docs-gameframex.doc.alianblank.com-brightgreen.svg)](https://gameframex.doc.alianblank.com)

**高效能、跨平臺的遊戲伺服器框架**

[📖 文檔](https://gameframex.doc.alianblank.com) • [🚀 快速開始](#快速開始) • [💬 QQ群: 467608841](https://qm.qq.com/cgi-bin/qm/qr?k=sYFd1nv6m2KZIWFLorZ5pBR0AE5ZhbuL&jump_from=webapi&authKey=oCu+uoL3n35fT5SEt7iLgGtROPxh31n/rHUxRlp0w1f+j38W4tKBuWyRH3KEdwHN)

---

🌐 **語言**: [English](README.md) | [简体中文](README.zh-CN.md) | **繁體中文** | [日本語](README.ja.md) | [한국어](README.ko.md)

---

</div>

## 目錄

- [框架簡介](#框架簡介)
- [核心特性](#核心特性)
- [系統架構](#系統架構)
- [專案結構](#專案結構)
- [快速開始](#快速開始)
- [配置管理](#配置管理)
- [業務邏輯開發](#業務邏輯開發)
- [熱更新機制](#熱更新機制)
- [Docker 部署](#docker-部署)
- [多程序跨程序聯調](#多程序跨程序聯調)
- [監控與可觀測性](#監控與可觀測性)
- [測試](#測試)
- [貢獻指南](#貢獻指南)
- [許可證](#許可證)
- [相關連結](#相關連結)

---

## 框架簡介

GameFrameX Server 是基於 C# .NET 10.0 開發的高效能、跨平臺遊戲伺服器框架，採用 Actor 模型設計，支援熱更新機制。專為多人線上遊戲開發而設計，支援 Unity3D、Godot、LayaBox 等多種客戶端平臺整合。

**設計理念**：大道至簡，以簡化繁

## 核心特性

### 高效能架構

- **Actor 模型**：基於 TPL DataFlow 構建的無鎖高併發系統，透過訊息傳遞機制避免傳統鎖效能損耗
- **全非同步程式設計**：完整的 async/await 非同步程式設計模型
- **零鎖設計**：Actor 內部狀態透過訊息佇列序列化存取，無需加鎖
- **批次持久化**：支援批次資料庫寫入，可配置批次大小和逾時時間
- **雪花 ID 生成**：內建分散式唯一 ID 生成器，支援工作節點和資料中心配置

### 熱更新系統

- **零停機更新**：執行時載入新邏輯組件，無需停止服務
- **狀態邏輯分離**：持久化狀態資料（Apps 層）與可熱更業務邏輯（Hotfix 層）嚴格分離
- **優雅過渡**：舊組件保留 10 分鐘寬限期，等待進行中請求完成後卸載
- **版本管理**：支援透過 HTTP 端點指定版本號載入

### 多協議網路通訊

- **TCP**：基於 SuperSocket 的高效能 TCP 伺服器，主要遊戲通訊協議
- **UDP**：可選的 UDP 協議支援
- **WebSocket**：基於 SuperSocket WebSocket 的雙向通訊
- **HTTP/HTTPS**：基於 Kestrel 的 HTTP 服務，支援 Swagger 文件、CORS、健康檢查
- **KCP**：基於 KCP 協議的 UDP 可靠傳輸（實驗性）
- **跨程序訊息**：內建 RemoteMessaging 模組，支援斷路器、重試策略、一致性雜湊分片

### 資料庫與持久化

- **MongoDB 主資料庫**：完整的 MongoDB 整合，支援健康狀態機（Healthy → Degraded → Unhealthy → Recovering）
- **透明持久化**：StateComponent 自動序列化/反序列化，透過定時批次 ReplaceOne 操作持久化
- **連線池管理**：可配置的連線池和重試策略
- **OpenTelemetry 整合**：資料庫操作指標（延遲、重試次數、健康狀態）

### 監控與可觀測性

- **OpenTelemetry**：全面的指標（Metrics）、追蹤（Tracing）和日誌（Logging）
- **Prometheus**：原生指標匯出端點
- **Grafana Loki**：日誌聚合輸出支援
- **Serilog**：結構化日誌，支援控制檯、檔案、Loki 多輸出

---

## 系統架構

```
┌─────────────────────────────────────────────────────────────────┐
│                          客戶端層                                │
│         Unity3D / Godot / LayaBox / Cocos Creator               │
├─────────────────────────────────────────────────────────────────┤
│                          網路層                                  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │   TCP    │ │WebSocket │ │   HTTP   │ │   KCP    │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
├─────────────────────────────────────────────────────────────────┤
│                       訊息處理層                                  │
│  ┌────────────────┐ ┌────────────────┐ ┌────────────────┐      │
│  │ TCP 訊息處理器  │ │  HTTP 處理器   │ │ 跨程序訊息路由  │      │
│  └────────────────┘ └────────────────┘ └────────────────┘      │
├─────────────────────────────────────────────────────────────────┤
│                       Actor 層                                   │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │ 玩家     │ │ 伺服器   │ │  帳戶    │ │ 全域     │           │
│  │ Actor    │ │ Actor    │ │  Actor   │ │ Actor    │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
├─────────────────────────────────────────────────────────────────┤
│                    元件-代理層（熱更新邊界）                        │
│  ┌─────────────────────┐  ┌─────────────────────────────┐      │
│  │ Apps 層 (不可熱更)   │  │ Hotfix 層 (可熱更)           │      │
│  │ StateComponent<T>   │←→│ StateComponentAgent<T,TState>│      │
│  │ CacheState          │  │ ComponentAgent               │      │
│  └─────────────────────┘  └─────────────────────────────┘      │
├─────────────────────────────────────────────────────────────────┤
│                       資料庫層                                    │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                    MongoDB                               │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

---

## 專案結構

```
Server/
├── GameFrameX.Launcher/              # 應用入口點
├── GameFrameX.StartUp/               # 啟動編排和初始化
├── GameFrameX.Core/                  # 核心框架（Actor 系統、元件、事件、熱更新管理）
├── GameFrameX.Apps/                  # 狀態資料層（帳戶、玩家、伺服器模組）— 不可熱更
├── GameFrameX.Hotfix/                # 業務邏輯層（HTTP、玩家、伺服器處理器）— 可熱更
├── GameFrameX.Config/                # 遊戲配置表（JSON 格式，LuBan 生成）
├── GameFrameX.Core.Config/           # 核心配置管理
├── GameFrameX.Proto/                 # ProtoBuf 協議定義
├── GameFrameX.ProtoBuf.Net/          # ProtoBuf 序列化實作
├── GameFrameX.NetWork/               # 網路核心（訊息物件、傳送器、WebSocket）
├── GameFrameX.NetWork.Abstractions/  # 網路介面（IMessage、IMessageHandler、訊息映射）
├── GameFrameX.NetWork.HTTP/          # HTTP 伺服器（Swagger、Kestrel、BaseHttpHandler）
├── GameFrameX.NetWork.Kcp/           # KCP 協議支援（基於 UDP 的可靠傳輸）
├── GameFrameX.NetWork.Message/       # 訊息管道和編解碼
├── GameFrameX.NetWork.RemoteMessaging/ # 跨程序遠端訊息（斷路器、重試、一致性雜湊）
├── GameFrameX.DataBase/              # 資料庫抽象層
├── GameFrameX.DataBase.Mongo/        # MongoDB 實作（健康監控、重試、批次操作）
├── GameFrameX.Localization/          # 本地化系統（Keys.*.cs + .resx 資源檔案）
├── GameFrameX.Monitor/               # OpenTelemetry + Prometheus 指標整合
├── GameFrameX.Utility/               # 工具集（日誌、壓縮、物件池、Mapster、Harmony）
├── GameFrameX.Client/                # 測試客戶端（TCP 連線）
├── GameFrameX.Architecture.Analyzers/         # Roslyn 架構分析器
├── GameFrameX.Hotfix.WrapperGenerator/ # Roslyn 原始碼生成器（熱更新代理包裝類別）
├── GameFrameX.AppHost/               # .NET Aspire 應用主機
├── GameFrameX.AppHost.ServiceDefaults/ # Aspire 共享預設配置（OTel、服務發現）
└── Tests/
    └── GameFrameX.Tests/             # xUnit 測試套件
```

---

## 快速開始

### 環境要求

- 僅支援 [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，不支援 .NET 8/9。
- [MongoDB 4.x+](https://www.mongodb.com/try/download/community)
- Visual Studio 2022 或 JetBrains Rider（推薦）

### 安裝步驟

1. **複製儲存庫**
   ```bash
   git clone https://github.com/GameFrameX/GameFrameX.git
   cd GameFrameX/Server
   ```

2. **還原相依套件**
   ```bash
   dotnet restore
   ```

3. **建置專案**
   ```bash
   dotnet build
   ```

4. **啟動 MongoDB**
   ```bash
   # 本地安裝方式
   mongod --dbpath /path/to/data

   # 或使用 Docker
   docker run -d -p 27017:27017 --name mongo mongo:8.2
   ```

5. **執行伺服器**
   ```bash
   dotnet run --project GameFrameX.Launcher -- \
       --ServerType=Game \
       --ServerId=1000 \
       --OuterPort=29100 \
       --HttpPort=28080 \
       --DataBaseUrl=mongodb://127.0.0.1:27017 \
       --DataBaseName=gameframex
   ```

6. **驗證啟動**
   - 健康檢查：`http://localhost:28080/game/api/health`
   - 檢視控制檯日誌確認啟動成功

---

## 配置管理

GameFrameX 使用命令列參數 (`--Key=Value`) 進行配置，所有配置項定義在 `StartupOptions` 類別中。

### 伺服器配置

| 配置項 | 說明 | 預設值 | 範例 |
|:------|:-----|:------|:----|
| `ServerType` | 伺服器類型（必填） | 無 | `Game`、`Social` |
| `ServerId` | 伺服器唯一標識 ID | 無 | `1000` |
| `ServerInstanceId` | 伺服器實例 ID（區分同類型不同實例） | `0` | `1001` |
| `IsSingleMode` | 是否單程序模式 | `false` | `true` |
| `MinModuleId` | 業務模組起始 ID（模組分片） | `0` | `100` |
| `MaxModuleId` | 業務模組結束 ID（模組分片） | `0` | `1000` |
| `TimeZone` | 伺服器時區 | `Asia/Shanghai` | `UTC` |
| `IsUseTimeZone` | 是否啟用自訂時區 | `false` | `true` |
| `Language` | 語言設定 | 無 | `zh-CN` |

### 網路配置

| 配置項 | 說明 | 預設值 | 範例 |
|:------|:-----|:------|:----|
| `InnerHost` | 內部通訊 IP（叢集間） | `0.0.0.0` | `0.0.0.0` |
| `InnerPort` | 內部通訊埠 | `8888` | `29100` |
| `OuterHost` | 外部通訊 IP（面向客戶端） | `0.0.0.0` | `0.0.0.0` |
| `OuterPort` | 外部通訊埠 | 無 | `29100` |
| `IsEnableTcp` | 是否啟用 TCP 服務 | `true` | `true` |
| `IsEnableUdp` | 是否啟用 UDP 服務 | `false` | `true` |
| `IsEnableWebSocket` | 是否啟用 WebSocket | `false` | `true` |
| `WsPort` | WebSocket 埠 | `8889` | `29300` |
| `IsEnableHttp` | 是否啟用 HTTP 服務 | `true` | `true` |
| `HttpPort` | HTTP 服務埠 | `8080` | `28080` |
| `HttpsPort` | HTTPS 服務埠 | 無 | `443` |
| `HttpUrl` | API 介面根路徑 | `/game/api/` | `/game/api/` |
| `HttpIsDevelopment` | HTTP 開發模式（啟用 Swagger） | `false` | `true` |

### 資料庫配置

| 配置項 | 說明 | 預設值 | 範例 |
|:------|:-----|:------|:----|
| `DataBaseUrl` | MongoDB 連線字串 | 無 | `mongodb://localhost:27017` |
| `DataBaseName` | 資料庫名稱 | 無 | `gameframex` |
| `DataBasePassword` | 資料庫密碼 | 無 | `your_password` |

### Actor 配置

| 配置項 | 說明 | 預設值 | 範例 |
|:------|:-----|:------|:----|
| `ActorTimeOut` | Actor 任務執行逾時（毫秒） | `30000` | `60000` |
| `ActorQueueTimeOut` | Actor 佇列逾時（毫秒） | `30000` | `60000` |
| `ActorRecycleTime` | Actor 閒置回收時間（分鐘） | `15` | `30` |
| `SaveDataInterval` | 資料儲存間隔（毫秒） | `30000` | `60000` |
| `SaveDataBatchCount` | 批次儲存數量 | `500` | `1000` |
| `SaveDataBatchTimeOut` | 批次儲存逾時（毫秒） | `30000` | `60000` |

### 日誌配置

| 配置項 | 說明 | 預設值 | 範例 |
|:------|:-----|:------|:----|
| `IsDebug` | 除錯日誌總開關 | `false` | `true` |
| `LogIsConsole` | 輸出到控制檯 | `true` | `false` |
| `LogIsWriteToFile` | 輸出到檔案 | `true` | `false` |
| `LogEventLevel` | 日誌級別 | `Debug` | `Information` |
| `LogRollingInterval` | 日誌滾動間隔 | `Day` | `Hour` |
| `LogIsFileSizeLimit` | 限制單個檔案大小 | `true` | `false` |
| `LogFileSizeLimitBytes` | 檔案大小限制 | `104857600` (100MB) | `52428800` |
| `LogRetainedFileCountLimit` | 保留檔案數量 | `31` | `90` |
| `LogIsGrafanaLoki` | 輸出到 Grafana Loki | `false` | `true` |
| `LogGrafanaLokiUrl` | Grafana Loki 位址 | `http://localhost:3100` | — |

### 監控配置

| 配置項 | 說明 | 預設值 | 範例 |
|:------|:-----|:------|:----|
| `IsOpenTelemetry` | 啟用 OpenTelemetry | `false` | `true` |
| `IsOpenTelemetryMetrics` | 啟用指標收集 | `false` | `true` |
| `IsOpenTelemetryTracing` | 啟用分散式追蹤 | `false` | `true` |
| `MetricsPort` | Prometheus 指標埠 | `0`（複用 HTTP 埠） | `9090` |
| `IsMonitorMessageTimeOut` | 監控訊息處理逾時 | `false` | `true` |
| `MonitorMessageTimeOutSeconds` | 逾時閾值（秒） | `1` | `5` |

### ID 生成配置

| 配置項 | 說明 | 預設值 | 範例 |
|:------|:-----|:------|:----|
| `WorkerId` | 雪花 ID 工作節點 ID | `1` | `2` |
| `DataCenterId` | 雪花 ID 資料中心 ID | `1` | `2` |

### 啟動命令範例

```bash
# 最小啟動參數
dotnet GameFrameX.Launcher.dll \
    --ServerType=Game \
    --ServerId=1000 \
    --DataBaseUrl=mongodb://127.0.0.1:27017 \
    --DataBaseName=game_db

# 完整啟動參數
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

## 業務邏輯開發

### 元件-代理模式

框架的核心設計模式是**狀態-邏輯分離**，將持久化狀態（Apps 層，不可熱更）與業務邏輯（Hotfix 層，可熱更）嚴格分離。

**1. 定義狀態（Apps 層）**

```csharp
// GameFrameX.Apps/Player/BagState.cs
public class BagState : BaseCacheState
{
    public List<ItemData> Items { get; set; } = new List<ItemData>();
    public int MaxSlots { get; set; } = 50;
}
```

**2. 建立元件（Apps 層）**

```csharp
// GameFrameX.Apps/Player/BagComponent.cs
public class BagComponent : StateComponent<BagState>
{
    protected override async Task OnInit()
    {
        await base.OnInit();
        // 初始化元件狀態
    }
}
```

**3. 實作業務邏輯（Hotfix 層）**

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

**4. 存取元件代理**

```csharp
// 透過 ActorManager 取得元件代理
var bagAgent = await ActorManager.GetComponentAgent<BagComponentAgent>(playerId);
var result = await bagAgent.AddItem(1001, 10);
```

### HTTP 處理器

HTTP 處理器繼承 `BaseHttpHandler`，使用 `[HttpMessageMapping]` 特性註冊路由。

```csharp
[HttpMessageMapping(typeof(GetPlayerInfoHandler))]
[Description("取得玩家資訊")]
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

### TCP/RPC 訊息處理器

TCP 訊息處理器負責處理客戶端透過 TCP 連線傳送的遊戲訊息。

**單向訊息處理器：**

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

**RPC 處理器（請求-回應）：**

```csharp
[MessageMapping(typeof(ReqAddItem))]
internal sealed class AddItemHandler : PlayerRpcComponentHandler<BagComponentAgent, ReqAddItem, RespAddItem>
{
    protected override async Task ActionAsync(ReqAddItem request, RespAddItem response)
    {
        try
        {
            // ComponentAgent 由基類自動注入
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

### 事件處理器

事件系統用於 Actor 之間的鬆耦合通訊。

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

        // 處理玩家登入事件
        return agent.OnLogin();
    }
}
```

---

## 熱更新機制

### 架構原理

熱更新系統透過 `AssemblyLoadContext`（可回收）實作組件的執行時載入和卸載：

```
┌───────────────────────────────────────────────────────┐
│  Apps 層（不可熱更）                                     │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │ StateComponent│  │ StateComponent│  │ StateComponent│  │
│  │   持久化狀態   │  │   持久化狀態   │  │   持久化狀態   │  │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘   │
│         │                │                │           │
├─────────┼────────────────┼────────────────┼───────────┤
│         ▼                ▼                ▼           │
│  Hotfix 層（可熱更）— 透過 AssemblyLoadContext 載入     │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │ComponentAgent│  │ComponentAgent│  │ComponentAgent│  │
│  │   業務邏輯    │  │   業務邏輯    │  │   業務邏輯    │  │
│  └─────────────┘  └─────────────┘  └─────────────┘   │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │ Msg Handler  │  │ EventHandler│  │ HttpHandler  │   │
│  └─────────────┘  └─────────────┘  └─────────────┘   │
└───────────────────────────────────────────────────────┘
```

### 熱更新流程

1. **編譯新邏輯**：建置更新後的 `GameFrameX.Hotfix.dll`
2. **部署組件**：複製到伺服器指定目錄
3. **觸發重新載入**：透過 HTTP 端點發起熱更新請求
4. **組件載入**：`HotfixManager` 使用可回收的 `AssemblyLoadContext` 載入新 DLL
5. **型別掃描**：`HotfixModule` 掃描新組件中的代理、處理器和事件監聽器
6. **代理切換**：`ActorManager.ClearAgent()` 清除快取的代理實例
7. **優雅過渡**：舊組件保留 10 分鐘寬限期，等待進行中請求完成後卸載

### 熱更新 API

```bash
# 觸發熱更新（指定版本號）
curl -X POST "http://localhost:28080/game/api/Reload?version=1.7.2"
```

---

## Docker 部署

### 單實例部署

使用 `docker-compose.yml` 啟動包含 MongoDB + Game + Social 的完整環境：

```bash
# 建置並啟動
docker compose up -d --build

# 檢視執行狀態
docker compose ps

# 檢視日誌
docker compose logs -f game social

# 停止
docker compose down
```

服務埠映射：

| 服務 | 容器內埠 | 宿主機埠 | 說明 |
|:----|:---------|:---------|:----|
| MongoDB | 27017 | 37017 | 資料庫 |
| Game TCP | 29100 | 39100 | 遊戲伺服器 |
| Game HTTP | 28080 | 38080 | 遊戲伺服器 HTTP API |
| Social TCP | 29400 | 39400 | 社交伺服器 |
| Social HTTP | 28081 | 38081 | 社交伺服器 HTTP API |

### 多實例部署

使用 `docker-compose.multi.yml` 啟動包含 1 個 MongoDB + 2 個 Social + 10 個 Game 的叢集環境：

```bash
# 建置並啟動
docker compose -f docker-compose.multi.yml up -d --build

# 檢視執行狀態
docker compose -f docker-compose.multi.yml ps

# 停止
docker compose -f docker-compose.multi.yml down
```

叢集拓撲：

| 元件 | 實例數 | 說明 |
|:----|:------|:----|
| MongoDB | 1 | 共享資料庫 |
| Social | 2 | 社交伺服器（social-1, social-2） |
| Game | 10 | 遊戲伺服器（game-1 ~ game-10） |

所有實例透過 Aspire 風格的環境變數進行服務發現：

```yaml
environment:
  services__Social_2001__tcp__0: "tcp://social-1:29400"
  services__Social_2002__tcp__0: "tcp://social-2:29401"
  services__Game_1001__tcp__0: "tcp://game-1:29100"
  # ...
```

### 自訂建置

```bash
# 建置映像
docker build -t gameframex/server:custom .

# 執行
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

## 多程序跨程序聯調

### 跨程序 Smoke 測試

```bash
# 確保多實例環境已啟動
docker compose -f docker-compose.multi.yml up -d --build

# 執行跨程序冒煙測試
./scripts/multi/smoke-cross-process.sh
```

指令碼驗證內容：
- `game-1` → `social` 跨程序呼叫
- `game-2` → `social` 跨程序呼叫
- 回傳 `code=0` 且 `FriendCount >= 1`

### 機器人壓力測試

模擬真實客戶端反覆「登入 → 線上 → 主動斷開 → 重連登入」：

```bash
# 預設參數執行
./scripts/multi/run-bots-rpc.sh

# 自訂參數
BOT_COUNT=200 \
TCP_PORT=49100 \
LOGIN_URL=http://127.0.0.1:48080/game/api/ \
DISCONNECT_AFTER_LOGIN_SECONDS=20 \
RUN_SECONDS=300 \
./scripts/multi/run-bots-rpc.sh
```

可選環境變數：

| 變數 | 說明 | 預設值 |
|:----|:-----|:------|
| `BOT_COUNT` | 機器人數量 | — |
| `TCP_PORT` | TCP 連線埠 | `49100` |
| `LOGIN_URL` | 登入介面位址 | `http://127.0.0.1:48080/game/api/` |
| `DISCONNECT_AFTER_LOGIN_SECONDS` | 登入後斷開延遲（秒） | `20` |
| `RUN_SECONDS` | 總執行時長（秒） | `300` |

### 常用排查命令

```bash
# 檢視所有服務日誌
docker compose -f docker-compose.multi.yml logs -f

# 檢視指定服務日誌
docker compose -f docker-compose.multi.yml logs -f game-1 game-2 social-1 social-2

# 重建並啟動（程式碼變更後）
docker compose -f docker-compose.multi.yml up -d --build
```

---

## 監控與可觀測性

### 端點

| 端點 | 說明 |
|:----|:-----|
| `http://<host>:<HttpPort>/game/api/health` | 健康檢查 |
| `http://<host>:<MetricsPort>/metrics` | Prometheus 指標 |

### 指標分類

- **資料庫**：操作延遲（`db_operation_latency_ms`）、重試次數（`db_open_retry_total`）、健康狀態（`db_health_status`）
- **網路**：連線數、訊息吞吐量、位元組傳輸量
- **業務**：玩家登入數、活躍工作階段數
- **系統**：GC 效能、執行緒池狀態

---

## 測試

### 執行測試

```bash
# 執行所有測試
dotnet test

# 執行指定測試專案
dotnet test Tests/GameFrameX.Tests/GameFrameX.Tests.csproj

# 執行並顯示詳細輸出
dotnet test --logger "console;verbosity=detailed"
```

### 測試覆蓋範圍

測試專案基於 **xUnit**，覆蓋以下模組：

| 測試目錄 | 說明 |
|:--------|:-----|
| `Utility/` | 數學/定點數測試、壓縮、隨機數、ID 生成、單例 |
| `NetWork/Kcp/` | KCP 管道過濾器、工作階段管理、伺服器整合測試 |
| `DataBase/` | MongoDB 連線和查詢測試 |
| `ProtoBuff/` | Protobuf 序列化和物件池測試 |
| `Localization/` | 本地化鍵值解析測試 |
| `RemoteMessaging/` | 跨程序訊息測試 |
| `UnifiedMessaging/` | 統一跨程序訊息測試 |
| `StartUp/` | HTTP 伺服器路由註冊測試 |

---

## 貢獻指南

我們歡迎任何形式的貢獻！請遵循以下步驟：

1. Fork 本儲存庫
2. 建立功能分支（`git checkout -b feature/amazing-feature`）
3. 提交變更（`git commit -m 'feat: 新增某個功能'`）
4. 推送到分支（`git push origin feature/amazing-feature`）
5. 建立 Pull Request

提交資訊請遵循 [Angular 提交規範](https://www.conventionalcommits.org/zh-Hans/)。

---

## 許可證

本專案採用 **Apache License 2.0** 許可證。詳見 [LICENSE](LICENSE) 檔案。

---

## 相關連結

- [官方文件](https://gameframex.doc.alianblank.com/)
- [GitHub 儲存庫](https://github.com/GameFrameX)
- [Gitee 儲存庫](https://gitee.com/GameFrameX)
- [CNB 儲存庫](https://cnb.cool/GameFrameX)
- [Unity 客戶端](https://github.com/GameFrameX/GameFrameX.Unity)
- [問題回饋](https://github.com/GameFrameX/GameFrameX/issues)
- [社群討論](https://github.com/GameFrameX/GameFrameX/discussions)

---

<div align="center">

**如果這個專案對你有幫助，請給我們一個 Star**

**Made by GameFrameX Team**

</div>
