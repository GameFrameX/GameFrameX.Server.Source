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

using GameFrameX.Online.Contracts;

namespace GameFrameX.Online.Scope;

/// <summary>
/// Online 作用域守卫（vault:C2：跨 Tenant/App/Server 请求被稳定拒绝且留审计，VC-1.6/1.7）。
/// <para>
/// 维护约束：四端（Server / Client API / Hub API / Admin API）统一经本守卫判定，禁止各 Handler 自行判断；
/// 判定拒绝必须 100% 留痕（审计钩子由调用方接线，拒绝码走 3xxx 段）。
/// </para>
/// </summary>
public static class OnlineScopeGuard
{
    /// <summary>
    /// 校验实际作用域是否被目标作用域（资源归属）接纳。
    /// </summary>
    /// <param name="targetScope">目标作用域（被访问资源或目标操作的归属作用域）。</param>
    /// <param name="actualScope">实际生效作用域（鉴权上下文解析产物）。</param>
    /// <returns>接纳返回 <see langword="null"/>；拒绝返回 3xxx 段错误码（跨租户/跨 App/跨服分别映射）。</returns>
    public static OnlineErrorCode? Validate(OnlineScope targetScope, OnlineScope actualScope)
    {
        if (targetScope == null || actualScope == null)
        {
            return OnlineErrorCode.ScopeMissing;
        }

        if (targetScope.TenantId <= 0 || targetScope.AppId <= 0 || targetScope.ServerId <= 0 ||
            actualScope.TenantId <= 0 || actualScope.AppId <= 0 || actualScope.ServerId <= 0)
        {
            return OnlineErrorCode.ScopeMissing;
        }

        if (actualScope.TenantId != targetScope.TenantId)
        {
            return OnlineErrorCode.CrossTenantDenied;
        }

        if (actualScope.AppId != targetScope.AppId)
        {
            return OnlineErrorCode.CrossAppDenied;
        }

        if (actualScope.ServerId != targetScope.ServerId)
        {
            return OnlineErrorCode.ServerScopeDenied;
        }

        return null;
    }
}
