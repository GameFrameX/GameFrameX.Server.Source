# GameFrameX.Templates

仓内 `dotnet new` 脚手架模板包（C221）。只在 **Server.Source 仓根**使用，不发布 NuGet（`IsPackable=false`，永不进入发布流水线）。

## 模板清单

| 短名 | 用途 |
|---|---|
| `gfx-role` | 按 C197 垂直切片约定生成 ServerRole 全切片骨架 |

产出（以 `-N Demo -D 240` 为例，均在仓根相对路径落盘）：

```
scripts/scaffold-out/_0240_Demo.proto                                  ← proto 源（待移入 Protobuf 仓）
GameFrameX.Apps/ServerRole/Demo/Entity/DemoState.cs
GameFrameX.Apps/ServerRole/Demo/Component/DemoComponent.cs             ← [ComponentType(GlobalConst.ActorTypeServer)]
GameFrameX.Hotfix/Logic/ServerRole/Demo/DemoComponentAgent.cs
GameFrameX.Hotfix/Logic/ServerRole/Demo/DemoHandlers.cs                ← 样板消息对 Req/RespDemoEcho（id = (240<<16)+10/11）
GameFrameX.Hotfix/Logic/ServerRole/Demo/DemoRules.cs
Tests/GameFrameX.Hotfix.Tests/DemoRulesTests.cs
Tests/GameFrameX.Tests/Proto/DemoMessageDomainTests.cs                 ← 域内护栏（清单 + handler 链路）
```

绝不产出 `GameFrameX.Proto/Proto/*.cs`——协议层生成物只能由 ProtoExport 导出（C197 Phase 3 教训：手写生成物会被导出器整体清空）。

## 安装 / 更新 / 卸载

```bash
dotnet new install ./GameFrameX.Templates --force   # 安装与「模板变更后更新」都用这条（--force 覆盖旧安装）
dotnet new --list | grep gfx-role                   # 确认已装
dotnet new uninstall GameFrameX.Templates           # 卸载
```

## 使用（工作流时序：实例化 → 导出 → 构建）

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

## 域号治理表（--domain / -D）

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

- **向既有切片追加消息对**：dotnet new 只能新建文件、不能编辑既有 `.proto`/Handlers——按 C197 约定手工追加（消息对 `Req<X>`/`Resp<X>`、Resp 带 `ErrorCode`(ProtoMember 2047)、handler 三段式样板见任意 `*Handlers.cs`）。
- 新 ServerRole 启动入口（`AppStartUpXxx`）、Inner 负域号消息、HTTP handler 模板、配置表（Luban）：按需后补/既有流程。
