// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.Localization;

/// <summary>
/// 本地化键常量定义 - Online 分部类
/// </summary>
public static partial class Keys
{
    /// <summary>
    /// Online 模块相关异常消息资源键
    /// </summary>
    public static class Online
    {
        /// <summary>
        /// 热修复回滚相关消息
        /// </summary>
        public static class HotfixRollback
        {
            /// <summary>
            /// 目标版本未登记，拒绝切换活跃版本：{0}
            /// </summary>
            /// <remarks>
            /// 键名: Online.HotfixRollback.TargetVersionNotRegistered
            /// 用途: SetActiveAsync 切换活跃版本时目标版本未登记
            /// 参数: {0} - 目标版本号
            /// </remarks>
            public const string TargetVersionNotRegistered = "Online.HotfixRollback.TargetVersionNotRegistered";
        }

        /// <summary>
        /// 玩家身份相关消息
        /// </summary>
        public static class Identity
        {
            /// <summary>
            /// 作用域缺少玩家主体位（PlayerId <= 0），不能派生玩家上下文。
            /// </summary>
            /// <remarks>
            /// 键名: Online.Identity.ScopeMissingPlayerId
            /// 用途: FromScope 派生玩家上下文时作用域缺少玩家主体位
            /// </remarks>
            public const string ScopeMissingPlayerId = "Online.Identity.ScopeMissingPlayerId";
        }

        /// <summary>
        /// 赛事存储相关消息
        /// </summary>
        public static class Tournament
        {
            /// <summary>
            /// 赛事不存在，保存前必须先经 CreateAsync 建立
            /// </summary>
            /// <remarks>
            /// 键名: Online.Tournament.NotFoundOnSave
            /// 用途: SaveAsync 保存时同键赛事不存在
            /// </remarks>
            public const string NotFoundOnSave = "Online.Tournament.NotFoundOnSave";
        }

        /// <summary>
        /// 榜单存储相关消息
        /// </summary>
        public static class Leaderboard
        {
            /// <summary>
            /// 榜单不存在或已失效，写入前必须先经 FindAsync 解析
            /// </summary>
            /// <remarks>
            /// 键名: Online.Leaderboard.NotFoundOnWrite
            /// 用途: ApplySubmissionAsync 写入时目标榜单不存在或已失效
            /// </remarks>
            public const string NotFoundOnWrite = "Online.Leaderboard.NotFoundOnWrite";

            /// <summary>
            /// 榜单不存在或已失效，查询前必须先经 FindAsync 解析
            /// </summary>
            /// <remarks>
            /// 键名: Online.Leaderboard.NotFoundOnQuery
            /// 用途: ListOrderedEntriesAsync 查询时目标榜单不存在或已失效
            /// </remarks>
            public const string NotFoundOnQuery = "Online.Leaderboard.NotFoundOnQuery";

            /// <summary>
            /// 榜单不存在或已失效，重置前必须先经 FindAsync 解析
            /// </summary>
            /// <remarks>
            /// 键名: Online.Leaderboard.NotFoundOnReset
            /// 用途: TryResetAsync 重置时目标榜单不存在或已失效
            /// </remarks>
            public const string NotFoundOnReset = "Online.Leaderboard.NotFoundOnReset";
        }

        /// <summary>
        /// 游戏事件相关消息
        /// </summary>
        public static class GameEvents
        {
            /// <summary>
            /// 事件标识不能为空（EventId 是存储幂等键）
            /// </summary>
            /// <remarks>
            /// 键名: Online.GameEvents.EventIdRequired
            /// 用途: AppendAsync 追加事件时 EventId 为空
            /// </remarks>
            public const string EventIdRequired = "Online.GameEvents.EventIdRequired";

            /// <summary>
            /// 事件名不能为空
            /// </summary>
            /// <remarks>
            /// 键名: Online.GameEvents.EventNameRequired
            /// 用途: 构造事件描述符时事件名为空
            /// </remarks>
            public const string EventNameRequired = "Online.GameEvents.EventNameRequired";

            /// <summary>
            /// 事件版本必须从 1 起
            /// </summary>
            /// <remarks>
            /// 键名: Online.GameEvents.EventVersionInvalid
            /// 用途: 构造事件描述符时当前版本小于 1
            /// </remarks>
            public const string EventVersionInvalid = "Online.GameEvents.EventVersionInvalid";
        }

        /// <summary>
        /// 赛季存储相关消息
        /// </summary>
        public static class Season
        {
            /// <summary>
            /// 赛季不存在，保存前必须先经 CreateAsync 建立
            /// </summary>
            /// <remarks>
            /// 键名: Online.Season.NotFoundOnSave
            /// 用途: SaveAsync 保存时同键赛季不存在
            /// </remarks>
            public const string NotFoundOnSave = "Online.Season.NotFoundOnSave";
        }

        /// <summary>
        /// 资产对账相关消息
        /// </summary>
        public static class Assets
        {
            /// <summary>
            /// 对账必须绑定玩家主体位
            /// </summary>
            /// <remarks>
            /// 键名: Online.Assets.ReconciliationRequiresPlayerScope
            /// 用途: ReconcilePlayerAsync 对账时作用域缺少玩家主体位
            /// </remarks>
            public const string ReconciliationRequiresPlayerScope = "Online.Assets.ReconciliationRequiresPlayerScope";
        }

        /// <summary>
        /// 会话相关消息
        /// </summary>
        public static class Session
        {
            /// <summary>
            /// 会话已进入终态
            /// </summary>
            /// <remarks>
            /// 键名: Online.Session.SessionTerminal
            /// 用途: 查询要求活跃会话时会话已处于终态
            /// </remarks>
            public const string SessionTerminal = "Online.Session.SessionTerminal";
        }

        /// <summary>
        /// Admin API 相关消息
        /// </summary>
        public static class AdminApi
        {
            /// <summary>
            /// Action 名称不能为 null 或空。
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.ActionNameRequired
            /// 用途: 调度器注册 action 时名称为空
            /// </remarks>
            public const string ActionNameRequired = "Online.AdminApi.ActionNameRequired";

            /// <summary>
            /// 内层响应 JSON 不能为 null 或空。
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.InnerJsonRequired
            /// 用途: WriteRaw 包装已序列化内层响应时内容为空
            /// </remarks>
            public const string InnerJsonRequired = "Online.AdminApi.InnerJsonRequired";

            /// <summary>
            /// Online admin API 前缀不能为空。
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.PrefixRequired
            /// 用途: 启动 Admin API 服务时前缀为空
            /// </remarks>
            public const string PrefixRequired = "Online.AdminApi.PrefixRequired";

            /// <summary>
            /// PlayerId 必须为正数（线缆形态：字符串）。
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.PlayerIdInvalid
            /// 用途: ReadPlayerId 读取玩家标识时非法
            /// </remarks>
            public const string PlayerIdInvalid = "Online.AdminApi.PlayerIdInvalid";

            /// <summary>
            /// {0} 必须为非空字符串。
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.NonEmptyStringRequired
            /// 用途: RequireString 读取必填字符串时为空
            /// 参数: {0} - 字段名
            /// </remarks>
            public const string NonEmptyStringRequired = "Online.AdminApi.NonEmptyStringRequired";

            /// <summary>
            /// 服务未返回任何结果。
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.ServiceNoResult
            /// 用途: Unwrap 解开服务结果时结果为 null
            /// </remarks>
            public const string ServiceNoResult = "Online.AdminApi.ServiceNoResult";

            /// <summary>
            /// {0} 必须为正数。
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.PositiveNumberRequired
            /// 用途: 读取必填正数标识/版本号时非法
            /// 参数: {0} - 字段名
            /// </remarks>
            public const string PositiveNumberRequired = "Online.AdminApi.PositiveNumberRequired";

            /// <summary>
            /// ConfigKind 必须是 [1,4] 范围内的数字（RemoteConfig/Announcement/DeviceGroup/ScheduledTask）。
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.ConfigKindInvalid
            /// 用途: ReadConfigKind 读取 ConfigKind 时非法
            /// </remarks>
            public const string ConfigKindInvalid = "Online.AdminApi.ConfigKindInvalid";

            /// <summary>
            /// AbnormalKind 必须是 [1,3] 范围内的数字（LongWaiting/RepeatedCancel/Orphan）。
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.AbnormalKindInvalid
            /// 用途: 查询异常票据时 AbnormalKind 非法
            /// </remarks>
            public const string AbnormalKindInvalid = "Online.AdminApi.AbnormalKindInvalid";

            /// <summary>
            /// 匹配票据不存在：{0}
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.MatchTicketNotFound
            /// 用途: 受控取消匹配票据时票据不存在
            /// 参数: {0} - 票据标识
            /// </remarks>
            public const string MatchTicketNotFound = "Online.AdminApi.MatchTicketNotFound";

            /// <summary>
            /// 管理面不支持结束异常对局；对局生命周期归游戏进程所有。
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.EndAbnormalMatchForbidden
            /// 用途: end_abnormal_match 固定拒绝
            /// </remarks>
            public const string EndAbnormalMatchForbidden = "Online.AdminApi.EndAbnormalMatchForbidden";

            /// <summary>
            /// Direction 必须为 'Apply' 或 'Lift'。
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.DirectionInvalid
            /// 用途: 读取受控双向命令方向时非法
            /// </remarks>
            public const string DirectionInvalid = "Online.AdminApi.DirectionInvalid";

            /// <summary>
            /// Status 必须是 [1,4] 范围内的数字（Pending/Handling/Resolved/Rejected）。
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.StatusInvalid
            /// 用途: 更新举报工单状态时 Status 非法
            /// </remarks>
            public const string StatusInvalid = "Online.AdminApi.StatusInvalid";

            /// <summary>
            /// 交易不存在：{0}
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.TransactionNotFound
            /// 用途: 撤销资产 / 查询交易明细时交易不存在
            /// 参数: {0} - 交易标识
            /// </remarks>
            public const string TransactionNotFound = "Online.AdminApi.TransactionNotFound";

            /// <summary>
            /// GrantItems 必须至少包含一个条目。
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.GrantItemsEmpty
            /// 用途: 解析 GrantItems 变更行时数组为空
            /// </remarks>
            public const string GrantItemsEmpty = "Online.AdminApi.GrantItemsEmpty";

            /// <summary>
            /// GrantItems 条目要求 ItemOrCurrencyType（字符串）且 Quantity（正数）。
            /// </summary>
            /// <remarks>
            /// 键名: Online.AdminApi.GrantItemInvalid
            /// 用途: 解析单条 GrantItems 变更行时字段非法
            /// </remarks>
            public const string GrantItemInvalid = "Online.AdminApi.GrantItemInvalid";
        }

        /// <summary>
        /// 运行时宿主相关消息
        /// </summary>
        public static class Runtime
        {
            /// <summary>
            /// Online 运行时选项非法（作用域三元组 / 端口 / 前缀）。
            /// </summary>
            /// <remarks>
            /// 键名: Online.Runtime.OptionsInvalid
            /// 用途: 构造 OnlineRuntimeHost 时装配选项未通过校验
            /// </remarks>
            public const string OptionsInvalid = "Online.Runtime.OptionsInvalid";
        }
    }
}
