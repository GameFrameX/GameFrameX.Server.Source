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
/// 好友关系状态机（vault:C7 S6.2：合法边固化，非法迁移一律拒绝）。
/// <para>
/// 维护约束：合法边集中在本类型的常量邻接表中维护，服务层禁止就地拼装迁移条件
/// （形态对齐 C94 <c>OnlinePresenceStateMachine</c> / C97 <c>OnlinePartyStateMachine</c>）；
/// 新增边必须先改本表再改调用方，避免「某条路径偷偷放行非法迁移」。
/// </para>
/// <para>
/// 合法边全集：
/// <c>Requested → Accepted / Rejected / Expired / Removed</c>、
/// <c>Rejected / Expired / Removed → Requested</c>（重新发起，VC-6.2）、
/// <c>Accepted → Removed</c>（删除好友）。
/// 自环（同态迁移）不属于合法边，由调用方按幂等语义单独处理。
/// </para>
/// </summary>
public static class OnlineFriendshipStateMachine
{
    /// <summary>合法迁移边（起始态 → 允许的目标态集合）。</summary>
    private static readonly Dictionary<OnlineFriendshipState, OnlineFriendshipState[]> AllowedEdges = new Dictionary<OnlineFriendshipState, OnlineFriendshipState[]>
    {
        {
            OnlineFriendshipState.Requested, new[]
            {
                OnlineFriendshipState.Accepted,
                OnlineFriendshipState.Rejected,
                OnlineFriendshipState.Expired,
                OnlineFriendshipState.Removed,
            }
        },
        {
            OnlineFriendshipState.Accepted, new[]
            {
                OnlineFriendshipState.Removed,
            }
        },
        {
            OnlineFriendshipState.Rejected, new[]
            {
                OnlineFriendshipState.Requested,
            }
        },
        {
            OnlineFriendshipState.Expired, new[]
            {
                OnlineFriendshipState.Requested,
            }
        },
        {
            OnlineFriendshipState.Removed, new[]
            {
                OnlineFriendshipState.Requested,
            }
        },
    };

    /// <summary>
    /// 判定一次状态迁移是否合法。
    /// </summary>
    /// <param name="from">起始状态。</param>
    /// <param name="to">目标状态。</param>
    /// <returns>合法返回 <c>true</c>；自环与表外边返回 <c>false</c>。</returns>
    public static bool TryTransition(OnlineFriendshipState from, OnlineFriendshipState to)
    {
        OnlineFriendshipState[] targets;
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
    /// 判定关系是否处于「已建立」态（双方互为好友）。
    /// </summary>
    /// <param name="state">关系状态。</param>
    /// <returns>已建立返回 <c>true</c>。</returns>
    public static bool IsEstablished(OnlineFriendshipState state)
    {
        return state == OnlineFriendshipState.Accepted;
    }
}
