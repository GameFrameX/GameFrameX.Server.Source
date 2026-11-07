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

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Competition.Leaderboard;
using GameFrameX.Capability.Online.Competition.Season;
using GameFrameX.Capability.Online.Competition.Tournament;
using GameFrameX.Localization;

namespace GameFrameX.Capability.Online.Competition;

/// <summary>
/// 竞技能力（赛季 / 锦标赛）的自装配模块——<b>冻结面</b>：两部分均无消费方接线，按包结构收敛裁决标注
/// frozen（不删除、不演进；榜单能力已拆至 <c>online-leaderboard</c> 独立包，LiveOps 运营面移交 Admin 仓）。
/// <para>
/// 维护约束（服务共享协议）：模块无参构造（扫描可发现）；<c>Register</c> 自装配本域——赛季 / 锦标赛两个
/// store 以懒默认描述符注册（族 / 宿主覆盖恒胜出，模块不感知）；对排行榜服务
/// （<see cref="OnlineLeaderboardService"/>，online-leaderboard 必选产物）与发放服务
/// （<see cref="OnlineGrantService"/>，assets 域可选产物）的依赖经 manifest 声明 + Get 解析，
/// 任一缺席时不装配两者（必选缺席 = 装配失败 fail-fast）；对外产物（赛季 / 锦标赛服务）注册回共享面。
/// </para>
/// </summary>
public sealed class OnlineCompetitionCapabilityModule : ICapabilityModule
{
    /// <summary>
    /// 竞技能力清单（Id 冻结 = 终态包名；必选依赖 online-leaderboard（赛季 / 锦标赛的榜单写路径），
    /// 可选依赖 online-assets（奖励发放），缺席时披露对应缺失功能名；无 admin action——
    /// 榜单查询随榜单能力迁移，运营编排随 LiveOps 移交 Admin 仓）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-competition",
        "1.0.0",
        null,
        new[] { "online-leaderboard" },
        new[]
        {
            new CapabilityOptionalDependency("online-assets", Keys.Capability.OnlineModule.Competition.OptionalAssetsAbsent),
        },
        null);

    /// <summary>
    /// 获取竞技能力清单，固定返回静态实例 <see cref="ManifestInstance"/>。
    /// </summary>
    /// <remarks>
    /// Gets the competition capability manifest; always returns the static <see cref="ManifestInstance"/>.
    /// </remarks>
    /// <value>竞技能力清单 / Competition capability manifest</value>
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <summary>
    /// 懒默认注册内存版赛季、锦标赛存储并解析生效实例；排行榜服务与发放服务均在场时再注册赛季服务与锦标赛服务
    /// （必选 online-leaderboard 缺席 = 装配失败，不会进入本方法后的降级分支；可选 assets 缺席时静默跳过）。
    /// </summary>
    /// <remarks>
    /// Registers in-memory season / tournament stores as lazy defaults and resolves the effective instances.
    /// Season and tournament services are registered only when both the leaderboard service (required
    /// online-leaderboard product) and the optional grant service are present.
    /// </remarks>
    /// <param name="context">能力注册上下文 / Capability registration context</param>
    public void Register(CapabilityRegistrationContext context)
    {
        // 两 store 懒默认描述符（族 / 宿主覆盖恒胜出；GetService 命中覆盖或本默认）。
        context.AddService<IOnlineSeasonStore>(static _ => new InMemoryOnlineSeasonStore());
        var seasonStore = context.GetService<IOnlineSeasonStore>();
        context.AddService<IOnlineTournamentStore>(static _ => new InMemoryOnlineTournamentStore());
        var tournamentStore = context.GetService<IOnlineTournamentStore>();

        // 排行榜服务（online-leaderboard 必选产物）与发放服务（assets 域可选产物）：双在场才装配
        // 赛季 / 锦标赛（壳时代 Seasons / Tournaments 置 null 的模块化等价）。
        var leaderboards = context.GetService<OnlineLeaderboardService>();
        var grantService = context.GetOptionalService<OnlineGrantService>();
        if (grantService != null)
        {
            var eventPublisher = context.GetService<IOnlineEventPublisher>();
            context.AddService(new OnlineSeasonService(seasonStore, leaderboards, grantService, eventPublisher));
            context.AddService(new OnlineTournamentService(tournamentStore, leaderboards, grantService, eventPublisher));
        }
    }

    /// <summary>
    /// 激活阶段无需处理（榜单预建与投影随榜单能力迁移至 online-leaderboard 包），直接返回已完成任务。
    /// </summary>
    /// <remarks>
    /// Nothing to do on activation: board pre-building and the settled projection moved to the online-leaderboard package.
    /// </remarks>
    /// <param name="context">能力激活上下文 / Capability activation context</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>已完成的任务 / Completed task</returns>
    public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停用阶段无需处理：本模块不持有需要释放的运行资源，直接返回已完成任务。
    /// </summary>
    /// <remarks>
    /// Nothing to do on deactivation: the module holds no runtime resources.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>已完成的任务 / Completed task</returns>
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
