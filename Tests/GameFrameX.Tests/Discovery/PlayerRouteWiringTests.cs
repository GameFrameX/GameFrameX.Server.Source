// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//  ==========================================================================================


using GameFrameX.Hotfix.Logic.Server.Unified;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.Messages;
using GameFrameX.NetWork.RemoteMessaging.Discovery;
using GameFrameX.NetWork.RemoteMessaging.Routing;
using GameFrameX.ProtoBuf.Net;
using ProtoBuf;
using Xunit;
using IPlayerLocalSender = GameFrameX.NetWork.RemoteMessaging.Unified.IPlayerLocalSender;
using IPlayerRouteResolver = GameFrameX.NetWork.RemoteMessaging.Unified.IPlayerRouteResolver;
using PlayerRouteInfo = GameFrameX.NetWork.RemoteMessaging.Unified.PlayerRouteInfo;

namespace GameFrameX.Tests.Discovery;

/// <summary>
/// 玩家路由装配接线测试（C152）。
/// </summary>
/// <remarks>
/// Unit tests for the C152 player-route wiring completion:
/// resolver selection (discovery-provided Mongo resolver vs Hotfix default fallback),
/// <c>MongoDiscoveryRuntime.AttachLocalDispatcher</c> semantics (install, idempotence,
/// remote-chain preservation, explicit pre-Activate failure), and the closed-loop
/// envelope re-delivery through the routing seam case 1 down to
/// <c>IPlayerLocalSender</c>. Mongo-dependent activation paths stay in the
/// integration suites (env-var gated); here the activated state is simulated
/// through the internal test seams.
/// </remarks>
public class PlayerRouteWiringTests
{
    /// <summary>
    /// 每个用例前重置消息注册与发现层装配状态，保证用例间隔离。
    /// </summary>
    public PlayerRouteWiringTests()
    {
        // 注册内嵌测试消息（清空后重扫本程序集：仅 WiringTestMessage 带 MessageTypeHandler，无其他用例依赖注册表）。
        MessageProtoHelper.Init(typeof(PlayerRouteWiringTests).Assembly);
        MongoDiscoveryRuntime.ResetForTest();
    }

    /// <summary>
    /// 内嵌路由测试消息（envelope 复投闭环的内层载荷；内部预留段 -131 子号 1）。
    /// </summary>
    [ProtoContract]
    [MessageTypeHandler(WiringTestMessage.ReservedMessageId)]
    public sealed class WiringTestMessage : MessageObject
    {
        /// <summary>
        /// 测试消息 Id（仅本测试程序集注册）。
        /// </summary>
        public const int ReservedMessageId = unchecked((int)(((-131) << 16) + 1));

        /// <summary>玩家 Id / The player id</summary>
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        /// <summary>载荷字段 / The payload field</summary>
        [ProtoMember(2)]
        public string Reason { get; set; }

        /// <summary>
        /// 清除测试消息内容：重置 <see cref="PlayerId"/> 为 0、<see cref="Reason"/> 为 null。
        /// </summary>
        /// <remarks>
        /// Clears the test message content by resetting <see cref="PlayerId"/> to 0 and <see cref="Reason"/> to null.
        /// </remarks>
        public override void Clear()
        {
            PlayerId = 0;
            Reason = null;
        }
    }

    [Fact]
    public async Task SelectRouteResolver_WithoutDiscoveredResolver_FallsBackToDefaultResolver()
    {
        var resolver = PlayerRouteWiring.SelectRouteResolver(null);

        Assert.IsType<DefaultPlayerRouteResolver>(resolver);
        // 回落分支行为等价：未知玩家在空 SessionManager 下解析为离线（与 C152 前一致）。
        var route = await resolver.ResolveAsync(987654321L);
        Assert.False(route.IsOnline);
    }

    [Fact]
    public async Task SelectRouteResolver_WithDiscoveredResolver_WrapsItAndMapsOnlineRoute()
    {
        var discovered = new StubRoutingResolver(GameFrameX.NetWork.RemoteMessaging.Routing.PlayerRouteInfo.Online("Social", 7, 42));

        var resolver = PlayerRouteWiring.SelectRouteResolver(discovered);

        var route = await resolver.ResolveAsync(1001);
        Assert.True(route.IsOnline);
        Assert.Equal("Social", route.ServerType);
        Assert.Equal(7, route.ServerId);
        Assert.Equal(42, route.Version);
    }

    [Fact]
    public async Task SelectRouteResolver_WithDiscoveredResolver_MapsOfflineRoute()
    {
        var discovered = new StubRoutingResolver(GameFrameX.NetWork.RemoteMessaging.Routing.PlayerRouteInfo.Offline());

        var resolver = PlayerRouteWiring.SelectRouteResolver(discovered);

        var route = await resolver.ResolveAsync(1001);
        Assert.False(route.IsOnline);
    }

    [Fact]
    public void AttachLocalDispatcher_BeforeActivate_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => MongoDiscoveryRuntime.AttachLocalDispatcher(new RecordingLocalDispatcher()));
    }

    [Fact]
    public async Task AttachLocalDispatcher_AfterActivate_InstallsCase1DispatcherIntoRoleRouterHolder()
    {
        var remoteRouter = new RecordingRemoteRouter();
        MongoDiscoveryRuntime.SimulateActivatedForTest(new[] { "Game", "Social" }, remoteRouter);
        var dispatcher = new RecordingLocalDispatcher();

        MongoDiscoveryRuntime.AttachLocalDispatcher(dispatcher);

        var envelope = new MessageEnvelope("Game", new WiringTestMessage { PlayerId = 1001 });
        var delivery = await RoleRouterHolder.Current.RouteAsync(envelope);

        Assert.Equal(RoleRouteDelivery.LocalActor, delivery);
        Assert.Same(envelope, Assert.Single(dispatcher.Received));
    }

    [Fact]
    public async Task AttachLocalDispatcher_SecondCall_IsNoOpAndKeepsFirstDispatcher()
    {
        var remoteRouter = new RecordingRemoteRouter();
        MongoDiscoveryRuntime.SimulateActivatedForTest(new[] { "Game" }, remoteRouter);
        var first = new RecordingLocalDispatcher();
        var second = new RecordingLocalDispatcher();

        MongoDiscoveryRuntime.AttachLocalDispatcher(first);
        MongoDiscoveryRuntime.AttachLocalDispatcher(second);

        var envelope = new MessageEnvelope("Game", new WiringTestMessage { PlayerId = 1001 });
        await RoleRouterHolder.Current.RouteAsync(envelope);

        Assert.Single(first.Received);
        Assert.Empty(second.Received);
    }

    [Fact]
    public async Task AttachLocalDispatcher_PreservesRemoteChain()
    {
        var remoteRouter = new RecordingRemoteRouter();
        MongoDiscoveryRuntime.SimulateActivatedForTest(new[] { "Game" }, remoteRouter);
        MongoDiscoveryRuntime.AttachLocalDispatcher(new RecordingLocalDispatcher());

        var envelope = new MessageEnvelope("Gate", new WiringTestMessage { PlayerId = 1001 });
        var delivery = await RoleRouterHolder.Current.RouteAsync(envelope);

        // 远程链不变：非本进程目标仍走 Activate 时创建的同一转发器实例（case 2/3 语义保持）。
        Assert.Equal(RoleRouteDelivery.RemoteForwarded, delivery);
        Assert.Same(envelope, Assert.Single(remoteRouter.Forwarded));
    }

    [Fact]
    public async Task EnvelopeRouting_Case1_UnpacksInnerMessageAndDeliversToLocalSender()
    {
        var sender = new RecordingPlayerLocalSender();
        sender.OnlinePlayerIds.Add(1001);
        var router = new InProcessRoleRouter(new[] { "Game" }, new LocalEnvelopeDispatcher(sender));

        var envelopeMessage = new RoleRouteEnvelopeMessage
        {
            TargetRole = "Game",
            TargetActorId = 1001,
            InnerMessageId = WiringTestMessage.ReservedMessageId,
            InnerMessageBytes = ProtoBufSerializerHelper.Serialize(new WiringTestMessage { PlayerId = 1001, Reason = "mail-notify" }),
        };

        var delivery = await router.RouteAsync(new MessageEnvelope("Game", envelopeMessage, 1001));

        Assert.Equal(RoleRouteDelivery.LocalActor, delivery);
        var delivered = Assert.Single(sender.Delivered);
        Assert.Equal(1001, delivered.PlayerId);
        var innerMessage = Assert.IsType<WiringTestMessage>(delivered.Message);
        Assert.Equal(1001, innerMessage.PlayerId);
        Assert.Equal("mail-notify", innerMessage.Reason);
    }

    [Fact]
    public async Task EnvelopeRouting_Case1_OfflineTarget_DropsWithoutSending()
    {
        var sender = new RecordingPlayerLocalSender();
        var router = new InProcessRoleRouter(new[] { "Game" }, new LocalEnvelopeDispatcher(sender));

        var envelopeMessage = new RoleRouteEnvelopeMessage
        {
            TargetRole = "Game",
            TargetActorId = 1001,
            InnerMessageId = WiringTestMessage.ReservedMessageId,
            InnerMessageBytes = ProtoBufSerializerHelper.Serialize(new WiringTestMessage { PlayerId = 1001 }),
        };

        // 离线走默认 Drop 策略：不抛（路由缝不被拖垮）、不投递。
        var delivery = await router.RouteAsync(new MessageEnvelope("Game", envelopeMessage, 1001));

        Assert.Equal(RoleRouteDelivery.LocalActor, delivery);
        Assert.Empty(sender.Delivered);
    }

    [Fact]
    public async Task EnvelopeRouting_Case1_UnregisteredInnerMessageId_DropsWithoutSending()
    {
        var sender = new RecordingPlayerLocalSender();
        sender.OnlinePlayerIds.Add(1001);
        var router = new InProcessRoleRouter(new[] { "Game" }, new LocalEnvelopeDispatcher(sender));

        var envelopeMessage = new RoleRouteEnvelopeMessage
        {
            TargetRole = "Game",
            TargetActorId = 1001,
            InnerMessageId = 12345,
            InnerMessageBytes = new byte[] { 1, 2, 3 },
        };

        var delivery = await router.RouteAsync(new MessageEnvelope("Game", envelopeMessage, 1001));

        Assert.Equal(RoleRouteDelivery.LocalActor, delivery);
        Assert.Empty(sender.Delivered);
    }

    /// <summary>
    /// 记录型 Routing 侧解析器（模拟发现层已装配的 Mongo 解析器）。
    /// </summary>
    private sealed class StubRoutingResolver : GameFrameX.NetWork.RemoteMessaging.Routing.IPlayerRouteResolver
    {
        private readonly GameFrameX.NetWork.RemoteMessaging.Routing.PlayerRouteInfo _routeInfo;

        public StubRoutingResolver(GameFrameX.NetWork.RemoteMessaging.Routing.PlayerRouteInfo routeInfo)
        {
            _routeInfo = routeInfo;
        }

        /// <summary>
        /// 忽略玩家 Id，返回构造时固定的路由信息，模拟发现层已装配的 Mongo 解析器。
        /// </summary>
        /// <remarks>
        /// Ignores the player id and returns the fixed route info supplied at construction,
        /// simulating the discovery-wired Mongo resolver.
        /// </remarks>
        /// <param name="playerId">玩家 ID / The player id</param>
        /// <returns>构造时固定的路由信息 / The fixed route info supplied at construction</returns>
        public Task<GameFrameX.NetWork.RemoteMessaging.Routing.PlayerRouteInfo> ResolveAsync(long playerId)
        {
            return Task.FromResult(_routeInfo);
        }
    }

    /// <summary>
    /// 记录型本地投递器（断言挂接语义用）。
    /// </summary>
    private sealed class RecordingLocalDispatcher : ILocalRoleMessageDispatcher
    {
        public List<MessageEnvelope> Received { get; } = new List<MessageEnvelope>();

        /// <summary>
        /// 将信封记录到 <see cref="Received"/> 列表后立即完成，供断言挂接语义。
        /// </summary>
        /// <remarks>
        /// Records the envelope into the <see cref="Received"/> list and completes
        /// immediately for attachment-semantics assertions.
        /// </remarks>
        /// <param name="envelope">路由信封 / The routing envelope</param>
        /// <param name="cancellationToken">取消操作的令牌 / The cancellation token</param>
        public Task DispatchAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Received.Add(envelope);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 记录型远程转发器（断言远程链保持用）。
    /// </summary>
    private sealed class RecordingRemoteRouter : IRemoteRoleRouter
    {
        public List<MessageEnvelope> Forwarded { get; } = new List<MessageEnvelope>();

        /// <summary>
        /// 将信封记录到 <see cref="Forwarded"/> 列表并恒返回 <see cref="RoleRouteDelivery.RemoteForwarded"/>，不执行真实转发。
        /// </summary>
        /// <remarks>
        /// Records the envelope into the <see cref="Forwarded"/> list and always returns
        /// <see cref="RoleRouteDelivery.RemoteForwarded"/> without performing any real forwarding.
        /// </remarks>
        /// <param name="envelope">路由信封 / The routing envelope</param>
        /// <param name="cancellationToken">取消操作的令牌 / The cancellation token</param>
        /// <returns>恒为 <see cref="RoleRouteDelivery.RemoteForwarded"/> / Always <see cref="RoleRouteDelivery.RemoteForwarded"/></returns>
        public Task<RoleRouteDelivery> ForwardAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Forwarded.Add(envelope);
            return Task.FromResult(RoleRouteDelivery.RemoteForwarded);
        }
    }

    /// <summary>
    /// 记录型本服玩家发送器（envelope 复投闭环的接收端）。
    /// </summary>
    private sealed class RecordingPlayerLocalSender : IPlayerLocalSender
    {
        public HashSet<long> OnlinePlayerIds { get; } = new HashSet<long>();

        public List<DeliveryRecord> Delivered { get; } = new List<DeliveryRecord>();

        /// <summary>
        /// 检查玩家 Id 是否包含于预置的 <see cref="OnlinePlayerIds"/> 在线集合。
        /// </summary>
        /// <remarks>
        /// Checks whether the player id is contained in the preset <see cref="OnlinePlayerIds"/> online set.
        /// </remarks>
        /// <param name="playerId">玩家 ID / The player id</param>
        /// <returns>是否在预置在线集合中 / Whether present in the preset online set</returns>
        public bool IsPlayerOnline(long playerId)
        {
            return OnlinePlayerIds.Contains(playerId);
        }

        /// <summary>
        /// 将一次投递记录到 <see cref="Delivered"/> 列表后恒返回成功，不发送真实网络消息。
        /// </summary>
        /// <remarks>
        /// Records one delivery into the <see cref="Delivered"/> list and always reports
        /// success without sending any real network message.
        /// </remarks>
        /// <param name="playerId">玩家 ID / The player id</param>
        /// <param name="message">内层消息对象 / The inner message object</param>
        /// <returns>恒为 true / Always true</returns>
        public Task<bool> SendToLocalPlayerAsync(long playerId, MessageObject message)
        {
            Delivered.Add(new DeliveryRecord(playerId, message));
            return Task.FromResult(true);
        }

        /// <summary>
        /// 一次本地投递记录（目标玩家 + 内层消息）。
        /// </summary>
        public readonly struct DeliveryRecord
        {
            /// <summary>目标玩家 Id / The target player id</summary>
            public readonly long PlayerId;

            /// <summary>内层消息 / The inner message</summary>
            public readonly MessageObject Message;

            public DeliveryRecord(long playerId, MessageObject message)
            {
                PlayerId = playerId;
                Message = message;
            }
        }
    }
}
