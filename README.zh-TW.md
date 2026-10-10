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

**獨立遊戲前後端一體化解決方案 · 獨立遊戲開發者的圓夢大使**

<br />

[文檔](https://gameframex.doc.alianblank.com) · [快速開始](#快速開始) · QQ群: 467608841 / 233840761

<br />

[English](README.md) | [简体中文](README.zh-CN.md) | **繁體中文** | [日本語](README.ja.md) | [한국어](README.ko.md)

</div>

## 目錄

- [項目簡介](#項目簡介)
  - [功能特性](#功能特性)
- [快速開始](#快速開始)
  - [環境要求](#環境要求)
  - [安裝](#安裝)
- [使用範例](#使用範例)
  - [配置管理](#配置管理)
  - [業務邏輯開發](#業務邏輯開發)
  - [熱更新機制](#熱更新機制)
  - [Docker 部署](#docker-部署)
  - [多程序跨程序聯調](#多程序跨程序聯調)
  - [監控與可觀測性](#監控與可觀測性)
  - [測試](#測試)
- [架構概覽](#架構概覽)
  - [專案結構](#專案結構)
- [程序拓撲同構](#程序拓撲同構)
  - [多角色啟動](#多角色啟動)
  - [Docker Compose 檔案](#docker-compose-檔案)
- [依賴](#依賴)
- [文檔與資源](#文檔與資源)
- [社區與支援](#社區與支援)
  - [貢獻指南](#貢獻指南)
- [更新日誌](#更新日誌)
- [開源協議](#開源協議)

---

## 項目簡介

GameFrameX Server 是基於 C# .NET 10.0 開發的高效能、跨平臺遊戲伺服器框架，採用 Actor 模型設計，支援熱更新機制。專為多人線上遊戲開發而設計，支援 Unity3D、Godot、LayaBox 等多種客戶端平臺整合。

**設計理念**：大道至簡，以簡化繁

---

### 功能特性

#### 高效能架構

- **Actor 模型**：基於 TPL DataFlow 構建的無鎖高併發系統，透過訊息傳遞機制避免傳統鎖效能損耗
- **全非同步程式設計**：完整的 async/await 非同步程式設計模型
- **零鎖設計**：Actor 內部狀態透過訊息佇列序列化存取，無需加鎖
- **批次持久化**：支援批次資料庫寫入，可配置批次大小和逾時時間
- **雪花 ID 生成**：內建分散式唯一 ID 生成器，支援工作節點和資料中心配置

#### 熱更新系統

- **零停機更新**：執行時載入新邏輯組件，無需停止服務
- **狀態邏輯分離**：持久化狀態資料（Apps 層）與可熱更業務邏輯（Hotfix 層）嚴格分離
- **優雅過渡**：舊組件保留 10 分鐘寬限期，等待進行中請求完成後卸載
- **版本管理**：支援透過 HTTP 端點指定版本號載入

#### 多協議網路通訊

- **TCP**：基於 SuperSocket 的高效能 TCP 伺服器，主要遊戲通訊協議
- **UDP**：可選的 UDP 協議支援
- **WebSocket**：基於 SuperSocket WebSocket 的雙向通訊
- **HTTP/HTTPS**：基於 Kestrel 的 HTTP 服務，支援 Swagger 文件、CORS、健康檢查
+- **KCP**：正式支援（opt-in）的 UDP 可靠傳輸；`KcpPort` 為 `0` 時與 TCP 共用埠
- **跨程序訊息**：內建 RemoteMessaging 模組，支援斷路器、重試策略、一致性雜湊分片

#### 資料庫與持久化

- **雙資料庫 Provider**：`DatabaseProvider` 可選 `Mongo`（預設）或 `PostgreSql`，兩者共用同一 `GameFrameX.DataBase` 抽象層
- **MongoDB Provider**：完整的 MongoDB 整合，支援健康狀態機（Healthy → Degraded → Unhealthy → Recovering）
- **PostgreSQL Provider**：基於 Npgsql，具備與 MongoDB Provider 同等的重試 / 復原 / 可用性 / 健康檢查能力
- **透明持久化**：StateComponent 自動序列化/反序列化，透過定時批次 upsert 操作持久化
- **連線池管理**：可配置的連線池和重試策略
- **OpenTelemetry 整合**：資料庫操作指標（延遲、重試次數、健康狀態）

#### 監控與可觀測性

- **OpenTelemetry**：全面的指標（Metrics）、追蹤（Tracing）和日誌（Logging）
- **Prometheus**：原生指標匯出端點
- **Grafana Loki**：日誌聚合輸出支援
- **Serilog**：結構化日誌，支援控制檯、檔案、Loki 多輸出

---

## 快速開始

### 環境要求

- 僅支援 [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，不支援 .NET 8/9。
- [MongoDB 4.x+](https://www.mongodb.com/try/download/community)（預設 Provider；亦可透過 `--DatabaseProvider=PostgreSql` 使用 PostgreSQL）
- Visual Studio 2022 或 JetBrains Rider（推薦）

### 安裝

1. **複製儲存庫**
   ```bash
   git clone https://github.com/GameFrameX/GameFrameX.Server.Source.git
   cd GameFrameX.Server.Source
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
   - 健康檢查：`http://localhost:28080/health`（存活探針：`/alive`）
   - 檢視控制檯日誌確認啟動成功

---

## 使用範例

以下範例涵蓋從配置、業務邏輯、熱更新到部署與偵錯的完整開發流程。

---

### 配置管理

GameFrameX 使用命令列參數 (`--Key=Value`) 進行配置，所有配置項定義在 `StartupOptions` 類別中。

#### 伺服器配置

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

#### 網路配置

| 配置項 | 說明 | 預設值 | 範例 |
|:------|:-----|:------|:----|
| `InnerHost` | 內部通訊 IP（叢集間） | `0.0.0.0` | `0.0.0.0` |
| `InnerPort` | 內部通訊埠 | `8888` | `29100` |
| `OuterHost` | 外部通訊 IP（面向客戶端） | `0.0.0.0` | `0.0.0.0` |
| `OuterPort` | 外部通訊埠 | 無 | `29100` |
| `IsEnableTcp` | 是否啟用 TCP 服務 | `true` | `true` |
| `IsEnableUdp` | 是否啟用 UDP 服務 | `false` | `true` |
| `IsEnableKcp` | 是否啟用 KCP 服務（僅配置檔案） | `false` | `true` |
| `KcpPort` | KCP 埠（`0` 時與 TCP 共用埠） | `0` | `29120` |
| `IsEnableWebSocket` | 是否啟用 WebSocket | `false` | `true` |
| `WsPort` | WebSocket 埠 | `8889` | `29300` |
| `IsEnableHttp` | 是否啟用 HTTP 服務 | `true` | `true` |
| `HttpPort` | HTTP 服務埠 | `8080` | `28080` |
| `HttpsPort` | HTTPS 服務埠 | 無 | `443` |
| `HttpUrl` | API 介面根路徑 | `/game/api/` | `/game/api/` |
| `HttpIsDevelopment` | HTTP 開發模式（啟用 Swagger） | `false` | `true` |

#### 資料庫配置

| 配置項 | 說明 | 預設值 | 範例 |
|:------|:-----|:------|:----|
| `DatabaseProvider` | 資料庫 Provider | `Mongo` | `PostgreSql` |
| `DataBaseUrl` | 資料庫連線字串 | 無 | `mongodb://localhost:27017` |
| `DataBaseName` | 資料庫名稱 | 無 | `gameframex` |
| `DataBasePassword` | 資料庫密碼 | 無 | `your_password` |

#### Actor 配置

| 配置項 | 說明 | 預設值 | 範例 |
|:------|:-----|:------|:----|
| `ActorTimeOut` | Actor 任務執行逾時（毫秒） | `30000` | `60000` |
| `ActorQueueTimeOut` | Actor 佇列逾時（毫秒） | `30000` | `60000` |
| `ActorRecycleTime` | Actor 閒置回收時間（分鐘） | `15` | `30` |
| `SaveDataInterval` | 資料儲存間隔（毫秒） | `30000` | `60000` |
| `SaveDataBatchCount` | 批次儲存數量 | `500` | `1000` |
| `SaveDataBatchTimeOut` | 批次儲存逾時（毫秒） | `30000` | `60000` |

#### 日誌配置

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

#### 監控配置

| 配置項 | 說明 | 預設值 | 範例 |
|:------|:-----|:------|:----|
| `IsOpenTelemetry` | 啟用 OpenTelemetry | `false` | `true` |
| `IsOpenTelemetryMetrics` | 啟用指標收集 | `false` | `true` |
| `IsOpenTelemetryTracing` | 啟用分散式追蹤 | `false` | `true` |
| `MetricsPort` | Prometheus 指標埠 | `0`（複用 HTTP 埠） | `9090` |
| `IsMonitorMessageTimeOut` | 監控訊息處理逾時 | `false` | `true` |
| `MonitorMessageTimeOutSeconds` | 逾時閾值（秒） | `1` | `5` |

#### ID 生成配置

| 配置項 | 說明 | 預設值 | 範例 |
|:------|:-----|:------|:----|
| `WorkerId` | 雪花 ID 工作節點 ID | `1` | `2` |
| `DataCenterId` | 雪花 ID 資料中心 ID | `1` | `2` |

#### 啟動命令範例

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

### 業務邏輯開發

#### 元件-代理模式

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

#### HTTP 處理器

HTTP 處理器繼承 `BaseHttpHandler`，使用 `[HttpMessageMapping]` 特性註冊路由。第 2/3 個建構參數為可選位置參數（request/response 型別，分別須繼承 `HttpMessageRequestBase` / `HttpMessageResponseBase`）；僅宣告回應型別時第二參數用 `null` 佔位。未提供時走普通 JSON 路徑，Swagger 的 data 使用通用物件。

```csharp
[HttpMessageMapping(typeof(GetPlayerInfoHttpHandler), typeof(GetPlayerInfoRequest), typeof(GetPlayerInfoResponse))]
[Description("取得玩家資訊")]
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

#### TCP/RPC 訊息處理器

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

#### 事件處理器

事件系統用於 Actor 之間的鬆耦合通訊。

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

        // 處理玩家登入事件
        return agent.OnLogin();
    }
}
```

---

### 熱更新機制

#### 架構原理

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

#### 熱更新流程

1. **編譯新邏輯**：建置更新後的 `GameFrameX.Hotfix.dll`
2. **部署組件**：複製到伺服器指定目錄
3. **觸發重新載入**：透過 HTTP 端點發起熱更新請求
4. **組件載入**：`HotfixManager` 使用可回收的 `AssemblyLoadContext` 載入新 DLL
5. **型別掃描**：`HotfixModule` 掃描新組件中的代理、處理器和事件監聽器
6. **代理切換**：`ActorManager.ClearAgent()` 清除快取的代理實例
7. **優雅過渡**：舊組件保留 10 分鐘寬限期，等待進行中請求完成後卸載

#### 熱更新 API

```bash
# 觸發熱更新（指定版本號）
curl -X POST "http://localhost:28080/game/api/Reload?version=1.7.2"
```

> 注意：`Reload` 處理器標記了 `[RequireHttpSignature]`，請求必須通過 HTTP 簽名校驗才會被接受。

---

### Docker 部署

#### 單實例部署

使用 `docker-compose.yml` 啟動包含 MongoDB + Game 的最小環境（Social 後續補回）：

```bash
# 建置並啟動
docker compose up -d --build

# 檢視執行狀態
docker compose ps

# 檢視日誌
docker compose logs -f game

# 停止
docker compose down
```

服務埠映射：

| 服務 | 容器內埠 | 宿主機埠 | 說明 |
|:----|:---------|:---------|:----|
| MongoDB | 27017 | 37017 | 資料庫 |
| Game TCP | 29100 | 39100 | 遊戲伺服器 |
| Game HTTP | 28080 | 38080 | 遊戲伺服器 HTTP API |
| Online Admin | 28090 | 28090 | Online 平台管理 API（見下文） |


#### Online 平台管理 API

當 `IsEnableOnlineAdmin=true` 時，Game 程序會組裝 Online Runtime（程序內能力庫：基於記憶體儲存的資產 / 工作階段 / 配對 / 社交 / LiveOps 服務），並在獨立的 Kestrel 監聽器上暴露管理 HTTP API：

- 端點：`POST http://<server-host>:28090/online/admin/{action}`（全部 29 個管理動作，HTTP 狀態碼恆為 200；業務結果透過內層封套 `Code` 傳回）
- 作用域三元組：每個請求攜帶 `TenantId` / `AppId` / `ServerId`；與授權的 `OnlineTenantId` / `OnlineAppId` / `ServerId` 不匹配的請求分別以 3002/3003/3004 拒絕，缺少三元組則以 3005 拒絕
- 在 GameFrameX Admin 控制檯註冊伺服器：將區服的 `HttpManageUrl` 指向 `http://<server-host>:28090`（`online/admin` 前綴與動作名由 Admin 客戶端依其線路約定附加）

#### 多實例部署

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

#### 自訂建置

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

### 多程序跨程序聯調

#### 跨程序 Smoke 測試

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

#### 機器人壓力測試

模擬真實客戶端反覆「登入 → 線上 → 主動斷開 → 重連登入」：

```bash
# 預設參數執行
./scripts/multi/run-bots-rpc.sh

# 自訂參數
BOT_COUNT=200 \
TCP_PORT=49100 \
LOGIN_URL=http://127.0.0.1:48080/game/api/ \
DISCONNECT_AFTER_LOGIN_SECONDS=20 \
RUN_SECONDS=600 \
./scripts/multi/run-bots-rpc.sh
```

可選環境變數：

| 變數 | 說明 | 預設值 |
|:----|:-----|:------|
| `BOT_COUNT` | 機器人數量 | `100` |
| `TCP_HOST` | TCP 連線主機 | `127.0.0.1` |
| `TCP_PORT` | TCP 連線埠 | `49100` |
| `LOGIN_URL` | 登入介面位址 | `http://127.0.0.1:48080/game/api/` |
| `SCENARIO` | 機器人場景 | `login` |
| `DISCONNECT_LOOP` | 是否重複斷開/重連循環 | `true` |
| `DISCONNECT_AFTER_LOGIN_SECONDS` | 登入後斷開延遲（秒） | `15` |
| `CONNECT_STAGGER_MS` | 機器人連線間隔（毫秒） | `10` |
| `RUN_SECONDS` | 總執行時長（秒） | `180` |

#### 常用排查命令

```bash
# 檢視所有服務日誌
docker compose -f docker-compose.multi.yml logs -f

# 檢視指定服務日誌
docker compose -f docker-compose.multi.yml logs -f game-1 game-2 social-1 social-2

# 重建並啟動（程式碼變更後）
docker compose -f docker-compose.multi.yml up -d --build
```

---

### 監控與可觀測性

#### 端點

| 端點 | 說明 |
|:----|:-----|
| `http://<host>:<HttpPort>/health` | 健康檢查（Aspire 預設；另有 `/alive` 存活探針） |
| `http://<host>:<MetricsPort>/metrics` | Prometheus 指標 |

#### 指標分類

- **資料庫**：操作延遲（`db_operation_latency_ms`）、重試次數（`db_open_retry_total`）、健康狀態（`db_health_status`）
- **網路**：連線數、訊息吞吐量、位元組傳輸量
- **業務**：玩家登入數、活躍工作階段數
- **系統**：GC 效能、執行緒池狀態

---

### 測試

#### 執行測試

```bash
# 執行所有測試
dotnet test

# 執行指定測試專案
dotnet test Tests/GameFrameX.Tests/GameFrameX.Tests.csproj

# 執行並顯示詳細輸出
dotnet test --logger "console;verbosity=detailed"
```

#### 測試覆蓋範圍

測試套件基於 **xUnit** — `Tests/GameFrameX.Tests` 覆蓋框架層，`Tests/GameFrameX.Hotfix.Tests` 覆蓋按 Role 劃分的業務規則：

| 測試目錄 | 說明 |
|:--------|:-----|
| `StartUp/` | 啟動編排、多角色選擇、All-in-One 選項、配置啟動驗證器、HTTP 路由註冊 |
| `Architecture/` | Roslyn 架構分析器測試（分層規則、代理密封） |
| `Core/` | Actor 與工作階段管理測試（重複登入） |
| `Network/` | SuperSocket KCP 監聽 / 驗證 / 端到端測試，HTTP 與工作階段驗證中介軟體測試 |
| `DataBase/` | MongoDB 與 PostgreSQL Provider 測試（查詢、連線、多資料庫、Provider 解析器） |
| `Discovery/` | 服務發現端點 / 路由整合測試（MongoDB 與 PostgreSQL） |
| `RemoteMessaging/` | 跨程序訊息測試（編解碼、傳輸） |
| `UnifiedMessaging/` | 統一跨程序訊息測試 |
| `Online/` | Online Runtime 測試（管理 API、配對、排行榜、賽季、錦標賽、LiveOps、在線狀態、工作階段、懲罰、稽核等） |
| `Topology/` | 程序拓撲同構等價性測試 |
| `Proto/` | ServerRole 訊息領域測試 |
| `ProtoBuff/` | Protobuf 序列化和物件池測試 |
| `Localization/` | 本地化鍵值解析測試 |
| `Client/` | 機器人客戶端執行選項 / 傳輸分發測試 |
| `Utility/` | 數學/定點數測試、壓縮、隨機數、ID 生成、單例、設定 |
| `GameFrameX.Hotfix.Tests/` | 按 Role 劃分的業務規則測試（Account / Login / Auth / Gateway / Chat / Mail / Friend / Team / Guild / Match / Room / Scene / World / Battle / Trade / Auction / Gm）及事件綁定測試 |

---

## 架構概覽

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
│  │ BaseCacheState          │  │ ComponentAgent               │      │
│  └─────────────────────┘  └─────────────────────────────┘      │
├─────────────────────────────────────────────────────────────────┤
│                       資料庫層                                    │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │             MongoDB (default) / PostgreSQL              │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

---

### 專案結構

```
GameFrameX.Server.Source/
├── GameFrameX.Launcher/              # 應用程式入口（Game + 18 個標準 Role 啟動入口）
├── GameFrameX.StartUp/               # 啟動編排和初始化
├── GameFrameX.Core/                  # 核心框架（Actor 系統、元件、事件、熱更新管理）
├── GameFrameX.Apps/                  # 狀態資料層（Account、Player、Game、ServerRole 模組）— 不可熱更
├── GameFrameX.Hotfix/                # 業務邏輯層（HTTP、Player、Server 處理器）— 可熱更
├── GameFrameX.Config/                # 遊戲配置表（JSON 格式，LuBan 生成）
├── GameFrameX.Proto/                 # ProtoBuf 協議定義
├── GameFrameX.ProtoBuf.Net/          # ProtoBuf 序列化實作
├── GameFrameX.Network/               # 網路核心（TCP/UDP/KCP/WebSocket 通道、訊息物件、傳送器）
├── GameFrameX.Network.Abstractions/  # 網路介面（IMessage、IMessageHandler、訊息映射）
├── GameFrameX.Network.HTTP/          # HTTP 伺服器（Swagger、Kestrel、BaseHttpHandler）
├── GameFrameX.Network.RemoteMessaging/ # 跨程序遠端訊息（斷路器、重試、一致性雜湊）
├── GameFrameX.Discovery/             # Aspire 風格服務發現（services__{Role}__tcp__0 引導映射）
├── GameFrameX.DataBase/              # 資料庫抽象層（多 Provider 註冊表、GameDb 查詢/更新/刪除）
├── GameFrameX.DataBase.Mongo/        # MongoDB Provider（健康監控、重試、批次操作）
├── GameFrameX.DataBase.PostgreSql/   # PostgreSQL Provider（Npgsql，具備與 MongoDB Provider 同等的韌性）
├── GameFrameX.Online/                # Online 平台能力庫（配對、排行榜、社交、賽季、錦標賽、時間軸、聊天稽核、資產、LiveOps 等）
├── GameFrameX.Online.Runtime/        # 程序內 Online Runtime 宿主 + 管理 API
├── GameFrameX.Localization/          # 本地化系統（Keys.*.cs + .resx 資源檔案）
├── GameFrameX.Utility/               # 工具集（設定、壓縮、隨機數、雪花 ID、物件池、Mapster、Harmony）
├── GameFrameX.Client/                # 測試客戶端（TCP/KCP 機器人壓測客戶端）
├── GameFrameX.Architecture.Analyzers/         # Roslyn 架構分析器
├── GameFrameX.Hotfix.WrapperGenerator/ # Roslyn 原始碼生成器（熱更新代理包裝類別）
└── Tests/
    ├── GameFrameX.Tests/             # xUnit 測試套件（框架層）
    └── GameFrameX.Hotfix.Tests/      # xUnit 測試套件（按 Role 劃分的 Hotfix 業務規則）
```

---

## 程序拓撲同構

伺服器支援「程序拓撲同構」模型：同一組角色可以按每個角色一個程序執行，也可以合併為單一 All-in-One 程序執行，且無需修改角色程式碼。

### 多角色啟動

- `--ServerType=Game,Social` — 在單一程序中啟動多個已註冊角色（按優先順序）。
- `--AllInOne` — 在單一程序中啟動全部已註冊角色。
- `Configs/app_config.json` — 為每個預定義 Role 提供一節配置（共 19 節：Game、Social 及其他 17 個標準角色）。
  每個角色解析自己的配置節；程序級欄位在各節之間必須一致。

啟動時的驗證器（`ConfigStartupValidator`）會 fail-fast —— 列出所有衝突欄位及其來源配置節 —— 觸發條件如下：

1. 透過 `--AllInOne` 或複數形式 `--ServerType` 選擇的角色在 `app_config.json` 中沒有對應配置節；
2. 同一程序內兩個角色會綁定相同的已啟用、非零監聽端點 —— 端點會跨埠欄位與傳輸方式（TCP vs UDP）比較，
   例如 `Game.InnerPort == Social.HttpPort` 也會 fail-fast；同一角色的 `InnerPort`/`OuterPort` 共用則是合法的同角色形式；
3. 程序級欄位（`SettingFieldLevel(ProcessLevel)`，如 `DataBaseUrl`）在所選配置節之間不一致（經執行階段正規化規則處理後），
   確保共享核心不會收到相互矛盾的程序配置；
4. 命令列顯式提供的欄位與正在使用的檔案配置節不一致（檔案配置節是唯一事實來源；CLI 角色級數值僅在無配置節的回退形式下生效）。
   「是否顯式提供」依原始參數 token 判斷，因此傳入與預設值相同的值（如 `--HttpPort=0`）仍會參與比較。
   `ServerType`（配置節鍵）與 `IsAllInOne` / `IsSingleMode` 開關豁免。

單角色與預設啟動命令保持現有行為（缺少配置節時回退到啟動器預設值）。

### Docker Compose 檔案

| 檔案 | 用途 |
|:--|:--|
| `docker-compose.development.yml` | 本地開發：單一 MongoDB 服務（宿主埠 `127.0.0.1:37017`，與範例 `app_config.json` 一致） |
| `docker-compose.multi.yml` | 多實例拓撲形式，由 `scripts/multi/generate-docker-compose-multi.py` **生成**；每個實例注入 `GameFrameX__AdvertiseHost` / `GameFrameX__AdvertisePort` / `GameFrameX__RoleInstanceId` 以及 `services__{Role}__tcp__0` 靜態引導映射，並掛載各自的 `Configs/multi/{service}.json` 配置節（數值與 `command` 參數一致）至 `/app/Configs/app_config.json` |
| `docker-compose.multi.legacy.yml` | 先前的靜態形式，作為過渡保留 |

修改拓撲的方式：編輯產生器中的 `ROLES` 定義並重新執行
`python3 scripts/multi/generate-docker-compose-multi.py`（冪等；`--check` 可驗證已提交的
compose 檔案與各實例配置是否與產生器輸出一致）。未啟用驗證的 MongoDB
服務僅發布在 `127.0.0.1`；若要對其他主機暴露，請先啟用 MongoDB 驗證。

---

## 依賴

| 套件 | 說明 |
|:--|:--|
| `GameFrameX.Foundation.*` | 本地化、日誌、命令列配置、ORM 特性、雜湊、HTTP 回應正規化、通用工具 |
| `GameFrameX.SuperSocket.Server` / `.ClientEngine` / `.Udp` / `.Kcp` / `.WebSocket.Server` | TCP、UDP、KCP、WebSocket 網路傳輸 |
| `MongoDB.Driver` | MongoDB 持久化驅動 |
| `Npgsql` | PostgreSQL 持久化驅動 |
| `OpenTelemetry.*` + `Grafana.OpenTelemetry` | 指標、分散式鏈路追蹤、執行階段插樁 |
| `OpenTelemetry.Exporter.Prometheus.AspNetCore` | Prometheus `/metrics` 抓取端點 |
| `Microsoft.Extensions.ServiceDiscovery` | Aspire 風格服務發現 |
| `Mapster` | 物件映射 |
| `Lib.Harmony` | 執行階段方法修補 |
| `Quartz` | 排程任務調度 |
| `Swashbuckle.AspNetCore.SwaggerGen` | HTTP API 的 Swagger 文件 |
| `xunit` | 單元測試框架 |

---

## 文檔與資源

- [官方文件](https://gameframex.doc.alianblank.com/)
- [GitHub 儲存庫](https://github.com/GameFrameX)
- [Gitee 儲存庫](https://gitee.com/GameFrameX)
- [CNB 儲存庫](https://cnb.cool/GameFrameX)
- [Unity 客戶端](https://github.com/GameFrameX/GameFrameX.Unity)
- [問題回饋](https://github.com/GameFrameX/GameFrameX/issues)
- [社群討論](https://github.com/GameFrameX/GameFrameX/discussions)

---

## 社區與支援

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

### 貢獻指南

我們歡迎任何形式的貢獻！請遵循以下步驟：

1. Fork 本儲存庫
2. 建立功能分支（`git checkout -b feature/amazing-feature`）
3. 提交變更（`git commit -m 'feat: 新增某個功能'`）
4. 推送到分支（`git push origin feature/amazing-feature`）
5. 建立 Pull Request

提交資訊請遵循 [Angular 提交規範](https://www.conventionalcommits.org/zh-Hans/)。

---

## 更新日誌

請參閱 [CHANGELOG.md](CHANGELOG.md) 了解 GameFrameX Server 的版本歷程。

---

## 開源協議

詳見 [LICENSE](LICENSE) 檔案。

<!--
EN: See [LICENSE](LICENSE) for license information.
zh-CN: 详见 [LICENSE](LICENSE) 文件。
zh-TW: 詳見 [LICENSE](LICENSE) 檔案。
ja: 詳しくは [LICENSE](LICENSE) をご参照ください。
ko: 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.
-->

---

<div align="center">

**如果這個專案對你有幫助，請給我們一個 Star**

**Made by GameFrameX Team**

</div>
