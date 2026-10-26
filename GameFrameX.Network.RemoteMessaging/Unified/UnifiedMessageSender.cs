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

using System.Diagnostics;
using System.Collections.Concurrent;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.RemoteMessaging.Contracts;

namespace GameFrameX.Network.RemoteMessaging.Unified;

/// <summary>
/// 统一消息发送器默认实现。
/// 内聚玩家消息（本服/跨服/离线）和系统消息（服务发现/一致性选路）的完整链路。
/// </summary>
/// <remarks>
/// Default implementation of the unified message sender.
/// Encapsulates the complete pipeline for player messages (local/cross-server/offline)
/// and server messages (service discovery/consistent routing).
/// </remarks>
public sealed class UnifiedMessageSender : IUnifiedMessageSender
{
    private readonly IRemoteMessageClient _remoteClient;
    private readonly IPlayerRouteResolver _routeResolver;
    private readonly IPlayerLocalSender _localSender;
    private readonly IServerInstanceSelector _instanceSelector;
    private readonly MessageSendMetrics _metrics;
    private readonly IRoleRouteTableProvider _routeTable;
    private readonly ConcurrentDictionary<string, string[]> _lastInstanceIdsByService = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _refreshLock = new object();
    private readonly string _currentServerType;
    private readonly string _currentInstanceId;

    /// <summary>
    /// 初始化统一消息发送器。本服身份（本服判定与指标服务名兜底的基准）由调用方在构造时显式注入，
    /// 不读取全局配置——多 Role 进程中应传入触发装配的 Role 的 <c>ServerType</c> 与本进程发现层实例 Id。
    /// </summary>
    /// <remarks>
    /// Initializes the unified message sender. The local-server identity (the basis for the
    /// local-server check and the metrics service-name fallback) is explicitly injected by the
    /// caller at construction time instead of reading global settings — in a multi-role
    /// process, pass the <c>ServerType</c> of the role that triggers the wiring and this
    /// process's discovery instance id. Instance selection reads the heartbeat registry's
    /// dual-view route table (<paramref name="routeTable"/>) — the single source of truth for
    /// inter-server endpoints; the hash ring is rebuilt only when the Active instance set
    /// actually changes.
    /// </remarks>
    /// <param name="remoteClient">远程消息客户端 / Remote message client</param>
    /// <param name="routeResolver">玩家路由解析器 / Player route resolver</param>
    /// <param name="localSender">本服玩家发送器 / Local player sender</param>
    /// <param name="instanceSelector">服务实例选择器 / Server instance selector</param>
    /// <param name="routeTable">双视图路由表提供者（实例列表唯一来源）/ The dual-view route table provider (the single source of the instance list)</param>
    /// <param name="localServerType">本服服务器类型（本服判定基准，忽略大小写） / Local server type (basis of the local-server check, case-insensitive)</param>
    /// <param name="localInstanceId">本进程发现层实例 Id（本服判定基准） / This process's discovery instance id (basis of the local-server check)</param>
    /// <param name="metrics">发送指标聚合器；为空时自动创建 / Metrics recorder; auto-created when null</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="remoteClient"/> 为 null 时抛出 / Thrown when <paramref name="remoteClient"/> is null</exception>
    /// <exception cref="ArgumentNullException">当 <paramref name="routeResolver"/> 为 null 时抛出 / Thrown when <paramref name="routeResolver"/> is null</exception>
    /// <exception cref="ArgumentNullException">当 <paramref name="localSender"/> 为 null 时抛出 / Thrown when <paramref name="localSender"/> is null</exception>
    /// <exception cref="ArgumentNullException">当 <paramref name="instanceSelector"/> 为 null 时抛出 / Thrown when <paramref name="instanceSelector"/> is null</exception>
    /// <exception cref="ArgumentNullException">当 <paramref name="routeTable"/> 为 null 时抛出 / Thrown when <paramref name="routeTable"/> is null</exception>
    /// <exception cref="ArgumentException">当 <paramref name="localServerType"/> 为 null、空串或空白时抛出 / Thrown when <paramref name="localServerType"/> is null, empty, or whitespace</exception>
    public UnifiedMessageSender(
        IRemoteMessageClient remoteClient,
        IPlayerRouteResolver routeResolver,
        IPlayerLocalSender localSender,
        IServerInstanceSelector instanceSelector,
        IRoleRouteTableProvider routeTable,
        string localServerType,
        string localInstanceId,
        MessageSendMetrics metrics = null)
    {
        _remoteClient = remoteClient ?? throw new ArgumentNullException(nameof(remoteClient));
        _routeResolver = routeResolver ?? throw new ArgumentNullException(nameof(routeResolver));
        _localSender = localSender ?? throw new ArgumentNullException(nameof(localSender));
        _instanceSelector = instanceSelector ?? throw new ArgumentNullException(nameof(instanceSelector));
        _routeTable = routeTable ?? throw new ArgumentNullException(nameof(routeTable));
        ArgumentException.ThrowIfNullOrWhiteSpace(localServerType, nameof(localServerType));
        _metrics = metrics ?? new MessageSendMetrics();
        _currentServerType = localServerType;
        _currentInstanceId = localInstanceId;
    }

    /// <summary>
    /// 获取统一发送器内部指标聚合器。
    /// </summary>
    /// <remarks>
    /// Gets the internal metrics recorder of the unified sender.
    /// </remarks>
    /// <value>发送指标聚合器实例 / The metrics recorder instance</value>
    public MessageSendMetrics Metrics
    {
        get { return _metrics; }
    }

    #region SendToPlayerAsync

    /// <summary>
    /// 发送消息给目标玩家。按“本服直发 → 路由解析 → 跨服转发 → 离线处理”的顺序执行完整投递链路，
    /// 全程记录耗时与 traceId 指标；参数非法、取消或异常时返回对应的失败结果。
    /// </summary>
    /// <remarks>
    /// Sends a message to the target player. Runs the full delivery pipeline in the order "local delivery → route resolution → cross-server forwarding → offline handling",
    /// recording elapsed time and traceId metrics throughout; returns a corresponding failure result on invalid arguments, cancellation, or exceptions.
    /// </remarks>
    /// <param name="playerId">目标玩家ID / Target player ID</param>
    /// <param name="message">消息对象 / Message object</param>
    /// <param name="options">发送选项 / Send options</param>
    /// <param name="ct">取消令牌 / Cancellation token</param>
    /// <returns>发送结果 / Send result</returns>
    public async Task<PlayerSendResult> SendToPlayerAsync(
        long playerId,
        MessageObject message,
        PlayerSendOptions options = null,
        CancellationToken ct = default)
    {
        if (playerId <= 0)
        {
            return RecordPlayerResult(PlayerSendResult.Fail(PlayerDeliverStatus.Failed, playerId, "Invalid playerId"));
        }

        if (message == null)
        {
            return RecordPlayerResult(PlayerSendResult.Fail(PlayerDeliverStatus.Failed, playerId, "Message is null"));
        }

        options = options ?? new PlayerSendOptions();
        var sw = Stopwatch.StartNew();
        var traceId = Guid.NewGuid().ToString("N")[..16];

        try
        {
            // Step 1: 优先检查本服在线
            if (_localSender.IsPlayerOnline(playerId))
            {
                var sent = await _localSender.SendToLocalPlayerAsync(playerId, message);
                sw.Stop();

                if (sent)
                {
                    return RecordPlayerResult(PlayerSendResult.Ok(
                                                  PlayerDeliverStatus.LocalDelivered,
                                                  playerId,
                                                  sw.ElapsedMilliseconds,
                                                  traceId));
                }

                // 本服投递失败（可能在投递瞬间断线），继续走路由解析
            }

            // Step 2: 解析路由
            var routeInfo = await _routeResolver.ResolveAsync(playerId);

            if (routeInfo == null)
            {
                sw.Stop();
                return RecordPlayerResult(PlayerSendResult.Fail(
                                              PlayerDeliverStatus.RouteMissing,
                                              playerId,
                                              "Route info not found",
                                              sw.ElapsedMilliseconds,
                                              traceId));
            }

            // Step 3: 路由指向本服但 SessionManager 未命中（路由过期）
            if (routeInfo.IsOnline && IsCurrentServer(routeInfo))
            {
                // 路由指向本服但本服投递失败，视为离线
                sw.Stop();
                return RecordPlayerResult(HandleOffline(playerId, options, sw.ElapsedMilliseconds, traceId));
            }

            // Step 4: 路由指向他服
            if (routeInfo.IsOnline)
            {
                var remoteResult = await ForwardToRemoteServerAsync(playerId, message, options, routeInfo, sw, traceId, ct);
                return RecordPlayerResult(remoteResult);
            }

            // Step 5: 路由标记为离线
            sw.Stop();
            return RecordPlayerResult(HandleOffline(playerId, options, sw.ElapsedMilliseconds, traceId));
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return RecordPlayerResult(PlayerSendResult.Fail(
                                          PlayerDeliverStatus.Cancelled,
                                          playerId,
                                          "Operation cancelled",
                                          sw.ElapsedMilliseconds,
                                          traceId));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return RecordPlayerResult(PlayerSendResult.Fail(
                                          PlayerDeliverStatus.Failed,
                                          playerId,
                                          ex.Message,
                                          sw.ElapsedMilliseconds,
                                          traceId));
        }
    }

    private async Task<PlayerSendResult> ForwardToRemoteServerAsync(
        long playerId,
        MessageObject message,
        PlayerSendOptions options,
        PlayerRouteInfo routeInfo,
        Stopwatch sw,
        string traceId,
        CancellationToken ct)
    {
        try
        {
            // 路由缺失实例 Id（null/空白）视为无效路由，按离线处理：
            // 放行会触发 ServerType 级实例选择，可能把消息送到错误实例。
            if (string.IsNullOrWhiteSpace(routeInfo.InstanceId))
            {
                sw.Stop();
                return HandleOffline(playerId, options, sw.ElapsedMilliseconds, traceId);
            }

            var context = new RemoteCallContext
            {
                ServiceName = routeInfo.ServerType,
                // 实例直达：按 player_route 记录的实例 Id 投递（Instance 视图含 Draining，下线中实例仍可送达；
                // 实例已消失时解析侧返回 EndpointNotFound → offline 分支）
                TargetInstanceId = routeInfo.InstanceId,
                TimeoutMs = options.TimeoutMs,
                AllowRetry = options.AllowRetry,
                MaxRetryCount = options.MaxRetryCount,
                CancellationToken = ct,
                TraceId = traceId,
                Metadata = new Dictionary<string, string>
                {
                    { "TargetPlayerId", playerId.ToString() },
                    { "OriginalServiceName", routeInfo.ServerType },
                },
            };

            var innerRequest = new ReqSendToPlayerInner
            {
                TargetPlayerId = playerId,
                InnerMessage = message,
            };

            var result = await _remoteClient.CallWithResultAsync<RespSendToPlayerInner>(context, innerRequest);
            sw.Stop();

            if (result.IsSuccess && result.Response != null && result.Response.Success)
            {
                return PlayerSendResult.Ok(
                    PlayerDeliverStatus.RemoteDelivered,
                    playerId,
                    sw.ElapsedMilliseconds,
                    traceId,
                    routeInfo.ServerType,
                    result.RetryCount);
            }

            // 目标实例不在路由表内（TTL 过期 / 逻辑服下线 / 旧版占位行）：请求未离开发送进程，
            // 按既有 offline 分支处理（Draining 实例仍在 Instance 视图内可送达，不进这里）。
            // The target instance is absent from the route table (TTL expiry / server decommission /
            // legacy placeholder row): the request never left this process, so it goes through the
            // existing offline branch (draining instances stay resolvable and never land here).
            if (result.StatusCode == RemoteStatusCode.EndpointNotFound)
            {
                return HandleOffline(playerId, options, sw.ElapsedMilliseconds, traceId, result.RetryCount);
            }

            // 跨服转发失败，检查目标服返回的状态
            var remoteFailed = result.Response != null && !result.Response.Success && result.Response.PlayerOffline;
            if (remoteFailed)
            {
                return HandleOffline(playerId, options, sw.ElapsedMilliseconds, traceId, result.RetryCount);
            }

            return PlayerSendResult.Fail(
                PlayerDeliverStatus.Failed,
                playerId,
                result.ErrorMessage ?? "Remote delivery failed",
                sw.ElapsedMilliseconds,
                traceId,
                result.RetryCount);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return PlayerSendResult.Fail(
                PlayerDeliverStatus.Failed,
                playerId,
                $"Cross-server forwarding failed: {ex.Message}",
                sw.ElapsedMilliseconds,
                traceId);
        }
    }

    private PlayerSendResult HandleOffline(long playerId, PlayerSendOptions options, long elapsedMs, string traceId, int retryCount = 0)
    {
        switch (options.OfflineStrategy)
        {
            case PlayerOfflineStrategy.StoreOffline:
                // TODO: 写入离线消息存储（后续完善）
                return PlayerSendResult.Ok(
                    PlayerDeliverStatus.OfflineStored,
                    playerId,
                    elapsedMs,
                    traceId,
                    retryCount: retryCount);

            case PlayerOfflineStrategy.Discard:
                return PlayerSendResult.Fail(
                    PlayerDeliverStatus.Offline,
                    playerId,
                    "Player offline, message discarded",
                    elapsedMs,
                    traceId,
                    retryCount);

            default:
                return PlayerSendResult.Fail(
                    PlayerDeliverStatus.Offline,
                    playerId,
                    "Player offline",
                    elapsedMs,
                    traceId,
                    retryCount);
        }
    }

    /// <summary>
    /// 判定路由是否指向本服：路由 <c>ServerType</c>（忽略大小写）与 <c>InstanceId</c>（序数比较）同时等于
    /// 构造时注入的本服身份。多 Role 进程中「本服」为单一身份（触发装配的 Role）。
    /// 旧版本占位 InstanceId 永不匹配本进程真实实例 Id，按远端处理（投递侧解析不到实例再落 offline 分支）。
    /// </summary>
    /// <remarks>
    /// Determines whether the route points to the local server: both the route's
    /// <c>ServerType</c> (case-insensitive) and <c>InstanceId</c> (ordinal) equal the identity
    /// injected at construction. In a multi-role process the "local server" is a single identity
    /// (the role that triggered the wiring). A legacy placeholder instance id never matches this
    /// process's real discovery instance id, so such routes are treated as remote (and fall into
    /// the offline branch when the dial cannot resolve the instance).
    /// </remarks>
    private bool IsCurrentServer(PlayerRouteInfo routeInfo)
    {
        if (routeInfo == null)
        {
            return false;
        }

        if (!string.Equals(routeInfo.ServerType, _currentServerType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return routeInfo.InstanceId != null && string.Equals(routeInfo.InstanceId, _currentInstanceId, StringComparison.Ordinal);
    }

    #endregion

    #region SendToServerAsync

    /// <summary>
    /// 发送请求-响应消息给目标服务。按需刷新实例列表并选择目标实例，
    /// 依据 AllowRetry 决定走带重试或不带重试的远程调用，并将结果转换为结构化发送结果；
    /// 服务名/消息为空、取消或异常时返回对应的失败结果。
    /// </summary>
    /// <remarks>
    /// Sends a request-response message to the target service. Refreshes the instance list on demand and selects a target instance,
    /// then performs a remote call with or without retry depending on AllowRetry and converts the result into a structured send result;
    /// returns a corresponding failure result on empty service name/message, cancellation, or exceptions.
    /// </remarks>
    /// <typeparam name="TResp">响应消息类型 / Response message type</typeparam>
    /// <param name="serviceName">目标服务名 / Target service name</param>
    /// <param name="message">请求消息对象 / Request message object</param>
    /// <param name="options">发送选项 / Send options</param>
    /// <param name="ct">取消令牌 / Cancellation token</param>
    /// <returns>结构化调用结果 / Structured call result</returns>
    public async Task<ServerSendResult<TResp>> SendToServerAsync<TResp>(
        string serviceName,
        MessageObject message,
        ServerSendOptions options = null,
        CancellationToken ct = default)
        where TResp : class, IResponseMessage
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            return RecordServerResult(
                serviceName,
                ServerSendResult<TResp>.Fail(RemoteStatusCode.EndpointNotFound, "Service name is empty"));
        }

        if (message == null)
        {
            return RecordServerResult(
                serviceName,
                ServerSendResult<TResp>.Fail(RemoteStatusCode.UnknownError, "Message is null"));
        }

        options = options ?? new ServerSendOptions();

        RefreshInstancesIfNeeded(serviceName);

        // 实例选择（实例列表唯一来源 = 心跳注册表路由表 Active 视图）
        var selection = _instanceSelector.Select(serviceName, options.RouteKey);

        var context = new RemoteCallContext
        {
            ServiceName = serviceName,
            TargetInstanceId = selection.HasInstance ? selection.InstanceId : null,
            TimeoutMs = options.TimeoutMs,
            AllowRetry = options.AllowRetry,
            MaxRetryCount = options.MaxRetryCount,
            CancellationToken = ct,
        };

        try
        {
            var result = await _remoteClient.CallWithResultAsync<TResp>(context, message);

            return RecordServerResult(
                serviceName,
                ServerSendResult<TResp>.FromRemoteResult(result, selection.InstanceId));
        }
        catch (OperationCanceledException)
        {
            return RecordServerResult(
                serviceName,
                ServerSendResult<TResp>.Fail(RemoteStatusCode.Cancelled, "Operation cancelled"));
        }
        catch (Exception ex)
        {
            return RecordServerResult(
                serviceName,
                ServerSendResult<TResp>.Fail(RemoteStatusCode.UnknownError, ex.Message));
        }
    }

    #endregion

    #region SendToServerOneWayAsync

    /// <summary>
    /// 单向发送消息给目标服务，不等待响应。按需刷新实例列表并选择目标实例后执行单向远程调用；
    /// 服务名/消息为空或调用异常时返回对应的失败结果。
    /// </summary>
    /// <remarks>
    /// Sends a one-way message to the target service without waiting for a response. Refreshes the instance list on demand, selects a target instance, then performs the one-way remote call;
    /// returns a corresponding failure result on empty service name/message or call exceptions.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / Target service name</param>
    /// <param name="message">消息对象 / Message object</param>
    /// <param name="options">发送选项 / Send options</param>
    /// <param name="ct">取消令牌 / Cancellation token</param>
    /// <returns>发送结果 / Send result</returns>
    public async Task<ServerSendResult> SendToServerOneWayAsync(
        string serviceName,
        MessageObject message,
        ServerSendOptions options = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            return RecordServerOneWayResult(serviceName, ServerSendResult.Fail("Service name is empty"));
        }

        if (message == null)
        {
            return RecordServerOneWayResult(serviceName, ServerSendResult.Fail("Message is null"));
        }

        options = options ?? new ServerSendOptions();

        RefreshInstancesIfNeeded(serviceName);

        var selection = _instanceSelector.Select(serviceName, options.RouteKey);

        var context = new RemoteCallContext
        {
            ServiceName = serviceName,
            TargetInstanceId = selection.HasInstance ? selection.InstanceId : null,
            TimeoutMs = options.TimeoutMs,
            AllowRetry = options.AllowRetry,
            MaxRetryCount = options.MaxRetryCount,
            CancellationToken = ct,
        };

        try
        {
            await _remoteClient.SendOneWayAsync(context, message);
            return RecordServerOneWayResult(serviceName, ServerSendResult.Ok(selection.InstanceId));
        }
        catch (Exception ex)
        {
            return RecordServerOneWayResult(serviceName, ServerSendResult.Fail($"One-way send failed: {ex.Message}"));
        }
    }

    #endregion

    /// <summary>
    /// 按需同步实例列表到实例选择器：仅当 Active 实例集合（InstanceId 集合）实际变化时才重建哈希环。
    /// </summary>
    /// <remarks>
    /// Feeds the Active instance set from the route table into the instance selector: the hash
    /// ring is rebuilt only when the instance-id set actually changes. The watcher replaces the
    /// snapshot object roughly every poll (about 5 s), but the instance set usually stays the
    /// same — comparing the id set (not the snapshot reference, not descriptor references)
    /// keeps ring rebuilds proportional to real topology changes.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    private void RefreshInstancesIfNeeded(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return;
        }

        lock (_refreshLock)
        {
            var activeInstances = _routeTable.Current.GetActiveInstances(serviceName);
            var instanceIds = new string[activeInstances.Count];
            for (var index = 0; index < activeInstances.Count; index++)
            {
                instanceIds[index] = activeInstances[index].InstanceId;
            }

            Array.Sort(instanceIds, StringComparer.Ordinal);

            var lastInstanceIds = _lastInstanceIdsByService.GetOrAdd(serviceName, _ => Array.Empty<string>());
            if (InstanceIdsEqual(lastInstanceIds, instanceIds))
            {
                return;
            }

            _lastInstanceIdsByService[serviceName] = instanceIds;
            _instanceSelector.RefreshInstances(serviceName, instanceIds);
        }
    }

    /// <summary>
    /// 逐元素序数比较两组实例 Id（数组已排序，长度不同即不相等）。
    /// </summary>
    /// <remarks>
    /// Compares two instance-id arrays element-wise with ordinal semantics (both sorted;
    /// different lengths mean different sets).
    /// </remarks>
    /// <param name="left">左侧实例 Id 数组 / The left array</param>
    /// <param name="right">右侧实例 Id 数组 / The right array</param>
    /// <returns>集合相同返回 true / true when the sets are equal</returns>
    private static bool InstanceIdsEqual(string[] left, string[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private PlayerSendResult RecordPlayerResult(PlayerSendResult result)
    {
        _metrics.Record(new SendMetricsRecord
        {
            TargetType = "player",
            ServiceName = result.TargetServiceName ?? _currentServerType,
            StatusCode = result.Status.ToString(),
            ElapsedMs = result.ElapsedMs,
            RetryCount = result.RetryCount,
        });
        return result;
    }

    private ServerSendResult<TResp> RecordServerResult<TResp>(string serviceName, ServerSendResult<TResp> result)
        where TResp : class, IResponseMessage
    {
        _metrics.Record(new SendMetricsRecord
        {
            TargetType = "server",
            ServiceName = serviceName ?? string.Empty,
            StatusCode = result.StatusCode.ToString(),
            ElapsedMs = result.ElapsedMs,
            RetryCount = result.RetryCount,
        });
        return result;
    }

    private ServerSendResult RecordServerOneWayResult(string serviceName, ServerSendResult result)
    {
        _metrics.Record(new SendMetricsRecord
        {
            TargetType = "server",
            ServiceName = serviceName ?? string.Empty,
            StatusCode = result.IsSuccess ? "Success" : "Failed",
        });
        return result;
    }
}