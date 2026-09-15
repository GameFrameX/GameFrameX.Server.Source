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

using System.Collections.Generic;
using GameFrameX.Online.Match;
using GameFrameX.Online.Party;
using GameFrameX.Online.Presence;
using GameFrameX.Online.Session;

namespace GameFrameX.Online.Overview;

/// <summary>
/// 在线总览只读快照（vault:C9 S8.1：Admin 在线总览的数据面，回答「当前有多少人在线/多少局/队列多长/谁在重连」）。
/// <para>
/// 维护约束（红线）：
/// ① **运行态快照而非权威状态**——数值由运行态存储现场聚合，随时会变，消费方不得缓存后当作权威状态使用；
/// ② 数据源唯一 = Online 玩家侧存储；**管理员连接不计入任何计数**（vault:C9 X6：管理员在线链路不是玩家在线数据源）；
/// ③ 所有计数按作用域三键 (TenantId, AppId, ServerId) 过滤，跨作用域读数与不存在同构（反预言）。
/// </para>
/// <para>
/// 口径定义见 <see cref="OnlineOverviewService"/> 类文档——各计数的统计边界集中在那里，本类型只承载结果。
/// </para>
/// </summary>
public sealed class OnlineOverviewSnapshot
{
    /// <summary>
    /// 获取或设置租户标识（本快照的作用域归属）。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识（本快照的作用域归属）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置区服标识（本快照的作用域归属）。
    /// </summary>
    public long ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置快照观测时刻（UTC 毫秒；由调用方注入或取系统时钟）。
    /// </summary>
    public long ObservedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置在线玩家数（作用域内 Presence 记录数，风控限制中的玩家不计入）。
    /// </summary>
    public int OnlinePlayerCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置非终态会话数（不含已关闭/被踢/过期会话）。
    /// </summary>
    public int SessionCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置会话状态分布（键为状态，值为该状态的会话数；按状态枚举值升序）。
    /// </summary>
    public IReadOnlyDictionary<OnlineSessionState, int> SessionStateCounts
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置非终态队伍数（解散/过期/离队/取消/失败均为终态，见 C97 <see cref="OnlinePartyStateMachine.IsTerminal"/>）。
    /// </summary>
    public int PartyCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置队伍状态分布（键为状态，值为该状态的队伍数；按状态枚举值升序）。
    /// </summary>
    public IReadOnlyDictionary<OnlinePartyState, int> PartyStateCounts
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置存活对局数（<see cref="OnlineMatchState.Closed"/> 即已释放，不计入——见 <see cref="OnlineOverviewService"/> 口径）。
    /// </summary>
    public int MatchCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置对局状态分布（键为状态，值为该状态的对局数；按状态枚举值升序）。
    /// </summary>
    public IReadOnlyDictionary<OnlineMatchState, int> MatchStateCounts
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置重连率（重连中会话 ÷ 非终态会话；分母为 0 时取 0）。
    /// </summary>
    public double ReconnectRate
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置队列概况（按「玩法模式 + 区域」聚合，按 Mode 升序、同 Mode 按 Region 升序）。
    /// </summary>
    public IReadOnlyList<OnlineOverviewQueueSummary> QueueSummaries
    {
        get;
        set;
    }
}
