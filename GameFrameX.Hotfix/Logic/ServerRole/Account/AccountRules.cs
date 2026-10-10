// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
// 
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
// 
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
// 
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
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

using System;
using System.Security.Cryptography;
using System.Text;

namespace GameFrameX.Hotfix.Logic.ServerRole.Account;

/// <summary>
/// 账号业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 承载与存储无关的账号规则：账号名/密码合法性、盐生成与密码哈希。
/// Agent 只做状态编排，规则判定一律走本类（<c>MailCampaignFilter</c> 先例）。
/// </remarks>
internal static class AccountRules
{
    /// <summary>
    /// 账号名最小长度。
    /// </summary>
    public const int MinNameLength = 4;

    /// <summary>
    /// 账号名最大长度。
    /// </summary>
    public const int MaxNameLength = 32;

    /// <summary>
    /// 密码最小长度。
    /// </summary>
    public const int MinPasswordLength = 6;

    /// <summary>
    /// 判定账号名是否合法：长度 4-32 且非纯空白。
    /// </summary>
    /// <param name="accountName">账号名。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsNameValid(string accountName)
    {
        return !string.IsNullOrWhiteSpace(accountName)
               && accountName.Length >= MinNameLength
               && accountName.Length <= MaxNameLength;
    }

    /// <summary>
    /// 判定密码是否合法：非空且长度不小于 6。
    /// </summary>
    /// <param name="password">明文密码。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsPasswordValid(string password)
    {
        return !string.IsNullOrEmpty(password) && password.Length >= MinPasswordLength;
    }

    /// <summary>
    /// 生成每账号随机盐（Guid N 格式 32 位十六进制）。
    /// </summary>
    /// <returns>盐字符串。</returns>
    public static string CreateSalt()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// 计算密码哈希：SHA256(salt + password) 的十六进制小写串。
    /// </summary>
    /// <param name="salt">盐。</param>
    /// <param name="password">明文密码。</param>
    /// <returns>64 位十六进制哈希串。</returns>
    public static string HashPassword(string salt, string password)
    {
        var bytes = Encoding.UTF8.GetBytes(salt + password);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
