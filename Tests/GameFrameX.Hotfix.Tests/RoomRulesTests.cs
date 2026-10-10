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

using GameFrameX.Apps.ServerRole.Room.Entity;
using GameFrameX.Hotfix.Logic.ServerRole.Room;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Room 业务纯规则测试（C197 Phase 2）。
/// </summary>
/// <remarks>
/// 覆盖容量校验边界、容量归一化与最低负载选房（同载小 id、跳过已满、无房返回 null）。
/// </remarks>
public class RoomRulesTests
{
    /// <summary>
    /// 容量校验：0（默认语义）与 1-20 合法，负数与超上限非法。
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(20, true)]
    [InlineData(-1, false)]
    [InlineData(21, false)]
    [InlineData(int.MaxValue, false)]
    public void IsCapacityValid_Boundary(int capacity, bool expected)
    {
        Assert.Equal(expected, RoomRules.IsCapacityValid(capacity));
    }

    /// <summary>
    /// 容量归一化：0 取默认 4，其余原样。
    /// </summary>
    [Theory]
    [InlineData(0, RoomRules.DefaultCapacity)]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    public void NormalizeCapacity_DefaultsToFour(int capacity, int expected)
    {
        Assert.Equal(expected, RoomRules.NormalizeCapacity(capacity));
    }

    /// <summary>
    /// 最低负载选房：选占用最少者；同载取房间ID较小者；已满房间不参与。
    /// </summary>
    [Fact]
    public void PickRoom_PrefersLowestLoadThenSmallerId()
    {
        var rooms = new Dictionary<long, RoomSlotState>
        {
            { 10001, new RoomSlotState { RoomId = 10001, Capacity = 4, PlayerCount = 3 } },
            { 10002, new RoomSlotState { RoomId = 10002, Capacity = 4, PlayerCount = 4 } },
            { 10003, new RoomSlotState { RoomId = 10003, Capacity = 4, PlayerCount = 1 } },
            { 10004, new RoomSlotState { RoomId = 10004, Capacity = 4, PlayerCount = 1 } },
        };

        var picked = RoomRules.PickRoom(rooms);
        Assert.NotNull(picked);
        Assert.Equal(10003, picked.RoomId);
    }

    /// <summary>
    /// 无可分配房间：全部已满或房间表为空时返回 null。
    /// </summary>
    [Fact]
    public void PickRoom_ReturnsNullWhenAllFullOrEmpty()
    {
        Assert.Null(RoomRules.PickRoom(new Dictionary<long, RoomSlotState>()));

        var full = new Dictionary<long, RoomSlotState>
        {
            { 10001, new RoomSlotState { RoomId = 10001, Capacity = 1, PlayerCount = 1 } },
        };
        Assert.Null(RoomRules.PickRoom(full));
    }
}
