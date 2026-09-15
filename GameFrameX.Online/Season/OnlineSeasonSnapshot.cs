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
using GameFrameX.Online.Leaderboard;

namespace GameFrameX.Online.Season;

/// <summary>
/// 赛季历史快照（vault:C8 S7.3：赛季重置的审计依据与旧榜回溯载体）。
/// <para>
/// 维护约束（红线）：
/// (1) 快照是**重置前**的冻结事实——在榜单被清空**之前**持久化（快照先行），一旦写成就与后续榜单状态无关：
/// 新一届的写入不得回流本快照（赛季结束不删除历史数据，VC-7.5）；
/// (2) 快照是**赛季结算的唯一依据**：名次取自冻结时的全序（<see cref="OnlineLeaderboardEntryView.Rank"/>），
/// 结算绝不回落实时榜单（重置后榜单为空）；
/// (3) 名次口径与 C102 <see cref="OnlineLeaderboardOrdering"/> 全序一致（分数按榜向 → 更新时间 → 玩家标识），
/// 保证快照名次与重置前查询结果逐项可复算。
/// </para>
/// </summary>
public sealed class OnlineSeasonSnapshot
{
    /// <summary>
    /// 获取或设置赛季标识（快照的归属键）。
    /// </summary>
    public string SeasonId
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
    /// 获取或设置快照来源的榜单标识。
    /// </summary>
    public string LeaderboardId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置快照生成时刻（UTC 毫秒；审计依据）。
    /// </summary>
    public long SnapshottedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置冻结的全序条目（含名次；空榜为合法快照，表示本届无成绩）。
    /// </summary>
    public List<OnlineLeaderboardEntryView> Entries
    {
        get;
        set;
    }

    /// <summary>
    /// 复制快照（条目逐项深拷贝，存储出入参防御性拷贝）。
    /// </summary>
    /// <returns>快照副本。</returns>
    public OnlineSeasonSnapshot Copy()
    {
        var entries = new List<OnlineLeaderboardEntryView>();
        if (Entries != null)
        {
            foreach (var view in Entries)
            {
                if (view != null)
                {
                    entries.Add(view.Copy());
                }
            }
        }

        return new OnlineSeasonSnapshot
        {
            SeasonId = SeasonId,
            TenantId = TenantId,
            AppId = AppId,
            LeaderboardId = LeaderboardId,
            SnapshottedTime = SnapshottedTime,
            Entries = entries,
        };
    }
}
