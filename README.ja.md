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

## 目次

- [プロジェクト概要](#プロジェクト概要)
  - [機能概要](#機能概要)
- [クイックスタート](#クイックスタート)
  - [前提条件](#前提条件)
  - [インストール](#インストール)
- [使用例](#使用例)
  - [設定管理](#設定管理)
  - [ビジネスロジック開発](#ビジネスロジック開発)
  - [ホットアップデート機構](#ホットアップデート機構)
  - [Docker デプロイ](#docker-デプロイ)
  - [マルチプロセス・クロスプロセス連携](#マルチプロセスクロスプロセス連携)
  - [モニタリングとオブザーバビリティ](#モニタリングとオブザーバビリティ)
  - [テスト](#テスト)
- [アーキテクチャ](#アーキテクチャ)
  - [プロジェクト構成](#プロジェクト構成)
- [プロセストポロジ同型](#プロセストポロジ同型)
  - [マルチロール起動](#マルチロール起動)
  - [Docker Compose ファイル](#docker-compose-ファイル)
- [依存関係](#依存関係)
- [ドキュメントとリソース](#ドキュメントとリソース)
- [コミュニティとサポート](#コミュニティとサポート)
  - [コントリビュート](#コントリビュート)
- [変更履歴](#変更履歴)
- [ライセンス](#ライセンス)

---

## プロジェクト概要

GameFrameX Server は、C# .NET 10.0 で開発された高性能・クロスプラットフォームのゲームサーバーフレームワークです。Actor モデルを採用し、ホットアップデート機構をサポートしています。マルチプレイヤーオンラインゲーム開発向けに設計されており、Unity3D、Godot、LayaBox など多様なクライアントプラットフォームとの統合をサポートします。

**設計理念**: 大道至簡、シンプルイズベスト

---

### 機能概要

#### 高性能アーキテクチャ

- **Actor モデル**: TPL DataFlow 上に構築されたロックフリー・高同時実行システム。メッセージパッシングにより従来のロックのパフォーマンス劣化を回避
- **完全非同期プログラミング**: 完全な async/await 非同期プログラミングモデル
- **ゼロロック設計**: Actor 内部状態はメッセージキューによる直列化アクセスでロック不要
- **バッチ永続化**: バッチDB書き込みをサポート。バッチサイズとタイムアウト設定可能
- **スノーフレーク ID 生成**: 分散ユニーク ID ジェネレーター内蔵。ワーカーノード・データセンター設定対応

#### ホットアップデートシステム

- **ゼロダウンタイム更新**: 実行時に新しいロジックアセンブリをロード。サービス停止不要
- **状態・ロジック分離**: 永続化状態データ（Apps 層）とホットアップデート可能なビジネスロジック（Hotfix 層）を厳密に分離
- **グレースフル移行**: 旧アセンブリは10分間の猶予期間を保持。進行中のリクエスト完了後にアンロード
- **バージョン管理**: HTTP エンドポイント経由でバージョン番号を指定してロード可能

#### マルチプロトコルネットワーク通信

- **TCP**: SuperSocket ベースの高性能 TCP サーバー。メインゲーム通信プロトコル
- **UDP**: オプションの UDP プロトコルサポート
- **WebSocket**: SuperSocket WebSocket ベースの双方向通信
- **HTTP/HTTPS**: Kestrel ベースの HTTP サービス。Swagger ドキュメント、CORS、ヘルスチェック対応
- **KCP**: 正式サポート（オプトイン）に昇格した UDP 信頼性伝送。`KcpPort` が `0` の場合は TCP ポートを共用

#### データベースと永続化

- **デュアルデータベース Provider**: `DatabaseProvider` で `Mongo`（デフォルト）または `PostgreSql` を選択。両 Provider は同じ `GameFrameX.DataBase` 抽象レイヤーを共有
- **MongoDB Provider**: 完全な MongoDB 統合。ヘルスステートマシン対応（Healthy → Degraded → Unhealthy → Recovering）
- **PostgreSQL Provider**: Npgsql ベース。MongoDB Provider と同等のリトライ / リカバリ / 可用性 / ヘルスチェック能力を備える
- **透過的永続化**: StateComponent の自動シリアライズ/デシリアライズ。定期的バッチ upsert 操作で永続化

#### モニタリングとオブザーバビリティ

- **OpenTelemetry**: 包括的なメトリクス（Metrics）、トレーシング（Tracing）、ロギング（Logging）
- **Prometheus**: ネイティブメトリクスエクスポートエンドポイント
- **Grafana Loki**: ログ集約出力対応
- **Serilog**: 構造化ログ。コンソール、ファイル、Loki マルチ出力対応

---

## クイックスタート

### 前提条件

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) のみをサポートします。.NET 8/9 はサポート対象外です。
- [MongoDB 4.x+](https://www.mongodb.com/try/download/community)（デフォルト Provider。`--DatabaseProvider=PostgreSql` で PostgreSQL も利用可能）
- Visual Studio 2022 または JetBrains Rider（推奨）

### インストール

1. **リポジトリをクローン**
   ```bash
   git clone https://github.com/GameFrameX/GameFrameX.Server.Source.git
   cd GameFrameX.Server.Source
   ```

2. **依存関係を復元**
   ```bash
   dotnet restore
   ```

3. **プロジェクトをビルド**
   ```bash
   dotnet build
   ```

4. **MongoDB を起動**
   ```bash
   # ローカルインストール
   mongod --dbpath /path/to/data

   # または Docker を使用
   docker run -d -p 27017:27017 --name mongo mongo:8.2
   ```

5. **サーバーを起動**
   ```bash
   dotnet run --project GameFrameX.Launcher -- \
       --ServerType=Game \
       --ServerId=1000 \
       --OuterPort=29100 \
       --HttpPort=28080 \
       --DataBaseUrl=mongodb://127.0.0.1:27017 \
       --DataBaseName=gameframex
   ```

6. **起動確認**
   - ヘルスチェック: `http://localhost:28080/health`（ライブネスプローブ: `/alive`）
   - コンソールログで起動成功を確認

---

## 使用例

以下の例では、設定、ビジネスロジック、ホットアップデート、デプロイ、デバッグまでの開発フローを網羅しています。

---

### 設定管理

GameFrameX はコマンドライン引数（`--Key=Value`）で設定を行います。すべての設定項目は `StartupOptions` クラスで定義されています。

#### サーバー設定

| 設定項目 | 説明 | デフォルト | 例 |
|:--------|:-----|:----------|:---|
| `ServerType` | サーバータイプ（必須） | なし | `Game`、`Social` |
| `ServerId` | サーバー一意 ID | なし | `1000` |
| `ServerInstanceId` | サーバーインスタンス ID（同タイプの異なるインスタンスを区別） | `0` | `1001` |
| `IsSingleMode` | シングルプロセスモード | `false` | `true` |
| `MinModuleId` | ビジネスモジュール開始 ID（モジュールシャーディング） | `0` | `100` |
| `MaxModuleId` | ビジネスモジュール終了 ID（モジュールシャーディング） | `0` | `1000` |
| `TimeZone` | サーバータイムゾーン | `Asia/Shanghai` | `UTC` |
| `IsUseTimeZone` | カスタムタイムゾーンを有効化 | `false` | `true` |
| `Language` | 言語設定 | なし | `zh-CN` |

#### ネットワーク設定

| 設定項目 | 説明 | デフォルト | 例 |
|:--------|:-----|:----------|:---|
| `InnerHost` | 内部通信用 IP（クラスタ間） | `0.0.0.0` | `0.0.0.0` |
| `InnerPort` | 内部通信用ポート | `8888` | `29100` |
| `OuterHost` | 外部通信用 IP（クライアント向け） | `0.0.0.0` | `0.0.0.0` |
| `OuterPort` | 外部通信用ポート | なし | `29100` |
| `IsEnableTcp` | TCP サービスを有効化 | `true` | `true` |
| `IsEnableUdp` | UDP サービスを有効化 | `false` | `true` |
| `IsEnableKcp` | KCP サービスを有効化（設定ファイルのみ） | `false` | `true` |
| `KcpPort` | KCP ポート（`0` で TCP ポートを共用） | `0` | `29120` |
| `IsEnableWebSocket` | WebSocket を有効化 | `false` | `true` |
| `WsPort` | WebSocket ポート | `8889` | `29300` |
| `IsEnableHttp` | HTTP サービスを有効化 | `true` | `true` |
| `HttpPort` | HTTP サービスポート | `8080` | `28080` |
| `HttpsPort` | HTTPS サービスポート | なし | `443` |
| `HttpUrl` | API ルートパス | `/game/api/` | `/game/api/` |
| `HttpIsDevelopment` | HTTP 開発モード（Swagger を有効化） | `false` | `true` |
| `IsEnableOnlineAdmin` | Online Runtime + 管理 API をプロセス内でホスト | `false` | `true` |
| `OnlineAdminPort` | Online 管理 API リッスンポート | `28090` | `28090` |
| `OnlineAdminApiPrefix` | Online 管理 API ルートプレフィックス | `online/admin` | `online/admin` |
| `OnlineTenantId` | Online Runtime 認可テナント ID（スコープトリプル） | `0` | `1` |
| `OnlineAppId` | Online Runtime 認可アプリ ID（スコープトリプル） | `0` | `1` |

#### データベース設定

| 設定項目 | 説明 | デフォルト | 例 |
|:--------|:-----|:----------|:---|
| `DatabaseProvider` | データベース Provider | `Mongo` | `PostgreSql` |
| `DataBaseUrl` | データベース接続文字列 | なし | `mongodb://localhost:27017` |
| `DataBaseName` | データベース名 | なし | `gameframex` |
| `DataBasePassword` | データベースパスワード | なし | `your_password` |

#### Actor 設定

| 設定項目 | 説明 | デフォルト | 例 |
|:--------|:-----|:----------|:---|
| `ActorTimeOut` | Actor タスク実行タイムアウト（ミリ秒） | `30000` | `60000` |
| `ActorQueueTimeOut` | Actor キュータイムアウト（ミリ秒） | `30000` | `60000` |
| `ActorRecycleTime` | Actor アイドルリサイクル時間（分） | `15` | `30` |
| `SaveDataInterval` | データ保存間隔（ミリ秒） | `30000` | `60000` |
| `SaveDataBatchCount` | バッチ保存数 | `500` | `1000` |
| `SaveDataBatchTimeOut` | バッチ保存タイムアウト（ミリ秒） | `30000` | `60000` |

#### ログ設定

| 設定項目 | 説明 | デフォルト | 例 |
|:--------|:-----|:----------|:---|
| `IsDebug` | デバッグログマスタースイッチ | `false` | `true` |
| `LogIsConsole` | コンソール出力 | `true` | `false` |
| `LogIsWriteToFile` | ファイル出力 | `true` | `false` |
| `LogEventLevel` | ログレベル | `Debug` | `Information` |
| `LogRollingInterval` | ログローリング間隔 | `Day` | `Hour` |
| `LogIsFileSizeLimit` | 単一ファイルサイズ制限 | `true` | `false` |
| `LogFileSizeLimitBytes` | ファイルサイズ制限 | `104857600` (100MB) | `52428800` |
| `LogRetainedFileCountLimit` | 保持ファイル数 | `31` | `90` |
| `LogIsGrafanaLoki` | Grafana Loki 出力 | `false` | `true` |
| `LogGrafanaLokiUrl` | Grafana Loki URL | `http://localhost:3100` | — |

#### モニタリング設定

| 設定項目 | 説明 | デフォルト | 例 |
|:--------|:-----|:----------|:---|
| `IsOpenTelemetry` | OpenTelemetry を有効化 | `false` | `true` |
| `IsOpenTelemetryMetrics` | メトリクス収集を有効化 | `false` | `true` |
| `IsOpenTelemetryTracing` | 分散トレーシングを有効化 | `false` | `true` |
| `MetricsPort` | Prometheus メトリクスポート | `0`（HTTP ポートを共用） | `9090` |
| `IsMonitorMessageTimeOut` | メッセージ処理タイムアウト監視 | `false` | `true` |
| `MonitorMessageTimeOutSeconds` | タイムアウト閾値（秒） | `1` | `5` |

#### ID 生成設定

| 設定項目 | 説明 | デフォルト | 例 |
|:--------|:-----|:----------|:---|
| `WorkerId` | スノーフレーク ID ワーカーノード ID | `1` | `2` |
| `DataCenterId` | スノーフレーク ID データセンター ID | `1` | `2` |

#### 起動コマンド例

```bash
# 最小起動パラメータ
dotnet GameFrameX.Launcher.dll \
    --ServerType=Game \
    --ServerId=1000 \
    --DataBaseUrl=mongodb://127.0.0.1:27017 \
    --DataBaseName=game_db

# フル起動パラメータ
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

### ビジネスロジック開発

#### コンポーネント・エージェントパターン

フレームワークのコア設計パターンは**状態・ロジック分離**です。永続化状態（Apps 層、ホット更新不可）とビジネスロジック（Hotfix 層、ホット更新可能）を厳密に分離します。

**1. 状態の定義（Apps 層）**

```csharp
// GameFrameX.Apps/Player/BagState.cs
public class BagState : BaseCacheState
{
    public List<ItemData> Items { get; set; } = new List<ItemData>();
    public int MaxSlots { get; set; } = 50;
}
```

**2. コンポーネントの作成（Apps 層）**

```csharp
// GameFrameX.Apps/Player/BagComponent.cs
public class BagComponent : StateComponent<BagState>
{
    protected override async Task OnInit()
    {
        await base.OnInit();
        // コンポーネント状態の初期化
    }
}
```

**3. ビジネスロジックの実装（Hotfix 層）**

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

**4. コンポーネントエージェントへのアクセス**

```csharp
// ActorManager 経由でコンポーネントエージェントを取得
var bagAgent = await ActorManager.GetComponentAgent<BagComponentAgent>(playerId);
var result = await bagAgent.AddItem(1001, 10);
```

#### HTTP ハンドラ

HTTP ハンドラは `BaseHttpHandler` を継承し、`[HttpMessageMapping]` 属性でルートを登録します。2 番目/3 番目のコンストラクター引数は省略可能な位置引数です（request/response 型。それぞれ `HttpMessageRequestBase` / `HttpMessageResponseBase` を継承する必要があります）。レスポンス型のみ宣言する場合は第 2 引数に `null` をプレースホルダーとして渡します。未指定の場合は通常の JSON パスが使用され、Swagger の data には汎用オブジェクトが使われます。

```csharp
[HttpMessageMapping(typeof(GetPlayerInfoHandler), typeof(GetPlayerInfoRequest), typeof(GetPlayerInfoResponse))]
[Description("プレイヤー情報を取得")]
public sealed class GetPlayerInfoHandler : BaseHttpHandler
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

#### TCP/RPC メッセージハンドラ

TCP メッセージハンドラは、クライアントから TCP 接続経由で送信されるゲームメッセージを処理します。

**単方向メッセージハンドラ:**

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

**RPC ハンドラ（リクエスト・レスポンス）:**

```csharp
[MessageMapping(typeof(ReqAddItem))]
internal sealed class AddItemHandler : PlayerRpcComponentHandler<BagComponentAgent, ReqAddItem, RespAddItem>
{
    protected override async Task ActionAsync(ReqAddItem request, RespAddItem response)
    {
        try
        {
            // ComponentAgent は基底クラスにより自動注入
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

#### イベントハンドラ

イベントシステムは Actor 間の疎結合通信に使用します。

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

        // プレイヤーログインイベントの処理
        return agent.OnLogin();
    }
}
```

---

### ホットアップデート機構

#### アーキテクチャ原理

ホットアップデートシステムは `AssemblyLoadContext`（回収可能）により、アセンブリのランタイムロード・アンロードを実現します：

```
┌───────────────────────────────────────────────────────┐
│  Apps 層（ホット更新不可）                               │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │StateComponent│  │StateComponent│  │StateComponent│   │
│  │ 永続化状態    │  │ 永続化状態    │  │ 永続化状態    │   │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘   │
│         │                │                │           │
├─────────┼────────────────┼────────────────┼───────────┤
│         ▼                ▼                ▼           │
│  Hotfix 層（ホット更新可能）— AssemblyLoadContext       │
│  経由でロード                                            │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │ComponentAgent│  │ComponentAgent│  │ComponentAgent│   │
│  │ ビジネスロジック│  │ ビジネスロジック│  │ ビジネスロジック│   │
│  └─────────────┘  └─────────────┘  └─────────────┘   │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │ Msg Handler  │  │ EventHandler│  │ HttpHandler  │   │
│  └─────────────┘  └─────────────┘  └─────────────┘   │
└───────────────────────────────────────────────────────┘
```

#### ホットアップデートフロー

1. **新ロジックのコンパイル**: 更新された `GameFrameX.Hotfix.dll` をビルド
2. **アセンブリのデプロイ**: サーバーの指定ディレクトリにコピー
3. **リロードのトリガー**: HTTP エンドポイント経由でホットアップデートリクエストを発行
4. **アセンブリロード**: `HotfixManager` が回収可能な `AssemblyLoadContext` で新 DLL をロード
5. **タイプスキャン**: `HotfixModule` が新アセンブリ内のエージェント、ハンドラ、イベントリスナーをスキャン
6. **エージェント切り替え**: `ActorManager.ClearAgent()` がキャッシュされたエージェントインスタンスをクリア
7. **グレースフル移行**: 旧アセンブリは10分間の猶予期間を保持。進行中のリクエスト完了後にアンロード

#### ホットアップデート API

```bash
# ホットアップデートのトリガー（バージョン指定）
curl -X POST "http://localhost:28080/game/api/Reload?version=1.7.2"
```

> 補足: `Reload` ハンドラには `[RequireHttpSignature]` が付与されており、リクエストは HTTP 署名検証を通過する必要があります。

---

### Docker デプロイ

#### 単一インスタンスデプロイ

`docker-compose.yml` を使用して MongoDB + Game の最小環境を起動します（Social は後日復旧予定）：

```bash
# ビルドして起動
docker compose up -d --build

# 実行状態を確認
docker compose ps

# ログを確認
docker compose logs -f game

# 停止
docker compose down
```

サービスポートマッピング：

| サービス | コンテナポート | ホストポート | 説明 |
|:--------|:-------------|:-----------|:-----|
| MongoDB | 27017 | 37017 | データベース |
| Game TCP | 29100 | 39100 | ゲームサーバー |
| Game HTTP | 28080 | 38080 | ゲームサーバー HTTP API |
| Online Admin | 28090 | 28090 | Online プラットフォーム管理 API（下記参照） |

#### Online プラットフォーム管理 API

`IsEnableOnlineAdmin=true` の場合、Game プロセスは Online Runtime（インメモリストア上のアセット / セッション / マッチング / ソーシャル / LiveOps サービスを含むプロセス内機能ライブラリ）を組み立て、管理用 HTTP API を独立した Kestrel リスナーで公開します：

- エンドポイント: `POST http://<server-host>:28090/online/admin/{action}`（全 29 の管理アクション。HTTP ステータスは常に 200、業務結果は内部エンベロープの `Code` で返却）
- スコープトリプル: 各リクエストは `TenantId` / `AppId` / `ServerId` を携行し、認可された `OnlineTenantId` / `OnlineAppId` / `ServerId` と不一致の場合は 3002/3003/3004、トリプル欠落の場合は 3005 で拒否されます
- GameFrameX Admin コンソールへのサーバー登録: エリアの `HttpManageUrl` を `http://<server-host>:28090` に設定します（`online/admin` プレフィックスとアクション名は Admin クライアント側のワイヤ契約に従って付加されます）

#### マルチインスタンスデプロイ

`docker-compose.multi.yml` を使用して 1 MongoDB + 2 Social + 10 Game のクラスタ環境を起動：

```bash
# ビルドして起動
docker compose -f docker-compose.multi.yml up -d --build

# 実行状態を確認
docker compose -f docker-compose.multi.yml ps

# 停止
docker compose -f docker-compose.multi.yml down
```

クラスタトポロジ：

| コンポーネント | インスタンス数 | 説明 |
|:-------------|:------------|:-----|
| MongoDB | 1 | 共有データベース |
| Social | 2 | ソーシャルサーバー（social-1, social-2） |
| Game | 10 | ゲームサーバー（game-1 ~ game-10） |

全インスタンスは Aspire スタイルの環境変数でサービスディスカバリを行います：

```yaml
environment:
  services__Social_2001__tcp__0: "tcp://social-1:29400"
  services__Social_2002__tcp__0: "tcp://social-2:29401"
  services__Game_1001__tcp__0: "tcp://game-1:29100"
  # ...
```

#### カスタムビルド

```bash
# イメージをビルド
docker build -t gameframex/server:custom .

# 実行
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

### マルチプロセス・クロスプロセス連携

#### クロスプロセススモークテスト

```bash
# マルチインスタンス環境が起動していることを確認
docker compose -f docker-compose.multi.yml up -d --build

# クロスプロセススモークテストを実行
./scripts/multi/smoke-cross-process.sh
```

スクリプトの検証内容：
- `game-1` → `social` クロスプロセスコール
- `game-2` → `social` クロスプロセスコール
- `code=0` および `FriendCount >= 1` を返却

#### ボットストレステスト

実際のクライアントをシミュレートして「ログイン → オンライン → 能動的切断 → 再接続ログイン」を繰り返し：

```bash
# デフォルトパラメータで実行
./scripts/multi/run-bots-rpc.sh

# カスタムパラメータ
BOT_COUNT=200 \
TCP_PORT=49100 \
LOGIN_URL=http://127.0.0.1:48080/game/api/ \
DISCONNECT_AFTER_LOGIN_SECONDS=20 \
RUN_SECONDS=600 \
./scripts/multi/run-bots-rpc.sh
```

利用可能な環境変数：

| 変数 | 説明 | デフォルト |
|:----|:-----|:---------|
| `BOT_COUNT` | ボット数 | `100` |
| `TCP_HOST` | TCP 接続ホスト | `127.0.0.1` |
| `TCP_PORT` | TCP 接続ポート | `49100` |
| `LOGIN_URL` | ログイン API URL | `http://127.0.0.1:48080/game/api/` |
| `SCENARIO` | ボットシナリオ | `login` |
| `DISCONNECT_LOOP` | 切断/再接続サイクルを繰り返し | `true` |
| `DISCONNECT_AFTER_LOGIN_SECONDS` | ログイン後切断遅延（秒） | `15` |
| `CONNECT_STAGGER_MS` | ボット接続のずらし間隔（ミリ秒） | `10` |
| `RUN_SECONDS` | 総実行時間（秒） | `180` |

#### トラブルシューティングコマンド

```bash
# 全サービスのログを確認
docker compose -f docker-compose.multi.yml logs -f

# 特定サービスのログを確認
docker compose -f docker-compose.multi.yml logs -f game-1 game-2 social-1 social-2

# リビルドして起動（コード変更後）
docker compose -f docker-compose.multi.yml up -d --build
```

---

### モニタリングとオブザーバビリティ

#### エンドポイント

| エンドポイント | 説明 |
|:-------------|:-----|
| `http://<host>:<HttpPort>/health` | ヘルスチェック（Aspire デフォルト。`/alive` ライブネスプローブも利用可能） |
| `http://<host>:<MetricsPort>/metrics` | Prometheus メトリクス |

#### メトリクスカテゴリ

- **データベース**: 操作レイテンシ（`db_operation_latency_ms`）、リトライ回数（`db_open_retry_total`）、ヘルスステータス（`db_health_status`）
- **ネットワーク**: 接続数、メッセージスループット、バイト転送量
- **ビジネス**: プレイヤーログイン数、アクティブセッション数
- **システム**: GC パフォーマンス、スレッドプールステータス

---

### テスト

#### テストの実行

```bash
# 全テストを実行
dotnet test

# 特定のテストプロジェクトを実行
dotnet test Tests/GameFrameX.Tests/GameFrameX.Tests.csproj

# 詳細出力で実行
dotnet test --logger "console;verbosity=detailed"
```

#### テストカバレッジ

テストスイートは **xUnit** ベースです — `Tests/GameFrameX.Tests` はフレームワーク層を、`Tests/GameFrameX.Hotfix.Tests` は Role 別のビジネスルールをカバーします：

| テストディレクトリ | 説明 |
|:----------------|:-----|
| `StartUp/` | 起動オーケストレーション、マルチロール選択、All-in-One オプション、設定起動バリデーター、HTTP ルート登録 |
| `Architecture/` | Roslyn アーキテクチャアナライザーテスト（レイヤリング規則、エージェントシーリング） |
| `Core/` | Actor とセッション管理テスト（重複ログイン） |
| `NetWork/` | SuperSocket KCP リッスン / 認証 / E2E テスト、HTTP・セッション認証ミドルウェアテスト |
| `DataBase/` | MongoDB・PostgreSQL Provider テスト（クエリ、接続、マルチデータベース、Provider リゾルバー） |
| `Discovery/` | サービスディスカバリーエンドポイント / ルーティング統合テスト（MongoDB・PostgreSQL） |
| `RemoteMessaging/` | クロスプロセスメッセージングテスト（コーデック、トランスポート） |
| `UnifiedMessaging/` | 統合クロスプロセスメッセージングテスト |
| `Online/` | Online Runtime テスト（管理 API、マッチメイキング、リーダーボード、シーズン、トーナメント、LiveOps、プレゼンス、セッション、処罰、監査、...） |
| `Topology/` | プロセストポロジ同型の等価性テスト |
| `Proto/` | ServerRole メッセージドメインテスト |
| `ProtoBuff/` | Protobuf シリアライズ・オブジェクトプールテスト |
| `Localization/` | ローカライゼーションキー値解析テスト |
| `Client/` | ボットクライアント実行オプション / トランスポートディスパッチテスト |
| `Utility/` | 数学/固定小数点テスト、圧縮、乱数、ID 生成、シングルトン、設定 |
| `GameFrameX.Hotfix.Tests/` | Role 別ビジネスルールテスト（Account / Login / Auth / Gateway / Chat / Mail / Friend / Team / Guild / Match / Room / Scene / World / Battle / Trade / Auction / Gm）＋イベントバインディングテスト |

---

## アーキテクチャ

```
┌─────────────────────────────────────────────────────────────────┐
│                       クライアント層                              │
│         Unity3D / Godot / LayaBox / Cocos Creator               │
├─────────────────────────────────────────────────────────────────┤
│                      ネットワーク層                               │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │   TCP    │ │WebSocket │ │   HTTP   │ │   KCP    │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
├─────────────────────────────────────────────────────────────────┤
│                    メッセージ処理層                                │
│  ┌────────────────┐ ┌────────────────┐ ┌────────────────┐      │
│  │TCP メッセージ   │ │  HTTP ハンドラ │ │クロスプロセス   │      │
│  │ハンドラ        │ │              │ │メッセージルータ │      │
│  └────────────────┘ └────────────────┘ └────────────────┘      │
├─────────────────────────────────────────────────────────────────┤
│                      Actor 層                                    │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │ プレイヤー│ │ サーバー │ │  アカウント│ │ グローバル│          │
│  │  Actor   │ │  Actor   │ │  Actor   │ │  Actor   │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
├─────────────────────────────────────────────────────────────────┤
│            コンポーネント・エージェント層（ホットアップデート境界）   │
│  ┌─────────────────────┐  ┌─────────────────────────────┐      │
│  │  Apps 層 (非ホット更) │  │ Hotfix 層 (ホット更可能)     │      │
│  │ StateComponent<T>   │←→│ StateComponentAgent<T,TState>│      │
│  │ BaseCacheState          │  │ ComponentAgent               │      │
│  └─────────────────────┘  └─────────────────────────────┘      │
├─────────────────────────────────────────────────────────────────┤
│                     データベース層                                │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │             MongoDB (デフォルト) / PostgreSQL              │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

---

### プロジェクト構成

```
GameFrameX.Server.Source/
├── GameFrameX.Launcher/              # アプリケーションエントリポイント（Game + 18 個の標準 Role 起動エントリ）
├── GameFrameX.StartUp/               # 起動オーケストレーションと初期化
├── GameFrameX.Core/                  # コアフレームワーク（Actor システム、コンポーネント、イベント、ホット更新管理）
├── GameFrameX.Apps/                  # 状態データ層（Account、Player、Game、ServerRole モジュール）— ホット更新不可
├── GameFrameX.Hotfix/                # ビジネスロジック層（HTTP、Player、サーバーハンドラ）— ホット更新可能
├── GameFrameX.Config/                # ゲーム設定テーブル（JSON 形式、LuBan 生成）
├── GameFrameX.Proto/                 # ProtoBuf プロトコル定義
├── GameFrameX.ProtoBuf.Net/          # ProtoBuf シリアライズ実装
├── GameFrameX.NetWork/               # ネットワークコア（TCP/UDP/KCP/WebSocket チャネル、メッセージオブジェクト、センダー）
├── GameFrameX.NetWork.Abstractions/  # ネットワークインターフェース（IMessage、IMessageHandler、メッセージマッピング）
├── GameFrameX.NetWork.HTTP/          # HTTP サーバー（Swagger、Kestrel、BaseHttpHandler）
├── GameFrameX.NetWork.RemoteMessaging/ # クロスプロセスリモートメッセージ（サーキットブレーカー、リトライ、コンシステントハッシング）
├── GameFrameX.Discovery/             # Aspire スタイルのサービスディスカバリー（services__{Role}__tcp__0 ブートストラップマップ）
├── GameFrameX.DataBase/              # データベース抽象レイヤー（マルチ Provider レジストリ、GameDb クエリ/更新/削除）
├── GameFrameX.DataBase.Mongo/        # MongoDB Provider（ヘルスモニタリング、リトライ、バッチ操作）
├── GameFrameX.DataBase.PostgreSql/   # PostgreSQL Provider（Npgsql、MongoDB Provider と同等の耐障害性）
├── GameFrameX.Online/                # Online プラットフォーム機能ライブラリ（マッチメイキング、リーダーボード、ソーシャル、シーズン、トーナメント、タイムライン、チャット監査、アセット、LiveOps、...）
├── GameFrameX.Online.Runtime/        # プロセス内 Online Runtime ホスト + 管理 API
├── GameFrameX.Localization/          # ローカライゼーションシステム（Keys.*.cs + .resx リソースファイル）
├── GameFrameX.Utility/               # ユーティリティ（設定、圧縮、乱数、スノーフレーク ID、オブジェクトプール、Mapster、Harmony）
├── GameFrameX.Client/                # テストクライアント（TCP/KCP ボットストレスクライアント）
├── GameFrameX.Architecture.Analyzers/         # Roslyn アーキテクチャアナライザー
├── GameFrameX.Hotfix.WrapperGenerator/ # Roslyn ソースジェネレーター（ホット更新プロキシラッパークラス）
└── Tests/
    ├── GameFrameX.Tests/             # xUnit テストスイート（フレームワーク層）
    └── GameFrameX.Hotfix.Tests/      # xUnit テストスイート（Role 別ホットフィックスビジネスルール）
```

---

## プロセストポロジ同型

サーバーは*プロセストポロジ同型*モデルをサポートします。同一のロールセットを、ロールごとに 1 プロセスで実行しても、単一の All-in-One プロセスで実行しても、ロールコードを変更せずに運用できます。

### マルチロール起動

- `--ServerType=Game,Social` — 登録済みの複数ロールを 1 プロセスで起動（優先順）。
- `--AllInOne` — 登録済みの全ロールを 1 プロセスで起動。
- `Configs/app_config.json` — 定義済みの各ロールごとに 1 セクションを同梱（全 19 セクション: Game、Social、その他 17 個の標準ロール）。各ロールは自身のセクションを解決し、プロセスレベルのフィールドはセクション間で一致している必要があります。

起動時バリデーター（`ConfigStartupValidator`）は以下の場合にフェイルファスト（競合フィールドとそのセクションをすべて列挙）します：

1. `--AllInOne` または複数指定の `--ServerType` で選択されたロールが `app_config.json` にセクションを持たない場合。
2. 同一プロセス内の 2 つのロールが、有効かつ非ゼロの同一リッスンエンドポイントにバインドする場合 — エンドポイントはポートフィールドとトランスポート（TCP vs UDP）を横断して比較されるため、例えば `Game.InnerPort == Social.HttpPort` もフェイルファスト対象です。同一ロール内での `InnerPort`/`OuterPort` 共用は正当な形式です。
3. プロセスレベルのフィールド（`SettingFieldLevel(ProcessLevel)`、例: `DataBaseUrl`）が選択されたセクション間で（ランタイム正規化ルール適用後も）異なる場合。共有カーネルが矛盾したプロセス設定を受け取ることはありません。
4. コマンドラインで明示指定されたフィールドが、使用中のファイルセクションと異なる場合（ファイルセクションが唯一の真実の情報源。CLI のロールレベル値はセクションなしフォールバック形式でのみ適用）。「明示指定」の判定は生の引数トークンに基づくため、デフォルトと同じ値の指定（例: `--HttpPort=0`）も比較対象です。`ServerType`（セクションキー）と `IsAllInOne` / `IsSingleMode` スイッチは除外されます。

シングルロールおよびデフォルトの起動コマンドの挙動は現状のままです（セクション欠落時はランチャーデフォルトにフォールバック）。

### Docker Compose ファイル

| ファイル | 用途 |
|:--|:--|
| `docker-compose.development.yml` | ローカル開発用: MongoDB サービス 1 つ（ホストポート `127.0.0.1:37017`、サンプルの `app_config.json` と一致） |
| `docker-compose.multi.yml` | マルチインスタンストポロジ形式。`scripts/multi/generate-docker-compose-multi.py` により**生成**されます。各インスタンスには `GameFrameX__AdvertiseHost` / `GameFrameX__AdvertisePort` / `GameFrameX__RoleInstanceId` と `services__{Role}__tcp__0` 静的ブートストラップマップが注入され、独自の `Configs/multi/{service}.json` セクション（`command` 引数と同一値）を `/app/Configs/app_config.json` としてマウント |
| `docker-compose.multi.legacy.yml` | 従来の静的形式。移行期間中は保持 |

トポロジを変更するには、ジェネレーターの `ROLES` 定義を編集し、`python3 scripts/multi/generate-docker-compose-multi.py` を再実行してください（冪等。`--check` はコミット済み compose ファイルとインスタンス別設定がジェネレーター出力と一致するか検証します）。認証なしの MongoDB サービスは `127.0.0.1` のみで公開されます。他ホストに公開する前に MongoDB 認証を有効化してください。

---

## 依存関係

| パッケージ | 説明 |
|:--|:--|
| `GameFrameX.Foundation.*` | ローカライズ、ログ、コマンドラインオプション、ORM 属性、ハッシュ、HTTP レスポンス正規化、ユーティリティ |
| `GameFrameX.SuperSocket.Server` / `.ClientEngine` / `.Udp` / `.Kcp` / `.WebSocket.Server` | TCP、UDP、KCP、WebSocket ネットワーク伝送 |
| `MongoDB.Driver` | MongoDB 永続化ドライバー |
| `Npgsql` | PostgreSQL 永続化ドライバー |
| `OpenTelemetry.*` + `Grafana.OpenTelemetry` | メトリクス、分散トレーシング、ランタイム計装 |
| `OpenTelemetry.Exporter.Prometheus.AspNetCore` | Prometheus `/metrics` スクレイピングエンドポイント |
| `Microsoft.Extensions.ServiceDiscovery` | Aspire スタイルのサービスディスカバリー |
| `Mapster` | オブジェクトマッピング |
| `Lib.Harmony` | ランタイムメソッドパッチ |
| `Quartz` | スケジュールタスク |
| `Swashbuckle.AspNetCore.SwaggerGen` | HTTP API の Swagger ドキュメント |
| `xunit` | ユニットテストフレームワーク |

---

## ドキュメントとリソース

- [公式ドキュメント](https://gameframex.doc.alianblank.com/)
- [GitHub リポジトリ](https://github.com/GameFrameX)
- [Gitee リポジトリ](https://gitee.com/GameFrameX)
- [CNB リポジトリ](https://cnb.cool/GameFrameX)
- [Unity クライアント](https://github.com/GameFrameX/GameFrameX.Unity)
- [イシュートラッカー](https://github.com/GameFrameX/GameFrameX/issues)
- [コミュニティディスカッション](https://github.com/GameFrameX/GameFrameX/discussions)

---

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

---

### コントリビュート

あらゆる形態の貢献を歓迎します！以下の手順に従ってください：

1. このリポジトリをフォーク
2. フィーチャーブランチを作成（`git checkout -b feature/amazing-feature`）
3. 変更をコミット（`git commit -m 'feat: 機能を追加'`）
4. ブランチにプッシュ（`git push origin feature/amazing-feature`）
5. Pull Request を作成

コミットメッセージは [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) 仕様に従ってください。

---

## 変更履歴

GameFrameX Server のバージョン履歴については、[CHANGELOG.md](CHANGELOG.md) をご参照ください。

---

## ライセンス

詳しくは [LICENSE](LICENSE) をご参照ください。

<!--
EN: See [LICENSE](LICENSE) for license information.
zh-CN: 详见 [LICENSE](LICENSE) 文件。
zh-TW: 詳見 [LICENSE](LICENSE) 檔案。
ja: 詳しくは [LICENSE](LICENSE) をご参照ください。
ko: 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.
-->

---

<div align="center">

**このプロジェクトが役に立ったら、Star をお願いします**

**Made by GameFrameX Team**

</div>
