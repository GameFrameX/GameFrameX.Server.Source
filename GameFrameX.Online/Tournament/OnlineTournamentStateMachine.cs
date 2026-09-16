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

using System;
using System.Collections.Generic;

namespace GameFrameX.Online.Tournament;

/// <summary>
/// 赛事状态机（合法边固化表，形态对齐 C97/C98/C99/C103 先例）。
/// <para>
/// 维护约束（红线）：合法边以常量表固化，禁止在服务层用 if/switch 拼装迁移条件——新增迁移必须先改本表
/// 并补状态机测试。本状态机是**单向线性链**：<c>Scheduled → Active → Ended → Settled</c>，
/// 反向与跨级迁移一概非法（未开始的赛事不能结束、已结束的赛事不能复活、已结算不可回退），
/// 保证「成绩冻结与奖励结算各自只会发生一次」这一结构前提（VC-7.7 / VC-7.8）。
/// </para>
/// </summary>
public static class OnlineTournamentStateMachine
{
    /// <summary>
    /// 合法迁移邻接表：键 = 起态，值 = 可达终态集合。
    /// </summary>
    private static readonly Dictionary<OnlineTournamentState, OnlineTournamentState[]> Adjacency = new Dictionary<OnlineTournamentState, OnlineTournamentState[]>
    {
        { OnlineTournamentState.Scheduled, new[] { OnlineTournamentState.Active } },
        { OnlineTournamentState.Active, new[] { OnlineTournamentState.Ended } },
        { OnlineTournamentState.Ended, new[] { OnlineTournamentState.Settled } },
        { OnlineTournamentState.Settled, Array.Empty<OnlineTournamentState>() },
    };

    /// <summary>
    /// 判定一次状态迁移是否合法。
    /// </summary>
    /// <param name="from">起态。</param>
    /// <param name="to">目标态。</param>
    /// <returns>合法返回 <c>true</c>。</returns>
    public static bool TryTransition(OnlineTournamentState from, OnlineTournamentState to)
    {
        return Adjacency.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;
    }

    /// <summary>
    /// 获取指定状态的合法目标态集合（诊断与测试用）。
    /// </summary>
    /// <param name="from">起态。</param>
    /// <returns>合法目标态列表；终态返回空列表。</returns>
    public static IReadOnlyList<OnlineTournamentState> GetLegalTargets(OnlineTournamentState from)
    {
        return Adjacency.TryGetValue(from, out var targets) ? targets : Array.Empty<OnlineTournamentState>();
    }

    /// <summary>
    /// 判定状态是否为终态（无出边）。
    /// </summary>
    /// <param name="state">待判定状态。</param>
    /// <returns>终态返回 <c>true</c>。</returns>
    public static bool IsTerminal(OnlineTournamentState state)
    {
        return GetLegalTargets(state).Count == 0;
    }
}
