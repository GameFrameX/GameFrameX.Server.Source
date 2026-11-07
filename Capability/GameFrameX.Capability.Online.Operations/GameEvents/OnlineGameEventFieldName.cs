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
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Operations.GameEvents;

/// <summary>
/// 游戏事件载荷字段名常量（投影器写入的载荷键与登记表声明的必需字段的**单一事实来源**）。
/// <para>
/// 维护约束（红线）：
/// (1) **字段名只在常量值处出现一次**——<see cref="OnlineGameEventProjector"/> 的载荷字典键与
/// <see cref="OnlineGameEventSchema"/> 登记表的必需字段一律引用本类常量，禁止字符串字面量：
/// 两侧各写字面量时拼错会静默漂移（校验恒拒或恒放），引用常量后拼错成员名 / 重命名在编译期即报错；
/// (2) 常量采用 <c>nameof</c> 自引用——成员名即字段值，声明处的拼写错误不可能产生；
/// 成员重命名会改变字段值（对下游是破坏性契约变更），由投影器测试中的字面量断言锁定拦截；
/// (3) 字段值是对外冻结的载荷键（数据管道按此解析），一经发布不得改值、不得复用；
/// 与 <see cref="OnlineGameEventName"/>（事件名契约）分工：本类管载荷字段名，那个类管事件名。
/// </para>
/// </summary>
public static class OnlineGameEventFieldName
{
    /// <summary>会话标识（会话域事件的关联标识）。</summary>
    public const string SessionId = nameof(SessionId);

    /// <summary>玩家标识（玩家维度事件的归属主体）。</summary>
    public const string PlayerId = nameof(PlayerId);

    /// <summary>设备标识（安装 / 登录事件的设备归属）。</summary>
    public const string DeviceId = nameof(DeviceId);

    /// <summary>领域对象状态名（投影时刻的状态快照，可选字段）。</summary>
    public const string State = nameof(State);

    /// <summary>会话结束原因（状态名：Closed / Kicked / Expired）。</summary>
    public const string EndReason = nameof(EndReason);

    /// <summary>匹配票据标识。</summary>
    public const string TicketId = nameof(TicketId);

    /// <summary>匹配 / 对局模式标识。</summary>
    public const string Mode = nameof(Mode);

    /// <summary>区服地区标识（可选字段）。</summary>
    public const string Region = nameof(Region);

    /// <summary>队伍人数（进入匹配队列事件）。</summary>
    public const string PlayerCount = nameof(PlayerCount);

    /// <summary>对局标识。</summary>
    public const string MatchId = nameof(MatchId);

    /// <summary>对局结果标识（权威结算事实的关联标识）。</summary>
    public const string MatchResultId = nameof(MatchResultId);

    /// <summary>对局结果（终态状态名）。</summary>
    public const string Outcome = nameof(Outcome);

    /// <summary>结果条目数（对局结束事件的可选字段）。</summary>
    public const string EntryCount = nameof(EntryCount);

    /// <summary>玩家名次（玩家维度对局结果的可选字段）。</summary>
    public const string Rank = nameof(Rank);

    /// <summary>玩家得分（玩家维度对局结果的可选字段）。</summary>
    public const string Score = nameof(Score);

    /// <summary>资产交易标识。</summary>
    public const string TransactionId = nameof(TransactionId);

    /// <summary>资产变更来源（PaymentConfirm 等来源名）。</summary>
    public const string ChangeSource = nameof(ChangeSource);

    /// <summary>资产操作（Grant / Deduct 等操作名，可选字段）。</summary>
    public const string Operation = nameof(Operation);

    /// <summary>业务订单标识（付费事件的商户侧订单号）。</summary>
    public const string BusinessOrderId = nameof(BusinessOrderId);

    /// <summary>预期变更条目数（资产交易事件的可选字段）。</summary>
    public const string ChangeCount = nameof(ChangeCount);

    /// <summary>举报工单标识。</summary>
    public const string ReportId = nameof(ReportId);

    /// <summary>举报人标识。</summary>
    public const string ReporterId = nameof(ReporterId);

    /// <summary>被举报玩家标识。</summary>
    public const string ReportedPlayerId = nameof(ReportedPlayerId);

    /// <summary>举报场景（Chat 等场景名，可选字段）。</summary>
    public const string Scene = nameof(Scene);

    /// <summary>原因（举报分类名或处罚原因文本，可选字段）。</summary>
    public const string Reason = nameof(Reason);

    /// <summary>被举报聊天消息标识（举报事件的可选字段）。</summary>
    public const string ChatMessageId = nameof(ChatMessageId);

    /// <summary>处罚标识。</summary>
    public const string PunishmentId = nameof(PunishmentId);

    /// <summary>处罚种类（封禁 / 禁言等种类名）。</summary>
    public const string Kind = nameof(Kind);

    /// <summary>处罚生效时刻（UTC 毫秒，可选字段）。</summary>
    public const string EffectiveAtTime = nameof(EffectiveAtTime);

    /// <summary>处罚失效时刻（UTC 毫秒，0 表示永久，可选字段）。</summary>
    public const string ExpiresAtTime = nameof(ExpiresAtTime);

    /// <summary>邮件标识（邮件打开事件；由客户端 SDK / 邮件域投递，Server 半边不投影）。</summary>
    public const string MailId = nameof(MailId);
}
