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


using MongoDB.Driver;

namespace GameFrameX.NetWork.RemoteMessaging.Discovery;

/// <summary>
/// Mongo 心跳读侧（C143d D11/D15：判活 + 双视图路由表 + 事件）。
/// </summary>
/// <remarks>
/// The Mongo heartbeat reader (C143d D11/D15: liveness + the dual-view route table + events).
/// Polls the control database <c>server_heartbeat</c> collection every interval (5 s default)
/// without mutating it, judges each instance against the three-period staleness threshold
/// (15 s by default — the primary liveness signal, with the Mongo TTL as the last-resort
/// document cleanup), and rebuilds the immutable <see cref="RoleRouteTable"/> snapshot
/// atomically (Interlocked.Exchange — D15). Every shape change is broadcast to
/// <see cref="IRoleInstanceEvents"/> subscribers: Online / Draining / Offline / Evicted /
/// Recovered, with the incarnation rule of D15 — the same instance id coming back with a
/// different incarnation emits Offline+Online, never Recovered. During a Mongo outage the
/// previous table is served unchanged (the partition risk mitigation of D15).
/// </remarks>
public sealed class MongoEndpointWatcher : IRoleRouteTableProvider, IDisposable
{
    /// <summary>
    /// 缺省轮询间隔（5s，D11）。
    /// </summary>
    /// <remarks>
    /// The default poll interval (5 s, D11).
    /// </remarks>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 缺省陈旧阈值周期数（3 个心跳周期，D11/D15）。
    /// </summary>
    /// <remarks>
    /// The default staleness threshold in heartbeat periods (3, D11/D15).
    /// </remarks>
    public const int DefaultStalenessPeriods = 3;

    /// <summary>
    /// 心跳集合。
    /// </summary>
    /// <remarks>
    /// The heartbeat collection.
    /// </remarks>
    private readonly IMongoCollection<ServerHeartbeatDocument> _collection;

    /// <summary>
    /// 轮询间隔。
    /// </summary>
    /// <remarks>
    /// The poll interval.
    /// </remarks>
    private readonly TimeSpan _pollInterval;

    /// <summary>
    /// 判活阈值（lastHeartbeat 超过此时长视为陈旧）。
    /// </summary>
    /// <remarks>
    /// The staleness threshold: a lastHeartbeat older than this marks the instance stale.
    /// </remarks>
    private readonly TimeSpan _stalenessThreshold;

    /// <summary>
    /// 事件订阅者列表（启动后只读快照，订阅在 Start 前完成）。
    /// </summary>
    /// <remarks>
    /// The event subscribers (a read-only snapshot once started; subscribe before Start).
    /// </remarks>
    private readonly List<IRoleInstanceEvents> _subscribers = new List<IRoleInstanceEvents>();

    /// <summary>
    /// 订阅者列表的同步锁。
    /// </summary>
    /// <remarks>
    /// The subscribers list lock.
    /// </remarks>
    private readonly object _subscribersLock = new object();

    /// <summary>
    /// 当前已知实例（instanceId → 最近观测与其陈旧标记；含 stale 待 Evicted 项）。
    /// </summary>
    /// <remarks>
    /// The currently known instances (instance id to the last observation and its stale flag;
    /// stale entries stay until the TTL removes their documents, so Recovered can be detected).
    /// </remarks>
    private readonly Dictionary<string, KnownInstance> _knownInstances = new Dictionary<string, KnownInstance>(StringComparer.Ordinal);

    /// <summary>
    /// 曾观测过的最后代数（instanceId → incarnation；Evicted 后仍保留，供重启判定）。
    /// </summary>
    /// <remarks>
    /// The last incarnation ever observed per instance id (kept after eviction so a restart
    /// under the same id can be distinguished from a recovery).
    /// </remarks>
    private readonly Dictionary<string, long> _lastSeenIncarnations = new Dictionary<string, long>(StringComparer.Ordinal);

    /// <summary>
    /// 轮询循环取消令牌源。
    /// </summary>
    /// <remarks>
    /// The poll loop cancellation token source.
    /// </remarks>
    private readonly CancellationTokenSource _loopCancellation = new CancellationTokenSource();

    /// <summary>
    /// 状态与快照的同步锁（单轮 poll 串行化）。
    /// </summary>
    /// <remarks>
    /// The lock serializing state transitions and snapshot swaps.
    /// </remarks>
    private readonly object _stateLock = new object();

    /// <summary>
    /// 轮询循环任务。
    /// </summary>
    /// <remarks>
    /// The poll loop task.
    /// </remarks>
    private Task _loopTask;

    /// <summary>
    /// 当前双视图路由表快照（D15：volatile 原子替换不可变快照）。
    /// </summary>
    /// <remarks>
    /// The current dual-view snapshot (D15: volatile write of an immutable snapshot,
    /// so lock-free readers always observe a fully-built table).
    /// </remarks>
    private volatile RoleRouteTable _currentTable = RoleRouteTable.Empty;

    /// <summary>
    /// 初始化 Mongo 心跳读侧。
    /// </summary>
    /// <remarks>
    /// Initializes the watcher. Call <see cref="StartAsync"/> to begin polling.
    /// </remarks>
    /// <param name="controlDatabase">控制库（gameframex_control）/ The control database</param>
    /// <param name="pollInterval">轮询间隔；缺省 5s / The poll interval; defaults to 5 s</param>
    /// <param name="stalenessThreshold">判活阈值；缺省 3 × 轮询间隔（15s）/ The staleness threshold; defaults to 3 × the poll interval (15 s)</param>
    public MongoEndpointWatcher(IMongoDatabase controlDatabase, TimeSpan? pollInterval = null, TimeSpan? stalenessThreshold = null)
    {
        ArgumentNullException.ThrowIfNull(controlDatabase, nameof(controlDatabase));

        _collection = controlDatabase.GetCollection<ServerHeartbeatDocument>(MongoEndpointRegistry.HeartbeatCollectionName);
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _stalenessThreshold = stalenessThreshold ?? TimeSpan.FromTicks(_pollInterval.Ticks * DefaultStalenessPeriods);
    }

    /// <summary>
    /// 获取当前双视图路由表快照（D15 原子替换语义）。
    /// </summary>
    /// <remarks>
    /// Gets the current snapshot (D15 atomic-replacement semantics); never null.
    /// </remarks>
    /// <value>当前快照 / The current snapshot</value>
    public RoleRouteTable Current
    {
        get
        {
            return _currentTable;
        }
    }

    /// <summary>
    /// 订阅实例上下线事件（须在 <see cref="StartAsync"/> 之前调用）。
    /// </summary>
    /// <remarks>
    /// Subscribes to instance lifecycle events (must be called before <see cref="StartAsync"/>
    /// so no event can be missed between subscribing and the first poll).
    /// </remarks>
    /// <param name="events">事件订阅者 / The subscriber</param>
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
    /// Starts the watcher: one immediate poll round (so callers get a non-empty table
    /// as soon as StartAsync returns) followed by the background loop.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await PollOnceAsync(cancellationToken);
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
                await _loopTask;
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
    /// 轮询循环。
    /// </summary>
    /// <remarks>
    /// The poll loop. A failed round keeps the previous snapshot (Mongo outage resilience)
    /// and retries on the next tick.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, cancellationToken);
                await PollOnceAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                LogHelper.Error(exception, "[MongoEndpointWatcher] poll round failed; keeping the previous route table and retrying next interval");
            }
        }
    }

    /// <summary>
    /// 执行一轮轮询：拉全量 → 判活 → 状态转移 → 原子替换快照 → 广播事件。
    /// </summary>
    /// <remarks>
    /// One poll round: fetch every document, judge liveness, apply the state machine
    /// (Online / Draining / Offline / Evicted / Recovered, with the incarnation rule),
    /// swap the snapshot atomically, then broadcast the collected events.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var documents = await _collection.Find(FilterDefinition<ServerHeartbeatDocument>.Empty).ToListAsync(cancellationToken);
        var pendingEvents = new List<KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>>();
        List<InstanceDescriptor> liveInstances;

        lock (_stateLock)
        {
            liveInstances = ApplyStateTransitions(documents, DateTime.UtcNow, pendingEvents);
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
    /// 应用状态机并产出本轮存活实例集合。
    /// </summary>
    /// <remarks>
    /// Applies the state machine (under the state lock) and returns the live instance set
    /// for the new snapshot. Events are collected instead of fired inline so the snapshot
    /// can be swapped before any subscriber runs.
    /// </remarks>
    /// <param name="documents">本轮拉取的心跳文档 / The documents fetched this round</param>
    /// <param name="nowUtc">判定基准时间（UTC）/ The judgement reference time (UTC)</param>
    /// <param name="pendingEvents">收集的事件 / The collected events</param>
    /// <returns>存活实例集合 / The live instance set</returns>
    private List<InstanceDescriptor> ApplyStateTransitions(List<ServerHeartbeatDocument> documents, DateTime nowUtc, List<KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>> pendingEvents)
    {
        var liveInstances = new List<InstanceDescriptor>(documents.Count);
        foreach (var document in documents)
        {
            var descriptor = TryToDescriptor(document);
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

        EvictMissingInstances(documents, pendingEvents);
        return liveInstances;
    }

    /// <summary>
    /// 状态机派发：按优先级匹配首观测 / incarnation 变化 / 陈旧恢复 / 状态跃迁 / 新鲜→陈旧 五条事件路径。
    /// </summary>
    /// <remarks>
    /// Dispatches the state machine. Each branch is a single-responsibility predicate
    /// so the method stays under the S3776 threshold.
    /// </remarks>
    private void RecordTransition(KnownInstance known, long lastIncarnation, InstanceDescriptor descriptor, bool isStale, List<KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>> pendingEvents)
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
    /// 首观测分支：仅对具备路由资格（非 stale 且 Active/Draining）的首次观测发事件。
    /// </summary>
    /// <remarks>
    /// The first-observation branch: only routable first sightings emit events; stale or
    /// non-routable first sightings stay silent so subscribers never see instances that
    /// the route table would refuse.
    /// </remarks>
    private static void RecordFirstObservation(bool isStale, long lastIncarnation, InstanceDescriptor descriptor, List<KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>> pendingEvents)
    {
        if (IsFirstObservationSkipped(isStale, descriptor.Status))
        {
            return;
        }

        if (lastIncarnation != default && lastIncarnation != descriptor.Incarnation)
        {
            // 曾在 graveyard 里见过且 incarnation 变化 → 重启语义（Offline+Online，D15 规则）。
            pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(RoleInstanceChangeKind.Offline, descriptor));
            pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(RoleInstanceChangeKind.Online, descriptor));
        }
        else if (descriptor.Status == InstanceStatus.Draining)
        {
            // 首次观测即为 Draining：发 Draining（不接新流量、保留在途投递）。
            pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(RoleInstanceChangeKind.Draining, descriptor));
        }
        else
        {
            pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(RoleInstanceChangeKind.Online, descriptor));
        }
    }

    /// <summary>
    /// 已知实例的 incarnation 变化：旧身份下线 + 新身份上线（D15 incarnation 规则）。
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
    /// 状态跃迁：→ Draining 发 Draining；→ Active（自 Draining 恢复接流）发 Online；→ Stopped 发 Offline。
    /// </summary>
    /// <remarks>
    /// Maps the new status to the matching event kind; unknown / non-transitioning states
    /// stay silent.
    /// </remarks>
    private static void RecordStatusChange(InstanceDescriptor descriptor, List<KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>> pendingEvents)
    {
        RoleInstanceChangeKind? kind = descriptor.Status switch
        {
            InstanceStatus.Draining => RoleInstanceChangeKind.Draining,
            InstanceStatus.Active => RoleInstanceChangeKind.Online,
            InstanceStatus.Stopped => RoleInstanceChangeKind.Offline,
            _ => null,
        };
        if (kind.HasValue)
        {
            pendingEvents.Add(new KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>(kind.Value, descriptor));
        }
    }

    /// <summary>
    /// 曾知实例本轮文档消失（TTL 已清除）→ Evicted，彻底移出观测。
    /// </summary>
    /// <remarks>
    /// Evicts known instances whose heartbeat documents disappeared this round (TTL cleanup).
    /// </remarks>
    private void EvictMissingInstances(List<ServerHeartbeatDocument> documents, List<KeyValuePair<RoleInstanceChangeKind, InstanceDescriptor>> pendingEvents)
    {
        var observedInstanceIds = CollectObservedInstanceIds(documents);
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
    /// 收集本轮文档中可解析的实例 id（用于 Evicted 判定）。
    /// </summary>
    /// <remarks>
    /// Collects the parseable instance ids from this round's documents for the missing-instance
    /// check (matches the per-document parse policy of the main loop).
    /// </remarks>
    private static HashSet<string> CollectObservedInstanceIds(List<ServerHeartbeatDocument> documents)
    {
        var observedInstanceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            var descriptor = TryToDescriptor(document);
            if (descriptor == null)
            {
                continue;
            }
            observedInstanceIds.Add(descriptor.InstanceId);
        }
        return observedInstanceIds;
    }

    /// <summary>
    /// 是否 incarnation 变化（D15：旧身份被换新身份）。
    /// </summary>
    private static bool IncarnationChanged(KnownInstance known, InstanceDescriptor descriptor)
    {
        return known.Descriptor.Incarnation != descriptor.Incarnation;
    }

    /// <summary>
    /// 是否从陈旧恢复新鲜（同 incarnation）。
    /// </summary>
    private static bool RecoveredFromStale(KnownInstance known, bool isStale)
    {
        return known.IsStale && !isStale;
    }

    /// <summary>
    /// 是否发生状态跃迁（fresh → 另一 fresh 状态）。
    /// </summary>
    private static bool StatusChanged(KnownInstance known, InstanceDescriptor descriptor, bool isStale)
    {
        return !known.IsStale && known.Descriptor.Status != descriptor.Status && !isStale;
    }

    /// <summary>
    /// 是否从新鲜变为陈旧（三周期阈值判死）。
    /// </summary>
    private static bool BecameStale(KnownInstance known, bool isStale)
    {
        return !known.IsStale && isStale;
    }

    /// <summary>
    /// 是否进入路由表（非 stale 且非 Stopped）。
    /// </summary>
    private static bool IsRoutable(InstanceDescriptor descriptor, bool isStale)
    {
        return !isStale && descriptor.Status != InstanceStatus.Stopped;
    }

    /// <summary>
    /// 首观测是否被跳过滤（stale 或非 Active/Draining 时不发事件）。
    /// </summary>
    private static bool IsFirstObservationSkipped(bool isStale, InstanceStatus status)
    {
        return isStale || !IsActiveOrDraining(status);
    }

    /// <summary>
    /// 是否为 Active 或 Draining 状态。
    /// </summary>
    private static bool IsActiveOrDraining(InstanceStatus status)
    {
        return status == InstanceStatus.Active || status == InstanceStatus.Draining;
    }

    /// <summary>
    /// 心跳文档 → 实例描述符（未知枚举名返回 null 防御旧版本文档）。
    /// </summary>
    /// <remarks>
    /// Converts a heartbeat document to a descriptor; returns null on an unknown enum
    /// name or empty endpoint (defensive against documents written by a different version).
    /// </remarks>
    /// <param name="document">心跳文档 / The heartbeat document</param>
    /// <returns>实例描述符；无法转换时为 null / The descriptor, or null when unparsable</returns>
    private static InstanceDescriptor TryToDescriptor(ServerHeartbeatDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.InstanceId) || string.IsNullOrWhiteSpace(document.Role) || string.IsNullOrWhiteSpace(document.AdvertiseEndpoint))
        {
            return null;
        }

        if (!Enum.TryParse<InstanceStatus>(document.Status, false, out var status) || !Enum.TryParse<EndpointAddressKind>(document.AddressKind, false, out var addressKind))
        {
            return null;
        }

        return new InstanceDescriptor(document.Role, document.InstanceId, document.AdvertiseEndpoint, status, document.Load, addressKind, document.Incarnation, document.LastHeartbeat);
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
