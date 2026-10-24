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

using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Abstractions.Contracts;

/// <summary>
/// Online 能力簇对通用 admin 管道的适配层（作用域守卫 / 幂等服务 / 错误码映射 / handler 包装）。
/// <para>
/// 维护约束：本类是注册反转的组合根胶水——Online 语义（3002-3005 守卫 / 幂等三段 / OnlineErrorCode 数值）
/// 只经本层进入机制管道；机制管道（Hosting.AdminApi）对 Online 类型零引用。
/// </para>
/// </summary>
public static class OnlineAdminApiAdapters
{
    /// <summary>
    /// Online 错误码 → 机制管道错误码映射（数值与消息键逐项对应 <see cref="OnlineErrorCode"/>）。
    /// </summary>
    public static AdminApiErrorCodes ErrorCodes
    {
        get
        {
            return new AdminApiErrorCodes(
                ToErrorCode(OnlineErrorCode.ParameterInvalid),
                ToErrorCode(OnlineErrorCode.ResourceNotFound),
                ToErrorCode(OnlineErrorCode.ScopeMissing),
                ToErrorCode(OnlineErrorCode.InternalError),
                ToErrorCode(OnlineErrorCode.CapabilityNotInstalled));
        }
    }

    /// <summary>
    /// 构造作用域适配器（线缆三元组提取 + <see cref="OnlineScopeGuard"/> 比对）。
    /// </summary>
    /// <param name="authorizedScope">保留参数（既有公共签名兼容）：比对用授权作用域由 AdminApiDispatcher 单独持有，经 <see cref="IAdminApiScopeAdapter.ValidateClaimed"/> 实参传入。</param>
    /// <returns>作用域适配器。</returns>
    public static IAdminApiScopeAdapter CreateScopeAdapter(OnlineScope authorizedScope)
    {
        return new ScopeAdapter();
    }

    /// <summary>
    /// 构造幂等适配器（<see cref="OnlineIdempotencyService"/> 三段契约 → 机制管道裁决形态）。
    /// </summary>
    /// <param name="idempotency">Online 幂等服务。</param>
    /// <returns>幂等适配器。</returns>
    public static IAdminApiIdempotencyAdapter CreateIdempotencyAdapter(OnlineIdempotencyService idempotency)
    {
        return new IdempotencyAdapter(idempotency);
    }

    /// <summary>
    /// 把 Online handler（<see cref="OnlineScope"/> 语义）包装为机制管道 handler
    /// （三元素组作用域 + <see cref="AdminActionException"/> 业务异常映射）。
    /// </summary>
    /// <param name="handler">Online handler。</param>
    /// <returns>机制管道 handler。</returns>
    public static AdminActionHandler Wrap(Func<AdminApiRequest, OnlineScope, CancellationToken, Task<object>> handler)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        return async (request, claimedScope, cancellationToken) =>
        {
            try
            {
                return await handler(request, ToOnlineScope(claimedScope), cancellationToken).ConfigureAwait(false);
            }
            catch (OnlineServiceException exception)
            {
                throw new AdminActionException((int)exception.Code, exception.Code.ToMessageKey(), exception.Message);
            }
        };
    }

    /// <summary>
    /// 三元素组作用域 → <see cref="OnlineScope"/>。
    /// </summary>
    /// <param name="claimedScope">机制管道作用域。</param>
    /// <returns>Online 作用域。</returns>
    internal static OnlineScope ToOnlineScope(AdminApiClaimedScope claimedScope)
    {
        return new OnlineScope(claimedScope.TenantId, claimedScope.AppId, claimedScope.ServerId);
    }

    /// <summary>
    /// Online 错误码 → 机制管道错误码条目。
    /// </summary>
    /// <param name="code">Online 错误码。</param>
    /// <returns>机制管道错误码条目。</returns>
    internal static AdminApiErrorCode ToErrorCode(OnlineErrorCode code)
    {
        return new AdminApiErrorCode((int)code, code.ToMessageKey());
    }

    /// <summary>
    /// 作用域适配器实现（无状态：比对用授权作用域经 <see cref="IAdminApiScopeAdapter.ValidateClaimed"/> 实参注入）。
    /// </summary>
    private sealed class ScopeAdapter : IAdminApiScopeAdapter
    {
        /// <summary>
        /// 从请求中读取声明的租户、应用、服务器三元素作用域；三者均可读取时组装为机制管道作用域并返回 true，否则输出 null 并返回 false。
        /// </summary>
        /// <remarks>
        /// Reads the claimed tenant / app / server triple from the request; when all three are readable it builds an
        /// <see cref="AdminApiClaimedScope"/> and returns true, otherwise outputs null and returns false.
        /// </remarks>
        /// <param name="request">管理 API 请求 / Admin API request</param>
        /// <param name="claimedScope">读取成功时为声明作用域，失败时为 null / Claimed scope on success, null on failure</param>
        /// <returns>是否成功读取声明作用域 / Whether the claimed scope was read successfully</returns>
        public bool TryReadClaimedScope(AdminApiRequest request, out AdminApiClaimedScope claimedScope)
        {
            if (request.TryReadClaimedScope(out var tenantId, out var appId, out var serverId))
            {
                claimedScope = new AdminApiClaimedScope(tenantId, appId, serverId);
                return true;
            }

            claimedScope = null;
            return false;
        }

        /// <summary>
        /// 将声明作用域与授权作用域转换为 Online 作用域后交由 <see cref="OnlineScopeGuard"/> 比对；越权时返回映射后的机制管道错误码，通过时返回 null。
        /// </summary>
        /// <remarks>
        /// Converts both scopes to Online scopes and compares them via <see cref="OnlineScopeGuard"/>; returns the mapped
        /// pipeline error code when access is denied, or null when the claimed scope is within the authorized scope.
        /// </remarks>
        /// <param name="claimed">请求声明的作用域 / Scope claimed by the request</param>
        /// <param name="authorized">调用方被授权的作用域 / Scope the caller is authorized for</param>
        /// <returns>拒绝时的错误码；通过时为 null / Error code when denied; null when allowed</returns>
        public AdminApiErrorCode ValidateClaimed(AdminApiClaimedScope claimed, AdminApiClaimedScope authorized)
        {
            var denied = OnlineScopeGuard.Validate(ToOnlineScope(claimed), ToOnlineScope(authorized));
            return denied.HasValue ? ToErrorCode(denied.Value) : null;
        }
    }

    /// <summary>
    /// 幂等适配器实现。
    /// </summary>
    private sealed class IdempotencyAdapter : IAdminApiIdempotencyAdapter
    {
        /// <summary>
        /// Online 幂等服务。
        /// </summary>
        private readonly OnlineIdempotencyService _idempotency;

        /// <summary>
        /// 初始化适配器。
        /// </summary>
        /// <param name="idempotency">Online 幂等服务。</param>
        public IdempotencyAdapter(OnlineIdempotencyService idempotency)
        {
            _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        }

        /// <summary>
        /// 将作用域转换为 Online 作用域后委托 Online 幂等服务开始幂等登记，并把结果种类、可执行标志、错误码（无错误时为 null）与首次响应副本映射为机制管道幂等结果。
        /// </summary>
        /// <remarks>
        /// Delegates to the Online idempotency service after converting the scope, then maps the outcome kind, can-execute
        /// flag, error code (null when none) and a copy of the first response into the pipeline idempotency outcome.
        /// </remarks>
        /// <param name="scope">幂等记录所属作用域 / Scope owning the idempotency record</param>
        /// <param name="idempotencyKey">幂等键 / Idempotency key</param>
        /// <param name="canonicalRequestText">规范化请求文本，用于比对重放请求是否一致 / Canonical request text used to detect mismatched replays</param>
        /// <param name="bindPlayer">是否将幂等记录绑定到玩家维度 / Whether the record is bound to the player dimension</param>
        /// <returns>机制管道幂等登记结果 / Pipeline idempotency outcome</returns>
        public async Task<AdminIdempotencyOutcome> BeginAsync(AdminApiClaimedScope scope, string idempotencyKey, string canonicalRequestText, bool bindPlayer)
        {
            var outcome = await _idempotency.BeginAsync(ToOnlineScope(scope), idempotencyKey, canonicalRequestText, bindPlayer).ConfigureAwait(false);
            var kind = (AdminIdempotencyOutcomeKind)(int)outcome.Kind;
            return new AdminIdempotencyOutcome(kind, outcome.CanExecute, outcome.ErrorCode == OnlineErrorCode.None ? null : ToErrorCode(outcome.ErrorCode), outcome.FirstResponse.ToArray());
        }

        /// <summary>
        /// 将作用域转换为 Online 作用域后委托 Online 幂等服务把幂等记录标记为完成并保存首次响应。
        /// </summary>
        /// <remarks>
        /// Converts the scope and delegates to the Online idempotency service to mark the record completed and store the first response.
        /// </remarks>
        /// <param name="scope">幂等记录所属作用域 / Scope owning the idempotency record</param>
        /// <param name="idempotencyKey">幂等键 / Idempotency key</param>
        /// <param name="firstResponse">首次执行的响应内容 / Response produced by the first execution</param>
        /// <param name="bindPlayer">是否将幂等记录绑定到玩家维度 / Whether the record is bound to the player dimension</param>
        /// <returns>表示异步操作的任务 / Task representing the asynchronous operation</returns>
        public Task CompleteAsync(AdminApiClaimedScope scope, string idempotencyKey, byte[] firstResponse, bool bindPlayer)
        {
            return _idempotency.CompleteAsync(ToOnlineScope(scope), idempotencyKey, firstResponse, bindPlayer);
        }

        /// <summary>
        /// 将作用域转换为 Online 作用域后委托 Online 幂等服务把幂等记录标记为失败，以便后续请求重新执行。
        /// </summary>
        /// <remarks>
        /// Converts the scope and delegates to the Online idempotency service to mark the record failed so later requests can execute again.
        /// </remarks>
        /// <param name="scope">幂等记录所属作用域 / Scope owning the idempotency record</param>
        /// <param name="idempotencyKey">幂等键 / Idempotency key</param>
        /// <param name="bindPlayer">是否将幂等记录绑定到玩家维度 / Whether the record is bound to the player dimension</param>
        /// <returns>表示异步操作的任务 / Task representing the asynchronous operation</returns>
        public Task FailAsync(AdminApiClaimedScope scope, string idempotencyKey, bool bindPlayer)
        {
            return _idempotency.FailAsync(ToOnlineScope(scope), idempotencyKey, bindPlayer);
        }
    }
}
