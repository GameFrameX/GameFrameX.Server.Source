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

namespace GameFrameX.Online.Social;

/// <summary>
/// 举报案件状态机（vault:C7 S6.3：合法边固化，非法迁移一律拒绝）。
/// <para>
/// 维护约束：合法边集中在本类型的常量邻接表中维护，服务层禁止就地拼装迁移条件
/// （形态对齐 C94 <c>OnlinePresenceStateMachine</c> / C97 <c>OnlinePartyStateMachine</c>）。
/// </para>
/// <para>
/// 合法边全集：
/// <c>Submitted → Reviewing / Rejected / Withdrawn</c>（受理、直接驳回、玩家撤回）、
/// <c>Reviewing → Actioned / Rejected</c>（裁决）。
/// <see cref="OnlineReportState.Submitted"/> 一旦进入 <see cref="OnlineReportState.Reviewing"/>
/// 即**不可撤回**——处置中途抽走证据会让 Admin 的裁决失去依据。
/// 自环不属于合法边，由调用方按幂等语义单独处理。
/// </para>
/// </summary>
public static class OnlineReportStateMachine
{
    /// <summary>合法迁移边（起始态 → 允许的目标态集合）。</summary>
    private static readonly Dictionary<OnlineReportState, OnlineReportState[]> AllowedEdges = new Dictionary<OnlineReportState, OnlineReportState[]>
    {
        {
            OnlineReportState.Submitted, new[]
            {
                OnlineReportState.Reviewing,
                OnlineReportState.Rejected,
                OnlineReportState.Withdrawn,
            }
        },
        {
            OnlineReportState.Reviewing, new[]
            {
                OnlineReportState.Actioned,
                OnlineReportState.Rejected,
            }
        },
    };

    /// <summary>
    /// 判定一次状态迁移是否合法。
    /// </summary>
    /// <param name="from">起始状态。</param>
    /// <param name="to">目标状态。</param>
    /// <returns>合法返回 <c>true</c>；自环与表外边返回 <c>false</c>。</returns>
    public static bool TryTransition(OnlineReportState from, OnlineReportState to)
    {
        OnlineReportState[] targets;
        if (!AllowedEdges.TryGetValue(from, out targets))
        {
            return false;
        }

        foreach (var target in targets)
        {
            if (target == to)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判定案件是否已到达终态（不再接受任何迁移）。
    /// </summary>
    /// <param name="state">案件状态。</param>
    /// <returns>终态返回 <c>true</c>。</returns>
    public static bool IsTerminal(OnlineReportState state)
    {
        return state == OnlineReportState.Actioned || state == OnlineReportState.Rejected || state == OnlineReportState.Withdrawn;
    }
}
