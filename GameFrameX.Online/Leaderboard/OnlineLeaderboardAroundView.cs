// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
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

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 玩家附近排名视图（以目标玩家为中心的连续名次窗口，窗口含目标玩家本人）。
/// <para>
/// 维护约束：窗口在榜单边界处截断——榜首请求的前向窗口与榜尾请求的后向窗口返回实际存在的条目，
/// 不以占位条目填充（VC-7.3 附近排名边界）；窗口内名次连续（rank 单调 +1），供客户端直接渲染。
/// </para>
/// </summary>
public sealed class OnlineLeaderboardAroundView
{
    /// <summary>
    /// 获取或设置目标玩家标识。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置目标玩家名次（从 1 开始）。
    /// </summary>
    public int Rank
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置榜单条目总数（当前快照口径）。
    /// </summary>
    public long TotalCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置附近窗口条目（含目标玩家，按名次升序；窗口边界处截断）。
    /// </summary>
    public List<OnlineLeaderboardEntryView> Around
    {
        get;
        set;
    }
}
