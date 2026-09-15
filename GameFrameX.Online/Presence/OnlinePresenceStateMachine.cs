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

namespace GameFrameX.Online.Presence;

/// <summary>
/// 在线状态机（vault:C3 S2.5/VC-2.5：转换表固化为可断言常量，阶段 4/5 直接消费不得旁路）。
/// <para>
/// 维护约束（红线）：转换表为唯一合法性判据——任何写入 Presence 的状态变更必须先经
/// <see cref="TryTransition"/> 判定；表外转换一律拒绝（返回 false，由服务层映射 5xxx）；
/// 触发语义（进队列/组队/开局）由调用方解释，状态机只固化 from → to 合法边；
/// 修改转换表必须走 vault 契约变更并同步 VC-2.5 用例集（状态边断言覆盖率 100%）。
/// </para>
/// </summary>
public static class OnlinePresenceStateMachine
{
    /// <summary>
    /// 合法转换邻接表：键 = 源状态，值 = 可达目标状态集合。
    /// </summary>
    private static readonly Dictionary<OnlinePresenceState, OnlinePresenceState[]> Adjacency = new Dictionary<OnlinePresenceState, OnlinePresenceState[]>
    {
        {
            OnlinePresenceState.Offline,
            new OnlinePresenceState[]
            {
                OnlinePresenceState.Online,
                OnlinePresenceState.Blocked,
            }
        },
        {
            OnlinePresenceState.Online,
            new OnlinePresenceState[]
            {
                OnlinePresenceState.Idle,
                OnlinePresenceState.Matching,
                OnlinePresenceState.InParty,
                OnlinePresenceState.InMatch,
                OnlinePresenceState.Reconnecting,
                OnlinePresenceState.Offline,
                OnlinePresenceState.Blocked,
            }
        },
        {
            OnlinePresenceState.Idle,
            new OnlinePresenceState[]
            {
                OnlinePresenceState.Online,
                OnlinePresenceState.Reconnecting,
                OnlinePresenceState.Offline,
                OnlinePresenceState.Blocked,
            }
        },
        {
            OnlinePresenceState.Matching,
            new OnlinePresenceState[]
            {
                OnlinePresenceState.Online,
                OnlinePresenceState.InParty,
                OnlinePresenceState.InMatch,
                OnlinePresenceState.Reconnecting,
                OnlinePresenceState.Offline,
                OnlinePresenceState.Blocked,
            }
        },
        {
            OnlinePresenceState.InParty,
            new OnlinePresenceState[]
            {
                OnlinePresenceState.Online,
                OnlinePresenceState.Matching,
                OnlinePresenceState.InMatch,
                OnlinePresenceState.Reconnecting,
                OnlinePresenceState.Offline,
                OnlinePresenceState.Blocked,
            }
        },
        {
            OnlinePresenceState.InMatch,
            new OnlinePresenceState[]
            {
                OnlinePresenceState.Online,
                OnlinePresenceState.InParty,
                OnlinePresenceState.Reconnecting,
                OnlinePresenceState.Offline,
                OnlinePresenceState.Blocked,
            }
        },
        {
            OnlinePresenceState.Reconnecting,
            new OnlinePresenceState[]
            {
                OnlinePresenceState.Online,
                OnlinePresenceState.InMatch,
                OnlinePresenceState.Offline,
                OnlinePresenceState.Blocked,
            }
        },
        {
            OnlinePresenceState.Blocked,
            new OnlinePresenceState[]
            {
                OnlinePresenceState.Offline,
            }
        },
    };

    /// <summary>
    /// 判定状态转换是否合法。
    /// </summary>
    /// <param name="from">源状态。</param>
    /// <param name="to">目标状态。</param>
    /// <returns>合法返回 true；表外转换返回 false。</returns>
    public static bool TryTransition(OnlinePresenceState from, OnlinePresenceState to)
    {
        return Adjacency.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;
    }

    /// <summary>
    /// 获取源状态的全部合法目标状态。
    /// </summary>
    /// <param name="from">源状态。</param>
    /// <returns>合法目标状态集合（只读）。</returns>
    public static IReadOnlyList<OnlinePresenceState> GetLegalTargets(OnlinePresenceState from)
    {
        return Adjacency.TryGetValue(from, out var targets) ? targets : Array.Empty<OnlinePresenceState>();
    }

    /// <summary>
    /// 枚举全部合法状态边（VC-2.5 状态机断言用：测试须对每条边逐一断言）。
    /// </summary>
    /// <returns>全部合法边 (from, to) 序列。</returns>
    public static IReadOnlyList<(OnlinePresenceState From, OnlinePresenceState To)> GetAllEdges()
    {
        var edges = new List<(OnlinePresenceState, OnlinePresenceState)>();
        foreach (var pair in Adjacency)
        {
            foreach (var to in pair.Value)
            {
                edges.Add((pair.Key, to));
            }
        }

        return edges;
    }
}
