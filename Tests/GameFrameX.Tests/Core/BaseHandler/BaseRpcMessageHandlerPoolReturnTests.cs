// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序的行为！
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷及责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
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

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Core.BaseHandler.RPC;
using GameFrameX.Network;
using GameFrameX.Network.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.Core.BaseHandler;

/// <summary>
/// BaseRpcMessageHandler 响应对象池生命周期回归测试（GFX-1019）：
/// 业务任务到达终态（成功/业务异常）时响应对象归还对象池；超时/外部取消时先取消孤儿业务任务，
/// 仍在运行的孤儿持有的响应对象不归还，防止其写入污染下一个借用者（数据串台）。
/// </summary>
public sealed class BaseRpcMessageHandlerPoolReturnTests
{
    private const int RequestUniqueId = 4242;

    /// <summary>
    /// InnerActionCoreAsync 读取 GlobalSettings.CurrentSetting，测试环境需保证非空。
    /// </summary>
    private static void EnsureGlobalSettings()
    {
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1, });
        }
    }

    private static async Task<ControlledRpcHandler> CreateInitializedHandlerAsync(CapturingNetworkChannel channel, Func<PoolReturnTestRequest, PoolReturnTestResponse, CancellationToken, Task> behavior)
    {
        EnsureGlobalSettings();
        var handler = new ControlledRpcHandler { Behavior = behavior, };
        var request = new PoolReturnTestRequest();
        request.SetUniqueId(RequestUniqueId);
        Assert.True(await handler.InitAsync(request, channel));
        return handler;
    }

    [Fact]
    public async Task Success_Path_WritesResponse_And_ReturnsResponseToPool()
    {
        var channel = new CapturingNetworkChannel();
        var handler = await CreateInitializedHandlerAsync(channel, null);

        await handler.InnerActionAsync(5000);

        var written = Assert.IsType<PoolReturnTestResponse>(Assert.Single(channel.WrittenMessages));
        var snapshot = Assert.Single(channel.WrittenResponseSnapshots);
        Assert.Equal(0, snapshot.ErrorCode);
        Assert.Equal(RequestUniqueId, snapshot.UniqueId);

        // 成功路径任务终态后归还：下一次借用应复得同一实例（DefaultObjectPool 后进先出）
        var nextBorrowed = MessageObjectPoolHelper.Get<PoolReturnTestResponse>();
        Assert.Same(handler.ReceivedResponse, nextBorrowed);
        MessageObjectPoolHelper.Return(nextBorrowed);
    }

    [Fact]
    public async Task Timeout_Path_CancelsBusinessTask_SendsTimeoutResponse_And_DoesNotReturnToPool()
    {
        var channel = new CapturingNetworkChannel();
        Task BusinessDelay(PoolReturnTestRequest _, PoolReturnTestResponse __, CancellationToken token)
        {
            return Task.Delay(Timeout.Infinite, token).ContinueWith(async delayedTask =>
            {
                try
                {
                    await delayedTask;
                }
                catch (TaskCanceledException)
                {
                    // 观察到取消但保持运行：模拟业务在协作式取消点之间不可中断的段落
                    await Task.Delay(Timeout.Infinite);
                }
            }, TaskContinuationOptions.ExecuteSynchronously).Unwrap();
        }

        var handler = await CreateInitializedHandlerAsync(channel, BusinessDelay);

        await handler.InnerActionAsync(50);

        var written = Assert.IsType<PoolReturnTestResponse>(Assert.Single(channel.WrittenMessages));
        var snapshot = Assert.Single(channel.WrittenResponseSnapshots);
        Assert.Equal(OperationErrorCode.TimeOut, snapshot.ErrorCode);
        Assert.Equal(RequestUniqueId, snapshot.UniqueId);

        // 孤儿任务仍持有该实例：不得归还对象池，下一次借用必须是不同实例
        var nextBorrowed = MessageObjectPoolHelper.Get<PoolReturnTestResponse>();
        Assert.NotSame(handler.ReceivedResponse, nextBorrowed);
        MessageObjectPoolHelper.Return(nextBorrowed);
    }

    [Fact]
    public async Task Timeout_LateBusinessWrite_ShouldNotChangeSentResponse()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new CapturingNetworkChannel();
        var handler = await CreateInitializedHandlerAsync(channel, (request, response, cancellationToken) => completion.Task);
        try
        {
            await handler.InnerActionAsync(20).WaitAsync(TimeSpan.FromSeconds(10));
            var sent = Assert.IsType<PoolReturnTestResponse>(Assert.Single(channel.WrittenMessages));
            handler.ReceivedResponse.ErrorCode = 12345;
            Assert.NotEqual(12345, sent.ErrorCode);
            Assert.Equal(OperationErrorCode.TimeOut, Assert.Single(channel.WrittenResponseSnapshots).ErrorCode);
        }
        finally
        {
            completion.TrySetResult(true);
        }
    }

    [Fact]
    public async Task BusinessFault_Path_DoesNotWrite_And_ReturnsResponseToPool()
    {
        var channel = new CapturingNetworkChannel();
        Task Throw(PoolReturnTestRequest _, PoolReturnTestResponse __, CancellationToken ___)
        {
            throw new InvalidOperationException("pool return test business failure");
        }

        var handler = await CreateInitializedHandlerAsync(channel, Throw);

        await handler.InnerActionAsync(5000);

        // 业务异常被外层捕获记录，不写响应；任务终态（Faulted）→ 归还对象池
        Assert.Empty(channel.WrittenMessages);
        var nextBorrowed = MessageObjectPoolHelper.Get<PoolReturnTestResponse>();
        Assert.Same(handler.ReceivedResponse, nextBorrowed);
        MessageObjectPoolHelper.Return(nextBorrowed);
    }

    [Fact]
    public async Task ExternalCancel_Path_DoesNotWrite_And_DoesNotReturnToPool()
    {
        var channel = new CapturingNetworkChannel();
        Task BusinessDelay(PoolReturnTestRequest _, PoolReturnTestResponse __, CancellationToken token)
        {
            return Task.Delay(Timeout.Infinite, token).ContinueWith(async delayedTask =>
            {
                try
                {
                    await delayedTask;
                }
                catch (TaskCanceledException)
                {
                    await Task.Delay(Timeout.Infinite);
                }
            }, TaskContinuationOptions.ExecuteSynchronously).Unwrap();
        }

        var handler = await CreateInitializedHandlerAsync(channel, BusinessDelay);

        using (var externalCancellationTokenSource = new CancellationTokenSource())
        {
            externalCancellationTokenSource.Cancel();
            await handler.InnerActionAsync(5000, externalCancellationTokenSource.Token);
        }

        // 外部取消走外层异常捕获，不写响应；孤儿任务仍在运行 → 不归还
        Assert.Empty(channel.WrittenMessages);
        Assert.True(handler.ReceivedToken.IsCancellationRequested);
        var nextBorrowed = MessageObjectPoolHelper.Get<PoolReturnTestResponse>();
        Assert.NotSame(handler.ReceivedResponse, nextBorrowed);
        MessageObjectPoolHelper.Return(nextBorrowed);
    }

    /// <summary>
    /// 受控测试处理器：记录收到的响应实例与取消令牌，行为可注入。
    /// </summary>
    private sealed class ControlledRpcHandler : BaseRpcMessageHandler<PoolReturnTestRequest, PoolReturnTestResponse>
    {
        /// <summary>
        /// 业务行为；null 表示立即成功返回。
        /// </summary>
        public Func<PoolReturnTestRequest, PoolReturnTestResponse, CancellationToken, Task> Behavior { get; init; }

        public PoolReturnTestResponse ReceivedResponse { get; private set; }

        public CancellationToken ReceivedToken { get; private set; }

        protected override async Task ActionAsync(PoolReturnTestRequest request, PoolReturnTestResponse response, CancellationToken cancellationToken)
        {
            ReceivedResponse = response;
            ReceivedToken = cancellationToken;
            if (Behavior != null)
            {
                await Behavior(request, response, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 捕获写通道消息的假网络通道。
    /// </summary>
    private sealed class CapturingNetworkChannel : INetworkChannel
    {
        private readonly System.Collections.Generic.Dictionary<string, object> _data = new();
        private bool _closed;

        public System.Collections.Generic.List<INetworkMessage> WrittenMessages { get; } = new();
        public System.Collections.Generic.List<(int UniqueId, int ErrorCode)> WrittenResponseSnapshots { get; } = new();

        public ulong SendBytesLength => 0;

        public ulong SendPacketLength => 0;

        public ulong ReceiveBytesLength => 0;

        public ulong ReceivePacketLength => 0;

        public IGameAppSession GameAppSession => null;

        public Task WriteAsync(INetworkMessage msg, int errorCode = 0)
        {
            WrittenMessages.Add(msg);
            if (msg is IResponseMessage responseMessage)
            {
                // 写通道时刻快照字段值：响应对象随后可能被归还对象池并重置
                WrittenResponseSnapshots.Add((responseMessage.UniqueId, responseMessage.ErrorCode));
            }

            return Task.CompletedTask;
        }

        public void Close()
        {
            _closed = true;
        }

        public T GetData<T>(string key)
        {
            return _data.TryGetValue(key, out var value) ? (T)value : default;
        }

        public void ClearData()
        {
            _data.Clear();
        }

        public void RemoveData(string key)
        {
            _data.Remove(key);
        }

        public void SetData(string key, object value)
        {
            _data[key] = value;
        }

        public void UpdateReceivePacketBytesLength(ulong bufferLength)
        {
        }

        public void UpdateReceiveMessageTime(long offsetTicks = 0)
        {
        }

        public long GetLastMessageTimeSecond(in System.DateTime utcTime)
        {
            return 0;
        }

        public bool IsClosed()
        {
            return _closed;
        }
    }

    /// <summary>
    /// 测试专用请求消息。
    /// </summary>
    private sealed class PoolReturnTestRequest : IRequestMessage
    {
        public int UniqueId { get; private set; }

        public void SetMessageId(int messageId)
        {
            UniqueId = messageId;
        }

        public void UpdateUniqueId()
        {
            UniqueId = GetHashCode();
        }

        public void SetUniqueId(int uniqueId)
        {
            UniqueId = uniqueId;
        }

        public string ToJsonString()
        {
            return string.Empty;
        }

        public string ToFormatMessageString(long actorId = default)
        {
            return string.Empty;
        }

        public void Clear()
        {
            UniqueId = 0;
        }
    }

    /// <summary>
    /// 测试专用响应消息。
    /// </summary>
    private sealed class PoolReturnTestResponse : IResponseMessage
    {
        public int UniqueId { get; private set; }

        public int ErrorCode { get; set; }

        public void SetMessageId(int messageId)
        {
            UniqueId = messageId;
        }

        public void UpdateUniqueId()
        {
            UniqueId = GetHashCode();
        }

        public void SetUniqueId(int uniqueId)
        {
            UniqueId = uniqueId;
        }

        public string ToJsonString()
        {
            return string.Empty;
        }

        public string ToFormatMessageString(long actorId = default)
        {
            return string.Empty;
        }

        public void Clear()
        {
            UniqueId = 0;
            ErrorCode = 0;
        }
    }
}
