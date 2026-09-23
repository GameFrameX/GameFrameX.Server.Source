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
//   Gitee Repository: https://github.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Storage;

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Scope;

/// <summary>
/// 玩家云存储服务（vault:C3 S2.6：玩家 + App 作用域隔离的 KV 读写/分页/软删/过期清理）。
/// <para>
/// 维护约束（红线）：作用域 = (TenantId, AppId, PlayerId)——区服不参与隔离（换服数据随身，R3），
/// 跨作用域读写在存储键层结构性不可达，查无记录一律 <see cref="OnlineErrorCode.ResourceNotFound"/>
/// （同构错误，防存在性探测，VC-2.7/2.8/2.9）；写入走存储层 CAS 乐观锁——expectedVersion=0 为创建、
/// &gt;0 为版本匹配更新，冲突映射 <see cref="OnlineErrorCode.VersionConflict"/>（VC-2.10）；
/// 单值/单集合键数/分页上限超限拒绝（VC-2.11）；删除为软删；过期条目由
/// <see cref="SweepExpiredAsync"/> 软删（读取即时不可见）。
/// </para>
/// </summary>
public sealed class OnlinePlayerStorageService
{
    /// <summary>玩家云存储。</summary>
    private readonly IOnlinePlayerStorageStore _store;

    /// <summary>限制选项。</summary>
    private readonly OnlinePlayerStorageOptions _options;

    /// <summary>
    /// 初始化 <see cref="OnlinePlayerStorageService"/>。
    /// </summary>
    /// <param name="store">玩家云存储。</param>
    /// <param name="options">限制选项（可空 = 默认限制）。</param>
    public OnlinePlayerStorageService(IOnlinePlayerStorageStore store, OnlinePlayerStorageOptions options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? new OnlinePlayerStorageOptions();
    }

    /// <summary>
    /// 读取条目（软删/过期等同不存在——跨作用域同构 ResourceNotFound，防探测）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="collection">集合名。</param>
    /// <param name="key">条目键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>条目。</returns>
    public async Task<OnlineResult<OnlinePlayerStorageEntry>> ReadAsync(OnlineScope scope, string collection, string key, CancellationToken cancellationToken = default)
    {
        var failure = ValidateTarget(scope, collection, key);
        if (failure != null)
        {
            return failure;
        }

        var entry = await _store.FindAsync(new OnlineStorageEntryKey { TenantId = scope.TenantId, AppId = scope.AppId, PlayerId = scope.PlayerId, Collection = collection, Key = key }, cancellationToken);
        if (entry == null || entry.DeletedAtTime > 0 || IsExpired(entry))
        {
            return OnlineResult<OnlinePlayerStorageEntry>.Fail(OnlineErrorCode.ResourceNotFound, "存储条目不存在");
        }

        return OnlineResult<OnlinePlayerStorageEntry>.Ok(entry);
    }

    /// <summary>
    /// 写入条目（expectedVersion=0 创建 / &gt;0 版本匹配更新；冲突映射 VersionConflict）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="collection">集合名。</param>
    /// <param name="key">条目键。</param>
    /// <param name="payload">负载数据。</param>
    /// <param name="expectedVersion">期望版本（0 = 创建；&gt;0 = 须匹配当前版本）。</param>
    /// <param name="operatorId">操作者（会话标识；审计留痕）。</param>
    /// <param name="expiresAtTime">过期时刻（Unix 毫秒；0 = 永不过期）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写入后的条目（含新版本）。</returns>
    public async Task<OnlineResult<OnlinePlayerStorageEntry>> WriteAsync(OnlineScope scope, string collection, string key, byte[] payload, long expectedVersion, string operatorId, long expiresAtTime = 0, CancellationToken cancellationToken = default)
    {
        var failure = ValidateTarget(scope, collection, key);
        if (failure != null)
        {
            return failure;
        }

        if (payload == null)
        {
            return OnlineResult<OnlinePlayerStorageEntry>.Fail(OnlineErrorCode.ParameterInvalid, "负载数据不得为空引用");
        }

        if (payload.Length > _options.MaxPayloadBytes)
        {
            return OnlineResult<OnlinePlayerStorageEntry>.Fail(OnlineErrorCode.ParameterInvalid, "负载超出单值上限（" + _options.MaxPayloadBytes + " 字节）");
        }

        var now = Now();
        var existing = await _store.FindAsync(new OnlineStorageEntryKey { TenantId = scope.TenantId, AppId = scope.AppId, PlayerId = scope.PlayerId, Collection = collection, Key = key }, cancellationToken);
        var casVersion = expectedVersion;
        var isResurrecting = false;
        if (existing != null && existing.DeletedAtTime > 0 && expectedVersion == 0)
        {
            // 软删条目复活：CAS 按其末版接管，条目版本按创建语义重新从 1 计。
            casVersion = existing.Version;
            isResurrecting = true;
        }

        if (expectedVersion == 0)
        {
            var activeKeys = await _store.CountActiveKeysAsync(scope.TenantId, scope.AppId, scope.PlayerId, collection, cancellationToken);
            if (activeKeys >= _options.MaxKeysPerCollection)
            {
                return OnlineResult<OnlinePlayerStorageEntry>.Fail(OnlineErrorCode.StateOperationForbidden, "单集合键数已达上限（" + _options.MaxKeysPerCollection + "）");
            }
        }
        else if (existing == null)
        {
            return OnlineResult<OnlinePlayerStorageEntry>.Fail(OnlineErrorCode.VersionConflict, "条目不存在，不能按指定版本更新（创建须传 expectedVersion=0）");
        }

        var keepCreated = !isResurrecting && existing != null && existing.DeletedAtTime == 0;
        var entry = new OnlinePlayerStorageEntry
        {
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            PlayerId = scope.PlayerId,
            Collection = collection,
            Key = key,
            Payload = payload,
            Version = expectedVersion + 1,
            CreatedAtTime = keepCreated ? existing.CreatedAtTime : now,
            CreatedBy = keepCreated ? existing.CreatedBy : operatorId ?? string.Empty,
            UpdatedAtTime = now,
            UpdatedBy = operatorId ?? string.Empty,
            ExpiresAtTime = expiresAtTime,
            DeletedAtTime = 0,
        };
        var swapped = await _store.UpsertAsync(entry, casVersion, cancellationToken);
        if (!swapped)
        {
            return OnlineResult<OnlinePlayerStorageEntry>.Fail(OnlineErrorCode.VersionConflict, "版本冲突，请读取最新版本后重试");
        }

        return OnlineResult<OnlinePlayerStorageEntry>.Ok(entry);
    }

    /// <summary>
    /// 列举集合条目（键字典序游标分页；软删/过期条目不可见）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="collection">集合名。</param>
    /// <param name="cursor">翻页游标（上一页末条键；空 = 从头列举）。</param>
    /// <param name="pageSize">请求页大小（钳制到 [1, MaxPageSize]；0 = 默认上限）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public async Task<OnlineResult<OnlineStoragePage>> ListAsync(OnlineScope scope, string collection, string cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var failure = ValidateCollectionOnly(scope, collection);
        if (failure != null)
        {
            return OnlineResult<OnlineStoragePage>.Fail(failure.Code, failure.Message);
        }

        if (pageSize < 0)
        {
            return OnlineResult<OnlineStoragePage>.Fail(OnlineErrorCode.ParameterInvalid, "页大小不得为负数");
        }

        var effectivePageSize = pageSize == 0 ? _options.MaxPageSize : Math.Min(pageSize, _options.MaxPageSize);
        var fetch = await _store.ListAsync(new OnlineStorageListQuery { TenantId = scope.TenantId, AppId = scope.AppId, PlayerId = scope.PlayerId, Collection = collection, AfterKey = cursor ?? string.Empty, MaxCount = effectivePageSize + 1 }, cancellationToken);
        var hasMore = fetch.Count > effectivePageSize;
        var pageEntries = hasMore ? fetch.Take(effectivePageSize).ToList() : fetch.ToList();
        var now = Now();
        for (var index = pageEntries.Count - 1; index >= 0; index--)
        {
            if (IsExpired(pageEntries[index], now))
            {
                pageEntries.RemoveAt(index);
            }
        }

        var lastKey = pageEntries.Count > 0 ? pageEntries[pageEntries.Count - 1].Key : null;
        return OnlineResult<OnlineStoragePage>.Ok(new OnlineStoragePage
        {
            Entries = pageEntries,
            Cursor = hasMore && lastKey != null ? lastKey : string.Empty,
            HasMore = hasMore,
        });
    }

    /// <summary>
    /// 软删条目（读取与列举即刻不可见；物理保留）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="collection">集合名。</param>
    /// <param name="key">条目键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<OnlineResult<bool>> DeleteAsync(OnlineScope scope, string collection, string key, CancellationToken cancellationToken = default)
    {
        var failure = ValidateTarget(scope, collection, key);
        if (failure != null)
        {
            return OnlineResult<bool>.Fail(failure.Code, failure.Message);
        }

        var entry = await _store.FindAsync(new OnlineStorageEntryKey { TenantId = scope.TenantId, AppId = scope.AppId, PlayerId = scope.PlayerId, Collection = collection, Key = key }, cancellationToken);
        if (entry == null || entry.DeletedAtTime > 0 || IsExpired(entry))
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ResourceNotFound, "存储条目不存在");
        }

        entry.DeletedAtTime = Now();
        entry.Version++;
        entry.UpdatedAtTime = entry.DeletedAtTime;
        var swapped = await _store.UpsertAsync(entry, entry.Version - 1, cancellationToken);
        if (!swapped)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.VersionConflict, "版本冲突，条目已被并发修改");
        }

        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 清理过期条目（ExpiresAtTime 已过且未软删的条目批量软删）。
    /// </summary>
    /// <param name="nowUnixMilliseconds">判定基准时刻（Unix 毫秒；0 = 当前时刻，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次清理的条目数。</returns>
    public async Task<int> SweepExpiredAsync(long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : Now();
        var swept = 0;
        var all = await _store.ListAllAsync(cancellationToken);
        foreach (var entry in all)
        {
            if (entry.DeletedAtTime == 0 && IsExpired(entry, now))
            {
                entry.DeletedAtTime = now;
                entry.Version++;
                entry.UpdatedAtTime = now;
                if (await _store.UpsertAsync(entry, entry.Version - 1, cancellationToken))
                {
                    swept++;
                }
            }
        }

        return swept;
    }

    /// <summary>
    /// 判断条目是否已过期。
    /// </summary>
    /// <param name="entry">目标条目。</param>
    /// <param name="nowUnixMilliseconds">判定基准时刻（0 = 当前时刻）。</param>
    /// <returns>已过期返回 true。</returns>
    private static bool IsExpired(OnlinePlayerStorageEntry entry, long nowUnixMilliseconds = 0)
    {
        if (entry.ExpiresAtTime <= 0)
        {
            return false;
        }

        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : Now();
        return now > entry.ExpiresAtTime;
    }

    /// <summary>
    /// 校验作用域与目标标识（集合名 + 键）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="collection">集合名。</param>
    /// <param name="key">条目键。</param>
    /// <returns>失败结果；合法返回 null。</returns>
    private static OnlineResult<OnlinePlayerStorageEntry> ValidateTarget(OnlineScope scope, string collection, string key)
    {
        var collectionFailure = ValidateCollectionOnly(scope, collection);
        if (collectionFailure != null)
        {
            return collectionFailure;
        }

        if (!OnlinePlayerStorageOptions.IsValidIdentifier(key))
        {
            return OnlineResult<OnlinePlayerStorageEntry>.Fail(OnlineErrorCode.ParameterInvalid, "条目键格式非法（仅允许字母/数字/下划线/连字符，长度 1-64）");
        }

        return null;
    }

    /// <summary>
    /// 校验作用域与集合名。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="collection">集合名。</param>
    /// <returns>失败结果；合法返回 null。</returns>
    private static OnlineResult<OnlinePlayerStorageEntry> ValidateCollectionOnly(OnlineScope scope, string collection)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (scope.PlayerId <= 0)
        {
            return OnlineResult<OnlinePlayerStorageEntry>.Fail(OnlineErrorCode.ScopeMissing, "玩家云存储必须携带玩家主体位（PlayerId）");
        }

        if (!OnlinePlayerStorageOptions.IsValidIdentifier(collection))
        {
            return OnlineResult<OnlinePlayerStorageEntry>.Fail(OnlineErrorCode.ParameterInvalid, "集合名格式非法（仅允许字母/数字/下划线/连字符，长度 1-64）");
        }

        return null;
    }

    /// <summary>
    /// 获取当前时刻（Unix 毫秒）。
    /// </summary>
    /// <returns>当前时刻。</returns>
    private static long Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
