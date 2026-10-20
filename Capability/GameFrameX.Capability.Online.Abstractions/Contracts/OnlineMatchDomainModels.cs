//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes and liabilities arising from secondary development,
//   本组织不承担任何责任，开发者须自行承担。
//   The organization shall not be responsible for any disputes; developers shall bear the responsibility.
//  ==========================================================================================

using System.Collections.Generic;

namespace GameFrameX.Capability.Online.Abstractions.Contracts;

/// <summary>
/// 匹配域入队回执（C246）：MatchId 非 0 = 即时窗口已撮合（协议数值对局标识）；0 = 已入队排队中。
/// </summary>
public sealed class OnlineMatchDomainEnqueue
{
    /// <summary>协议数值对局标识（0 = 排队中，尚未撮合）。</summary>
    public long MatchId { get; set; }
}

/// <summary>
/// 匹配域取消回执（C246）：Cancelled=false 表示票据已处终态（已撮合 / 已过期 / 无票），非失败。
/// </summary>
public sealed class OnlineMatchDomainCancel
{
    /// <summary>是否真实执行了取消（true = 排队票已取消；false = 终态回执）。</summary>
    public bool Cancelled { get; set; }
}

/// <summary>
/// 匹配域状态回执（C246）：排队中 / 已撮合（含数值对局标识）/ 其余终态三态投影。
/// </summary>
public sealed class OnlineMatchDomainStatus
{
    /// <summary>是否排队中。</summary>
    public bool Queuing { get; set; }

    /// <summary>最近一次尝试是否已撮合（Matched 终态）。</summary>
    public bool Matched { get; set; }

    /// <summary>已撮合的协议数值对局标识（未撮合为 0）。</summary>
    public long MatchId { get; set; }
}

/// <summary>
/// 匹配域入场回执（C246）：对局 create-or-reuse + 双方就绪启动后的入场事实。
/// </summary>
public sealed class OnlineMatchDomainEntry
{
    /// <summary>协议数值对局标识（回显请求标识）。</summary>
    public long MatchId { get; set; }

    /// <summary>对局是否已进入运行态。</summary>
    public bool Started { get; set; }

    /// <summary>对局成员表（含请求方）。</summary>
    public IReadOnlyList<long> PlayerIds { get; set; }
}

/// <summary>
/// 匹配分配解析视图（C246）：供通知推送方按 assignmentId 解析数值对局标识与成员表。
/// </summary>
public sealed class OnlineMatchDomainAssignment
{
    /// <summary>协议数值对局标识。</summary>
    public long MatchId { get; set; }

    /// <summary>匹配模式（协议标签）。</summary>
    public int Mode { get; set; }

    /// <summary>分配成员表。</summary>
    public IReadOnlyList<long> PlayerIds { get; set; }
}
