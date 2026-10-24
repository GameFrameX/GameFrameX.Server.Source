# {{Name}} —— Online 能力最小组合根宿主

由 `gfx-online` 模板生成（C230，P2b 模板轨；C294 改走机制化组合）。核心不变量：**引用集 = 装配集 = `list_capabilities` 输出集**（三者一致，编译期保证）。

## 构建 / 运行

```bash
# 必须在 Server.Source 仓根目录
dotnet build {{Name}}          # 独立构建（未注册 Server.slnx）
dotnet run --project {{Name}}  # 启动宿主 + 管理面（127.0.0.1:28091）
```

## 已引用的能力包

见 `{{Name}}.csproj` 的 `ProjectReference`——`CapabilityComposition.Compose` 按族部署前缀扫描已加载的能力包，**引用了哪些包就装配哪些能力**，无需按包书写装配代码。

## 增减能力

1. csproj 增/删对应 `Capability/GameFrameX.Capability.Online.<包名>` 的 `ProjectReference`；
2. 未引用的能力不参与编译与装配，其 admin action 不可执行——**未部署 → `4002 UnknownAction`**；已引用但被过滤排除 → `4003 CapabilityNotInstalled`（载荷带已装清单）。

依赖声明缺口（如引 Social 不引 Foundation）在组合期 fail-fast（一次报全）。

## 宿主扩展点

部分能力以 `RequireHostService<T>` 声明宿主扩展点（包内无默认实现，宿主未 `Override` 即组合期 fail-fast）。本模板已在 `Program.cs` 提供最小实现：

- `IOnlinePlayerDirectory`（online-social 好友搜索 / 展示名）→ `EmptyPlayerDirectory`：恒返回空集合，好友搜索无结果，其余社交功能不受影响；
- `IOnlineHotfixRollbackExecutor`（online-operations 回滚 action，仅 `--Operations` 时生成）→ `UnsupportedHotfixRollbackExecutor`：本宿主不承载热更运行时，回滚显式失败。

接入真实玩家库或热更运行时时，替换这两个实现并在 `Program.cs` 的 `options.Override<T>(...)` 处更换实例。

## 管理面调用

```bash
# 能力目录（已装配集合如实输出；body 带作用域三元组）
curl -X POST http://127.0.0.1:28091/online/admin/list_capabilities -H "Content-Type: application/json" \
  -d '{"TenantId":1,"AppId":1,"ServerId":1}'

# 未部署（未引用）能力的 action → 4002 UnknownAction
curl -X POST http://127.0.0.1:28091/online/admin/<未引用能力的action> -H "Content-Type: application/json" \
  -d '{"TenantId":1,"AppId":1,"ServerId":1}'

# 已引用但被 Disable 排除的能力 action → 4003 CapabilityNotInstalled（载荷带已装清单）
```

## 配置升级路径

本宿主为最小演示形态：作用域三元组与族配置段（空段 = 全部能力走包内默认参数）为 `Program.cs` 常量。接入配置管道时遵循能力族全局约定（2026-10-12）：嵌套强类型段 + POCO 绑定，**禁止平铺键**：

```json
{
  "Online": {
    "EnableRoles": ["Game"],
    "Capabilities": [],
    "Disable": [],
    "UseGameDbStores": false,
    "Settings": {
      "online-social": { "Friend": { "SearchLimit": 50 } }
    }
  }
}
```

族级键（`EnableRoles` / `Capabilities` / `Disable` / `UseGameDbStores`）由宿主解析后落到
`CapabilityCompositionOptions`（如 `Filter`）；`Settings` 子对象由机制按字段绑定到各能力
Options 根类型（未知能力 Id / 未知字段 / 类型不符 / `Validate` 失败 = fail-fast）。
