//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and other related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws and regulations of the People's Republic of China and international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file at the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations.
using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Foundation.Identity;

namespace GameFrameX.Capability.Online.Foundation.Session;

/// <summary>
/// 会话恢复服务（<see cref="IOnlineSessionReconnectContract"/> 的 Foundation 实现）：
/// Core 物理连接会话与 Online 逻辑会话的协同编排——登录签发（断线前凭据交付）、
/// 新连接恢复（验证 + 连接替换 + 原子轮换）、断线通知（开重连窗）。
/// <para>
/// 维护约束（红线）：不另建第二凭据权威——Token 签发 / 校验 / 轮换全部委托
/// <see cref="OnlineSessionTokenService"/>（服务端仅存 SHA-256 指纹），会话状态推进全部经
/// <see cref="OnlineSessionManager"/>（单写者纪律）；恢复路径在自有串行门内完成
/// 「验证 → 连接替换 → 轮换」三步，使并发重复恢复在后到的验证步显式失败（旧指纹已失效），
/// 不产生半恢复态。
/// </para>
/// </summary>
public sealed class OnlineSessionReconnectService : IOnlineSessionReconnectContract
{
    /// <summary>登录签发 Token 默认有效期（秒；24 小时）。</summary>
    public const int DefaultTokenTimeToLiveSeconds = 86400;

    /// <summary>会话令牌服务（签发 / 校验 / 轮换唯一权威）。</summary>
    private readonly OnlineSessionTokenService _tokens;

    /// <summary>会话生命周期管理器（状态推进单写者）。</summary>
    private readonly OnlineSessionManager _sessions;

    /// <summary>会话存储（按玩家检索活跃会话）。</summary>
    private readonly IOnlineSessionStore _store;

    /// <summary>登录签发 Token 有效期（秒）。</summary>
    private readonly int _tokenTimeToLiveSeconds;

    /// <summary>
    /// 恢复串行门：同一时刻只允许一个「验证 → 连接替换 → 轮换」序列推进（并发重复恢复显式分出先后）。
    /// </summary>
    private readonly SemaphoreSlim _resumeGate = new(1, 1);

    /// <summary>
    /// 初始化 <see cref="OnlineSessionReconnectService"/>。
    /// </summary>
    /// <param name="tokens">会话令牌服务。</param>
    /// <param name="sessions">会话生命周期管理器。</param>
    /// <param name="store">会话存储。</param>
    /// <param name="tokenTimeToLiveSeconds">登录签发 Token 有效期（秒；缺省 24 小时）。</param>
    public OnlineSessionReconnectService(OnlineSessionTokenService tokens, OnlineSessionManager sessions, IOnlineSessionStore store, int tokenTimeToLiveSeconds = DefaultTokenTimeToLiveSeconds)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (tokenTimeToLiveSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenTimeToLiveSeconds), "Token 有效期必须为正数（秒）");
        }

        _tokenTimeToLiveSeconds = tokenTimeToLiveSeconds;
    }

    /// <summary>
    /// 校验作用域与连接标识后，按最新登录胜出策略签发新会话令牌（顶替旧活跃会话），再把新会话标记为已连接到指定连接；任一步失败即返回该步失败码，成功返回令牌、会话标识与过期时间。
    /// </summary>
    /// <remarks>
    /// Validates the scope and connection id, issues a session token with the latest-wins multi-device policy (kicking the
    /// previous active session) using the configured token lifetime, then marks the new session connected on the given
    /// connection. Any failing step returns its failure code; on success the token, session id and expiry are returned.
    /// </remarks>
    /// <param name="scope">登录玩家所属作用域 / Scope of the logging-in player</param>
    /// <param name="connectionId">物理连接通道标识 / Physical connection id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>登录凭据结果，失败时携带失败码 / Login credential result carrying the failure code on failure</returns>
    public async Task<OnlineResult<OnlineSessionLoginCredential>> IssueForLoginAsync(OnlineScope scope, string connectionId, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScopeAndConnection(scope, connectionId);
        if (failure != null)
        {
            return OnlineResult<OnlineSessionLoginCredential>.Fail(failure.Value.Code, failure.Value.Message);
        }

        // LatestWins：完整登录顶替旧活跃会话（Kicked + ReplacedByNewSession，旧 Token 全部失效）。
        var issued = await _tokens.IssueSessionAsync(scope, _tokenTimeToLiveSeconds, OnlineMultiDevicePolicy.LatestWins, null, null, cancellationToken).ConfigureAwait(false);
        if (!issued.IsSuccess)
        {
            return OnlineResult<OnlineSessionLoginCredential>.Fail(issued.Code, issued.Message);
        }

        var session = issued.Data.Session;
        var connected = await _sessions.MarkConnectedAsync(session.Id, connectionId, cancellationToken).ConfigureAwait(false);
        if (!connected.IsSuccess)
        {
            return OnlineResult<OnlineSessionLoginCredential>.Fail(connected.Code, connected.Message);
        }

        return OnlineResult<OnlineSessionLoginCredential>.Ok(new OnlineSessionLoginCredential
        {
            Token = issued.Data.Token,
            SessionId = session.Id,
            ExpiresAtTime = issued.Data.ExpiresAtTime,
        });
    }

    /// <summary>
    /// 作用域为空、连接标识为空或令牌为空时返回参数非法；否则在恢复串行门内依次校验令牌、比对会话作用域、把会话切换到新连接并原子轮换令牌，任一步失败返回对应失败码，成功返回新令牌、玩家、会话标识与过期时间。
    /// </summary>
    /// <remarks>
    /// Returns parameter-invalid when the scope, connection id or token is missing. Otherwise, inside the resume gate, it
    /// validates the token, checks the session scope, moves the session to the new connection and atomically rotates the
    /// token so the old fingerprint is invalidated; concurrent duplicate resumes therefore fail at validation. Any failing
    /// step returns its code; on success the new token, player id, session id and expiry are returned.
    /// </remarks>
    /// <param name="scope">恢复请求声明的作用域 / Scope claimed by the resume request</param>
    /// <param name="token">待恢复会话的令牌 / Token of the session to resume</param>
    /// <param name="connectionId">新的物理连接通道标识 / New physical connection id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>恢复凭据结果，失败时携带失败码 / Resume credential result carrying the failure code on failure</returns>
    public async Task<OnlineResult<OnlineSessionResumeCredential>> ResumeAsync(OnlineScope scope, string token, string connectionId, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            return OnlineResult<OnlineSessionResumeCredential>.Fail(OnlineErrorCode.ParameterInvalid, "缺少作用域");
        }

        if (string.IsNullOrEmpty(connectionId))
        {
            return OnlineResult<OnlineSessionResumeCredential>.Fail(OnlineErrorCode.ParameterInvalid, "连接通道标识不得为空");
        }

        if (string.IsNullOrEmpty(token))
        {
            return OnlineResult<OnlineSessionResumeCredential>.Fail(OnlineErrorCode.ParameterInvalid, "会话凭据不得为空");
        }

        await _resumeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 持有证明：指纹 / 终态 / 到期三重校验（失败码 TokenRevoked / SessionInvalid / TokenExpired）。
            var validation = await _tokens.ValidateTokenAsync(token, cancellationToken).ConfigureAwait(false);
            if (!validation.IsSuccess)
            {
                return OnlineResult<OnlineSessionResumeCredential>.Fail(validation.Code, validation.Message);
            }

            var session = validation.Data;
            var scopeFailure = EnsureScopeMatch(session, scope);
            if (scopeFailure.HasValue)
            {
                return OnlineResult<OnlineSessionResumeCredential>.Fail(scopeFailure.Value.Code, scopeFailure.Value.Message);
            }

            // 连接替换：Authenticated / Connected / Active / Reconnecting → Connected + 新连接标识 + 清窗。
            var resumed = await _sessions.MarkResumedAsync(session.Id, connectionId, cancellationToken).ConfigureAwait(false);
            if (!resumed.IsSuccess)
            {
                return OnlineResult<OnlineSessionResumeCredential>.Fail(resumed.Code, resumed.Message);
            }

            // 原子轮换（一次性消费与更新）：旧指纹即刻失效，重放判 TokenRevoked。
            var rotated = await _tokens.RefreshSessionTokenAsync(scope, token, null, cancellationToken).ConfigureAwait(false);
            if (!rotated.IsSuccess)
            {
                return OnlineResult<OnlineSessionResumeCredential>.Fail(rotated.Code, rotated.Message);
            }

            return OnlineResult<OnlineSessionResumeCredential>.Ok(new OnlineSessionResumeCredential
            {
                NewToken = rotated.Data.Token,
                PlayerId = session.PlayerId,
                SessionId = session.Id,
                ExpiresAtTime = rotated.Data.ExpiresAtTime,
            });
        }
        finally
        {
            _resumeGate.Release();
        }
    }

    /// <summary>
    /// 作用域为空、缺少玩家位或重连窗口非正数时返回参数非法；否则把该玩家所有活跃会话标记为断线并开启重连窗口，跳过状态未就绪或会话无效的会话，遇其它失败立即返回该结果，全部处理完返回 true。
    /// </summary>
    /// <remarks>
    /// Returns parameter-invalid when the scope or player id is missing or the reconnect window is not positive. Otherwise
    /// marks every active session of the player disconnected with the given reconnect window, skipping sessions that report
    /// state-not-ready or session-invalid; any other failure is returned immediately, and true is returned when all are handled.
    /// </remarks>
    /// <param name="scope">断线玩家所属作用域，须带玩家位 / Scope of the disconnected player, must carry a player id</param>
    /// <param name="reconnectWindowSeconds">重连窗口秒数 / Reconnect window in seconds</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>处理结果，成功为 true / Operation result, true on success</returns>
    public async Task<OnlineResult<bool>> NotifyPlayerDisconnectedAsync(OnlineScope scope, long reconnectWindowSeconds, CancellationToken cancellationToken = default)
    {
        if (scope == null || scope.PlayerId <= 0)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "断线通知必须带玩家主体位");
        }

        if (reconnectWindowSeconds <= 0)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "重连窗口必须为正数（秒）");
        }

        var active = await _store.ListActiveByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        foreach (var session in active)
        {
            if (session == null)
            {
                continue;
            }

            // 幂等：非可交互态（Created / 已终态）的会话无断线语义，由管理器显式返回并跳过。
            var outcome = await _sessions.MarkDisconnectedAsync(session.Id, reconnectWindowSeconds, cancellationToken).ConfigureAwait(false);
            if (!outcome.IsSuccess && outcome.Code != OnlineErrorCode.StateNotReady && outcome.Code != OnlineErrorCode.SessionInvalid)
            {
                return outcome;
            }
        }

        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 校验作用域（含玩家主体位）与连接标识非空。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="connectionId">连接标识。</param>
    /// <returns>失败结果；合法返回 null。</returns>
    private static (OnlineErrorCode Code, string Message)? ValidateScopeAndConnection(OnlineScope scope, string connectionId)
    {
        if (scope == null || scope.PlayerId <= 0)
        {
            return (OnlineErrorCode.ParameterInvalid, "会话操作必须带玩家主体位");
        }

        if (string.IsNullOrEmpty(connectionId))
        {
            return (OnlineErrorCode.ParameterInvalid, "连接通道标识不得为空");
        }

        return null;
    }

    /// <summary>
    /// 校验作用域与会话一致（跨租户 / 跨 App / 跨服 / 跨玩家一律拒绝）。
    /// </summary>
    /// <param name="session">目标会话。</param>
    /// <param name="scope">生效作用域。</param>
    /// <returns>失败结果；一致返回 null。</returns>
    private static (OnlineErrorCode Code, string Message)? EnsureScopeMatch(OnlineSession session, OnlineScope scope)
    {
        if (session.TenantId != scope.TenantId)
        {
            return (OnlineErrorCode.CrossTenantDenied, "会话不属于当前租户");
        }

        if (session.AppId != scope.AppId)
        {
            return (OnlineErrorCode.CrossAppDenied, "会话不属于当前应用");
        }

        if (session.ServerId != scope.ServerId)
        {
            return (OnlineErrorCode.ServerScopeDenied, "会话不属于当前区服");
        }

        if (scope.PlayerId > 0 && session.PlayerId != scope.PlayerId)
        {
            return (OnlineErrorCode.ScopeDenied, "会话不属于当前玩家");
        }

        return null;
    }
}
