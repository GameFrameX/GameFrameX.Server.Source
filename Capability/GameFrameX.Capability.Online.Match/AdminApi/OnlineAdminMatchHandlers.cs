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
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Online.Match.AdminApi;

/// <summary>
/// 对局域 admin 命令（end_abnormal_match）。
/// <para>
/// 维护约束：end_abnormal_match 固定 5003 拒绝（对局结束属游戏进程权威，admin 面无该命令通道）——
/// 无状态依赖，静态注册形态。
/// </para>
/// </summary>
/// <remarks>
/// Admin commands of the match domain (end_abnormal_match). end_abnormal_match always rejects with 5003 —
/// match termination belongs to the
/// game process authority and the admin plane offers no such command channel; the handler is stateless and
/// registers statically.
/// </remarks>
public static class OnlineAdminMatchHandlers
{
    /// <summary>
    /// 注册对局域 action（结束异常对局）。
    /// </summary>
    /// <param name="registry">动作注册表。</param>
    public static void RegisterEndAbnormalMatch(AdminActionRegistry registry)
    {
        registry.Register("end_abnormal_match", OnlineAdminApiAdapters.Wrap(EndAbnormalMatchAsync), false);
    }

    /// <summary>
    /// end_abnormal_match：固定拒绝（对局结束属游戏进程权威，admin 面无通道）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>不返回（恒抛 5003）。</returns>
    public static Task<object> EndAbnormalMatchAsync(AdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        // Localization: Online.AdminApi.EndAbnormalMatchForbidden - 管理面不支持结束异常对局；对局生命周期归游戏进程所有。
        throw new OnlineServiceException(OnlineErrorCode.StateOperationForbidden,
                                         LocalizationService.GetString(Localization.Keys.Online.AdminApi.EndAbnormalMatchForbidden));
    }
}
