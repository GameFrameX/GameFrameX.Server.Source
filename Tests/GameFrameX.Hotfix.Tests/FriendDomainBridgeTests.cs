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
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Hotfix.Logic.Player.Friend;
using GameFrameX.Proto.Proto;
using Xunit;
using FriendDomainBridge = GameFrameX.Hotfix.Logic.Player.Friend.FriendDomainBridge;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 好友域桥薄适配器测试（C244 立项，C294 改为能力桥契约）：120 / 660 两族错误码映射表（T2 裁定 R2-R7）、
/// 作用域三元组来自授权作用域提供者桥 + 玩家位来自会话、桥缺席 / 撤销显式段与异常兜底。
/// 桥由桩能力模块在真实组合的激活期发布，宿主停机自动撤销（<see cref="HotfixBridgeHarness"/>）。
/// </summary>
[Collection(HotfixBridgeTestCollection.Name)]
public sealed class FriendDomainBridgeTests
{
    /// <summary>玩家一（请求方）。</summary>
    private const long PlayerOne = 1001;

    /// <summary>玩家二（被请求方）。</summary>
    private const long PlayerTwo = 1002;

    /// <summary>
    /// 好友域桥未发布：列表查询按依赖不可用显式失败，好友列表为空。
    /// </summary>
    [Fact]
    public async Task QueryFriendList_NoBridge_ReturnsDependencyUnavailable()
    {
        var response = new RespFriendList();
        await FriendDomainBridge.QueryFriendListAsync(PlayerOne, response);

        Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
        Assert.Empty(response.Friends);
    }

    /// <summary>
    /// 好友域桥在场：条目逐字段映射；作用域三元组取授权提供者，玩家位取会话 playerId（R2）。
    /// </summary>
    [Fact]
    public async Task QueryFriendList_Published_MapsEntriesAndFillsSessionPlayerPosition()
    {
        var face = new StubFriendDomain();
        face.Entries.Add(new OnlineFriendEntry { PlayerId = PlayerTwo, Name = "bob" });
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendList();
            await FriendDomainBridge.QueryFriendListAsync(PlayerOne, response);

            Assert.Equal(0, response.ErrorCode);
            var friend = Assert.Single(response.Friends);
            Assert.Equal(PlayerTwo, friend.PlayerId);
            Assert.Equal("bob", friend.PlayerName);
            // 作用域玩家位 = 会话 playerId（R2），请求体 / 授权提供者的玩家位均不可覆盖。
            Assert.NotNull(face.LastListFriendsScope);
            Assert.Equal(PlayerOne, face.LastListFriendsScope.PlayerId);
            Assert.Equal(HotfixBridgeStubScopeProvider.TenantId, face.LastListFriendsScope.TenantId);
            Assert.Equal(HotfixBridgeStubScopeProvider.AppId, face.LastListFriendsScope.AppId);
            Assert.Equal(HotfixBridgeStubScopeProvider.ServerId, face.LastListFriendsScope.ServerId);
        });
    }

    /// <summary>
    /// 域面失败码走 Online 显式段（取负）。
    /// </summary>
    [Fact]
    public async Task QueryFriendList_FaceFailure_MapsOnlineBand()
    {
        var face = new StubFriendDomain { FailWith = OnlineErrorCode.ScopeDenied };
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendList();
            await FriendDomainBridge.QueryFriendListAsync(PlayerOne, response);

            Assert.Equal(-(int)OnlineErrorCode.ScopeDenied, response.ErrorCode);
            Assert.Empty(response.Friends);
        });
    }

    /// <summary>
    /// 宿主停机撤桥后再请求：按依赖不可用显式失败（旧「租约撤销」语义 = 宿主 StopAsync）。
    /// </summary>
    [Fact]
    public async Task QueryFriendList_BridgeRetractedByHostStop_ReturnsDependencyUnavailable()
    {
        var face = new StubFriendDomain();
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            await runtime.StopAsync();

            var response = new RespFriendList();
            await FriendDomainBridge.QueryFriendListAsync(PlayerOne, response);

            Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
            Assert.Equal(0, face.CallCount);
        });
    }

    /// <summary>
    /// 好友域桥在场但授权作用域提供者桥缺席：作用域三元组不可得，按适配器契约
    /// （FriendDomainBridge.RequireLease / OnlineAuthorizedScopeReader 红线 ③）以依赖不可用显式失败，域面不被调用。
    /// </summary>
    [Fact]
    public async Task QueryFriendList_ScopeProviderAbsent_ReturnsDependencyUnavailableWithoutCallingFace()
    {
        var face = new StubFriendDomain();
        face.Entries.Add(new OnlineFriendEntry { PlayerId = PlayerTwo, Name = "bob" });
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendList();
            await FriendDomainBridge.QueryFriendListAsync(PlayerOne, response);

            Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
            Assert.Empty(response.Friends);
            Assert.Equal(0, face.CallCount);
        }, publishScopeProvider: false);
    }

    /// <summary>
    /// 直接添加自己：本地返回旧 -101，不触达域桥。
    /// </summary>
    [Fact]
    public async Task AddFriendDirect_Self_ReturnsLegacySelfCodeWithoutBridge()
    {
        var face = new StubFriendDomain();
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendByAdd();
            await FriendDomainBridge.AddFriendDirectAsync(PlayerOne, PlayerOne, response);

            Assert.False(response.Success);
            Assert.Equal(-101, response.ErrorCode);
            Assert.Null(face.LastRequestTarget);
        });
    }

    /// <summary>
    /// 直接添加非法目标：本地返回旧参数错误 -100。
    /// </summary>
    [Fact]
    public async Task AddFriendDirect_InvalidTarget_ReturnsLegacyParameterCode()
    {
        await HotfixBridgeHarness.RunWithFriendDomainAsync(new StubFriendDomain(), async runtime =>
        {
            var response = new RespFriendByAdd();
            await FriendDomainBridge.AddFriendDirectAsync(PlayerOne, 0, response);

            Assert.False(response.Success);
            Assert.Equal(-100, response.ErrorCode);
        });
    }

    /// <summary>
    /// 直接添加建立成功：Success=true，目标与作用域玩家位如实传入域面（R3：Establish）。
    /// </summary>
    [Fact]
    public async Task AddFriendDirect_Established_ReturnsSuccess()
    {
        var face = new StubFriendDomain { NextChange = new OnlineFriendRelationChange { Before = OnlineFriendRelationKind.None, After = OnlineFriendRelationKind.Friend } };
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendByAdd();
            await FriendDomainBridge.AddFriendDirectAsync(PlayerOne, PlayerTwo, response);

            Assert.True(response.Success);
            Assert.Equal(0, response.ErrorCode);
            Assert.Equal(PlayerTwo, face.LastRequestTarget);
            Assert.Equal(PlayerOne, face.LastRequestScope.PlayerId);
        });
    }

    /// <summary>
    /// 直接添加已是好友：旧 -103（Success=false）。
    /// </summary>
    [Fact]
    public async Task AddFriendDirect_AlreadyFriend_ReturnsLegacyAlreadyFriendsCode()
    {
        var face = new StubFriendDomain { NextChange = new OnlineFriendRelationChange { Before = OnlineFriendRelationKind.Friend, After = OnlineFriendRelationKind.Friend } };
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendByAdd();
            await FriendDomainBridge.AddFriendDirectAsync(PlayerOne, PlayerTwo, response);

            Assert.False(response.Success);
            Assert.Equal(-103, response.ErrorCode);
        });
    }

    /// <summary>
    /// 删除不存在的好友关系：旧 -104。
    /// </summary>
    [Fact]
    public async Task DeleteFriend_NotFound_ReturnsLegacyRelationMissingCode()
    {
        var face = new StubFriendDomain { FailWith = OnlineErrorCode.ResourceNotFound };
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespDeleteFriend();
            await FriendDomainBridge.DeleteFriendAsync(PlayerOne, PlayerTwo, response);

            Assert.False(response.Success);
            Assert.Equal(-104, response.ErrorCode);
        });
    }

    /// <summary>
    /// 删除好友成功：Success=true。
    /// </summary>
    [Fact]
    public async Task DeleteFriend_Removed_ReturnsSuccess()
    {
        var face = new StubFriendDomain { NextChange = new OnlineFriendRelationChange { Before = OnlineFriendRelationKind.Friend, After = OnlineFriendRelationKind.None } };
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespDeleteFriend();
            await FriendDomainBridge.DeleteFriendAsync(PlayerOne, PlayerTwo, response);

            Assert.True(response.Success);
            Assert.Equal(0, response.ErrorCode);
        });
    }

    /// <summary>
    /// 向自己发申请：本地返回 660002，不触达域桥。
    /// </summary>
    [Fact]
    public async Task SendRequest_Self_ReturnsCannotSelfWithoutBridge()
    {
        var face = new StubFriendDomain();
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendSendRequest();
            await FriendDomainBridge.SendRequestAsync(PlayerOne, PlayerOne, response);

            Assert.Equal((int)FriendErrorCode.CannotSelf, response.ErrorCode);
            Assert.Null(face.LastRequestTarget);
        });
    }

    /// <summary>
    /// 发申请按操作前关系分类映射 660 族旧码（R5/R6：已是好友 660003，任一方向待答 660004）。
    /// </summary>
    [Theory]
    [InlineData(OnlineFriendRelationKind.None, 0)]
    [InlineData(OnlineFriendRelationKind.Friend, 660003)]
    [InlineData(OnlineFriendRelationKind.OutgoingRequest, 660004)]
    [InlineData(OnlineFriendRelationKind.IncomingRequest, 660004)]
    public async Task SendRequest_MapsBeforeKindToLegacyCodes(OnlineFriendRelationKind before, int expectedCode)
    {
        var face = new StubFriendDomain { NextChange = new OnlineFriendRelationChange { Before = before, After = before == OnlineFriendRelationKind.None ? OnlineFriendRelationKind.OutgoingRequest : before } };
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendSendRequest();
            await FriendDomainBridge.SendRequestAsync(PlayerOne, PlayerTwo, response);

            Assert.Equal(expectedCode, response.ErrorCode);
        });
    }

    /// <summary>
    /// 接受不存在的申请：660005（R7）。
    /// </summary>
    [Fact]
    public async Task Accept_PendingMissing_ReturnsRequestNotFound()
    {
        var face = new StubFriendDomain { FailWith = OnlineErrorCode.ResourceNotFound };
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendAccept();
            await FriendDomainBridge.AcceptAsync(PlayerOne, PlayerTwo, response);

            Assert.Equal((int)FriendErrorCode.RequestNotFound, response.ErrorCode);
        });
    }

    /// <summary>
    /// 接受待答申请：成功码 0。
    /// </summary>
    [Fact]
    public async Task Accept_Pending_ReturnsZero()
    {
        var face = new StubFriendDomain { NextChange = new OnlineFriendRelationChange { Before = OnlineFriendRelationKind.IncomingRequest, After = OnlineFriendRelationKind.Friend } };
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendAccept();
            await FriendDomainBridge.AcceptAsync(PlayerOne, PlayerTwo, response);

            Assert.Equal(0, response.ErrorCode);
        });
    }

    /// <summary>
    /// 拒绝不存在的申请：660005。
    /// </summary>
    [Fact]
    public async Task Reject_PendingMissing_ReturnsRequestNotFound()
    {
        var face = new StubFriendDomain { FailWith = OnlineErrorCode.ResourceNotFound };
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendReject();
            await FriendDomainBridge.RejectAsync(PlayerOne, PlayerTwo, response);

            Assert.Equal((int)FriendErrorCode.RequestNotFound, response.ErrorCode);
        });
    }

    /// <summary>
    /// 解除与自己的关系：本地返回 660002。
    /// </summary>
    [Fact]
    public async Task Remove_Self_ReturnsCannotSelf()
    {
        await HotfixBridgeHarness.RunWithFriendDomainAsync(new StubFriendDomain(), async runtime =>
        {
            var response = new RespFriendRemove();
            await FriendDomainBridge.RemoveAsync(PlayerOne, PlayerOne, response);

            Assert.Equal((int)FriendErrorCode.CannotSelf, response.ErrorCode);
        });
    }

    /// <summary>
    /// 解除非好友关系：660005（R7）。
    /// </summary>
    [Fact]
    public async Task Remove_NotFriend_ReturnsRequestNotFound()
    {
        var face = new StubFriendDomain { FailWith = OnlineErrorCode.ResourceNotFound };
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendRemove();
            await FriendDomainBridge.RemoveAsync(PlayerOne, PlayerTwo, response);

            Assert.Equal((int)FriendErrorCode.RequestNotFound, response.ErrorCode);
        });
    }

    /// <summary>
    /// 解除好友关系成功：成功码 0。
    /// </summary>
    [Fact]
    public async Task Remove_Friend_ReturnsZero()
    {
        var face = new StubFriendDomain { NextChange = new OnlineFriendRelationChange { Before = OnlineFriendRelationKind.Friend, After = OnlineFriendRelationKind.None } };
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendRemove();
            await FriendDomainBridge.RemoveAsync(PlayerOne, PlayerTwo, response);

            Assert.Equal(0, response.ErrorCode);
        });
    }

    /// <summary>
    /// 全量关系按分类投影为好友 / 收到申请 / 发出申请三列表。
    /// </summary>
    [Fact]
    public async Task ListRelations_ProjectsKindsIntoThreeLists()
    {
        var face = new StubFriendDomain();
        face.Relations.Add(new OnlineFriendRelation { OtherPlayerId = 2001, Kind = OnlineFriendRelationKind.Friend });
        face.Relations.Add(new OnlineFriendRelation { OtherPlayerId = 2002, Kind = OnlineFriendRelationKind.IncomingRequest });
        face.Relations.Add(new OnlineFriendRelation { OtherPlayerId = 2003, Kind = OnlineFriendRelationKind.OutgoingRequest });
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendRelationList();
            await FriendDomainBridge.ListRelationsAsync(PlayerOne, response);

            Assert.Equal(0, response.ErrorCode);
            Assert.Equal(new List<long> { 2001 }, response.Friends);
            Assert.Equal(new List<long> { 2002 }, response.IncomingRequests);
            Assert.Equal(new List<long> { 2003 }, response.OutgoingRequests);
        });
    }

    /// <summary>
    /// 全量关系查询域面抛异常：660 族按 InternalServerError 兜底。
    /// </summary>
    [Fact]
    public async Task ListRelations_FaceThrows_ReturnsInternalServerError()
    {
        var face = new StubFriendDomain { Throw = true };
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendRelationList();
            await FriendDomainBridge.ListRelationsAsync(PlayerOne, response);

            Assert.Equal((int)OperationStatusCode.InternalServerError, response.ErrorCode);
        });
    }

    /// <summary>
    /// 好友列表查询域面抛异常：120 族按旧异常码 -2 兜底，列表为空。
    /// </summary>
    [Fact]
    public async Task QueryFriendList_FaceThrows_ReturnsLegacyExceptionCode()
    {
        var face = new StubFriendDomain { Throw = true };
        await HotfixBridgeHarness.RunWithFriendDomainAsync(face, async runtime =>
        {
            var response = new RespFriendList();
            await FriendDomainBridge.QueryFriendListAsync(PlayerOne, response);

            Assert.Equal(-2, response.ErrorCode);
            Assert.Empty(response.Friends);
        });
    }

    /// <summary>
    /// 域契约桩（可配置成功变更 / 失败码 / 抛异常；记录作用域、目标与调用次数）。
    /// </summary>
    private sealed class StubFriendDomain : IOnlineFriendDomain
    {
        /// <summary>变更类调用的成功回执。</summary>
        public OnlineFriendRelationChange NextChange { get; set; } = new() { Before = OnlineFriendRelationKind.None, After = OnlineFriendRelationKind.None };

        /// <summary>失败码（None = 成功）。</summary>
        public OnlineErrorCode FailWith { get; set; }

        /// <summary>是否抛异常。</summary>
        public bool Throw { get; set; }

        /// <summary>好友列表条目。</summary>
        public List<OnlineFriendEntry> Entries { get; } = new();

        /// <summary>全量关系条目。</summary>
        public List<OnlineFriendRelation> Relations { get; } = new();

        /// <summary>域面被调用的总次数。</summary>
        public int CallCount { get; private set; }

        /// <summary>最近一次好友列表查询作用域。</summary>
        public OnlineScope LastListFriendsScope { get; private set; }

        /// <summary>最近一次申请 / 建立作用域。</summary>
        public OnlineScope LastRequestScope { get; private set; }

        /// <summary>最近一次申请 / 建立目标。</summary>
        public long? LastRequestTarget { get; private set; }

        public Task<OnlineResult<IReadOnlyList<OnlineFriendEntry>>> ListFriendsAsync(OnlineScope scope, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastListFriendsScope = scope;
            return Result();
        }

        public Task<OnlineResult<IReadOnlyList<OnlineFriendRelation>>> ListRelationsAsync(OnlineScope scope, CancellationToken cancellationToken = default)
        {
            CallCount++;
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
            return ChangeResult();
        }

        public Task<OnlineResult<OnlineFriendRelationChange>> AcceptAsync(OnlineScope scope, long fromPlayerId, CancellationToken cancellationToken = default)
        {
            return ChangeResult();
        }

        public Task<OnlineResult<OnlineFriendRelationChange>> RejectAsync(OnlineScope scope, long fromPlayerId, CancellationToken cancellationToken = default)
        {
            return ChangeResult();
        }

        public Task<OnlineResult<OnlineFriendRelationChange>> RemoveAsync(OnlineScope scope, long targetPlayerId, CancellationToken cancellationToken = default)
        {
            return ChangeResult();
        }

        public Task<OnlineResult<OnlineFriendRelationChange>> EstablishAsync(OnlineScope scope, long targetPlayerId, CancellationToken cancellationToken = default)
        {
            LastRequestScope = scope;
            LastRequestTarget = targetPlayerId;
            return ChangeResult();
        }

        private Task<OnlineResult<IReadOnlyList<OnlineFriendEntry>>> Result()
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

        private Task<OnlineResult<OnlineFriendRelationChange>> ChangeResult()
        {
            CallCount++;
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
