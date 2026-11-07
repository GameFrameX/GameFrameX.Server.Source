# GameFrameX.Templates

本地 `dotnet new` 模板包，`IsPackable=false`，不进入 NuGet 发布流程。

## 模板清单

| 短名 | 用途 | 使用位置 |
|---|---|---|
| `gfx-role` | 同一 server Actor 上的本地业务切片：状态、组件、代理、消息处理器、规则与测试 | Server.Source 仓根 |
| `gfx-online` | 按选项引用能力包的单进程最小组合根宿主 | Server.Source 仓根 |
| `gfx-game` | 通过 NuGet 引用 Online 能力的独立起点工程 | 已准备相应包源的目录 |

保留既有短名与 `ServerRole` 路径以兼容现有业务组织。`gfx-role` 的组件使用
`[ComponentType(GlobalConst.ActorTypeServer)]`，多个业务组件挂在同一个本地 server Actor；模板不生成独立进程入口或跨进程接线。

## 安装与更新

```bash
dotnet new install ./GameFrameX.Templates --force
dotnet new list gfx-
```

模板变更后重新安装。卸载时使用安装输出中的包标识；本地目录安装通常使用安装目录的完整路径：

```bash
dotnet new uninstall /absolute/path/to/Server.Source/GameFrameX.Templates
```

## gfx-role：实例化、导出、构建

```bash
# 在 Server.Source 仓根预览并生成；消息域须先确认未占用。
dotnet new gfx-role -N ExampleFeature -D 240 --dry-run
dotnet new gfx-role -N ExampleFeature -D 240
```

产出如下（相对于仓根）：

```text
scripts/scaffold-out/_0240_ExampleFeature.proto
GameFrameX.Apps/ServerRole/ExampleFeature/Entity/ExampleFeatureState.cs
GameFrameX.Apps/ServerRole/ExampleFeature/Component/ExampleFeatureComponent.cs
GameFrameX.Hotfix/Logic/ServerRole/ExampleFeature/ExampleFeatureComponentAgent.cs
GameFrameX.Hotfix/Logic/ServerRole/ExampleFeature/ReqExampleFeatureEchoHandler.cs
GameFrameX.Hotfix/Logic/ServerRole/ExampleFeature/ExampleFeatureRules.cs
Tests/GameFrameX.Hotfix.Tests/ExampleFeatureRulesTests.cs
Tests/GameFrameX.Tests/Proto/ExampleFeatureMessageDomainTests.cs
```

将 `.proto` 源交给项目现有 Protobuf 导出流程，再将生成物输出到 `GameFrameX.Proto/Proto`。
模板不会手写协议 C# 生成物；导出前请求/响应类型缺失，不能整体编译。导出后再执行：

```bash
dotnet build Server.slnx --no-incremental -warnaserror:CS9057
dotnet test Tests/GameFrameX.Tests/GameFrameX.Tests.csproj --no-build
dotnet test Tests/GameFrameX.Hotfix.Tests/GameFrameX.Hotfix.Tests.csproj --no-build
```

参数区分大小写：`-N/--Name` 为切片名，`-D/--Domain` 为消息域；引擎的 `-n` 不替代 `-N`。
切片名必须唯一，生成前检查 `--dry-run` 清单，避免覆盖既有文件。
`Domain` 采用 100–999 的三位正整数，并避开现有协议和 Online 预留的 600–649、700–799；
240 只是示例，不保证将来仍空闲。模板本身不扫描域号占用，导出后须运行消息域护栏测试。
错误码基值为域号乘以 1000，消息号由协议导出器生成。

## gfx-online：本进程能力宿主

```bash
# 在 Server.Source 仓根执行，直接生成 ./ExampleOnlineHost/。
dotnet new gfx-online -N ExampleOnlineHost --dry-run
dotnet new gfx-online -N ExampleOnlineHost

dotnet build ExampleOnlineHost
dotnet run --project ExampleOnlineHost
```

默认引用 Foundation 和 Assets。开关包括 `--Assets`（默认 true）、`--Social`、
`--Matchmaking`、`--Match`、`--Leaderboard`、`--Competition`、`--Operations`（其余默认 false）。
`--Assets:false` 可关闭 Assets；Competition 会同时引用其必需的 Leaderboard。
包的必选依赖以当前 manifest 为准，缺失在组合期报错；可选能力缺席时如实披露功能限制。

工程通过 `CapabilityComposition.Compose(new OnlineCapabilityFamily(), options)` 扫描已部署的能力包，
在本进程中完成依赖解析、装配和生命周期管理。过滤器可进一步排除模块，因此不能将引用数量直接视为运行能力数量；
`list_capabilities` 返回实际装配结果。宿主不引用 Launcher，也不自动注册到 `Server.slnx`。
不要叠加 `-o`，否则会在输出目录内再生成一层 `<Name>/`，破坏默认相对工程引用。

管理 API 示例：

```bash
curl -X POST http://127.0.0.1:28091/online/admin/list_capabilities   -H "Content-Type: application/json" -d '{"ServerId":1}'
```

作用域只有 `ServerId`。默认管理面无身份认证并监听所有网卡，本机 URL 只是访问示例；按部署环境限制访问。
未部署能力的 action 返回 `4002 UnknownAction`，已引用但被过滤的能力 action 返回 `4003 CapabilityNotInstalled`。

生成文件还包含 `EmptyPlayerDirectory`：最小宿主不接玩家库，搜索和 ID 查询都返回空集合。
启用 Operations 时额外生成 `UnsupportedHotfixRollbackExecutor`，因宿主不承载热更运行时而明确拒绝版本切换。
接入真实业务时通过 `options.Override<T>(...)` 替换实现。默认存储使用内存实现，不能据此宣称重启后持久化。

## gfx-game：包引用工程

```bash
dotnet new gfx-game -N ExampleGame --dry-run
dotnet new gfx-game -N ExampleGame
```

生成工程使用 .NET 10 和模板固定的 2.0.0 包引用；`nuget.config` 中的本地源为 `../LocalPackages`，
相对于生成工程目录解析。先准备匹配版本的包，或将源改为实际可用的包源，再还原与构建。
本次重建不发布这些包，因此包模板构建不能替代当前源码树的验证。

模板组合 Foundation、Social、Assets、Match、Leaderboard、Competition 与通用管理面，
不包含完整玩法实现，也不自动连接玩家库；玩法和真实玩家目录需由应用接入。
管理端口为 28092，具有与 `gfx-online` 相同的访问边界。

## 验证边界

模板安装与实例化只能验证符号、目录和条件选项的展开。
业务切片还需要协议导出与应用编译；源码宿主需要当前依赖工程可构建；包引用宿主需要可用的包源。
本说明不沿用历史演练的测试数量作为本次重建结果。
