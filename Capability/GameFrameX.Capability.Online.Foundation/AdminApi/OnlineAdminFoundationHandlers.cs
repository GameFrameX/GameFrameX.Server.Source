// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://github.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Foundation.Presence;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Foundation.AdminApi;

/// <summary>
/// 平台地基域 admin 命令（suspend_resume_player）。
/// <para>
/// 维护约束：suspend_resume_player 为 Admin 受控命令（调度器层幂等包裹）；消费面
/// （在线状态服务）由模块 Register 期构造后参数化传入（静态化模式对齐包族 admin handlers）。
/// </para>
/// </summary>
/// <remarks>
/// Admin commands of the foundation domain (suspend_resume_player). The consumed presence service is
/// parameterized in by the module after <c>Register</c>, mirroring the package-family handler pattern.
/// </remarks>
public static class OnlineAdminFoundationHandlers
{
    /// <summary>
    /// 注册平台地基域 action（封禁 / 解禁）。
    /// </summary>
    /// <param name="registry">动作注册表。</param>
    /// <param name="presence">在线状态服务（模块 Register 期构造）。</param>
    public static void RegisterSuspendResume(AdminActionRegistry registry, OnlinePresenceService presence)
    {
        if (registry == null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        if (presence == null)
        {
            throw new ArgumentNullException(nameof(presence));
        }

        registry.Register("suspend_resume_player", OnlineAdminApiAdapters.Wrap((request, scope, cancellationToken) => SuspendResumePlayerAsync(request, scope, cancellationToken, presence)), true);
    }

    /// <summary>
    /// suspend_resume_player：暂停 / 恢复匹配资格（在线状态阻断位，Apply / Lift 双向）——逐语句迁移自壳
    /// <c>OnlineAdminMatchHandlers</c>。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="presence">在线状态服务。</param>
    /// <returns>受控命令回执。</returns>
    private static async Task<object> SuspendResumePlayerAsync(AdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken, OnlinePresenceService presence)
    {
        var playerScope = OnlineAdminApiContract.ReadPlayerScope(request, scope);
        var direction = OnlineAdminApiContract.ReadDirection(request);
        var reason = OnlineAdminApiContract.RequireString(request, "Reason");
        if (direction == "Apply")
        {
            OnlineAdminApiContract.Unwrap(await presence.MarkBlockedAsync(playerScope, request.ReadRequestId(), cancellationToken).ConfigureAwait(false));
        }
        else
        {
            OnlineAdminApiContract.Unwrap(await presence.ReleaseBlockedAsync(playerScope, request.ReadRequestId(), cancellationToken).ConfigureAwait(false));
        }

        return OnlineAdminApiContract.BuildControlledAck(request, playerScope.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture), "Match eligibility " + (direction == "Apply" ? "suspended" : "resumed") + ". Reason: " + reason);
    }
}
