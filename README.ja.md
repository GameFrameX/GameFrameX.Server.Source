# GameFrameX Server

**高性能・クロスプラットフォームのゲームサーバーフレームワーク**

[![License](https://img.shields.io/badge/license-MIT%20%7C%20Apache%202.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey.svg)]()
[![Architecture](https://img.shields.io/badge/architecture-Actor%20Model-orange.svg)]()
[![Version](https://img.shields.io/github/v/release/GameFrameX/GameFrameX.Server.Source?label=version&color=green)](https://github.com/GameFrameX/GameFrameX.Server.Source/releases)

🌐 **言語**: [English](README_EN.md) | [简体中文](README.md) | [繁體中文](README.zh-TW.md) | **日本語** | [한국어](README.ko.md)

## 目次

- [プロジェクト概要](#プロジェクト概要)
- [コア機能](#コア機能)
- [システムアーキテクチャ](#システムアーキテクチャ)
- [プロジェクト構成](#プロジェクト構成)
- [クイックスタート](#クイックスタート)
- [設定管理](#設定管理)
- [ビジネスロジック開発](#ビジネスロジック開発)
- [ホットアップデート機構](#ホットアップデート機構)
- [Docker デプロイ](#docker-デプロイ)
- [マルチプロセス・クロスプロセス連携](#マルチプロセスクロスプロセス連携)
- [モニタリングとオブザーバビリティ](#モニタリングとオブザーバビリティ)
- [テスト](#テスト)
- [コントリビュート](#コントリビュート)
- [ライセンス](#ライセンス)
- [関連リンク](#関連リンク)

---

## プロジェクト概要

GameFrameX Server は、C# .NET 10.0 で開発された高性能・クロスプラットフォームのゲームサーバーフレームワークです。Actor モデルを採用し、ホットアップデート機構をサポートしています。マルチプレイヤーオンラインゲーム開発向けに設計されており、Unity3D、Godot、LayaBox など多様なクライアントプラットフォームとの統合をサポートします。

**設計理念**: 大道至簡、シンプルイズベスト

## コア機能

### 高性能アーキテクチャ

- **Actor モデル**: TPL DataFlow 上に構築されたロックフリー・高同時実行システム。メッセージパッシングにより従来のロックのパフォーマンス劣化を回避
- **完全非同期プログラミング**: 完全な async/await 非同期プログラミングモデル
- **ゼロロック設計**: Actor 内部状態はメッセージキューによる直列化アクセスでロック不要
- **バッチ永続化**: バッチDB書き込みをサポート。バッチサイズとタイムアウト設定可能
- **スノーフレーク ID 生成**: 分散ユニーク ID ジェネレーター内蔵。ワーカーノード・データセンター設定対応

### ホットアップデートシステム

- **ゼロダウンタイム更新**: 実行時に新しいロジックアセンブリをロード。サービス停止不要
- **状態・ロジック分離**: 永続化状態データ（Apps 層）とホットアップデート可能なビジネスロジック（Hotfix 層）を厳密に分離
- **グレースフル移行**: 旧アセンブリは10分間の猶予期間を保持。進行中のリクエスト完了後にアンロード
- **バージョン管理**: HTTP エンドポイント経由でバージョン番号を指定してロード可能

### マルチプロトコルネットワーク通信

- **TCP**: SuperSocket ベースの高性能 TCP サーバー。メインゲーム通信プロトコル
- **UDP**: オプションの UDP プロトコルサポート
- **WebSocket**: SuperSocket WebSocket ベースの双方向通信
- **HTTP/HTTPS**: Kestrel ベースの HTTP サービス。Swagger ドキュメント、CORS、ヘルスチェック対応
- **KCP**: KCP プロトコルベースの UDP 信頼性伝送（実験的）
- **クロスプロセスメッセージング**: RemoteMessaging モジュール内蔵。サーキットブレーカー、リトライ戦略、コンシステントハッシングシャーディング対応

### データベースと永続化

- **MongoDB プライマリDB**: 完全な MongoDB 統合。ヘルスステートマシン対応（Healthy → Degraded → Unhealthy → Recovering）
- **透過的永続化**: StateComponent の自動シリアライズ/デシリアライズ。定期的バッチ ReplaceOne 操作で永続化
- **接続プール管理**: 設定可能な接続プールとリトライ戦略
- **OpenTelemetry 統合**: データベース操作メトリクス（レイテンシ、リトライ回数、ヘルスステータス）

### モニタリングとオブザーバビリティ

- **OpenTelemetry**: 包括的なメトリクス（Metrics）、トレーシング（Tracing）、ロギング（Logging）
- **Prometheus**: ネイティブメトリクスエクスポートエンドポイント
- **Grafana Loki**: ログ集約出力対応
- **Serilog**: 構造化ログ。コンソール、ファイル、Loki マルチ出力対応

---

## システムアーキテクチャ

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
│  │ CacheState          │  │ ComponentAgent               │      │
│  └─────────────────────┘  └─────────────────────────────┘      │
├─────────────────────────────────────────────────────────────────┤
│                     データベース層                                │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                    MongoDB                               │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

---

## プロジェクト構成

```
Server/
├── GameFrameX.Launcher/              # アプリケーションエントリポイント
├── GameFrameX.StartUp/               # 起動オーケストレーションと初期化
├── GameFrameX.Core/                  # コアフレームワーク（Actor システム、コンポーネント、イベント、ホット更新管理）
├── GameFrameX.Apps/                  # 状態データ層（アカウント、プレイヤー、サーバーモジュール）— ホット更新不可
├── GameFrameX.Hotfix/                # ビジネスロジック層（HTTP、プレイヤー、サーバーハンドラ）— ホット更新可能
├── GameFrameX.Config/                # ゲーム設定テーブル（JSON 形式、LuBan 生成）
├── GameFrameX.Core.Config/           # コア設定管理
├── GameFrameX.Proto/                 # ProtoBuf プロトコル定義
├── GameFrameX.ProtoBuf.Net/          # ProtoBuf シリアライズ実装
├── GameFrameX.NetWork/               # ネットワークコア（メッセージオブジェクト、センダー、WebSocket）
├── GameFrameX.NetWork.Abstractions/  # ネットワークインターフェース（IMessage、IMessageHandler、メッセージマッピング）
├── GameFrameX.NetWork.HTTP/          # HTTP サーバー（Swagger、Kestrel、BaseHttpHandler）
├── GameFrameX.NetWork.Kcp/           # KCP プロトコルサポート（UDP ベースの信頼性伝送）
├── GameFrameX.NetWork.Message/       # メッセージパイプラインとコーデック
├── GameFrameX.NetWork.RemoteMessaging/ # クロスプロセスリモートメッセージ（サーキットブレーカー、リトライ、コンシステントハッシング）
├── GameFrameX.DataBase/              # データベース抽象レイヤー
├── GameFrameX.DataBase.Mongo/        # MongoDB 実装（ヘルスモニタリング、リトライ、バッチ操作）
├── GameFrameX.Localization/          # ローカライゼーションシステム（Keys.*.cs + .resx リソースファイル）
├── GameFrameX.Monitor/               # OpenTelemetry + Prometheus メトリクス統合
├── GameFrameX.Utility/               # ユーティリティ（ログ、圧縮、オブジェクトプール、Mapster、Harmony）
├── GameFrameX.Client/                # テストクライアント（TCP 接続）
├── GameFrameX.CodeGenerator/         # Roslyn ソースジェネレーター（ホット更新プロキシラッパークラス）
├── GameFrameX.AppHost/               # .NET Aspire アプリケーションホスト
├── GameFrameX.AppHost.ServiceDefaults/ # Aspire 共有デフォルト設定（OTel、サービスディスカバリ）
└── Tests/
    └── GameFrameX.Tests/             # xUnit テストスイート
```

---

## クイックスタート

### 前提条件

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [MongoDB 4.x+](https://www.mongodb.com/try/download/community)
- Visual Studio 2022 または JetBrains Rider（推奨）

### インストール手順

1. **リポジトリをクローン**
   ```bash
   git clone https://github.com/GameFrameX/GameFrameX.git
   cd GameFrameX/Server
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
   - ヘルスチェック: `http://localhost:28080/game/api/health`
   - コンソールログで起動成功を確認

---

## 設定管理

GameFrameX はコマンドライン引数（`--Key=Value`）で設定を行います。すべての設定項目は `StartupOptions` クラスで定義されています。

### サーバー設定

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

### ネットワーク設定

| 設定項目 | 説明 | デフォルト | 例 |
|:--------|:-----|:----------|:---|
| `InnerHost` | 内部通信用 IP（クラスタ間） | `0.0.0.0` | `0.0.0.0` |
| `InnerPort` | 内部通信用ポート | `8888` | `29100` |
| `OuterHost` | 外部通信用 IP（クライアント向け） | `0.0.0.0` | `0.0.0.0` |
| `OuterPort` | 外部通信用ポート | なし | `29100` |
| `IsEnableTcp` | TCP サービスを有効化 | `true` | `true` |
| `IsEnableUdp` | UDP サービスを有効化 | `false` | `true` |
| `IsEnableWebSocket` | WebSocket を有効化 | `false` | `true` |
| `WsPort` | WebSocket ポート | `8889` | `29300` |
| `IsEnableHttp` | HTTP サービスを有効化 | `true` | `true` |
| `HttpPort` | HTTP サービスポート | `8080` | `28080` |
| `HttpsPort` | HTTPS サービスポート | なし | `443` |
| `HttpUrl` | API ルートパス | `/game/api/` | `/game/api/` |
| `HttpIsDevelopment` | HTTP 開発モード（Swagger を有効化） | `false` | `true` |

### データベース設定

| 設定項目 | 説明 | デフォルト | 例 |
|:--------|:-----|:----------|:---|
| `DataBaseUrl` | MongoDB 接続文字列 | なし | `mongodb://localhost:27017` |
| `DataBaseName` | データベース名 | なし | `gameframex` |
| `DataBasePassword` | データベースパスワード | なし | `your_password` |

### Actor 設定

| 設定項目 | 説明 | デフォルト | 例 |
|:--------|:-----|:----------|:---|
| `ActorTimeOut` | Actor タスク実行タイムアウト（ミリ秒） | `30000` | `60000` |
| `ActorQueueTimeOut` | Actor キュータイムアウト（ミリ秒） | `30000` | `60000` |
| `ActorRecycleTime` | Actor アイドルリサイクル時間（分） | `15` | `30` |
| `SaveDataInterval` | データ保存間隔（ミリ秒） | `30000` | `60000` |
| `SaveDataBatchCount` | バッチ保存数 | `500` | `1000` |
| `SaveDataBatchTimeOut` | バッチ保存タイムアウト（ミリ秒） | `30000` | `60000` |

### ログ設定

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

### モニタリング設定

| 設定項目 | 説明 | デフォルト | 例 |
|:--------|:-----|:----------|:---|
| `IsOpenTelemetry` | OpenTelemetry を有効化 | `false` | `true` |
| `IsOpenTelemetryMetrics` | メトリクス収集を有効化 | `false` | `true` |
| `IsOpenTelemetryTracing` | 分散トレーシングを有効化 | `false` | `true` |
| `MetricsPort` | Prometheus メトリクスポート | `0`（HTTP ポートを共用） | `9090` |
| `IsMonitorMessageTimeOut` | メッセージ処理タイムアウト監視 | `false` | `true` |
| `MonitorMessageTimeOutSeconds` | タイムアウト閾値（秒） | `1` | `5` |

### ID 生成設定

| 設定項目 | 説明 | デフォルト | 例 |
|:--------|:-----|:----------|:---|
| `WorkerId` | スノーフレーク ID ワーカーノード ID | `1` | `2` |
| `DataCenterId` | スノーフレーク ID データセンター ID | `1` | `2` |

### 起動コマンド例

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

## ビジネスロジック開発

### コンポーネント・エージェントパターン

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

### HTTP ハンドラ

HTTP ハンドラは `BaseHttpHandler` を継承し、`[HttpMessageMapping]` 属性でルートを登録します。

```csharp
[HttpMessageMapping(typeof(GetPlayerInfoHandler))]
[Description("プレイヤー情報を取得")]
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

### TCP/RPC メッセージハンドラ

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

### イベントハンドラ

イベントシステムは Actor 間の疎結合通信に使用します。

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

        // プレイヤーログインイベントの処理
        return agent.OnLogin();
    }
}
```

---

## ホットアップデート機構

### アーキテクチャ原理

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

### ホットアップデートフロー

1. **新ロジックのコンパイル**: 更新された `GameFrameX.Hotfix.dll` をビルド
2. **アセンブリのデプロイ**: サーバーの指定ディレクトリにコピー
3. **リロードのトリガー**: HTTP エンドポイント経由でホットアップデートリクエストを発行
4. **アセンブリロード**: `HotfixManager` が回収可能な `AssemblyLoadContext` で新 DLL をロード
5. **タイプスキャン**: `HotfixModule` が新アセンブリ内のエージェント、ハンドラ、イベントリスナーをスキャン
6. **エージェント切り替え**: `ActorManager.ClearAgent()` がキャッシュされたエージェントインスタンスをクリア
7. **グレースフル移行**: 旧アセンブリは10分間の猶予期間を保持。進行中のリクエスト完了後にアンロード

### ホットアップデート API

```bash
# ホットアップデートのトリガー（バージョン指定）
curl -X POST "http://localhost:28080/game/api/Reload?version=1.7.2"
```

---

## Docker デプロイ

### 単一インスタンスデプロイ

`docker-compose.yml` を使用して MongoDB + Game + Social の完全環境を起動：

```bash
# ビルドして起動
docker compose up -d --build

# 実行状態を確認
docker compose ps

# ログを確認
docker compose logs -f game social

# 停止
docker compose down
```

サービスポートマッピング：

| サービス | コンテナポート | ホストポート | 説明 |
|:--------|:-------------|:-----------|:-----|
| MongoDB | 27017 | 37017 | データベース |
| Game TCP | 29100 | 39100 | ゲームサーバー |
| Game HTTP | 28080 | 38080 | ゲームサーバー HTTP API |
| Social TCP | 29400 | 39400 | ソーシャルサーバー |
| Social HTTP | 28081 | 38081 | ソーシャルサーバー HTTP API |

### マルチインスタンスデプロイ

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

### カスタムビルド

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

## マルチプロセス・クロスプロセス連携

### クロスプロセススモークテスト

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

### ボットストレステスト

実際のクライアントをシミュレートして「ログイン → オンライン → 能動的切断 → 再接続ログイン」を繰り返し：

```bash
# デフォルトパラメータで実行
./scripts/multi/run-bots-rpc.sh

# カスタムパラメータ
BOT_COUNT=200 \
TCP_PORT=49100 \
LOGIN_URL=http://127.0.0.1:48080/game/api/ \
DISCONNECT_AFTER_LOGIN_SECONDS=20 \
RUN_SECONDS=300 \
./scripts/multi/run-bots-rpc.sh
```

オプション環境変数：

| 変数 | 説明 | デフォルト |
|:----|:-----|:---------|
| `BOT_COUNT` | ボット数 | — |
| `TCP_PORT` | TCP 接続ポート | `49100` |
| `LOGIN_URL` | ログイン API URL | `http://127.0.0.1:48080/game/api/` |
| `DISCONNECT_AFTER_LOGIN_SECONDS` | ログイン後切断遅延（秒） | `20` |
| `RUN_SECONDS` | 総実行時間（秒） | `300` |

### トラブルシューティングコマンド

```bash
# 全サービスのログを確認
docker compose -f docker-compose.multi.yml logs -f

# 特定サービスのログを確認
docker compose -f docker-compose.multi.yml logs -f game-1 game-2 social-1 social-2

# リビルドして起動（コード変更後）
docker compose -f docker-compose.multi.yml up -d --build
```

---

## モニタリングとオブザーバビリティ

### エンドポイント

| エンドポイント | 説明 |
|:-------------|:-----|
| `http://<host>:<HttpPort>/game/api/health` | ヘルスチェック |
| `http://<host>:<MetricsPort>/metrics` | Prometheus メトリクス |

### メトリクスカテゴリ

- **データベース**: 操作レイテンシ（`db_operation_latency_ms`）、リトライ回数（`db_open_retry_total`）、ヘルスステータス（`db_health_status`）
- **ネットワーク**: 接続数、メッセージスループット、バイト転送量
- **ビジネス**: プレイヤーログイン数、アクティブセッション数
- **システム**: GC パフォーマンス、スレッドプールステータス

---

## テスト

### テストの実行

```bash
# 全テストを実行
dotnet test

# 特定のテストプロジェクトを実行
dotnet test Tests/GameFrameX.Tests/GameFrameX.Tests.csproj

# 詳細出力で実行
dotnet test --logger "console;verbosity=detailed"
```

### テストカバレッジ

テストプロジェクトは **xUnit** ベースで、以下のモジュールをカバーしています：

| テストディレクトリ | 説明 |
|:----------------|:-----|
| `Utility/` | 数学/固定小数点テスト、圧縮、乱数、ID 生成、シングルトン |
| `NetWork/Kcp/` | KCP パイプラインフィルター、セッション管理、サーバー統合テスト |
| `DataBase/` | MongoDB 接続・クエリテスト |
| `ProtoBuff/` | Protobuf シリアライズ・オブジェクトプールテスト |
| `Localization/` | ローカライゼーションキー値解析テスト |
| `RemoteMessaging/` | クロスプロセスメッセージングテスト |
| `UnifiedMessaging/` | 統合クロスプロセスメッセージングテスト |
| `StartUp/` | HTTP サーバールート登録テスト |

---

## コントリビュート

あらゆる形態の貢献を歓迎します！以下の手順に従ってください：

1. このリポジトリをフォーク
2. フィーチャーブランチを作成（`git checkout -b feature/amazing-feature`）
3. 変更をコミット（`git commit -m 'feat: 機能を追加'`）
4. ブランチにプッシュ（`git push origin feature/amazing-feature`）
5. Pull Request を作成

コミットメッセージは [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) 仕様に従ってください。

---

## ライセンス

本プロジェクトは **MIT ライセンス** と **Apache License 2.0** のデュアルライセンスで配布されています。詳細は [LICENSE](LICENSE) ファイルを参照してください。

---

## 関連リンク

- [公式ドキュメント](https://gameframex.doc.alianblank.com/)
- [GitHub リポジトリ](https://github.com/GameFrameX)
- [Gitee リポジトリ](https://gitee.com/GameFrameX)
- [CNB リポジトリ](https://cnb.cool/GameFrameX)
- [Unity クライアント](https://github.com/GameFrameX/GameFrameX.Unity)
- [イシュートラッカー](https://github.com/GameFrameX/GameFrameX/issues)
- [コミュニティディスカッション](https://github.com/GameFrameX/GameFrameX/discussions)

---

<div align="center">

**このプロジェクトが役に立ったら、Star をお願いします**

**Made by GameFrameX Team**

</div>
