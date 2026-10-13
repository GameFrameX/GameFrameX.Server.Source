# {{Name}} —— Online 能力最小组合根宿主

由 `gfx-online` 模板生成（C230，P2b 模板轨）。核心不变量：**引用集 = 装配集 = `list_capabilities` 输出集**（三者一致，编译期保证）。

## 构建 / 运行

```bash
# 必须在 Server.Source 仓根目录
dotnet build {{Name}}          # 独立构建（未注册 Server.slnx）
dotnet run --project {{Name}}  # 启动宿主 + 管理面（127.0.0.1:28091）
```

## 已引用的能力包

见 `{{Name}}.csproj` 的 `ProjectReference` 与 `Program.cs` 的 `AddOnlineXxxRuntime` 装配语句——**两者必须成对存在**。

## 增减能力

1. csproj 增/删对应 `Capability/GameFrameX.Capability.Online.<包名>` 的 `ProjectReference`；
2. `Program.cs` 同步增/删 `builder.AddOnline<包名>Runtime(...)` 装配语句；
3. 未引用的能力不参与编译与装配，其 admin action 不可执行——**未部署 → `4002 UnknownAction`**；已引用但被 `SetFilter`/`Disable` 排除 → `4003 CapabilityNotInstalled`（载荷带已装清单）。

闭包缺口（如引 Social 不引 Foundation）会在 `Build` 期 fail-fast。

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

本宿主为最小演示形态，作用域三元组等为 `Program.cs` 常量。接入配置管道时遵循能力族全局约定（2026-10-12）：嵌套强类型段 + POCO 绑定，**禁止平铺键**：

```json
{
  "Online": {
    "Capabilities": [ "online-foundation", "online-assets" ],
    "Disable": []
  }
}
```

装配代码保持回调式 `AddXxx(options => ...)` 形态，不生成裸数据对象传参。
