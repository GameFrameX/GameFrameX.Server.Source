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
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Concurrent;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Events;
using GameFrameX.Discovery.Routing;
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Logger;
using GameFrameX.Network.Abstractions;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Core.Session;

/// <summary>
/// 管理玩家运行时会话：一个玩家一个，下线之后移除，顶号之后释放之前的channel，替换channel。
/// 单例实现 <see cref="IPlayerSessionManager"/>（更名 PlayerSessionManager，避免与他系统 Session 管理器族重名）。
/// </summary>
public sealed class PlayerSessionManager : IPlayerSessionManager
{
    /// <summary>
    /// 全局单例入口。获取路径为 <c>PlayerSessionManager.Instance</c>，引用面一律按 <see cref="IPlayerSessionManager"/> 接口
    /// （<see cref="PlayerSessionFastPathAdapter"/> 单例先例同款：私有构造 + <c>static readonly</c> 实例）。
    /// </summary>
    public static readonly IPlayerSessionManager Instance = new PlayerSessionManager();

    private PlayerSessionManager()
    {
    }

    private readonly ConcurrentDictionary<string, IPlayerSession> SessionMap = new();
    private readonly ConcurrentDictionary<long, SessionRouteSnapshot> PlayerRouteMap = new();

    /// <summary>
    /// 顶号串行锁表：playerId → in-flight 登录的互斥信号量（C306）。
    /// </summary>
    /// <remarks>
    /// Per-player login serialization semaphores. The dictionary only holds entries for
    /// in-flight logins — <see cref="ReleaseLoginLock"/> reclaims the entry while still
    /// holding the semaphore, so the map never grows with the historical player count.
    /// </remarks>
    private readonly ConcurrentDictionary<long, SemaphoreSlim> PlayerLoginLocks = new();

    /// <summary>
    /// 玩家路由外发同步目标（控制库写入钩子）。
    /// </summary>
    /// <remarks>
    /// The outbound player-route sync target (control-database write hook). Wired by
    /// <see>
    ///     <cref>MongoPlayerRouteResolverBootstrap</cref>
    /// </see>
    /// once the control database
    /// is registered; default is <see cref="NullPlayerRouteSyncTarget.Instance"/>
    /// so the in-process-only launch flow keeps working with zero side effects.
    /// Callers must catch and swallow any exception from the hook — the local
    /// <see cref="PlayerRouteMap"/> stays the single source of truth for the
    /// process and a failed Mongo write is recovered on the next SetOnline.
    /// </remarks>
    public IPlayerRouteSyncTarget PlayerRouteSyncTarget
    {
        get { return _playerRouteSyncTarget; }
        set { _playerRouteSyncTarget = value ?? NullPlayerRouteSyncTarget.Instance; }
    }

    private IPlayerRouteSyncTarget _playerRouteSyncTarget = NullPlayerRouteSyncTarget.Instance;

    /// <summary>
    /// 本进程发现层实例 Id（路由同步写入 player_route.InstanceId 的权威值；与心跳自描述符同源注入）。
    /// </summary>
    /// <remarks>
    /// This process's discovery instance id — the authoritative value written into
    /// <c>player_route.InstanceId</c> by the route-sync hook (injected from the same source as
    /// the heartbeat self descriptor through the player-route bootstrap wiring). Blank for
    /// observe-only processes (no advertise identity): the upsert hook is then skipped — such
    /// rows would be unroutable cross-process anyway (the process writes no heartbeat either).
    /// </remarks>
    public string LocalDiscoveryInstanceId { get; set; }

    /// <summary>
    /// 获取当前在线玩家的数量。
    /// </summary>
    /// <returns>当前在线玩家的数量。</returns>
    public int Count()
    {
        return SessionMap.Count;
    }

    /// <summary>
    /// 获取分页的玩家列表。
    /// </summary>
    /// <param name="pageSize">每页的玩家数量。</param>
    /// <param name="pageIndex">当前页的索引，从0开始。</param>
    /// <returns>指定页的玩家会话列表。</returns>
    public List<IPlayerSession> GetPageList(int pageSize, int pageIndex)
    {
        var result = SessionMap.Values.OrderBy(m => m.CreateTime)
                              .Where(m => ActorManager.HasActor(m.PlayerId))
                              .Skip(pageIndex * pageSize)
                              .Take(pageSize)
                              .ToList();
        return result;
    }

    /// <summary>
    /// 按玩家ID踢下线，移除其会话。
    /// </summary>
    /// <param name="playerId">要踢掉的玩家ID。</param>
    public void KickOffLineByPlayerId(long playerId)
    {
        var roleSession = Get(m => m.PlayerId == playerId);
        if (roleSession != null)
        {
            if (SessionMap.TryRemove(roleSession.SessionId, out var value) && ActorManager.HasActor(roleSession.PlayerId))
            {
                EventDispatcher.Dispatch(roleSession.PlayerId, new SessionRemovedEventArgs());
            }

            SetPlayerRouteOffline(roleSession.PlayerId);
        }
    }

    /// <summary>
    /// 根据玩家ID获取对应的会话对象。
    /// 会话对象必须已经存在才会返回。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <returns>对应的会话对象，如果不存在则返回null。</returns>
    public IPlayerSession GetByPlayerId(long playerId)
    {
        var roleSession = Get(m => m.PlayerId == playerId);
        if (roleSession != null && ActorManager.HasActor(roleSession.PlayerId))
        {
            return roleSession;
        }

        return roleSession;
    }

    /// <summary>
    /// 根据会话ID获取连接的会话对象。
    /// </summary>
    /// <param name="sessionId">会话ID。</param>
    /// <returns>对应的会话对象，如果不存在则返回null。</returns>
    public IPlayerSession Get(string sessionId)
    {
        SessionMap.TryGetValue(sessionId, out var value);
        return value;
    }

    /// <summary>
    /// 根据指定的查询条件获取会话对象。
    /// </summary>
    /// <param name="predicate">查询条件的委托。</param>
    /// <returns>符合条件的会话对象，如果不存在则返回null。</returns>
    public IPlayerSession Get(Func<IPlayerSession, bool> predicate)
    {
        return SessionMap.Values.FirstOrDefault(predicate);
    }

    /// <summary>
    /// 根据指定的查询条件获取会话对象列表。
    /// </summary>
    /// <param name="predicate">查询条件的委托。</param>
    /// <returns>符合条件的会话对象列表。</returns>
    public List<IPlayerSession> GetList(Func<IPlayerSession, bool> predicate)
    {
        return SessionMap.Values.Where(predicate).ToList();
    }

    /// <summary>
    /// 移除指定会话ID的玩家。
    /// </summary>
    /// <param name="sessionId">要移除的会话ID。</param>
    /// <returns>被移除的会话对象，如果不存在则返回null。</returns>
    public IPlayerSession Remove(string sessionId)
    {
        if (SessionMap.TryRemove(sessionId, out var value) && ActorManager.HasActor(value.PlayerId))
        {
            EventDispatcher.Dispatch(value.PlayerId, new SessionRemovedEventArgs());
        }

        if (value != null && value.PlayerId > 0 && !HasAnySession(value.PlayerId))
        {
            SetPlayerRouteOffline(value.PlayerId);
        }

        return value;
    }

    /// <summary>
    /// 判断玩家在会话表中是否仍有剩余会话（断线路由守卫）：
    /// 旧连接迟到的 OnDisconnected 回调 Remove(旧会话) 时，若新会话（顶号/重连后绑定）仍在场，
    /// 则不得将玩家路由打成离线。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <returns>是否仍有剩余会话。</returns>
    private bool HasAnySession(long playerId)
    {
        return Get(m => m.PlayerId == playerId) != null;
    }

    /// <summary>
    /// 移除所有在线玩家的会话。
    /// </summary>
    /// <returns>一个表示异步操作的任务。</returns>
    public Task RemoveAllAsync()
    {
        foreach (var session in SessionMap.Values)
        {
            if (ActorManager.HasActor(session.PlayerId))
            {
                EventDispatcher.Dispatch(session.PlayerId, new SessionRemovedEventArgs());
            }

            if (session.PlayerId > 0)
            {
                SetPlayerRouteOffline(session.PlayerId);
            }
        }

        SessionMap.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取指定会话ID的网络连接通道。
    /// </summary>
    /// <param name="sessionId">会话ID。</param>
    /// <returns>对应的网络连接通道，如果不存在则返回null。</returns>
    public INetworkChannel GetChannel(string sessionId)
    {
        SessionMap.TryGetValue(sessionId, out var session);
        return session?.WorkChannel;
    }

    /// <summary>
    /// 添加新的连接会话。
    /// </summary>
    /// <param name="session">要添加的会话对象。</param>
    public void Add(IPlayerSession session)
    {
        session.WorkChannel.SetData(GlobalConst.SessionIdKey, session.SessionId);
        SessionMap[session.SessionId] = session;
        if (session.PlayerId > 0)
        {
            SetPlayerRouteOnline(session.PlayerId);
        }
    }

    /// <summary>
    /// 更新会话，处理玩家ID和签名的更新。
    /// 如果玩家ID已在其他设备上登录，则会通知旧会话并关闭其连接。
    /// 同一玩家的并发登录按 <paramref name="playerId" /> 串行化：后进入者把先完成者判为旧会话（顶号），
    /// 任一时刻至多一个绑定生效（C306）。
    /// </summary>
    /// <param name="sessionId">会话ID，用于标识当前会话</param>
    /// <param name="playerId">玩家ID，表示当前会话所关联的玩家</param>
    /// <param name="sign">签名，用于验证会话的唯一性</param>
    /// <param name="duplicateLoginNotifier">
    /// 顶号通知器：向旧会话发送"账号已在其他设备登录"提示。通知必须在 Close 之前内联 await——
    /// 事件派发（Tell 入队）与 Close 存在竞态，异步入队可能晚于断连导致通知丢失；
    /// 顶号提示 RespPrompt 属游戏协议（Proto 在 Core 之上），故由调用方注入构造逻辑。必填参数使漏注入成为编译错误。
    /// </param>
    public async Task UpdateSessionAsync(string sessionId, long playerId, string sign, Func<IPlayerSession, Task> duplicateLoginNotifier)
    {
        // 顶号关键段按 playerId 互斥串行（C306）：读旧会话→通知→关闭→移除→绑定→路由上线必须作为整体执行，
        // 否则并发登录可双活绑定 / 互踢 / 路由版本重复递增（GFX-1018）。
        var loginLock = await AcquireLoginLockAsync(playerId);
        try
        {
            // 获取与玩家ID关联的旧会话；同一会话重登（同 sessionId）不触发顶号，幂等重绑
            var oldSession = GetByPlayerId(playerId);
            if (oldSession != null && oldSession.SessionId != sessionId)
            {
                // 内联 await 顶号通知（须先于 Close，理由见参数注释）
                await duplicateLoginNotifier(oldSession);
                // 清除旧会话的连接数据并关闭连接
                oldSession.WorkChannel.ClearData();
                oldSession.WorkChannel.Close();
                // 这里先移除，等待Disconnected回调断开在移除的话有延迟
                Remove(oldSession.SessionId);
            }

            // 获取当前会话并更新玩家ID和签名
            var session = Get(sessionId);
            if (session.IsNull())
            {
                return;
            }

            session.SetPlayerId(playerId);
            session.SetSign(sign);
            SetPlayerRouteOnline(playerId);
        }
        finally
        {
            ReleaseLoginLock(playerId, loginLock);
        }
    }

    /// <summary>
    /// 获取指定玩家的登录串行锁：等待信号量后复核字典映射仍为本实例，否则让位重取。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <returns>已持有的、且当前仍被 <see cref="PlayerLoginLocks" /> 映射的信号量。</returns>
    /// <remarks>
    /// Mutual-exclusion invariant: a holder that passes the mapping re-check can never be
    /// concurrent with another validated holder of a different instance, because the mapping
    /// for a key can only be removed by a thread that holds the mapped instance (removal
    /// happens inside the critical section, before <c>Release</c>), and a new instance is
    /// only inserted while the key is absent. A waiter whose instance got retired mid-wait
    /// fails the re-check, releases, and retries with the current mapping — a brief liveness
    /// cost with no exclusivity loss.
    /// </remarks>
    private async Task<SemaphoreSlim> AcquireLoginLockAsync(long playerId)
    {
        while (true)
        {
            var semaphore = PlayerLoginLocks.GetOrAdd(playerId, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();

            // 复核：等待期间本条目可能已被持锁者回收并替换，此实例不再代表该玩家的互斥权
            if (PlayerLoginLocks.TryGetValue(playerId, out var mapped) && ReferenceEquals(mapped, semaphore))
            {
                return semaphore;
            }

            semaphore.Release();
        }
    }

    /// <summary>
    /// 释放指定玩家的登录串行锁：先在持锁状态下回收字典条目，再释放信号量。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="semaphore">由 <see cref="AcquireLoginLockAsync" /> 返回的信号量。</param>
    /// <remarks>
    /// Reclaim-while-held: removing the entry before <c>Release</c> guarantees every removal
    /// happens under the semaphore, which is exactly the invariant
    /// <see cref="AcquireLoginLockAsync" /> relies on. The lock map therefore stays bounded to
    /// in-flight logins without idle-detection heuristics — count/waiting-probe reclamation has
    /// an inherent check-then-remove race that can let two different semaphore instances be
    /// treated as authoritative concurrently.
    /// </remarks>
    private void ReleaseLoginLock(long playerId, SemaphoreSlim semaphore)
    {
        // 持锁期间映射不可能被他人改写（移除只发生在持锁者手里、插入要求键缺席，见 AcquireLoginLockAsync
        // 不变式），故按键移除即精确回收本实例；随后释放信号量，唤醒的等待方经映射复核让位重取
        PlayerLoginLocks.TryRemove(playerId, out _);
        semaphore.Release();
    }

    /// <summary>
    /// 获取玩家路由快照（内存态）。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="snapshot">路由快照。</param>
    /// <returns>是否命中。</returns>
    public bool TryGetPlayerRoute(long playerId, out SessionRouteSnapshot snapshot)
    {
        return PlayerRouteMap.TryGetValue(playerId, out snapshot);
    }

    /// <summary>
    /// 将玩家标记为在线并更新路由信息（内存态）。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="serverType">服务器类型，默认当前服务。</param>
    /// <param name="serverId">服务器ID，默认当前服务。</param>
    public void SetPlayerRouteOnline(long playerId, string serverType = null, int? serverId = null)
    {
        if (playerId <= 0)
        {
            return;
        }

        var resolvedServerType = string.IsNullOrWhiteSpace(serverType)
                                     ? (GlobalSettings.CurrentSetting?.ServerType ?? GameServerConst.Game.Name)
                                     : serverType;
        var resolvedServerId = serverId ?? (GlobalSettings.CurrentSetting?.ServerId ?? GameServerConst.Game.Id);

        var snapshot = PlayerRouteMap.AddOrUpdate(
            playerId,
            _ => SessionRouteSnapshot.Online(playerId, resolvedServerType, resolvedServerId, LocalDiscoveryInstanceId, 1),
            (_, old) => SessionRouteSnapshot.Online(playerId, resolvedServerType, resolvedServerId, LocalDiscoveryInstanceId, old.Version + 1));

        // 内存态落定后再调控制库钩子；失败 swallow（warning），主链路不挂。
        FireSyncUpsert(playerId, resolvedServerType, snapshot);
    }

    /// <summary>
    /// 将玩家标记为离线（内存态）。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    public void SetPlayerRouteOffline(long playerId)
    {
        if (playerId <= 0)
        {
            return;
        }

        PlayerRouteMap.AddOrUpdate(
            playerId,
            _ => SessionRouteSnapshot.Offline(playerId, 1),
            (_, old) => SessionRouteSnapshot.Offline(playerId, old.Version + 1));

        // 内存态落定后再调控制库钩子；失败 swallow（warning），主链路不挂。
        FireSyncDelete(playerId);
    }

    private void FireSyncUpsert(long playerId, string resolvedServerType, SessionRouteSnapshot snapshot)
    {
        var syncTarget = _playerRouteSyncTarget;
        if (syncTarget == null || ReferenceEquals(syncTarget, NullPlayerRouteSyncTarget.Instance))
        {
            return;
        }

        // 实例 Id 直取本进程发现层身份（装配缝注入，与心跳自描述符同源）；仅观察形态（无发现身份）跳过
        // upsert 钩子——该进程不写心跳，行对外天然不可达，写入只会制造按 offline 处理的悬空行。
        var instanceId = LocalDiscoveryInstanceId;
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        // 丢弃的是完全自愈的 SafeAsync 任务：异步失败也被观测（catch + warning），不进 UnobservedTaskException
        _ = SyncUpsertSafeAsync(syncTarget, playerId, instanceId, resolvedServerType, snapshot);
    }

    /// <summary>
    /// 路由同步 upsert 钩子的全程观测包装：同步与异步失败统一 swallow + warning，主链路不挂。
    /// </summary>
    /// <remarks>
    /// Fully-observed wrapper for the route-sync upsert hook: both synchronous and
    /// asynchronous failures are swallowed with a warning log, so the discarded task
    /// never surfaces as an <c>UnobservedTaskException</c> and the in-memory
    /// <c>PlayerRouteMap</c> stays authoritative until the next SetOnline converges.
    /// </remarks>
    /// <param name="syncTarget">控制库同步目标 / The control-database sync target</param>
    /// <param name="playerId">玩家ID / The player id</param>
    /// <param name="instanceId">钩子实例标识 / The hook instance identifier</param>
    /// <param name="role">归属服务类型 / The owning server type</param>
    /// <param name="snapshot">会话路由快照 / The session route snapshot</param>
    private static async Task SyncUpsertSafeAsync(IPlayerRouteSyncTarget syncTarget, long playerId, string instanceId, string role, SessionRouteSnapshot snapshot)
    {
        try
        {
            await syncTarget.UpsertAsync(new PlayerRouteRecord { PlayerId = playerId, InstanceId = instanceId, Role = role, Version = snapshot.Version, });
        }
        catch (Exception exception)
        {
            LogHelper.Warning(exception, "[PlayerSessionManager] player_route upsert hook failed for player {playerId}; the local PlayerRouteMap stays authoritative and the next SetOnline converges", playerId);
        }
    }

    private void FireSyncDelete(long playerId)
    {
        var syncTarget = _playerRouteSyncTarget;
        if (syncTarget == null || ReferenceEquals(syncTarget, NullPlayerRouteSyncTarget.Instance))
        {
            return;
        }

        // 同 upsert：丢弃完全自愈的 SafeAsync 任务，异步失败同样被观测
        _ = SyncDeleteSafeAsync(syncTarget, playerId);
    }

    /// <summary>
    /// 路由同步 delete 钩子的全程观测包装：同步与异步失败统一 swallow + warning，主链路不挂。
    /// </summary>
    /// <remarks>
    /// Fully-observed wrapper for the route-sync delete hook: both synchronous and
    /// asynchronous failures are swallowed with a warning log, so the discarded task
    /// never surfaces as an <c>UnobservedTaskException</c>.
    /// </remarks>
    /// <param name="syncTarget">控制库同步目标 / The control-database sync target</param>
    /// <param name="playerId">玩家ID / The player id</param>
    private static async Task SyncDeleteSafeAsync(IPlayerRouteSyncTarget syncTarget, long playerId)
    {
        try
        {
            await syncTarget.DeleteAsync(playerId);
        }
        catch (Exception exception)
        {
            LogHelper.Warning(exception, "[PlayerSessionManager] player_route delete hook failed for player {playerId}", playerId);
        }
    }
}