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
//   Any legal disputes and liabilities arising from secondary development based on this project
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

using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Online.Tokens;

/// <summary>
/// 会话 Token 契约承载（vault:C2 S1.4）。
/// 仅声明 Server 侧会话 Token 的签发 / 刷新 / 吊销 / 踢下线四端契约；具体实现（存储、加密、过期策略）由 C94 会话管理任务落地，本基座不提供默认实现。
/// </summary>
public interface IOnlineSessionTokenContract
{
    /// <summary>
    /// 签发会话 Token。
    /// </summary>
    /// <param name="request">签发请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>签发结果（Token、会话标识、过期时刻）。</returns>
    Task<OnlineTokenIssueResult> IssueAsync(OnlineTokenIssueRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 刷新会话 Token（原 Token 随刷新失效）。
    /// </summary>
    /// <param name="request">刷新请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>刷新结果（新 Token、新过期时刻）。</returns>
    Task<OnlineTokenRefreshResult> RefreshAsync(OnlineTokenRefreshRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 吊销会话 Token（主动登出、管理员强制下线）。
    /// </summary>
    /// <param name="request">吊销请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>吊销结果（目标会话不存在或已吊销时 <c>Revoked</c> 为 <c>false</c>）。</returns>
    Task<OnlineTokenRevokeResult> RevokeAsync(OnlineTokenRevokeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 踢下线（同账号多端互踢、风控强制下线）。
    /// </summary>
    /// <param name="request">踢下线请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>踢下线结果（目标会话不存在或已离线时 <c>Kicked</c> 为 <c>false</c>）。</returns>
    Task<OnlineTokenKickResult> KickAsync(OnlineTokenKickRequest request, CancellationToken cancellationToken = default);
}
