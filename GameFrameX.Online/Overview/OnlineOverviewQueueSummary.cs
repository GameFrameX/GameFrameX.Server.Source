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

namespace GameFrameX.Online.Overview;

/// <summary>
/// 在线总览的匹配队列概况行（按「玩法模式 + 区域」聚合）。
/// <para>
/// 维护约束：队列口径归 C97 匹配域所有，本行只是该口径在总览快照中的投影——深度、平均等待与吞吐的
/// 统计定义集中在 <see cref="OnlineOverviewService"/> 类文档，本类型不得另行定义，否则同一指标会出现两处口径而漂移。
/// 与 C97 <see cref="Matchmaking.OnlineMatchQueueSnapshot"/> 的分工：后者是匹配域的完整观测快照（含逐票据明细），
/// 本行是总览页需要的聚合视图（不含任何玩家或票据明细）。
/// </para>
/// </summary>
public sealed class OnlineOverviewQueueSummary
{
    /// <summary>
    /// 获取或设置玩法模式（取自票据 <see cref="Matchmaking.OnlineMatchTicket.Mode"/> 原值）。
    /// </summary>
    public int Mode
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置区域（取自票据 <see cref="Matchmaking.OnlineMatchTicket.Region"/> 原值）。
    /// </summary>
    public int Region
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置队列深度（当前作用域内处于排队态的票据数）。
    /// </summary>
    public int QueueDepth
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置平均已等待秒数（排队态票据的平均等待时长；无排队票据时为 0）。
    /// </summary>
    public int AverageWaitSeconds
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近一分钟达成匹配的票据数（滑动窗口吞吐，详见服务类文档）。
    /// </summary>
    public int ThroughputPerMinute
    {
        get;
        set;
    }
}
