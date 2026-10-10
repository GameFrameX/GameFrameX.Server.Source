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

using GameFrameX.Apps.ServerRole.Room.Component;
using GameFrameX.Apps.ServerRole.Room.Entity;
using GameFrameX.Foundation.Utility;

namespace GameFrameX.Hotfix.Logic.ServerRole.Room;

/// <summary>
/// 房间分配服务器（Room Role，域 710，专属服槽位语义，非 Game 房间玩法）业务组件代理：槽位分配、释放与查询。
/// </summary>
/// <remarks>
/// Room Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="RoomRules"/>（纯函数，可单测）；
/// 本类只做状态编排：幂等分配、最低负载选房、全满开新房、释放清映射。
/// </remarks>
public class RoomComponentAgent : StateComponentAgent<RoomComponent, RoomState>
{
    /// <summary>
    /// 分配房间槽位：重复分配幂等返回原房；否则最低负载选房，全满开新房（容量 = 请求值或默认 4）。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">分配请求。</param>
    /// <param name="response">分配响应。</param>
    public Task OnAllocateAsync(long playerId, ReqRoomAllocate request, RespRoomAllocate response)
    {
        if (State.PlayerRoom.TryGetValue(playerId, out var existingRoomId) && State.Rooms.TryGetValue(existingRoomId, out var existing))
        {
            response.RoomId = existing.RoomId;
            response.PlayerCount = existing.PlayerCount;
            return Task.CompletedTask;
        }

        if (!RoomRules.IsCapacityValid(request.Capacity))
        {
            response.ErrorCode = (int)RoomErrorCode.CapacityInvalid;
            return Task.CompletedTask;
        }

        var room = RoomRules.PickRoom(State.Rooms);
        if (room == null)
        {
            room = new RoomSlotState
            {
                RoomId = State.NextRoomId++,
                Capacity = RoomRules.NormalizeCapacity(request.Capacity),
                CreatedUnixTime = TimerHelper.UnixTimeSeconds(),
            };
            State.Rooms[room.RoomId] = room;
        }

        room.PlayerCount++;
        State.PlayerRoom[playerId] = room.RoomId;
        response.RoomId = room.RoomId;
        response.PlayerCount = room.PlayerCount;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 释放房间槽位：清玩家映射并计数-1（计数归零房间保留可复用）；未分配返回 <see cref="RoomErrorCode.NotAllocated"/>。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">释放请求。</param>
    /// <param name="response">释放响应。</param>
    public Task OnReleaseAsync(long playerId, ReqRoomRelease request, RespRoomRelease response)
    {
        if (!State.PlayerRoom.TryGetValue(playerId, out var roomId) || !State.Rooms.TryGetValue(roomId, out var room))
        {
            response.ErrorCode = (int)RoomErrorCode.NotAllocated;
            return Task.CompletedTask;
        }

        room.PlayerCount = System.Math.Max(0, room.PlayerCount - 1);
        State.PlayerRoom.Remove(playerId);
        response.RoomId = roomId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询全部房间槽位。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">查询请求。</param>
    /// <param name="response">查询响应。</param>
    public Task OnQueryAsync(long playerId, ReqRoomQuery request, RespRoomQuery response)
    {
        foreach (var room in State.Rooms.Values)
        {
            response.Rooms.Add(new RoomSlotInfo
            {
                RoomId = room.RoomId,
                Capacity = room.Capacity,
                PlayerCount = room.PlayerCount,
            });
        }

        return Task.CompletedTask;
    }
}
