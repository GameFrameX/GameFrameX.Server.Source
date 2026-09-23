// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.NetWork.RemoteMessaging.Routing;

namespace GameFrameX.Tests.Topology.Equivalence;

/// <summary>
/// InProcessRoleRouter D3 三步判定单元测试（C143c）。
/// </summary>
/// <remarks>
/// Unit tests for the InProcessRoleRouter three-step decision (C143c):
/// case 1 goes through the local dispatcher (mock), case 2/3 delegates to the remote
/// seam — with the production placeholder that throws NotImplementedException — and
/// every undecidable route fails loudly with RouteNotFoundException.
/// </remarks>
public class InProcessRoleRouterTests
{
    /// <summary>
    /// 记录型本地投递器（mock Actor 投递缝）。
    /// </summary>
    private sealed class RecordingLocalDispatcher : ILocalRoleMessageDispatcher
    {
        /// <summary>已收到的信封 / The received envelopes</summary>
        public List<MessageEnvelope> ReceivedEnvelopes { get; } = new List<MessageEnvelope>();

        /// <summary>
        /// 记录信封到已接收列表并立即返回已完成的任务。
        /// </summary>
        /// <remarks>
        /// Records the envelope into the received list and returns an already completed task.
        /// </remarks>
        /// <param name="envelope">要记录的路由信封 / The routing envelope to record</param>
        /// <param name="cancellationToken">本实现忽略的取消令牌 / The cancellation token ignored by this implementation</param>
        /// <returns>已完成的任务 / A completed task</returns>
        public Task DispatchAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            ReceivedEnvelopes.Add(envelope);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 固定应答型远程转发器（mock D3 case 2/3 缝）。
    /// </summary>
    private sealed class StubRemoteRoleRouter : IRemoteRoleRouter
    {
        /// <summary>已收到的信封 / The received envelopes</summary>
        public List<MessageEnvelope> ReceivedEnvelopes { get; } = new List<MessageEnvelope>();

        /// <summary>
        /// 记录信封到已接收列表并固定返回 RemoteForwarded。
        /// </summary>
        /// <remarks>
        /// Records the envelope into the received list and always returns <see cref="RoleRouteDelivery.RemoteForwarded"/>.
        /// </remarks>
        /// <param name="envelope">要记录的路由信封 / The routing envelope to record</param>
        /// <param name="cancellationToken">本实现忽略的取消令牌 / The cancellation token ignored by this implementation</param>
        /// <returns>固定为 <see cref="RoleRouteDelivery.RemoteForwarded"/> 的投递分支 / Always <see cref="RoleRouteDelivery.RemoteForwarded"/></returns>
        public Task<RoleRouteDelivery> ForwardAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            ReceivedEnvelopes.Add(envelope);
            return Task.FromResult(RoleRouteDelivery.RemoteForwarded);
        }
    }

    /// <summary>
    /// case 1：目标 Role 属于本进程角色集 → 经本地投递器送达并返回 LocalActor。
    /// </summary>
    [Fact]
    public async Task RouteAsync_TargetRoleHostedLocally_DispatchesThroughLocalDispatcher()
    {
        var localDispatcher = new RecordingLocalDispatcher();
        var router = new InProcessRoleRouter(new List<string> { "Game", "Social" }, localDispatcher);
        var envelope = new MessageEnvelope("Game", new EquivalenceTestMessages.SessionEstablishRequest { PlayerId = 1001 });

        var delivery = await router.RouteAsync(envelope);

        Assert.Equal(RoleRouteDelivery.LocalActor, delivery);
        Assert.Single(localDispatcher.ReceivedEnvelopes);
        Assert.Same(envelope, localDispatcher.ReceivedEnvelopes[0]);
    }

    /// <summary>
    /// case 1 命中但未配置本地投递器 → 显式抛 RouteNotFoundException。
    /// </summary>
    [Fact]
    public async Task RouteAsync_LocalTargetWithoutDispatcher_ThrowsRouteNotFound()
    {
        var router = new InProcessRoleRouter(new List<string> { "Game" }, null);
        var envelope = new MessageEnvelope("Game", new EquivalenceTestMessages.SessionEstablishRequest());

        var exception = await Assert.ThrowsAsync<RouteNotFoundException>(() => router.RouteAsync(envelope));

        Assert.Equal("Game", exception.TargetRole);
    }

    /// <summary>
    /// case 2/3：目标 Role 不属于本进程 → 委托远程缝；生产占位 RemoteRoleRouter 恒抛 NotImplementedException。
    /// </summary>
    [Fact]
    public async Task RouteAsync_RemoteTargetWithPlaceholderForwarder_ThrowsNotImplemented()
    {
        var router = new InProcessRoleRouter(new List<string> { "Gate" }, new RecordingLocalDispatcher(), new RemoteRoleRouter());
        var envelope = new MessageEnvelope("Game", new EquivalenceTestMessages.SessionEstablishRequest());

        await Assert.ThrowsAsync<NotImplementedException>(() => router.RouteAsync(envelope));
    }

    /// <summary>
    /// case 2/3：目标 Role 不属于本进程 → 委托远程缝并原样返回转发结果。
    /// </summary>
    [Fact]
    public async Task RouteAsync_RemoteTargetWithForwarder_ForwardsAndReturnsRemoteForwarded()
    {
        var remoteRouter = new StubRemoteRoleRouter();
        var router = new InProcessRoleRouter(new List<string> { "Gate" }, new RecordingLocalDispatcher(), remoteRouter);
        var envelope = new MessageEnvelope("Game", new EquivalenceTestMessages.SessionEstablishRequest(), targetInstanceId: "game-instance-01");

        var delivery = await router.RouteAsync(envelope);

        Assert.Equal(RoleRouteDelivery.RemoteForwarded, delivery);
        Assert.Single(remoteRouter.ReceivedEnvelopes);
        Assert.Same(envelope, remoteRouter.ReceivedEnvelopes[0]);
    }

    /// <summary>
    /// case 2/3 未配置远程缝 → 显式抛 RouteNotFoundException（不静默）。
    /// </summary>
    [Fact]
    public async Task RouteAsync_RemoteTargetWithoutForwarder_ThrowsRouteNotFound()
    {
        var router = new InProcessRoleRouter(new List<string> { "Gate" }, new RecordingLocalDispatcher());
        var envelope = new MessageEnvelope("Game", new EquivalenceTestMessages.SessionEstablishRequest());

        var exception = await Assert.ThrowsAsync<RouteNotFoundException>(() => router.RouteAsync(envelope));

        Assert.Equal("Game", exception.TargetRole);
    }

    /// <summary>
    /// 空白目标 Role → 路由决策失败抛 RouteNotFoundException。
    /// </summary>
    [Fact]
    public async Task RouteAsync_EmptyTargetRole_ThrowsRouteNotFound()
    {
        var router = new InProcessRoleRouter(new List<string> { "Game" }, new RecordingLocalDispatcher());
        var envelope = new MessageEnvelope("   ", new EquivalenceTestMessages.SessionEstablishRequest());

        await Assert.ThrowsAsync<RouteNotFoundException>(() => router.RouteAsync(envelope));
    }

    /// <summary>
    /// null 信封 → ArgumentNullException。
    /// </summary>
    [Fact]
    public async Task RouteAsync_NullEnvelope_ThrowsArgumentNullException()
    {
        var router = new InProcessRoleRouter(new List<string> { "Game" }, new RecordingLocalDispatcher());

        await Assert.ThrowsAsync<ArgumentNullException>(() => router.RouteAsync(null));
    }

    /// <summary>
    /// 构造后源集合变更不影响路由判定（角色集快照防御性拷贝）。
    /// </summary>
    [Fact]
    public async Task Constructor_DefensivelyCopiesHostedRoleNames_LaterMutationDoesNotAffectRouting()
    {
        var hostedRoleNames = new List<string> { "Game" };
        var localDispatcher = new RecordingLocalDispatcher();
        var router = new InProcessRoleRouter(hostedRoleNames, localDispatcher);

        hostedRoleNames.Clear();

        var delivery = await router.RouteAsync(new MessageEnvelope("Game", new EquivalenceTestMessages.SessionEstablishRequest()));

        Assert.Equal(RoleRouteDelivery.LocalActor, delivery);
    }
}
