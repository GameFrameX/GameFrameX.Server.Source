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

using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Identity;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Tokens;

/// <summary>
/// 会话 Token 服务（vault:C3 S2.4：<see cref="IOnlineSessionTokenContract"/> 的 C94 默认实现）。
/// <para>
/// 维护约束（红线）：Token 为 256 位随机不透明串——明文只存调用方，服务端只存 SHA-256 指纹（防拖库重放）；
/// 刷新 = 原子轮换——旧指纹即刻失效、<see cref="OnlineSession.TokenGeneration"/> 递增，重放旧 Token 判
/// <see cref="OnlineErrorCode.TokenRevoked"/>（VC-2.2/VC-2.12）；吊销/踢下线后原 Token 一律失效（VC-2.3，
/// 踢下线审计事件必须含 SessionId 与 Reason）；终态会话指纹清空。契约 DTO 形状承载不了错误码的失败经
/// <see cref="OnlineServiceException"/> 抛出，由宿主装配层映射为响应信封。
/// </para>
/// </summary>
public sealed class OnlineSessionTokenService : IOnlineSessionTokenContract
{
    /// <summary>会话存储。</summary>
    private readonly IOnlineSessionStore _store;

    /// <summary>事件发布出口。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>
    /// 变更串行门（原子轮换保证：签发/刷新/吊销/踢下线/清理互斥，防止并发双写产生即刻失效的新 Token）。
    /// ponytail: 单进程信号量——多进程部署时依赖装配层按会话归属单写者路由，跨进程原子性由持久化存储的 CAS 实现。
    /// </summary>
    private readonly SemaphoreSlim _mutationGate = new SemaphoreSlim(1, 1);

    /// <summary>
    /// 初始化 <see cref="OnlineSessionTokenService"/>。
    /// </summary>
    /// <param name="store">会话存储。</param>
    /// <param name="eventPublisher">事件发布出口（生命周期与轮换审计事件）。</param>
    public OnlineSessionTokenService(IOnlineSessionStore store, IOnlineEventPublisher eventPublisher)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    /// <summary>
    /// 签发会话 Token（C93 契约入口：按默认 LatestWins 多端策略开新会话）。
    /// </summary>
    /// <param name="request">签发请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>签发结果（Token、会话标识、过期时刻）。</returns>
    public async Task<OnlineTokenIssueResult> IssueAsync(OnlineTokenIssueRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var outcome = await IssueSessionAsync(request.Scope, request.TimeToLiveSeconds, OnlineMultiDevicePolicy.LatestWins, null, null, cancellationToken);
        if (!outcome.IsSuccess)
        {
            throw new OnlineServiceException(outcome.Code, outcome.Message);
        }

        return new OnlineTokenIssueResult
        {
            Token = outcome.Data.Token,
            SessionId = outcome.Data.Session.Id,
            ExpiresAtTime = outcome.Data.ExpiresAtTime,
        };
    }

    /// <summary>
    /// 刷新会话 Token（原 Token 随刷新即刻失效；重放原 Token 抛 <see cref="OnlineErrorCode.TokenRevoked"/>）。
    /// </summary>
    /// <param name="request">刷新请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>刷新结果（新 Token、新过期时刻）。</returns>
    public async Task<OnlineTokenRefreshResult> RefreshAsync(OnlineTokenRefreshRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var outcome = await RefreshSessionTokenAsync(request.Scope, request.Token, null, cancellationToken);
        if (!outcome.IsSuccess)
        {
            throw new OnlineServiceException(outcome.Code, outcome.Message);
        }

        return outcome.Data;
    }

    /// <summary>
    /// 吊销会话 Token（目标会话不存在或已终态时 <c>Revoked</c> 为 <c>false</c>，幂等不报错）。
    /// </summary>
    /// <param name="request">吊销请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>吊销结果。</returns>
    public async Task<OnlineTokenRevokeResult> RevokeAsync(OnlineTokenRevokeRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var session = await FindLiveBySessionIdAsync(request.SessionId, request.Scope, requireLive: false, cancellationToken);
            if (session == null || session.State.IsTerminal())
            {
                return new OnlineTokenRevokeResult
                {
                    Revoked = false,
                };
            }

            await CloseSessionAsync(session, OnlineSessionState.Closed, OnlineSessionCloseReason.Revoked, OnlineSessionEvents.SessionClosed, request.Reason, null, cancellationToken);
            return new OnlineTokenRevokeResult
            {
                Revoked = true,
            };
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <summary>
    /// 踢下线（语义 = 吊销 + 审计通知；被踢端后续请求映射 <see cref="OnlineErrorCode.SessionInvalid"/>）。
    /// </summary>
    /// <param name="request">踢下线请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>踢下线结果（目标会话不存在或已终态时 <c>Kicked</c> 为 <c>false</c>，幂等不报错）。</returns>
    public async Task<OnlineTokenKickResult> KickAsync(OnlineTokenKickRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var session = await FindLiveBySessionIdAsync(request.SessionId, request.Scope, requireLive: false, cancellationToken);
            if (session == null || session.State.IsTerminal())
            {
                return new OnlineTokenKickResult
                {
                    Kicked = false,
                };
            }

            await CloseSessionAsync(session, OnlineSessionState.Kicked, OnlineSessionCloseReason.KickedByOperator, OnlineSessionEvents.SessionKicked, request.Reason, null, cancellationToken);
            return new OnlineTokenKickResult
            {
                Kicked = true,
            };
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <summary>
    /// 开启新会话（vault:C3 S2.4：签发即建会话 + 多端策略裁决；VC-2.4）。
    /// <para>
    /// 裁决口径：SingleDevice——已存在活跃会话时拒绝新登录（5xxx）；LatestWins——关闭全部活跃旧会话
    /// （Kicked + <see cref="OnlineSessionCloseReason.ReplacedByNewSession"/>，跨区服一并顶替）；
    /// Coexist——保留旧会话并存。新会话落 Authenticated 态（Token 已签发，连接未建立）。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="timeToLiveSeconds">Token 有效期（秒）。</param>
    /// <param name="multiDevicePolicy">多端登录策略。</param>
    /// <param name="deviceId">登录设备标识（可空）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话开启结果（含明文 Token 与被顶替会话列表）。</returns>
    public async Task<OnlineResult<OnlineSessionStartResult>> IssueSessionAsync(OnlineScope scope, long timeToLiveSeconds, OnlineMultiDevicePolicy multiDevicePolicy, string deviceId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (scope.PlayerId <= 0)
        {
            return OnlineResult<OnlineSessionStartResult>.Fail(OnlineErrorCode.ScopeMissing, "会话签发必须携带玩家主体位（PlayerId）");
        }

        if (timeToLiveSeconds <= 0)
        {
            return OnlineResult<OnlineSessionStartResult>.Fail(OnlineErrorCode.ParameterInvalid, "Token 有效期必须为正数（秒）");
        }

        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var replacedSessionIds = new List<string>();
            var activeSessions = await _store.ListActiveByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
            if (activeSessions.Count > 0)
            {
                switch (multiDevicePolicy)
                {
                    case OnlineMultiDevicePolicy.SingleDevice:
                    {
                        return OnlineResult<OnlineSessionStartResult>.Fail(OnlineErrorCode.StateOperationForbidden, "已存在活跃会话，单设备策略拒绝新登录");
                    }

                    case OnlineMultiDevicePolicy.LatestWins:
                    {
                        foreach (var activeSession in activeSessions)
                        {
                            await CloseSessionAsync(activeSession, OnlineSessionState.Kicked, OnlineSessionCloseReason.ReplacedByNewSession, OnlineSessionEvents.SessionKicked, "replaced by new session", correlationId, cancellationToken);
                            replacedSessionIds.Add(activeSession.Id);
                        }

                        break;
                    }

                    case OnlineMultiDevicePolicy.Coexist:
                    {
                        break;
                    }

                    default:
                    {
                        return OnlineResult<OnlineSessionStartResult>.Fail(OnlineErrorCode.ParameterInvalid, "未知多端登录策略：" + (int)multiDevicePolicy);
                    }
                }
            }

            var now = Now();
            var token = GenerateToken();
            var session = new OnlineSession
            {
                Id = "sess-" + Guid.NewGuid().ToString("N"),
                TenantId = scope.TenantId,
                AppId = scope.AppId,
                ServerId = scope.ServerId,
                PlayerId = scope.PlayerId,
                DeviceId = deviceId ?? string.Empty,
                ConnectionId = string.Empty,
                State = OnlineSessionState.Authenticated,
                MultiDevicePolicy = multiDevicePolicy,
                CurrentTokenHash = HashToken(token),
                TokenGeneration = 1,
                TokenTimeToLiveSeconds = timeToLiveSeconds,
                TokenExpiresAtTime = now + timeToLiveSeconds * 1000L,
                CreatedAtTime = now,
                AuthenticatedAtTime = now,
                CloseReason = OnlineSessionCloseReason.None,
            };
            await _store.AddOrUpdateAsync(session, cancellationToken);
            await _eventPublisher.PublishAsync(OnlineSessionEvents.Create(OnlineSessionEvents.SessionAuthenticated, session, "token issued", correlationId), cancellationToken);

            return OnlineResult<OnlineSessionStartResult>.Ok(new OnlineSessionStartResult
            {
                Session = session,
                Token = token,
                ExpiresAtTime = session.TokenExpiresAtTime,
                ReplacedSessionIds = replacedSessionIds,
            });
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <summary>
    /// 刷新会话 Token（原子轮换：旧指纹即刻失效，按原有效期滑动续期）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="token">待刷新的原 Token 明文。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>刷新结果（新 Token 与新过期时刻）。</returns>
    public async Task<OnlineResult<OnlineTokenRefreshResult>> RefreshSessionTokenAsync(OnlineScope scope, string token, string correlationId = null, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var validation = await ValidateTokenAsync(token, cancellationToken);
            if (!validation.IsSuccess)
            {
                return OnlineResult<OnlineTokenRefreshResult>.Fail(validation.Code, validation.Message);
            }

            var session = validation.Data;
            var scopeFailure = EnsureScopeMatch(session, scope);
            if (scopeFailure != null)
            {
                return scopeFailure;
            }

            var newToken = GenerateToken();
            var now = Now();
            session.TokenGeneration++;
            session.CurrentTokenHash = HashToken(newToken);
            session.TokenExpiresAtTime = now + session.TokenTimeToLiveSeconds * 1000L;
            await _store.AddOrUpdateAsync(session, cancellationToken);
            await _eventPublisher.PublishAsync(OnlineSessionEvents.Create(OnlineSessionEvents.SessionTokenRefreshed, session, "token rotated", correlationId), cancellationToken);

            return OnlineResult<OnlineTokenRefreshResult>.Ok(new OnlineTokenRefreshResult
            {
                Token = newToken,
                ExpiresAtTime = session.TokenExpiresAtTime,
            });
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <summary>
    /// 校验 Token 并返回所属会话（读路径：不推进状态、不落任何写）。
    /// </summary>
    /// <param name="token">Token 明文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>所属会话；失败码——指纹不存在/已轮换 = <see cref="OnlineErrorCode.TokenRevoked"/>，
    /// 终态 = <see cref="OnlineErrorCode.SessionInvalid"/>，已到期 = <see cref="OnlineErrorCode.TokenExpired"/>。</returns>
    public async Task<OnlineResult<OnlineSession>> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
        {
            return OnlineResult<OnlineSession>.Fail(OnlineErrorCode.ParameterInvalid, "Token 不得为空");
        }

        var session = await _store.FindByTokenHashAsync(HashToken(token), cancellationToken);
        if (session == null)
        {
            return OnlineResult<OnlineSession>.Fail(OnlineErrorCode.TokenRevoked, "Token 不存在或已轮换失效");
        }

        if (session.State.IsTerminal())
        {
            return OnlineResult<OnlineSession>.Fail(OnlineErrorCode.SessionInvalid, "会话已进入终态");
        }

        if (Now() > session.TokenExpiresAtTime)
        {
            return OnlineResult<OnlineSession>.Fail(OnlineErrorCode.TokenExpired, "Token 已到期未刷新");
        }

        return OnlineResult<OnlineSession>.Ok(session);
    }

    /// <summary>
    /// 清理过期 Token 的非终态会话（转 <see cref="OnlineSessionState.Expired"/>；VC-2.14 重启/到期失效兜底）。
    /// </summary>
    /// <param name="nowUnixMilliseconds">判定基准时刻（Unix 毫秒；0 = 当前时刻，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次清理的会话数。</returns>
    public async Task<int> SweepExpiredAsync(long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : Now();
            var swept = 0;
            var nonTerminal = await _store.ListNonTerminalAsync(cancellationToken);
            foreach (var session in nonTerminal)
            {
                if (now > session.TokenExpiresAtTime)
                {
                    await CloseSessionAsync(session, OnlineSessionState.Expired, OnlineSessionCloseReason.Expired, OnlineSessionEvents.SessionExpired, "token expired", null, cancellationToken);
                    swept++;
                }
            }

            return swept;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <summary>
    /// 按会话标识定位会话并做作用域一致性校验。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="scope">生效作用域。</param>
    /// <param name="requireLive">是否要求非终态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话实体；作用域不一致抛 <see cref="OnlineServiceException"/>（3xxx）。</returns>
    private async Task<OnlineSession> FindLiveBySessionIdAsync(string sessionId, OnlineScope scope, bool requireLive, CancellationToken cancellationToken)
    {
        var session = await _store.FindAsync(sessionId, cancellationToken);
        if (session == null)
        {
            return null;
        }

        var scopeFailure = EnsureScopeMatch(session, scope);
        if (scopeFailure != null)
        {
            throw new OnlineServiceException(scopeFailure.Code, scopeFailure.Message);
        }

        if (requireLive && session.State.IsTerminal())
        {
            throw new OnlineServiceException(OnlineErrorCode.SessionInvalid, "会话已进入终态");
        }

        return session;
    }

    /// <summary>
    /// 关闭会话（终态落库 + 指纹清空 + 终态事件发布）。
    /// </summary>
    /// <param name="session">目标会话。</param>
    /// <param name="targetState">目标终态。</param>
    /// <param name="closeReason">终态原因码。</param>
    /// <param name="eventType">终态事件类型。</param>
    /// <param name="reasonText">原因描述（审计承载）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task CloseSessionAsync(OnlineSession session, OnlineSessionState targetState, OnlineSessionCloseReason closeReason, string eventType, string reasonText, string correlationId, CancellationToken cancellationToken)
    {
        session.State = targetState;
        session.CloseReason = closeReason;
        session.ClosedAtTime = Now();
        session.CurrentTokenHash = string.Empty;
        session.ConnectionId = string.Empty;
        await _store.AddOrUpdateAsync(session, cancellationToken);
        await _eventPublisher.PublishAsync(OnlineSessionEvents.Create(eventType, session, reasonText, correlationId), cancellationToken);
    }

    /// <summary>
    /// 校验作用域与会话一致（跨租户/跨 App/跨服一律拒绝）。
    /// </summary>
    /// <param name="session">目标会话。</param>
    /// <param name="scope">生效作用域。</param>
    /// <returns>失败结果；一致返回 null。</returns>
    private static OnlineResult<OnlineTokenRefreshResult> EnsureScopeMatch(OnlineSession session, OnlineScope scope)
    {
        if (session.TenantId != scope.TenantId)
        {
            return OnlineResult<OnlineTokenRefreshResult>.Fail(OnlineErrorCode.CrossTenantDenied, "会话不属于当前租户");
        }

        if (session.AppId != scope.AppId)
        {
            return OnlineResult<OnlineTokenRefreshResult>.Fail(OnlineErrorCode.CrossAppDenied, "会话不属于当前应用");
        }

        if (session.ServerId != scope.ServerId)
        {
            return OnlineResult<OnlineTokenRefreshResult>.Fail(OnlineErrorCode.ServerScopeDenied, "会话不属于当前区服");
        }

        if (scope.PlayerId > 0 && session.PlayerId != scope.PlayerId)
        {
            return OnlineResult<OnlineTokenRefreshResult>.Fail(OnlineErrorCode.ScopeDenied, "会话不属于当前玩家");
        }

        return null;
    }

    /// <summary>
    /// 生成 256 位随机不透明 Token。
    /// </summary>
    /// <returns>Token 明文（"otk-" 前缀 + 64 位十六进制）。</returns>
    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "otk-" + Convert.ToHexString(bytes);
    }

    /// <summary>
    /// 计算 Token SHA-256 指纹。
    /// </summary>
    /// <param name="token">Token 明文。</param>
    /// <returns>指纹十六进制小写。</returns>
    private static string HashToken(string token)
    {
        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(digest).ToLowerInvariant();
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
