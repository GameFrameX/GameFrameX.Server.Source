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


namespace GameFrameX.Discovery.Routing;

/// <summary>
/// 玩家路由 CAS 更新结果。
/// </summary>
/// <remarks>
/// The outcome of a guarded player-route update. The three values drive
/// the generic sync target's branch policy; the interpretation (not the
/// detection) is business logic and lives in <see cref="PlayerRouteSyncTarget"/>.
/// </remarks>
public enum PlayerRouteCasOutcome
{
    /// <summary>
    /// 守卫更新命中（持久版本 == 送入版本 - 1，已成功推进）。
    /// </summary>
    /// <remarks>
    /// The guarded update matched (persisted version equals supplied minus one; the row advanced).
    /// </remarks>
    Updated = 0,

    /// <summary>
    /// 行 / 文档缺失（可能首登，可能已被删除）。
    /// </summary>
    /// <remarks>
    /// The row/document is missing (either a first login or a deleted route).
    /// </remarks>
    Missing = 1,

    /// <summary>
    /// 版本守卫未命中且行存在（持久版本已被其它进程推进）。
    /// </summary>
    /// <remarks>
    /// The version guard missed while the row exists (the persisted version was already advanced by another process).
    /// </remarks>
    Stale = 2,
}

/// <summary>
/// 玩家路由存储适配契约（驱动语义的最小封闭面）。
/// </summary>
/// <remarks>
/// The player-route storage seam: the minimal closed surface of driver
/// semantics consumed by the generic <see cref="PlayerRouteResolver"/> /
/// <see cref="PlayerRouteSyncTarget"/> / <see cref="PlayerRouteResolverBootstrap"/>.
/// Each database driver implements this interface once; the three-tier
/// resolution and the CAS branch policy are backend-neutral.
/// </remarks>
public interface IPlayerRouteStore
{
    /// <summary>
    /// 幂等建 schema（Mongo：playerId 唯一索引；PG：CREATE TABLE / INDEX IF NOT EXISTS）。
    /// </summary>
    /// <remarks>
    /// Idempotently ensures the player_route schema exists. Safe to call repeatedly.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取单个玩家的路由记录。
    /// </summary>
    /// <remarks>
    /// Reads one player's route record; null when absent.
    /// </remarks>
    /// <param name="playerId">玩家 ID / The player id</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>路由记录；缺失时为 null / The record, or null when absent</returns>
    Task<PlayerRouteRecord> GetAsync(long playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以 version CAS 守卫更新路由（持久版本 == 送入版本 - 1 才命中）。
    /// </summary>
    /// <remarks>
    /// Attempts the guarded update: it only matches when the persisted version
    /// equals the supplied version minus one. The store reports the raw outcome
    /// (matched / missing / stale) and never throws on a version miss — the
    /// three-branch policy (first-login insert / silent return /
    /// <see cref="PlayerRouteStaleException"/>) is decided by the generic sync target.
    /// </remarks>
    /// <param name="record">待写入的路由记录 / The record to write</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>CAS 结果 / The CAS outcome</returns>
    Task<PlayerRouteCasOutcome> CasUpsertAsync(PlayerRouteRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// 首登无条件写入（last-writer-wins，与并发首登语义对齐）。
    /// </summary>
    /// <remarks>
    /// Writes the first-login record unconditionally (version = 1,
    /// last-writer-wins under concurrent first logins). Only called by the
    /// generic sync target after a <see cref="PlayerRouteCasOutcome.Missing"/>
    /// outcome with <c>Version &lt;= 1</c>.
    /// </remarks>
    /// <param name="record">首登记录 / The first-login record</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    Task InsertFirstLoginAsync(PlayerRouteRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除单个玩家的路由记录（幂等；缺失不报错）。
    /// </summary>
    /// <remarks>
    /// Deletes one player's route record; a missing record is not an error (idempotent semantics).
    /// </remarks>
    /// <param name="playerId">玩家 ID / The player id</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    Task DeleteAsync(long playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除过期路由（30 天窗口；客户端 TTL 等效语义，两个后端都执行真实删除）。
    /// </summary>
    /// <remarks>
    /// Deletes routes whose <c>last_seen_at</c> is older than
    /// <paramref name="timeToLive"/> (the 30-day offline garbage window). Both
    /// the Mongo and PostgreSQL implementations execute the real delete,
    /// driven by the registry's cleanup loop. Removal is relaxed to within one
    /// cleanup period.
    /// </remarks>
    /// <param name="timeToLive">路由保存窗口 / The expire-after window</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    Task DeleteExpiredAsync(TimeSpan timeToLive, CancellationToken cancellationToken = default);
}
