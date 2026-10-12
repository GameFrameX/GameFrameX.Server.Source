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

namespace GameFrameX.Capability.Hosting.AdminApi;

/// <summary>
/// admin 请求的声称作用域三元组（机制层不解释语义，判定由组合根注入的作用域适配器完成）。
/// </summary>
public sealed class AdminApiClaimedScope
{
    /// <summary>
    /// 初始化 <see cref="AdminApiClaimedScope"/>。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="serverId">区服标识。</param>
    public AdminApiClaimedScope(long tenantId, long appId, long serverId)
    {
        TenantId = tenantId;
        AppId = appId;
        ServerId = serverId;
    }

    /// <summary>
    /// 获取租户标识。
    /// </summary>
    public long TenantId { get; }

    /// <summary>
    /// 获取应用标识。
    /// </summary>
    public long AppId { get; }

    /// <summary>
    /// 获取区服标识。
    /// </summary>
    public long ServerId { get; }
}

/// <summary>
/// admin 作用域适配器（组合根注入：线缆三元组提取 + 声称作用域与授权作用域的比对判定）。
/// </summary>
public interface IAdminApiScopeAdapter
{
    /// <summary>
    /// 从请求体提取客户端声称的作用域三元组。
    /// </summary>
    /// <param name="request">请求包装。</param>
    /// <param name="claimedScope">输出：声称作用域。</param>
    /// <returns>三元组完整提取返回 true；否则 false（按作用域缺失拒绝）。</returns>
    bool TryReadClaimedScope(AdminApiRequest request, out AdminApiClaimedScope claimedScope);

    /// <summary>
    /// 比对声称作用域与授权作用域。
    /// </summary>
    /// <param name="claimed">客户端声称作用域。</param>
    /// <param name="authorized">宿主授权作用域。</param>
    /// <returns>拒绝时返回拒绝码；通过返回 null。</returns>
    AdminApiErrorCode ValidateClaimed(AdminApiClaimedScope claimed, AdminApiClaimedScope authorized);
}

/// <summary>
/// admin action 业务失败异常（能力侧业务异常的机制层载体；错误码与消息键由能力侧映射后抛出）。
/// </summary>
public sealed class AdminActionException : Exception
{
    /// <summary>
    /// 初始化 <see cref="AdminActionException"/>。
    /// </summary>
    /// <param name="code">协议错误码。</param>
    /// <param name="messageKey">可本地化消息键。</param>
    /// <param name="message">诊断消息。</param>
    public AdminActionException(int code, string messageKey, string message)
        : base(message)
    {
        Code = code;
        MessageKey = messageKey ?? string.Empty;
    }

    /// <summary>
    /// 获取协议错误码。
    /// </summary>
    public int Code { get; }

    /// <summary>
    /// 获取可本地化消息键。
    /// </summary>
    public string MessageKey { get; }
}

/// <summary>
/// admin 幂等裁决结果种类（Replay 原样透传首次响应；其余按拒绝处理）。
/// </summary>
public enum AdminIdempotencyOutcomeKind
{
    /// <summary>
    /// 允许执行。
    /// </summary>
    Execute = 0,

    /// <summary>
    /// 幂等重放（透传首次响应原文）。
    /// </summary>
    Replay = 1,

    /// <summary>
    /// 键冲突拒绝。
    /// </summary>
    Conflict = 2,

    /// <summary>
    /// 并发占用拒绝。
    /// </summary>
    Busy = 3,

    /// <summary>
    /// 键非法拒绝。
    /// </summary>
    InvalidKey = 4,
}

/// <summary>
/// admin 幂等裁决结果。
/// </summary>
public sealed class AdminIdempotencyOutcome
{
    /// <summary>
    /// 初始化 <see cref="AdminIdempotencyOutcome"/>。
    /// </summary>
    /// <param name="kind">裁决种类。</param>
    /// <param name="canExecute">是否允许执行。</param>
    /// <param name="errorCode">拒绝码（拒绝时必填）。</param>
    /// <param name="firstResponse">首次响应原文（Replay 时必填）。</param>
    public AdminIdempotencyOutcome(AdminIdempotencyOutcomeKind kind, bool canExecute, AdminApiErrorCode errorCode = null, byte[] firstResponse = null)
    {
        Kind = kind;
        CanExecute = canExecute;
        ErrorCode = errorCode;
        FirstResponse = firstResponse;
    }

    /// <summary>
    /// 获取裁决种类。
    /// </summary>
    public AdminIdempotencyOutcomeKind Kind { get; }

    /// <summary>
    /// 获取是否允许执行。
    /// </summary>
    public bool CanExecute { get; }

    /// <summary>
    /// 获取拒绝码。
    /// </summary>
    public AdminApiErrorCode ErrorCode { get; }

    /// <summary>
    /// 获取首次响应原文（Replay 用）。
    /// </summary>
    public byte[] FirstResponse { get; }
}

/// <summary>
/// admin 幂等适配器（组合根注入：调度器层幂等包裹的 Begin/Complete/Fail 三段契约）。
/// </summary>
public interface IAdminApiIdempotencyAdapter
{
    /// <summary>
    /// 开启一次幂等裁决。
    /// </summary>
    /// <param name="scope">已通过守卫的作用域。</param>
    /// <param name="idempotencyKey">幂等键。</param>
    /// <param name="canonicalRequestText">规范化请求文本。</param>
    /// <param name="bindPlayer">是否绑定玩家主体。</param>
    /// <returns>裁决结果。</returns>
    Task<AdminIdempotencyOutcome> BeginAsync(AdminApiClaimedScope scope, string idempotencyKey, string canonicalRequestText, bool bindPlayer);

    /// <summary>
    /// 落定首次执行成功（登记首次响应原文）。
    /// </summary>
    /// <param name="scope">已通过守卫的作用域。</param>
    /// <param name="idempotencyKey">幂等键。</param>
    /// <param name="firstResponse">首次响应原文。</param>
    /// <param name="bindPlayer">是否绑定玩家主体。</param>
    /// <returns>异步任务。</returns>
    Task CompleteAsync(AdminApiClaimedScope scope, string idempotencyKey, byte[] firstResponse, bool bindPlayer);

    /// <summary>
    /// 登记首次执行失败。
    /// </summary>
    /// <param name="scope">已通过守卫的作用域。</param>
    /// <param name="idempotencyKey">幂等键。</param>
    /// <param name="bindPlayer">是否绑定玩家主体。</param>
    /// <returns>异步任务。</returns>
    Task FailAsync(AdminApiClaimedScope scope, string idempotencyKey, bool bindPlayer);
}
