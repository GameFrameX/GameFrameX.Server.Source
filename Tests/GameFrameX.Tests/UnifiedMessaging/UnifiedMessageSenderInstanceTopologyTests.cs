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
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:   https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Discovery;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.Messages;
using GameFrameX.Network.RemoteMessaging.Contracts;
using GameFrameX.Network.RemoteMessaging.Unified;
using System.Collections.Concurrent;
using GameFrameX.Tests.RemoteMessaging;

namespace GameFrameX.Tests.UnifiedMessaging;

/// <summary>
/// C304 统一发送端拓扑测试：实例列表读路由表 Active 视图、仅 InstanceId 集合变化才重建哈希环、
/// 拨号 context 携带 TargetInstanceId 与调用方 CancellationToken；玩家路由按 InstanceId 实例直达。
/// </summary>
/// <remarks>
/// C304 unified-sender topology tests: the instance list reads the route table's Active view, the
/// hash ring is rebuilt only when the instance-id set changes, the dial context carries
/// TargetInstanceId and the caller's CancellationToken, and player routes deliver to the exact instance.
/// </remarks>
public sealed class UnifiedMessageSenderInstanceTopologyTests
{
    private sealed class CaptureRemoteMessageClient : IRemoteMessageClient
    {
        public readonly ConcurrentQueue<RemoteCallContext> Contexts = new();
        public Func<RemoteCallResult<RespSendToPlayerInner>> ResultFactory;

        public Task<TResponse> CallAsync<TResponse>(string serviceName, MessageObject requestMessage) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<TResponse> CallAsync<TResponse>(string serviceName, MessageObject requestMessage, CancellationToken cancellationToken) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<TResponse> CallAsync<TResponse>(string serviceName, MessageObject requestMessage, int timeoutMs, CancellationToken cancellationToken = default) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(string serviceName, MessageObject requestMessage) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(string serviceName, MessageObject requestMessage, int timeoutMs) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(string serviceName, MessageObject requestMessage, int timeoutMs, CancellationToken cancellationToken) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(RemoteCallContext context, MessageObject requestMessage) where TResponse : class, IResponseMessage
        {
            Contexts.Enqueue(context);
            return Task.FromResult(BuildResult<TResponse>());
        }

        public Task<RemoteCallResult<TResponse>> CallWithRetryAsync<TResponse>(string serviceName, MessageObject requestMessage, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, int maxRetryCount = RemoteCallContext.DefaultMaxRetryCount, CancellationToken cancellationToken = default) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithoutRetryAsync<TResponse>(string serviceName, MessageObject requestMessage, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, CancellationToken cancellationToken = default) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithMetadataAsync<TResponse>(string serviceName, MessageObject requestMessage, Dictionary<string, string> metadata, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, CancellationToken cancellationToken = default) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task SendOneWayAsync(string serviceName, MessageObject requestMessage, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task SendOneWayAsync(RemoteCallContext context, MessageObject requestMessage)
        {
            Contexts.Enqueue(context);
            return Task.CompletedTask;
        }

        public Task<bool> IsServiceAvailableAsync(string serviceName)
        {
            return Task.FromResult(true);
        }

        private RemoteCallResult<TResponse> BuildResult<TResponse>() where TResponse : class, IResponseMessage
        {
            if (typeof(TResponse) != typeof(RespSendToPlayerInner))
            {
                throw new NotSupportedException($"Unsupported response type: {typeof(TResponse).Name}");
            }

            if (ResultFactory != null)
            {
                return (RemoteCallResult<TResponse>)(object)ResultFactory();
            }

            var response = new RespSendToPlayerInner
            {
                Success = true,
                PlayerOffline = false,
            };

            return RemoteCallResult<TResponse>.Ok((TResponse)(object)response, 5, "trace-test", 0);
        }
    }

    private sealed class CountingInstanceSelector : IServerInstanceSelector
    {
        public int RefreshCount;
        private IReadOnlyList<string> _instanceIds = Array.Empty<string>();

        public InstanceSelection Select(string serviceName, string routeKey = null)
        {
            if (_instanceIds.Count == 0)
            {
                return InstanceSelection.None(serviceName);
            }

            // 一致性选路的确定性替身：按 routeKey（或固定）取模，同一 routeKey 恒落同一实例
            var index = routeKey == null ? 0 : Math.Abs(routeKey.GetHashCode(StringComparison.Ordinal)) % _instanceIds.Count;
            return InstanceSelection.Selected(serviceName, _instanceIds[index]);
        }

        public void RefreshInstances(string serviceName, IReadOnlyList<string> instanceIds)
        {
            RefreshCount++;
            _instanceIds = instanceIds;
        }
    }

    private sealed class FixedRouteResolver : IPlayerRouteResolver
    {
        private readonly PlayerRouteInfo _routeInfo;

        public FixedRouteResolver(PlayerRouteInfo routeInfo)
        {
            _routeInfo = routeInfo;
        }

        public Task<PlayerRouteInfo> ResolveAsync(long playerId)
        {
            return Task.FromResult(_routeInfo);
        }
    }

    private sealed class SwappableRouteTableProvider : IRoleRouteTableProvider
    {
        public SwappableRouteTableProvider(RoleRouteTable table)
        {
            Current = table ?? RoleRouteTable.Empty;
        }

        public RoleRouteTable Current
        {
            get;
            set;
        }
    }

    private static (UnifiedMessageSender Sender, CaptureRemoteMessageClient Client, CountingInstanceSelector Selector, SwappableRouteTableProvider RouteTable) CreateSender(PlayerRouteInfo routeInfo = null, IPlayerLocalSender localSender = null)
    {
        var client = new CaptureRemoteMessageClient();
        var selector = new CountingInstanceSelector();
        var routeTable = new SwappableRouteTableProvider(RoleRouteTable.Empty);
        var sender = new UnifiedMessageSender(
            client,
            new FixedRouteResolver(routeInfo ?? PlayerRouteInfo.Offline()),
            localSender ?? new FakePlayerLocalSender(false, false),
            selector,
            routeTable,
            "Social",
            "2001");
        return (sender, client, selector, routeTable);
    }

    /// <summary>
    /// 方法 2：实例集合不变时哈希环不重建——快照对象变化但 InstanceId 集合相同，RefreshInstances 只在首次发生。
    /// </summary>
    [Fact]
    public async Task SameInstanceSet_SnapshotReplaced_HashRingNotRebuilt()
    {
        var (sender, _, selector, routeTable) = CreateSender();
        routeTable.Current = RouteTableTestHelper.Table(
            RouteTableTestHelper.Descriptor("Social", "2001", "tcp://10.0.0.1:29400"),
            RouteTableTestHelper.Descriptor("Social", "2002", "tcp://10.0.0.2:29401"));

        await sender.SendToServerAsync<RespSendToPlayerInner>("Social", new ReqSendToPlayerInner());
        Assert.Equal(1, selector.RefreshCount);

        // 换新快照对象（新描述符实例、同 InstanceId 集合）——哈希环不重建
        routeTable.Current = RoleRouteTable.FromInstances(new[]
        {
            RouteTableTestHelper.Descriptor("Social", "2001", "tcp://10.0.0.1:29400"),
            RouteTableTestHelper.Descriptor("Social", "2002", "tcp://10.0.0.2:29401"),
        });

        await sender.SendToServerAsync<RespSendToPlayerInner>("Social", new ReqSendToPlayerInner());
        Assert.Equal(1, selector.RefreshCount);
    }

    /// <summary>
    /// 方法 2：实例集合变化后哈希环重建（RefreshInstances 再次被调）。
    /// </summary>
    [Fact]
    public async Task InstanceSetChanged_HashRingRebuilt()
    {
        var (sender, _, selector, routeTable) = CreateSender();
        routeTable.Current = RouteTableTestHelper.Table(
            RouteTableTestHelper.Descriptor("Social", "2001", "tcp://10.0.0.1:29400"),
            RouteTableTestHelper.Descriptor("Social", "2002", "tcp://10.0.0.2:29401"));

        await sender.SendToServerAsync<RespSendToPlayerInner>("Social", new ReqSendToPlayerInner());
        Assert.Equal(1, selector.RefreshCount);

        routeTable.Current = RouteTableTestHelper.Table(
            RouteTableTestHelper.Descriptor("Social", "2001", "tcp://10.0.0.1:29400"),
            RouteTableTestHelper.Descriptor("Social", "2002", "tcp://10.0.0.2:29401"),
            RouteTableTestHelper.Descriptor("Social", "2003", "tcp://10.0.0.3:29402"));

        await sender.SendToServerAsync<RespSendToPlayerInner>("Social", new ReqSendToPlayerInner());
        Assert.Equal(2, selector.RefreshCount);
    }

    /// <summary>
    /// 方法 2：同一 routeKey 稳定落到同一实例；拨号 context 带 TargetInstanceId 与调用方 CancellationToken。
    /// </summary>
    [Fact]
    public async Task DialContext_CarriesTargetInstanceIdAndCallerCancellationToken()
    {
        var (sender, client, _, routeTable) = CreateSender();
        routeTable.Current = RouteTableTestHelper.Table(
            RouteTableTestHelper.Descriptor("Social", "2001", "tcp://10.0.0.1:29400"),
            RouteTableTestHelper.Descriptor("Social", "2002", "tcp://10.0.0.2:29401"));

        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            var first = await sender.SendToServerAsync<RespSendToPlayerInner>("Social", new ReqSendToPlayerInner(), new ServerSendOptions { RouteKey = "player-42" }, cancellationTokenSource.Token);
            var second = await sender.SendToServerAsync<RespSendToPlayerInner>("Social", new ReqSendToPlayerInner(), new ServerSendOptions { RouteKey = "player-42" }, cancellationTokenSource.Token);

            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            var contexts = client.Contexts.ToArray();
            Assert.Equal(2, contexts.Length);
            Assert.NotNull(contexts[0].TargetInstanceId);
            Assert.Equal(contexts[0].TargetInstanceId, contexts[1].TargetInstanceId); // 同 routeKey 稳定同实例
            Assert.Equal(cancellationTokenSource.Token, contexts[0].CancellationToken); // 取消令牌显式映射
        }
    }

    /// <summary>
    /// 方法 12：玩家路由指向实例 2002 时，跨服投递的拨号 context TargetInstanceId == "2002"（不再恒落首实例 / ServerType 级）。
    /// </summary>
    [Fact]
    public async Task PlayerRouteTargetsInstance_DialsThatExactInstance()
    {
        var (sender, client, _, routeTable) = CreateSender(PlayerRouteInfo.Online("Social", 5532, "2002", 3));
        routeTable.Current = RouteTableTestHelper.Table(
            RouteTableTestHelper.Descriptor("Social", "2001", "tcp://10.0.0.1:29400"),
            RouteTableTestHelper.Descriptor("Social", "2002", "tcp://10.0.0.2:29401"));

        var result = await sender.SendToPlayerAsync(921001L, new ReqSendToPlayerInner());

        Assert.Equal(PlayerDeliverStatus.RemoteDelivered, result.Status);
        var context = Assert.Single(client.Contexts);
        Assert.Equal("Social", context.ServiceName);
        Assert.Equal("2002", context.TargetInstanceId);
    }

    /// <summary>
    /// 方法 12：目标实例不在路由表内（实例消失 / 旧版占位行）→ 远端返回 EndpointNotFound → offline 分支（不再恒落首实例投递）。
    /// </summary>
    [Fact]
    public async Task PlayerRouteTargetMissing_FallsIntoOfflineBranch()
    {
        var (sender, client, _, routeTable) = CreateSender(PlayerRouteInfo.Online("Social", 5532, "9999", 3));
        routeTable.Current = RouteTableTestHelper.Table(
            RouteTableTestHelper.Descriptor("Social", "2001", "tcp://10.0.0.1:29400"));
        client.ResultFactory = () => RemoteCallResult<RespSendToPlayerInner>.Fail(
            RemoteStatusCode.EndpointNotFound, "Failed to resolve service endpoint", 5, "trace-test");

        var result = await sender.SendToPlayerAsync(921001L, new ReqSendToPlayerInner(), new PlayerSendOptions { OfflineStrategy = PlayerOfflineStrategy.Discard });

        Assert.Equal(PlayerDeliverStatus.Offline, result.Status);
        var context = Assert.Single(client.Contexts);
        Assert.Equal("9999", context.TargetInstanceId); // 直达目标实例而非回落 ServerType 级
    }

    /// <summary>
    /// 方法 12：IsCurrentServer 按 InstanceId 判定——路由指向本进程实例（2001）时本服判定成立、指向 2002 时判远端。
    /// </summary>
    [Fact]
    public async Task IsCurrentServer_ComparesInstanceId()
    {
        var localSender = new FakePlayerLocalSender(false, false);
        var (selfSender, _, _, routeTable) = CreateSender(PlayerRouteInfo.Online("Social", 5531, "2001", 3), localSender);
        routeTable.Current = RouteTableTestHelper.Table(
            RouteTableTestHelper.Descriptor("Social", "2001", "tcp://10.0.0.1:29400"),
            RouteTableTestHelper.Descriptor("Social", "2002", "tcp://10.0.0.2:29401"));

        // 本地未命中会话 + 路由指向本进程实例 2001 → IsCurrentServer 成立 → 按本服离线处理（不跨服转发）
        var localResult = await selfSender.SendToPlayerAsync(921001L, new ReqSendToPlayerInner());
        Assert.Equal(PlayerDeliverStatus.Offline, localResult.Status);

        // 路由指向 2002 → 远端投递
        var (remoteSender, remoteClient, _, _) = CreateSender(PlayerRouteInfo.Online("Social", 5532, "2002", 3), localSender);
        var remoteResult = await remoteSender.SendToPlayerAsync(921001L, new ReqSendToPlayerInner());
        Assert.Equal(PlayerDeliverStatus.RemoteDelivered, remoteResult.Status);
        Assert.Equal("2002", Assert.Single(remoteClient.Contexts).TargetInstanceId);
    }
}
