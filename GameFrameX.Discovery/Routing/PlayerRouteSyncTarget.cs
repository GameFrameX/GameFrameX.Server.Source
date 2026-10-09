// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Discovery.Routing;

/// <summary>
/// 通用玩家路由同步目标（自 Mongo / PG 平行实现归一，消费 <see cref="IPlayerRouteStore"/>）。
/// </summary>
/// <remarks>
/// The generic player-route sync target (unified from the Mongo /
/// PostgreSQL parallel implementations; consumes <see cref="IPlayerRouteStore"/>
/// and never branches on the backend). Each upsert runs the unified CAS
/// policy — the supplied version must equal the current version + 1 — with
/// three branches on a guarded-update miss: a missing record with
/// <c>Version &lt;= 1</c> (first login) inserts unconditionally; a missing
/// record with <c>Version &gt; 1</c> returns silently (the next SetOnline
/// retries); an existing record whose version no longer satisfies
/// current+1 throws <see cref="PlayerRouteStaleException"/> for the
/// SessionManager hook to swallow.
/// </remarks>
public sealed class PlayerRouteSyncTarget : IPlayerRouteSyncTarget
{
    /// <summary>
    /// 玩家路由存储适配。
    /// </summary>
    /// <remarks>
    /// The player-route storage seam.
    /// </remarks>
    private readonly IPlayerRouteStore _store;

    /// <summary>
    /// 初始化通用同步目标。
    /// </summary>
    /// <remarks>
    /// Initializes the sync target. Schema creation is the bootstrap's responsibility;
    /// the sync target only writes.
    /// </remarks>
    /// <param name="store">玩家路由存储适配 / The player-route storage seam</param>
    public PlayerRouteSyncTarget(IPlayerRouteStore store)
    {
        ArgumentNullException.ThrowIfNull(store, nameof(store));
        _store = store;
    }

    /// <summary>
    /// 以 version CAS 语义把玩家路由原子写入控制库 player_route 表。
    /// </summary>
    /// <remarks>
    /// Atomically writes the player route into the control database. Non-positive
    /// player ids are ignored. The CAS guard requires the persisted version to
    /// equal the supplied version minus one; on a miss the three-branch policy
    /// applies (first-login insert / silent return / stale exception).
    /// </remarks>
    /// <param name="record">待写入的玩家路由记录 / The player-route record to write</param>
    /// <returns>异步任务 / Async task</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="record"/> 为 null 时抛出 / Thrown when record is null</exception>
    /// <exception cref="ArgumentException">当 <paramref name="record"/> 的 <c>InstanceId</c> 为空白时抛出 / Thrown when the record's InstanceId is blank</exception>
    /// <exception cref="PlayerRouteStaleException">当控制库中的版本已不满足 current+1 时抛出 / Thrown when the persisted version no longer satisfies current+1</exception>
    public async Task UpsertAsync(PlayerRouteRecord record)
    {
        ArgumentNullException.ThrowIfNull(record, nameof(record));

        var playerId = record.PlayerId;
        var instanceId = record.InstanceId;
        var version = record.Version;

        if (playerId <= 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            // Localization: Discovery.PlayerRouteSync.InstanceIdEmpty - 实例 Id 不能为空。
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Discovery.PlayerRouteSync.InstanceIdEmpty), nameof(record));
        }

        // 统一 CAS：送入 version 必须等于当前 version + 1（旧记录 version == version-1）。
        // Unified CAS: the supplied version must equal the current version + 1.
        var outcome = await _store.CasUpsertAsync(record).ConfigureAwait(false);
        if (outcome == PlayerRouteCasOutcome.Updated)
        {
            return;
        }

        if (outcome == PlayerRouteCasOutcome.Stale)
        {
            // 版本竞争失败：回读最新版本构造异常（供 SessionManager 钩子吞掉）。
            var latest = await _store.GetAsync(playerId).ConfigureAwait(false);
            throw new PlayerRouteStaleException(playerId, version, latest?.Version ?? version);
        }

        if (version <= 1)
        {
            // 首登（记录缺失且 version<=1）无条件写入；并发首登 last-writer-wins（后写者胜出）。
            await _store.InsertFirstLoginAsync(record).ConfigureAwait(false);
        }

        // 记录缺失且 version>1：静默返回（下一轮 SetOnline 重试）。
    }

    /// <summary>
    /// 从控制库 player_route 表删除该玩家的路由记录。
    /// </summary>
    /// <remarks>
    /// Deletes the player's route record; non-positive ids are ignored and a missing
    /// record is not an error (idempotent semantics).
    /// </remarks>
    /// <param name="playerId">玩家 ID / Player id</param>
    /// <returns>异步任务 / Async task</returns>
    public Task DeleteAsync(long playerId)
    {
        if (playerId <= 0)
        {
            return Task.CompletedTask;
        }

        return _store.DeleteAsync(playerId);
    }
}
