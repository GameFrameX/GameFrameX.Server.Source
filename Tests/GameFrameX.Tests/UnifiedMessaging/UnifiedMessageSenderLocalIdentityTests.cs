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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Network.RemoteMessaging.Unified;
using GameFrameX.Network.RemoteMessaging.Contracts;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.RemoteMessaging;
using GameFrameX.Network.Messages;
using System.Collections;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.UnifiedMessaging;

public class UnifiedMessageSenderLocalIdentityTests
{
    [Fact]
    public async Task SendToPlayerAsync_RouteOnlineDiffersOnlyByCase_TreatedAsLocalServerWithoutRemoteCall()
    {
        // 注入 ("Social", 77)、路由 ("social", 77)：ServerType 仅大小写不同 → 本服；本服投递失败 → 离线，不跨服转发。
        var remoteClient = new CountingRemoteMessageClient();
        var sender = new UnifiedMessageSender(
            remoteClient,
            new FakePlayerRouteResolver(PlayerRouteInfo.Online("social", 77)),
            new FakePlayerLocalSender(isOnline: true, sendResult: false),
            new ConsistentHashServerInstanceSelector(),
            "Social",
            77);

        var result = await sender.SendToPlayerAsync(2001, new ReqSendToPlayerInner());

        Assert.False(result.IsSuccess);
        Assert.Equal(PlayerDeliverStatus.Offline, result.Status);
        Assert.Equal(0, remoteClient.CallWithResultCount);
    }

    [Fact]
    public async Task SendToPlayerAsync_ExactIdentityMatch_TreatedAsLocalServerWithoutRemoteCall()
    {
        // 注入 ("Social", 77) 且路由 ("Social", 77)：构造期读 CurrentSetting 的旧实现（测试内为 null，
        // 回落 Game/1000）会误判为他服并跨服转发；构造注入后判定为本服。
        var remoteClient = new CountingRemoteMessageClient();
        var sender = new UnifiedMessageSender(
            remoteClient,
            new FakePlayerRouteResolver(PlayerRouteInfo.Online("Social", 77)),
            new FakePlayerLocalSender(isOnline: true, sendResult: false),
            new ConsistentHashServerInstanceSelector(),
            "Social",
            77);

        var result = await sender.SendToPlayerAsync(2002, new ReqSendToPlayerInner());

        Assert.False(result.IsSuccess);
        Assert.Equal(PlayerDeliverStatus.Offline, result.Status);
        Assert.Equal(0, remoteClient.CallWithResultCount);
    }

    [Fact]
    public async Task SendToPlayerAsync_SameServerTypeDifferentServerId_ForwardsToRemoteServer()
    {
        // 注入 ("Social", 77)、路由 ("Social", 78)：ServerId 不同 → 非本服 → 跨服转发。
        var remoteClient = new CountingRemoteMessageClient();
        var sender = new UnifiedMessageSender(
            remoteClient,
            new FakePlayerRouteResolver(PlayerRouteInfo.Online("Social", 78)),
            new FakePlayerLocalSender(isOnline: false, sendResult: false),
            new ConsistentHashServerInstanceSelector(),
            "Social",
            77);

        var result = await sender.SendToPlayerAsync(2003, new ReqSendToPlayerInner());

        Assert.True(result.IsSuccess);
        Assert.Equal(PlayerDeliverStatus.RemoteDelivered, result.Status);
        Assert.Equal(1, remoteClient.CallWithResultCount);
    }

    [Fact]
    public async Task SendToPlayerAsync_SameServerIdDifferentServerType_ForwardsToRemoteServer()
    {
        // 注入 ("Social", 77)、路由 ("GateWay", 77)：ServerType 不同 → 非本服 → 跨服转发。
        var remoteClient = new CountingRemoteMessageClient();
        var sender = new UnifiedMessageSender(
            remoteClient,
            new FakePlayerRouteResolver(PlayerRouteInfo.Online("GateWay", 77)),
            new FakePlayerLocalSender(isOnline: false, sendResult: false),
            new ConsistentHashServerInstanceSelector(),
            "Social",
            77);

        var result = await sender.SendToPlayerAsync(2004, new ReqSendToPlayerInner());

        Assert.True(result.IsSuccess);
        Assert.Equal(PlayerDeliverStatus.RemoteDelivered, result.Status);
        Assert.Equal(1, remoteClient.CallWithResultCount);
    }

    [Fact]
    public async Task SendToPlayerAsync_LocalDelivered_MetricsUseInjectedServerType()
    {
        // 指标服务名兜底取注入身份：注入 "Social" → 本地投递成功指标键 player:Social。
        var metrics = new MessageSendMetrics();
        var sender = new UnifiedMessageSender(
            new CountingRemoteMessageClient(),
            new FakePlayerRouteResolver(PlayerRouteInfo.Offline()),
            new FakePlayerLocalSender(isOnline: true, sendResult: true),
            new ConsistentHashServerInstanceSelector(),
            "Social",
            77,
            metrics);

        var result = await sender.SendToPlayerAsync(2005, new ReqSendToPlayerInner());

        Assert.True(result.IsSuccess);
        Assert.Equal(PlayerDeliverStatus.LocalDelivered, result.Status);
        var snapshot = Assert.Single(metrics.GetSnapshots());
        Assert.Equal("player:Social", snapshot.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrWhiteSpaceLocalServerType_ThrowsArgumentException(string localServerType)
    {
        Assert.ThrowsAny<ArgumentException>(() => new UnifiedMessageSender(
            new CountingRemoteMessageClient(),
            new FakePlayerRouteResolver(PlayerRouteInfo.Offline()),
            new FakePlayerLocalSender(isOnline: true, sendResult: true),
            new ConsistentHashServerInstanceSelector(),
            localServerType,
            77));
    }

    /// <summary>
    /// 计数间谍远程客户端：仅 <c>CallWithResultAsync(RemoteCallContext, …)</c> 计数并返回
    /// <c>RespSendToPlayerInner</c> 成功结果（本服判定/跨服转发测试唯一走到的成员），其余成员直接抛
    /// <see cref="NotImplementedException"/> 兜底。
    /// </summary>
    internal sealed class CountingRemoteMessageClient : IRemoteMessageClient
    {
        public int CallWithResultCount { get; private set; }

        public Task<TResponse> CallAsync<TResponse>(string serviceName, MessageObject requestMessage) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<TResponse> CallAsync<TResponse>(string serviceName, MessageObject requestMessage, CancellationToken cancellationToken) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<TResponse> CallAsync<TResponse>(string serviceName, MessageObject requestMessage, int timeoutMs, CancellationToken cancellationToken = default) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(string serviceName, MessageObject requestMessage) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(string serviceName, MessageObject requestMessage, int timeoutMs) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(string serviceName, MessageObject requestMessage, int timeoutMs, CancellationToken cancellationToken) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(RemoteCallContext context, MessageObject requestMessage) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            CallWithResultCount++;
            if (typeof(TResponse) != typeof(RespSendToPlayerInner))
            {
                throw new NotSupportedException($"Unsupported response type: {typeof(TResponse).Name}");
            }

            var response = new RespSendToPlayerInner
            {
                Success = true,
                PlayerOffline = false,
            };

            return Task.FromResult(RemoteCallResult<TResponse>.Ok((TResponse)(object)response, elapsedMs: 5, traceId: "trace-test", retryCount: 1));
        }

        public Task<RemoteCallResult<TResponse>> CallWithRetryAsync<TResponse>(string serviceName, MessageObject requestMessage, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, int maxRetryCount = RemoteCallContext.DefaultMaxRetryCount, CancellationToken cancellationToken = default) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithoutRetryAsync<TResponse>(string serviceName, MessageObject requestMessage, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, CancellationToken cancellationToken = default) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithMetadataAsync<TResponse>(string serviceName, MessageObject requestMessage, Dictionary<string, string> metadata, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, CancellationToken cancellationToken = default) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task SendOneWayAsync(string serviceName, MessageObject requestMessage, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<bool> IsServiceAvailableAsync(string serviceName)
        {
            throw new NotImplementedException();
        }
    }
}
