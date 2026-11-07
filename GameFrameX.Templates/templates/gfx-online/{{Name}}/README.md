# {{Name}} —— 单进程 Online 能力宿主

由 `gfx-online` 模板生成，通过 `CapabilityComposition.Compose` 在本进程装配已部署的能力模块。
宿主独立于 Launcher，不自动加入 `Server.slnx`。

## 构建与运行

从 Server.Source 仓根执行：

```bash
dotnet build {{Name}}
dotnet run --project {{Name}}
```

## 能力装配

`{{Name}}.csproj` 的 `ProjectReference` 决定构建产物中可扫描的能力包。
增加能力时检查其 manifest 必选依赖；配置过滤器可以进一步排除模块。
依赖不满足会在组合期失败；`list_capabilities` 返回实际装配集合。
默认存储为内存实现，不保证重启后持久化。

## 宿主扩展点

- `IOnlinePlayerDirectory` 使用 `EmptyPlayerDirectory`，搜索和 ID 查询都返回空集合。
- 启用 Operations 时，`IOnlineHotfixRollbackExecutor` 使用 `UnsupportedHotfixRollbackExecutor`。此宿主不承载热更运行时，版本切换明确失败。

接入真实玩家目录或本地热更运行时时，通过 `Program.cs` 的 `options.Override<T>(...)` 替换实现。

## 管理 API

默认无身份认证并监听所有网卡；以下回环地址只是访问示例，需按部署环境限制访问。
请求作用域只有 `ServerId`：

```bash
curl -X POST http://127.0.0.1:28091/online/admin/list_capabilities   -H "Content-Type: application/json" -d '{"ServerId":1}'
```

未部署能力的 action 返回 `4002 UnknownAction`；已引用但被过滤排除的能力 action 返回
`4003 CapabilityNotInstalled`。

## 配置接入

模板默认在 `Program.cs` 设置 `ServerId` 和空 `Settings`。接入配置文件时，可读取以下
`Online` 段并作为 `JsonElement` 传给 `CapabilityCompositionOptions`：

```json
{
  "Online": {
    "ServerId": 1,
    "Settings": {}
  }
}
```

`Settings` 按能力 Id 绑定到各能力 Options；未知 Id、字段、类型错误或验证失败会阻止装配。
如需能力过滤、数据库存储替换或其他宿主策略，显式设置组合选项和服务覆盖；仅添加配置键不会自动完成接线。
