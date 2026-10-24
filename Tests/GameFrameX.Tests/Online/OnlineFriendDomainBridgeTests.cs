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
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 好友域桥接缝测试（C244 → C294 机制桥）：好友域契约面由 online-social 在 Activate 期经机制桥发布、
    /// 域面语义（T2 裁定 R3/R5/R6/R7 的实现行为）经租约访问、桥生命周期（启动前缺席 / 停止后撤销且旧租约显式失败）、
    /// 契约面缺席披露（未装配 social 即无桥）与验证种子（§4.6 测试夹具 <see cref="FriendBridgeVerificationSeed"/>）。
    /// </summary>
    [Collection(CapabilityRuntimeCollection.Name)]
    public class OnlineFriendDomainBridgeTests
    {
        /// <summary>玩家一。</summary>
        private const long PlayerOne = 1001;

        /// <summary>玩家二。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>玩家三。</summary>
        private const long PlayerThree = 1003;

        /// <summary>
        /// 白名单圈定 foundation + social（好友域桥发布者 = online-social）。
        /// </summary>
        /// <param name="options">组合选项。</param>
        private static void SocialClosure(CapabilityCompositionOptions options)
        {
            options.Filter = new CapabilityAssemblyFilter(new[] { "online-foundation", "online-social" });
        }

        /// <summary>
        /// 构造玩家作用域（三元组与族配置段一致）。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>玩家作用域。</returns>
        private static OnlineScope PlayerScope(long playerId)
        {
            return new OnlineScope(OnlineCapabilityTestHarness.TenantId, OnlineCapabilityTestHarness.AppId, OnlineCapabilityTestHarness.ServerId, playerId);
        }

        /// <summary>
        /// 组合 foundation + social 并启动，经机制桥取好友域契约面执行用例主体（结束恒停止撤桥）。
        /// </summary>
        /// <param name="body">用例主体（入参：好友域契约面、已启动的组合产物）。</param>
        /// <returns>异步任务。</returns>
        private static Task WithStartedFriendDomainAsync(Func<IOnlineFriendDomain, CapabilityRuntime, Task> body)
        {
            return OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                await runtime.Host.StartAsync();
                Assert.True(CapabilityBridge<IOnlineFriendDomain>.TryAcquire(out var lease));
                await body(lease.Contract, runtime);
            }, configure: SocialClosure);
        }

        /// <summary>
        /// 桥生命周期：装配未启动时好友域桥缺席；启动后 Activate 发布好友域桥，租约可取用契约面。
        /// </summary>
        [Fact]
        public async Task Bridge_PublishedOnlyAfterStart()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                Assert.False(CapabilityBridge<IOnlineFriendDomain>.TryAcquire(out _));

                await runtime.Host.StartAsync();

                Assert.True(CapabilityBridge<IOnlineFriendDomain>.TryAcquire(out var lease));
                Assert.NotNull(lease.Contract);
            }, configure: SocialClosure);
        }

        /// <summary>
        /// 桥生命周期：宿主停止自动撤桥——再取租约失败，停止前取得的旧租约访问契约面显式抛
        /// <see cref="CapabilityBridgeUnavailableException"/>；同进程重新组合后发布新桥，旧租约仍失效、新租约可用。
        /// </summary>
        [Fact]
        public async Task Bridge_AfterStop_RetractsAndOldLeaseFailsExplicitly()
        {
            CapabilityBridgeLease<IOnlineFriendDomain> staleLease = null;
            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                await runtime.Host.StartAsync();
                Assert.True(CapabilityBridge<IOnlineFriendDomain>.TryAcquire(out staleLease));
                var before = await staleLease.Contract.ListFriendsAsync(PlayerScope(PlayerOne));
                Assert.True(before.IsSuccess);

                await runtime.StopAsync();

                Assert.False(CapabilityBridge<IOnlineFriendDomain>.TryAcquire(out _));
                Assert.Throws<CapabilityBridgeUnavailableException>(() => staleLease.Contract);
            }, configure: SocialClosure);

            await WithStartedFriendDomainAsync(async (domain, runtime) =>
            {
                Assert.Throws<CapabilityBridgeUnavailableException>(() => staleLease.Contract);
                var fresh = await domain.ListFriendsAsync(PlayerScope(PlayerOne));
                Assert.True(fresh.IsSuccess);
            });
        }

        /// <summary>
        /// 契约面缺席披露：白名单不含 online-social 时启动后好友域桥不发布（消费方按依赖不可用显式失败）。
        /// </summary>
        [Fact]
        public async Task Bridge_WithoutSocial_IsNotPublished()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                await runtime.Host.StartAsync();

                Assert.DoesNotContain("online-social", runtime.Report.EnabledIdsInOrder);
                Assert.False(CapabilityBridge<IOnlineFriendDomain>.TryAcquire(out _));
            }, configure: options => options.Filter = new CapabilityAssemblyFilter(new[] { "online-foundation" }));
        }

        /// <summary>
        /// R3：无关系时直接建立 = 申请 + 立即接受（None → Friend）。
        /// </summary>
        [Fact]
        public async Task Face_Establish_FreshPair_ConvergesToFriend()
        {
            await WithStartedFriendDomainAsync(async (domain, runtime) =>
            {
                var result = await domain.EstablishAsync(PlayerScope(PlayerOne), PlayerTwo);

                Assert.True(result.IsSuccess);
                Assert.Equal(OnlineFriendRelationKind.None, result.Data.Before);
                Assert.Equal(OnlineFriendRelationKind.Friend, result.Data.After);
                var listed = await domain.ListFriendsAsync(PlayerScope(PlayerTwo));
                Assert.Contains(listed.Data, entry => entry.PlayerId == PlayerOne);
            });
        }

        /// <summary>
        /// R3：对方先申请时直接建立 = 本人接受对方申请（IncomingRequest → Friend）。
        /// </summary>
        [Fact]
        public async Task Face_Establish_ReversePending_ActsAsAccept()
        {
            await WithStartedFriendDomainAsync(async (domain, runtime) =>
            {
                await domain.RequestAsync(PlayerScope(PlayerTwo), PlayerOne);
                var result = await domain.EstablishAsync(PlayerScope(PlayerOne), PlayerTwo);

                Assert.True(result.IsSuccess);
                Assert.Equal(OnlineFriendRelationKind.IncomingRequest, result.Data.Before);
                Assert.Equal(OnlineFriendRelationKind.Friend, result.Data.After);
            });
        }

        /// <summary>
        /// R3：本方申请待答时直接建立 = 以被请求方身份补接受（OutgoingRequest → Friend）。
        /// </summary>
        [Fact]
        public async Task Face_Establish_OwnPending_AutoAccepts()
        {
            await WithStartedFriendDomainAsync(async (domain, runtime) =>
            {
                await domain.RequestAsync(PlayerScope(PlayerOne), PlayerTwo);
                var result = await domain.EstablishAsync(PlayerScope(PlayerOne), PlayerTwo);

                Assert.True(result.IsSuccess);
                Assert.Equal(OnlineFriendRelationKind.OutgoingRequest, result.Data.Before);
                Assert.Equal(OnlineFriendRelationKind.Friend, result.Data.After);
            });
        }

        /// <summary>
        /// R3：已是好友时直接建立幂等返回（Friend → Friend，适配层据此映射「已是好友」）。
        /// </summary>
        [Fact]
        public async Task Face_Establish_AlreadyFriend_Idempotent()
        {
            await WithStartedFriendDomainAsync(async (domain, runtime) =>
            {
                await domain.EstablishAsync(PlayerScope(PlayerOne), PlayerTwo);
                var result = await domain.EstablishAsync(PlayerScope(PlayerOne), PlayerTwo);

                Assert.True(result.IsSuccess);
                Assert.Equal(OnlineFriendRelationKind.Friend, result.Data.Before);
                Assert.Equal(OnlineFriendRelationKind.Friend, result.Data.After);
            });
        }

        /// <summary>
        /// R6：请求的幂等收敛分类（新鲜 None → Outgoing；重复 Outgoing → Outgoing；对方先申请得 Incoming）。
        /// </summary>
        [Fact]
        public async Task Face_Request_ConvergesAndClassifiesDirection()
        {
            await WithStartedFriendDomainAsync(async (domain, runtime) =>
            {
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
            });
        }

        /// <summary>
        /// R7：无待答申请 / 重复答复 / 非被请求方答复 → 显式 ResourceNotFound。
        /// </summary>
        [Fact]
        public async Task Face_Accept_WithoutAnswerablePending_ReturnsResourceNotFound()
        {
            await WithStartedFriendDomainAsync(async (domain, runtime) =>
            {
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
            });
        }

        /// <summary>
        /// R7：拒绝清理待答（Incoming → None）；拒绝后可重新发起（方向翻转）。
        /// </summary>
        [Fact]
        public async Task Face_Reject_ThenRenew_FlipsDirection()
        {
            await WithStartedFriendDomainAsync(async (domain, runtime) =>
            {
                await domain.RequestAsync(PlayerScope(PlayerOne), PlayerTwo);
                var rejected = await domain.RejectAsync(PlayerScope(PlayerTwo), PlayerOne);
                Assert.True(rejected.IsSuccess);
                Assert.Equal(OnlineFriendRelationKind.IncomingRequest, rejected.Data.Before);
                Assert.Equal(OnlineFriendRelationKind.None, rejected.Data.After);

                var renewed = await domain.RequestAsync(PlayerScope(PlayerTwo), PlayerOne);
                Assert.True(renewed.IsSuccess);
                Assert.Equal(OnlineFriendRelationKind.None, renewed.Data.Before);
                Assert.Equal(OnlineFriendRelationKind.OutgoingRequest, renewed.Data.After);
            });
        }

        /// <summary>
        /// R7：非好友解除 / 重复解除 → ResourceNotFound；好友解除成功（Friend → None）。
        /// </summary>
        [Fact]
        public async Task Face_Remove_RequiresEstablishedFriendship()
        {
            await WithStartedFriendDomainAsync(async (domain, runtime) =>
            {
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
            });
        }

        /// <summary>
        /// ListRelations 按玩家视角投影三类关系；ListFriends 只含已建立。
        /// </summary>
        [Fact]
        public async Task Face_ListRelations_ProjectsKinds_And_ListFriends_OnlyEstablished()
        {
            await WithStartedFriendDomainAsync(async (domain, runtime) =>
            {
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
            });
        }

        /// <summary>
        /// 验证种子经真实写路径建立非空好友，请求方经好友域桥可见；幂等重放不产生第二条关系、不报错。
        /// </summary>
        [Fact]
        public async Task Seed_EstablishesFriendshipsThroughRealWritePath()
        {
            await WithStartedFriendDomainAsync(async (domain, runtime) =>
            {
                var established = await FriendBridgeVerificationSeed.SeedAsync(runtime.Host, new[] { PlayerOne, PlayerTwo });
                Assert.Equal(1, established);
                await FriendBridgeVerificationSeed.SeedAsync(runtime.Host, new[] { PlayerOne, PlayerTwo });

                var friends = await domain.ListFriendsAsync(PlayerScope(PlayerOne));
                var only = Assert.Single(friends.Data);
                Assert.Equal(PlayerTwo, only.PlayerId);
                var reverse = await domain.ListFriendsAsync(PlayerScope(PlayerTwo));
                Assert.Contains(reverse.Data, entry => entry.PlayerId == PlayerOne);
            });
        }

        /// <summary>
        /// 验证种子多好友：首元素为请求方、其余逐一成友，请求方经好友域桥看到全部种子好友。
        /// </summary>
        [Fact]
        public async Task Seed_MultipleFriends_AllVisibleToRequester()
        {
            await WithStartedFriendDomainAsync(async (domain, runtime) =>
            {
                var established = await FriendBridgeVerificationSeed.SeedAsync(runtime.Host, new[] { PlayerOne, PlayerTwo, PlayerThree });
                Assert.Equal(2, established);

                var friends = await domain.ListFriendsAsync(PlayerScope(PlayerOne));
                Assert.Equal(2, friends.Data.Count);
                Assert.Contains(friends.Data, entry => entry.PlayerId == PlayerTwo);
                Assert.Contains(friends.Data, entry => entry.PlayerId == PlayerThree);
            });
        }

        /// <summary>
        /// 验证种子空列表 / 单元素列表为空操作：不建立任何关系。
        /// </summary>
        [Fact]
        public async Task Seed_EmptyOrSingleList_IsNoOp()
        {
            await WithStartedFriendDomainAsync(async (domain, runtime) =>
            {
                Assert.Equal(0, await FriendBridgeVerificationSeed.SeedAsync(runtime.Host, Array.Empty<long>()));
                Assert.Equal(0, await FriendBridgeVerificationSeed.SeedAsync(runtime.Host, new[] { PlayerOne }));

                var relations = await domain.ListRelationsAsync(PlayerScope(PlayerOne));
                Assert.True(relations.IsSuccess);
                Assert.Empty(relations.Data);
            });
        }
    }
}
