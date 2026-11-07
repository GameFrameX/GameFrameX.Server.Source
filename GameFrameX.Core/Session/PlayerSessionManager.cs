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
using GameFrameX.Foundation.Extensions;
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
    /// 新组合根可直接构造独立实例并按接口注入。
    /// </summary>
    [Obsolete("静态单例入口过渡保留：请改用注入的 IPlayerSessionManager（组合根经 Override 提供；进程缺省即本实例）。Static singleton entry kept for transition; use the injected IPlayerSessionManager (provided by the composition root; the process default is this very instance).", false)]
    public static readonly IPlayerSessionManager Instance = new PlayerSessionManager();

    /// <summary>创建独立的本地会话管理器。</summary>
    public PlayerSessionManager()
    {
    }

    private readonly ConcurrentDictionary<string, IPlayerSession> SessionMap = new();

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
            Remove(roleSession.SessionId);
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
        return Get(m => m.PlayerId == playerId);
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
        if (SessionMap.TryRemove(sessionId, out var value) && !HasAnySession(value.PlayerId) && ActorManager.HasActor(value.PlayerId))
        {
            EventDispatcher.Dispatch(value.PlayerId, new SessionRemovedEventArgs());
        }

        return value;
    }

    /// <summary>
    /// 判断玩家在会话表中是否仍有剩余会话（旧连接断线守卫）：
    /// 旧连接迟到的 OnDisconnected 回调 Remove(旧会话) 时，若新会话（顶号/重连后绑定）仍在场，
    /// 则不得派发玩家会话移除事件。
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
        // 顶号关键段按 playerId 互斥串行（C306）：读旧会话→通知→关闭→移除→绑定必须作为整体执行，
        // 否则并发登录可双活绑定 / 互踢（GFX-1018）。
        var loginLock = await AcquireLoginLockAsync(playerId);
        try
        {
            var session = Get(sessionId);
            if (session.IsNull())
            {
                return;
            }

            // 获取与玩家ID关联的旧会话；同一会话重登（同 sessionId）不触发顶号，幂等重绑
            var oldSession = GetByPlayerId(playerId);
            if (oldSession != null && oldSession.SessionId != sessionId)
            {
                // 内联 await 顶号通知（须先于 Close，理由见参数注释）
                await duplicateLoginNotifier(oldSession);
                if (!ReferenceEquals(Get(sessionId), session))
                {
                    return;
                }

                // 清除旧会话的连接数据并关闭连接
                oldSession.WorkChannel.ClearData();
                oldSession.WorkChannel.Close();
                // 这里先移除，等待Disconnected回调断开在移除的话有延迟
                Remove(oldSession.SessionId);
            }

            // 获取当前会话并更新玩家ID和签名
            session = Get(sessionId);
            if (session.IsNull())
            {
                return;
            }

            session.SetPlayerId(playerId);
            session.SetSign(sign);
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

}
