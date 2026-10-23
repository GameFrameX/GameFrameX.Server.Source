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

public class UnifiedMessageSenderMetricsIntegrationTests
{
    [Fact]
    public async Task SendToPlayerAsync_LocalDelivered_RecordsMetrics()
    {
        var metrics = new MessageSendMetrics();
        var sender = new UnifiedMessageSender(
            new FakeRemoteMessageClient(),
            new FakePlayerRouteResolver(PlayerRouteInfo.Offline()),
            new FakePlayerLocalSender(isOnline: true, sendResult: true),
            new ConsistentHashServerInstanceSelector(),
            GameServerConst.Game.Name,
            GameServerConst.Game.Id,
            metrics);

        var result = await sender.SendToPlayerAsync(1001, new ReqSendToPlayerInner());

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(metrics.GetSnapshots());
        Assert.Equal("player:Game", snapshot.Key);
        Assert.Equal(1, snapshot.TotalCalls);
        Assert.Equal(1, snapshot.SuccessCalls);
    }

    [Fact]
    public async Task SendToServerAsync_Success_RecordsMetrics()
    {
        var metrics = new MessageSendMetrics();
        var sender = new UnifiedMessageSender(
            new FakeRemoteMessageClient(),
            new FakePlayerRouteResolver(PlayerRouteInfo.Offline()),
            new FakePlayerLocalSender(isOnline: false, sendResult: false),
            new ConsistentHashServerInstanceSelector(),
            GameServerConst.Game.Name,
            GameServerConst.Game.Id,
            metrics);

        var result = await sender.SendToServerAsync<RespSendToPlayerInner>("social-service", new ReqSendToPlayerInner());

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(metrics.GetSnapshots());
        Assert.Equal("server:social-service", snapshot.Key);
        Assert.Equal(1, snapshot.TotalCalls);
        Assert.Equal(1, snapshot.SuccessCalls);
    }

    internal sealed class FakeRemoteMessageClient : IRemoteMessageClient
    {
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
            return Task.FromResult(BuildSuccessResult<TResponse>());
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(string serviceName, MessageObject requestMessage, int timeoutMs) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            return Task.FromResult(BuildSuccessResult<TResponse>());
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(string serviceName, MessageObject requestMessage, int timeoutMs, CancellationToken cancellationToken) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            return Task.FromResult(BuildSuccessResult<TResponse>());
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(RemoteCallContext context, MessageObject requestMessage) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            return Task.FromResult(BuildSuccessResult<TResponse>());
        }

        public Task<RemoteCallResult<TResponse>> CallWithRetryAsync<TResponse>(string serviceName, MessageObject requestMessage, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, int maxRetryCount = RemoteCallContext.DefaultMaxRetryCount, CancellationToken cancellationToken = default) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            return Task.FromResult(BuildSuccessResult<TResponse>());
        }

        public Task<RemoteCallResult<TResponse>> CallWithoutRetryAsync<TResponse>(string serviceName, MessageObject requestMessage, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, CancellationToken cancellationToken = default) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            return Task.FromResult(BuildSuccessResult<TResponse>());
        }

        public Task<RemoteCallResult<TResponse>> CallWithMetadataAsync<TResponse>(string serviceName, MessageObject requestMessage, Dictionary<string, string> metadata, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, CancellationToken cancellationToken = default) where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            return Task.FromResult(BuildSuccessResult<TResponse>());
        }

        public Task SendOneWayAsync(string serviceName, MessageObject requestMessage, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> IsServiceAvailableAsync(string serviceName)
        {
            return Task.FromResult(true);
        }

        private static RemoteCallResult<TResponse> BuildSuccessResult<TResponse>() where TResponse : class, GameFrameX.Network.Abstractions.IResponseMessage
        {
            if (typeof(TResponse) != typeof(RespSendToPlayerInner))
            {
                throw new NotSupportedException($"Unsupported response type: {typeof(TResponse).Name}");
            }

            var response = new RespSendToPlayerInner
            {
                Success = true,
                PlayerOffline = false,
            };

            return RemoteCallResult<TResponse>.Ok((TResponse)(object)response, elapsedMs: 5, traceId: "trace-test", retryCount: 1);
        }
    }
}
