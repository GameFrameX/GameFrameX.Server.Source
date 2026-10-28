//  ==========================================================================================
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
//   Any legal disputes or liabilities arising from secondary development of this project
//   本组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Foundation.Identity;

/// <summary>
/// 已解绑身份跨账号改绑的一次性授权票据（由原账号已登录会话显式签发）。
/// <para>
/// 维护约束（红线）：票据是易失凭证——进程内承载、短有效期（默认 5 分钟）、
/// 消费式单次使用（验证即从登记表移除，无论后续改绑 CAS 是否成功）；
/// 票据只授权「签发账号名下指定身份」的一次改绑，不含任何会话或凭据语义；
/// 登记键 <see cref="Code"/> 为 256 位随机不透明串，票据标识 <see cref="TicketId"/>
/// 供审计留痕，两者独立——秘密串不落审计。
/// </para>
/// </summary>
public sealed class OnlineIdentityRebindAuthorization
{
    /// <summary>
    /// 获取或设置票据秘密串（"rebind-" 前缀 + 64 位十六进制；登记与验证键，不得写入审计或日志）。
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// 获取或设置票据标识（"rebind-auth-" 前缀 + GUID；审计留痕用，与秘密串独立）。
    /// </summary>
    public string TicketId { get; set; }

    /// <summary>
    /// 获取或设置租户标识（票据作用域：必须与被改绑身份一致）。
    /// </summary>
    public long TenantId { get; set; }

    /// <summary>
    /// 获取或设置应用标识（票据作用域：必须与被改绑身份一致）。
    /// </summary>
    public long AppId { get; set; }

    /// <summary>
    /// 获取或设置身份类型（票据作用域：必须与被改绑身份一致）。
    /// </summary>
    public OnlineIdentityKind Kind { get; set; }

    /// <summary>
    /// 获取或设置身份标识串（票据作用域：必须与被改绑身份一致）。
    /// </summary>
    public string Identifier { get; set; }

    /// <summary>
    /// 获取或设置签发账号标识（原账号；被改绑身份在签发时必须归属该账号）。
    /// </summary>
    public long IssuerGameAccountId { get; set; }

    /// <summary>
    /// 获取或设置过期时间（Unix 毫秒；过期票据验证拒绝并移除）。
    /// </summary>
    public long ExpiresAtTime { get; set; }
}
