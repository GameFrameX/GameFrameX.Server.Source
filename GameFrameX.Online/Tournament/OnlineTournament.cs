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
//   please see the LICENSE file in the root directory of the source code for the full license text.
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
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Generic;

namespace GameFrameX.Online.Tournament;

/// <summary>
/// 赛事定义（vault:C8 S7.4：开始/结束时间、关联榜单、资格条件、赛事奖励与结算状态）。
/// <para>
/// 维护约束（红线）：
/// (1) 作用域 = (TenantId, AppId)，与 C102 榜单 / C103 赛季同口径（App 内跨区服聚合）——跨 App / 跨租户读写
/// 一律查不到（ResourceNotFound 反预言，对齐 C94/C99/C102/C103 先例）；
/// (2) 赛事本身**不承载分数与名次**：成绩只存在于冻结的 <see cref="OnlineTournamentStandings"/> 中，
/// 赛事只描述周期、门槛与奖励口径；
/// (3) 赛事**只读关联榜单、从不写入或重置**——榜单的写入归 C102 可信链路、重置归 C103 赛季；
/// 同一榜单可被赛季与赛事共用，赛事若也重置榜单会清空对方数据（本 change P0-1）；
/// (4) 资格条件与奖励规则创建后固化，运行期不可变更（配置变更与结算并发的冻结要求，vault:C8 风险表）；
/// (5) <see cref="StartTime"/> / <see cref="EndTime"/> 是**排期元数据**，由运行时装配（X4）的调度器到点驱动
/// 开始/结束，本类型不以时钟做隐式迁移。
/// </para>
/// </summary>
public sealed class OnlineTournament
{
    /// <summary>
    /// 获取或设置赛事标识（App 内唯一，创建方提供）。
    /// </summary>
    public string TournamentId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识（作用域隔离键）。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识（作用域隔离键）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置关联榜单标识（报名资格判定与结束成绩冻结的唯一数据来源；本赛事只读该榜）。
    /// </summary>
    public string LeaderboardId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置赛事开始时刻（UTC 毫秒；排期元数据，不触发隐式迁移）。
    /// </summary>
    public long StartTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置赛事结束时刻（UTC 毫秒；排期元数据，不触发隐式迁移）。
    /// </summary>
    public long EndTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置报名资格条件（创建后固化）。
    /// </summary>
    public OnlineTournamentEligibility Eligibility
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置赛事奖励规则（名次区间 → 资产变更行；创建后固化）。
    /// </summary>
    public List<OnlineTournamentRewardRule> RewardRules
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置当前生命周期状态（只经状态机迁移）。
    /// </summary>
    public OnlineTournamentState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置创建时刻（UTC 毫秒）。
    /// </summary>
    public long CreatedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置开始时刻（UTC 毫秒；未开始为 0；开始即关闭报名窗口）。
    /// </summary>
    public long StartedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置结束（成绩冻结）时刻（UTC 毫秒；未结束为 0）。
    /// </summary>
    public long EndedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置结算完成时刻（UTC 毫秒；未结算为 0）。
    /// </summary>
    public long SettledTime
    {
        get;
        set;
    }

    /// <summary>
    /// 复制赛事定义（资格条件与奖励规则深拷贝，存储出入参防御性拷贝）。
    /// </summary>
    /// <returns>赛事定义副本。</returns>
    public OnlineTournament Copy()
    {
        var rules = new List<OnlineTournamentRewardRule>();
        if (RewardRules != null)
        {
            foreach (var rule in RewardRules)
            {
                if (rule != null)
                {
                    rules.Add(rule.Copy());
                }
            }
        }

        return new OnlineTournament
        {
            TournamentId = TournamentId,
            TenantId = TenantId,
            AppId = AppId,
            LeaderboardId = LeaderboardId,
            StartTime = StartTime,
            EndTime = EndTime,
            Eligibility = Eligibility == null ? null : Eligibility.Copy(),
            RewardRules = rules,
            State = State,
            CreatedTime = CreatedTime,
            StartedTime = StartedTime,
            EndedTime = EndedTime,
            SettledTime = SettledTime,
        };
    }
}
