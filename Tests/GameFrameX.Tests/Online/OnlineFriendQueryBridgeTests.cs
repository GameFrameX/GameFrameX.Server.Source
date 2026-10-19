// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers shall bear the responsibility themselves.
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Foundation.Session;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Online.Social;
using GameFrameX.Launcher.Online;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 好友查询桥接缝测试（C243）：契约面装配与映射、作用域隔离、lease 失效语义、
    /// holder 发布/撤销生命周期、真实组合根可解析、组合根接线 fail-fast 与验证种子。
    /// </summary>
    public class OnlineFriendQueryBridgeTests : IDisposable
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

        public OnlineFriendQueryBridgeTests()
        {
            OnlineFriendBridgeHolder.Retract();
        }

        public void Dispose()
        {
            OnlineFriendBridgeHolder.Retract();
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
        /// Foundation 桩模块（满足 online-social 的 Requires 拓扑；会话面预置由 MinimalRoot 承担）。
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
        /// 模块自装配把好友查询契约面注册回共享面，且按服务语义映射条目字段
        ///（预置非空好友数据 → 面返回同名目条目，字段逐一对齐）。
        /// </summary>
        [Fact]
        public async Task Module_RegistersFriendQueryFace_MappingServiceSemantics()
        {
            var builder = MinimalRoot();
            builder.AddOnlineSocialRuntime();
            var host = builder.Build(ValidOptions());

            var face = host.Services.GetService<IOnlineFriendQuery>();
            Assert.NotNull(face);

            var service = host.Services.GetService<OnlineFriendService>();
            Assert.True((await service.RequestAsync(PlayerScope(PlayerOne), PlayerTwo)).IsSuccess);
            Assert.True((await service.AcceptAsync(PlayerScope(PlayerTwo), (await service.RequestAsync(PlayerScope(PlayerOne), PlayerTwo)).Data.FriendshipId)).IsSuccess);

            var result = await face.ListFriendsAsync(PlayerScope(PlayerOne));
            Assert.True(result.IsSuccess);
            var entry = Assert.Single(result.Data);
            Assert.Equal(PlayerTwo, entry.PlayerId);
            Assert.Equal(PlayerTwo.ToString(), entry.Name); // 未装配目录 → 展示名回退标识字面量（C242 披露态）
            Assert.False(entry.Online); // 未装配探针 → 恒 false（披露态）
            Assert.Equal(0, entry.ServerId); // 归属区服经玩家目录解析，目录缺席为 0（C242 披露态，非错库）
            Assert.True(entry.EstablishedAtTime > 0);
        }

        /// <summary>
        /// 作用域隔离经契约面仍然生效：不同租户的同玩家标识互不可见。
        /// </summary>
        [Fact]
        public async Task FriendQueryFace_ScopeIsolation_PreservedThroughFace()
        {
            var builder = MinimalRoot();
            builder.AddOnlineSocialRuntime();
            var host = builder.Build(ValidOptions());

            var service = host.Services.GetService<OnlineFriendService>();
            var request = await service.RequestAsync(PlayerScope(PlayerOne), PlayerTwo);
            Assert.True(request.IsSuccess);
            Assert.True((await service.AcceptAsync(PlayerScope(PlayerTwo), request.Data.FriendshipId)).IsSuccess);

            var face = host.Services.GetService<IOnlineFriendQuery>();
            var sameScope = await face.ListFriendsAsync(PlayerScope(PlayerOne));
            var crossTenant = await face.ListFriendsAsync(new OnlineScope(TenantId + 1, AppId, ServerId, PlayerOne));

            Assert.Single(sameScope.Data);
            Assert.Empty(crossTenant.Data);
        }

        /// <summary>
        /// lease 失效语义：撤销（Deactivate）后旧引用调用返回显式 DependencyUnavailable，
        /// 不回旧数据、不抛未分类异常。
        /// </summary>
        [Fact]
        public async Task BridgeLease_AfterDeactivate_ReturnsExplicitDependencyUnavailable()
        {
            var builder = MinimalRoot();
            builder.AddOnlineSocialRuntime();
            var host = builder.Build(ValidOptions());
            var face = host.Services.GetService<IOnlineFriendQuery>();

            var lease = OnlineFriendBridgeHolder.Publish(face, new OnlineScope(TenantId, AppId, ServerId));
            var before = await lease.ListFriendsAsync(PlayerScope(PlayerOne));
            Assert.True(before.IsSuccess);

            OnlineFriendBridgeHolder.Retract();

            Assert.Null(OnlineFriendBridgeHolder.Current);
            var after = await lease.ListFriendsAsync(PlayerScope(PlayerOne));
            Assert.False(after.IsSuccess);
            Assert.Equal(OnlineErrorCode.DependencyUnavailable, after.Code);
        }

        /// <summary>
        /// holder 生命周期：未发布时 Current 为 null；发布后可取用；再发布替换当前 lease；
        /// 撤销后旧 lease 一并失效（重启语义——旧引用不得继续使用失效服务）。
        /// </summary>
        [Fact]
        public async Task BridgeHolder_PublishReplaceAndRetract_InvalidateStaleLeases()
        {
            var builder = MinimalRoot();
            builder.AddOnlineSocialRuntime();
            var host = builder.Build(ValidOptions());
            var face = host.Services.GetService<IOnlineFriendQuery>();

            Assert.False(OnlineFriendBridgeHolder.IsPublished);

            var first = OnlineFriendBridgeHolder.Publish(face, new OnlineScope(TenantId, AppId, ServerId));
            Assert.True(OnlineFriendBridgeHolder.IsPublished);
            Assert.Same(first, OnlineFriendBridgeHolder.Current);
            Assert.Equal(TenantId, OnlineFriendBridgeHolder.Current.AuthorizedScope.TenantId);

            var second = OnlineFriendBridgeHolder.Publish(face, new OnlineScope(TenantId, AppId, ServerId));
            Assert.Same(second, OnlineFriendBridgeHolder.Current);

            OnlineFriendBridgeHolder.Retract();
            Assert.False(OnlineFriendBridgeHolder.IsPublished);

            var staleFirst = await first.ListFriendsAsync(PlayerScope(PlayerOne));
            var staleSecond = await second.ListFriendsAsync(PlayerScope(PlayerOne));
            Assert.Equal(OnlineErrorCode.DependencyUnavailable, staleFirst.Code);
            Assert.Equal(OnlineErrorCode.DependencyUnavailable, staleSecond.Code);
        }

        /// <summary>
        /// 真实组合根（AddOnlineCapabilities）可解析好友查询契约面与授权作用域（C243 最小装配入口）。
        /// </summary>
        [Fact]
        public void RealComposition_ExposesFriendQueryFaceAndAuthorizedScope()
        {
            var composition = OnlineCapabilityComposition.AddOnlineCapabilities(options =>
            {
                options.TenantId = TenantId;
                options.AppId = AppId;
                options.ServerId = ServerId;
                options.EnableRuntime = true;
            });

            Assert.NotNull(composition.Runtime);
            Assert.NotNull(composition.Runtime.Services.GetService<IOnlineFriendQuery>());
            var scope = composition.Runtime.Services.GetService<OnlineScope>();
            Assert.NotNull(scope);
            Assert.Equal(TenantId, scope.TenantId);
            Assert.Equal(AppId, scope.AppId);
            Assert.Equal(ServerId, scope.ServerId);
        }

        /// <summary>
        /// 组合根接线 fail-fast：桥开关开而业务运行时开关关（或作用域三元组缺失）时启动即抛；
        /// 开关关（缺省）时不抛——旧行为边界。
        /// </summary>
        [Fact]
        public void BridgeWiring_EnsureConfigured_FailsFastOnUnsupportedTopology()
        {
            var gateOff = new AppSetting { IsEnableOnline = false, Online = new OnlineCapabilityOptions { EnableFriendQueryBridge = false } };
            OnlineFriendBridgeWiring.EnsureFriendQueryBridgeConfigured(gateOff); // 缺省：不抛

            var gateOnWithoutRuntime = new AppSetting { IsEnableOnline = false, Online = new OnlineCapabilityOptions { EnableFriendQueryBridge = true } };
            Assert.Throws<InvalidOperationException>(() => OnlineFriendBridgeWiring.EnsureFriendQueryBridgeConfigured(gateOnWithoutRuntime));

            var valid = new AppSetting { IsEnableOnline = true, OnlineTenantId = TenantId, OnlineAppId = AppId, ServerId = (int)ServerId, Online = new OnlineCapabilityOptions { EnableFriendQueryBridge = true } };
            OnlineFriendBridgeWiring.EnsureFriendQueryBridgeConfigured(valid); // 合法形态：不抛

            var missingScope = new AppSetting { IsEnableOnline = true, Online = new OnlineCapabilityOptions { EnableFriendQueryBridge = true } };
            Assert.Throws<InvalidOperationException>(() => OnlineFriendBridgeWiring.EnsureFriendQueryBridgeConfigured(missingScope));
        }

        /// <summary>
        /// 组合根发布与验证种子：发布后 lease 指向真实组合根面；
        /// 种子经真实服务写路径建立「进程玩家全局 actor × 种子 id」好友（幂等收敛，非直改 store）。
        /// </summary>
        [Fact]
        public async Task BridgeWiring_PublishAndSeed_CreateRealFriendshipForGlobalActor()
        {
            var composition = OnlineCapabilityComposition.AddOnlineCapabilities(options =>
            {
                options.TenantId = TenantId;
                options.AppId = AppId;
                options.ServerId = ServerId;
                options.EnableRuntime = true;
            });
            var runtime = composition.Runtime;

            var lease = OnlineFriendBridgeWiring.PublishFriendQueryBridge(runtime);
            Assert.Same(lease, OnlineFriendBridgeHolder.Current);

            await OnlineFriendBridgeWiring.SeedFriendQueryBridgeVerificationAsync(runtime, new[] { PlayerOne, PlayerTwo });

            var service = runtime.Services.GetService<OnlineFriendService>();
            var friends = await service.ListFriendsAsync(new OnlineScope(TenantId, AppId, ServerId, PlayerOne));
            Assert.True(friends.IsSuccess);
            Assert.Contains(friends.Data, entry => entry.PlayerId == PlayerTwo);

            // 幂等：重复种子不产生第二条关系、不抛。
            await OnlineFriendBridgeWiring.SeedFriendQueryBridgeVerificationAsync(runtime, new[] { PlayerOne, PlayerTwo });
            var friendsAgain = await service.ListFriendsAsync(new OnlineScope(TenantId, AppId, ServerId, PlayerOne));
            Assert.Single(friendsAgain.Data);
        }
    }
}
