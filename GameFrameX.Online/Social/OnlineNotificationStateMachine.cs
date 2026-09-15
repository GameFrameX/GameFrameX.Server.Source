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
/// 通知状态机（vault:C7 S6.8：合法边固化，非法迁移一律拒绝）。
/// <para>
/// 维护约束：合法边集中在本类型的常量邻接表中维护，服务层禁止就地拼装迁移条件
/// （形态对齐 C94 <c>OnlinePresenceStateMachine</c> / C97 <c>OnlinePartyStateMachine</c> /
/// C99 <see cref="OnlineFriendshipStateMachine"/>）；新增边必须先改本表再改调用方，
/// 避免「某条路径偷偷放行非法迁移」。
/// </para>
/// <para>
/// 合法边全集：
/// <c>Created → Queued / Expired</c>、
/// <c>Queued → Delivered / Failed / Retrying / Expired</c>、
/// <c>Delivered → Read / Expired</c>、
/// <c>Failed → Queued / Retrying / Expired</c>、
/// <c>Retrying → Queued / Delivered / Failed / Expired</c>；
/// <see cref="OnlineNotificationState.Read"/> 与 <see cref="OnlineNotificationState.Expired"/> 为终态（无出边）。
/// 自环（同态迁移）不属于合法边，调用方<b>不得</b>为「幂等」单独豁免——「已在途（Queued）的通知再次推送」
/// 被这条规则拦下正是要的结果：在途说明另一个调用方正持有该记录，放行即重复推送（VC-6.12）。
/// </para>
/// <para>
/// 为什么 <c>Failed → Queued</c> 是合法边：重试次数耗尽落 <c>Failed</c> 的通知仍要能被离线补发与
/// 重试扫描（<c>BackfillAsync</c> / <c>RetryPendingAsync</c>）重新入队在途；缺这条边则「失败记录重投成功」
/// 无处落址（成功路径只有 <c>Queued → Delivered</c>）。
/// </para>
/// </summary>
public static class OnlineNotificationStateMachine
{
    /// <summary>合法迁移边（起始态 → 允许的目标态集合）。</summary>
    private static readonly Dictionary<OnlineNotificationState, OnlineNotificationState[]> AllowedEdges = new Dictionary<OnlineNotificationState, OnlineNotificationState[]>
    {
        {
            OnlineNotificationState.Created, new[]
            {
                OnlineNotificationState.Queued,
                OnlineNotificationState.Expired,
            }
        },
        {
            OnlineNotificationState.Queued, new[]
            {
                OnlineNotificationState.Delivered,
                OnlineNotificationState.Failed,
                OnlineNotificationState.Retrying,
                OnlineNotificationState.Expired,
            }
        },
        {
            OnlineNotificationState.Delivered, new[]
            {
                OnlineNotificationState.Read,
                OnlineNotificationState.Expired,
            }
        },
        {
            OnlineNotificationState.Failed, new[]
            {
                OnlineNotificationState.Queued,
                OnlineNotificationState.Retrying,
                OnlineNotificationState.Expired,
            }
        },
        {
            OnlineNotificationState.Retrying, new[]
            {
                OnlineNotificationState.Queued,
                OnlineNotificationState.Delivered,
                OnlineNotificationState.Failed,
                OnlineNotificationState.Expired,
            }
        },
    };

    /// <summary>
    /// 判定一次状态迁移是否合法。
    /// </summary>
    /// <param name="from">起始状态。</param>
    /// <param name="to">目标状态。</param>
    /// <returns>合法返回 <c>true</c>；自环与表外边返回 <c>false</c>。</returns>
    public static bool TryTransition(OnlineNotificationState from, OnlineNotificationState to)
    {
        OnlineNotificationState[] targets;
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
    /// 判定通知是否处于终态（不可再迁移）。
    /// </summary>
    /// <param name="state">通知状态。</param>
    /// <returns>终态（已读 / 已过期）返回 <c>true</c>。</returns>
    public static bool IsTerminal(OnlineNotificationState state)
    {
        return state == OnlineNotificationState.Read || state == OnlineNotificationState.Expired;
    }
}
