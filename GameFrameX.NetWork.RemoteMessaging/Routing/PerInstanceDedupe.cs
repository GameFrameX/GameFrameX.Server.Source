// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//  ==========================================================================================


using System.Collections.Concurrent;

namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 跨服投递单飞去重（C143e D21：per-instance + 时间窗）。
/// </summary>
/// <remarks>
/// Per-instance single-flight deduplication for cross-server deliveries
/// (C143e D21). Two cross-server sends to the same <c>targetInstanceId</c>
/// within the same time window collapse into one — the second call returns
/// <c>false</c> and the caller treats it as already-in-flight. A retry against
/// a different instance clears the key (different instance = different slot),
/// so failover to a new instance is never collapsed. The window is short
/// (default 1s) because the actual completion signal is the target server's
/// response; the window just bounds the race between an outstanding send and
/// an immediate retry triggered by a transient connection error.
/// ponytail: 时间窗选 1s 是基于 RetrySemantics 的指数退避起点（500ms→1s→2s）——
/// 单飞窗口 ≥ 最长一次端到端往返，避免重投与首投同时在途。配置化留给后续 change。
/// </remarks>
public sealed class PerInstanceDedupe
{
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, long> _lastSeenTicks = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);

    /// <summary>
    /// 初始化单飞去重器（1s 时间窗）。
    /// </summary>
    /// <remarks>
    /// Initializes the dedupe with the default 1s window.
    /// </remarks>
    public PerInstanceDedupe()
        : this(TimeSpan.FromSeconds(1))
    {
    }

    /// <summary>
    /// 初始化单飞去重器（自定义时间窗）。
    /// </summary>
    /// <remarks>
    /// Initializes the dedupe with a custom window (testing seam).
    /// </remarks>
    /// <param name="window">单飞时间窗 / The single-flight window</param>
    public PerInstanceDedupe(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Window must be positive.");
        }

        _window = window;
    }

    /// <summary>
    /// 标记一次 (instanceId, correlationKey) 投递尝试；若同 instanceId 在时间窗内已存在同 correlationKey，则返回 false。
    /// </summary>
    /// <remarks>
    /// Marks a delivery attempt. Returns <c>false</c> when a prior attempt for
    /// the same instance id and correlation key is still inside the window —
    /// the caller treats <c>false</c> as "already in flight, skip retry". The
    /// correlation key default of <c>0</c> collapses all sends against the
    /// same instance id within the window (good enough for cross-server
    /// player messages because the source envelope is unique per send).
    /// </remarks>
    /// <param name="instanceId">目标实例 ID / Target instance id</param>
    /// <param name="correlationKey">可选的关联键（默认 0）/ Optional correlation key (defaults to 0)</param>
    /// <returns>true=本次是新投递；false=窗口内已有同 instanceId 投递 / true=new send, false=already in flight</returns>
    public bool TryAcquire(string instanceId, long correlationKey = 0)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            return true;
        }

        var slot = $"{instanceId}|{correlationKey}";
        var now = Environment.TickCount64;
        var previousTicks = _lastSeenTicks.AddOrUpdate(slot, now, (_, previous) => now - previous <= _window.Ticks ? previous : now);
        return now - previousTicks >= _window.Ticks;
    }

    /// <summary>
    /// 清空某实例的单飞记录（切流到新实例后调用）。
    /// </summary>
    /// <remarks>
    /// Clears the dedupe slot for an instance id (used after the sender
    /// successfully fails over to a different instance — the old slot is no
    /// longer relevant).
    /// </remarks>
    /// <param name="instanceId">目标实例 ID / Target instance id</param>
    public void Release(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            return;
        }

        foreach (var pair in _lastSeenTicks)
        {
            if (pair.Key.StartsWith(instanceId + "|", StringComparison.Ordinal))
            {
                _lastSeenTicks.TryRemove(pair.Key, out _);
            }
        }
    }
}
