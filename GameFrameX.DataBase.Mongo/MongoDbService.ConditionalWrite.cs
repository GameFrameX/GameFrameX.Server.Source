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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Orm.Entity;
using GameFrameX.Localization;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GameFrameX.DataBase.Mongo;

/// <summary>
/// MongoDB 服务的条件写原语实现（CAS 乐观锁、唯一性插入、原子序列）。
/// </summary>
/// <remarks>
/// Conditional write primitives for the MongoDB service (CAS optimistic lock,
/// uniqueness-guarded insert and atomic sequence).
/// </remarks>
public sealed partial class MongoDbService
{
    /// <summary>
    /// 序列计数器集合名（内部专用）。
    /// </summary>
    /// <remarks>
    /// The sequence counter collection name (internal use only).
    /// </remarks>
    private const string SequenceCollectionName = "__sequences";

    /// <summary>
    /// 序列计数器文档中的计数字段名。
    /// </summary>
    /// <remarks>
    /// The counter field name inside a sequence counter document.
    /// </remarks>
    private const string SequenceValueFieldName = "seq";

    /// <summary>
    /// 序列号分配的 DuplicateKey 并发竞态最大重试次数（upsert 首插竞态）。
    /// </summary>
    /// <remarks>
    /// Maximum retry count for DuplicateKey races during sequence allocation
    /// (upsert first-insert races).
    /// </remarks>
    private const int SequenceDuplicateKeyRetryTotal = 3;

    #region 条件写

    /// <summary>
    /// 条件写：仅当存量文档的行版本号数值等值于期望值时整档写入（CAS / 乐观锁原语）。
    /// </summary>
    /// <remarks>
    /// Conditional write: replaces the whole document only when the stored row version
    /// numerically equals the expected value (CAS / optimistic-lock primitive).
    /// See <see cref="IDatabaseService.UpdateIfRowVersionAsync{TState}(long, long, TState)"/> for the full
    /// concurrency contract.
    /// </remarks>
    /// <typeparam name="TState">文档的类型，必须继承自 BaseCacheState / Document type, must inherit from BaseCacheState</typeparam>
    /// <param name="id">数据 ID（须与 <paramref name="state"/> 的 Id 一致）/ The document ID (must equal <paramref name="state"/>'s Id)</param>
    /// <param name="expectedRowVersion">期望的存量行版本号 / The expected stored row version</param>
    /// <param name="state">要写入的数据对象 / The data object to write</param>
    /// <returns>成功写入返回 true；版本不符或行缺失返回 false / true when written; false on version mismatch or missing row</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="state"/> 的 Id 与 <paramref name="id"/> 不一致时抛出。
    /// Thrown when <paramref name="state"/>'s Id differs from <paramref name="id"/>.
    /// </exception>
    /// <exception cref="OverflowException">
    /// <paramref name="expectedRowVersion"/> 为 long.MaxValue 递增溢出时抛出（此情况不写库）。
    /// Thrown when incrementing <paramref name="expectedRowVersion"/> == long.MaxValue overflows (nothing is written in that case).
    /// </exception>
    public Task<bool> UpdateIfRowVersionAsync<TState>(long id, long expectedRowVersion, TState state) where TState : BaseCacheState, new()
    {
        return UpdateIfRowVersionAsync(id, expectedRowVersion, state, CancellationToken.None);
    }

    /// <summary>
    /// 条件写：仅当存量文档的行版本号数值等值于期望值时整档写入（CAS / 乐观锁原语）。
    /// </summary>
    /// <remarks>
    /// Conditional write: replaces the whole document only when the stored row version
    /// numerically equals the expected value (CAS / optimistic-lock primitive).
    /// See <see cref="IDatabaseService.UpdateIfRowVersionAsync{TState}(long, long, TState, CancellationToken)"/> for the full
    /// concurrency contract.
    /// </remarks>
    /// <typeparam name="TState">文档的类型，必须继承自 BaseCacheState / Document type, must inherit from BaseCacheState</typeparam>
    /// <param name="id">数据 ID（须与 <paramref name="state"/> 的 Id 一致）/ The document ID (must equal <paramref name="state"/>'s Id)</param>
    /// <param name="expectedRowVersion">期望的存量行版本号 / The expected stored row version</param>
    /// <param name="state">要写入的数据对象 / The data object to write</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>成功写入返回 true；版本不符或行缺失返回 false / true when written; false on version mismatch or missing row</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="state"/> 的 Id 与 <paramref name="id"/> 不一致时抛出。
    /// Thrown when <paramref name="state"/>'s Id differs from <paramref name="id"/>.
    /// </exception>
    /// <exception cref="OverflowException">
    /// <paramref name="expectedRowVersion"/> 为 long.MaxValue 递增溢出时抛出（此情况不写库）。
    /// Thrown when incrementing <paramref name="expectedRowVersion"/> == long.MaxValue overflows (nothing is written in that case).
    /// </exception>
    public async Task<bool> UpdateIfRowVersionAsync<TState>(long id, long expectedRowVersion, TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        if (state.Id != id)
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Database.ConditionalWriteStateIdMismatch, state.Id, id), nameof(state));
        }

        // 版本准备：先对齐到期望版本，再吃 checked 递增语义；失败（false）路径回滚内存版本——写前递增若不回滚
        // 会污染调用方 state.RowVersion，后续 CAS 持续 Stale；异常路径不回滚（写结果未知，回滚可能反向脱节）。
        // Version preparation: align to the expected version first, then take the checked increment. The
        // pre-write increment is rolled back on the false path — leaving it in place would pollute the caller's
        // state.RowVersion and starve every later CAS; it is NOT rolled back on exceptions (the write outcome
        // is unknown, and rolling back could desync the other way).
        var originalRowVersion = state.RowVersion;
        state.RowVersion = expectedRowVersion;
        state.IncrementRowVersion();

        // 写库前捕获字节快照（行版本递增后），成功后按该快照精确提交基线
        var persistedData = state.ToBytes();

        var collection = GetCollection<TState>();

        // 属性索引引导（异步单飞）：沿用原类型化入口的引导语义，先于本次条件写完成。
        // Attribute-index bootstrap (asynchronous single flight): keeps the former typed-entry
        // bootstrap semantics and completes before this conditional write.
        await CreateIndexesAsync(collection, cancellationToken).ConfigureAwait(false);
        var filter = Builders<TState>.Filter.And(
            Builders<TState>.Filter.Eq(m => m.Id, id),
            Builders<TState>.Filter.Eq(m => m.RowVersion, (long?)expectedRowVersion));
        var replaced = await ExecuteWriteWithRetryAsync(async token =>
        {
            var options = new FindOneAndReplaceOptions<TState> { ReturnDocument = ReturnDocument.Before };
            return await FindOneAndReplaceWithAmbientSessionAsync(collection, filter, state, options, token).ConfigureAwait(false);
        }, cancellationToken, nameof(UpdateIfRowVersionAsync), false).ConfigureAwait(false);

        if (replaced == null)
        {
            // 版本不符或行缺失：未写库，回滚写前递增的内存版本。
            // Version mismatch or missing row: nothing was written — roll back the pre-write in-memory increment.
            state.RowVersion = originalRowVersion;
            return false;
        }

        state.SaveToDbPostHandler(persistedData);
        return true;
    }

    /// <summary>
    /// 唯一性插入：仅当无既有文档满足唯一性条件时插入（冲突返回 false 而非异常）。
    /// </summary>
    /// <remarks>
    /// Uniqueness-guarded insert: inserts only when no existing document satisfies the
    /// uniqueness filter (conflicts return false instead of throwing).
    /// The uniqueness probe deliberately ignores soft-delete visibility, matching
    /// <see cref="IDatabaseService.TryAddUniqueAsync{TState}(TState, Expression{Func{TState, bool}})"/>.
    /// 完全竞态安全的前置条件：唯一性条件引用的字段必须存在 provider 唯一索引
    /// （如 <c>EntityIndexAttribute(Unique = true)</c> 声明，调用即引导先于查插完成）；
    /// 缺失该索引时先查后插仅兜底顺序冲突，并发同键可能双双入表而非返回 false。
    /// Full race-safety precondition: the fields referenced by the uniqueness filter must be
    /// covered by a provider unique index (e.g. declared via <c>EntityIndexAttribute(Unique = true)</c>;
    /// the bootstrap on this call completes before probe-and-insert). Without such an index the
    /// check-then-insert sequence only covers sequential conflicts — concurrent same-key callers
    /// may both insert instead of one receiving false.
    /// </remarks>
    /// <typeparam name="TState">文档的类型，必须继承自 BaseCacheState / Document type, must inherit from BaseCacheState</typeparam>
    /// <param name="state">要插入的数据对象 / The data object to insert</param>
    /// <param name="uniqueFilter">唯一性判定条件（无视软删可见性）/ The uniqueness predicate (soft-delete visibility ignored)</param>
    /// <returns>插入成功返回 true；唯一键命中或 Id 已占用返回 false / true when inserted; false on uniqueness hit or occupied Id</returns>
    public Task<bool> TryAddUniqueAsync<TState>(TState state, Expression<Func<TState, bool>> uniqueFilter) where TState : BaseCacheState, new()
    {
        return TryAddUniqueAsync(state, uniqueFilter, CancellationToken.None);
    }

    /// <summary>
    /// 唯一性插入：仅当无既有文档满足唯一性条件时插入（冲突返回 false 而非异常）。
    /// </summary>
    /// <remarks>
    /// Uniqueness-guarded insert: inserts only when no existing document satisfies the
    /// uniqueness filter (conflicts return false instead of throwing).
    /// The uniqueness probe deliberately ignores soft-delete visibility, matching
    /// <see cref="IDatabaseService.TryAddUniqueAsync{TState}(TState, Expression{Func{TState, bool}}, CancellationToken)"/>.
    /// 完全竞态安全的前置条件：唯一性条件引用的字段必须存在 provider 唯一索引
    /// （如 <c>EntityIndexAttribute(Unique = true)</c> 声明，调用即引导先于查插完成）；
    /// 缺失该索引时先查后插仅兜底顺序冲突，并发同键可能双双入表而非返回 false。
    /// Full race-safety precondition: the fields referenced by the uniqueness filter must be
    /// covered by a provider unique index (e.g. declared via <c>EntityIndexAttribute(Unique = true)</c>;
    /// the bootstrap on this call completes before probe-and-insert). Without such an index the
    /// check-then-insert sequence only covers sequential conflicts — concurrent same-key callers
    /// may both insert instead of one receiving false.
    /// </remarks>
    /// <typeparam name="TState">文档的类型，必须继承自 BaseCacheState / Document type, must inherit from BaseCacheState</typeparam>
    /// <param name="state">要插入的数据对象 / The data object to insert</param>
    /// <param name="uniqueFilter">唯一性判定条件（无视软删可见性）/ The uniqueness predicate (soft-delete visibility ignored)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>插入成功返回 true；唯一键命中或 Id 已占用返回 false / true when inserted; false on uniqueness hit or occupied Id</returns>
    public async Task<bool> TryAddUniqueAsync<TState>(TState state, Expression<Func<TState, bool>> uniqueFilter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        var currentTime = GetCurrentTimestamp();
        state.CreatedTime = currentTime;
        state.UpdateTime = currentTime;
        state.UpdateCount ??= 0;

        var collection = GetCollection<TState>();

        // 唯一索引引导必须先于查插（异步单飞）：并发首触不得在索引在建时穿透，
        // 否则唯一键窗口内重复文档将被放行。原类型化入口内联引导语义保持。
        // Bootstrap the unique indexes BEFORE probe-and-insert (asynchronous single flight):
        // a concurrent first touch must not slip through while the index build is in flight,
        // or duplicate documents would pass through the uniqueness window. This preserves the
        // former typed-entry inline bootstrap semantics.
        await CreateIndexesAsync(collection, cancellationToken).ConfigureAwait(false);
        var uniquenessFilter = new ExpressionFilterDefinition<TState>(uniqueFilter);
        return await ExecuteWriteWithRetryAsync(async token =>
        {
            // 唯一性预查与插入放在同一个操作内：弹性层重试时重新预查，保证重试后仍然冲突安全。
            // Probe and insert live in one operation so a resilience retry re-runs the probe first.
            var existing = await FindWithAmbientSession(collection, uniquenessFilter).Limit(1).FirstOrDefaultAsync(token).ConfigureAwait(false);
            if (existing != null)
            {
                return false;
            }

            try
            {
                await InsertOneWithAmbientSessionAsync(collection, state, token).ConfigureAwait(false);
                return true;
            }
            catch (MongoWriteException we) when (we.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // 并发窗口内被抢先插入：唯一索引冲突转译为 false 而非上抛。
                // Lost the concurrent race: translate the unique-index conflict into false.
                return false;
            }
        }, cancellationToken, nameof(TryAddUniqueAsync), false).ConfigureAwait(false);
    }

    /// <summary>
    /// 原子序列：为指定类型分配下一个单调递增序列号（首次为 1，永不回退）。
    /// </summary>
    /// <remarks>
    /// Atomic sequence: allocates the next monotonically increasing sequence number for
    /// the given type (first value is 1; the counter never repeats or regresses).
    /// See <see cref="IDatabaseService.NextSequenceAsync{TState}(string)"/>.
    /// </remarks>
    /// <typeparam name="TState">序列归属的类型，必须继承自 BaseCacheState / The owning type of the sequence, must inherit from BaseCacheState</typeparam>
    /// <param name="sequenceName">序列名（与类型名拼接构成计数键）/ The sequence name (combined with the type name to form the counter key)</param>
    /// <returns>分配到的新序列号（首次为 1）/ The newly allocated sequence number (first value is 1)</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="sequenceName"/> 为 null 或空白时抛出。
    /// Thrown when <paramref name="sequenceName"/> is null or whitespace.
    /// </exception>
    public Task<long> NextSequenceAsync<TState>(string sequenceName) where TState : BaseCacheState, new()
    {
        return NextSequenceAsync<TState>(sequenceName, CancellationToken.None);
    }

    /// <summary>
    /// 原子序列：为指定类型分配下一个单调递增序列号（首次为 1，永不回退）。
    /// </summary>
    /// <remarks>
    /// Atomic sequence: allocates the next monotonically increasing sequence number for
    /// the given type (first value is 1; the counter never repeats or regresses).
    /// Implemented via <c>FindOneAndUpdate</c> with <c>$inc</c> + upsert on an internal
    /// <c>__sequences</c> collection; the operation is single-document atomic in MongoDB,
    /// so it is deliberately NOT wrapped in <see cref="ExecuteWriteWithRetryAsync{T}(Func{CancellationToken, Task{T}}, CancellationToken, string, bool)"/>
    /// to avoid double-layer retries — only the upsert first-insert DuplicateKey race
    /// (E11000) gets a bounded local retry below.
    /// See <see cref="IDatabaseService.NextSequenceAsync{TState}(string, CancellationToken)"/>.
    /// </remarks>
    /// <typeparam name="TState">序列归属的类型，必须继承自 BaseCacheState / The owning type of the sequence, must inherit from BaseCacheState</typeparam>
    /// <param name="sequenceName">序列名（与类型名拼接构成计数键）/ The sequence name (combined with the type name to form the counter key)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="sequenceName"/> 为 null 或空白时抛出。
    /// Thrown when <paramref name="sequenceName"/> is null or whitespace.
    /// </exception>
    public async Task<long> NextSequenceAsync<TState>(string sequenceName, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(sequenceName))
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Database.ConditionalWriteSequenceNameRequired), nameof(sequenceName));
        }

        var key = typeof(TState).Name + ":" + sequenceName;
        var collection = CurrentDatabase.GetCollection<BsonDocument>(SequenceCollectionName);
        var filter = Builders<BsonDocument>.Filter.Eq("_id", key);
        var update = Builders<BsonDocument>.Update.Inc(SequenceValueFieldName, 1L);
        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After,
        };

        // upsert 首插并发竞态会抛 DuplicateKey(E11000)：只对该类别做有界本地重试（退避 20/50ms），
        // 重试耗尽后上抛；不套 ExecuteWriteWithRetryAsync 以避免双层重试。
        // Concurrent upsert first-insert races throw DuplicateKey (E11000): retry only that
        // category locally with a bounded loop (20/50ms backoff) and rethrow once exhausted;
        // ExecuteWriteWithRetryAsync is intentionally not used to avoid double-layer retries.
        var retryDelays = new[] { 20, 50 };
        for (var attempt = 0; attempt <= SequenceDuplicateKeyRetryTotal; attempt++)
        {
            try
            {
                var document = await FindOneAndUpdateWithAmbientSessionAsync(collection, filter, update, options, cancellationToken).ConfigureAwait(false);
                return document[SequenceValueFieldName].ToInt64();
            }
            catch (MongoWriteException we) when (we.WriteError?.Category == ServerErrorCategory.DuplicateKey && attempt < SequenceDuplicateKeyRetryTotal)
            {
                if (attempt < retryDelays.Length)
                {
                    await Task.Delay(retryDelays[attempt], cancellationToken).ConfigureAwait(false);
                }
            }
        }

        // 循环上界只是编译器要求的显式出口：成功即 return，重试耗尽后最后一次 DuplicateKey
        // 因 catch 过滤器不匹配直接外抛，正常路径不可能落到这里。
        // The loop bound exists only as the compiler-required exit: success returns, and the
        // final DuplicateKey after exhaustion propagates via the catch filter miss, so this
        // endpoint is unreachable on any real path.
        throw new UnreachableException();
    }

    #endregion 条件写
}
