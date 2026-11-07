// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯玩家合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   侵犯他人合法权益等法律法规所禁止的行为！
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.ServerLifecycle;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 开服锚点（区服生命周期的第一等事实：ServerId 登记 + 开服时刻）。
/// </summary>
/// <remarks>
/// 开服天数全部由此锚点推导（<see cref="IOnlineServerCalendar.GetOpenServerDayAsync"/>），
/// 合服后源服锚点保留并登记 <see cref="MergedIntoServerId"/>（历史锚点只读，供追溯与跨服对账）。
/// </remarks>
public sealed class OnlineServerAnchor
{
    /// <summary>
    /// 区服标识（正数）。
    /// </summary>
    public long ServerId { get; set; }

    /// <summary>
    /// 开服时刻（UTC 毫秒；开服天数推导基准）。
    /// </summary>
    public long OpenedAtUnixMilliseconds { get; set; }

    /// <summary>
    /// 锚点登记时刻（UTC 毫秒）。
    /// </summary>
    public long RegisteredAtUnixMilliseconds { get; set; }

    /// <summary>
    /// 合服归入的目标区服标识（0 = 未合入）。
    /// </summary>
    public long MergedIntoServerId { get; set; }

    /// <summary>
    /// 是否已被合服归并（<see cref="MergedIntoServerId"/> 非 0）。
    /// </summary>
    public bool IsMerged
    {
        get { return MergedIntoServerId != 0; }
    }
}
