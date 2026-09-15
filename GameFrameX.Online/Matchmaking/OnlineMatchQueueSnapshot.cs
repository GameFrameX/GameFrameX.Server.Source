//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
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
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Matchmaking;

/// <summary>
/// 匹配队列观测快照（vault:C5 S4.10 / VC-4.10：Admin 可核对各状态票据、队列构成与分配数）。
/// <para>
/// 维护约束：快照是**某一时刻的事实读数**，不保证跨字段的强一致（读取期间队列可能变化）；
/// 需要强一致的判定请用单次 CAS 的返回值。指标口径与 vault:C5 对齐：
/// <see cref="DuplicateAssignmentTicketCount"/> 必须为 0（重复 assignment = 0），
/// <see cref="QueuedTicketCount"/> 与实际排队票据数一致（丢失有效 Ticket = 0）。
/// </para>
/// </summary>
public sealed class OnlineMatchQueueSnapshot
{
    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置观测时刻（UTC 毫秒）。
    /// </summary>
    public long ObservedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置排队中票据数。
    /// </summary>
    public int QueuedTicketCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置排队中玩家数（去重后）。
    /// </summary>
    public int QueuedPlayerCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置已匹配票据数。
    /// </summary>
    public int MatchedTicketCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置已匹配玩家数（去重后）。
    /// </summary>
    public int MatchedPlayerCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置已取消票据数。
    /// </summary>
    public int CancelledTicketCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置已过期票据数。
    /// </summary>
    public int ExpiredTicketCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置失败票据数。
    /// </summary>
    public int FailedTicketCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置对局分配总数。
    /// </summary>
    public int AssignmentCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置被 **多份** 分配同时消费的票据数（VC-4.12 红指标，必须为 0）。
    /// </summary>
    public int DuplicateAssignmentTicketCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置票据摘要明细（按入队时刻升序）。
    /// </summary>
    public List<OnlineMatchTicketSummary> Tickets
    {
        get;
        set;
    }
}
