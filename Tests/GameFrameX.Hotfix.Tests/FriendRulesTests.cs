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

using GameFrameX.Apps.ServerRole.Friend.Entity;
using GameFrameX.Hotfix.Logic.ServerRole.Friend;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Friend 业务纯规则测试（C197 Phase 2）。
/// </summary>
/// <remarks>
/// 覆盖好友申请约束集（自己 / 已好友 / 已在申请）、成友双向清理与解除双向清理语义。
/// </remarks>
public class FriendRulesTests
{
    /// <summary>
    /// 申请约束：不能加自己、已是好友、已有待处理申请均拒绝。
    /// </summary>
    [Fact]
    public void CanSendRequest_ConstraintSet()
    {
        var profile = new FriendProfileState { PlayerId = 1 };
        Assert.False(FriendRules.CanSendRequest(1, 1, profile));

        profile.Friends.Add(2);
        Assert.False(FriendRules.CanSendRequest(1, 2, profile));
        Assert.True(FriendRules.CanSendRequest(1, 3, profile));

        profile.Outgoing.Add(3);
        Assert.False(FriendRules.CanSendRequest(1, 3, profile));
        Assert.True(FriendRules.CanSendRequest(1, 4, profile));
    }

    /// <summary>
    /// 成友：双向 Friends 建立并去重，双向申请全部清理。
    /// </summary>
    [Fact]
    public void Befriend_EstablishesBidirectionalAndClearsRequests()
    {
        var acceptor = new FriendProfileState
        {
            PlayerId = 1,
            Incoming = new List<long> { 2, 3 },
        };
        var requester = new FriendProfileState
        {
            PlayerId = 2,
            Outgoing = new List<long> { 1 },
        };

        FriendRules.Befriend(acceptor, requester);
        FriendRules.Befriend(acceptor, requester);

        Assert.Equal(new long[] { 2 }, acceptor.Friends);
        Assert.Equal(new long[] { 1 }, requester.Friends);
        Assert.Equal(new long[] { 3 }, acceptor.Incoming);
        Assert.Empty(requester.Outgoing);
    }

    /// <summary>
    /// 解除好友：双向 Friends 解除，且清理双向残留申请。
    /// </summary>
    [Fact]
    public void Unfriend_RemovesBidirectionalAndResidualRequests()
    {
        var self = new FriendProfileState
        {
            PlayerId = 1,
            Friends = new List<long> { 2 },
            Outgoing = new List<long> { 2 },
        };
        var other = new FriendProfileState
        {
            PlayerId = 2,
            Friends = new List<long> { 1 },
            Incoming = new List<long> { 1 },
        };

        FriendRules.Unfriend(self, other);
        Assert.Empty(self.Friends);
        Assert.Empty(other.Friends);
        Assert.Empty(self.Outgoing);
        Assert.Empty(other.Incoming);
    }

    /// <summary>
    /// 好友 / 申请判定：与列表内容一致（集合化判重）。
    /// </summary>
    [Fact]
    public void IsFriend_And_HasOutgoingRequest()
    {
        var profile = new FriendProfileState { PlayerId = 1 };
        profile.Friends.Add(2);
        profile.Outgoing.Add(3);

        Assert.True(FriendRules.IsFriend(profile, 2));
        Assert.False(FriendRules.IsFriend(profile, 3));
        Assert.True(FriendRules.HasOutgoingRequest(profile, 3));
        Assert.False(FriendRules.HasOutgoingRequest(profile, 2));
    }
}
