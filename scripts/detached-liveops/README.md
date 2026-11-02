# detached-liveops 存档

LiveOps 运营面（远程配置 / 设备组 / 定时任务 / 公告 / 玩家分群 / 配置灰度与回滚）已从能力包
`GameFrameX.Capability.Online.Competition` 拆离：运营编排属平台运营特征，按包结构收敛裁决移交
Admin 仓（运营台后端，clientDelivery 承接）。本目录为原文存档，不参与任何工程编译，
供 Admin 仓实现时按需搬迁。

原包内位置与形态：

- `OnlineLiveOpsRegistry.cs` / `OnlineLiveOpsCommand.cs` / `OnlineLiveOpsEntry.cs` / `OnlineLiveOpsOperation.cs`
  —— 原位于 `Capability/GameFrameX.Capability.Online.Competition/LiveOps/`（命名空间
  `GameFrameX.Capability.Online.Competition.LiveOps`），纯 POCO + 进程内注册表，零包外依赖。
- `OnlineAdminLiveOpsHandlers.cs` —— 原位于同包 `AdminApi/`，admin 面 7 个 action 的处理器
  （publishRemoteConfig / syncDeviceGroup / syncScheduledTask / pushAnnouncement / syncPlayerSegment /
  publishConfigRollout / rollbackConfigRollout），依赖 Hosting 管理契约与 Foundation.Json。

搬迁时注意：管理契约（`IAdminActionContributor` / `AdminActionRegistry` / `AdminApiRequest` 等）已上移
`GameFrameX.Capability.Hosting.Abstractions` 包；Admin 仓经自身引用面消费。
