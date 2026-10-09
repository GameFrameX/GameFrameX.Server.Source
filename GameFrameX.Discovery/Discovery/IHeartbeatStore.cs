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


namespace GameFrameX.Discovery;

/// <summary>
/// 发现层心跳存储适配契约（C167：驱动语义的最小封闭面）。
/// </summary>
/// <remarks>
/// The discovery heartbeat storage seam (C167): the minimal closed surface of
/// driver semantics consumed by the generic <see cref="DiscoveryRegistry"/> /
/// <see cref="DiscoveryWatcher"/>. Each database driver implements this
/// interface once (Mongo MQL / PostgreSQL parameterized SQL); the business
/// logic — lifecycle, heartbeat loop, liveness state machine — lives entirely
/// in the generic components and never branches on the backend.
/// </remarks>
public interface IHeartbeatStore
{
    /// <summary>
    /// 幂等建 schema（Mongo：TTL 索引；PG：CREATE TABLE / INDEX IF NOT EXISTS）。
    /// </summary>
    /// <remarks>
    /// Idempotently ensures the storage schema exists (Mongo: the TTL index;
    /// PostgreSQL: CREATE TABLE / INDEX IF NOT EXISTS). Safe to call repeatedly.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 全量 upsert 一条心跳（store 以写入时刻的 UTC 时间打 lastHeartbeat 戳）。
    /// </summary>
    /// <remarks>
    /// Upserts the full heartbeat row/document. The store stamps
    /// <c>last_heartbeat</c> with the current UTC time at write time (the
    /// descriptor's <see cref="InstanceDescriptor.LastHeartbeatUtc"/> is an
    /// observation, not the write timestamp); every other field comes from the
    /// descriptor verbatim. Always the complete latest state — after a database
    /// outage the next successful write recovers every field.
    /// </remarks>
    /// <param name="instance">实例描述符 / The instance descriptor</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    Task UpsertAsync(InstanceDescriptor instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// 拉取全部心跳（防御性解析：无法解析的行 / 文档被静默跳过）。
    /// </summary>
    /// <remarks>
    /// Queries every heartbeat row/document as descriptors. Unparsable rows
    /// (unknown enum names, blank endpoints — defensive against documents
    /// written by a different version) are silently skipped, so the returned
    /// list only contains well-formed observations.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>全部可解析的实例描述符 / Every parsable instance descriptor</returns>
    Task<IReadOnlyList<InstanceDescriptor>> QueryAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除过期心跳（TTL 等效语义；Mongo 实现为 no-op，由服务端 TTL 索引兜底）。
    /// </summary>
    /// <remarks>
    /// Deletes heartbeats whose <c>last_heartbeat</c> is older than
    /// <paramref name="heartbeatTimeToLive"/>. This is the explicit TTL
    /// equivalent: the Mongo implementation is a no-op (the server-side TTL
    /// index already covers it); the PostgreSQL implementation executes the
    /// DELETE. Removal is relaxed to within one cleanup period (the watcher's
    /// three-period staleness check is the primary liveness signal and never
    /// depends on row disappearance).
    /// </remarks>
    /// <param name="heartbeatTimeToLive">心跳保存窗口 / The heartbeat expire-after window</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    Task DeleteExpiredAsync(TimeSpan heartbeatTimeToLive, CancellationToken cancellationToken = default);
}
