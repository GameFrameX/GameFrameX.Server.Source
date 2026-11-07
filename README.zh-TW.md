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

## 項目簡介

GameFrameX Server 是基於 C# .NET 10 的單進程遊戲伺服器框架。一個宿主承載用戶端連線、本地玩家工作階段、Actor、熱更新邏輯與可選能力模組；目前遊戲及 Online 業務模組完整保留。

### 功能特性

- **Actor 與元件**：循序執行、狀態持久化、事件、計時器及產生的代理包裝。
- **Hotfix 熱更新**：可載入的業務組件與本地生命週期管理，狀態定義與業務邏輯分層。
- **用戶端網路**：TCP、WebSocket（WS/WSS）、KCP、請求回應訊息、工作階段驗證與斷線清理。
- **HTTP/HTTPS**：處理器、簽章驗證、TLS 及可選開發文件。
- **資料庫**：MongoDB、SQLite、PostgreSQL 三種介接，保留具名連線、條件寫入、交易與重試，語義依各 Provider 支援範圍而定。
- **Capability.Hosting**：本進程註冊、相依解析、排程、模組生命週期及通用管理 API。
- **配套能力**：設定載入、序列化、在地化、診斷、架構分析器與測試。

## 快速開始

### 安裝

使用 `global.json` 指定的 .NET 10 SDK，在儲存庫根目錄執行以下命令。啟動前將 `Configs/app_config.json` 設為單一宿主；下方資料庫欄位屬於應用設定。SQLite 不需獨立資料庫服務，MongoDB 與 PostgreSQL 需要可存取的資料庫實例。複製命令適用 macOS/Linux，Windows 請使用對應命令複製相同目錄。

```bash
dotnet restore Server.slnx
dotnet build Server.slnx --no-restore --no-incremental -warnaserror:CS9057
dotnet build GameFrameX.Hotfix/GameFrameX.Hotfix.csproj -o bin/app_debug/hotfix --no-restore
cp -R GameFrameX.Config/json bin/app_debug/
cp -R Configs bin/app_debug/
cd bin/app_debug
dotnet GameFrameX.Launcher.dll
```

在應用設定中選擇資料庫。使用 `Mongo` 搭配 MongoDB 連線字串，或 `PostgreSql` 搭配 PostgreSQL 連線字串，即可選擇其他介接。`DataBaseName` 指定連線/資料庫名稱。`GameDatabase` 及資料庫登錄表繼續支援具名連線；切換 Provider 不會遷移既有資料。

```json
{
  "DatabaseProvider": "Sqlite",
  "DataBaseUrl": "Data Source=data/gameframex.db",
  "DataBaseName": "gameframex"
}
```

SQLite 檔案模式將資料持久化到設定路徑，`Data Source=:memory:` 是易失記憶體資料庫。SQLite 循序寫入，部分查詢在記憶體中求值，大型查詢可能掃描整張表。MongoDB 交易需要副本集；PostgreSQL 使用 EF Core Provider。查詢與交易語義須以對應契約測試核驗。

## 使用範例

### 用戶端連線與 HTTP

TCP 使用 `IsEnableTcp`、`OuterHost`、`OuterPort`；WS 使用 `IsEnableWebSocket`、`WsPort`；WSS 另需 `WssPort`、`WssCertFilePath`。PEM 憑證設定 `WssCertificateKeyPath`，受保護金鑰使用 `WssCertificatePassword`。用戶端 KCP 使用 `IsEnableKcp`、`KcpPort`。

HTTP 使用 `IsEnableHttp`、`HttpPort`、`HttpUrl`；HTTPS 使用 `HttpsPort`、`HttpsCertPath`，PEM 私密金鑰透過 `HttpsCertificateKeyPath` 設定，密碼欄位為 `HttpsCertPassword`。HTTPS 與 WSS 憑證分別設定。啟用簽章驗證後須提供 `HttpSignSecret`，並以 `HttpSignVersion` 與用戶端保持一致；版本 2 包含請求要素與 nonce 重放防護。

### 業務開發與熱更新

狀態與元件放在 `GameFrameX.Apps`，代理及訊息處理器放在 `GameFrameX.Hotfix`。跨 Actor 透過元件代理存取。使用目前的 `Network`、`PlayerSession`/`PlayerId`、`MessageHandler` 命名及非同步 API；非同步操作使用 `Async` 後綴。

`gfx-role` 模板向本地 server Actor 新增業務切片，保留的 `ServerRole` 目錄名稱表示業務組織。`gfx-online` 產生獨立最小宿主，在本進程裝配能力。安裝方法及限制見 [GameFrameX.Templates](GameFrameX.Templates/README.md)。

熱更新組件由本地宿主載入。Online 管理操作需要設定執行器並準備製品；最小 Online 模板不承載熱更新執行環境，因此明確拒絕版本切換。熱更新失敗恢復不等於交易回滾保證。

### 診斷與驗證

保留日誌與診斷能力。`/health`、`/alive` 預設僅在開發模式（`HttpIsDevelopment`）映射，正式環境開放需明確設定。既有資料庫指標不代表三種 Provider 的宿主健康檢查和 Meter 接線已完整涵蓋。

```bash
dotnet test Tests/GameFrameX.Tests/GameFrameX.Tests.csproj --no-build
dotnet test Tests/GameFrameX.Hotfix.Tests/GameFrameX.Hotfix.Tests.csproj --no-build
```

以上是驗證命令，不代表本次重建已通過驗收。MongoDB/PostgreSQL 整合測試需要 `GAMEFRAMEX_TEST_MONGODB_CONNECTION_STRING`、`GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING`。須先檢查環境變數並使用真實資料庫；部分測試缺少設定會直接返回。SQLite 測試涵蓋檔案持久化與交易。

## 架構概覽

Launcher 裝配一個宿主；StartUp 負責用戶端監聽與宿主生命週期；Core 負責本地 Actor、工作階段、元件、事件、計時器及熱更新執行；Hosting 在同一進程中組合可選模組。目前業務模組作為獨立業務層保留。

保留設定載入與熱重載。`ConfigComponent` 仍是靜態單例，重載不承諾跨表交易。停止時協調本地工作排空、狀態儲存與資源釋放。

## 依賴

執行階段工程使用 `net10.0`，產生器保留各自宣告的目標框架。SDK 選擇見 `global.json`，套件版本見工程檔及 `Version.props`。主要相依包括 GameFrameX.Foundation、SuperSocket、MongoDB.Driver、Microsoft.Data.Sqlite 與 Npgsql/Entity Framework Core。本次重建不發布套件、不修改既有 tag。

## 文檔與資源

- [GameFrameX](https://gameframex.doc.alianblank.com/)
- [GitHub](https://github.com/GameFrameX/GameFrameX.Server.Source)
- [Gitee](https://gitee.com/GameFrameX)
- [CNB](https://cnb.cool/GameFrameX)
- [Unity](https://github.com/GameFrameX/GameFrameX.Unity)
- [GameFrameX.Templates](GameFrameX.Templates/README.md)

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

## 更新日誌

[CHANGELOG.md](CHANGELOG.md) 記錄尚未發布的重建摘要及保留的早期歷史。

## 開源協議

詳見 [LICENSE](LICENSE) 檔案。
