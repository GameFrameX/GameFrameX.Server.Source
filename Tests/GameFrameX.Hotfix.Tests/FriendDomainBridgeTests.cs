// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
// 
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本组织及其贡献者概不承担。
//  shall be borne by the developer; the project organization and contributors assume no responsibility.
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  CNB  仓库：https://cnb.cool/GameFrameX
//  CNB Repository:    https://cnb.cool/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Hotfix.Logic.Player.Friend;
using GameFrameX.Proto.Proto;
using Xunit;
using FriendDomainBridge = GameFrameX.Hotfix.Logic.Player.Friend.FriendDomainBridge;

namespace GameFrameX.Hotfix.Tests;
/// <summary>
/// 好友域桥薄适配器测试（C244）：120 / 660 两族错误码映射表（T2 裁定 R2-R7）、
/// 作用域玩家位来源、租约失效显式段与异常兜底。
/// </summary>
public sealed class FriendDomainBridgeTests : IDisposable
{
    /// <summary>测试用租户标识。</summary>
    private const long TenantId = 1;

    /// <summary>测试用应用标识。</summary>
    private const long AppId = 1;

    /// <summary>测试用区服标识。</summary>
    private const long ServerId = 1001;

    /// <summary>玩家一（请求方）。</summary>
    private const long PlayerOne = 1001;

    /// <summary>玩家二（被请求方）。</summary>
    private const long PlayerTwo = 1002;

    public FriendDomainBridgeTests()
    {
        OnlineFriendDomainBridgeHolder.Retract();
    }

    public void Dispose()
    {
        OnlineFriendDomainBridgeHolder.Retract();
    }

    /// <summary>
    /// 构造玩家作用域。
    /// </summary>
    private static OnlineScope PlayerScope(long playerId)
    {
        return new OnlineScope(TenantId, AppId, ServerId, playerId);
    }

    [Fact]
    public async Task QueryFriendList_NoLease_ReturnsDependencyUnavailable()
    {
        var response = new RespFriendList();
        await FriendDomainBridge.QueryFriendListAsync(PlayerOne, response);

        Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
        Assert.Empty(response.Friends);
    }

    [Fact]
    public async Task QueryFriendList_Published_MapsEntriesAndFillsSessionPlayerPosition()
    {
        var face = new StubFriendDomain();
        face.Entries.Add(new OnlineFriendEntry { PlayerId = PlayerTwo, Name = "bob" });
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendList();
        await FriendDomainBridge.QueryFriendListAsync(PlayerOne, response);

        Assert.Equal(0, response.ErrorCode);
        var friend = Assert.Single(response.Friends);
        Assert.Equal(PlayerTwo, friend.PlayerId);
        Assert.Equal("bob", friend.PlayerName);
        // 作用域玩家位 = 会话 playerId（R2），请求体 / lease 值均不可覆盖。
        Assert.NotNull(face.LastListFriendsScope);
        Assert.Equal(PlayerOne, face.LastListFriendsScope.PlayerId);
        Assert.Equal(TenantId, face.LastListFriendsScope.TenantId);
    }

    [Fact]
    public async Task QueryFriendList_FaceFailure_MapsOnlineBand()
    {
        var face = new StubFriendDomain { FailWith = OnlineErrorCode.ScopeDenied };
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendList();
        await FriendDomainBridge.QueryFriendListAsync(PlayerOne, response);

        Assert.Equal(-(int)OnlineErrorCode.ScopeDenied, response.ErrorCode);
        Assert.Empty(response.Friends);
    }

    [Fact]
    public async Task QueryFriendList_LeaseRetracted_ReturnsDependencyUnavailable()
    {
        var face = new StubFriendDomain();
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));
        OnlineFriendDomainBridgeHolder.Retract();

        var response = new RespFriendList();
        await FriendDomainBridge.QueryFriendListAsync(PlayerOne, response);

        Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
    }

    [Fact]
    public async Task AddFriendDirect_Self_ReturnsLegacySelfCodeWithoutBridge()
    {
        var face = new StubFriendDomain();
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendByAdd();
        await FriendDomainBridge.AddFriendDirectAsync(PlayerOne, PlayerOne, response);

        Assert.False(response.Success);
        Assert.Equal(-101, response.ErrorCode);
        Assert.Null(face.LastRequestTarget);
    }

    [Fact]
    public async Task AddFriendDirect_InvalidTarget_ReturnsLegacyParameterCode()
    {
        OnlineFriendDomainBridgeHolder.Publish(new StubFriendDomain(), PlayerScope(0));

        var response = new RespFriendByAdd();
        await FriendDomainBridge.AddFriendDirectAsync(PlayerOne, 0, response);

        Assert.False(response.Success);
        Assert.Equal(-100, response.ErrorCode);
    }

    [Fact]
    public async Task AddFriendDirect_Established_ReturnsSuccess()
    {
        var face = new StubFriendDomain { NextChange = new OnlineFriendRelationChange { Before = OnlineFriendRelationKind.None, After = OnlineFriendRelationKind.Friend } };
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendByAdd();
        await FriendDomainBridge.AddFriendDirectAsync(PlayerOne, PlayerTwo, response);

        Assert.True(response.Success);
        Assert.Equal(0, response.ErrorCode);
        Assert.Equal(PlayerTwo, face.LastRequestTarget);
        Assert.Equal(PlayerOne, face.LastRequestScope.PlayerId);
    }

    [Fact]
    public async Task AddFriendDirect_AlreadyFriend_ReturnsLegacyAlreadyFriendsCode()
    {
        var face = new StubFriendDomain { NextChange = new OnlineFriendRelationChange { Before = OnlineFriendRelationKind.Friend, After = OnlineFriendRelationKind.Friend } };
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendByAdd();
        await FriendDomainBridge.AddFriendDirectAsync(PlayerOne, PlayerTwo, response);

        Assert.False(response.Success);
        Assert.Equal(-103, response.ErrorCode);
    }

    [Fact]
    public async Task DeleteFriend_NotFound_ReturnsLegacyRelationMissingCode()
    {
        var face = new StubFriendDomain { FailWith = OnlineErrorCode.ResourceNotFound };
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespDeleteFriend();
        await FriendDomainBridge.DeleteFriendAsync(PlayerOne, PlayerTwo, response);

        Assert.False(response.Success);
        Assert.Equal(-104, response.ErrorCode);
    }

    [Fact]
    public async Task DeleteFriend_Removed_ReturnsSuccess()
    {
        var face = new StubFriendDomain { NextChange = new OnlineFriendRelationChange { Before = OnlineFriendRelationKind.Friend, After = OnlineFriendRelationKind.None } };
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespDeleteFriend();
        await FriendDomainBridge.DeleteFriendAsync(PlayerOne, PlayerTwo, response);

        Assert.True(response.Success);
        Assert.Equal(0, response.ErrorCode);
    }

    [Fact]
    public async Task SendRequest_Self_ReturnsCannotSelfWithoutBridge()
    {
        var face = new StubFriendDomain();
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendSendRequest();
        await FriendDomainBridge.SendRequestAsync(PlayerOne, PlayerOne, response);

        Assert.Equal((int)FriendErrorCode.CannotSelf, response.ErrorCode);
        Assert.Null(face.LastRequestTarget);
    }

    [Theory]
    [InlineData(OnlineFriendRelationKind.None, 0)]
    [InlineData(OnlineFriendRelationKind.Friend, 660003)]
    [InlineData(OnlineFriendRelationKind.OutgoingRequest, 660004)]
    [InlineData(OnlineFriendRelationKind.IncomingRequest, 660004)]
    public async Task SendRequest_MapsBeforeKindToLegacyCodes(OnlineFriendRelationKind before, int expectedCode)
    {
        var face = new StubFriendDomain { NextChange = new OnlineFriendRelationChange { Before = before, After = before == OnlineFriendRelationKind.None ? OnlineFriendRelationKind.OutgoingRequest : before } };
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendSendRequest();
        await FriendDomainBridge.SendRequestAsync(PlayerOne, PlayerTwo, response);

        Assert.Equal(expectedCode, response.ErrorCode);
    }

    [Fact]
    public async Task Accept_PendingMissing_ReturnsRequestNotFound()
    {
        var face = new StubFriendDomain { FailWith = OnlineErrorCode.ResourceNotFound };
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendAccept();
        await FriendDomainBridge.AcceptAsync(PlayerOne, PlayerTwo, response);

        Assert.Equal((int)FriendErrorCode.RequestNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task Accept_Pending_ReturnsZero()
    {
        var face = new StubFriendDomain { NextChange = new OnlineFriendRelationChange { Before = OnlineFriendRelationKind.IncomingRequest, After = OnlineFriendRelationKind.Friend } };
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendAccept();
        await FriendDomainBridge.AcceptAsync(PlayerOne, PlayerTwo, response);

        Assert.Equal(0, response.ErrorCode);
    }

    [Fact]
    public async Task Reject_PendingMissing_ReturnsRequestNotFound()
    {
        var face = new StubFriendDomain { FailWith = OnlineErrorCode.ResourceNotFound };
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendReject();
        await FriendDomainBridge.RejectAsync(PlayerOne, PlayerTwo, response);

        Assert.Equal((int)FriendErrorCode.RequestNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task Remove_Self_ReturnsCannotSelf()
    {
        OnlineFriendDomainBridgeHolder.Publish(new StubFriendDomain(), PlayerScope(0));

        var response = new RespFriendRemove();
        await FriendDomainBridge.RemoveAsync(PlayerOne, PlayerOne, response);

        Assert.Equal((int)FriendErrorCode.CannotSelf, response.ErrorCode);
    }

    [Fact]
    public async Task Remove_NotFriend_ReturnsRequestNotFound()
    {
        var face = new StubFriendDomain { FailWith = OnlineErrorCode.ResourceNotFound };
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendRemove();
        await FriendDomainBridge.RemoveAsync(PlayerOne, PlayerTwo, response);

        Assert.Equal((int)FriendErrorCode.RequestNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task Remove_Friend_ReturnsZero()
    {
        var face = new StubFriendDomain { NextChange = new OnlineFriendRelationChange { Before = OnlineFriendRelationKind.Friend, After = OnlineFriendRelationKind.None } };
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendRemove();
        await FriendDomainBridge.RemoveAsync(PlayerOne, PlayerTwo, response);

        Assert.Equal(0, response.ErrorCode);
    }

    [Fact]
    public async Task ListRelations_ProjectsKindsIntoThreeLists()
    {
        var face = new StubFriendDomain();
        face.Relations.Add(new OnlineFriendRelation { OtherPlayerId = 2001, Kind = OnlineFriendRelationKind.Friend });
        face.Relations.Add(new OnlineFriendRelation { OtherPlayerId = 2002, Kind = OnlineFriendRelationKind.IncomingRequest });
        face.Relations.Add(new OnlineFriendRelation { OtherPlayerId = 2003, Kind = OnlineFriendRelationKind.OutgoingRequest });
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendRelationList();
        await FriendDomainBridge.ListRelationsAsync(PlayerOne, response);

        Assert.Equal(0, response.ErrorCode);
        Assert.Equal(new List<long> { 2001 }, response.Friends);
        Assert.Equal(new List<long> { 2002 }, response.IncomingRequests);
        Assert.Equal(new List<long> { 2003 }, response.OutgoingRequests);
    }

    [Fact]
    public async Task ListRelations_FaceThrows_ReturnsInternalServerError()
    {
        var face = new StubFriendDomain { Throw = true };
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendRelationList();
        await FriendDomainBridge.ListRelationsAsync(PlayerOne, response);

        Assert.Equal((int)OperationStatusCode.InternalServerError, response.ErrorCode);
    }

    [Fact]
    public async Task QueryFriendList_FaceThrows_ReturnsLegacyExceptionCode()
    {
        var face = new StubFriendDomain { Throw = true };
        OnlineFriendDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespFriendList();
        await FriendDomainBridge.QueryFriendListAsync(PlayerOne, response);

        Assert.Equal(-2, response.ErrorCode);
        Assert.Empty(response.Friends);
    }

    /// <summary>
    /// 域契约桩（可配置成功变更 / 失败码 / 抛异常；记录作用域与目标）。
    /// </summary>
    private sealed class StubFriendDomain : IOnlineFriendDomain
    {
        public OnlineFriendRelationChange NextChange { get; set; } = new() { Before = OnlineFriendRelationKind.None, After = OnlineFriendRelationKind.None };

        public OnlineErrorCode FailWith { get; set; }

        public bool Throw { get; set; }

        public List<OnlineFriendEntry> Entries { get; } = new();

        public List<OnlineFriendRelation> Relations { get; } = new();

        public OnlineScope LastListFriendsScope { get; private set; }

        public OnlineScope LastRequestScope { get; private set; }

        public long? LastRequestTarget { get; private set; }

        public Task<OnlineResult<IReadOnlyList<OnlineFriendEntry>>> ListFriendsAsync(OnlineScope scope, CancellationToken cancellationToken = default)
        {
            LastListFriendsScope = scope;
            return Result(scope);
        }

        public Task<OnlineResult<IReadOnlyList<OnlineFriendRelation>>> ListRelationsAsync(OnlineScope scope, CancellationToken cancellationToken = default)
        {
            if (Throw)
            {
                throw new InvalidOperationException("stub");
            }

            IReadOnlyList<OnlineFriendRelation> relations = Relations;
            return Task.FromResult(FailWith == OnlineErrorCode.None
                ? OnlineResult<IReadOnlyList<OnlineFriendRelation>>.Ok(relations)
                : OnlineResult<IReadOnlyList<OnlineFriendRelation>>.Fail(FailWith, "stub failure"));
        }

        public Task<OnlineResult<OnlineFriendRelationChange>> RequestAsync(OnlineScope scope, long targetPlayerId, CancellationToken cancellationToken = default)
        {
            LastRequestScope = scope;
            LastRequestTarget = targetPlayerId;
            return ChangeResult(scope);
        }

        public Task<OnlineResult<OnlineFriendRelationChange>> AcceptAsync(OnlineScope scope, long fromPlayerId, CancellationToken cancellationToken = default)
        {
            return ChangeResult(scope);
        }

        public Task<OnlineResult<OnlineFriendRelationChange>> RejectAsync(OnlineScope scope, long fromPlayerId, CancellationToken cancellationToken = default)
        {
            return ChangeResult(scope);
        }

        public Task<OnlineResult<OnlineFriendRelationChange>> RemoveAsync(OnlineScope scope, long targetPlayerId, CancellationToken cancellationToken = default)
        {
            return ChangeResult(scope);
        }

        public Task<OnlineResult<OnlineFriendRelationChange>> EstablishAsync(OnlineScope scope, long targetPlayerId, CancellationToken cancellationToken = default)
        {
            LastRequestScope = scope;
            LastRequestTarget = targetPlayerId;
            return ChangeResult(scope);
        }

        private Task<OnlineResult<IReadOnlyList<OnlineFriendEntry>>> Result(OnlineScope scope)
        {
            if (Throw)
            {
                throw new InvalidOperationException("stub");
            }

            IReadOnlyList<OnlineFriendEntry> entries = Entries;
            return Task.FromResult(FailWith == OnlineErrorCode.None
                ? OnlineResult<IReadOnlyList<OnlineFriendEntry>>.Ok(entries)
                : OnlineResult<IReadOnlyList<OnlineFriendEntry>>.Fail(FailWith, "stub failure"));
        }

        private Task<OnlineResult<OnlineFriendRelationChange>> ChangeResult(OnlineScope scope)
        {
            if (Throw)
            {
                throw new InvalidOperationException("stub");
            }

            return Task.FromResult(FailWith == OnlineErrorCode.None
                ? OnlineResult<OnlineFriendRelationChange>.Ok(NextChange)
                : OnlineResult<OnlineFriendRelationChange>.Fail(FailWith, "stub failure"));
        }
    }
}
