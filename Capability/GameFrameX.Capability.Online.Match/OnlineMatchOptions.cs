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

using GameFrameX.Capability.Hosting.Abstractions;

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 对局能力 Options 根类型（机制三层合并的承载面：包内默认 → 配置段 <c>Online.Settings.online-match</c> → 代码 Configure）。
/// <para>
/// 维护约束：成员默认值唯一来源 = 字段初始值（子选项聚合为属性，子项默认收敛在子 Options 自身）；
/// <see cref="Validate"/> 只做合并终值的结构校验（正数阈值），不做业务裁决；
/// 新增编排参数一律落子 Options 属性，禁止回退平铺配置键。
/// </para>
/// </summary>
/// <remarks>
/// The options root of the match capability, consumed by the three-layer mechanism merge
/// (package defaults, the configuration section, then code Configure). Default values live only on
/// field initializers; Validate only checks the structural positivity of the merged thresholds.
/// </remarks>
public sealed class OnlineMatchOptions : ICapabilityOptions
{
    /// <summary>
    /// 对局运行时阈值（等待 / 进行中超时、重连窗口、结算留存、服务端事件容量；默认值唯一来源 =
    /// <see cref="OnlineMatchRuntimeOptions"/> 字段初始值）。
    /// </summary>
    public OnlineMatchRuntimeOptions Runtime { get; set; } = new OnlineMatchRuntimeOptions();

    /// <summary>
    /// 结算编排阈值（发奖重投尝试上限；默认值唯一来源 =
    /// <see cref="OnlineMatchSettlementOptions"/> 字段初始值）。
    /// </summary>
    public OnlineMatchSettlementOptions Settlement { get; set; } = new OnlineMatchSettlementOptions();

    /// <summary>
    /// 校验合并后的运行时与结算阈值：等待超时、进行中超时、重连窗口、结束留存时长、单局服务端事件上限与发奖投递尝试上限均须为正数。
    /// </summary>
    /// <remarks>
    /// Requires waiting timeout, running timeout, reconnect window, ended retention, maximum server events per match and the maximum grant attempts to be positive.
    /// </remarks>
    /// <param name="validation">选项校验收集器 / Options validation collector</param>
    public void Validate(CapabilityOptionsValidation validation)
    {
        validation.RequirePositive("Runtime.WaitingTimeoutSeconds", Runtime.WaitingTimeoutSeconds);
        validation.RequirePositive("Runtime.RunningTimeoutSeconds", Runtime.RunningTimeoutSeconds);
        validation.RequirePositive("Runtime.ReconnectWindowSeconds", Runtime.ReconnectWindowSeconds);
        validation.RequirePositive("Runtime.EndedRetentionSeconds", Runtime.EndedRetentionSeconds);
        validation.RequirePositive("Settlement.MaxGrantAttempts", Settlement.MaxGrantAttempts);
    }
}
