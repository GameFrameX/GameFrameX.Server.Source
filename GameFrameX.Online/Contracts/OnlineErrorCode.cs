// ==========================================================================================
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

namespace GameFrameX.Online.Contracts;

/// <summary>
/// GFX Online 平台协议错误码（vault:C2 S1.3 八段分层，Server 侧承载）。
/// <para>
/// 协议码分段固定：<c>1xxx</c> 系统与网络 / <c>2xxx</c> 鉴权与 Session / <c>3xxx</c> 租户·App·Server 作用域 /
/// <c>4xxx</c> 参数与资源 / <c>5xxx</c> 业务状态 / <c>6xxx</c> 幂等与冲突 / <c>7xxx</c> 限流与风控 / <c>8xxx</c> 临时失败与可重试；
/// <c>0</c> 表示成功。成员集合与 Admin 侧错误码表快照（唯一权威详表）的 23 个错误成员一一同构，
/// Admin 按「协议段 Nxxx ↔ 镜像段 32(N-1)」映射消费（Admin 仓 <c>OnlineErrorCode</c> 镜像码 32000001～32700002）。
/// </para>
/// <para>
/// 维护约束：新增成员只能在对应段内顺延序号，禁止手改既有数值、禁止跨段挪用；
/// 任何增删改必须先改错误码表快照并经评审，再同步 Admin 镜像与 SDK 映射，三端一致由守护测试锁定（VC-1.11）。
/// 消费端对段外未知码必须兜底映射到 <see cref="InternalError"/>，不得崩溃、不得误判成功（VC-1.10）。
/// </para>
/// </summary>
public enum OnlineErrorCode
{
    /// <summary>
    /// 成功（无错误）。协议中与非 0 错误码互斥使用。
    /// </summary>
    None = 0,

    // ---- 段 1xxx：系统与网络 ----

    /// <summary>
    /// Online 系统内部错误（未捕获异常、不变量被破坏等；段外未知码的兜底映射目标）。
    /// </summary>
    InternalError = 1001,

    /// <summary>
    /// Online 网络超时（上游或下游调用超时）。
    /// </summary>
    NetworkTimeout = 1002,

    /// <summary>
    /// Online 网络错误（连接中断、协议解析失败等）。
    /// </summary>
    NetworkError = 1003,

    // ---- 段 2xxx：鉴权与 Session ----

    /// <summary>
    /// 会话令牌已过期，需按 Token 契约刷新或重新签发。
    /// </summary>
    TokenExpired = 2001,

    /// <summary>
    /// 会话令牌已被吊销（主动登出、管理员强制下线等）。
    /// </summary>
    TokenRevoked = 2002,

    /// <summary>
    /// 会话无效或未授权（签名/格式非法、会话不存在、权限不足）。
    /// </summary>
    SessionInvalid = 2003,

    // ---- 段 3xxx：租户、App、Server 作用域 ----

    /// <summary>
    /// 作用域越界拒绝（泛化越权：不归属其他更具体作用域错误时的兜底拒绝码）。
    /// </summary>
    ScopeDenied = 3001,

    /// <summary>
    /// 跨租户请求被拒绝（作用域三元组的租户与鉴权上下文不一致）。
    /// </summary>
    CrossTenantDenied = 3002,

    /// <summary>
    /// 跨 App 请求被拒绝（目标资源不归属当前 AppId）。
    /// </summary>
    CrossAppDenied = 3003,

    /// <summary>
    /// 区服作用域非法（ServerId 不存在或不归属当前 AppId）。
    /// </summary>
    ServerScopeDenied = 3004,

    /// <summary>
    /// 作用域缺失（TenantId/AppId/ServerId 未提供且无法从鉴权上下文补齐）。
    /// </summary>
    ScopeMissing = 3005,

    // ---- 段 4xxx：参数与资源 ----

    /// <summary>
    /// 参数校验失败（请求上下文缺失、幂等键格式非法、字段取值非法等，VC-1.1）。
    /// </summary>
    ParameterInvalid = 4001,

    /// <summary>
    /// 资源不存在（目标实体/记录未找到）。
    /// </summary>
    ResourceNotFound = 4002,

    // ---- 段 5xxx：业务状态 ----

    /// <summary>
    /// 业务状态未准备（前置流程未完成，如未创建会话即请求匹配）。
    /// </summary>
    StateNotReady = 5001,

    /// <summary>
    /// 业务状态已结束（对已完结对象重复推进）。
    /// </summary>
    StateEnded = 5002,

    /// <summary>
    /// 业务当前状态不允许该操作（状态机非法迁移）。
    /// </summary>
    StateOperationForbidden = 5003,

    // ---- 段 6xxx：幂等与冲突 ----

    /// <summary>
    /// 重复请求（幂等键命中首次结果的显式报告码；透明回放通常直接返回首次结果而非本码）。
    /// </summary>
    DuplicateRequest = 6001,

    /// <summary>
    /// 版本冲突（相同幂等键承载了不同的业务意图，即同键不同请求体，VC-1.4）。
    /// </summary>
    VersionConflict = 6002,

    // ---- 段 7xxx：限流、安全和风控 ----

    /// <summary>
    /// 频率超限（触达限流阈值）。
    /// </summary>
    RateLimitExceeded = 7001,

    /// <summary>
    /// 风控拒绝（命中风控规则）。
    /// </summary>
    RiskControlRejected = 7002,

    /// <summary>
    /// 账号已被封禁。
    /// </summary>
    AccountBanned = 7003,

    // ---- 段 8xxx：临时失败与可重试 ----

    /// <summary>
    /// 服务忙（并发占位未落定等临时繁忙，调用方可安全重试，VC-1.3 并发半边）。
    /// </summary>
    ServiceBusy = 8001,

    /// <summary>
    /// 依赖服务暂不可用（存储/下游不可达，可重试；重试后不得重复生效）。
    /// </summary>
    DependencyUnavailable = 8002,
}
