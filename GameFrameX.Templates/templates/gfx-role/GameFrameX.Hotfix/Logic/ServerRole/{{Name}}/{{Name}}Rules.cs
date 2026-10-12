// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
//
//  使用本项目须严格遵守相应法律法规与开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file at the root of the source code for the full license text.
//
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations.
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes or liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

namespace GameFrameX.Hotfix.Logic.ServerRole.{{Name}};

/// <summary>
/// {{Name}} 业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 骨架规则（gfx-role 模板生成）：仅含回显文本校验样例，按业务替换/扩充；
/// Agent 只做状态编排，规则判定一律走本类（<c>ChatRules</c> / <c>MailCampaignFilter</c> 先例）。
/// </remarks>
internal static class {{Name}}Rules
{
    /// <summary>
    /// 单条回显文本长度上限（字符）——样板常量，按业务替换。
    /// </summary>
    public const int MaxTextLength = 200;

    /// <summary>
    /// 判定回显文本是否合法：非空白且不超过长度上限。
    /// </summary>
    /// <param name="text">回显文本。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsTextValid(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextLength;
    }
}
