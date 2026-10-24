// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Online.Competition.Leaderboard;

namespace GameFrameX.Capability.Online.Competition;

/// <summary>
/// 竞技能力包级 Options 根类型（C294：聚合榜单组件参数与 Match.Settled 投影配置；
/// 投影子对象收编族级键 <c>MatchBridgeLeaderboardId</c> → <c>Settings.online-competition.LeaderboardProjection.MatchSettledBoardId</c>，
/// 默认值唯一来源 = 字段初始值）。
/// </summary>
public sealed class OnlineCompetitionOptions : ICapabilityOptions
{
    /// <summary>
    /// 获取或设置排行榜组件选项（防刷阈值 / 限流窗口 / 读缓存 / 分页上限）。
    /// </summary>
    public OnlineLeaderboardOptions Leaderboard { get; set; } = new OnlineLeaderboardOptions();

    /// <summary>
    /// 获取或设置 Match.Settled 写榜投影配置（<c>MatchSettledBoardId</c> 空白 = 不投影不预建）。
    /// </summary>
    public OnlineLeaderboardProjectionOptions LeaderboardProjection { get; set; } = new OnlineLeaderboardProjectionOptions();

    /// <summary>
    /// 校验排行榜组件选项：单次提交最大分数、限流最大提交次数、限流窗口秒数、缓存有效期秒数与最大分页大小均须为正数；投影配置不做校验（空白榜单标识表示不投影）。
    /// </summary>
    /// <remarks>
    /// Validates the leaderboard options: default maximum score per submission, rate-limit maximum submissions, rate-limit window
    /// seconds, cache time-to-live seconds and maximum page size must all be positive. The projection settings are not validated because
    /// a blank board id simply disables the projection. Failures are recorded on the validation collector.
    /// </remarks>
    /// <param name="validation">选项校验结果收集器 / Options validation collector</param>
    public void Validate(CapabilityOptionsValidation validation)
    {
        validation.RequirePositive("Leaderboard.DefaultMaxScorePerSubmission", Leaderboard.DefaultMaxScorePerSubmission);
        validation.RequirePositive("Leaderboard.RateLimitMaxSubmissions", Leaderboard.RateLimitMaxSubmissions);
        validation.RequirePositive("Leaderboard.RateLimitWindowSeconds", Leaderboard.RateLimitWindowSeconds);
        validation.RequirePositive("Leaderboard.CacheTtlSeconds", Leaderboard.CacheTtlSeconds);
        validation.RequirePositive("Leaderboard.MaxPageSize", Leaderboard.MaxPageSize);
    }
}
