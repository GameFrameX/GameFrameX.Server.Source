// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//   This project is dual-licensed under the MIT License and Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等违法行为！
//   or infringe upon the legitimate rights and interest of others, as prohibited by laws and regulations!
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

using System.Collections.Concurrent;
using GameFrameX.Foundation.Logger;

namespace GameFrameX.Core.Idempotency;

/// <summary>
/// 全局业务级幂等管理器。以 PlayerActorId + RequestId 为复合键进行 per-player 去重。
/// 区分 RPC（请求-响应，缓存结果 + TCS 等待）和非 RPC（tell 模式，仅标记已处理）。
/// </summary>
public sealed class IdempotencyManager
{
    private static readonly Lazy<IdempotencyManager> _instance = new(() => new IdempotencyManager());
    public static IdempotencyManager Instance => _instance.Value;

    private readonly IIdempotencyStorage _storage;
    private readonly ConcurrentDictionary<(long, string), TaskCompletionSource<IdempotencyRecord>> _pendingRequests = new();
    private readonly IdempotencyOptions _options;

    public IdempotencyManager() : this(new InMemoryIdempotencyStorage(), new IdempotencyOptions()) { }

    public IdempotencyManager(IIdempotencyStorage storage, IdempotencyOptions options)
    {
        _storage = storage;
        _options = options;
    }

    /// <summary>
    /// 检查非 RPC 请求是否已处理。若未处理则标记为处理中，由调用方在完成后调用 MarkCompleted。
    /// </summary>
    /// <returns>true 表示重复请求（应跳过），false 表示首次请求（应执行业务逻辑）</returns>
    public bool CheckOrMarkNonRpc(long playerActorId, string requestId, int ttlSeconds = 0)
    {
        var ttl = ttlSeconds > 0 ? ttlSeconds : _options.DefaultTtlSeconds;

        if (_storage.TryGetOrMark(playerActorId, requestId, out var existing))
        {
            return true;
        }

        var record = new IdempotencyRecord
        {
            IsRpc = false,
            ExpiresAt = DateTime.UtcNow.AddSeconds(ttl),
        };
        _storage.Set(playerActorId, requestId, record);
        return false;
    }

    /// <summary>
    /// 标记非 RPC 请求已完成
    /// </summary>
    public void MarkCompleted(long playerActorId, string requestId)
    {
        if (_storage.TryGet(playerActorId, requestId, out var record))
        {
            record.IsCompleted = true;
        }
    }

    /// <summary>
    /// 检查 RPC 请求的幂等状态。若已处理则返回缓存结果；若处理中则等待；若首次则标记并返回 IsHit=false。
    /// </summary>
    public async Task<IdempotencyCheckResult> CheckOrWaitRpc(long playerActorId, string requestId, int ttlSeconds = 0, int timeoutMs = 30000)
    {
        var ttl = ttlSeconds > 0 ? ttlSeconds : _options.DefaultTtlSeconds;

        if (_storage.TryGet(playerActorId, requestId, out var existing) && existing.IsCompleted)
        {
            return new IdempotencyCheckResult { IsHit = true, Record = existing };
        }

        var tcs = new TaskCompletionSource<IdempotencyRecord>();
        if (_pendingRequests.TryAdd((playerActorId, requestId), tcs))
        {
            var record = new IdempotencyRecord
            {
                IsRpc = true,
                ExpiresAt = DateTime.UtcNow.AddSeconds(ttl),
            };
            _storage.Set(playerActorId, requestId, record);
            return new IdempotencyCheckResult { IsHit = false, Record = null };
        }

        if (_pendingRequests.TryGetValue((playerActorId, requestId), out var existingTcs))
        {
            try
            {
                var completed = await existingTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
                return new IdempotencyCheckResult { IsHit = true, Record = completed };
            }
            catch (TimeoutException)
            {
                LogHelper.Warning("IdempotencyManager.CheckOrWaitRpc, Wait for in-flight request timed out, playerActorId: {playerActorId}, requestId: {requestId}", playerActorId, requestId);
                return new IdempotencyCheckResult { IsHit = false, Record = null };
            }
        }

        return new IdempotencyCheckResult { IsHit = false, Record = null };
    }

    /// <summary>
    /// 缓存 RPC 请求的成功结果并通知等待者
    /// </summary>
    public void SetRpcResult(long playerActorId, string requestId, byte[] responseData)
    {
        if (_storage.TryGet(playerActorId, requestId, out var record))
        {
            record.ResponseData = responseData;
            record.IsCompleted = true;
        }

        if (_pendingRequests.TryRemove((playerActorId, requestId), out var tcs))
        {
            tcs.TrySetResult(record);
        }
    }

    /// <summary>
    /// 缓存 RPC 请求的异常结果（根据 CachePolicy 决定是否缓存）并通知等待者
    /// </summary>
    public void SetRpcError(long playerActorId, string requestId, string exceptionMessage, IdempotentCachePolicy cachePolicy)
    {
        if (cachePolicy == IdempotentCachePolicy.AllOutcomes)
        {
            if (_storage.TryGet(playerActorId, requestId, out var record))
            {
                record.ExceptionMessage = exceptionMessage;
                record.IsCompleted = true;
            }
        }
        else
        {
            _storage.Remove(playerActorId, requestId);
        }

        if (_pendingRequests.TryRemove((playerActorId, requestId), out var tcs))
        {
            tcs.TrySetResult(cachePolicy == IdempotentCachePolicy.AllOutcomes
                ? _storage.TryGet(playerActorId, requestId, out var r) ? r : null
                : null);
        }
    }

    /// <summary>
    /// 清理所有过期的幂等记录。由全局清理定时器调用。
    /// </summary>
    public int CleanupExpired()
    {
        return _storage.CleanupExpired();
    }
}

/// <summary>
/// 幂等检查结果
/// </summary>
public sealed class IdempotencyCheckResult
{
    /// <summary>
    /// 是否命中缓存（true 表示应返回缓存结果，false 表示首次请求应执行业务逻辑）
    /// </summary>
    public bool IsHit { get; init; }

    /// <summary>
    /// 缓存的幂等记录（IsHit=true 时有值）
    /// </summary>
    public IdempotencyRecord Record { get; init; }
}
