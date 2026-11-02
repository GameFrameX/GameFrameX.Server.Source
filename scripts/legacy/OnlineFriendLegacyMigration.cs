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
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Apps.Player.Friend.Entity;
using GameFrameX.Apps.ServerRole.Friend.Entity;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Social;

/// <summary>
/// 旧好友数据一次性迁移（C244 T3 落地，C294 自组合根逐语句迁入社交能力包；开关 =
/// <see cref="OnlineFriendOptions.MigrateLegacyFriendData"/>，模块激活期显式授权触发）：
/// 读取本进程 GameDb 的旧 <see cref="FriendRelationState"/>（域 120 旧链，Status=0 行 = 已建立关系）
/// 与旧 660 <see cref="FriendState"/> 档案文档（Outgoing = 待答申请），经 <see cref="OnlineFriendService"/>
/// 真实写路径（<see cref="OnlineFriendDomainFace"/> 编排面）幂等收敛为 Online 好友关系。
/// <para>
/// 维护约束（红线）：
/// ① 只读单向复制——旧表 / 旧文档零改动，回退 = 重部署旧版本代码即回旧链旧数据；
/// ② 幂等——重复执行经 SaveIfAbsent / 幂等收敛不产生第二条关系（已建立优先于待答）；
/// ③ 零静默丢失——单条失败计数并继续（containment，不中断整体迁移），失败明细以返回汇总披露
///（能力包零 LogHelper 惯例，逐条 Error 日志不再随迁，失败数与总数对账即披露面）；
/// ④ 分进程旧数据在 Social 库时，须在 GameDb 指向该库的进程上执行（AllInOne 天然满足，spec 披露）；
/// ⑤ 软删行（Status != 0）不迁移 = 已删除语义。
/// </para>
/// </summary>
public static class OnlineFriendLegacyMigration
{
    /// <summary>
    /// 执行旧好友数据迁移（社交能力模块在激活期、好友域桥发布后调用）。
    /// </summary>
    /// <param name="friends">好友域服务（真实写路径的唯一属主）。</param>
    /// <param name="tenantId">租户标识（宿主 OnlineScope 三元组）。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>迁移汇总（已建立关系数 / 待答申请数 / 失败数）。</returns>
    public static async Task<(int Established, int Pending, int Failed)> MigrateAsync(OnlineFriendService friends, long tenantId, long appId, long serverId, CancellationToken cancellationToken = default)
    {
        var domain = new OnlineFriendDomainFace(friends);

        // ① 域 120 旧链：FriendRelationState Status=0 → 已建立关系（Establish = 申请 + 立即接受，幂等收敛）。
        var (established, relationFailed) = await MigrateEstablishedRelationsAsync(domain, tenantId, appId, serverId, cancellationToken).ConfigureAwait(false);

        // ② 域 660 旧链：FriendState 档案 Outgoing → 待答申请（对已建立的对幂等无效果）。
        var (pending, profileFailed) = await MigratePendingRequestsAsync(domain, tenantId, appId, serverId, cancellationToken).ConfigureAwait(false);

        var failed = relationFailed + profileFailed;
        return (established, pending, failed);
    }

    /// <summary>
    /// 迁移域 120 旧链：读取 FriendRelationState Status=0 行，逐条经 Establish 收敛为已建立关系
    /// （Establish = 申请 + 立即接受，幂等收敛）；单条失败计数并继续（containment）。
    /// </summary>
    /// <param name="domain">Online 好友域契约面。</param>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>（已建立关系数，失败数）。</returns>
    private static async Task<(int Established, int Failed)> MigrateEstablishedRelationsAsync(OnlineFriendDomainFace domain, long tenantId, long appId, long serverId, CancellationToken cancellationToken)
    {
        var established = 0;
        var failed = 0;
        var relations = await GameFrameX.DataBase.GameDb.FindListAsync<FriendRelationState>(state => state.Status == 0).ConfigureAwait(false);
        foreach (var relation in relations)
        {
            var low = relation.PlayerIdA;
            var high = relation.PlayerIdB;
            if (low <= 0 || high <= 0 || low == high)
            {
                continue;
            }

            var result = await domain.EstablishAsync(PlayerScope(tenantId, appId, serverId, low), high, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess && result.Data.After == OnlineFriendRelationKind.Friend)
            {
                established++;
            }
            else
            {
                failed++;
            }
        }

        return (established, failed);
    }

    /// <summary>
    /// 迁移域 660 旧链：读取 FriendState 档案文档，逐档案的 Outgoing 待答申请经真实写路径收敛
    /// （对已建立的对幂等无效果）。
    /// </summary>
    /// <param name="domain">Online 好友域契约面。</param>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>（待答申请数，失败数）。</returns>
    private static async Task<(int Pending, int Failed)> MigratePendingRequestsAsync(OnlineFriendDomainFace domain, long tenantId, long appId, long serverId, CancellationToken cancellationToken)
    {
        var pending = 0;
        var failed = 0;
        var profiles = await GameFrameX.DataBase.GameDb.FindListAsync<FriendState>(state => state.Id >= 0).ConfigureAwait(false);
        foreach (var document in profiles)
        {
            if (document?.Profiles == null)
            {
                continue;
            }

            foreach (var profile in document.Profiles.Values)
            {
                if (profile?.Outgoing == null)
                {
                    continue;
                }

                var (entryPending, entryFailed) = await MigrateOutgoingRequestsAsync(domain, tenantId, appId, serverId, profile, cancellationToken).ConfigureAwait(false);
                pending += entryPending;
                failed += entryFailed;
            }
        }

        return (pending, failed);
    }

    /// <summary>
    /// 迁移单个旧档案的 Outgoing 待答申请：逐目标玩家校验后经 Request 真实写路径请求，
    /// 单条失败计数并继续（containment）。
    /// </summary>
    /// <param name="domain">Online 好友域契约面。</param>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="profile">旧 660 好友档案。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>（待答申请数，失败数）。</returns>
    private static async Task<(int Pending, int Failed)> MigrateOutgoingRequestsAsync(OnlineFriendDomainFace domain, long tenantId, long appId, long serverId, FriendProfileState profile, CancellationToken cancellationToken)
    {
        var pending = 0;
        var failed = 0;
        foreach (var targetPlayerId in profile.Outgoing)
        {
            if (profile.PlayerId <= 0 || targetPlayerId <= 0 || targetPlayerId == profile.PlayerId)
            {
                continue;
            }

            var result = await domain.RequestAsync(PlayerScope(tenantId, appId, serverId, profile.PlayerId), targetPlayerId, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                pending++;
            }
            else
            {
                failed++;
            }
        }

        return (pending, failed);
    }

    /// <summary>
    /// 构造玩家作用域（三元组取宿主 OnlineScope 值，玩家位为迁移主体）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>生效作用域。</returns>
    private static OnlineScope PlayerScope(long tenantId, long appId, long serverId, long playerId)
    {
        return new OnlineScope(tenantId, appId, serverId, playerId);
    }
}
