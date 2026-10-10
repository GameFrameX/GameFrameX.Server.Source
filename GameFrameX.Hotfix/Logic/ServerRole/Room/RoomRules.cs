// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
// 
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
// 
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
// 
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
// 
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System.Collections.Generic;
using System.Linq;
using GameFrameX.Apps.ServerRole.Room.Entity;

namespace GameFrameX.Hotfix.Logic.ServerRole.Room;

/// <summary>
/// 房间分配业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 承载与存储无关的房间槽位规则：容量校验与最低负载选房。
/// Agent 只做状态编排，规则判定一律走本类（<c>MailCampaignFilter</c> 先例）。
/// </remarks>
internal static class RoomRules
{
    /// <summary>
    /// 默认房间容量（请求 Capacity 为 0 时采用）。
    /// </summary>
    public const int DefaultCapacity = 4;

    /// <summary>
    /// 房间容量上限。
    /// </summary>
    public const int MaxCapacity = 20;

    /// <summary>
    /// 判定请求容量是否合法：1-20（0 表示默认容量，由调用方归一化，不算非法）。
    /// </summary>
    /// <param name="capacity">请求容量。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsCapacityValid(int capacity)
    {
        return capacity >= 0 && capacity <= MaxCapacity;
    }

    /// <summary>
    /// 归一化请求容量：0 取默认容量 4；其余原样返回（调用前先校验合法性）。
    /// </summary>
    /// <param name="capacity">请求容量。</param>
    /// <returns>归一化后的容量。</returns>
    public static int NormalizeCapacity(int capacity)
    {
        return capacity == 0 ? DefaultCapacity : capacity;
    }

    /// <summary>
    /// 最低负载选房：在未满房间中选择占用数最小者，同载取房间ID较小者。
    /// </summary>
    /// <param name="rooms">全部房间槽位。</param>
    /// <returns>目标房间；无未满房间返回 null。</returns>
    public static RoomSlotState PickRoom(IReadOnlyDictionary<long, RoomSlotState> rooms)
    {
        RoomSlotState best = null;
        foreach (var room in rooms.Values.Where(slot => slot.PlayerCount < slot.Capacity))
        {
            if (best == null || room.PlayerCount < best.PlayerCount || (room.PlayerCount == best.PlayerCount && room.RoomId < best.RoomId))
            {
                best = room;
            }
        }

        return best;
    }
}
