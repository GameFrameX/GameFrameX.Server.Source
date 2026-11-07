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

**All-in-One Solution for Indie Game Development · Empowering Indie Developers' Dreams**

<br />

[Documentation](https://gameframex.doc.alianblank.com) · [Quick Start](#quick-start) · QQ Group: 467608841 / 233840761

<br />

**English** | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [한국어](README.ko.md)

</div>

## Project Overview

GameFrameX Server is a C# .NET 10 game server framework for a single process. One host runs client connections, local player sessions, Actors, hotfix logic and optional capability modules. The current game and Online business modules remain part of the application.

### Features

- **Actor and components**: serialized execution, state persistence, events and timers, with generated agent wrappers.
- **Hotfix**: loadable business assemblies and local lifecycle management, separate from persistent state definitions.
- **Client networking**: TCP, WebSocket (WS/WSS) and KCP, request/response messages, session authentication and disconnect cleanup.
- **HTTP/HTTPS**: handlers, signature validation, TLS and optional development documentation.
- **Persistence**: MongoDB, SQLite and PostgreSQL adapters, named connections, conditional writes, transactions and retries within each provider’s supported semantics.
- **Capability.Hosting**: in-process registration, dependency resolution, scheduling, module lifecycle and a generic management API.
- **Supporting libraries**: configuration loading, serialization, localization, diagnostics, architecture analyzers and tests.

## Quick Start

### Installation

Use the .NET 10 SDK selected by `global.json` and run the commands below from this repository. Prepare `Configs/app_config.json` for one host before starting; the database fields below belong to the application settings. SQLite needs no separate database service. MongoDB and PostgreSQL require reachable database instances. The copy command is for macOS/Linux; on Windows copy the same directory with the shell’s equivalent.

```bash
dotnet restore Server.slnx
dotnet build Server.slnx --no-restore --no-incremental -warnaserror:CS9057
cp -R Configs bin/app_debug/
cd bin/app_debug
dotnet GameFrameX.Launcher.dll
```

Configure the selected provider in the application settings. Use `Mongo` with a MongoDB connection string or `PostgreSql` with a PostgreSQL connection string to select the other adapters. `DataBaseName` names the connection/database. Named connections remain available through `GameDatabase` and the database registry; switching the provider does not migrate existing data.

```json
{
  "DatabaseProvider": "Sqlite",
  "DataBaseUrl": "Data Source=data/gameframex.db",
  "DataBaseName": "gameframex"
}
```

SQLite file mode persists to the configured path; `Data Source=:memory:` is ephemeral. SQLite serializes writes and some queries evaluate in memory, so large queries may scan the table. MongoDB transactions require a replica set; PostgreSQL uses its EF Core provider. Provider behavior and supported query shapes must be checked against the relevant contract tests.

## Usage Examples

### Client connections and HTTP

Configure TCP with `IsEnableTcp`, `OuterHost` and `OuterPort`; WS with `IsEnableWebSocket` and `WsPort`; WSS additionally requires `WssPort` and `WssCertFilePath`. For PEM certificates set `WssCertificateKeyPath`; use `WssCertificatePassword` for protected keys. Client KCP uses `IsEnableKcp` and `KcpPort`.

HTTP uses `IsEnableHttp`, `HttpPort` and `HttpUrl`. HTTPS uses `HttpsPort`, `HttpsCertPath`, and, for PEM, `HttpsCertificateKeyPath`; the password field is `HttpsCertPassword`. Keep HTTPS and WSS certificates configured separately. When signature checks are enabled, supply `HttpSignSecret` and choose `HttpSignVersion` to match the client; version 2 includes request data and nonce replay protection.

### Business logic and hotfix

Keep state and components in `GameFrameX.Apps`, and agents and message handlers in `GameFrameX.Hotfix`. Access another Actor through its component agent. Use the current `Network`, `PlayerSession`/`PlayerId`, `MessageHandler` and asynchronous API names; asynchronous operations use the `Async` suffix.

The `gfx-role` template adds a local business slice to the server Actor. Its existing `ServerRole` directory name describes business organization. `gfx-online` creates a separate minimal host for in-process capability composition. Installation and limitations are documented in [GameFrameX.Templates](GameFrameX.Templates/README.md).

Hotfix assemblies are loaded by the local host. The Online management operations require a configured executor and available artifacts; the minimal Online template deliberately rejects version switching because it does not host that runtime. Hotfix failure recovery is not a transactional rollback guarantee.

### Diagnostics and verification

Logs and diagnostics remain available. `/health` and `/alive` are mapped by default only in development mode (`HttpIsDevelopment`); production exposure requires explicit configuration. Existing database instrumentation does not imply that all three providers have complete host health-check and meter wiring.

```bash
dotnet test Tests/GameFrameX.Tests/GameFrameX.Tests.csproj --no-build
dotnet test Tests/GameFrameX.Hotfix.Tests/GameFrameX.Hotfix.Tests.csproj --no-build
```

These are verification commands, not a declaration that this rebuild has passed them. MongoDB and PostgreSQL integration suites require `GAMEFRAMEX_TEST_MONGODB_CONNECTION_STRING` and `GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING`. Check their presence and run against real databases: some tests return early without those variables. SQLite tests cover file persistence and transactions.

## Architecture

The Launcher composes one host. StartUp owns client listeners and the host lifecycle; Core owns local Actors, sessions, components, events, timers and hotfix execution. Hosting composes optional modules in the same process. Current business modules are preserved independently of this framework restructuring.

Configuration loading and hot reload remain available. `ConfigComponent` is still a static singleton; reload does not promise a transaction across multiple tables. Shutdown coordinates local work draining, state saving and resource disposal.

## Dependencies

Runtime projects target `net10.0`; generators keep their declared target frameworks. SDK selection is in `global.json`; package versions are in the project files and `Version.props`. Core dependencies include GameFrameX.Foundation, SuperSocket, MongoDB.Driver, Microsoft.Data.Sqlite and Npgsql/Entity Framework Core. This rebuild does not publish packages or change existing tags.

## Documentation & Resources

- [GameFrameX](https://gameframex.doc.alianblank.com/)
- [GitHub](https://github.com/GameFrameX/GameFrameX.Server.Source)
- [Gitee](https://gitee.com/GameFrameX)
- [CNB](https://cnb.cool/GameFrameX)
- [Unity](https://github.com/GameFrameX/GameFrameX.Unity)
- [GameFrameX.Templates](GameFrameX.Templates/README.md)

## Community & Support

[![GitHub](https://img.shields.io/badge/GitHub-181717?style=for-the-badge&logo=github&logoColor=white)](https://github.com/GameFrameX/gameframex)
[![Discord](https://img.shields.io/badge/Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/VDWUjWMDw9)
[<img src="https://cdn.jsdelivr.net/npm/devicon@2/icons/linkedin/linkedin-original.svg" height="28" alt="LinkedIn" />](https://www.linkedin.com/in/alianblank)
[![Reddit](https://img.shields.io/badge/Reddit-FF4500?style=for-the-badge&logo=reddit&logoColor=white)](https://www.reddit.com/r/GameFrameX/)
[![X](https://img.shields.io/badge/X-000000?style=for-the-badge&logo=x&logoColor=white)](https://x.com/alian_blank)
[![YouTube](https://img.shields.io/badge/YouTube-FF0000?style=for-the-badge&logo=youtube&logoColor=white)](https://www.youtube.com/channel/UCD9QhSFJ5xZkn5NTSV-DVAw)
[![Bluesky](https://img.shields.io/badge/Bluesky-0285FF?style=for-the-badge&logo=bluesky&logoColor=white)](https://bsky.app/profile/alianblank.bsky.social)
[![Bilibili](https://img.shields.io/badge/Bilibili-00A1D6?style=for-the-badge&logo=bilibili&logoColor=white)](https://www.bilibili.com/video/BV1yrpeepEn7)
[![Gitee](https://img.shields.io/badge/Gitee-C71D23?style=for-the-badge&logo=gitee&logoColor=white)](https://gitee.com/GameFrameX/gameframex)
![QQ](https://img.shields.io/badge/QQ-467608841%2F233840761-EB1923?style=for-the-badge&logo=qq&logoColor=white)

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the unreleased rebuild summary and retained earlier history.

## License

See [LICENSE](LICENSE) for license information.
