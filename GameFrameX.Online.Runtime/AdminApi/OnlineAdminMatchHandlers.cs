// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应开源许可证与规定。
//   Usage of this project must strictly comply with applicable open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目 实施任何危害国家安全、破坏社会秩序、
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

using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Contracts;
using GameFrameX.Capability.Online.Presence;
using GameFrameX.Capability.Online.Scope;

namespace GameFrameX.Online.Runtime.AdminApi;

/// <summary>
/// 对局 / 管制域 admin 命令（end_abnormal_match / suspend_resume_player；C231 P3 后的壳残留半边——
/// 匹配四 action 已迁 Capability.Online.Matchmaking、match_isolation 已迁 Capability.Online.Social，本类 P4 随壳退役）。
/// <para>
/// 维护约束：suspend_resume_player 为 Admin 受控命令（调度器层幂等包裹）；
/// end_abnormal_match 固定 5003 拒绝（对局结束属游戏进程权威，admin 面无该命令通道）。
/// </para>
/// </summary>
public sealed class OnlineAdminMatchHandlers
{
    /// <summary>
    /// 宿主。
    /// </summary>
    private readonly OnlineRuntimeHost _host;

    /// <summary>
    /// 初始化 <see cref="OnlineAdminMatchHandlers"/>。
    /// </summary>
    /// <param name="host">宿主。</param>
    public OnlineAdminMatchHandlers(OnlineRuntimeHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// 注册对局域 action（结束异常对局）。
    /// </summary>
    /// <param name="registry">动作注册表。</param>
    public void RegisterEndAbnormalMatch(AdminActionRegistry registry)
    {
        registry.Register("end_abnormal_match", OnlineAdminApiAdapters.Wrap(EndAbnormalMatchAsync), false);
    }

    /// <summary>
    /// 注册玩家管制域 action（封禁 / 解禁）。
    /// </summary>
    /// <param name="registry">动作注册表。</param>
    public void RegisterSuspendResume(AdminActionRegistry registry)
    {
        registry.Register("suspend_resume_player", OnlineAdminApiAdapters.Wrap(SuspendResumePlayerAsync), true);
    }

    /// <summary>
    /// end_abnormal_match：固定拒绝（对局结束属游戏进程权威，admin 面无通道）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>不返回（恒抛 5003）。</returns>
    public Task<object> EndAbnormalMatchAsync(AdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        // Localization: Online.AdminApi.EndAbnormalMatchForbidden - 管理面不支持结束异常对局；对局生命周期归游戏进程所有。
        throw new OnlineServiceException(OnlineErrorCode.StateOperationForbidden,
                                         LocalizationService.GetString(Localization.Keys.Online.AdminApi.EndAbnormalMatchForbidden));
    }

    /// <summary>
    /// suspend_resume_player：暂停 / 恢复匹配资格（在线状态阻断位，Apply / Lift 双向）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受控命令回执。</returns>
    public async Task<object> SuspendResumePlayerAsync(AdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var playerScope = OnlineAdminApiContract.ReadPlayerScope(request, scope);
        var direction = OnlineAdminApiContract.ReadDirection(request);
        var reason = OnlineAdminApiContract.RequireString(request, "Reason");
        if (direction == "Apply")
        {
            OnlineAdminApiContract.Unwrap(await _host.Presence.MarkBlockedAsync(playerScope, request.ReadRequestId(), cancellationToken).ConfigureAwait(false));
        }
        else
        {
            OnlineAdminApiContract.Unwrap(await _host.Presence.ReleaseBlockedAsync(playerScope, request.ReadRequestId(), cancellationToken).ConfigureAwait(false));
        }

        return OnlineAdminApiContract.BuildControlledAck(request, playerScope.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture), "Match eligibility " + (direction == "Apply" ? "suspended" : "resumed") + ". Reason: " + reason);
    }


}