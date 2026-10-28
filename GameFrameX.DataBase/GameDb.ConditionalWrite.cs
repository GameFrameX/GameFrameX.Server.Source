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
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.DataBase.Abstractions;

namespace GameFrameX.DataBase;

/// <summary>
/// 游戏数据库静态工具类,提供条件写原语（CAS / 唯一插入 / 序列）的门面转发。
/// </summary>
/// <remarks>
/// Static utility class for game database operations: facade forwarding for the conditional-write
/// primitives (CAS / unique insert / sequence). Concurrency semantics live on
/// <see cref="IDatabaseService"/> member documentation.
/// </remarks>
public static partial class GameDb
{
    /// <summary>
    /// 条件写:仅当存量文档的行版本号与期望值一致时整档写入（CAS / 乐观锁原语）。
    /// </summary>
    /// <remarks>
    /// Conditional write: replaces the whole document only when the stored <c>RowVersion</c> equals
    /// <paramref name="expectedRowVersion"/> (compare-and-swap / optimistic-lock primitive). See
    /// <see cref="IDatabaseService.UpdateIfRowVersionAsync{TState}(long, long, TState)"/> for the full
    /// concurrency contract.
    /// </remarks>
    /// <typeparam name="TState">文档的类型,必须继承自BaseCacheState / Document type, must inherit from BaseCacheState</typeparam>
    /// <param name="id">数据ID（须与 <paramref name="state"/> 的 Id 一致）/ The document ID (must equal <paramref name="state"/>'s Id)</param>
    /// <param name="expectedRowVersion">期望的存量行版本号 / The expected stored row version</param>
    /// <param name="state">要写入的新状态对象 / The new state to write</param>
    /// <returns>版本匹配并写入返回 <c>true</c>；版本不符或行缺失返回 <c>false</c> / <c>true</c> when the version matched and the document was written; <c>false</c> on mismatch or missing row</returns>
    public static Task<bool> UpdateIfRowVersionAsync<TState>(long id, long expectedRowVersion, TState state) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(_dbServiceImplementation);
        return FacadeService.UpdateIfRowVersionAsync(id, expectedRowVersion, state);
    }

    /// <summary>
    /// 条件写:仅当存量文档的行版本号与期望值一致时整档写入（CAS / 乐观锁原语）。
    /// </summary>
    /// <remarks>
    /// Conditional write: replaces the whole document only when the stored <c>RowVersion</c> equals
    /// <paramref name="expectedRowVersion"/> (compare-and-swap / optimistic-lock primitive). See
    /// <see cref="IDatabaseService.UpdateIfRowVersionAsync{TState}(long, long, TState)"/> for the full
    /// concurrency contract.
    /// </remarks>
    /// <typeparam name="TState">文档的类型,必须继承自BaseCacheState / Document type, must inherit from BaseCacheState</typeparam>
    /// <param name="id">数据ID（须与 <paramref name="state"/> 的 Id 一致）/ The document ID (must equal <paramref name="state"/>'s Id)</param>
    /// <param name="expectedRowVersion">期望的存量行版本号 / The expected stored row version</param>
    /// <param name="state">要写入的新状态对象 / The new state to write</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>版本匹配并写入返回 <c>true</c>；版本不符或行缺失返回 <c>false</c> / <c>true</c> when the version matched and the document was written; <c>false</c> on mismatch or missing row</returns>
    public static Task<bool> UpdateIfRowVersionAsync<TState>(long id, long expectedRowVersion, TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(_dbServiceImplementation);
        return FacadeService.UpdateIfRowVersionAsync(id, expectedRowVersion, state, cancellationToken);
    }

    /// <summary>
    /// 条件写:仅当存量文档缺失行版本号（字段缺位或值为 null）时整档写入（存量迁移回填原语）。
    /// </summary>
    /// <remarks>
    /// Conditional write: replaces the whole document only when the stored <c>RowVersion</c> is
    /// missing or null (legacy-row migration / backfill primitive). See
    /// <see cref="IDatabaseService.UpdateIfRowVersionIsNullAsync{TState}(long, TState)"/> for the
    /// full concurrency contract.
    /// </remarks>
    /// <typeparam name="TState">文档的类型,必须继承自BaseCacheState / Document type, must inherit from BaseCacheState</typeparam>
    /// <param name="id">数据ID（须与 <paramref name="state"/> 的 Id 一致）/ The document ID (must equal <paramref name="state"/>'s Id)</param>
    /// <param name="state">要写入的新状态对象 / The new state to write</param>
    /// <returns>存量缺失行版本且写入成功返回 <c>true</c>；存量 <c>RowVersion</c> 非 null 或行缺失返回 <c>false</c> / <c>true</c> when the stored version was missing and the document was written; <c>false</c> on a non-null stored version or a missing row</returns>
    public static Task<bool> UpdateIfRowVersionIsNullAsync<TState>(long id, TState state) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(_dbServiceImplementation);
        return FacadeService.UpdateIfRowVersionIsNullAsync(id, state);
    }

    /// <summary>
    /// 条件写:仅当存量文档缺失行版本号（字段缺位或值为 null）时整档写入（存量迁移回填原语）。
    /// </summary>
    /// <remarks>
    /// Conditional write: replaces the whole document only when the stored <c>RowVersion</c> is
    /// missing or null (legacy-row migration / backfill primitive). See
    /// <see cref="IDatabaseService.UpdateIfRowVersionIsNullAsync{TState}(long, TState, CancellationToken)"/> for the
    /// full concurrency contract.
    /// </remarks>
    /// <typeparam name="TState">文档的类型,必须继承自BaseCacheState / Document type, must inherit from BaseCacheState</typeparam>
    /// <param name="id">数据ID（须与 <paramref name="state"/> 的 Id 一致）/ The document ID (must equal <paramref name="state"/>'s Id)</param>
    /// <param name="state">要写入的新状态对象 / The new state to write</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>存量缺失行版本且写入成功返回 <c>true</c>；存量 <c>RowVersion</c> 非 null 或行缺失返回 <c>false</c> / <c>true</c> when the stored version was missing and the document was written; <c>false</c> on a non-null stored version or a missing row</returns>
    public static Task<bool> UpdateIfRowVersionIsNullAsync<TState>(long id, TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(_dbServiceImplementation);
        return FacadeService.UpdateIfRowVersionIsNullAsync(id, state, cancellationToken);
    }

    /// <summary>
    /// 唯一性插入:仅当无既有文档满足唯一性条件时插入（冲突返回 false 而非异常）。
    /// </summary>
    /// <remarks>
    /// Unique insert: inserts <paramref name="state"/> only when no existing document satisfies
    /// <paramref name="uniqueFilter"/> (a conflict returns <c>false</c> instead of throwing). See
    /// <see cref="IDatabaseService.TryAddUniqueAsync{TState}(TState, Expression{Func{TState, bool}})"/> for the
    /// full concurrency contract.
    /// </remarks>
    /// <typeparam name="TState">文档的类型,必须继承自BaseCacheState / Document type, must inherit from BaseCacheState</typeparam>
    /// <param name="state">要插入的数据对象 / The state to insert</param>
    /// <param name="uniqueFilter">唯一性判定条件（对既有文档求值）/ The uniqueness predicate (evaluated against existing documents)</param>
    /// <returns>插入成功返回 <c>true</c>；唯一键冲突或 Id 已占用返回 <c>false</c> / <c>true</c> when inserted; <c>false</c> on a unique-key or Id conflict</returns>
    public static Task<bool> TryAddUniqueAsync<TState>(TState state, Expression<Func<TState, bool>> uniqueFilter) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(_dbServiceImplementation);
        return FacadeService.TryAddUniqueAsync(state, uniqueFilter);
    }

    /// <summary>
    /// 唯一性插入:仅当无既有文档满足唯一性条件时插入（冲突返回 false 而非异常）。
    /// </summary>
    /// <remarks>
    /// Unique insert: inserts <paramref name="state"/> only when no existing document satisfies
    /// <paramref name="uniqueFilter"/> (a conflict returns <c>false</c> instead of throwing). See
    /// <see cref="IDatabaseService.TryAddUniqueAsync{TState}(TState, Expression{Func{TState, bool}})"/> for the
    /// full concurrency contract.
    /// </remarks>
    /// <typeparam name="TState">文档的类型,必须继承自BaseCacheState / Document type, must inherit from BaseCacheState</typeparam>
    /// <param name="state">要插入的数据对象 / The state to insert</param>
    /// <param name="uniqueFilter">唯一性判定条件（对既有文档求值）/ The uniqueness predicate (evaluated against existing documents)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>插入成功返回 <c>true</c>；唯一键冲突或 Id 已占用返回 <c>false</c> / <c>true</c> when inserted; <c>false</c> on a unique-key or Id conflict</returns>
    public static Task<bool> TryAddUniqueAsync<TState>(TState state, Expression<Func<TState, bool>> uniqueFilter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(_dbServiceImplementation);
        return FacadeService.TryAddUniqueAsync(state, uniqueFilter, cancellationToken);
    }

    /// <summary>
    /// 获取指定状态类型命名序列的下一个值（原子单调递增）。
    /// </summary>
    /// <remarks>
    /// Atomically increments and returns the next value of the named sequence for
    /// <typeparamref name="TState"/>. See
    /// <see cref="IDatabaseService.NextSequenceAsync{TState}(string)"/> for the full concurrency contract.
    /// </remarks>
    /// <typeparam name="TState">文档的类型,必须继承自BaseCacheState / Document type, must inherit from BaseCacheState</typeparam>
    /// <param name="sequenceName">序列名（与状态类型名联合键控）/ The sequence name (keyed together with the state type name)</param>
    /// <returns>序列的新值（首次为 1）/ The new sequence value (1 on first use)</returns>
    public static Task<long> NextSequenceAsync<TState>(string sequenceName) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(_dbServiceImplementation);
        return FacadeService.NextSequenceAsync<TState>(sequenceName);
    }

    /// <summary>
    /// 获取指定状态类型命名序列的下一个值（原子单调递增）。
    /// </summary>
    /// <remarks>
    /// Atomically increments and returns the next value of the named sequence for
    /// <typeparamref name="TState"/>. See
    /// <see cref="IDatabaseService.NextSequenceAsync{TState}(string)"/> for the full concurrency contract.
    /// </remarks>
    /// <typeparam name="TState">文档的类型,必须继承自BaseCacheState / Document type, must inherit from BaseCacheState</typeparam>
    /// <param name="sequenceName">序列名（与状态类型名联合键控）/ The sequence name (keyed together with the state type name)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>序列的新值（首次为 1）/ The new sequence value (1 on first use)</returns>
    public static Task<long> NextSequenceAsync<TState>(string sequenceName, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(_dbServiceImplementation);
        return FacadeService.NextSequenceAsync<TState>(sequenceName, cancellationToken);
    }
}
