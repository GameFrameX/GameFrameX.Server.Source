// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织及其贡献者不承担任何责任，开发者应自行承担。
//   The organization and contributors assume no responsibility; developers shall bear the responsibility themselves.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:    https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Apps.Player.Friend.Entity;
using GameFrameX.Apps.ServerRole.Friend.Entity;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Launcher.Online;

/// <summary>
/// 旧好友数据一次性迁移（C244 T3，开关 <c>Online.MigrateLegacyFriendData</c> 显式授权）：
/// 读取本进程 GameDb 的旧 <see cref="FriendRelationState"/>（域 120 旧链，Status=0 行 = 已建立关系）
/// 与旧 660 <see cref="FriendState"/> 档案文档（Outgoing = 待答申请），经 Online 好友域契约面真实写路径
/// 幂等收敛为 Online 好友关系。
/// <para>
/// 维护约束（红线）：
/// ① 只读单向复制——旧表 / 旧文档零改动，回退 = 重部署旧版本代码即回旧链旧数据；
/// ② 幂等——重复执行经 SaveIfAbsent / 幂等收敛不产生第二条关系（已建立优先于待答）；
/// ③ 零静默丢失——单条失败逐条 Error 披露并继续（containment），结束输出汇总；
/// ④ 分进程旧数据在 Social 库时，须在 GameDb 指向该库的进程上执行（AllInOne 天然满足，spec 披露）；
/// ⑤ 软删行（Status != 0）不迁移 = 已删除语义。
/// </para>
/// </summary>
/// <remarks>
/// The one-shot legacy friend-data migration (C244 T3, explicitly authorized by the
/// <c>Online.MigrateLegacyFriendData</c> switch): reads this process's GameDb — the legacy
/// <see cref="FriendRelationState"/> rows (the domain-120 chain; Status=0 means established) and the
/// legacy domain-660 <see cref="FriendState"/> profile documents (Outgoing = pending requests) — and
/// converges them into Online friendships through the real Online friend-domain contract write path.
/// It is a read-only one-way copy (the legacy tables and documents are never modified; rollback =
/// redeploying the legacy version restores the legacy chain over the legacy data); repeated runs are
/// idempotent (SaveIfAbsent / idempotent convergence, never a second record — established wins over
/// pending); nothing is silently lost — per-row failures are disclosed with Error logs and contained
/// (the run continues) with a summary at the end; when split-process legacy data lives in the Social
/// database the migration must run on a process whose GameDb points there (AllInOne satisfies this
/// naturally — disclosed in the spec); soft-deleted rows (Status != 0) are not migrated (deleted
/// semantics).
/// </remarks>
public static class OnlineFriendLegacyMigration
{
    /// <summary>
    /// 执行旧好友数据迁移（组合根在业务运行时启动且域桥发布后调用）。
    /// </summary>
    /// <param name="runtime">业务能力宿主。</param>
    /// <returns>迁移汇总（已建立关系数 / 待答申请数 / 失败数）。</returns>
    public static async Task<(int Established, int Pending, int Failed)> MigrateAsync(CapabilityHost runtime)
    {
        if (!runtime.Services.TryGetService<IOnlineFriendDomain>(out var domain))
        {
            // Localization: Launcher.OnlineFriendBridgeFaceMissing - Online 好友域桥契约面不可解析（online-social 未装配或被排除），好友消息将显式失败
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendBridgeFaceMissing));
            return (0, 0, 0);
        }

        // ① 域 120 旧链：FriendRelationState Status=0 → 已建立关系（Establish = 申请 + 立即接受，幂等收敛）。
        var (established, relationFailed) = await MigrateEstablishedRelationsAsync(runtime, domain).ConfigureAwait(false);

        // ② 域 660 旧链：FriendState 档案 Outgoing → 待答申请（对已建立的对幂等无效果）。
        var (pending, profileFailed) = await MigratePendingRequestsAsync(runtime, domain).ConfigureAwait(false);

        var failed = relationFailed + profileFailed;

        // Localization: Launcher.OnlineFriendLegacyMigrationDone - 旧好友数据迁移完成：已建立 {0}，待答申请 {1}，失败 {2}（旧数据零改动）
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendLegacyMigrationDone, established, pending, failed));
        return (established, pending, failed);
    }

    /// <summary>
    /// 迁移域 120 旧链：读取 FriendRelationState Status=0 行，逐条经 Establish 收敛为已建立关系
    /// （Establish = 申请 + 立即接受，幂等收敛）；单条失败逐条 Error 披露并继续（containment）。
    /// </summary>
    /// <param name="runtime">业务能力宿主。</param>
    /// <param name="domain">Online 好友域契约面。</param>
    /// <returns>（已建立关系数，失败数）。</returns>
    private static async Task<(int Established, int Failed)> MigrateEstablishedRelationsAsync(CapabilityHost runtime, IOnlineFriendDomain domain)
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

            var result = await domain.EstablishAsync(PlayerScope(runtime, low), high).ConfigureAwait(false);
            if (result.IsSuccess && result.Data.After == OnlineFriendRelationKind.Friend)
            {
                established++;
            }
            else
            {
                failed++;
                // Localization: Launcher.OnlineFriendLegacyMigrationEntryFailed - 旧好友关系迁移单条失败：{0} <-> {1}, Code: {2}, Message: {3}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendLegacyMigrationEntryFailed, low, high, result.Code, result.Message));
            }
        }

        return (established, failed);
    }

    /// <summary>
    /// 迁移域 660 旧链：读取 FriendState 档案文档，逐档案的 Outgoing 待答申请经真实写路径收敛
    /// （对已建立的对幂等无效果）。
    /// </summary>
    /// <param name="runtime">业务能力宿主。</param>
    /// <param name="domain">Online 好友域契约面。</param>
    /// <returns>（待答申请数，失败数）。</returns>
    private static async Task<(int Pending, int Failed)> MigratePendingRequestsAsync(CapabilityHost runtime, IOnlineFriendDomain domain)
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

                var (entryPending, entryFailed) = await MigrateOutgoingRequestsAsync(runtime, domain, profile).ConfigureAwait(false);
                pending += entryPending;
                failed += entryFailed;
            }
        }

        return (pending, failed);
    }

    /// <summary>
    /// 迁移单个旧档案的 Outgoing 待答申请：逐目标玩家校验后经 Request 真实写路径请求，
    /// 单条失败逐条 Error 披露并继续（containment）。
    /// </summary>
    /// <param name="runtime">业务能力宿主。</param>
    /// <param name="domain">Online 好友域契约面。</param>
    /// <param name="profile">旧 660 好友档案。</param>
    /// <returns>（待答申请数，失败数）。</returns>
    private static async Task<(int Pending, int Failed)> MigrateOutgoingRequestsAsync(CapabilityHost runtime, IOnlineFriendDomain domain, FriendProfileState profile)
    {
        var pending = 0;
        var failed = 0;
        foreach (var targetPlayerId in profile.Outgoing)
        {
            if (profile.PlayerId <= 0 || targetPlayerId <= 0 || targetPlayerId == profile.PlayerId)
            {
                continue;
            }

            var result = await domain.RequestAsync(PlayerScope(runtime, profile.PlayerId), targetPlayerId).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                pending++;
            }
            else
            {
                failed++;
                // Localization: Launcher.OnlineFriendLegacyMigrationEntryFailed - 旧好友关系迁移单条失败：{0} <-> {1}, Code: {2}, Message: {3}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendLegacyMigrationEntryFailed, profile.PlayerId, targetPlayerId, result.Code, result.Message));
            }
        }

        return (pending, failed);
    }

    /// <summary>
    /// 构造玩家作用域（三元组取宿主授权值，玩家位为迁移主体）。
    /// </summary>
    /// <param name="runtime">业务能力宿主。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>生效作用域。</returns>
    private static OnlineScope PlayerScope(CapabilityHost runtime, long playerId)
    {
        return new OnlineScope(runtime.TenantId, runtime.AppId, runtime.ServerId, playerId);
    }
}
