// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应开源许可证与规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   Any legal disputes or liabilities arising from secondary development based on this project
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://cnb.cool/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Text;
using System.Text.Json;
using GameFrameX.Foundation.Json;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Idempotency;
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Runtime.AdminApi;

/// <summary>
/// Online admin API 调度器（change C122：action 注册表 + 作用域守卫 + 请求标识 + 幂等包裹 + 双层信封）。
/// <para>
/// 维护约束（横切红线，四端统一语义）：
/// 作用域判定只经 <see cref="OnlineScopeGuard"/>（客户端声称三元组 vs 宿主授权作用域，拒绝码 3002/3003/3004/3005），
/// 禁止 Handler 自行判定；HTTP 状态恒 200，业务错误只进内层 <see cref="OnlineAdminApiResponse.Code"/>
/// （外层信封非 0 会被 Admin 判为基础设施异常）；未知 action → 内层 4002；
/// 副作用命令（kick / revoke_token / mute / 受控四命令）经 <see cref="OnlineIdempotencyService"/> 包裹
/// （ReExecute 策略：失败重试重执行，Replay 原样透传首次响应原文）；grant/revoke 走
/// <see cref="OnlineGrantService"/> 内建幂等（避免双重包裹）；<see cref="OnlineAdminApiResponse.ServerTime"/>
/// 为 Unix 秒（Admin 线缆契约）。
/// </para>
/// </summary>
public sealed class OnlineAdminApiDispatcher
{
    /// <summary>
    /// 宿主（服务集合与授权作用域来源）。
    /// </summary>
    private readonly OnlineRuntimeHost _host;

    /// <summary>
    /// action 注册表（大小写不敏感）。
    /// </summary>
    private readonly Dictionary<string, ActionRegistration> _actions = new Dictionary<string, ActionRegistration>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化 <see cref="OnlineAdminApiDispatcher"/> 并注册全部 action。
    /// </summary>
    /// <param name="host">宿主。</param>
    public OnlineAdminApiDispatcher(OnlineRuntimeHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        new OnlineAdminAssetHandlers(host).Register(this);
        new OnlineAdminSocialHandlers(host).Register(this);
        new OnlineAdminMatchHandlers(host).Register(this);
        new OnlineAdminConsoleHandlers(host).Register(this);
        new OnlineAdminLiveOpsHandlers(host).Register(this);
    }

    /// <summary>
    /// 获取已注册 action 名集合（诊断与测试用）。
    /// </summary>
    public IReadOnlyCollection<string> ActionNames
    {
        get
        {
            return _actions.Keys;
        }
    }

    /// <summary>
    /// 注册 action。
    /// </summary>
    /// <param name="action">action 名（路由末段）。</param>
    /// <param name="handler">处理器（返回业务数据载荷；抛 <see cref="OnlineServiceException"/> 映射协议码）。</param>
    /// <param name="wrapIdempotency">是否经调度器层幂等包裹。</param>
    public void Register(string action, ActionHandler handler, bool wrapIdempotency)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("Action name must not be null or empty.", nameof(action));
        }

        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        _actions[action] = new ActionRegistration(handler, wrapIdempotency);
    }

    /// <summary>
    /// 调度一个 admin 请求（横切：action 解析 → 请求标识 → 作用域守卫 → 幂等包裹 → 双层信封）。
    /// </summary>
    /// <param name="action">action 名（路由末段）。</param>
    /// <param name="bodyJson">原始请求体 JSON 字符串。</param>
    /// <returns>HTTP 响应体原文（外层信封 JSON；HTTP 状态恒 200）。</returns>
    public async Task<string> DispatchAsync(string action, string bodyJson)
    {
        string requestId;
        OnlineAdminApiRequest request = null;
        try
        {
            request = new OnlineAdminApiRequest(action, bodyJson);
        }
        catch (JsonException exception)
        {
            requestId = "req-" + Guid.NewGuid().ToString("N");
            return OnlineAdminApiEnvelope.Write(OnlineAdminApiResponse.Fail(OnlineErrorCode.ParameterInvalid, "Malformed JSON body: " + exception.Message, requestId, NowSeconds()));
        }

        requestId = request.ReadRequestId();
        if (string.IsNullOrEmpty(requestId))
        {
            requestId = "req-" + Guid.NewGuid().ToString("N");
        }

        if (!_actions.TryGetValue(request.Action ?? string.Empty, out var registration))
        {
            return WriteFail(OnlineErrorCode.ResourceNotFound, "Unknown admin action: " + request.Action, requestId, request);
        }

        if (!request.TryReadClaimedScope(out var tenantId, out var appId, out var serverId))
        {
            return WriteFail(OnlineErrorCode.ScopeMissing, "Scope triple (TenantId/AppId/ServerId) missing or invalid in request body.", requestId, request);
        }

        var claimedScope = new OnlineScope(tenantId, appId, serverId);
        var deniedCode = OnlineScopeGuard.Validate(claimedScope, _host.AuthorizedScope);
        if (deniedCode.HasValue)
        {
            return WriteFail(deniedCode.Value, "Scope denied for tenant " + tenantId + ", app " + appId + ", server " + serverId + ".", requestId, request);
        }

        if (registration.WrapIdempotency)
        {
            return await DispatchWithIdempotencyAsync(registration, request, claimedScope, requestId).ConfigureAwait(false);
        }

        return await DispatchDirectAsync(registration.Handler, request, claimedScope, requestId).ConfigureAwait(false);
    }

    /// <summary>
    /// 直连执行（无调度器层幂等包裹）。
    /// </summary>
    /// <param name="handler">处理器。</param>
    /// <param name="request">请求。</param>
    /// <param name="claimedScope">已通过守卫的作用域。</param>
    /// <param name="requestId">请求标识。</param>
    /// <returns>响应体原文。</returns>
    private async Task<string> DispatchDirectAsync(ActionHandler handler, OnlineAdminApiRequest request, OnlineScope claimedScope, string requestId)
    {
        try
        {
            var payload = await handler(request, claimedScope, CancellationToken.None).ConfigureAwait(false);
            return OnlineAdminApiEnvelope.Write(OnlineAdminApiResponse.Ok(payload, requestId, NowSeconds()));
        }
        catch (OnlineServiceException exception)
        {
            return WriteFail(exception.Code, exception.Message, requestId, request);
        }
        catch (Exception exception)
        {
            return WriteFail(OnlineErrorCode.InternalError, "Unhandled admin action failure: " + exception.Message, requestId, request);
        }
    }

    /// <summary>
    /// 幂等包裹执行（Execute 落首次响应原文 → Replay 原样透传；Conflict/Busy/InvalidKey 直接映射协议码）。
    /// </summary>
    /// <param name="registration">action 注册项。</param>
    /// <param name="request">请求。</param>
    /// <param name="claimedScope">已通过守卫的作用域。</param>
    /// <param name="requestId">请求标识。</param>
    /// <returns>响应体原文。</returns>
    private async Task<string> DispatchWithIdempotencyAsync(ActionRegistration registration, OnlineAdminApiRequest request, OnlineScope claimedScope, string requestId)
    {
        var idempotencyKey = request.ReadIdempotencyKey();
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            // Admin 三族写命令线缆均携带幂等键（grant/revoke、penalty、onlineControlled）；键缺失时退化为直连执行，
            // 不拒绝请求（Admin 侧键生成属调用方职责，服务端不做强约束）。
            return await DispatchDirectAsync(registration.Handler, request, claimedScope, requestId).ConfigureAwait(false);
        }

        request.TryReadInt64("PlayerId", out var playerId);
        var bindPlayer = playerId > 0;
        var canonicalRequestText = request.Action + "\n" + request.BodyText;
        var outcome = await _host.Idempotency.BeginAsync(claimedScope, idempotencyKey, canonicalRequestText, bindPlayer).ConfigureAwait(false);
        if (outcome.Kind == OnlineIdempotencyOutcomeKind.Replay)
        {
            return OnlineAdminApiEnvelope.WriteRaw(Encoding.UTF8.GetString(outcome.FirstResponse.ToArray()));
        }

        if (!outcome.CanExecute)
        {
            return WriteFail(outcome.ErrorCode, "Idempotency rejected (" + outcome.Kind + ") for key: " + idempotencyKey, requestId, request);
        }

        try
        {
            var payload = await registration.Handler(request, claimedScope, CancellationToken.None).ConfigureAwait(false);
            var innerJson = JsonHelper.Serialize(OnlineAdminApiResponse.Ok(payload, requestId, NowSeconds()));
            await _host.Idempotency.CompleteAsync(claimedScope, idempotencyKey, Encoding.UTF8.GetBytes(innerJson), bindPlayer).ConfigureAwait(false);
            return OnlineAdminApiEnvelope.WriteRaw(innerJson);
        }
        catch (OnlineServiceException exception)
        {
            await _host.Idempotency.FailAsync(claimedScope, idempotencyKey, bindPlayer).ConfigureAwait(false);
            return WriteFail(exception.Code, exception.Message, requestId, request);
        }
        catch (Exception exception)
        {
            await _host.Idempotency.FailAsync(claimedScope, idempotencyKey, bindPlayer).ConfigureAwait(false);
            return WriteFail(OnlineErrorCode.InternalError, "Unhandled admin action failure: " + exception.Message, requestId, request);
        }
    }

    /// <summary>
    /// 构造失败响应体原文。
    /// </summary>
    /// <param name="code">协议错误码。</param>
    /// <param name="message">诊断消息。</param>
    /// <param name="requestId">请求标识。</param>
    /// <param name="request">请求（可空；仅用于日志语义）。</param>
    /// <returns>响应体原文。</returns>
    private static string WriteFail(OnlineErrorCode code, string message, string requestId, OnlineAdminApiRequest request)
    {
        return OnlineAdminApiEnvelope.Write(OnlineAdminApiResponse.Fail(code, message, requestId, NowSeconds()));
    }

    /// <summary>
    /// 当前 Unix 秒。
    /// </summary>
    /// <returns>Unix 秒。</returns>
    private static long NowSeconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    /// <summary>
    /// action 处理器委托（返回业务数据载荷；作用域已通过守卫）。
    /// </summary>
    /// <param name="request">请求包装。</param>
    /// <param name="scope">已通过守卫的作用域（客户端声称 = 宿主授权）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>业务数据载荷（成功响应的 Data，必须非 null）。</returns>
    public delegate Task<object> ActionHandler(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// action 注册项。
    /// </summary>
    private sealed class ActionRegistration
    {
        /// <summary>
        /// 处理器。
        /// </summary>
        public ActionHandler Handler
        {
            get;
        }

        /// <summary>
        /// 是否经调度器层幂等包裹。
        /// </summary>
        public bool WrapIdempotency
        {
            get;
        }

        /// <summary>
        /// 初始化注册项。
        /// </summary>
        /// <param name="handler">处理器。</param>
        /// <param name="wrapIdempotency">是否幂等包裹。</param>
        public ActionRegistration(ActionHandler handler, bool wrapIdempotency)
        {
            Handler = handler;
            WrapIdempotency = wrapIdempotency;
        }
    }
}
