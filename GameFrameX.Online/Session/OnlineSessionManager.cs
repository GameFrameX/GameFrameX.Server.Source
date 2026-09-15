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
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Session;

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;

/// <summary>
/// 会话生命周期管理器（vault:C3 S2.4：Session 状态推进的单写者——本类之外不得直改
/// <see cref="OnlineSession.State"/>（Token 签发/轮换/终态化归 <see cref="OnlineSessionTokenService"/>））。
/// <para>
/// 维护约束：状态推进严格按 Created → Authenticated → Connected → Active 主线，断线入
/// <see cref="OnlineSessionState.Reconnecting"/>（带重连窗口），超窗/登出/服务端关闭转终态——
/// 无永久 Reconnecting（VC-2.15）；每次推进发布对应会话事件（VC-2.16）；
/// 终态不可逆，终态后的一切操作映射 <see cref="OnlineErrorCode.SessionInvalid"/>。
/// </para>
/// </summary>
public sealed class OnlineSessionManager
{
    /// <summary>会话存储。</summary>
    private readonly IOnlineSessionStore _store;

    /// <summary>事件发布出口。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>
    /// 初始化 <see cref="OnlineSessionManager"/>。
    /// </summary>
    /// <param name="store">会话存储。</param>
    /// <param name="eventPublisher">事件发布出口（生命周期审计事件）。</param>
    public OnlineSessionManager(IOnlineSessionStore store, IOnlineEventPublisher eventPublisher)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    /// <summary>
    /// 标记会话已建立连接（Authenticated → Connected；绑定连接通道标识）。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="connectionId">连接通道标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<OnlineResult<bool>> MarkConnectedAsync(string sessionId, string connectionId, CancellationToken cancellationToken = default)
    {
        var failure = ValidateSessionId(sessionId);
        if (failure != null)
        {
            return failure;
        }

        if (string.IsNullOrEmpty(connectionId))
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "连接通道标识不得为空");
        }

        var session = await _store.FindAsync(sessionId, cancellationToken);
        if (session == null)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ResourceNotFound, "会话不存在");
        }

        if (session.State.IsTerminal())
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.SessionInvalid, "会话已进入终态");
        }

        if (session.State == OnlineSessionState.Created)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.StateNotReady, "会话尚未通过鉴权，不能建立连接");
        }

        if (session.State != OnlineSessionState.Authenticated)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.StateOperationForbidden, "会话已建立过连接，不得重复标记");
        }

        session.State = OnlineSessionState.Connected;
        session.ConnectionId = connectionId;
        session.ConnectedAtTime = Now();
        await _store.AddOrUpdateAsync(session, cancellationToken);
        await _eventPublisher.PublishAsync(OnlineSessionEvents.Create(OnlineSessionEvents.SessionConnected, session, "connection established"), cancellationToken);
        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 标记会话进入活跃交互（Connected → Active；Active 下幂等成功）。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<OnlineResult<bool>> MarkActiveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var failure = ValidateSessionId(sessionId);
        if (failure != null)
        {
            return failure;
        }

        var session = await _store.FindAsync(sessionId, cancellationToken);
        if (session == null)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ResourceNotFound, "会话不存在");
        }

        if (session.State.IsTerminal())
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.SessionInvalid, "会话已进入终态");
        }

        if (session.State == OnlineSessionState.Active)
        {
            return OnlineResult<bool>.Ok(true);
        }

        if (session.State != OnlineSessionState.Connected)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.StateNotReady, "会话尚未建立连接，不能进入活跃");
        }

        session.State = OnlineSessionState.Active;
        await _store.AddOrUpdateAsync(session, cancellationToken);
        await _eventPublisher.PublishAsync(OnlineSessionEvents.Create(OnlineSessionEvents.SessionActive, session, "session active"), cancellationToken);
        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 记录会话心跳（活跃可交互态内刷新最近心跳时刻；Idle 判定输入）。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<OnlineResult<bool>> HeartbeatAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var failure = ValidateSessionId(sessionId);
        if (failure != null)
        {
            return failure;
        }

        var session = await _store.FindAsync(sessionId, cancellationToken);
        if (session == null)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ResourceNotFound, "会话不存在");
        }

        if (session.State.IsTerminal())
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.SessionInvalid, "会话已进入终态");
        }

        if (!session.State.IsLive())
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.StateNotReady, "会话尚未建立连接，心跳无意义");
        }

        session.LastHeartbeatAtTime = Now();
        await _store.AddOrUpdateAsync(session, cancellationToken);
        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 标记会话断线（活跃可交互态 → Reconnecting，开启重连窗口；窗口内重复断线刷新窗口）。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="reconnectWindowSeconds">重连窗口（秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<OnlineResult<bool>> MarkDisconnectedAsync(string sessionId, long reconnectWindowSeconds, CancellationToken cancellationToken = default)
    {
        var failure = ValidateSessionId(sessionId);
        if (failure != null)
        {
            return failure;
        }

        if (reconnectWindowSeconds <= 0)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "重连窗口必须为正数（秒）");
        }

        var session = await _store.FindAsync(sessionId, cancellationToken);
        if (session == null)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ResourceNotFound, "会话不存在");
        }

        if (session.State.IsTerminal())
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.SessionInvalid, "会话已进入终态");
        }

        if (!session.State.IsLive())
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.StateNotReady, "会话尚未建立连接，无断线语义");
        }

        var now = Now();
        if (session.State == OnlineSessionState.Reconnecting)
        {
            session.ReconnectDeadlineAtTime = now + reconnectWindowSeconds * 1000L;
            await _store.AddOrUpdateAsync(session, cancellationToken);
            return OnlineResult<bool>.Ok(true);
        }

        session.State = OnlineSessionState.Reconnecting;
        session.DisconnectAtTime = now;
        session.ReconnectDeadlineAtTime = now + reconnectWindowSeconds * 1000L;
        session.ConnectionId = string.Empty;
        await _store.AddOrUpdateAsync(session, cancellationToken);
        await _eventPublisher.PublishAsync(OnlineSessionEvents.Create(OnlineSessionEvents.SessionReconnecting, session, "connection lost, reconnect window opened"), cancellationToken);
        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 标记重连成功（Reconnecting → Connected；重接连接通道并关闭重连窗口）。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="connectionId">新的连接通道标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<OnlineResult<bool>> MarkReconnectedAsync(string sessionId, string connectionId, CancellationToken cancellationToken = default)
    {
        var failure = ValidateSessionId(sessionId);
        if (failure != null)
        {
            return failure;
        }

        if (string.IsNullOrEmpty(connectionId))
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "连接通道标识不得为空");
        }

        var session = await _store.FindAsync(sessionId, cancellationToken);
        if (session == null)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ResourceNotFound, "会话不存在");
        }

        if (session.State.IsTerminal())
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.SessionInvalid, "会话已进入终态");
        }

        if (session.State != OnlineSessionState.Reconnecting)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.StateOperationForbidden, "会话未处于重连窗口内");
        }

        session.State = OnlineSessionState.Connected;
        session.ConnectionId = connectionId;
        session.ReconnectDeadlineAtTime = 0;
        await _store.AddOrUpdateAsync(session, cancellationToken);
        await _eventPublisher.PublishAsync(OnlineSessionEvents.Create(OnlineSessionEvents.SessionConnected, session, "reconnected"), cancellationToken);
        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 关闭会话（任意非终态 → 终态；终态下幂等成功）。
    /// <para>
    /// 终态映射：<see cref="OnlineSessionCloseReason.KickedByOperator"/> /
    /// <see cref="OnlineSessionCloseReason.ReplacedByNewSession"/> → <see cref="OnlineSessionState.Kicked"/>；
    /// <see cref="OnlineSessionCloseReason.Expired"/> → <see cref="OnlineSessionState.Expired"/>；其余 → Closed。
    /// </para>
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="closeReason">终态原因码。</param>
    /// <param name="reason">原因描述（审计承载）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<OnlineResult<bool>> CloseAsync(string sessionId, OnlineSessionCloseReason closeReason, string reason, CancellationToken cancellationToken = default)
    {
        var failure = ValidateSessionId(sessionId);
        if (failure != null)
        {
            return failure;
        }

        var session = await _store.FindAsync(sessionId, cancellationToken);
        if (session == null)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ResourceNotFound, "会话不存在");
        }

        if (session.State.IsTerminal())
        {
            return OnlineResult<bool>.Ok(true);
        }

        OnlineSessionState targetState;
        string eventType;
        if (closeReason == OnlineSessionCloseReason.KickedByOperator || closeReason == OnlineSessionCloseReason.ReplacedByNewSession)
        {
            targetState = OnlineSessionState.Kicked;
            eventType = OnlineSessionEvents.SessionKicked;
        }
        else if (closeReason == OnlineSessionCloseReason.Expired)
        {
            targetState = OnlineSessionState.Expired;
            eventType = OnlineSessionEvents.SessionExpired;
        }
        else
        {
            targetState = OnlineSessionState.Closed;
            eventType = OnlineSessionEvents.SessionClosed;
        }

        session.State = targetState;
        session.CloseReason = closeReason;
        session.ClosedAtTime = Now();
        session.CurrentTokenHash = string.Empty;
        session.ConnectionId = string.Empty;
        await _store.AddOrUpdateAsync(session, cancellationToken);
        await _eventPublisher.PublishAsync(OnlineSessionEvents.Create(eventType, session, reason ?? string.Empty), cancellationToken);
        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 清理重连窗口超时的会话（Reconnecting 超窗 → Closed/<see cref="OnlineSessionCloseReason.ReconnectWindowExpired"/>；
    /// VC-2.15：无永久 Reconnecting）。
    /// </summary>
    /// <param name="nowUnixMilliseconds">判定基准时刻（Unix 毫秒；0 = 当前时刻，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次清理的会话数。</returns>
    public async Task<int> SweepAsync(long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : Now();
        var swept = 0;
        var nonTerminal = await _store.ListNonTerminalAsync(cancellationToken);
        foreach (var session in nonTerminal)
        {
            if (session.State == OnlineSessionState.Reconnecting && session.ReconnectDeadlineAtTime > 0 && now > session.ReconnectDeadlineAtTime)
            {
                var outcome = await CloseAsync(session.Id, OnlineSessionCloseReason.ReconnectWindowExpired, "reconnect window expired", cancellationToken);
                if (outcome.IsSuccess)
                {
                    swept++;
                }
            }
        }

        return swept;
    }

    /// <summary>
    /// 校验会话标识非空。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>失败结果；合法返回 null。</returns>
    private static OnlineResult<bool> ValidateSessionId(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "会话标识不得为空");
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
