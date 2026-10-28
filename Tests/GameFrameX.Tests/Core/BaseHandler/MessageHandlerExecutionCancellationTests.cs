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
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Core.Abstractions;
using GameFrameX.Core.Actors.Impl;
using GameFrameX.Core.BaseHandler.Normal;
using GameFrameX.Core.BaseHandler.RPC;
using GameFrameX.Network;
using GameFrameX.Network.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using GameFrameX.Tests.Utility;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.Core.BaseHandler;

/// <summary>
/// 消息处理器执行期协作取消回归测试（C336 / GFX-1046）。
/// </summary>
/// <remarks>
/// Regression tests for cooperative execution cancellation in message handlers (C336 / GFX-1046).
/// </remarks>
[Collection(GameAppRuntimeCollection.Name)]
public sealed class MessageHandlerExecutionCancellationTests
{
    private static readonly object SettingsLock = new();

    [Fact]
    public async Task NormalHandler_Timeout_ShouldCancelExecutionToken()
    {
        EnsureSettings();
        var handler = new DelayNormalHandler();
        await handler.InitAsync(new TestRequestMessage(), new CapturingNetworkChannel());

        // 超时 50ms：InnerActionAsync 返回后，业务代码应经 ExecutionCancellationToken 观察到取消
        await handler.InnerActionAsync(50);

        await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(handler.ObservedToken.CanBeCanceled);
        Assert.True(handler.ObservedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task NormalHandler_CallerCancel_ShouldPropagateToExecutionToken()
    {
        EnsureSettings();
        var handler = new DelayNormalHandler();
        await handler.InitAsync(new TestRequestMessage(), new CapturingNetworkChannel());
        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            var running = handler.InnerActionAsync(30000, cancellationTokenSource.Token);

            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellationTokenSource.Cancel();

            await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task RpcHandler_Timeout_ShouldCancelTokenAndWriteTimeOutResponse()
    {
        EnsureSettings();
        var networkChannel = new CapturingNetworkChannel();
        var handler = new DelayRpcHandler();
        await handler.InitAsync(new TestRequestMessage(), networkChannel);

        await handler.InnerActionAsync(50);

        // 超时回写语义保持：ErrorCode = TimeOut 且响应回填请求 UniqueId
        var written = Assert.Single(networkChannel.WrittenMessages);
        var response = Assert.IsType<TestResponseMessage>(written);
        Assert.Equal(OperationErrorCode.TimeOut, response.ErrorCode);
        Assert.Equal(TestRequestMessage.KnownUniqueId, response.UniqueId);

        await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(handler.ObservedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ExecutionCancellationToken_WithoutBaseAssignment_ShouldFallBackToRuntimeContext()
    {
        EnsureSettings();
        // 模拟组件处理器路径：handler 未经过基类 InnerActionAsync 赋值，在 actor 工作单元内读属性
        var handler = new DelayNormalHandler();
        var worker = new WorkerActor(210);
        var observedCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken tokenFromProperty = CancellationToken.None;
        CancellationToken tokenFromContext = CancellationToken.None;

        var task = worker.SendAsync(async () =>
        {
            tokenFromProperty = handler.GetExecutionCancellationToken();
            tokenFromContext = RuntimeContext.CurrentCancellationToken;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), handler.GetExecutionCancellationToken());
            }
            catch (OperationCanceledException)
            {
                observedCanceled.TrySetResult();
            }
        }, 80, true);

        // 等待方 80ms 超时放行（或协作取消先收尾），两者皆符合契约
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
        }

        await observedCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 回落语义：属性令牌 = WorkWrapper 写入的 RuntimeContext.CurrentCancellationToken，且随 actor 超时取消
        Assert.Equal(tokenFromContext, tokenFromProperty);
        Assert.True(tokenFromProperty.CanBeCanceled);
        Assert.True(tokenFromProperty.IsCancellationRequested);
    }

    private static void EnsureSettings()
    {
        if (GlobalSettings.CurrentSetting != null)
        {
            return;
        }

        lock (SettingsLock)
        {
            if (GlobalSettings.CurrentSetting == null)
            {
                GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
            }
        }
    }

    /// <summary>
    /// 等待执行期令牌取消的业务处理器（非组件路径）。业务体挂起在 <see cref="BaseMessageHandler{TRequest}.ExecutionCancellationToken"/> 上，
    /// 取消被观察后记录并正常收尾（不重抛，孤儿任务不产生未观测异常）。
    /// </summary>
    /// <remarks>
    /// A business handler waiting on the execution token (non-component path). The body parks on the execution token,
    /// records cancellation when observed and completes normally (no rethrow, so the orphan task yields no unobserved exception).
    /// </remarks>
    private sealed class DelayNormalHandler : BaseMessageHandler<TestRequestMessage>
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ObservedToken { get; private set; }

        /// <summary>
        /// 供测试在 actor 工作单元内读取受保护的执行期令牌属性（保持真实回落路径）。
        /// </summary>
        /// <remarks>
        /// Lets tests read the protected execution token property inside an actor work unit (preserving the real fallback path).
        /// </remarks>
        public CancellationToken GetExecutionCancellationToken()
        {
            return ExecutionCancellationToken;
        }

        protected override async Task ActionAsync(TestRequestMessage request)
        {
            ObservedToken = ExecutionCancellationToken;
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ExecutionCancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
            }
        }
    }

    /// <summary>
    /// 等待执行期令牌取消的 RPC 业务处理器（非组件路径）。
    /// </summary>
    /// <remarks>
    /// An RPC business handler waiting on the execution token (non-component path).
    /// </remarks>
    private sealed class DelayRpcHandler : BaseRpcMessageHandler<TestRequestMessage, TestResponseMessage>
    {
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ObservedToken { get; private set; }

        protected override async Task ActionAsync(TestRequestMessage request, TestResponseMessage response, CancellationToken cancellationToken)
        {
            ObservedToken = ExecutionCancellationToken;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ExecutionCancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
            }
        }
    }

    /// <summary>
    /// 测试请求消息。UniqueId 取固定已知值供超时响应回填断言。
    /// </summary>
    /// <remarks>
    /// Test request message with a fixed known UniqueId for timeout response assertions.
    /// </remarks>
    private sealed class TestRequestMessage : IRequestMessage
    {
        public const int KnownUniqueId = 4242;

        public int UniqueId { get; private set; } = KnownUniqueId;

        public void SetMessageId(int messageId)
        {
        }

        public void UpdateUniqueId()
        {
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
        }
    }

    /// <summary>
    /// 测试响应消息。
    /// </summary>
    /// <remarks>
    /// Test response message.
    /// </remarks>
    private sealed class TestResponseMessage : IResponseMessage
    {
        public int ErrorCode { get; set; }

        public int UniqueId { get; private set; }

        public void SetMessageId(int messageId)
        {
        }

        public void UpdateUniqueId()
        {
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
        }
    }

    /// <summary>
    /// 捕获写消息的最小网络通道假件。
    /// </summary>
    /// <remarks>
    /// A minimal network channel fake that captures written messages.
    /// </remarks>
    private sealed class CapturingNetworkChannel : INetworkChannel
    {
        public void Dispose()
        {
        }
        public List<INetworkMessage> WrittenMessages { get; } = new();

        public ulong SendBytesLength => 0;

        public ulong SendPacketLength => 0;

        public ulong ReceiveBytesLength => 0;

        public ulong ReceivePacketLength => 0;

        public IGameAppSession GameAppSession => null;

        public Task WriteAsync(INetworkMessage msg, int errorCode = 0)
        {
            WrittenMessages.Add(msg);
            return Task.CompletedTask;
        }

        public void Close()
        {
        }

        public T GetData<T>(string key)
        {
            return default;
        }

        public void ClearData()
        {
        }

        public void RemoveData(string key)
        {
        }

        public void SetData(string key, object value)
        {
        }

        public void UpdateReceivePacketBytesLength(ulong bufferLength)
        {
        }

        public void UpdateReceiveMessageTime(long offsetTicks = 0)
        {
        }

        public long GetLastMessageTimeSecond(in DateTime utcTime)
        {
            return 0;
        }

        public bool IsClosed()
        {
            return false;
        }
    }
}
