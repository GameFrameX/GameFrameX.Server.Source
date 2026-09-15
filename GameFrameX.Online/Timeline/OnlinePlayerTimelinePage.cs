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

using System.Collections.Generic;
using GameFrameX.Online.Contracts;

namespace GameFrameX.Online.Timeline;

/// <summary>
/// 玩家时间线分页结果。
/// <para>
/// 维护约束：翻页语义沿用 C93 <see cref="OnlinePageCursor"/>——游标按「发生时刻倒序 → 行标识序数升序」的全序编码，
/// 翻页期间新写入的行不会造成重复或漏项（偏移分页做不到这一点，故不提供）。
/// 末页 <see cref="Page"/> 的游标为空字符串且 HasMore 为 false。
/// </para>
/// </summary>
public sealed class OnlinePlayerTimelinePage
{
    /// <summary>
    /// 获取本页事件行（按发生时刻倒序；同一秒内按行标识序数升序）。
    /// </summary>
    public IReadOnlyList<OnlinePlayerTimelineEntry> Entries
    {
        get;
    }

    /// <summary>
    /// 获取分页游标（末页 HasMore = false 且游标为空字符串）。
    /// </summary>
    public OnlinePageCursor Page
    {
        get;
    }

    /// <summary>
    /// 构造时间线分页结果。
    /// </summary>
    /// <param name="entries">本页事件行。</param>
    /// <param name="page">分页游标。</param>
    public OnlinePlayerTimelinePage(IReadOnlyList<OnlinePlayerTimelineEntry> entries, OnlinePageCursor page)
    {
        Entries = entries;
        Page = page;
    }
}
