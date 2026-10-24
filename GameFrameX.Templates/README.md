# GameFrameX.Templates

仓内 `dotnet new` 脚手架模板包（C221）。只在 **Server.Source 仓根**使用，不发布 NuGet（`IsPackable=false`，永不进入发布流水线）。

## 模板清单

| 短名 | 用途 |
|---|---|
| `gfx-role` | 按 C197 垂直切片约定生成 ServerRole 全切片骨架 |
| `gfx-online` | 生成 Online 能力最小组合根宿主工程（C230，引啥装配啥） |

## 安装 / 更新 / 卸载

```bash
dotnet new install ./GameFrameX.Templates --force   # 安装与「模板变更后更新」都用这条（--force 覆盖旧安装）
dotnet new --list | grep gfx-                       # 确认已装
dotnet new uninstall GameFrameX.Templates           # 卸载
```

## 使用 gfx-role（工作流时序：实例化 → 导出 → 构建）

```bash
# ① 实例化（必须在 Server.Source 仓根执行；-N 切片名 PascalCase，-D 消息域号）
dotnet new gfx-role -N Foo -D 240 --dry-run         # 先预览落盘清单
dotnet new gfx-role -N Foo -D 240

# ② proto 源移入 Protobuf 仓并导出（生成物回流本仓；跨仓 git 同步走既有 gfx-dev-e2e-sync 流程）
cp scripts/scaffold-out/_0240_Foo.proto ../Protobuf/
cd ../Protobuf && dotnet Tools/ProtoExport.dll --mode csharp --isServer true \
  --usingStatements "using System|using ProtoBuf|using System.Collections.Generic|using GameFrameX.Network.Abstractions|using GameFrameX.Network.Messages" \
  --isGenerateDescription true --inputPath ./ --outputPath ../Server.Source/GameFrameX.Proto/Proto \
  --namespaceName GameFrameX.Proto.Proto --isGenerateErrorCode true

# ③ 构建与测试（导出前全仓构建不过属预期：Handler 引用尚未生成的消息类型）
cd ../Server.Source && dotnet build Server.slnx && dotnet test Server.slnx --no-restore
```

**导出前不可整体构建属预期**；导出后零手工修补即应全绿（C221 演练实证：构建 0 错 0 警告 + 测试 1707/1707）。

gfx-role 产出（以 `-N Demo -D 240` 为例，均在仓根相对路径落盘）：

```
scripts/scaffold-out/_0240_Demo.proto                                  ← proto 源（待移入 Protobuf 仓）
GameFrameX.Apps/ServerRole/Demo/Entity/DemoState.cs
GameFrameX.Apps/ServerRole/Demo/Component/DemoComponent.cs             ← [ComponentType(GlobalConst.ActorTypeServer)]
GameFrameX.Hotfix/Logic/ServerRole/Demo/DemoComponentAgent.cs
GameFrameX.Hotfix/Logic/ServerRole/Demo/ReqDemoEchoHandler.cs          ← 样板消息对 Req/RespDemoEcho（id = (240<<16)+10/11）
GameFrameX.Hotfix/Logic/ServerRole/Demo/DemoRules.cs
Tests/GameFrameX.Hotfix.Tests/DemoRulesTests.cs
Tests/GameFrameX.Tests/Proto/DemoMessageDomainTests.cs                 ← 域内护栏（清单 + handler 链路）
```

绝不产出 `GameFrameX.Proto/Proto/*.cs`——协议层生成物只能由 ProtoExport 导出（C197 Phase 3 教训：手写生成物会被导出器整体清空）。

## 使用 gfx-online（最小组合根宿主，C230）

生成一个最小宿主工程：按开关只引所选 `GameFrameX.Capability.Online.*` 能力包，经机制层唯一组合入口
`CapabilityComposition.Compose(new OnlineCapabilityFamily(), options)` 按部署前缀扫描装配——
**引用集 = 装配集 = `list_capabilities` 输出集**（编译期保证，C294 起无按包装配代码）。与 `gfx-role`
正交（垂直切片 vs 组合根宿主）；不引 `GameFrameX.Launcher`（其直引 11 能力包全家桶，引用即全量 DLL
落盘，编译期「引啥装配啥」不成立）。

```bash
# 必须在 Server.Source 仓根执行；-N 宿主工程名（PascalCase），免 -o 直接落地 ./MyOnlineHost/
dotnet new gfx-online -N MyOnlineHost                          # 缺省最小集 Foundation+Assets
dotnet new gfx-online -N MyOnlineHost --Social --Operations    # 按开关加能力
dotnet new gfx-online -N MyOnlineHost --Assets:false           # 去示例叶子（仅 Foundation）

dotnet build MyOnlineHost && dotnet run --project MyOnlineHost  # 独立构建运行（不入 slnx）
```

能力开关：`--Assets`（缺省 true）/ `--Social` / `--Matchmaking` / `--Match` / `--Competition` /
`--Operations`（缺省 false）；Foundation 为脊柱恒引、不设开关。生成物 = `<Name>.csproj` +
`Program.cs` + `README.md` 三件；增减能力 = 只增删 csproj `ProjectReference`（`Program.cs` 组合代码与
能力集无关；依赖声明缺口在组合期 fail-fast 一次报全）。

管理面：`POST http://127.0.0.1:28091/online/admin/{action}`（body 带作用域三元组
`TenantId/AppId/ServerId`）；`list_capabilities` 如实输出装配集；未装配能力的 action 不可执行
（未部署 → `4002 UnknownAction`；已引用被 `Capabilities`/`Disable` 过滤排除 → `4003 CapabilityNotInstalled`
载荷带已装清单）。

### 行为实测结论（C230 演练，2026-10-13）

1. **免 `-o` 落地**：`dotnet new gfx-online -N Foo` 直接落地 `./Foo/`（模板根 `{{Name}}/` 目录随符号改名）；若再叠加 `-o Bar` 会产生 `Bar/Foo/` 双层嵌套——不要混用。
2. **csproj 条件块必须用 XML 注释指令**：`<!--#if (Assets)-->`（`//#if` 行指令在 .csproj/.xml/.props/.targets 中不被引擎处理，仅 .cs/.json/.config 等生效）——模板源树实测探针结论。
3. **未装配 action 应答两分**：未引用（未部署）→ `4002 UnknownAction`；已引用但被 `SetFilter`/`Disable` 排除 → `4003 CapabilityNotInstalled`（载荷带已装清单）。两者同为「不可执行 + 诊断如实」。
4. **缺 `-N` → 硬错误**：`isRequired` 生效，退出码 102，零落盘（C221 同款）。
5. **布尔开关三态可用**：`--Assets`（开）/ `--Assets:false` / `--Assets=false`（关）；缺省 Assets=true 其余 false；参数名区分大小写（C221 承袭）。
6. **生成工程构建 0 错**；输出 DLL 面 = 引用集（默认集下无 Social/Matchmaking/Match/Competition/Operations DLL）；11 包全开形态 = 7 manifest / 29 action / 拓扑序机制排定，与生产组合根契约吻合。
7. **演练清理**：生成工程删除即归零（不入 slnx、产物自隔离 bin/）；模板包 `uninstall` 后 0 模板、重装恢复两模板。

## 域号治理表（gfx-role --domain / -D）

`-D` 取 3 位正整数（100-999），**避开已用域号与预留段**；模板不扫描校验（dotnet new 无逻辑能力），冲突由护栏测试兜底拦截：

| 段 | 域号 | 状态 |
|---|---|---|
| 已用 | 10 / 20 / 100 / 120 / 200-230 / 300 / 310 / 400-460 / 500 / 650-690 / 800 / 810 / 900 | 禁用 |
| Online 预留 | 600-649 / 700-799 | 禁用 |
| 空置 | 240-299 / 320-390 / 470-490 / 510-640 / 820-899 等 | 可用（取未占整数） |

错误码基值 = 域号×1000（如 240 → `240001` 起）；域内消息序号 10 起、Req/Resp 成对递增（导出器按 .proto 文件内顺序编号）。

## 行为实测结论（C221 演练，2026-10-12）

1. **缺 `-N` 或 `-D` → 硬错误**：`isRequired` 生效，退出码 102，零落盘。
2. **重复实例化同名切片 → 静默覆盖既有同名文件**（无 `--force` 也覆盖）：切片名必须全局唯一；覆盖既有真实切片前先 `--dry-run` 预览。
3. **模板变更后必须重装**（`install --force`），dotnet new 不自动感知模板源树修改。
4. 参数名区分大小写：`-N/--Name`、`-D/--Domain`；`-n` 是引擎通用输出名选项，**不会**替换模板符号。

## 范围外（保持手工）

- **向既有切片追加消息对**：dotnet new 只能新建文件、不能编辑既有 `.proto`/`Req*Handler.cs`——按 C197 约定手工追加（消息对 `Req<X>`/`Resp<X>`、Resp 带 `ErrorCode`(ProtoMember 2047)、handler 三段式样板见任意 `Req*Handler.cs`）。
- 新 ServerRole 启动入口（`AppStartUpXxx`）、Inner 负域号消息、HTTP handler 模板、配置表（Luban）：按需后补/既有流程。
- NuGet 发布（模板包不发布，C221 约定）；Launcher 轨组合根（走 `CapabilityComposition.Compose` 单入口，C294）。
