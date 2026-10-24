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
//  ==========================================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;

namespace GameFrameX.Tests.Online;


/// <summary>
/// 机制测试作用域适配器（从请求体读三元组 + 恒放行——作用域守卫语义另有 Online 侧用例锚定）。
/// </summary>
public sealed class MechanismTestScopeAdapter : IAdminApiScopeAdapter
{
    /// <summary>
    /// 从管理面请求中读取声明的租户、应用、服务器三元组；读取成功时据此构造声明作用域并返回 <c>true</c>，否则输出 <c>null</c> 并返回 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// Reads the claimed tenant, app and server triple from the admin API request; on success builds the claimed scope and returns <c>true</c>, otherwise outputs <c>null</c> and returns <c>false</c>.
    /// </remarks>
    /// <param name="request">管理面请求 / Admin API request</param>
    /// <param name="claimedScope">读取成功时为声明作用域，失败时为 <c>null</c> / Claimed scope on success, <c>null</c> on failure</param>
    /// <returns>请求中带有完整三元组时为 <c>true</c>，否则为 <c>false</c> / <c>true</c> when the request carries the full triple, otherwise <c>false</c></returns>
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
    /// 恒放行：不比较声明作用域与授权作用域，始终返回 <c>null</c> 表示校验通过；作用域守卫的拒绝语义由 Online 侧用例单独覆盖，这里只服务机制层测试。
    /// </summary>
    /// <remarks>
    /// Always allows: never compares the claimed and authorized scopes and always returns <c>null</c> (validation passed). Rejection semantics of the scope guard are covered separately by Online test cases; this adapter only serves mechanism tests.
    /// </remarks>
    /// <param name="claimed">请求声明的作用域（未使用） / Scope claimed by the request (unused)</param>
    /// <param name="authorized">已授权的作用域（未使用） / Authorized scope (unused)</param>
    /// <returns>始终为 <c>null</c>，表示无错误 / Always <c>null</c>, meaning no error</returns>
    public AdminApiErrorCode ValidateClaimed(AdminApiClaimedScope claimed, AdminApiClaimedScope authorized)
    {
        return null;
    }
}

/// <summary>槽位一模块（发现序最先）。</summary>
