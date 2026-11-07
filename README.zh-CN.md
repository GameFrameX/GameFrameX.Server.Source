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

## 项目简介

GameFrameX Server 是基于 C# .NET 10 的单进程游戏服务器框架。一个宿主承载客户端连接、本地玩家会话、Actor、热更逻辑与可选能力模块；当前游戏和 Online 业务模块完整保留。

### 功能特性

- **Actor 与组件**：串行执行、状态持久化、事件、定时器和生成的代理包装。
- **Hotfix 热更**：可加载的业务程序集与本地生命周期管理，状态定义与业务逻辑分层。
- **客户端网络**：TCP、WebSocket（WS/WSS）、KCP、请求响应消息、会话鉴权与断线清理。
- **HTTP/HTTPS**：处理器、签名校验、TLS 与可选开发文档。
- **数据库**：MongoDB、SQLite、PostgreSQL 三种适配，保留命名连接、条件写、事务和重试，具体语义以各 Provider 支持范围为准。
- **Capability.Hosting**：本进程注册、依赖解析、调度、模块生命周期与通用管理 API。
- **配套能力**：配置加载、序列化、本地化、诊断、架构分析器和测试。

## 快速开始

### 安装

使用 `global.json` 指定的 .NET 10 SDK，在仓库根执行以下命令。启动前将 `Configs/app_config.json` 配置为单宿主；下方数据库字段属于应用配置。SQLite 无需独立数据库服务，MongoDB 和 PostgreSQL 需要可访问的数据库实例。复制命令适用于 macOS/Linux，Windows 请用对应命令复制相同目录。

```bash
dotnet restore Server.slnx
dotnet build Server.slnx --no-restore --no-incremental -warnaserror:CS9057
dotnet build GameFrameX.Hotfix/GameFrameX.Hotfix.csproj -o bin/app_debug/hotfix --no-restore
cp -R GameFrameX.Config/json bin/app_debug/
cp -R Configs bin/app_debug/
cd bin/app_debug
dotnet GameFrameX.Launcher.dll
```

在应用配置中选择数据库。使用 `Mongo` 与 MongoDB 连接串，或 `PostgreSql` 与 PostgreSQL 连接串，可切换到另外两种适配。`DataBaseName` 指定连接/数据库名称。`GameDatabase` 和数据库注册表继续支持命名连接；更换 Provider 不会迁移已有数据。

```json
{
  "DatabaseProvider": "Sqlite",
  "DataBaseUrl": "Data Source=data/gameframex.db",
  "DataBaseName": "gameframex"
}
```

SQLite 文件模式持久化到配置路径，`Data Source=:memory:` 为易失内存库。SQLite 串行写入，部分查询在内存中求值，大查询可能扫描整表。MongoDB 事务需要副本集；PostgreSQL 使用 EF Core Provider。具体查询和事务语义须通过对应契约测试核验。

## 使用示例

### 客户端连接与 HTTP

TCP 使用 `IsEnableTcp`、`OuterHost`、`OuterPort`；WS 使用 `IsEnableWebSocket`、`WsPort`；WSS 另需 `WssPort`、`WssCertFilePath`。PEM 证书设置 `WssCertificateKeyPath`，受保护密钥使用 `WssCertificatePassword`。客户端 KCP 使用 `IsEnableKcp`、`KcpPort`。

HTTP 使用 `IsEnableHttp`、`HttpPort`、`HttpUrl`；HTTPS 使用 `HttpsPort`、`HttpsCertPath`，PEM 私钥通过 `HttpsCertificateKeyPath` 配置，密码字段为 `HttpsCertPassword`。HTTPS 与 WSS 证书分别配置。启用签名校验后须提供 `HttpSignSecret`，并用 `HttpSignVersion` 与客户端约定一致的签名口径；版本 2 包含请求要素与 nonce 防重放。

### 业务开发与热更

状态和组件放在 `GameFrameX.Apps`，代理与消息处理器放在 `GameFrameX.Hotfix`。跨 Actor 通过组件代理访问。使用当前 `Network`、`PlayerSession`/`PlayerId`、`MessageHandler` 命名与异步 API；异步操作使用 `Async` 后缀。

`gfx-role` 模板向本地 server Actor 添加业务切片，保留的 `ServerRole` 目录名表示业务组织。`gfx-online` 生成独立的最小宿主，在本进程装配能力。安装方法和限制见 [GameFrameX.Templates](GameFrameX.Templates/README.md)。

热更程序集由本地宿主加载。Online 管理操作需要配置执行器并准备制品；最小 Online 模板不承载热更运行时，因此明确拒绝版本切换。热更失败恢复不等于事务回滚保证。

### 诊断与验证

保留日志与诊断能力。`/health`、`/alive` 默认仅在开发模式（`HttpIsDevelopment`）映射，生产开放需显式配置。数据库已有指标不代表三种 Provider 的宿主健康检查和 Meter 接线均已完整覆盖。

```bash
dotnet test Tests/GameFrameX.Tests/GameFrameX.Tests.csproj --no-build
dotnet test Tests/GameFrameX.Hotfix.Tests/GameFrameX.Hotfix.Tests.csproj --no-build
```

以上是验证命令，不代表本次重建已通过验收。MongoDB/PostgreSQL 集成测试需要 `GAMEFRAMEX_TEST_MONGODB_CONNECTION_STRING`、`GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING`。须先检查环境变量并使用真实数据库；部分测试缺配置会直接返回。SQLite 测试覆盖文件持久化和事务。

## 架构概览

Launcher 装配一个宿主；StartUp 负责客户端监听与宿主生命周期；Core 负责本地 Actor、会话、组件、事件、定时器和热更执行；Hosting 在同一进程中组合可选模块。当前业务模块作为独立业务层保留。

保留配置加载与热重载。`ConfigComponent` 仍是静态单例，重载不承诺跨表事务。停机协调本地任务排空、状态回存和资源释放。

## 依赖

运行时工程使用 `net10.0`，生成器保留各自声明的目标框架。SDK 选择见 `global.json`，包版本见工程文件和 `Version.props`。主要依赖包括 GameFrameX.Foundation、SuperSocket、MongoDB.Driver、Microsoft.Data.Sqlite 与 Npgsql/Entity Framework Core。本次重建不发布包、不修改已有 tag。

## 文档与资源

- [GameFrameX](https://gameframex.doc.alianblank.com/)
- [GitHub](https://github.com/GameFrameX/GameFrameX.Server.Source)
- [Gitee](https://gitee.com/GameFrameX)
- [CNB](https://cnb.cool/GameFrameX)
- [Unity](https://github.com/GameFrameX/GameFrameX.Unity)
- [GameFrameX.Templates](GameFrameX.Templates/README.md)

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

## 更新日志

[CHANGELOG.md](CHANGELOG.md) 记录未发布的重建摘要和保留的早期历史。

## 开源协议

详见 [LICENSE](LICENSE) 文件。
