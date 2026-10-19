// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织及其贡献者不承担任何责任，开发者应自行承担。
//   The organization and contributors assume no responsibility; developers shall bear the responsibility themselves.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:    https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Foundation.Session;
using GameFrameX.Capability.Online.Social;
using GameFrameX.Launcher.Online;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 好友域桥接缝测试（C244）：域契约面装配、域面语义（T2 裁定 R3/R5/R6/R7 的实现行为）、
    /// lease 失效语义与 holder 生命周期、组合根接线发布 / 契约面缺席披露与验证种子。
    /// </summary>
    public class OnlineFriendDomainBridgeTests : IDisposable
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 1;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 1001;

        /// <summary>玩家一。</summary>
        private const long PlayerOne = 1001;

        /// <summary>玩家二。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>玩家三。</summary>
        private const long PlayerThree = 1003;

        public OnlineFriendDomainBridgeTests()
        {
            OnlineFriendDomainBridgeHolder.Retract();
        }

        public void Dispose()
        {
            OnlineFriendDomainBridgeHolder.Retract();
        }

        /// <summary>
        /// 最小组合根（Social 运行时装配所需预置：作用域 + 事件出口 + 幂等 + Foundation 会话面）。
        /// </summary>
        private static CapabilityHostBuilder MinimalRoot()
        {
            var builder = new CapabilityHostBuilder();
            builder.UseService(new OnlineScope(TenantId, AppId, ServerId));
            var eventPublisher = new OnlineEventPublisher(new GameFrameX.Foundation.Idempotency.InMemoryEventPublisher());
            builder.UseService<IOnlineEventPublisher>(eventPublisher);
            builder.UseService(new OnlineIdempotencyService(new GameFrameX.Foundation.Idempotency.IdempotencyCoordinator(
                new GameFrameX.Foundation.Idempotency.InMemoryIdempotencyStore(),
                GameFrameX.Foundation.Idempotency.SystemClock.Instance,
                new GameFrameX.Foundation.Idempotency.IdempotencyOptions())));
            var sessionStore = new InMemoryOnlineSessionStore();
            builder.UseService<IOnlineSessionStore>(sessionStore);
            builder.UseService(new OnlineSessionTokenService(sessionStore, eventPublisher));
            builder.AddModule(new FoundationStubModule());
            return builder;
        }

        /// <summary>
        /// Foundation 桩模块（满足 online-social 的 Requires 拓扑）。
        /// </summary>
        private sealed class FoundationStubModule : ICapabilityModule
        {
            /// <inheritdoc />
            public CapabilityManifest Manifest { get; } = new("online-foundation", "1.0.0", Array.Empty<string>());

            /// <inheritdoc />
            public void Register(CapabilityRegistrationContext context)
            {
            }
        }

        /// <summary>
        /// 构造合法宿主选项。
        /// </summary>
        private static CapabilityHostOptions ValidOptions()
        {
            return new CapabilityHostOptions { TenantId = TenantId, AppId = AppId, ServerId = ServerId };
        }

        /// <summary>
        /// 构造玩家作用域。
        /// </summary>
        private static OnlineScope PlayerScope(long playerId)
        {
            return new OnlineScope(TenantId, AppId, ServerId, playerId);
        }

        /// <summary>
        /// 构造已装配 social 运行时的宿主（不自启：仅验证装配与域面行为）。
        /// </summary>
        private static CapabilityHost BuildSocialHost()
        {
            var builder = MinimalRoot();
            builder.AddOnlineSocialRuntime();
            return builder.Build(ValidOptions());
        }

        /// <summary>
        /// 模块自装配把好友域契约面注册回共享面。
        /// </summary>
        [Fact]
        public void Module_RegistersFriendDomainFace()
        {
            var host = BuildSocialHost();

            Assert.NotNull(host.Services.GetService<IOnlineFriendDomain>());
        }

        /// <summary>
        /// R3：无关系时直接建立 = 申请 + 立即接受（None → Friend）。
        /// </summary>
        [Fact]
        public async Task Face_Establish_FreshPair_ConvergesToFriend()
        {
            var host = BuildSocialHost();
            var domain = host.Services.GetService<IOnlineFriendDomain>();

            var result = await domain.EstablishAsync(PlayerScope(PlayerOne), PlayerTwo);

            Assert.True(result.IsSuccess);
            Assert.Equal(OnlineFriendRelationKind.None, result.Data.Before);
            Assert.Equal(OnlineFriendRelationKind.Friend, result.Data.After);
            var listed = await domain.ListFriendsAsync(PlayerScope(PlayerTwo));
            Assert.Contains(listed.Data, entry => entry.PlayerId == PlayerOne);
        }

        /// <summary>
        /// R3：对方先申请时直接建立 = 本人接受对方申请（IncomingRequest → Friend）。
        /// </summary>
        [Fact]
        public async Task Face_Establish_ReversePending_ActsAsAccept()
        {
            var host = BuildSocialHost();
            var domain = host.Services.GetService<IOnlineFriendDomain>();

            await domain.RequestAsync(PlayerScope(PlayerTwo), PlayerOne);
            var result = await domain.EstablishAsync(PlayerScope(PlayerOne), PlayerTwo);

            Assert.True(result.IsSuccess);
            Assert.Equal(OnlineFriendRelationKind.IncomingRequest, result.Data.Before);
            Assert.Equal(OnlineFriendRelationKind.Friend, result.Data.After);
        }

        /// <summary>
        /// R3：本方申请待答时直接建立 = 以被请求方身份补接受（OutgoingRequest → Friend）。
        /// </summary>
        [Fact]
        public async Task Face_Establish_OwnPending_AutoAccepts()
        {
            var host = BuildSocialHost();
            var domain = host.Services.GetService<IOnlineFriendDomain>();

            await domain.RequestAsync(PlayerScope(PlayerOne), PlayerTwo);
            var result = await domain.EstablishAsync(PlayerScope(PlayerOne), PlayerTwo);

            Assert.True(result.IsSuccess);
            Assert.Equal(OnlineFriendRelationKind.OutgoingRequest, result.Data.Before);
            Assert.Equal(OnlineFriendRelationKind.Friend, result.Data.After);
        }

        /// <summary>
        /// R3：已是好友时直接建立幂等返回（Friend → Friend，适配层据此映射「已是好友」）。
        /// </summary>
        [Fact]
        public async Task Face_Establish_AlreadyFriend_Idempotent()
        {
            var host = BuildSocialHost();
            var domain = host.Services.GetService<IOnlineFriendDomain>();

            await domain.EstablishAsync(PlayerScope(PlayerOne), PlayerTwo);
            var result = await domain.EstablishAsync(PlayerScope(PlayerOne), PlayerTwo);

            Assert.True(result.IsSuccess);
            Assert.Equal(OnlineFriendRelationKind.Friend, result.Data.Before);
            Assert.Equal(OnlineFriendRelationKind.Friend, result.Data.After);
        }

        /// <summary>
        /// R6：请求的幂等收敛分类（新鲜 None → Outgoing；重复 Outgoing → Outgoing；对方先申请得 Incoming）。
        /// </summary>
        [Fact]
        public async Task Face_Request_ConvergesAndClassifiesDirection()
        {
            var host = BuildSocialHost();
            var domain = host.Services.GetService<IOnlineFriendDomain>();

            var fresh = await domain.RequestAsync(PlayerScope(PlayerOne), PlayerTwo);
            Assert.True(fresh.IsSuccess);
            Assert.Equal(OnlineFriendRelationKind.None, fresh.Data.Before);
            Assert.Equal(OnlineFriendRelationKind.OutgoingRequest, fresh.Data.After);

            var repeat = await domain.RequestAsync(PlayerScope(PlayerOne), PlayerTwo);
            Assert.True(repeat.IsSuccess);
            Assert.Equal(OnlineFriendRelationKind.OutgoingRequest, repeat.Data.Before);

            var reverse = await domain.RequestAsync(PlayerScope(PlayerTwo), PlayerOne);
            Assert.True(reverse.IsSuccess);
            Assert.Equal(OnlineFriendRelationKind.IncomingRequest, reverse.Data.Before);

            // 玩家三的新鲜申请不受影响。
            var third = await domain.RequestAsync(PlayerScope(PlayerThree), PlayerOne);
            Assert.True(third.IsSuccess);
            Assert.Equal(OnlineFriendRelationKind.None, third.Data.Before);
        }

        /// <summary>
        /// R7：无待答申请 / 重复答复 / 非被请求方答复 → 显式 ResourceNotFound。
        /// </summary>
        [Fact]
        public async Task Face_Accept_WithoutAnswerablePending_ReturnsResourceNotFound()
        {
            var host = BuildSocialHost();
            var domain = host.Services.GetService<IOnlineFriendDomain>();

            var noRecord = await domain.AcceptAsync(PlayerScope(PlayerOne), PlayerTwo);
            Assert.False(noRecord.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, noRecord.Code);

            // 发起方本人不是被请求方：看不到自己发出的申请。
            await domain.RequestAsync(PlayerScope(PlayerOne), PlayerTwo);
            var selfAnswer = await domain.AcceptAsync(PlayerScope(PlayerOne), PlayerTwo);
            Assert.False(selfAnswer.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, selfAnswer.Code);

            // 首次接受成功，重放（Incoming 已消费）→ ResourceNotFound（R7：重复答复同旧协议「申请不存在」）。
            var accepted = await domain.AcceptAsync(PlayerScope(PlayerTwo), PlayerOne);
            Assert.True(accepted.IsSuccess);
            Assert.Equal(OnlineFriendRelationKind.IncomingRequest, accepted.Data.Before);
            Assert.Equal(OnlineFriendRelationKind.Friend, accepted.Data.After);
            var replay = await domain.AcceptAsync(PlayerScope(PlayerTwo), PlayerOne);
            Assert.False(replay.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, replay.Code);
        }

        /// <summary>
        /// R7：拒绝清理待答（Incoming → None）；拒绝后可重新发起（方向翻转）。
        /// </summary>
        [Fact]
        public async Task Face_Reject_ThenRenew_FlipsDirection()
        {
            var host = BuildSocialHost();
            var domain = host.Services.GetService<IOnlineFriendDomain>();

            await domain.RequestAsync(PlayerScope(PlayerOne), PlayerTwo);
            var rejected = await domain.RejectAsync(PlayerScope(PlayerTwo), PlayerOne);
            Assert.True(rejected.IsSuccess);
            Assert.Equal(OnlineFriendRelationKind.IncomingRequest, rejected.Data.Before);
            Assert.Equal(OnlineFriendRelationKind.None, rejected.Data.After);

            var renewed = await domain.RequestAsync(PlayerScope(PlayerTwo), PlayerOne);
            Assert.True(renewed.IsSuccess);
            Assert.Equal(OnlineFriendRelationKind.None, renewed.Data.Before);
            Assert.Equal(OnlineFriendRelationKind.OutgoingRequest, renewed.Data.After);
        }

        /// <summary>
        /// R7：非好友解除 / 重复解除 → ResourceNotFound；好友解除成功（Friend → None）。
        /// </summary>
        [Fact]
        public async Task Face_Remove_RequiresEstablishedFriendship()
        {
            var host = BuildSocialHost();
            var domain = host.Services.GetService<IOnlineFriendDomain>();

            var notFriend = await domain.RemoveAsync(PlayerScope(PlayerOne), PlayerTwo);
            Assert.False(notFriend.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, notFriend.Code);

            await domain.EstablishAsync(PlayerScope(PlayerOne), PlayerTwo);
            var removed = await domain.RemoveAsync(PlayerScope(PlayerOne), PlayerTwo);
            Assert.True(removed.IsSuccess);
            Assert.Equal(OnlineFriendRelationKind.Friend, removed.Data.Before);
            Assert.Equal(OnlineFriendRelationKind.None, removed.Data.After);

            var replay = await domain.RemoveAsync(PlayerScope(PlayerOne), PlayerTwo);
            Assert.False(replay.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, replay.Code);
        }

        /// <summary>
        /// ListRelations 按玩家视角投影三类关系；ListFriends 只含已建立。
        /// </summary>
        [Fact]
        public async Task Face_ListRelations_ProjectsKinds_And_ListFriends_OnlyEstablished()
        {
            var host = BuildSocialHost();
            var domain = host.Services.GetService<IOnlineFriendDomain>();

            await domain.EstablishAsync(PlayerScope(PlayerOne), PlayerTwo);
            await domain.RequestAsync(PlayerScope(PlayerThree), PlayerOne);

            var relations = await domain.ListRelationsAsync(PlayerScope(PlayerOne));
            Assert.True(relations.IsSuccess);
            Assert.Contains(relations.Data, r => r.OtherPlayerId == PlayerTwo && r.Kind == OnlineFriendRelationKind.Friend);
            Assert.Contains(relations.Data, r => r.OtherPlayerId == PlayerThree && r.Kind == OnlineFriendRelationKind.IncomingRequest);
            Assert.Equal(2, relations.Data.Count);

            var friends = await domain.ListFriendsAsync(PlayerScope(PlayerOne));
            var only = Assert.Single(friends.Data);
            Assert.Equal(PlayerTwo, only.PlayerId);
        }

        /// <summary>
        /// 租约撤销后旧引用一律显式 DependencyUnavailable；再发布替换旧租约。
        /// </summary>
        [Fact]
        public async Task Lease_RetractAndRepublish_FailExplicitly()
        {
            var host = BuildSocialHost();
            var domain = host.Services.GetService<IOnlineFriendDomain>();

            var first = OnlineFriendDomainBridgeHolder.Publish(domain, PlayerScope(0));
            var before = await first.ListFriendsAsync(PlayerScope(PlayerOne));
            Assert.True(before.IsSuccess);

            var second = OnlineFriendDomainBridgeHolder.Publish(domain, PlayerScope(0));
            var staleFirst = await first.ListFriendsAsync(PlayerScope(PlayerOne));
            Assert.False(staleFirst.IsSuccess);
            Assert.Equal(OnlineErrorCode.DependencyUnavailable, staleFirst.Code);

            OnlineFriendDomainBridgeHolder.Retract();
            Assert.Null(OnlineFriendDomainBridgeHolder.Current);
            var staleSecond = await second.ListFriendsAsync(PlayerScope(PlayerOne));
            Assert.False(staleSecond.IsSuccess);
            Assert.Equal(OnlineErrorCode.DependencyUnavailable, staleSecond.Code);
        }

        /// <summary>
        /// 组合根接线：完整宿主发布域桥；契约面缺席（online-social 未装配）披露 Error 且不发布、不抛出。
        /// </summary>
        [Fact]
        public void Wiring_PublishesWithFullHost_And_DisclosesMissingFace()
        {
            var host = BuildSocialHost();

            var lease = OnlineFriendDomainBridgeWiring.PublishFriendDomainBridge(host);
            Assert.NotNull(lease);
            Assert.Same(lease, OnlineFriendDomainBridgeHolder.Current);
            OnlineFriendDomainBridgeWiring.RetractFriendDomainBridge();
            Assert.False(OnlineFriendDomainBridgeHolder.IsPublished);

            var bareBuilder = new CapabilityHostBuilder();
            bareBuilder.UseService(new OnlineScope(TenantId, AppId, ServerId));
            var bareHost = bareBuilder.Build(ValidOptions());
            Assert.Null(OnlineFriendDomainBridgeWiring.PublishFriendDomainBridge(bareHost));
            Assert.False(OnlineFriendDomainBridgeHolder.IsPublished);
        }

        /// <summary>
        /// 验证种子经真实写路径建立非空好友（幂等重放不报错）。
        /// </summary>
        [Fact]
        public async Task Seed_EstablishesFriendshipsThroughRealWritePath()
        {
            var host = BuildSocialHost();

            await OnlineFriendDomainBridgeWiring.SeedFriendDomainBridgeVerificationAsync(host, new[] { PlayerOne, PlayerTwo });
            await OnlineFriendDomainBridgeWiring.SeedFriendDomainBridgeVerificationAsync(host, new[] { PlayerOne, PlayerTwo });

            var domain = host.Services.GetService<IOnlineFriendDomain>();
            var friends = await domain.ListFriendsAsync(PlayerScope(PlayerOne));
            var only = Assert.Single(friends.Data);
            Assert.Equal(PlayerTwo, only.PlayerId);
        }
    }
}
