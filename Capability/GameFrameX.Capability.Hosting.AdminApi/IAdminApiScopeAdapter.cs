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
