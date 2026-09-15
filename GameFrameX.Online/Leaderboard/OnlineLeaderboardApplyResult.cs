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

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 存储临界区内的单笔写入结果（新条目 / 聚合后条目 + 临界区内事实的前值，供事件载荷使用）。
/// <para>
/// 维护约束：<see cref="OldScore"/> 与 <see cref="Entry"/> 均取自同一临界区快照——并发写入下
/// 事件载荷的前后值也是真实前后值，不得由服务层先读后写拼装（会被并发交错撕裂）。
/// </para>
/// </summary>
public sealed class OnlineLeaderboardApplyResult
{
    /// <summary>
    /// 获取或设置写入后的条目副本（含聚合分数与提交计数）。
    /// </summary>
    public OnlineLeaderboardEntry Entry
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置写入前分数（首笔为 0；临界区内事实）。
    /// </summary>
    public long OldScore
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置是否为本玩家首笔写入（此前未上榜）。
    /// </summary>
    public bool IsNewEntry
    {
        get;
        set;
    }
}
