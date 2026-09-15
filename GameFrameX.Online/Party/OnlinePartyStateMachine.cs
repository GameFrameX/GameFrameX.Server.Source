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

namespace GameFrameX.Online.Party;

/// <summary>
/// 队伍状态机（vault:C5 生命周期图的合法边固化表）。
/// <para>
/// 维护约束（红线）：合法边以常量表固化，禁止在服务层用 if/switch 拼装迁移条件——
/// 新增迁移必须先改本表并补状态机测试（vault:C5 S4.3 生命周期图）。
/// 终态（<see cref="IsTerminal"/> 为 <c>true</c>）出边为空，任何离开终态的请求都映射
/// <c>StateEnded</c>，保证「取消/解散后状态唯一且不可复活」。
/// </para>
/// </summary>
public static class OnlinePartyStateMachine
{
    /// <summary>
    /// 合法迁移邻接表：键 = 起态，值 = 可达终态集合。
    /// </summary>
    private static readonly Dictionary<OnlinePartyState, OnlinePartyState[]> Adjacency = new Dictionary<OnlinePartyState, OnlinePartyState[]>
    {
        {
            OnlinePartyState.Created, new[]
            {
                OnlinePartyState.Inviting,
                OnlinePartyState.Formed,
                OnlinePartyState.Cancelled,
                OnlinePartyState.Expired,
                OnlinePartyState.Disbanded,
            }
        },
        {
            OnlinePartyState.Inviting, new[]
            {
                OnlinePartyState.Formed,
                OnlinePartyState.Cancelled,
                OnlinePartyState.Expired,
                OnlinePartyState.Disbanded,
            }
        },
        {
            OnlinePartyState.Formed, new[]
            {
                OnlinePartyState.Inviting,
                OnlinePartyState.Ready,
                OnlinePartyState.Left,
                OnlinePartyState.Matching,
                OnlinePartyState.Cancelled,
                OnlinePartyState.Expired,
                OnlinePartyState.Disbanded,
            }
        },
        {
            OnlinePartyState.Ready, new[]
            {
                OnlinePartyState.Formed,
                OnlinePartyState.Left,
                OnlinePartyState.Matching,
                OnlinePartyState.Cancelled,
                OnlinePartyState.Expired,
                OnlinePartyState.Disbanded,
            }
        },
        {
            OnlinePartyState.Matching, new[]
            {
                OnlinePartyState.Ready,
                OnlinePartyState.Left,
                OnlinePartyState.Matched,
                OnlinePartyState.Failed,
                OnlinePartyState.Cancelled,
                OnlinePartyState.Disbanded,
            }
        },
        {
            OnlinePartyState.Left, new[]
            {
                OnlinePartyState.Inviting,
                OnlinePartyState.Formed,
                OnlinePartyState.Cancelled,
                OnlinePartyState.Expired,
                OnlinePartyState.Disbanded,
            }
        },
        { OnlinePartyState.Matched, new OnlinePartyState[0] },
        { OnlinePartyState.Disbanded, new OnlinePartyState[0] },
        { OnlinePartyState.Expired, new OnlinePartyState[0] },
        { OnlinePartyState.Cancelled, new OnlinePartyState[0] },
        { OnlinePartyState.Failed, new OnlinePartyState[0] },
    };

    /// <summary>
    /// 判定一次状态迁移是否合法。
    /// </summary>
    /// <param name="from">起态。</param>
    /// <param name="to">目标态。</param>
    /// <returns>合法返回 <c>true</c>。</returns>
    public static bool TryTransition(OnlinePartyState from, OnlinePartyState to)
    {
        return Adjacency.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;
    }

    /// <summary>
    /// 获取指定状态的合法目标态集合（诊断与测试用）。
    /// </summary>
    /// <param name="from">起态。</param>
    /// <returns>合法目标态列表；终态返回空列表。</returns>
    public static IReadOnlyList<OnlinePartyState> GetLegalTargets(OnlinePartyState from)
    {
        return Adjacency.TryGetValue(from, out var targets) ? targets : new OnlinePartyState[0];
    }

    /// <summary>
    /// 判定状态是否为终态（无出边）。
    /// </summary>
    /// <param name="state">待判定状态。</param>
    /// <returns>终态返回 <c>true</c>。</returns>
    public static bool IsTerminal(OnlinePartyState state)
    {
        return Adjacency.TryGetValue(state, out var targets) && targets.Length == 0;
    }

    /// <summary>
    /// 枚举全部合法边（守护测试用：锁定边集合不被无声增删）。
    /// </summary>
    /// <returns>合法边列表。</returns>
    public static IReadOnlyList<(OnlinePartyState From, OnlinePartyState To)> GetAllEdges()
    {
        var edges = new List<(OnlinePartyState From, OnlinePartyState To)>();
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
