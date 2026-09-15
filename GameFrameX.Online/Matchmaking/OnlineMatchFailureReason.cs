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
/// 匹配失败原因（vault:C5 量化指标：匹配失败可给出原因码的比例 = 100%）。
/// <para>
/// 维护约束：任何离开 <see cref="OnlineMatchTicketState.Queued"/> 的终态都必须带非
/// <see cref="None"/> 的原因码——「无原因失败」是不可接受的（玩家要能知道为什么没匹配上）。
/// 成功匹配的票据原因码为 <see cref="None"/>。
/// </para>
/// </summary>
public enum OnlineMatchFailureReason
{
    /// <summary>无（仅成功匹配或仍在排队时为该值）。</summary>
    None = 0,

    /// <summary>等待超时（超过 ExpiresAt 仍未凑齐）。</summary>
    WaitTimeout = 1,

    /// <summary>无可兼容票据（存在队列但条件无法成组）。</summary>
    NoCompatibleTicket = 2,

    /// <summary>队伍完整性不满足（队伍人数与目标规模不符）。</summary>
    PartySizeMismatch = 3,

    /// <summary>玩家主动取消。</summary>
    CancelledByPlayer = 4,

    /// <summary>重复入队（同一队伍/玩家已有有效票据）。</summary>
    DuplicateEnqueue = 5,

    /// <summary>触发限流。</summary>
    RateLimited = 6,

    /// <summary>队长退出致队伍不再满足入队条件。</summary>
    LeaderExited = 7,

    /// <summary>成员离线致队伍不再完整。</summary>
    MemberOffline = 8,
}
