// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using Npgsql;

namespace GameFrameX.NetWork.RemoteMessaging.Discovery;

/// <summary>
/// PostgreSQL 心跳读侧（C166 T8：与 <c>MongoEndpointWatcher</c> 状态机逐字段对齐的平行实现）。
/// </summary>
/// <remarks>
/// The PostgreSQL heartbeat reader (C166 T8): a field-by-field parallel of
/// <c>MongoEndpointWatcher</c> polling the <c>server_heartbeat</c> table
/// every interval (5 s default). The three-period staleness threshold (15 s by
/// default) remains the primary liveness signal: liveness is judged by comparing
/// each row's <c>last_heartbeat</c> against the current UTC time, never inferred
/// from row counts (rows disappear only when the TTL cleanup job runs, which is
/// later than the staleness judgment). The Online / Draining / Offline / Evicted /
/// Recovered state machine, the incarnation rule (same id + new incarnation emits
/// Offline+Online, never Recovered), the atomic snapshot swap, and the
/// outage-resilience behavior (a failed poll round keeps the previous table) are
/// all identical to the Mongo watcher.
/// </remarks>
public sealed class PostgreSqlEndpointWatcher : IRoleRouteTableProvider, IDisposable
{
    /// <summary>
    /// 缺省轮询间隔（5s，与 Mongo 版同值）。
    /// </summary>
    /// <remarks>
    /// The default poll interval (5 s, same value as the Mongo implementation).
    /// </remarks>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 缺省陈旧阈值周期数（3 个心跳周期）。
    /// </summary>
    /// <remarks>
    /// The default staleness threshold in heartbeat periods (3).
    /// </remarks>
    public const int DefaultStalenessPeriods = 3;

    /// <summary>
    /// 全量拉取心跳行。
    /// </summary>
    /// <remarks>
    /// Fetches every heartbeat row (all eight contract columns).
    /// </remarks>
    private const string SelectAllSql = "SELECT instance_id, role, advertise_endpoint, status, load, address_kind, incarnation, last_heartbeat FROM server_heartbeat;";

    /// <summary>
    /// 数据源（池化）。
    /// </summary>
    /// <remarks>
    /// The pooled data source.
    /// </remarks>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// 轮询间隔。
    /// </summary>
    /// <remarks>
    /// The poll interval.
    /// </remarks>
    private readonly TimeSpan _pollInterval;

    /// <summary>
    /// 判活阈值（last_heartbeat 超过此时长视为陈旧）。
    /// </summary>
    /// <remarks>
    /// The staleness threshold.
    /// </remarks>
    private readonly TimeSpan _stalenessThreshold;

    /// <summary>
    /// 事件订阅者列表（启动后只读快照，订阅在 Start 前完成）。
    /// </summary>
    /// <remarks>
    /// The event subscribers (subscribe before Start).
    /// </remarks>
    private readonly List<IRoleInstanceEvents> _subscribers = new();

    /// <summary>
    /// 订阅者列表的同步锁。
    /// </summary>
    /// <remarks>
    /// The subscribers list lock.
    /// </remarks>
    private readonly object _subscribersLock = new();

    /// <summary>
    /// 当前已知实例（instanceId → 最近观测与陈旧标记）。
    /// </summary>
    /// <remarks>
    /// The currently known instances (stale entries stay until the TTL cleanup removes their rows).
    /// </remarks>
    private readonly Dictionary<string, KnownInstance> _knownInstances = new(StringComparer.Ordinal);

    /// <summary>
    /// 曾观测过的最后代数（instanceId → incarnation；Evicted 后仍保留）。
    /// </summary>
    /// <remarks>
    /// The last incarnation ever observed per instance id.
    /// </remarks>
    private readonly Dictionary<string, long> _lastSeenIncarnations = new(StringComparer.Ordinal);

    /// <summary>
    /// 轮询循环取消令牌源。
    /// </summary>
    /// <remarks>
    /// The poll loop cancellation token source.
    /// </remarks>
    private readonly CancellationTokenSource _loopCancellation = new();

    /// <summary>
    /// 状态与快照的同步锁（单轮 poll 串行化）。
    /// </summary>
    /// <remarks>
    /// The lock serializing state transitions and snapshot swaps.
    /// </remarks>
    private readonly object _stateLock = new();

    /// <summary>
    /// 轮询循环任务。
    /// </summary>
    /// <remarks>
    /// The poll loop task.
    /// </remarks>
    private Task _loopTask;

    /// <summary>
    /// 当前双视图路由表快照（volatile 原子替换不可变快照）。
    /// </summary>
    /// <remarks>
    /// The current dual-view snapshot (volatile atomic replacement).
    /// </remarks>
    private volatile RoleRouteTable _currentTable = RoleRouteTable.Empty;

    /// <summary>
    /// 初始化 PostgreSQL 心跳读侧。
    /// </summary>
    /// <remarks>
    /// Initializes the watcher; call <see cref="StartAsync"/> to begin polling. The
    /// heartbeat table must exist (the registry's StartAsync or the runtime wiring
    /// creates it; the watcher never mutates the table).
    /// </remarks>
    /// <param name="dataSource">控制库数据源 / The control-database data source</param>
    /// <param name="pollInterval">轮询间隔；缺省 5s / The poll interval; defaults to 5 s</param>
    /// <param name="stalenessThreshold">判活阈值；缺省 3 × 轮询间隔（15s）/ The staleness threshold; defaults to 3 × the poll interval</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="dataSource"/> 为 null 时抛出 / Thrown when <paramref name="dataSource"/> is null</exception>
    public PostgreSqlEndpointWatcher(NpgsqlDataSource dataSource, TimeSpan? pollInterval = null, TimeSpan? stalenessThreshold = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource, nameof(dataSource));

        _dataSource = dataSource;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _stalenessThreshold = stalenessThreshold ?? TimeSpan.FromTicks(_pollInterval.Ticks * DefaultStalenessPeriods);
    }

    /// <summary>
    /// 获取当前双视图路由表快照；永不为 null。
    /// </summary>
    /// <remarks>
    /// Gets the current snapshot; never null.
    /// </remarks>
    /// <value>当前快照 / The current snapshot</value>
    public RoleRouteTable Current
    {
        get { return _currentTable; }
    }

    /// <summary>
    /// 订阅实例上下线事件（须在 <see cref="StartAsync"/> 之前调用）。
    /// </summary>
    /// <remarks>
    /// Subscribes to instance lifecycle events (must be called before <see cref="StartAsync"/>).
    /// </remarks>
    /// <param name="events">事件订阅者 / The subscriber</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="events"/> 为 null 时抛出 / Thrown when <paramref name="events"/> is null</exception>
    public void Subscribe(IRoleInstanceEvents events)
    {
        ArgumentNullException.ThrowIfNull(events, nameof(events));

        lock (_subscribersLock)
        {
            _subscribers.Add(events);
        }
    }

    /// <summary>
    /// 启动读侧：立即执行一轮轮询 + 起后台轮询循环。
    /// </summary>
    /// <remarks>
    /// Starts the watcher: one immediate poll round followed by the background loop.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await PollOnceAsync(cancellationToken).ConfigureAwait(false);
        _loopTask = Task.Run(() => PollLoopAsync(_loopCancellation.Token));
    }

    /// <summary>
    /// 停止读侧轮询。
    /// </summary>
    /// <remarks>
    /// Stops the poll loop and waits for it; the last snapshot stays readable through <see cref="Current"/>.
    /// </remarks>
    /// <returns>异步任务 / Async task</returns>
    public async Task StopAsync()
    {
        _loopCancellation.Cancel();
        if (_loopTask != null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    /// <summary>
    /// 释放资源。
    /// </summary>
    /// <remarks>
    /// Releases resources (cancels the loop).
    /// </remarks>
    public void Dispose()
    {
        _loopCancellation.Cancel();
        _loopCancellation.Dispose();
    }

    /// <summary>
    /// 轮询循环：失败轮次保留上一快照，下一 tick 重试。
    /// </summary>
    /// <remarks>
    /// The poll loop; a failed round keeps the previous snapshot (database outage resilience).
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
                await PollOnceAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                LogHelper.Error(exception, "[PostgreSqlEndpointWatcher] poll round failed; keeping the previous route table and retrying next interval");
            }
        }
    }

    /// <summary>
    /// 执行一轮轮询：拉全量 → 判活 → 状态转移 → 原子替换快照 → 广播事件。
    /// </summary>
    /// <remarks>
    /// One poll round: fetch every row, judge liveness, apply the state machine,
    /// swap the snapshot atomically, then broadcast the collected events.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var rows = await FetchRowsAsync(cancellationToken).ConfigureAwait(false);
        var pendingEvents = new List<KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>>();
        List<InstanceDescriptor> liveInstances;

        lock (_stateLock)
        {
            liveInstances = ApplyStateTransitions(rows, DateTime.UtcNow, pendingEvents);
            _currentTable = RoleRouteTable.FromInstances(liveInstances);
        }

        // 先换表后广播：订阅者在事件里读到的 Current 已是转移后的新表。
        IRoleInstanceEvents[] subscribersSnapshot;
        lock (_subscribersLock)
        {
            subscribersSnapshot = _subscribers.ToArray();
        }

        foreach (var pair in pendingEvents)
        {
            foreach (var subscriber in subscribersSnapshot)
            {
                subscriber.OnInstanceChanged(pair.Key, pair.Value);
            }
        }
    }

    /// <summary>
    /// 拉取全部心跳行。
    /// </summary>
    /// <remarks>
    /// Fetches every heartbeat row from the table (timestamptz is read back as UTC).
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>心跳行集合 / The heartbeat rows</returns>
    private async Task<List<HeartbeatRow>> FetchRowsAsync(CancellationToken cancellationToken)
    {
        var rows = new List<HeartbeatRow>();
        await using var command = _dataSource.CreateCommand(SelectAllSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new HeartbeatRow(
                         reader.GetString(0),
                         reader.GetString(1),
                         reader.GetString(2),
                         reader.GetString(3),
                         reader.GetInt32(4),
                         reader.GetString(5),
                         reader.GetInt64(6),
                         DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)));
        }

        return rows;
    }

    /// <summary>
    /// 应用状态机并产出本轮存活实例集合（与 Mongo 版逐分支对齐）。
    /// </summary>
    /// <remarks>
    /// Applies the state machine (under the state lock) and returns the live instance set.
    /// </remarks>
    /// <param name="rows">本轮拉取的心跳行 / The rows fetched this round</param>
    /// <param name="nowUtc">判定基准时间（UTC）/ The judgement reference time (UTC)</param>
    /// <param name="pendingEvents">收集的事件 / The collected events</param>
    /// <returns>存活实例集合 / The live instance set</returns>
    private List<InstanceDescriptor> ApplyStateTransitions(List<HeartbeatRow> rows, DateTime nowUtc, List<KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>> pendingEvents)
    {
        var liveInstances = new List<InstanceDescriptor>(rows.Count);
        foreach (var row in rows)
        {
            var descriptor = TryToDescriptor(row);
            if (descriptor == null)
            {
                continue;
            }

            var isStale = nowUtc - descriptor.LastHeartbeatUtc > _stalenessThreshold;
            _lastSeenIncarnations.TryGetValue(descriptor.InstanceId, out var lastIncarnation);
            _knownInstances.TryGetValue(descriptor.InstanceId, out var known);

            RecordTransition(known, lastIncarnation, descriptor, isStale, pendingEvents);
            _knownInstances[descriptor.InstanceId] = new KnownInstance(descriptor, isStale);
            _lastSeenIncarnations[descriptor.InstanceId] = descriptor.Incarnation;

            if (IsRoutable(descriptor, isStale))
            {
                liveInstances.Add(descriptor);
            }
        }

        EvictMissingInstances(rows, pendingEvents);
        return liveInstances;
    }

    /// <summary>
    /// 状态机派发：首观测 / incarnation 变化 / 陈旧恢复 / 状态跃迁 / 新鲜→陈旧 五条事件路径。
    /// </summary>
    /// <remarks>
    /// Dispatches the state machine (same single-responsibility predicates as the Mongo watcher).
    /// </remarks>
    private static void RecordTransition(KnownInstance known, long lastIncarnation, InstanceDescriptor descriptor, bool isStale, List<KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>> pendingEvents)
    {
        if (known == null)
        {
            RecordFirstObservation(isStale, lastIncarnation, descriptor, pendingEvents);
        }
        else if (IncarnationChanged(known, descriptor))
        {
            EmitIncarnationChange(known.Descriptor, descriptor, pendingEvents);
        }
        else if (RecoveredFromStale(known, isStale))
        {
            // 同 incarnation 从陈旧恢复新鲜 → Recovered。
            pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(RoleInstanceChangeKind.Recovered, descriptor));
        }
        else if (StatusChanged(known, descriptor, isStale))
        {
            RecordStatusChange(descriptor, pendingEvents);
        }
        else if (BecameStale(known, isStale))
        {
            // 新鲜 → 陈旧：三周期阈值判死，摘出路由表。
            pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(RoleInstanceChangeKind.Offline, descriptor));
        }
    }

    /// <summary>
    /// 首观测分支：仅对具备路由资格的首次观测发事件。
    /// </summary>
    /// <remarks>
    /// The first-observation branch (routable sightings only).
    /// </remarks>
    private static void RecordFirstObservation(bool isStale, long lastIncarnation, InstanceDescriptor descriptor, List<KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>> pendingEvents)
    {
        if (IsFirstObservationSkipped(isStale, descriptor.Status))
        {
            return;
        }

        if (lastIncarnation != default && lastIncarnation != descriptor.Incarnation)
        {
            // 曾在 graveyard 里见过且 incarnation 变化 → 重启语义（Offline+Online）。
            pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(RoleInstanceChangeKind.Offline, descriptor));
            pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(RoleInstanceChangeKind.Online, descriptor));
        }
        else if (descriptor.Status == InstanceStatus.Draining)
        {
            pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(RoleInstanceChangeKind.Draining, descriptor));
        }
        else
        {
            pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(RoleInstanceChangeKind.Online, descriptor));
        }
    }

    /// <summary>
    /// 已知实例的 incarnation 变化：旧身份下线 + 新身份上线。
    /// </summary>
    /// <remarks>
    /// Emits the incarnation-change pair (Offline the old identity, Online the new one).
    /// </remarks>
    private static void EmitIncarnationChange(InstanceDescriptor oldDescriptor, InstanceDescriptor newDescriptor, List<KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>> pendingEvents)
    {
        pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(RoleInstanceChangeKind.Offline, oldDescriptor));
        pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(RoleInstanceChangeKind.Online, newDescriptor));
    }

    /// <summary>
    /// 状态跃迁：→ Draining 发 Draining；→ Active 发 Online；→ Stopped 发 Offline。
    /// </summary>
    /// <remarks>
    /// Maps the new status to the matching event kind.
    /// </remarks>
    private static void RecordStatusChange(InstanceDescriptor descriptor, List<KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>> pendingEvents)
    {
        RoleInstanceChangeKind? kind = descriptor.Status switch
        {
            InstanceStatus.Draining => RoleInstanceChangeKind.Draining,
            InstanceStatus.Active   => RoleInstanceChangeKind.Online,
            InstanceStatus.Stopped  => RoleInstanceChangeKind.Offline,
            _                       => null,
        };
        if (kind.HasValue)
        {
            pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(kind.Value, descriptor));
        }
    }

    /// <summary>
    /// 曾知实例本轮行消失（TTL 清理已删除）→ Evicted，彻底移出观测。
    /// </summary>
    /// <remarks>
    /// Evicts known instances whose heartbeat rows disappeared this round. Note the
    /// relaxed timing versus Mongo (AC-4): rows are removed by the TTL cleanup job
    /// within one cleanup period after expiry, not by a TTL index at the exact
    /// expire-after instant — Evicted therefore arrives within that window.
    /// </remarks>
    private void EvictMissingInstances(List<HeartbeatRow> rows, List<KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>> pendingEvents)
    {
        var observedInstanceIds = CollectObservedInstanceIds(rows);
        var evictedIds = new List<string>();
        foreach (var pair in _knownInstances)
        {
            if (!observedInstanceIds.Contains(pair.Key))
            {
                pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(RoleInstanceChangeKind.Evicted, pair.Value.Descriptor));
                evictedIds.Add(pair.Key);
            }
        }

        foreach (var evictedId in evictedIds)
        {
            _knownInstances.Remove(evictedId);
        }
    }

    /// <summary>
    /// 收集本轮行中可解析的实例 id（用于 Evicted 判定）。
    /// </summary>
    /// <remarks>
    /// Collects the parseable instance ids from this round's rows.
    /// </remarks>
    private static HashSet<string> CollectObservedInstanceIds(List<HeartbeatRow> rows)
    {
        var observedInstanceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var descriptor = TryToDescriptor(row);
            if (descriptor == null)
            {
                continue;
            }

            observedInstanceIds.Add(descriptor.InstanceId);
        }

        return observedInstanceIds;
    }

    /// <summary>
    /// 是否 incarnation 变化。
    /// </summary>
    /// <remarks>
    /// Whether the incarnation changed.
    /// </remarks>
    private static bool IncarnationChanged(KnownInstance known, InstanceDescriptor descriptor)
    {
        return known.Descriptor.Incarnation != descriptor.Incarnation;
    }

    /// <summary>
    /// 是否从陈旧恢复新鲜（同 incarnation）。
    /// </summary>
    /// <remarks>
    /// Whether the instance recovered from stale (same incarnation).
    /// </remarks>
    private static bool RecoveredFromStale(KnownInstance known, bool isStale)
    {
        return known.IsStale && !isStale;
    }

    /// <summary>
    /// 是否发生状态跃迁（fresh → 另一 fresh 状态）。
    /// </summary>
    /// <remarks>
    /// Whether the status changed (fresh to a different fresh status).
    /// </remarks>
    private static bool StatusChanged(KnownInstance known, InstanceDescriptor descriptor, bool isStale)
    {
        return !known.IsStale && known.Descriptor.Status != descriptor.Status && !isStale;
    }

    /// <summary>
    /// 是否从新鲜变为陈旧（三周期阈值判死）。
    /// </summary>
    /// <remarks>
    /// Whether the instance became stale (the three-period death threshold).
    /// </remarks>
    private static bool BecameStale(KnownInstance known, bool isStale)
    {
        return !known.IsStale && isStale;
    }

    /// <summary>
    /// 是否进入路由表（非 stale 且非 Stopped）。
    /// </summary>
    /// <remarks>
    /// Whether the instance is routable (not stale and not Stopped).
    /// </remarks>
    private static bool IsRoutable(InstanceDescriptor descriptor, bool isStale)
    {
        return !isStale && descriptor.Status != InstanceStatus.Stopped;
    }

    /// <summary>
    /// 首观测是否被跳过（stale 或非 Active/Draining 时不发事件）。
    /// </summary>
    /// <remarks>
    /// Whether the first observation is skipped (no event when stale or not Active/Draining).
    /// </remarks>
    private static bool IsFirstObservationSkipped(bool isStale, InstanceStatus status)
    {
        return isStale || !IsActiveOrDraining(status);
    }

    /// <summary>
    /// 是否为 Active 或 Draining 状态。
    /// </summary>
    /// <remarks>
    /// Whether the status is Active or Draining.
    /// </remarks>
    private static bool IsActiveOrDraining(InstanceStatus status)
    {
        return status == InstanceStatus.Active || status == InstanceStatus.Draining;
    }

    /// <summary>
    /// 心跳行 → 实例描述符（未知枚举名或空端点返回 null，防御异构写方）。
    /// </summary>
    /// <remarks>
    /// Converts a heartbeat row to a descriptor; returns null on an unknown enum
    /// name or empty endpoint (defensive against rows written by a different version).
    /// </remarks>
    /// <param name="row">心跳行 / The heartbeat row</param>
    /// <returns>实例描述符；无法转换时为 null / The descriptor, or null when unparsable</returns>
    private static InstanceDescriptor TryToDescriptor(HeartbeatRow row)
    {
        if (string.IsNullOrWhiteSpace(row.InstanceId) || string.IsNullOrWhiteSpace(row.Role) || string.IsNullOrWhiteSpace(row.AdvertiseEndpoint))
        {
            return null;
        }

        if (!Enum.TryParse<InstanceStatus>(row.Status, false, out var status) || !Enum.TryParse<EndpointAddressKind>(row.AddressKind, false, out var addressKind))
        {
            return null;
        }

        return new InstanceDescriptor(row.Role, row.InstanceId, row.AdvertiseEndpoint, status, row.Load, addressKind, row.Incarnation, DateTime.SpecifyKind(row.LastHeartbeat, DateTimeKind.Utc));
    }

    /// <summary>
    /// server_heartbeat 行（读侧投影）。
    /// </summary>
    /// <remarks>
    /// The server_heartbeat row projection (read side).
    /// </remarks>
    private readonly struct HeartbeatRow
    {
        /// <summary>
        /// 初始化行投影。
        /// </summary>
        /// <remarks>
        /// Initializes the row projection.
        /// </remarks>
        /// <param name="instanceId">实例唯一标识 / The instance id</param>
        /// <param name="role">Role 名 / The role name</param>
        /// <param name="advertiseEndpoint">广播端点 / The advertise endpoint</param>
        /// <param name="status">状态名 / The status name</param>
        /// <param name="load">负载值 / The load value</param>
        /// <param name="addressKind">地址形态名 / The address kind name</param>
        /// <param name="incarnation">代数 / The incarnation</param>
        /// <param name="lastHeartbeat">最后心跳时间（UTC）/ The last heartbeat time (UTC)</param>
        public HeartbeatRow(string instanceId, string role, string advertiseEndpoint, string status, int load, string addressKind, long incarnation, DateTime lastHeartbeat)
        {
            InstanceId = instanceId;
            Role = role;
            AdvertiseEndpoint = advertiseEndpoint;
            Status = status;
            Load = load;
            AddressKind = addressKind;
            Incarnation = incarnation;
            LastHeartbeat = lastHeartbeat;
        }

        /// <summary>实例唯一标识 / The instance id</summary>
        public string InstanceId { get; }

        /// <summary>Role 名 / The role name</summary>
        public string Role { get; }

        /// <summary>广播端点 / The advertise endpoint</summary>
        public string AdvertiseEndpoint { get; }

        /// <summary>状态名 / The status name</summary>
        public string Status { get; }

        /// <summary>负载值 / The load value</summary>
        public int Load { get; }

        /// <summary>地址形态名 / The address kind name</summary>
        public string AddressKind { get; }

        /// <summary>代数 / The incarnation</summary>
        public long Incarnation { get; }

        /// <summary>最后心跳时间（UTC）/ The last heartbeat time (UTC)</summary>
        public DateTime LastHeartbeat { get; }
    }

    /// <summary>
    /// 已知实例观测记录（描述符 + 陈旧标记）。
    /// </summary>
    /// <remarks>
    /// The record of a known instance: its last observed descriptor and the stale flag.
    /// </remarks>
    private sealed class KnownInstance
    {
        /// <summary>
        /// 初始化观测记录。
        /// </summary>
        /// <remarks>
        /// Initializes the record.
        /// </remarks>
        /// <param name="descriptor">最近观测 / The last observed descriptor</param>
        /// <param name="isStale">是否陈旧 / Whether the observation is stale</param>
        public KnownInstance(InstanceDescriptor descriptor, bool isStale)
        {
            Descriptor = descriptor;
            IsStale = isStale;
        }

        /// <summary>最近观测 / The last observed descriptor</summary>
        public InstanceDescriptor Descriptor { get; }

        /// <summary>是否陈旧 / The stale flag</summary>
        public bool IsStale { get; }
    }
}