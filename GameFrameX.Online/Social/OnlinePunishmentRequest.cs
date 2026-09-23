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
//   please see the LICENSE file in the root directory of the source code.
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

namespace GameFrameX.Online.Social;

/// <summary>
/// 处罚施加请求（<see cref="OnlinePunishmentService.ApplyAsync"/> 的唯一提交形态）。
/// <para>
/// 维护约束：请求只表达「对谁施加什么处罚」；处罚标识、落库时刻与修订号一律由服务生成，
/// 调用方不可指定（避免伪造/复用处罚标识）。
/// </para>
/// </summary>
public sealed class OnlinePunishmentRequest
{
    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    /// <remarks>Gets or sets the tenant id.</remarks>
    public long TenantId { get; init; }

    /// <summary>
    /// 获取或设置 App 标识。
    /// </summary>
    /// <remarks>Gets or sets the app id.</remarks>
    public long AppId { get; init; }

    /// <summary>
    /// 获取或设置被处罚玩家标识。
    /// </summary>
    /// <remarks>Gets or sets the punished player id.</remarks>
    public long PlayerId { get; init; }

    /// <summary>
    /// 获取或设置处罚种类。
    /// </summary>
    /// <remarks>Gets or sets the punishment kind.</remarks>
    public OnlinePunishmentKind Kind { get; init; }

    /// <summary>
    /// 获取或设置处罚原因（必填）。
    /// </summary>
    /// <remarks>Gets or sets the punishment reason (required).</remarks>
    public string Reason { get; init; }

    /// <summary>
    /// 获取或设置生效时刻（UTC 毫秒；<c>0</c> 表示立即生效）。
    /// </summary>
    /// <remarks>Gets or sets the effective time (UTC milliseconds; 0 = immediate).</remarks>
    public long EffectiveAtTime { get; init; }

    /// <summary>
    /// 获取或设置失效时刻（UTC 毫秒；<c>0</c> 表示永久处罚）。
    /// </summary>
    /// <remarks>Gets or sets the expiry time (UTC milliseconds; 0 = permanent).</remarks>
    public long ExpiresAtTime { get; init; }

    /// <summary>
    /// 获取或设置施加处罚的 Admin 标识。
    /// </summary>
    /// <remarks>Gets or sets the admin id applying the punishment.</remarks>
    public long AdminId { get; init; }

    /// <summary>
    /// 获取或设置来源案件标识（可空；非举报来源不填）。
    /// </summary>
    /// <remarks>Gets or sets the source admin case id (optional; empty for non-report sources).</remarks>
    public string AdminCaseId { get; init; }

    /// <summary>
    /// 获取或设置关联标识（可空）。
    /// </summary>
    /// <remarks>Gets or sets the correlation id (optional).</remarks>
    public string CorrelationId { get; init; }
}
