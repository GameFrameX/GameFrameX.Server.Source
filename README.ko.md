<div align="center">

![GameFrameX Logo](https://download.alianblank.com/gameframex/gameframex_logo_320.png)

# GameFrameX Server

[![Version](https://img.shields.io/github/v/release/GameFrameX/GameFrameX.Server.Source?label=version&color=green)](https://github.com/GameFrameX/GameFrameX.Server.Source/releases)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![Documentation](https://img.shields.io/badge/docs-gameframex.doc.alianblank.com-brightgreen.svg)](https://gameframex.doc.alianblank.com)

**고성능, 크로스 플랫폼 게임 서버 프레임워크**

[📖 문서](https://gameframex.doc.alianblank.com) • [🚀 빠른 시작](#빠른-시작) • [💬 QQ 그룹: 467608841](https://qm.qq.com/cgi-bin/qm/qr?k=sYFd1nv6m2KZIWFLorZ5pBR0AE5ZhbuL&jump_from=webapi&authKey=oCu+uoL3n35fT5SEt7iLgGtROPxh31n/rHUxRlp0w1f+j38W4tKBuWyRH3KEdwHN)

---

🌐 **언어**: [English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | **한국어**

---

</div>

## 목차

- [프로젝트 개요](#프로젝트-개요)
- [핵심 기능](#핵심-기능)
- [시스템 아키텍처](#시스템-아키텍처)
- [프로젝트 구조](#프로젝트-구조)
- [빠른 시작](#빠른-시작)
- [설정 관리](#설정-관리)
- [비즈니스 로직 개발](#비즈니스-로직-개발)
- [핫 업데이트 메커니즘](#핫-업데이트-메커니즘)
- [Docker 배포](#docker-배포)
- [멀티 프로세스 크로스 프로세스 디버깅](#멀티-프로세스-크로스-프로세스-디버깅)
- [모니터링 및 관측 가능성](#모니터링-및-관측-가능성)
- [테스트](#테스트)
- [기여 가이드](#기여-가이드)
- [라이선스](#라이선스)
- [관련 링크](#관련-링크)

---

## 프로젝트 개요

GameFrameX Server는 C# .NET 10.0으로 개발된 고성능, 크로스 플랫폼 게임 서버 프레임워크입니다. Actor 모델을 채택하고 핫 업데이트 메커니즘을 지원합니다. 멀티플레이어 온라인 게임 개발을 위해 설계되었으며 Unity3D, Godot, LayaBox 등 다양한 클라이언트 플랫폼 통합을 지원합니다.

**설계 철학**: 대도지간, 심플 이즈 베스트

## 핵심 기능

### 고성능 아키텍처

- **Actor 모델**: TPL DataFlow 기반의 락프리 고동시성 시스템. 메시지 전달을 통해 기존 락 성능 저하 회피
- **완전 비동기 프로그래밍**: 완전한 async/await 비동기 프로그래밍 모델
- **제로 락 설계**: Actor 내부 상태는 메시지 큐를 통한 직렬화 접근으로 락 불필요
- **배치 영속화**: 배치 DB 쓰기를 지원하며 배치 크기와 타임아웃 설정 가능
- **스노우플레이크 ID 생성**: 분산 유니크 ID 생성기 내장. 워커 노드 및 데이터센터 설정 지원

### 핫 업데이트 시스템

- **제로 다운타임 업데이트**: 런타임에 새로운 로직 어셈블리 로드. 서비스 중지 불필요
- **상태-로직 분리**: 영속화 상태 데이터(Apps 레이어)와 핫 업데이트 가능한 비즈니스 로직(Hotfix 레이어)을 엄격히 분리
- **그레이스풀 전환**: 이전 어셈블리는 10분 유예 기간을 유지하며 진행 중인 요청 완료 후 언로드
- **버전 관리**: HTTP 엔드포인트를 통해 버전 번호를 지정하여 로드 가능

### 멀티 프로토콜 네트워크 통신

- **TCP**: SuperSocket 기반의 고성능 TCP 서버. 메인 게임 통신 프로토콜
- **UDP**: 선택적 UDP 프로토콜 지원
- **WebSocket**: SuperSocket WebSocket 기반의 양방향 통신
- **HTTP/HTTPS**: Kestrel 기반 HTTP 서비스. Swagger 문서, CORS, 헬스 체크 지원
- **KCP**: KCP 프로토콜 기반 UDP 신뢰성 전송(실험적)
- **크로스 프로세스 메시징**: RemoteMessaging 모듈 내장. 서킷 브레이커, 재시도 전략, 일관 해싱 샤딩 지원

### 데이터베이스 및 영속화

- **MongoDB 주 데이터베이스**: 완전한 MongoDB 통합. 헬스 상태 머신 지원(Healthy → Degraded → Unhealthy → Recovering)
- **투명한 영속화**: StateComponent 자동 직렬화/역직렬화. 정기적 배치 ReplaceOne 작업으로 영속화
- **연결 풀 관리**: 설정 가능한 연결 풀 및 재시도 전략
- **OpenTelemetry 통합**: 데이터베이스 작업 메트릭(지연 시간, 재시도 횟수, 헬스 상태)

### 모니터링 및 관측 가능성

- **OpenTelemetry**: 포괄적인 메트릭(Metrics), 트레이싱(Tracing), 로깅(Logging)
- **Prometheus**: 네이티브 메트릭 익스포트 엔드포인트
- **Grafana Loki**: 로그 집계 출력 지원
- **Serilog**: 구조화된 로깅. 콘솔, 파일, Loki 멀티 출력 지원

---

## 시스템 아키텍처

```
┌─────────────────────────────────────────────────────────────────┐
│                        클라이언트 레이어                          │
│         Unity3D / Godot / LayaBox / Cocos Creator               │
├─────────────────────────────────────────────────────────────────┤
│                       네트워크 레이어                             │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │   TCP    │ │WebSocket │ │   HTTP   │ │   KCP    │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
├─────────────────────────────────────────────────────────────────┤
│                     메시지 처리 레이어                             │
│  ┌────────────────┐ ┌────────────────┐ ┌────────────────┐      │
│  │TCP 메시지       │ │  HTTP 핸들러   │ │크로스 프로세스  │      │
│  │핸들러          │ │               │ │메시지 라우터    │      │
│  └────────────────┘ └────────────────┘ └────────────────┘      │
├─────────────────────────────────────────────────────────────────┤
│                       Actor 레이어                               │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │ 플레이어 │ │  서버    │ │  계정    │ │ 글로벌   │           │
│  │  Actor   │ │  Actor   │ │  Actor   │ │  Actor   │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
├─────────────────────────────────────────────────────────────────┤
│         컴포넌트-에이전트 레이어(핫 업데이트 경계)                 │
│  ┌─────────────────────┐  ┌─────────────────────────────┐      │
│  │ Apps 레이어 (핫불가)  │  │ Hotfix 레이어 (핫 가능)      │      │
│  │ StateComponent<T>   │←→│ StateComponentAgent<T,TState>│      │
│  │ CacheState          │  │ ComponentAgent               │      │
│  └─────────────────────┘  └─────────────────────────────┘      │
├─────────────────────────────────────────────────────────────────┤
│                      데이터베이스 레이어                          │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                    MongoDB                               │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

---

## 프로젝트 구조

```
Server/
├── GameFrameX.Launcher/              # 애플리케이션 진입점
├── GameFrameX.StartUp/               # 시작 오케스트레이션 및 초기화
├── GameFrameX.Core/                  # 코어 프레임워크(Actor 시스템, 컴포넌트, 이벤트, 핫 업데이트 관리)
├── GameFrameX.Apps/                  # 상태 데이터 레이어(계정, 플레이어, 서버 모듈) — 핫 업데이트 불가
├── GameFrameX.Hotfix/                # 비즈니스 로직 레이어(HTTP, 플레이어, 서버 핸들러) — 핫 업데이트 가능
├── GameFrameX.Config/                # 게임 설정 테이블(JSON 형식, LuBan 생성)
├── GameFrameX.Core.Config/           # 코어 설정 관리
├── GameFrameX.Proto/                 # ProtoBuf 프로토콜 정의
├── GameFrameX.ProtoBuf.Net/          # ProtoBuf 직렬화 구현
├── GameFrameX.NetWork/               # 네트워크 코어(메시지 객체, 센더, WebSocket)
├── GameFrameX.NetWork.Abstractions/  # 네트워크 인터페이스(IMessage, IMessageHandler, 메시지 매핑)
├── GameFrameX.NetWork.HTTP/          # HTTP 서버(Swagger, Kestrel, BaseHttpHandler)
├── GameFrameX.NetWork.Kcp/           # KCP 프로토콜 지원(UDP 기반 신뢰성 전송)
├── GameFrameX.NetWork.Message/       # 메시지 파이프라인 및 코덱
├── GameFrameX.NetWork.RemoteMessaging/ # 크로스 프로세스 원격 메시지(서킷 브레이커, 재시도, 일관 해싱)
├── GameFrameX.DataBase/              # 데이터베이스 추상 레이어
├── GameFrameX.DataBase.Mongo/        # MongoDB 구현(헬스 모니터링, 재시도, 배치 작업)
├── GameFrameX.Localization/          # 현지화 시스템(Keys.*.cs + .resx 리소스 파일)
├── GameFrameX.Monitor/               # OpenTelemetry + Prometheus 메트릭 통합
├── GameFrameX.Utility/               # 유틸리티(로깅, 압축, 오브젝트 풀, Mapster, Harmony)
├── GameFrameX.Client/                # 테스트 클라이언트(TCP 연결)
├── GameFrameX.Architecture.Analyzers/         # Roslyn 아키텍처 분석기
├── GameFrameX.Hotfix.WrapperGenerator/ # Roslyn 소스 제너레이터(핫 업데이트 프록시 래퍼 클래스)
├── GameFrameX.AppHost/               # .NET Aspire 애플리케이션 호스트
├── GameFrameX.AppHost.ServiceDefaults/ # Aspire 공유 기본 설정(OTel, 서비스 디스커버리)
└── Tests/
    └── GameFrameX.Tests/             # xUnit 테스트 스위트
```

---

## 빠른 시작

### 요구 사항

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)만 지원합니다. .NET 8/9는 지원하지 않습니다.
- [MongoDB 4.x+](https://www.mongodb.com/try/download/community)
- Visual Studio 2022 또는 JetBrains Rider(권장)

### 설치 단계

1. **저장소 클론**
   ```bash
   git clone https://github.com/GameFrameX/GameFrameX.git
   cd GameFrameX/Server
   ```

2. **종속성 복원**
   ```bash
   dotnet restore
   ```

3. **프로젝트 빌드**
   ```bash
   dotnet build
   ```

4. **MongoDB 시작**
   ```bash
   # 로컬 설치
   mongod --dbpath /path/to/data

   # 또는 Docker 사용
   docker run -d -p 27017:27017 --name mongo mongo:8.2
   ```

5. **서버 실행**
   ```bash
   dotnet run --project GameFrameX.Launcher -- \
       --ServerType=Game \
       --ServerId=1000 \
       --OuterPort=29100 \
       --HttpPort=28080 \
       --DataBaseUrl=mongodb://127.0.0.1:27017 \
       --DataBaseName=gameframex
   ```

6. **시작 확인**
   - 헬스 체크: `http://localhost:28080/game/api/health`
   - 콘솔 로그에서 시작 성공 확인

---

## 설정 관리

GameFrameX는 명령줄 인수(`--Key=Value`)로 설정합니다. 모든 설정 항목은 `StartupOptions` 클래스에 정의되어 있습니다.

### 서버 설정

| 설정 항목 | 설명 | 기본값 | 예시 |
|:---------|:-----|:------|:-----|
| `ServerType` | 서버 유형(필수) | 없음 | `Game`, `Social` |
| `ServerId` | 서버 고유 ID | 없음 | `1000` |
| `ServerInstanceId` | 서버 인스턴스 ID(동일 유형의 다른 인스턴스 구분) | `0` | `1001` |
| `IsSingleMode` | 단일 프로세스 모드 여부 | `false` | `true` |
| `MinModuleId` | 비즈니스 모듈 시작 ID(모듈 샤딩) | `0` | `100` |
| `MaxModuleId` | 비즈니스 모듈 종료 ID(모듈 샤딩) | `0` | `1000` |
| `TimeZone` | 서버 시간대 | `Asia/Shanghai` | `UTC` |
| `IsUseTimeZone` | 커스텀 시간대 활성화 | `false` | `true` |
| `Language` | 언어 설정 | 없음 | `zh-CN` |

### 네트워크 설정

| 설정 항목 | 설명 | 기본값 | 예시 |
|:---------|:-----|:------|:-----|
| `InnerHost` | 내부 통신 IP(클러스터 간) | `0.0.0.0` | `0.0.0.0` |
| `InnerPort` | 내부 통신 포트 | `8888` | `29100` |
| `OuterHost` | 외부 통신 IP(클라이언트 대상) | `0.0.0.0` | `0.0.0.0` |
| `OuterPort` | 외부 통신 포트 | 없음 | `29100` |
| `IsEnableTcp` | TCP 서비스 활성화 | `true` | `true` |
| `IsEnableUdp` | UDP 서비스 활성화 | `false` | `true` |
| `IsEnableWebSocket` | WebSocket 활성화 | `false` | `true` |
| `WsPort` | WebSocket 포트 | `8889` | `29300` |
| `IsEnableHttp` | HTTP 서비스 활성화 | `true` | `true` |
| `HttpPort` | HTTP 서비스 포트 | `8080` | `28080` |
| `HttpsPort` | HTTPS 서비스 포트 | 없음 | `443` |
| `HttpUrl` | API 루트 경로 | `/game/api/` | `/game/api/` |
| `HttpIsDevelopment` | HTTP 개발 모드(Swagger 활성화) | `false` | `true` |

### 데이터베이스 설정

| 설정 항목 | 설명 | 기본값 | 예시 |
|:---------|:-----|:------|:-----|
| `DataBaseUrl` | MongoDB 연결 문자열 | 없음 | `mongodb://localhost:27017` |
| `DataBaseName` | 데이터베이스 이름 | 없음 | `gameframex` |
| `DataBasePassword` | 데이터베이스 비밀번호 | 없음 | `your_password` |

### Actor 설정

| 설정 항목 | 설명 | 기본값 | 예시 |
|:---------|:-----|:------|:-----|
| `ActorTimeOut` | Actor 작업 실행 타임아웃(밀리초) | `30000` | `60000` |
| `ActorQueueTimeOut` | Actor 큐 타임아웃(밀리초) | `30000` | `60000` |
| `ActorRecycleTime` | Actor 유휴 리사이클 시간(분) | `15` | `30` |
| `SaveDataInterval` | 데이터 저장 간격(밀리초) | `30000` | `60000` |
| `SaveDataBatchCount` | 배치 저장 수 | `500` | `1000` |
| `SaveDataBatchTimeOut` | 배치 저장 타임아웃(밀리초) | `30000` | `60000` |

### 로깅 설정

| 설정 항목 | 설명 | 기본값 | 예시 |
|:---------|:-----|:------|:-----|
| `IsDebug` | 디버그 로깅 마스터 스위치 | `false` | `true` |
| `LogIsConsole` | 콘솔 출력 | `true` | `false` |
| `LogIsWriteToFile` | 파일 출력 | `true` | `false` |
| `LogEventLevel` | 로그 레벨 | `Debug` | `Information` |
| `LogRollingInterval` | 로그 롤링 간격 | `Day` | `Hour` |
| `LogIsFileSizeLimit` | 단일 파일 크기 제한 | `true` | `false` |
| `LogFileSizeLimitBytes` | 파일 크기 제한 | `104857600` (100MB) | `52428800` |
| `LogRetainedFileCountLimit` | 보관 파일 수 | `31` | `90` |
| `LogIsGrafanaLoki` | Grafana Loki 출력 | `false` | `true` |
| `LogGrafanaLokiUrl` | Grafana Loki URL | `http://localhost:3100` | — |

### 모니터링 설정

| 설정 항목 | 설명 | 기본값 | 예시 |
|:---------|:-----|:------|:-----|
| `IsOpenTelemetry` | OpenTelemetry 활성화 | `false` | `true` |
| `IsOpenTelemetryMetrics` | 메트릭 수집 활성화 | `false` | `true` |
| `IsOpenTelemetryTracing` | 분산 트레이싱 활성화 | `false` | `true` |
| `MetricsPort` | Prometheus 메트릭 포트 | `0`(HTTP 포트 공유) | `9090` |
| `IsMonitorMessageTimeOut` | 메시지 처리 타임아웃 모니터링 | `false` | `true` |
| `MonitorMessageTimeOutSeconds` | 타임아웃 임계값(초) | `1` | `5` |

### ID 생성 설정

| 설정 항목 | 설명 | 기본값 | 예시 |
|:---------|:-----|:------|:-----|
| `WorkerId` | 스노우플레이크 ID 워커 노드 ID | `1` | `2` |
| `DataCenterId` | 스노우플레이크 ID 데이터센터 ID | `1` | `2` |

### 시작 명령 예시

```bash
# 최소 시작 매개변수
dotnet GameFrameX.Launcher.dll \
    --ServerType=Game \
    --ServerId=1000 \
    --DataBaseUrl=mongodb://127.0.0.1:27017 \
    --DataBaseName=game_db

# 전체 시작 매개변수
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

## 비즈니스 로직 개발

### 컴포넌트-에이전트 패턴

프레임워크의 핵심 설계 패턴은 **상태-로직 분리**입니다. 영속화 상태(Apps 레이어, 핫 업데이트 불가)와 비즈니스 로직(Hotfix 레이어, 핫 업데이트 가능)을 엄격히 분리합니다.

**1. 상태 정의(Apps 레이어)**

```csharp
// GameFrameX.Apps/Player/BagState.cs
public class BagState : BaseCacheState
{
    public List<ItemData> Items { get; set; } = new List<ItemData>();
    public int MaxSlots { get; set; } = 50;
}
```

**2. 컴포넌트 생성(Apps 레이어)**

```csharp
// GameFrameX.Apps/Player/BagComponent.cs
public class BagComponent : StateComponent<BagState>
{
    protected override async Task OnInit()
    {
        await base.OnInit();
        // 컴포넌트 상태 초기화
    }
}
```

**3. 비즈니스 로직 구현(Hotfix 레이어)**

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

**4. 컴포넌트 에이전트 접근**

```csharp
// ActorManager를 통해 컴포넌트 에이전트 가져오기
var bagAgent = await ActorManager.GetComponentAgent<BagComponentAgent>(playerId);
var result = await bagAgent.AddItem(1001, 10);
```

### HTTP 핸들러

HTTP 핸들러는 `BaseHttpHandler`를 상속하고 `[HttpMessageMapping]` 속성으로 라우트를 등록합니다.

```csharp
[HttpMessageMapping(typeof(GetPlayerInfoHandler))]
[Description("플레이어 정보 가져오기")]
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

### TCP/RPC 메시지 핸들러

TCP 메시지 핸들러는 클라이언트가 TCP 연결을 통해 보낸 게임 메시지를 처리합니다.

**단방향 메시지 핸들러:**

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

**RPC 핸들러(요청-응답):**

```csharp
[MessageMapping(typeof(ReqAddItem))]
internal sealed class AddItemHandler : PlayerRpcComponentHandler<BagComponentAgent, ReqAddItem, RespAddItem>
{
    protected override async Task ActionAsync(ReqAddItem request, RespAddItem response)
    {
        try
        {
            // ComponentAgent는 기본 클래스에서 자동 주입
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

### 이벤트 핸들러

이벤트 시스템은 Actor 간의 느슨한 결합 통신에 사용됩니다.

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

        // 플레이어 로그인 이벤트 처리
        return agent.OnLogin();
    }
}
```

---

## 핫 업데이트 메커니즘

### 아키텍처 원리

핫 업데이트 시스템은 `AssemblyLoadContext`(수집 가능)를 통해 어셈블리의 런타임 로드 및 언로드를 구현합니다:

```
┌───────────────────────────────────────────────────────┐
│  Apps 레이어(핫 업데이트 불가)                          │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │StateComponent│  │StateComponent│  │StateComponent│   │
│  │ 영속화 상태   │  │ 영속화 상태   │  │ 영속화 상태   │   │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘   │
│         │                │                │           │
├─────────┼────────────────┼────────────────┼───────────┤
│         ▼                ▼                ▼           │
│  Hotfix 레이어(핫 업데이트 가능) — AssemblyLoadContext  │
│  를 통해 로드                                           │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │ComponentAgent│  │ComponentAgent│  │ComponentAgent│   │
│  │ 비즈니스 로직 │  │ 비즈니스 로직 │  │ 비즈니스 로직 │   │
│  └─────────────┘  └─────────────┘  └─────────────┘   │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │ Msg Handler  │  │ EventHandler│  │ HttpHandler  │   │
│  └─────────────┘  └─────────────┘  └─────────────┘   │
└───────────────────────────────────────────────────────┘
```

### 핫 업데이트 흐름

1. **새 로직 컴파일**: 업데이트된 `GameFrameX.Hotfix.dll` 빌드
2. **어셈블리 배포**: 서버의 지정된 디렉토리에 복사
3. **리로드 트리거**: HTTP 엔드포인트를 통해 핫 업데이트 요청 발행
4. **어셈블리 로드**: `HotfixManager`가 수집 가능한 `AssemblyLoadContext`로 새 DLL 로드
5. **타입 스캔**: `HotfixModule`이 새 어셈블리에서 에이전트, 핸들러, 이벤트 리스너를 스캔
6. **에이전트 전환**: `ActorManager.ClearAgent()`가 캐시된 에이전트 인스턴스를 클리어
7. **그레이스풀 전환**: 이전 어셈블리는 10분 유예 기간을 유지하며 진행 중인 요청 완료 후 언로드

### 핫 업데이트 API

```bash
# 핫 업데이트 트리거(버전 지정)
curl -X POST "http://localhost:28080/game/api/Reload?version=1.7.2"
```

---

## Docker 배포

### 단일 인스턴스 배포

`docker-compose.yml`을 사용하여 MongoDB + Game + Social의 완전한 환경을 시작:

```bash
# 빌드 및 시작
docker compose up -d --build

# 실행 상태 확인
docker compose ps

# 로그 확인
docker compose logs -f game social

# 중지
docker compose down
```

서비스 포트 매핑:

| 서비스 | 컨테이너 포트 | 호스트 포트 | 설명 |
|:------|:-------------|:-----------|:-----|
| MongoDB | 27017 | 37017 | 데이터베이스 |
| Game TCP | 29100 | 39100 | 게임 서버 |
| Game HTTP | 28080 | 38080 | 게임 서버 HTTP API |
| Social TCP | 29400 | 39400 | 소셜 서버 |
| Social HTTP | 28081 | 38081 | 소셜 서버 HTTP API |

### 멀티 인스턴스 배포

`docker-compose.multi.yml`을 사용하여 1 MongoDB + 2 Social + 10 Game의 클러스터 환경을 시작:

```bash
# 빌드 및 시작
docker compose -f docker-compose.multi.yml up -d --build

# 실행 상태 확인
docker compose -f docker-compose.multi.yml ps

# 중지
docker compose -f docker-compose.multi.yml down
```

클러스터 토폴로지:

| 컴포넌트 | 인스턴스 수 | 설명 |
|:--------|:----------|:-----|
| MongoDB | 1 | 공유 데이터베이스 |
| Social | 2 | 소셜 서버(social-1, social-2) |
| Game | 10 | 게임 서버(game-1 ~ game-10) |

모든 인스턴스는 Aspire 스타일 환경 변수로 서비스 디스커버리를 수행합니다:

```yaml
environment:
  services__Social_2001__tcp__0: "tcp://social-1:29400"
  services__Social_2002__tcp__0: "tcp://social-2:29401"
  services__Game_1001__tcp__0: "tcp://game-1:29100"
  # ...
```

### 커스텀 빌드

```bash
# 이미지 빌드
docker build -t gameframex/server:custom .

# 실행
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

## 멀티 프로세스 크로스 프로세스 디버깅

### 크로스 프로세스 스모크 테스트

```bash
# 멀티 인스턴스 환경이 실행 중인지 확인
docker compose -f docker-compose.multi.yml up -d --build

# 크로스 프로세스 스모크 테스트 실행
./scripts/multi/smoke-cross-process.sh
```

스크립트 검증 내용:
- `game-1` → `social` 크로스 프로세스 호출
- `game-2` → `social` 크로스 프로세스 호출
- `code=0` 및 `FriendCount >= 1` 반환

### 봇 스트레스 테스트

실제 클라이언트를 시뮬레이션하여 "로그인 → 온라인 → 능동적 연결 해제 → 재연결 로그인"을 반복:

```bash
# 기본 매개변수로 실행
./scripts/multi/run-bots-rpc.sh

# 커스텀 매개변수
BOT_COUNT=200 \
TCP_PORT=49100 \
LOGIN_URL=http://127.0.0.1:48080/game/api/ \
DISCONNECT_AFTER_LOGIN_SECONDS=20 \
RUN_SECONDS=300 \
./scripts/multi/run-bots-rpc.sh
```

선택적 환경 변수:

| 변수 | 설명 | 기본값 |
|:----|:-----|:------|
| `BOT_COUNT` | 봇 수 | — |
| `TCP_PORT` | TCP 연결 포트 | `49100` |
| `LOGIN_URL` | 로그인 API URL | `http://127.0.0.1:48080/game/api/` |
| `DISCONNECT_AFTER_LOGIN_SECONDS` | 로그인 후 연결 해제 지연(초) | `20` |
| `RUN_SECONDS` | 총 실행 시간(초) | `300` |

### 일반적인 문제 해결 명령

```bash
# 모든 서비스 로그 확인
docker compose -f docker-compose.multi.yml logs -f

# 특정 서비스 로그 확인
docker compose -f docker-compose.multi.yml logs -f game-1 game-2 social-1 social-2

# 리빌드 및 시작(코드 변경 후)
docker compose -f docker-compose.multi.yml up -d --build
```

---

## 모니터링 및 관측 가능성

### 엔드포인트

| 엔드포인트 | 설명 |
|:---------|:-----|
| `http://<host>:<HttpPort>/game/api/health` | 헬스 체크 |
| `http://<host>:<MetricsPort>/metrics` | Prometheus 메트릭 |

### 메트릭 카테고리

- **데이터베이스**: 작업 지연 시간(`db_operation_latency_ms`), 재시도 횟수(`db_open_retry_total`), 헬스 상태(`db_health_status`)
- **네트워크**: 연결 수, 메시지 처리량, 바이트 전송량
- **비즈니스**: 플레이어 로그인 수, 활성 세션 수
- **시스템**: GC 성능, 스레드 풀 상태

---

## 테스트

### 테스트 실행

```bash
# 모든 테스트 실행
dotnet test

# 특정 테스트 프로젝트 실행
dotnet test Tests/GameFrameX.Tests/GameFrameX.Tests.csproj

# 상세 출력으로 실행
dotnet test --logger "console;verbosity=detailed"
```

### 테스트 커버리지

테스트 프로젝트는 **xUnit** 기반이며, 다음 모듈을 커버합니다:

| 테스트 디렉토리 | 설명 |
|:-------------|:-----|
| `Utility/` | 수학/고정소수점 테스트, 압축, 난수, ID 생성, 싱글톤 |
| `NetWork/Kcp/` | KCP 파이프라인 필터, 세션 관리, 서버 통합 테스트 |
| `DataBase/` | MongoDB 연결 및 쿼리 테스트 |
| `ProtoBuff/` | Protobuf 직렬화 및 오브젝트 풀 테스트 |
| `Localization/` | 현지화 키값 파싱 테스트 |
| `RemoteMessaging/` | 크로스 프로세스 메시징 테스트 |
| `UnifiedMessaging/` | 통합 크로스 프로세스 메시징 테스트 |
| `StartUp/` | HTTP 서버 라우트 등록 테스트 |

---

## 기여 가이드

모든 형태의 기여를 환영합니다! 다음 단계를 따라주세요:

1. 이 저장소를 포크
2. 피처 브랜치 생성(`git checkout -b feature/amazing-feature`)
3. 변경 사항 커밋(`git commit -m 'feat: 기능 추가'`)
4. 브랜치에 푸시(`git push origin feature/amazing-feature`)
5. Pull Request 생성

커밋 메시지는 [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) 사양을 따라주세요.

---

## 라이선스

본 프로젝트는 **Apache License 2.0** 라이선스에 따라 배포됩니다. 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.

---

## 관련 링크

- [공식 문서](https://gameframex.doc.alianblank.com/)
- [GitHub 저장소](https://github.com/GameFrameX)
- [Gitee 저장소](https://gitee.com/GameFrameX)
- [CNB 저장소](https://cnb.cool/GameFrameX)
- [Unity 클라이언트](https://github.com/GameFrameX/GameFrameX.Unity)
- [이슈 트래커](https://github.com/GameFrameX/GameFrameX/issues)
- [커뮤니티 토론](https://github.com/GameFrameX/GameFrameX/discussions)

---

<div align="center">

**이 프로젝트가 도움이 되었다면 Star를 부탁드립니다**

**Made by GameFrameX Team**

</div>
