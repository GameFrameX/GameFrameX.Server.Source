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
//   please see the LICENSE file in the root directory for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Persistence.Storage;

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Foundation.Storage;
using GameFrameX.DataBase;

/// <summary>
/// 玩家云存储 GameDb 持久化适配（一级 store；CAS 乐观锁经条件写原语落地）。
/// <para>
/// CAS 语义（版本换算）：expectedVersion == 0 → <c>TryAddUniqueAsync</c>（五元组唯一索引互斥，
/// 已存在即 false）；&gt; 0 → <c>UpdateIfRowVersionAsync(行Id, expectedVersion - 1, 新档)</c>
/// （行缺失或存量 RowVersion 不符即 false——原子单语句比较交换）。恒等式
/// <c>实体 Version = 行 RowVersion + 1</c>（实体从 1 起、行版本从 0 起），读回按此校准。
/// </para>
/// <para>
/// 软删/复活语义：条目行的 <c>DeletedAtTime</c> 业务字段为唯一可见性事实源——
/// <c>ListAsync</c>/<c>CountActiveKeysAsync</c> 过滤、<c>FindAsync</c>/<c>ListAllAsync</c> 不过滤；
/// 软删/复活/过期清理全部经 CAS 写路径（版本接管），适配层零删除调用，行物理长存。
/// TTL Sweep 的全表扫描输入（<c>ListAllAsync</c>）为全量物化（filter 恒真），不做增量游标。
/// </para>
/// <para>
/// 列举保真：provider 字符串排序为 culture-aware（Mongo 对齐比较器 / PG 库排序规则），
/// 与 InMemory 的 ordinal 字典序在大小写混合/非 ASCII 键上不同——本适配对「玩家集合内列举」采用
/// 等值条件下推 + 客户端 ordinal 过滤/排序/截取，与 InMemory 语义逐字节一致（超额拉取上界 =
/// 单玩家单集合条目数）；游标分页不漏不重由「过滤与排序同用 ordinal」保证。
/// 单写者假设继承 store 契约，不引入分布式协调。
/// </para>
/// </summary>
public sealed class GameDbOnlinePlayerStorageStore : IOnlinePlayerStorageStore
{
    /// <summary>
    /// 行 Id 分配序列名（键控 = 状态类型名 + 序列名）。
    /// </summary>
    private const string RowIdSequence = "row-id";

    /// <summary>按键查找条目（含软删条目；可见性判定归服务层）。</summary>
    /// <param name="key">条目键载荷。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>条目；不存在返回 null。</returns>
    public async Task<OnlinePlayerStorageEntry> FindAsync(OnlineStorageEntryKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var state = await FindStateAsync(BuildKey(key.TenantId, key.AppId, key.PlayerId, key.Collection, key.Key), cancellationToken).ConfigureAwait(false);
        return state == null ? null : ToEntity(state);
    }

    /// <summary>
    /// 原子比较交换写入（乐观锁；expectedVersion=0 为仅创建，&gt;0 为版本匹配更新）。
    /// </summary>
    /// <param name="entry">待写入条目（Version 由调用方预置为目标版本）。</param>
    /// <param name="expectedVersion">期望版本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>交换成功返回 true；版本不匹配或创建冲突返回 false。</returns>
    public async Task<bool> UpsertAsync(OnlinePlayerStorageEntry entry, long expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var uniqueKey = BuildKey(entry.TenantId, entry.AppId, entry.PlayerId, entry.Collection, entry.Key);
        var existing = await FindStateAsync(uniqueKey, cancellationToken).ConfigureAwait(false);
        if (expectedVersion == 0)
        {
            if (existing != null)
            {
                return false;
            }

            var rowId = await GameDb.NextSequenceAsync<OnlinePlayerStorageEntryState>(RowIdSequence, cancellationToken).ConfigureAwait(false);
            return await GameDb.TryAddUniqueAsync(ToState(entry, uniqueKey, rowId), state => state.UniqueKey == uniqueKey, cancellationToken).ConfigureAwait(false);
        }

        if (existing == null)
        {
            return false;
        }

        // 行版本从 0 起、实体版本从 1 起：期望实体版本 - 1 = 期望行版本；CAS 写入后行版本 = 期望实体版本 = 目标版本 - 1。
        return await GameDb.UpdateIfRowVersionAsync(existing.Id, expectedVersion - 1, ToState(entry, uniqueKey, existing.Id), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>按键字典序列举集合内非软删条目（游标 = 排序起始键，不含；客户端 ordinal 保真）。</summary>
    /// <param name="query">列举查询载荷。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>非软删条目列表（按键 ordinal 字典序）。</returns>
    public async Task<IReadOnlyList<OnlinePlayerStorageEntry>> ListAsync(OnlineStorageListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var afterKey = query.AfterKey ?? string.Empty;
        var states = await GameDb.FindListAsync<OnlinePlayerStorageEntryState>(state => state.TenantId == query.TenantId && state.AppId == query.AppId && state.PlayerId == query.PlayerId && state.Collection == query.Collection, cancellationToken).ConfigureAwait(false);
        return states.Where(state => state.DeletedAtTime == 0)
            .Where(state => afterKey.Length == 0 || string.CompareOrdinal(state.Key, afterKey) > 0)
            .OrderBy(state => state.Key, StringComparer.Ordinal)
            .Take(query.MaxCount)
            .Select(ToEntity)
            .ToList();
    }

    /// <summary>统计集合内活跃（非软删）键数。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="collection">集合名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>活跃键数。</returns>
    public async Task<int> CountActiveKeysAsync(long tenantId, long appId, long playerId, string collection, CancellationToken cancellationToken = default)
    {
        var count = await GameDb.CountAsync<OnlinePlayerStorageEntryState>(state => state.TenantId == tenantId && state.AppId == appId && state.PlayerId == playerId && state.Collection == collection && state.DeletedAtTime == 0, cancellationToken).ConfigureAwait(false);
        return checked((int)count);
    }

    /// <summary>列出全部条目（含软删；过期清理任务的扫描输入——全量物化）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全部条目列表。</returns>
    public async Task<IReadOnlyList<OnlinePlayerStorageEntry>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var states = await GameDb.FindListAsync<OnlinePlayerStorageEntryState>(state => true, cancellationToken).ConfigureAwait(false);
        return states.Select(ToEntity).ToList();
    }

    /// <summary>按物化复合键查找条目行。</summary>
    /// <param name="uniqueKey">物化复合键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>条目行；不存在返回 null。</returns>
    private static Task<OnlinePlayerStorageEntryState> FindStateAsync(string uniqueKey, CancellationToken cancellationToken)
    {
        return GameDb.FindAsync<OnlinePlayerStorageEntryState>(state => state.UniqueKey == uniqueKey, isCreateIfNotExists: false, cancellationToken: cancellationToken);
    }

    /// <summary>构造条目完整键（与 InMemory BuildKey 同构）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="collection">集合名。</param>
    /// <param name="key">条目键。</param>
    /// <returns>复合键字符串。</returns>
    private static string BuildKey(long tenantId, long appId, long playerId, string collection, string key)
    {
        return tenantId + ":" + appId + ":" + playerId + ":" + collection + ":" + key;
    }

    /// <summary>条目实体 → 行（指定行 Id；RowVersion 由写原语管理，不作映射输入）。</summary>
    private static OnlinePlayerStorageEntryState ToState(OnlinePlayerStorageEntry entry, string uniqueKey, long rowId)
    {
        return new OnlinePlayerStorageEntryState
        {
            Id = rowId,
            UniqueKey = uniqueKey,
            TenantId = entry.TenantId,
            AppId = entry.AppId,
            PlayerId = entry.PlayerId,
            Collection = entry.Collection,
            Key = entry.Key,
            Payload = entry.Payload,
            CreatedAtTime = entry.CreatedAtTime,
            CreatedBy = entry.CreatedBy,
            UpdatedAtTime = entry.UpdatedAtTime,
            UpdatedBy = entry.UpdatedBy,
            ExpiresAtTime = entry.ExpiresAtTime,
            DeletedAtTime = entry.DeletedAtTime,
        };
    }

    /// <summary>条目行 → 实体（版本校准：实体 Version = 行 RowVersion + 1）。</summary>
    private static OnlinePlayerStorageEntry ToEntity(OnlinePlayerStorageEntryState state)
    {
        return new OnlinePlayerStorageEntry
        {
            TenantId = state.TenantId,
            AppId = state.AppId,
            PlayerId = state.PlayerId,
            Collection = state.Collection,
            Key = state.Key,
            Payload = state.Payload,
            Version = (state.RowVersion ?? 0) + 1,
            CreatedAtTime = state.CreatedAtTime,
            CreatedBy = state.CreatedBy,
            UpdatedAtTime = state.UpdatedAtTime,
            UpdatedBy = state.UpdatedBy,
            ExpiresAtTime = state.ExpiresAtTime,
            DeletedAtTime = state.DeletedAtTime,
        };
    }
}
