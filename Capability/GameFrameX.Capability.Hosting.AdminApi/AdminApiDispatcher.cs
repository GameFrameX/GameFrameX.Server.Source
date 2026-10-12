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
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Text;
using System.Text.Json;
using GameFrameX.Capability.Hosting;
using GameFrameX.Foundation.Json;

namespace GameFrameX.Capability.Hosting.AdminApi;

/// <summary>
/// admin API 调度器（去域化 action 调度：action 注册表 + 作用域适配 + 请求标识 + 幂等包裹 + 双层信封 +
/// <c>list_capabilities</c> 元端点与「未安装能力」判定）。
/// <para>
/// 维护约束（横切红线，语义与既有 Online admin 管道逐语句等价）：
/// 作用域判定只经组合根注入的 <see cref="IAdminApiScopeAdapter"/>（禁止 Handler 自行判定）；
/// HTTP 状态恒 200，业务错误只进内层 <see cref="AdminApiResponse.Code"/>（外层信封非 0 会被 Admin
/// 判为基础设施异常）；未知 action 走 <c>UnknownAction</c> 码，action 属于已禁用能力走
/// <c>CapabilityNotInstalled</c> 码并携带已装能力清单；副作用命令经 <see cref="IAdminApiIdempotencyAdapter"/>
/// 包裹（Replay 原样透传首次响应原文；幂等适配器缺省时包裹位退化为直连执行）；
/// <see cref="AdminApiResponse.ServerTime"/> 为 Unix 秒（Admin 线缆契约）。
/// </para>
/// </summary>
public sealed class AdminApiDispatcher
{
    /// <summary>
    /// action 注册表（能力侧贡献）。
    /// </summary>
    private readonly AdminActionRegistry _registry;

    /// <summary>
    /// 管道错误码映射（能力侧注入）。
    /// </summary>
    private readonly AdminApiErrorCodes _errorCodes;

    /// <summary>
    /// 作用域适配器（能力侧注入）。
    /// </summary>
    private readonly IAdminApiScopeAdapter _scopeAdapter;

    /// <summary>
    /// 幂等适配器（能力侧注入；null = 无幂等包裹服务，包裹位退化为直连）。
    /// </summary>
    private readonly IAdminApiIdempotencyAdapter _idempotencyAdapter;

    /// <summary>
    /// 宿主授权作用域。
    /// </summary>
    private readonly AdminApiClaimedScope _authorizedScope;

    /// <summary>
    /// 能力报告（null = 无报告面，未安装判定不可用）。
    /// </summary>
    private readonly CapabilityReport _report;

    /// <summary>
    /// 初始化 <see cref="AdminApiDispatcher"/> 并注册 <c>list_capabilities</c> 元端点（有报告面时）。
    /// </summary>
    /// <param name="registry">action 注册表（能力侧已贡献）。</param>
    /// <param name="errorCodes">管道错误码映射。</param>
    /// <param name="scopeAdapter">作用域适配器。</param>
    /// <param name="authorizedScope">宿主授权作用域。</param>
    /// <param name="idempotencyAdapter">幂等适配器（可空）。</param>
    /// <param name="report">能力报告（可空）。</param>
    public AdminApiDispatcher(AdminActionRegistry registry, AdminApiErrorCodes errorCodes, IAdminApiScopeAdapter scopeAdapter, AdminApiClaimedScope authorizedScope, IAdminApiIdempotencyAdapter idempotencyAdapter = null, CapabilityReport report = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _errorCodes = errorCodes ?? throw new ArgumentNullException(nameof(errorCodes));
        _scopeAdapter = scopeAdapter ?? throw new ArgumentNullException(nameof(scopeAdapter));
        _authorizedScope = authorizedScope ?? throw new ArgumentNullException(nameof(authorizedScope));
        _idempotencyAdapter = idempotencyAdapter;
        _report = report;
        if (report != null)
        {
            registry.Register("list_capabilities", ListCapabilitiesAsync, false);
        }
    }

    /// <summary>
    /// 获取已注册 action 名集合（含 <c>list_capabilities</c> 元端点；诊断与测试用）。
    /// </summary>
    public IReadOnlyCollection<string> ActionNames
    {
        get { return _registry.ActionNames; }
    }

    /// <summary>
    /// 调度一个 admin 请求（横切：action 解析 → 请求标识 → action 注册表/未安装判定 → 作用域守卫 → 幂等包裹 → 双层信封）。
    /// </summary>
    /// <param name="action">action 名（路由末段）。</param>
    /// <param name="bodyJson">原始请求体 JSON 字符串。</param>
    /// <returns>HTTP 响应体原文（外层信封 JSON；HTTP 状态恒 200）。</returns>
    public async Task<string> DispatchAsync(string action, string bodyJson)
    {
        string requestId;
        AdminApiRequest request = null;
        try
        {
            request = new AdminApiRequest(action, bodyJson);
        }
        catch (JsonException exception)
        {
            requestId = "req-" + Guid.NewGuid().ToString("N");
            return AdminApiEnvelope.Write(AdminApiResponse.Fail(_errorCodes.MalformedJson.Code, _errorCodes.MalformedJson.MessageKey, "Malformed JSON body: " + exception.Message, requestId, NowSeconds()));
        }

        requestId = request.ReadRequestId();
        if (string.IsNullOrEmpty(requestId))
        {
            requestId = "req-" + Guid.NewGuid().ToString("N");
        }

        var registration = _registry.Find(request.Action);
        if (registration == null)
        {
            var disabledOwner = _report == null ? null : _report.FindDisabledOwnerOfAction(request.Action);
            if (disabledOwner != null)
            {
                return AdminApiEnvelope.Write(AdminApiResponse.Fail(_errorCodes.CapabilityNotInstalled.Code, _errorCodes.CapabilityNotInstalled.MessageKey, "Capability not installed: " + disabledOwner.Manifest.Id, requestId, NowSeconds(), BuildNotInstalledPayload(disabledOwner)));
            }

            return WriteFail(_errorCodes.UnknownAction, "Unknown admin action: " + request.Action, requestId);
        }

        if (!_scopeAdapter.TryReadClaimedScope(request, out var claimedScope))
        {
            return WriteFail(_errorCodes.ScopeMissing, "Scope triple (TenantId/AppId/ServerId) missing or invalid in request body.", requestId);
        }

        var deniedCode = _scopeAdapter.ValidateClaimed(claimedScope, _authorizedScope);
        if (deniedCode != null)
        {
            return WriteFail(deniedCode, "Scope denied for tenant " + claimedScope.TenantId + ", app " + claimedScope.AppId + ", server " + claimedScope.ServerId + ".", requestId);
        }

        if (registration.WrapIdempotency && _idempotencyAdapter != null)
        {
            return await DispatchWithIdempotencyAsync(registration, request, claimedScope, requestId).ConfigureAwait(false);
        }

        return await DispatchDirectAsync(registration.Handler, request, claimedScope, requestId).ConfigureAwait(false);
    }

    /// <summary>
    /// 构造「未安装能力」失败载荷（已装能力清单 + 未安装归属）。
    /// </summary>
    /// <param name="disabledOwner">声明该 action 的已禁用能力条目。</param>
    /// <returns>诊断载荷。</returns>
    private object BuildNotInstalledPayload(CapabilityReportEntry disabledOwner)
    {
        return new
        {
            InstalledCapabilities = _report.EnabledIdsInOrder,
            NotInstalledCapability = disabledOwner.Manifest.Id,
        };
    }

    /// <summary>
    /// <c>list_capabilities</c> 元端点处理器（能力报告面：启用清单/顺序/来源/禁用项）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">已通过守卫的作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>能力报告载荷。</returns>
    private Task<object> ListCapabilitiesAsync(AdminApiRequest request, AdminApiClaimedScope scope, CancellationToken cancellationToken)
    {
        var capabilities = new List<object>(_report.Entries.Count);
        foreach (var entry in _report.Entries)
        {
            capabilities.Add(new
            {
                entry.Manifest.Id,
                entry.Manifest.Version,
                entry.Manifest.Actions,
                entry.Manifest.Requires,
                entry.SourceAssemblyName,
                State = entry.State.ToString(),
            });
        }

        object payload = new
        {
            Capabilities = capabilities,
            _report.EnabledIdsInOrder,
        };
        return Task.FromResult(payload);
    }

    /// <summary>
    /// 直连执行（无调度器层幂等包裹）。
    /// </summary>
    /// <param name="handler">处理器。</param>
    /// <param name="request">请求。</param>
    /// <param name="claimedScope">已通过守卫的作用域。</param>
    /// <param name="requestId">请求标识。</param>
    /// <returns>响应体原文。</returns>
    private async Task<string> DispatchDirectAsync(AdminActionHandler handler, AdminApiRequest request, AdminApiClaimedScope claimedScope, string requestId)
    {
        try
        {
            var payload = await handler(request, claimedScope, CancellationToken.None).ConfigureAwait(false);
            return AdminApiEnvelope.Write(AdminApiResponse.Ok(payload, requestId, NowSeconds()));
        }
        catch (AdminActionException exception)
        {
            return WriteFail(new AdminApiErrorCode(exception.Code, exception.MessageKey), exception.Message, requestId);
        }
        catch (Exception exception)
        {
            return WriteFail(_errorCodes.InternalError, "Unhandled admin action failure: " + exception.Message, requestId);
        }
    }

    /// <summary>
    /// 幂等包裹执行（Execute 落首次响应原文 → Replay 原样透传；拒绝码直接映射协议码）。
    /// </summary>
    /// <param name="registration">action 注册项。</param>
    /// <param name="request">请求。</param>
    /// <param name="claimedScope">已通过守卫的作用域。</param>
    /// <param name="requestId">请求标识。</param>
    /// <returns>响应体原文。</returns>
    private async Task<string> DispatchWithIdempotencyAsync(AdminActionRegistration registration, AdminApiRequest request, AdminApiClaimedScope claimedScope, string requestId)
    {
        var idempotencyKey = request.ReadIdempotencyKey();
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            // Admin 三族写命令线缆均携带幂等键；键缺失时退化为直连执行，
            // 不拒绝请求（Admin 侧键生成属调用方职责，服务端不做强约束）。
            return await DispatchDirectAsync(registration.Handler, request, claimedScope, requestId).ConfigureAwait(false);
        }

        request.TryReadInt64("PlayerId", out var playerId);
        var bindPlayer = playerId > 0;
        var canonicalRequestText = request.Action + "\n" + request.BodyText;
        var outcome = await _idempotencyAdapter.BeginAsync(claimedScope, idempotencyKey, canonicalRequestText, bindPlayer).ConfigureAwait(false);
        if (outcome.Kind == AdminIdempotencyOutcomeKind.Replay)
        {
            return AdminApiEnvelope.WriteRaw(Encoding.UTF8.GetString(outcome.FirstResponse));
        }

        if (!outcome.CanExecute)
        {
            return WriteFail(outcome.ErrorCode, "Idempotency rejected (" + outcome.Kind + ") for key: " + idempotencyKey, requestId);
        }

        try
        {
            var payload = await registration.Handler(request, claimedScope, CancellationToken.None).ConfigureAwait(false);
            var innerJson = JsonHelper.Serialize(AdminApiResponse.Ok(payload, requestId, NowSeconds()));
            await _idempotencyAdapter.CompleteAsync(claimedScope, idempotencyKey, Encoding.UTF8.GetBytes(innerJson), bindPlayer).ConfigureAwait(false);
            return AdminApiEnvelope.WriteRaw(innerJson);
        }
        catch (AdminActionException exception)
        {
            await _idempotencyAdapter.FailAsync(claimedScope, idempotencyKey, bindPlayer).ConfigureAwait(false);
            return WriteFail(new AdminApiErrorCode(exception.Code, exception.MessageKey), exception.Message, requestId);
        }
        catch (Exception exception)
        {
            await _idempotencyAdapter.FailAsync(claimedScope, idempotencyKey, bindPlayer).ConfigureAwait(false);
            return WriteFail(_errorCodes.InternalError, "Unhandled admin action failure: " + exception.Message, requestId);
        }
    }

    /// <summary>
    /// 构造失败响应体原文。
    /// </summary>
    /// <param name="code">协议错误码条目。</param>
    /// <param name="message">诊断消息。</param>
    /// <param name="requestId">请求标识。</param>
    /// <returns>响应体原文。</returns>
    private static string WriteFail(AdminApiErrorCode code, string message, string requestId)
    {
        return AdminApiEnvelope.Write(AdminApiResponse.Fail(code.Code, code.MessageKey, message, requestId, NowSeconds()));
    }

    /// <summary>
    /// 当前 Unix 秒。
    /// </summary>
    /// <returns>Unix 秒。</returns>
    private static long NowSeconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
