// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and other international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the full license text in the LICENSE file located at the root of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activity that endangers national security, disrupts social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of citizens as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织不承担任何责任，开发者应自行承担。
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

using GameFrameX.Capability.Hosting.Abstractions;

namespace GameFrameX.Capability.Online.Matchmaking;

/// <summary>
/// 匹配能力 Options 根类型（机制三层合并的承载面：包内默认 → 配置段 <c>Online.Settings.online-matchmaking</c> → 代码 Configure）。
/// <para>
/// 维护约束：成员默认值唯一来源 = 字段初始值（子选项聚合为属性，子项默认收敛在子 Options 自身）；
/// <see cref="Validate"/> 只做合并终值的结构校验（正数阈值），不做业务裁决；
/// 新增编排参数一律落子 Options 属性，禁止回退平铺配置键。
/// </para>
/// </summary>
/// <remarks>
/// The options root of the matchmaking capability, consumed by the three-layer mechanism merge
/// (package defaults, the configuration section, then code Configure). Default values live only on
/// field initializers; Validate only checks the structural positivity of the merged thresholds.
/// </remarks>
public sealed class OnlineMatchmakingOptions : ICapabilityOptions
{
    /// <summary>
    /// 撮合参数（票据存活 / 等待扩窗 / 频控阈值；默认值唯一来源 = <see cref="OnlineMatchmakerOptions"/> 字段初始值）。
    /// </summary>
    public OnlineMatchmakerOptions Matchmaker { get; set; } = new OnlineMatchmakerOptions();

    /// <summary>
    /// 校验合并后的撮合参数：票据存活时长、等待扩窗阈值、扩窗间隔、每次扩窗评分增量、频控最大操作数与频控窗口均须为正数。
    /// </summary>
    /// <remarks>
    /// Requires ticket time to live, wait expansion threshold, expansion interval, skill expansion per interval, rate limit maximum operations and rate limit window to be positive.
    /// </remarks>
    /// <param name="validation">选项校验收集器 / Options validation collector</param>
    public void Validate(CapabilityOptionsValidation validation)
    {
        validation.RequirePositive("Matchmaker.TicketTimeToLiveSeconds", Matchmaker.TicketTimeToLiveSeconds);
        validation.RequirePositive("Matchmaker.WaitExpansionThresholdSeconds", Matchmaker.WaitExpansionThresholdSeconds);
        validation.RequirePositive("Matchmaker.ExpansionIntervalSeconds", Matchmaker.ExpansionIntervalSeconds);
        validation.RequirePositive("Matchmaker.SkillExpansionPerInterval", Matchmaker.SkillExpansionPerInterval);
        validation.RequirePositive("Matchmaker.RateLimitMaxOperations", Matchmaker.RateLimitMaxOperations);
        validation.RequirePositive("Matchmaker.RateLimitWindowSeconds", Matchmaker.RateLimitWindowSeconds);
    }
}
