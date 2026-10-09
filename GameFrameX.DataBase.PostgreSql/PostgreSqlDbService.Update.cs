// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or violate the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Reflection;
using GameFrameX.DataBase.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GameFrameX.DataBase.PostgreSql;

public sealed partial class PostgreSqlDbService
{
    /// <summary>
    /// 保存数据（仅当 StateHash 判定已修改才写库；整文档重写，新档为准，对齐 C168 语义裁定）。
    /// </summary>
    /// <remarks>
    /// Saves a document (only writes when the StateHash marks it modified). C168 semantics: the whole document
    /// is rewritten from the state object — fields the state type no longer declares are naturally cleared
    /// (unlike C166's stored-side merge); the state's own <c>IsDeleted</c>/<c>CreatedTime</c> etc. are
    /// authoritative since update flows load states from the database first. A missing row is a silent no-op
    /// (aligned with the previous <c>UPDATE ... WHERE id</c> row-count behavior).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="state">要保存的数据 / The state to save</param>
    /// <returns>保存后的数据 / The saved state</returns>
    public async Task<TState> UpdateAsync<TState>(TState state) where TState : BaseCacheState, new()
    {
        return await UpdateAsync(state, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 保存数据（仅当 StateHash 判定已修改才写库；整文档重写，新档为准，对齐 C168 语义裁定）。
    /// </summary>
    /// <remarks>
    /// Saves a document (only writes when the StateHash marks it modified). C168 semantics: the whole document
    /// is rewritten from the state object — fields the state type no longer declares are naturally cleared
    /// (unlike C166's stored-side merge); the state's own <c>IsDeleted</c>/<c>CreatedTime</c> etc. are
    /// authoritative since update flows load states from the database first. A missing row is a silent no-op
    /// (aligned with the previous <c>UPDATE ... WHERE id</c> row-count behavior).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="state">要保存的数据 / The state to save</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>保存后的数据 / The saved state</returns>
    public async Task<TState> UpdateAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var isChanged = state.IsModify();
        if (isChanged)
        {
            state.UpdateTime = GetCurrentTimestamp();
            state.UpdateCount = (state.UpdateCount ?? 0) + 1;

            var updated = await ExecuteWriteWithRetryAsync(async token =>
            {
                await EnsureTableAsync<TState>(token).ConfigureAwait(false);
                using var context = CreateContext<TState>();
                var updatedRow = await ReplaceDocumentAsync(context, state, token).ConfigureAwait(false);
                if (updatedRow)
                {
                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                }

                return updatedRow;
            }, cancellationToken, nameof(UpdateAsync), true).ConfigureAwait(false);
            if (updated)
            {
                state.SaveToDbPostHandler();
            }
        }

        return state;
    }

    /// <summary>
    /// 保存多条数据（返回实际变更数；未变更项跳过，对齐 Mongo BulkWrite 语义）。
    /// </summary>
    /// <remarks>
    /// Saves multiple documents (returns the actually-changed count; unchanged items are skipped, aligned with
    /// Mongo BulkWrite semantics). All writes go through one <c>SaveChanges</c> (atomic batch); when at least
    /// one row was written, every item receives <c>SaveToDbPostHandler</c> (aligned with the previous flow).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="stateList">要保存的数据列表 / The states to save</param>
    /// <returns>实际更新的数量 / The number of actually updated documents</returns>
    public async Task<long> UpdateAsync<TState>(IEnumerable<TState> stateList) where TState : BaseCacheState, new()
    {
        return await UpdateAsync(stateList, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 保存多条数据（返回实际变更数；未变更项跳过，对齐 Mongo BulkWrite 语义）。
    /// </summary>
    /// <remarks>
    /// Saves multiple documents (returns the actually-changed count; unchanged items are skipped, aligned with
    /// Mongo BulkWrite semantics). All writes go through one <c>SaveChanges</c> (atomic batch); when at least
    /// one row was written, every item receives <c>SaveToDbPostHandler</c> (aligned with the previous flow).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="stateList">要保存的数据列表 / The states to save</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际更新的数量 / The number of actually updated documents</returns>
    public async Task<long> UpdateAsync<TState>(IEnumerable<TState> stateList, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var cacheStates = stateList as TState[] ?? stateList?.ToArray();
        if (cacheStates == null || cacheStates.Length == 0)
        {
            return 0;
        }

        var changedStates = new List<TState>(cacheStates.Length);
        var currentTime = GetCurrentTimestamp();
        foreach (var state in cacheStates)
        {
            if (state.IsModify())
            {
                state.UpdateTime = currentTime;
                state.UpdateCount = (state.UpdateCount ?? 0) + 1;
                changedStates.Add(state);
            }
        }

        if (changedStates.Count == 0)
        {
            return 0;
        }

        var anyRowWritten = await ExecuteWriteWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            using var context = CreateContext<TState>();
            var written = false;
            foreach (var state in changedStates)
            {
                written |= await ReplaceDocumentAsync(context, state, token).ConfigureAwait(false);
            }

            if (written)
            {
                await context.SaveChangesAsync(token).ConfigureAwait(false);
            }

            return written;
        }, cancellationToken, nameof(UpdateAsync), true).ConfigureAwait(false);
        if (anyRowWritten)
        {
            foreach (var state in cacheStates)
            {
                state.SaveToDbPostHandler();
            }
        }

        return changedStates.Count;
    }

    /// <summary>
    /// 根据ID部分更新数据（读改写整档：字段写入 / null 移除 + UpdateTime 写入 + UpdateCount 自增）。
    /// </summary>
    /// <remarks>
    /// Partially updates by ID (load-modify-save whole document: non-null fields written, null-valued fields
    /// removed/zeroed; <c>UpdateTime</c> stamped and <c>UpdateCount</c> incremented). 对齐 Mongo 语义：
    /// <c>Id/CreatedTime/CreatedId</c> 不可更新；过滤附带软删默认过滤（仅可见行可更新）；
    /// 返回值对齐 ModifiedCount——命中可见行且字段非空即写库并返回 1，行不可见（软删过滤）或
    /// 字段空返回 0。UpdateTime/UpdateCount 恒推进使「值未变」不可达，与 C166 的 IS DISTINCT FROM
    /// 比较档内嵌新时间戳/计数、Mongo 的 $set UpdateTime + $inc UpdateCount 行为三方一致。
    /// 值类型字段的「移除」落地为 CLR 默认值（JSON 中的 <c>0/false</c> 与缺失键读取等价）。
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="updateFields">要更新的字段字典 / The fields to update</param>
    /// <returns>实际更新的行数 / The number of actually updated rows</returns>
    public async Task<long> UpdatePartialAsync<TState>(long id, IReadOnlyDictionary<string, object> updateFields) where TState : BaseCacheState, new()
    {
        return await UpdatePartialAsync<TState>(id, updateFields, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据ID部分更新数据（读改写整档：字段写入 / null 移除 + UpdateTime 写入 + UpdateCount 自增）。
    /// </summary>
    /// <remarks>
    /// Partially updates by ID (load-modify-save whole document: non-null fields written, null-valued fields
    /// removed/zeroed; <c>UpdateTime</c> stamped and <c>UpdateCount</c> incremented). 对齐 Mongo 语义：
    /// <c>Id/CreatedTime/CreatedId</c> 不可更新；过滤附带软删默认过滤（仅可见行可更新）；
    /// 返回值对齐 ModifiedCount——命中可见行且字段非空即写库并返回 1，行不可见（软删过滤）或
    /// 字段空返回 0。UpdateTime/UpdateCount 恒推进使「值未变」不可达，与 C166 的 IS DISTINCT FROM
    /// 比较档内嵌新时间戳/计数、Mongo 的 $set UpdateTime + $inc UpdateCount 行为三方一致。
    /// 值类型字段的「移除」落地为 CLR 默认值（JSON 中的 <c>0/false</c> 与缺失键读取等价）。
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="updateFields">要更新的字段字典 / The fields to update</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际更新的行数 / The number of actually updated rows</returns>
    public async Task<long> UpdatePartialAsync<TState>(long id, IReadOnlyDictionary<string, object> updateFields, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        if (updateFields == null || updateFields.Count == 0)
        {
            return 0;
        }

        var excludedKeys = new HashSet<string>
        {
            nameof(BaseCacheState.Id),
            nameof(BaseCacheState.CreatedTime),
            nameof(BaseCacheState.CreatedId),
        };
        var setFields = new Dictionary<string, object>(StringComparer.Ordinal);
        var removedKeys = new List<string>();
        foreach (var item in updateFields)
        {
            if (string.IsNullOrWhiteSpace(item.Key) || excludedKeys.Contains(item.Key))
            {
                continue;
            }

            if (item.Value == null)
            {
                removedKeys.Add(item.Key);
                continue;
            }

            setFields[item.Key] = item.Value;
        }

        if (setFields.Count == 0 && removedKeys.Count == 0)
        {
            return 0;
        }

        return await ExecuteWriteWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            using var context = CreateContext<TState>();
            // 软删默认过滤生效：仅可见行可被部分更新（对齐原 WHERE 软删过滤条款）
            // The soft-delete query filter applies: only visible rows can be partially updated.
            var row = await context.Rows.FirstOrDefaultAsync(row => row.Id == id, token).ConfigureAwait(false);
            if (row == null)
            {
                return 0;
            }

            var document = row.Doc;
            var stateType = typeof(TState);
            foreach (var item in setFields)
            {
                ApplyFieldValue(stateType, document, item.Key, item.Value);
            }

            foreach (var removedKey in removedKeys)
            {
                ApplyFieldValue(stateType, document, removedKey, null);
            }

            // UpdateTime/UpdateCount 恒推进：命中可见行且字段非空即视为修改（C170 移除值未变比较探针——
            // 其 before/after 两侧必然不同，为不可达死代码；基线行为见方法注释）。
            // UpdateTime/UpdateCount always advance: a visible row with non-empty fields always counts as
            // modified (C170 removed the value-unchanged probe — its before/after captures could never
            // compare equal; baseline behavior documented on the method).
            document.UpdateTime = GetCurrentTimestamp();
            document.UpdateCount = (document.UpdateCount ?? 0) + 1;
            await context.SaveChangesAsync(token).ConfigureAwait(false);
            return 1;
        }, cancellationToken, nameof(UpdatePartialAsync), true).ConfigureAwait(false);
    }

    /// <summary>
    /// 反射写入文档字段值（null → 引用/可空置 null，值类型置 CLR 默认值；类型不匹配时按 IConvertible 转换）。
    /// </summary>
    /// <remarks>
    /// Reflectively assigns a document field (null assigns null to reference/nullable members and the CLR
    /// default to value types; mismatched runtime types convert via <see cref="IConvertible"/>). Unknown
    /// property names are ignored (defensive against a different version's field sets).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="stateType">状态类型 / The state type</param>
    /// <param name="document">文档实例 / The document instance</param>
    /// <param name="fieldName">字段名 / The field name</param>
    /// <param name="value">字段值 / The field value</param>
    private static void ApplyFieldValue<TState>(Type stateType, TState document, string fieldName, object value) where TState : BaseCacheState, new()
    {
        var property = stateType.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (property == null || !property.CanWrite)
        {
            return;
        }

        var propertyType = property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (value == null)
        {
            property.SetValue(document, propertyType.IsValueType && Nullable.GetUnderlyingType(propertyType) == null ? Activator.CreateInstance(propertyType) : null);
            return;
        }

        var valueType = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        if (valueType != underlyingType)
        {
            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(underlyingType))
            {
                value = Convert.ChangeType(value, underlyingType, System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                return;
            }
        }

        property.SetValue(document, value);
    }

    /// <summary>
    /// 以整文档替换方式写回状态对象（加载跟踪行后替换 Doc 引用；行缺失返回 false）。
    /// </summary>
    /// <remarks>
    /// Writes the state back as a whole-document replacement: the tracked row is loaded (bypassing the
    /// soft-delete filter, matching the former unfiltered <c>UPDATE ... WHERE id</c> shape) and its <c>Doc</c>
    /// reference replaced. The caller owns <c>SaveChangesAsync</c> so multiple replacements can share one
    /// atomic save. Returns false when the row does not exist (no write, no exception).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="context">EF 上下文 / The EF context</param>
    /// <param name="state">要写回的状态对象 / The state to write back</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>行存在并已标记写回时为 true；行缺失为 false / True when the row exists and the replacement is staged; false when the row is missing</returns>
    private static async Task<bool> ReplaceDocumentAsync<TState>(PostgreSqlDbContext<TState> context, TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        var row = await context.Rows.IgnoreQueryFilters().FirstOrDefaultAsync(row => row.Id == state.Id, cancellationToken).ConfigureAwait(false);
        if (row == null)
        {
            return false;
        }

        row.Doc = state;
        return true;
    }
}
