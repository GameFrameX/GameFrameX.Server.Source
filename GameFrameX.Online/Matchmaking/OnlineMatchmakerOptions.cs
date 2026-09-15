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

namespace GameFrameX.Online.Matchmaking;

/// <summary>
/// 匹配与限流可配置项（vault:C5 风险表：规则不硬编码、限流阈值可配置且需验误伤率）。
/// <para>
/// 维护约束：所有阈值必须可由装配方覆盖——压测要同时验证「刷 Ticket 被拦」与「正常玩家不受影响」，
/// 阈值写死则该验证无法进行。默认值只保证单机测试可用，不代表线上取值。
/// </para>
/// </summary>
public sealed class OnlineMatchmakerOptions
{
    /// <summary>
    /// 获取或设置票据存活时长（秒；默认 300）。到点未成组转 Expired（VC-4.9）。
    /// </summary>
    public int TicketTimeToLiveSeconds
    {
        get;
        set;
    } = 300;

    /// <summary>
    /// 获取或设置等待时间扩展阈值（秒；默认 30）。等待超过该值后技能区间按扩展间隔放宽（VC-4.5）。
    /// </summary>
    public int WaitExpansionThresholdSeconds
    {
        get;
        set;
    } = 30;

    /// <summary>
    /// 获取或设置技能区间扩展间隔（秒；默认 15）。每满一个间隔放宽一档。
    /// </summary>
    public int ExpansionIntervalSeconds
    {
        get;
        set;
    } = 15;

    /// <summary>
    /// 获取或设置单档扩展量（默认 50）。
    /// </summary>
    public int SkillExpansionPerInterval
    {
        get;
        set;
    } = 50;

    /// <summary>
    /// 获取或设置限流窗口内允许的入队/取消次数（默认 10）。
    /// </summary>
    public int RateLimitMaxOperations
    {
        get;
        set;
    } = 10;

    /// <summary>
    /// 获取或设置限流窗口时长（秒；默认 10）。
    /// </summary>
    public int RateLimitWindowSeconds
    {
        get;
        set;
    } = 10;

    /// <summary>
    /// 复制配置（装配方传递后不被后续改动影响）。
    /// </summary>
    /// <returns>配置副本。</returns>
    public OnlineMatchmakerOptions Copy()
    {
        return new OnlineMatchmakerOptions
        {
            TicketTimeToLiveSeconds = TicketTimeToLiveSeconds,
            WaitExpansionThresholdSeconds = WaitExpansionThresholdSeconds,
            ExpansionIntervalSeconds = ExpansionIntervalSeconds,
            SkillExpansionPerInterval = SkillExpansionPerInterval,
            RateLimitMaxOperations = RateLimitMaxOperations,
            RateLimitWindowSeconds = RateLimitWindowSeconds,
        };
    }
}
