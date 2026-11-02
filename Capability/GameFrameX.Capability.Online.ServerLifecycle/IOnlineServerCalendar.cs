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
/// 开服日历（按 ServerId 查询开服天数；扩展自 <c>ICrossDay(openServerDay)</c> 的锚点化裁决）。
/// </summary>
public interface IOnlineServerCalendar
{
    /// <summary>
    /// 登记开服锚点（幂等；首次登记发布 <see cref="OnlineServerLifecycleEvents.ServerOpened"/> 能力事件）。
    /// </summary>
    /// <param name="serverId">区服标识。</param>
    /// <param name="openedAtUnixMilliseconds">开服时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效锚点与是否本次首次登记。</returns>
    Task<(OnlineServerAnchor Anchor, bool Registered)> TryRegisterOpenAsync(long serverId, long openedAtUnixMilliseconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找开服锚点。
    /// </summary>
    /// <param name="serverId">区服标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>锚点；未登记返回 null。</returns>
    Task<OnlineServerAnchor> GetAnchorAsync(long serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算开服天数（边界：开服当日 = 第 1 天；锚点未登记返回 0 = 未知，调用方不得以 0 驱动跨天）。
    /// </summary>
    /// <param name="serverId">区服标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>开服天数（第 1 天起算）；未登记锚点返回 0。</returns>
    Task<int> GetOpenServerDayAsync(long serverId, long nowUnixMilliseconds, CancellationToken cancellationToken = default);
}
