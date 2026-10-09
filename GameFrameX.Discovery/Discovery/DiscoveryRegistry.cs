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


using GameFrameX.Foundation.Logger;
using GameFrameX.Discovery.Routing;

namespace GameFrameX.Discovery;

/// <summary>
/// 通用心跳写侧（C167：自 Mongo / PG 平行实现归一，消费 <see cref="IHeartbeatStore"/>）。
/// </summary>
/// <remarks>
/// The generic heartbeat writer (C167, unified from the Mongo / PostgreSQL
/// parallel implementations; consumes <see cref="IHeartbeatStore"/> and never
/// branches on the backend). Owns this process's row/document in the control
/// database <c>server_heartbeat</c>: an immediate upsert on start, then a
/// full upsert every heartbeat interval (5 s default). The upsert is always
/// the complete latest state, so after a database outage every field recovers
/// with the next successful write. Graceful exit writes Stopped instead of
/// waiting for the TTL: once on <see cref="StopAsync"/> and, as a best-effort
/// safety net, on <see cref="AppDomain.ProcessExit"/>. The expiry-cleanup
/// loop (default 5 s) drives <see cref="IHeartbeatStore.DeleteExpiredAsync"/>
/// — and the player-route window when an <see cref="IPlayerRouteStore"/> is
/// supplied — replacing the former PostgreSQL-only TTL cleanup job: Mongo's
/// no-op implementation keeps the server-side TTL index as the backstop.
/// </remarks>
public sealed class DiscoveryRegistry : IDisposable
{

    /// <summary>
    /// 缺省心跳间隔（5s，D11）。
    /// </summary>
    /// <remarks>
    /// The default heartbeat interval (5 s, D11).
    /// </remarks>
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// TTL 保存时长（15s，D11：watcher 三周期阈值同值兜底）。
    /// </summary>
    /// <remarks>
    /// The TTL expire-after window (15 s, D11; matches the watcher three-period threshold).
    /// </remarks>
    public static readonly TimeSpan HeartbeatTimeToLive = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 缺省过期清理周期（5s）。
    /// </summary>
    /// <remarks>
    /// The default expiry-cleanup period (5 s). Removal of an expired row is relaxed to
    /// within one cleanup period after expiry — the watcher's three-period staleness check
    /// is the primary liveness signal and never depends on row disappearance.
    /// </remarks>
    public static readonly TimeSpan DefaultTtlCleanupInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 心跳存储适配。
    /// </summary>
    /// <remarks>
    /// The heartbeat storage seam.
    /// </remarks>
    private readonly IHeartbeatStore _heartbeatStore;

    /// <summary>
    /// 玩家路由存储适配（可选；提供时清理循环同时驱动 player_route 30 天窗口）。
    /// </summary>
    /// <remarks>
    /// The optional player-route storage seam; when supplied the cleanup loop also
    /// drives the player_route 30-day window.
    /// </remarks>
    private readonly IPlayerRouteStore _playerRouteStore;

    /// <summary>
    /// 本进程实例身份（null 表示仅观察拓扑：只跑清理循环，不写心跳）。
    /// </summary>
    /// <remarks>
    /// This process's instance identity, or null when only the cleanup loop runs
    /// (no advertise port configured — single-process local development).
    /// </remarks>
    private readonly InstanceDescriptor _selfDescriptor;

    /// <summary>
    /// 心跳间隔。
    /// </summary>
    /// <remarks>
    /// The heartbeat interval.
    /// </remarks>
    private readonly TimeSpan _heartbeatInterval;

    /// <summary>
    /// 过期清理周期。
    /// </summary>
    /// <remarks>
    /// The expiry-cleanup period.
    /// </remarks>
    private readonly TimeSpan _ttlCleanupInterval;

    /// <summary>
    /// 循环取消令牌源（心跳与清理共用）。
    /// </summary>
    /// <remarks>
    /// The cancellation token source shared by the heartbeat and cleanup loops.
    /// </remarks>
    private readonly CancellationTokenSource _loopCancellation = new();

    /// <summary>
    /// 心跳循环任务。
    /// </summary>
    /// <remarks>
    /// The heartbeat loop task.
    /// </remarks>
    private Task _heartbeatLoopTask;

    /// <summary>
    /// 清理循环任务。
    /// </summary>
    /// <remarks>
    /// The cleanup loop task.
    /// </remarks>
    private Task _cleanupLoopTask;

    /// <summary>
    /// 是否已写终态 Stopped（幂等守卫）。
    /// </summary>
    /// <remarks>
    /// Whether the terminal Stopped state was already written (idempotent guard —
    /// only Stopped writes are deduplicated; heartbeats keep flowing until then).
    /// </remarks>
    private int _stoppedWritten;

    /// <summary>
    /// 当前宣告的实例状态（启动完成为 Booting，MarkActive 后为 Active）。
    /// </summary>
    /// <remarks>
    /// The status currently announced by the heartbeat loop: Booting until the
    /// owning startup flow calls <see cref="MarkActiveAsync"/>, Active afterwards.
    /// </remarks>
    private volatile int _currentStatus = (int)InstanceStatus.Booting;

    /// <summary>
    /// 初始化通用心跳写侧。
    /// </summary>
    /// <remarks>
    /// Initializes the writer. Call <see cref="StartAsync"/> to begin. A null
    /// <paramref name="selfDescriptor"/> keeps this instance observe-only: the
    /// cleanup loop still runs, but no heartbeat is ever written.
    /// </remarks>
    /// <param name="heartbeatStore">心跳存储适配 / The heartbeat storage seam</param>
    /// <param name="playerRouteStore">玩家路由存储适配（可选）/ The optional player-route storage seam</param>
    /// <param name="selfDescriptor">本进程实例身份；null 表示仅观察 / This process's identity; null observes only</param>
    /// <param name="heartbeatInterval">心跳间隔；缺省 5s / The heartbeat interval; defaults to 5 s</param>
    /// <param name="ttlCleanupInterval">过期清理周期；缺省 5s / The cleanup period; defaults to 5 s</param>
    public DiscoveryRegistry(IHeartbeatStore heartbeatStore, IPlayerRouteStore playerRouteStore = null, InstanceDescriptor selfDescriptor = null, TimeSpan? heartbeatInterval = null, TimeSpan? ttlCleanupInterval = null)
    {
        ArgumentNullException.ThrowIfNull(heartbeatStore, nameof(heartbeatStore));

        _heartbeatStore = heartbeatStore;
        _playerRouteStore = playerRouteStore;
        _selfDescriptor = selfDescriptor;
        _heartbeatInterval = heartbeatInterval ?? DefaultHeartbeatInterval;
        _ttlCleanupInterval = ttlCleanupInterval ?? DefaultTtlCleanupInterval;
    }

    /// <summary>
    /// 从环境变量构建本进程实例身份（D12 广播引导；驱动无关单一事实源的转发）。
    /// </summary>
    /// <remarks>
    /// Builds this process's instance identity from the D12 bootstrap environment
    /// variables (forwarded to the driver-neutral <see cref="AdvertiseEndpointEnvironment"/>).
    /// Returns null when the advertise port is not configured — callers treat that
    /// as "no cross-process identity" and skip the write side.
    /// </remarks>
    /// <param name="roleName">承载的 Role 名 / The hosted role name</param>
    /// <returns>实例身份；未配置广播端口时为 null / The identity, or null when the advertise port is not configured</returns>
    public static InstanceDescriptor CreateSelfDescriptorFromEnvironment(string roleName)
    {
        return AdvertiseEndpointEnvironment.CreateSelfDescriptorFromEnvironment(roleName);
    }

    /// <summary>
    /// 启动写侧：幂等建 schema + 立即写入当前状态（Booting）+ 起后台心跳与清理循环。
    /// </summary>
    /// <remarks>
    /// Starts the writer: ensures the schema (idempotent), upserts the current
    /// status (Booting) immediately so the topology can see this process within
    /// one poll round — without routing to it yet — then runs the background
    /// heartbeat loop and the expiry-cleanup loop. The owning startup flow must
    /// call <see cref="MarkActiveAsync"/> once the process is truly ready. Also
    /// subscribes to <see cref="AppDomain.ProcessExit"/> as the best-effort
    /// Stopped safety net. Observe-only instances (null descriptor) skip the
    /// heartbeat write path but still run the cleanup loop.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _heartbeatStore.EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);

        _cleanupLoopTask = Task.Run(() => CleanupLoopAsync(_loopCancellation.Token));

        if (_selfDescriptor == null)
        {
            return;
        }

        await UpsertHeartbeatAsync((InstanceStatus)_currentStatus, CancellationToken.None).ConfigureAwait(false);
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        _heartbeatLoopTask = Task.Run(() => HeartbeatLoopAsync(_loopCancellation.Token));
    }

    /// <summary>
    /// 标记实例就绪：心跳状态由 Booting 切换为 Active 并立即写入。
    /// </summary>
    /// <remarks>
    /// Marks the instance as ready: flips the announced status from Booting to
    /// Active and writes it immediately (instead of waiting for the next loop
    /// tick), so other processes only discover this instance as routable once
    /// its databases, components, and listeners are up. Repeat calls are
    /// harmless. No-op for observe-only instances.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public Task MarkActiveAsync(CancellationToken cancellationToken = default)
    {
        if (_selfDescriptor == null)
        {
            return Task.CompletedTask;
        }

        _currentStatus = (int)InstanceStatus.Active;
        return UpsertHeartbeatAsync(InstanceStatus.Active, cancellationToken);
    }

    /// <summary>
    /// 停止写侧并写终态 Stopped（D17 通道 4 优雅退出）。
    /// </summary>
    /// <remarks>
    /// Stops the loops and writes the terminal Stopped state so watchers drop this
    /// instance immediately instead of waiting for the TTL. Idempotent.
    /// </remarks>
    /// <returns>异步任务 / Async task</returns>
    public async Task StopAsync()
    {
        await _loopCancellation.CancelAsync().ConfigureAwait(false);
        await WaitLoopAsync(_heartbeatLoopTask).ConfigureAwait(false);
        await WaitLoopAsync(_cleanupLoopTask).ConfigureAwait(false);
        await WriteStoppedAsync().ConfigureAwait(false);
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }

    /// <summary>
    /// 释放资源（未写终态时尽力同步写 Stopped）。
    /// </summary>
    /// <remarks>
    /// Disposes and, when Stopped was not written yet, writes it synchronously as a best effort.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            _loopCancellation.Cancel();
            WriteStoppedAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // 尽力而为：进程退出路径上数据库不可达时无法补写终态，交给 TTL 兜底清除。
        }
        finally
        {
            _loopCancellation.Dispose();
        }
    }

    /// <summary>
    /// 心跳循环：固定间隔全量 upsert。
    /// </summary>
    /// <remarks>
    /// The heartbeat loop: a full upsert every interval. Database-outage periods
    /// leave the row stale (the watcher marks the instance Offline after the
    /// three-period threshold — the designed risk behavior); recovery is simply
    /// the next successful full upsert, which carries the complete latest state.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_heartbeatInterval, cancellationToken).ConfigureAwait(false);
                await UpsertHeartbeatAsync((InstanceStatus)_currentStatus, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                LogHelper.Error(exception, "[DiscoveryRegistry] heartbeat upsert failed for instance {instanceId}; will retry next interval", _selfDescriptor.InstanceId);
            }
        }
    }

    /// <summary>
    /// 过期清理循环：周期驱动心跳 15s 与 player_route 30 天的 DeleteExpired。
    /// </summary>
    /// <remarks>
    /// The expiry-cleanup loop: periodically drives
    /// <see cref="IHeartbeatStore.DeleteExpiredAsync"/> (15 s window) and, when a
    /// player-route store is supplied, <see cref="IPlayerRouteStore.DeleteExpiredAsync"/>
    /// (30-day window). Mongo stores no-op both calls (server-side TTL indexes);
    /// PostgreSQL stores execute the DELETEs. Removal is deliberately relaxed to
    /// within one cleanup period — liveness never depends on it. A failed pass is
    /// logged and retried on the next tick.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    private async Task CleanupLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_ttlCleanupInterval, cancellationToken).ConfigureAwait(false);
                await _heartbeatStore.DeleteExpiredAsync(HeartbeatTimeToLive, CancellationToken.None).ConfigureAwait(false);
                if (_playerRouteStore != null)
                {
                    await _playerRouteStore.DeleteExpiredAsync(TimeSpan.FromSeconds(PlayerRouteResolverBootstrap.RouteTimeToLiveSeconds), CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                LogHelper.Error(exception, "[DiscoveryRegistry] TTL cleanup pass failed; will retry next interval");
            }
        }
    }

    /// <summary>
    /// 等待循环任务退出（吞掉预期取消）。
    /// </summary>
    /// <remarks>
    /// Waits for a loop task to exit (swallowing the expected cancellation).
    /// </remarks>
    private static async Task WaitLoopAsync(Task loopTask)
    {
        if (loopTask == null)
        {
            return;
        }

        try
        {
            await loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// 进程退出钩子：尽力同步写 Stopped。
    /// </summary>
    /// <remarks>
    /// The ProcessExit hook: writes Stopped synchronously as a best effort.
    /// </remarks>
    /// <param name="sender">事件源 / The event source</param>
    /// <param name="eventArguments">事件参数 / The event arguments</param>
    private void OnProcessExit(object sender, EventArgs eventArguments)
    {
        try
        {
            WriteStoppedAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // 进程退出路径上数据库不可达时无法补写终态，交给 TTL 兜底清除。
        }
    }

    /// <summary>
    /// 写终态 Stopped（幂等）。
    /// </summary>
    /// <remarks>
    /// Writes the terminal Stopped state (idempotent; the first writer wins). No-op for observe-only instances.
    /// </remarks>
    /// <returns>异步任务 / Async task</returns>
    private Task WriteStoppedAsync()
    {
        return _selfDescriptor == null ? Task.CompletedTask : UpsertHeartbeatAsync(InstanceStatus.Stopped, CancellationToken.None);
    }

    /// <summary>
    /// 全量 upsert 本进程心跳（终态守卫：Stopped 只写一次、终态后不回退）。
    /// </summary>
    /// <remarks>
    /// Upserts the full heartbeat (every field, always the latest state) with the
    /// terminal-state guard: Stopped is written once, and in-flight
    /// Booting/Active heartbeats never overwrite the terminal state.
    /// </remarks>
    /// <param name="status">本次写入状态 / The status to write</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    private Task UpsertHeartbeatAsync(InstanceStatus status, CancellationToken cancellationToken)
    {
        if (status == InstanceStatus.Stopped)
        {
            // 终态幂等：首个 Stopped 写入胜出，后续 Stopped 调用直接返回。
            if (Interlocked.Exchange(ref _stoppedWritten, 1) != 0)
            {
                return Task.CompletedTask;
            }
        }
        else if (Volatile.Read(ref _stoppedWritten) != 0)
        {
            // 终态后不再回退：Stopped 已写入时，仍在途的心跳（Booting/Active）不再覆盖终态。
            return Task.CompletedTask;
        }

        var descriptor = new InstanceDescriptor(
            _selfDescriptor.Role,
            _selfDescriptor.InstanceId,
            _selfDescriptor.AdvertiseEndpoint,
            status,
            0, // 负载自报：固定 0，接真实指标源属后续 change（ponytail：升级路径 = 注入负载采样回调）。
            _selfDescriptor.AddressKind,
            _selfDescriptor.Incarnation,
            DateTime.UtcNow);
        return _heartbeatStore.UpsertAsync(descriptor, cancellationToken);
    }
}
