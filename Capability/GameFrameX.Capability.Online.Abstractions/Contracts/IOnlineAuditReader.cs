// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and other international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the full license text in the LICENSE file located at the root of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activity that endangers national security, disrupts social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of citizens as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织不承担任何责任，开发者应自行承担。
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

using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Abstractions.Contracts;

/// <summary>
/// 统一审计读面（叶子能力包消费聚合审计域的最窄契约，C223 gate 上移同款手法，C231）：
/// 叶子域 admin handler 的域内审计查询（如 query_asset_audit）经本接口读统一审计面，
/// 不引入对聚合域（Operations/Audit 实现体）的编译期依赖。
/// <para>
/// 维护约束：作用域只锚定 TenantId/AppId 两键（审计是跨区服检索域）；实现方 = <c>OnlineAuditService</c>
/// （P4 归 Operations 包）；装配经能力服务共享面注入（组合根 <c>UseService&lt;IOnlineAuditReader&gt;</c>）。
/// </para>
/// </summary>
/// <remarks>
/// The unified audit read face (the narrowest contract through which leaf capability packages consume the
/// aggregate audit domain — the same gate-lifting approach as C223, applied by C231): leaf-domain admin
/// handlers query domain-scoped audit trails (e.g. query_asset_audit) through this interface without a
/// compile-time dependency on the aggregate domain (the Operations/Audit implementation). The scope anchors
/// on TenantId/AppId only (audit is a cross-server retrieval domain); the implementor is
/// <c>OnlineAuditService</c> (moving to the Operations package in P4); wiring goes through the capability
/// service-sharing face (composition-root <c>UseService&lt;IOnlineAuditReader&gt;</c>).
/// </remarks>
public interface IOnlineAuditReader
{
    /// <summary>
    /// 按查询条件检索统一审计记录（游标分页；作用域只锚定 TenantId/AppId 两键）。
    /// </summary>
    /// <param name="scope">授权作用域。</param>
    /// <param name="query">查询条件（可空 = 缺省全量首页）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>审计记录页（失败以 <see cref="OnlineResult{TData}"/> 错误码表达）。</returns>
    Task<OnlineResult<OnlineAuditPage>> QueryAsync(OnlineScope scope, OnlineAuditQuery query = null, CancellationToken cancellationToken = default);
}
