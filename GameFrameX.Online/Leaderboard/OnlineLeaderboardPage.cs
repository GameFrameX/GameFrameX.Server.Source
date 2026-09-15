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
using GameFrameX.Online.Contracts;

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 排行榜分页结果（Top N / 翻页共用；keyset 游标，翻页期间新增数据不重复不漏项）。
/// <para>
/// 维护约束：游标由服务端按全序稳定键编码，客户端视为不透明令牌只回传不解释（VC-1.12 稳定性约定沿用）；
/// 翻页期间插入更高名次条目不会使后续页漏项或重复（keyset 语义，区别于偏移量分页）。
/// </para>
/// </summary>
public sealed class OnlineLeaderboardPage
{
    /// <summary>
    /// 获取或设置本页条目（按榜单全序升序名次排列）。
    /// </summary>
    public List<OnlineLeaderboardEntryView> Entries
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置分页游标（末页游标为空字符串）。
    /// </summary>
    public OnlinePageCursor Cursor
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
}
