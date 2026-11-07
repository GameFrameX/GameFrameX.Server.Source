# legacy 脚本存档

随包结构收敛移出能力包的旧迁移代码存档：不再参与任何工程编译，仅作历史参考与二次搬迁素材。

## OnlineFriendLegacyMigration.cs

- 原位置：`Capability/GameFrameX.Capability.Online.Social/OnlineFriendLegacyMigration.cs`。
- 原职责：社交能力激活期可选执行的一次性旧好友数据迁移（只读单向复制旧链域 120 `FriendRelationState` / 域 660 `FriendState`，幂等可重跑），经 `OnlineFriendOptions.MigrateLegacyFriendData` 开关触发（默认关闭）。
- 移出原因：能力包禁止依赖玩法层（`GameFrameX.Apps`）与数据实现（`GameFrameX.DataBase`）；旧好友状态已随旧链退役，无存量生产数据可迁。
- 依赖形态：编译需 `GameFrameX.Apps.Player.Friend.Entity`、`GameFrameX.Apps.ServerRole.Friend.Entity` 与 `GameFrameX.DataBase.GameDb` 静态门面；如需复用，建议以独立迁移工具工程承载，不回能力包。
