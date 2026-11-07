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

**インディゲーム開発者向けオールインワンソリューション · インディ開発者の夢を支援**

<br />

[ドキュメント](https://gameframex.doc.alianblank.com) · [クイックスタート](#クイックスタート) · QQグループ: 467608841 / 233840761

<br />

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | **日本語** | [한국어](README.ko.md)

</div>

## プロジェクト概要

GameFrameX Server は C# .NET 10 の単一プロセス型ゲームサーバーフレームワークです。1 つのホストがクライアント接続、ローカルプレイヤーセッション、Actor、Hotfix ロジック、任意の機能モジュールを実行します。現在のゲームおよび Online の業務モジュールは維持されます。

### 機能概要

- **Actor とコンポーネント**：直列実行、状態の永続化、イベント、タイマー、生成されたエージェントラッパー。
- **Hotfix**：ロード可能な業務アセンブリとローカルライフサイクル管理。状態定義と業務ロジックを分離します。
- **クライアント通信**：TCP、WebSocket（WS/WSS）、KCP、要求応答メッセージ、セッション認証、切断時の解放。
- **HTTP/HTTPS**：ハンドラー、署名検証、TLS、任意の開発用ドキュメント。
- **永続化**：MongoDB、SQLite、PostgreSQL、名前付き接続、条件付き書き込み、トランザクション、再試行。詳細な意味論は各 Provider の対応範囲に従います。
- **Capability.Hosting**：プロセス内登録、依存関係解決、スケジューリング、モジュールのライフサイクル、汎用管理 API。
- **関連機能**：設定の読み込み、シリアライズ、多言語対応、診断、アーキテクチャアナライザー、テスト。

## クイックスタート

### インストール

`global.json` が指定する .NET 10 SDK を使用し、リポジトリのルートで以下を実行します。起動前に `Configs/app_config.json` を単一ホスト用に設定してください。以下のデータベース項目はアプリケーション設定です。SQLite は別のデータベースサービスを必要としません。MongoDB と PostgreSQL には接続可能なインスタンスが必要です。コピーコマンドは macOS/Linux 用です。Windows では同じディレクトリを対応するコマンドでコピーします。

```bash
dotnet restore Server.slnx
dotnet build Server.slnx --no-restore --no-incremental -warnaserror:CS9057
dotnet build GameFrameX.Hotfix/GameFrameX.Hotfix.csproj -o bin/app_debug/hotfix --no-restore
cp -R GameFrameX.Config/json bin/app_debug/
cp -R Configs bin/app_debug/
cd bin/app_debug
dotnet GameFrameX.Launcher.dll
```

アプリケーション設定で Provider を選択します。MongoDB には `Mongo` とその接続文字列、PostgreSQL には `PostgreSql` とその接続文字列を使用します。`DataBaseName` は接続/データベース名です。`GameDatabase` とレジストリは名前付き接続を維持します。Provider の変更で既存データが移行されることはありません。

```json
{
  "DatabaseProvider": "Sqlite",
  "DataBaseUrl": "Data Source=data/gameframex.db",
  "DataBaseName": "gameframex"
}
```

SQLite のファイルモードは指定パスに永続化し、`Data Source=:memory:` は一時的なメモリデータベースです。書き込みは直列化され、一部のクエリはメモリ内で評価されるため全表走査になる場合があります。MongoDB のトランザクションにはレプリカセットが必要です。PostgreSQL は EF Core Provider を使用します。クエリとトランザクションの意味論は対応する契約テストで確認してください。

## 使用例

### クライアント接続と HTTP

TCP は `IsEnableTcp`、`OuterHost`、`OuterPort`、WS は `IsEnableWebSocket`、`WsPort` を使用します。WSS はさらに `WssPort`、`WssCertFilePath` を必要とします。PEM 秘密鍵は `WssCertificateKeyPath`、保護された鍵のパスワードは `WssCertificatePassword` で設定します。クライアント KCP は `IsEnableKcp`、`KcpPort` を使用します。

HTTP は `IsEnableHttp`、`HttpPort`、`HttpUrl`、HTTPS は `HttpsPort`、`HttpsCertPath` を使用します。PEM 秘密鍵には `HttpsCertificateKeyPath`、パスワードには `HttpsCertPassword` を使用します。HTTPS と WSS の証明書は個別に設定します。署名検証を有効にする場合、`HttpSignSecret` とクライアントに対応する `HttpSignVersion` を設定します。バージョン 2 は要求内容と nonce によるリプレイ対策を含みます。

### 業務ロジックと Hotfix

状態とコンポーネントは `GameFrameX.Apps`、エージェントとメッセージハンドラーは `GameFrameX.Hotfix` に配置します。別 Actor へのアクセスにはコンポーネントエージェントを使用します。現行の `Network`、`PlayerSession`/`PlayerId`、`MessageHandler` と非同期 API を使用し、非同期操作には `Async` 接尾辞を使用します。

`gfx-role` はローカル server Actor に業務スライスを追加します。既存の `ServerRole` ディレクトリ名は業務上の分類です。`gfx-online` はプロセス内の機能を構成する独立した最小ホストを生成します。手順と制約は [GameFrameX.Templates](GameFrameX.Templates/README.md) を参照してください。

Hotfix アセンブリはローカルホストがロードします。Online の管理操作には実行器と利用可能な成果物が必要です。最小 Online テンプレートには Hotfix ランタイムがないため、バージョン切り替えは明示的に失敗します。失敗時の復旧はトランザクションのロールバックを保証しません。

### 診断と検証

ログと診断を維持します。`/health`、`/alive` は既定で開発モード（`HttpIsDevelopment`）のみ公開され、本番公開には明示的な設定が必要です。既存のデータベース計測だけで、3 種の Provider のヘルスチェックと Meter 接続がすべて完成しているとはいえません。

```bash
dotnet test Tests/GameFrameX.Tests/GameFrameX.Tests.csproj --no-build
dotnet test Tests/GameFrameX.Hotfix.Tests/GameFrameX.Hotfix.Tests.csproj --no-build
```

これらは検証用コマンドであり、今回の再構築が合格済みであることを示しません。MongoDB/PostgreSQL の統合テストには `GAMEFRAMEX_TEST_MONGODB_CONNECTION_STRING`、`GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING` が必要です。変数を確認し、実際のデータベースで実行してください。設定がないと早期終了するテストがあります。SQLite テストはファイル永続化とトランザクションを扱います。

## アーキテクチャ

Launcher は単一ホストを構成します。StartUp はクライアントのリスナーとホストのライフサイクル、Core はローカル Actor、セッション、コンポーネント、イベント、タイマー、Hotfix を担当します。Hosting は同一プロセス内で任意のモジュールを構成します。現在の業務モジュールは独立した業務層として維持します。

設定読み込みとホットリロードを維持します。`ConfigComponent` は静的シングルトンのままで、複数テーブル間のトランザクションは保証しません。停止時はローカル処理の完了待ち、状態保存、リソース解放を調整します。

## 依存関係

実行時プロジェクトは `net10.0`、ジェネレーターは各宣言のターゲットを維持します。SDK は `global.json`、パッケージバージョンはプロジェクトファイルと `Version.props` を参照してください。主要依存は GameFrameX.Foundation、SuperSocket、MongoDB.Driver、Microsoft.Data.Sqlite、Npgsql/Entity Framework Core です。今回の再構築ではパッケージ公開や既存 tag の変更は行いません。

## ドキュメントとリソース

- [GameFrameX](https://gameframex.doc.alianblank.com/)
- [GitHub](https://github.com/GameFrameX/GameFrameX.Server.Source)
- [Gitee](https://gitee.com/GameFrameX)
- [CNB](https://cnb.cool/GameFrameX)
- [Unity](https://github.com/GameFrameX/GameFrameX.Unity)
- [GameFrameX.Templates](GameFrameX.Templates/README.md)

## コミュニティとサポート

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

## 変更履歴

未公開の再構築概要と保持された過去の履歴は [CHANGELOG.md](CHANGELOG.md) を参照してください。

## ライセンス

詳しくは [LICENSE](LICENSE) をご参照ください。
