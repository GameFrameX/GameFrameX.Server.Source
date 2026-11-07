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

**인디 게임 개발자를 위한 올인원 솔루션 · 인디 개발자의 꿈을 실현**

<br />

[문서](https://gameframex.doc.alianblank.com) · [빠른 시작](#빠른-시작) · QQ 그룹: 467608841 / 233840761

<br />

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | **한국어**

</div>

## 프로젝트 개요

GameFrameX Server는 C# .NET 10 기반의 단일 프로세스 게임 서버 프레임워크입니다. 하나의 호스트에서 클라이언트 연결, 로컬 플레이어 세션, Actor, Hotfix 로직과 선택적 기능 모듈을 실행합니다. 현재 게임 및 Online 비즈니스 모듈은 유지합니다.

### 기능

- **Actor와 컴포넌트**: 직렬 실행, 상태 영속성, 이벤트, 타이머, 생성된 에이전트 래퍼.
- **Hotfix**: 로드 가능한 비즈니스 어셈블리와 로컬 수명 주기 관리. 상태 정의와 비즈니스 로직을 분리합니다.
- **클라이언트 통신**: TCP, WebSocket(WS/WSS), KCP, 요청/응답 메시지, 세션 인증, 연결 종료 정리.
- **HTTP/HTTPS**: 핸들러, 서명 검증, TLS, 선택적 개발 문서.
- **데이터베이스**: MongoDB, SQLite, PostgreSQL, 이름 있는 연결, 조건부 쓰기, 트랜잭션과 재시도. 구체적인 동작은 각 Provider의 지원 범위를 따릅니다.
- **Capability.Hosting**: 프로세스 내부 등록, 의존성 해결, 스케줄링, 모듈 수명 주기와 범용 관리 API.
- **지원 기능**: 설정 로드, 직렬화, 현지화, 진단, 아키텍처 분석기와 테스트.

## 빠른 시작

### 설치

`global.json`에서 선택한 .NET 10 SDK를 사용하여 저장소 루트에서 다음 명령을 실행합니다. 시작 전에 `Configs/app_config.json`을 단일 호스트용으로 설정합니다. 아래 데이터베이스 필드는 애플리케이션 설정입니다. SQLite에는 별도 데이터베이스 서비스가 필요하지 않으며, MongoDB와 PostgreSQL에는 연결 가능한 인스턴스가 필요합니다. 복사 명령은 macOS/Linux용이며 Windows에서는 같은 디렉터리를 해당 셸 명령으로 복사합니다.

```bash
dotnet restore Server.slnx
dotnet build Server.slnx --no-restore --no-incremental -warnaserror:CS9057
dotnet build GameFrameX.Hotfix/GameFrameX.Hotfix.csproj -o bin/app_debug/hotfix --no-restore
cp -R GameFrameX.Config/json bin/app_debug/
cp -R Configs bin/app_debug/
cd bin/app_debug
dotnet GameFrameX.Launcher.dll
```

애플리케이션 설정에서 Provider를 선택합니다. MongoDB에는 `Mongo`와 MongoDB 연결 문자열, PostgreSQL에는 `PostgreSql`과 PostgreSQL 연결 문자열을 사용합니다. `DataBaseName`은 연결/데이터베이스 이름입니다. `GameDatabase`와 레지스트리는 이름 있는 연결을 유지합니다. Provider 변경은 기존 데이터를 마이그레이션하지 않습니다.

```json
{
  "DatabaseProvider": "Sqlite",
  "DataBaseUrl": "Data Source=data/gameframex.db",
  "DataBaseName": "gameframex"
}
```

SQLite 파일 모드는 지정된 경로에 저장하고 `Data Source=:memory:`는 임시 메모리 데이터베이스입니다. 쓰기는 직렬화되며 일부 쿼리는 메모리에서 평가되어 전체 테이블을 스캔할 수 있습니다. MongoDB 트랜잭션에는 복제본 집합이 필요하고 PostgreSQL은 EF Core Provider를 사용합니다. 쿼리와 트랜잭션 동작은 해당 계약 테스트로 확인해야 합니다.

## 사용 예시

### 클라이언트 연결과 HTTP

TCP에는 `IsEnableTcp`, `OuterHost`, `OuterPort`, WS에는 `IsEnableWebSocket`, `WsPort`를 사용합니다. WSS에는 `WssPort`, `WssCertFilePath`가 추가로 필요합니다. PEM 개인 키는 `WssCertificateKeyPath`, 보호된 키의 암호는 `WssCertificatePassword`로 설정합니다. 클라이언트 KCP에는 `IsEnableKcp`, `KcpPort`를 사용합니다.

HTTP에는 `IsEnableHttp`, `HttpPort`, `HttpUrl`, HTTPS에는 `HttpsPort`, `HttpsCertPath`를 사용합니다. PEM 개인 키는 `HttpsCertificateKeyPath`, 암호는 `HttpsCertPassword`로 설정합니다. HTTPS와 WSS 인증서는 각각 설정합니다. 서명 검증을 켜면 `HttpSignSecret`과 클라이언트에 맞는 `HttpSignVersion`을 설정해야 합니다. 버전 2는 요청 요소와 nonce 재전송 방지를 포함합니다.

### 비즈니스 로직과 Hotfix

상태와 컴포넌트는 `GameFrameX.Apps`, 에이전트와 메시지 핸들러는 `GameFrameX.Hotfix`에 둡니다. 다른 Actor에는 컴포넌트 에이전트로 접근합니다. 현재 `Network`, `PlayerSession`/`PlayerId`, `MessageHandler` 명명과 비동기 API를 사용하며 비동기 작업에는 `Async` 접미사를 사용합니다.

`gfx-role`은 로컬 server Actor에 비즈니스 슬라이스를 추가합니다. 기존 `ServerRole` 디렉터리 이름은 비즈니스 분류입니다. `gfx-online`은 프로세스 내부 기능 구성을 위한 독립적인 최소 호스트를 생성합니다. 설치 및 제약은 [GameFrameX.Templates](GameFrameX.Templates/README.md)를 참조하세요.

Hotfix 어셈블리는 로컬 호스트가 로드합니다. Online 관리 작업에는 실행기와 준비된 산출물이 필요합니다. 최소 Online 템플릿에는 Hotfix 런타임이 없어 버전 전환이 명시적으로 실패합니다. 실패 복구가 트랜잭션 롤백을 보장하지는 않습니다.

### 진단과 검증

로그와 진단 기능을 유지합니다. `/health`, `/alive`는 기본적으로 개발 모드(`HttpIsDevelopment`)에서만 매핑되며 운영 환경 공개에는 명시적인 설정이 필요합니다. 기존 데이터베이스 계측이 세 Provider 모두의 호스트 상태 검사 및 Meter 연결 완료를 의미하지는 않습니다.

```bash
dotnet test Tests/GameFrameX.Tests/GameFrameX.Tests.csproj --no-build
dotnet test Tests/GameFrameX.Hotfix.Tests/GameFrameX.Hotfix.Tests.csproj --no-build
```

이는 검증 명령이며 이번 재구축이 이미 통과했다는 뜻이 아닙니다. MongoDB/PostgreSQL 통합 테스트에는 `GAMEFRAMEX_TEST_MONGODB_CONNECTION_STRING`, `GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING`이 필요합니다. 변수를 확인한 뒤 실제 데이터베이스에서 실행하세요. 설정이 없으면 바로 반환하는 테스트가 있습니다. SQLite 테스트는 파일 영속성과 트랜잭션을 다룹니다.

## 아키텍처

Launcher가 하나의 호스트를 구성합니다. StartUp은 클라이언트 리스너와 호스트 수명 주기, Core는 로컬 Actor, 세션, 컴포넌트, 이벤트, 타이머와 Hotfix 실행을 담당합니다. Hosting은 같은 프로세스에서 선택적 모듈을 구성합니다. 현재 비즈니스 모듈은 별도의 비즈니스 계층으로 유지합니다.

설정 로드와 핫 리로드를 유지합니다. `ConfigComponent`는 여전히 정적 싱글턴이며 여러 테이블에 걸친 트랜잭션을 보장하지 않습니다. 종료 시 로컬 작업 완료 대기, 상태 저장과 리소스 해제를 조정합니다.

## 의존성

런타임 프로젝트는 `net10.0`을 사용하고 생성기는 선언된 대상 프레임워크를 유지합니다. SDK 선택은 `global.json`, 패키지 버전은 프로젝트 파일과 `Version.props`에 있습니다. 주요 의존성은 GameFrameX.Foundation, SuperSocket, MongoDB.Driver, Microsoft.Data.Sqlite, Npgsql/Entity Framework Core입니다. 이번 재구축은 패키지를 배포하거나 기존 tag를 변경하지 않습니다.

## 문서 및 자료

- [GameFrameX](https://gameframex.doc.alianblank.com/)
- [GitHub](https://github.com/GameFrameX/GameFrameX.Server.Source)
- [Gitee](https://gitee.com/GameFrameX)
- [CNB](https://cnb.cool/GameFrameX)
- [Unity](https://github.com/GameFrameX/GameFrameX.Unity)
- [GameFrameX.Templates](GameFrameX.Templates/README.md)

## 커뮤니티 및 지원

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

## 변경 로그

미출시 재구축 요약과 유지된 이전 이력은 [CHANGELOG.md](CHANGELOG.md)를 참조하세요.

## 라이선스

자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.
