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

using System;
using System.Collections.Generic;

namespace GameFrameX.Online.Match;

/// <summary>
/// 对局生命周期状态机（vault:C6「Match 生命周期」的边集合固化）。
/// <para>
/// 维护约束（红线）：合法边以常量邻接表固化，禁止在服务层用 if/switch 拼装迁移条件——
/// 新增迁移必须先改本表并补状态机测试。所有「结束态」（Completed / Cancelled / Timeout / Failed /
/// Aborted / SettlementFailed）都保留指向 <see cref="OnlineMatchState.Closed"/> 的唯一边，
/// 使释放路径唯一：任何对局都能到达 <see cref="OnlineMatchState.Closed"/>，
/// 这是 VC-5.11「僵尸 Match（未清理）数量 = 0」能够被结构性保证的前提。
/// </para>
/// </summary>
public static class OnlineMatchStateMachine
{
    /// <summary>
    /// 合法迁移邻接表：键 = 起态，值 = 可达目标态集合。
    /// </summary>
    private static readonly Dictionary<OnlineMatchState, OnlineMatchState[]> Adjacency = new Dictionary<OnlineMatchState, OnlineMatchState[]>
    {
        {
            OnlineMatchState.Created, new[]
            {
                OnlineMatchState.Waiting,
                OnlineMatchState.Cancelled,
                OnlineMatchState.Timeout,
                OnlineMatchState.Failed,
                OnlineMatchState.Aborted,
            }
        },
        {
            OnlineMatchState.Waiting, new[]
            {
                OnlineMatchState.Ready,
                OnlineMatchState.Cancelled,
                OnlineMatchState.Timeout,
                OnlineMatchState.Failed,
                OnlineMatchState.Aborted,
            }
        },
        {
            OnlineMatchState.Ready, new[]
            {
                OnlineMatchState.Running,
                OnlineMatchState.Waiting,
                OnlineMatchState.Cancelled,
                OnlineMatchState.Timeout,
                OnlineMatchState.Failed,
                OnlineMatchState.Aborted,
            }
        },
        {
            OnlineMatchState.Running, new[]
            {
                OnlineMatchState.Settling,
                OnlineMatchState.Cancelled,
                OnlineMatchState.Timeout,
                OnlineMatchState.Failed,
                OnlineMatchState.Aborted,
            }
        },
        {
            OnlineMatchState.Settling, new[]
            {
                OnlineMatchState.Completed,
                OnlineMatchState.SettlementFailed,
            }
        },
        {
            OnlineMatchState.SettlementFailed, new[]
            {
                OnlineMatchState.Settling,
                OnlineMatchState.Cancelled,
                OnlineMatchState.Failed,
                OnlineMatchState.Aborted,
                OnlineMatchState.Closed,
            }
        },
        {
            OnlineMatchState.Completed, new[]
            {
                OnlineMatchState.Closed,
            }
        },
        {
            OnlineMatchState.Cancelled, new[]
            {
                OnlineMatchState.Closed,
            }
        },
        {
            OnlineMatchState.Timeout, new[]
            {
                OnlineMatchState.Closed,
            }
        },
        {
            OnlineMatchState.Failed, new[]
            {
                OnlineMatchState.Closed,
            }
        },
        {
            OnlineMatchState.Aborted, new[]
            {
                OnlineMatchState.Closed,
            }
        },
        { OnlineMatchState.Closed, new OnlineMatchState[0] },
    };

    /// <summary>
    /// 判定一次状态迁移是否合法。
    /// </summary>
    /// <param name="from">起态。</param>
    /// <param name="to">目标态。</param>
    /// <returns>合法返回 <c>true</c>。</returns>
    public static bool TryTransition(OnlineMatchState from, OnlineMatchState to)
    {
        return Adjacency.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;
    }

    /// <summary>
    /// 获取指定状态的合法目标态集合（诊断与测试用）。
    /// </summary>
    /// <param name="from">起态。</param>
    /// <returns>合法目标态列表；终态返回空列表。</returns>
    public static IReadOnlyList<OnlineMatchState> GetLegalTargets(OnlineMatchState from)
    {
        return Adjacency.TryGetValue(from, out var targets) ? targets : new OnlineMatchState[0];
    }

    /// <summary>
    /// 判定状态是否为终态（无出边）。
    /// </summary>
    /// <param name="state">待判定状态。</param>
    /// <returns>终态（仅 <see cref="OnlineMatchState.Closed"/>）返回 <c>true</c>。</returns>
    public static bool IsTerminal(OnlineMatchState state)
    {
        return Adjacency.TryGetValue(state, out var targets) && targets.Length == 0;
    }

    /// <summary>
    /// 判定状态是否为「结束态」（对局已不可能再推进玩法，与是否已释放无关）。
    /// <para>
    /// <see cref="OnlineMatchState.SettlementFailed"/> 属于结束态：结算产出失败后不会再产出权威结果，
    /// 它经保留期后同样释放为 <see cref="OnlineMatchState.Closed"/>——否则该状态既无玩法推进也无释放路径，
    /// 会成为僵尸对局（VC-5.11）。
    /// </para>
    /// </summary>
    /// <param name="state">待判定状态。</param>
    /// <returns>结束态返回 <c>true</c>。</returns>
    public static bool IsEnded(OnlineMatchState state)
    {
        return state == OnlineMatchState.Completed
               || state == OnlineMatchState.Cancelled
               || state == OnlineMatchState.Timeout
               || state == OnlineMatchState.Failed
               || state == OnlineMatchState.Aborted
               || state == OnlineMatchState.SettlementFailed
               || state == OnlineMatchState.Closed;
    }

    /// <summary>
    /// 判定状态是否接受玩家输入（唯一可接受输入的阶段）。
    /// </summary>
    /// <param name="state">待判定状态。</param>
    /// <returns>可接受输入返回 <c>true</c>。</returns>
    public static bool IsPlayable(OnlineMatchState state)
    {
        return state == OnlineMatchState.Running;
    }

    /// <summary>
    /// 枚举全部合法边（守护测试用：锁定边集合不被无声增删）。
    /// </summary>
    /// <returns>合法边列表。</returns>
    public static IReadOnlyList<(OnlineMatchState From, OnlineMatchState To)> GetAllEdges()
    {
        var edges = new List<(OnlineMatchState From, OnlineMatchState To)>();
        foreach (var pair in Adjacency)
        {
            foreach (var target in pair.Value)
            {
                edges.Add((pair.Key, target));
            }
        }

        return edges;
    }
}
