// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full text of the license.
//   禁止利用本项目实施任何危害国家安全、破坏公共秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt public order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or to infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Network.RemoteMessaging.Composition;
using GameFrameX.Network.RemoteMessaging.Contracts;
using GameFrameX.Network.RemoteMessaging.Discovery;
using GameFrameX.Network.RemoteMessaging.Observability;
using GameFrameX.Network.RemoteMessaging.Resilience;
using GameFrameX.Network.RemoteMessaging.Transport;
using GameFrameX.Network.RemoteMessaging.Versioning;

namespace GameFrameX.Tests.RemoteMessaging;

/// <summary>
/// C351 接收泵故障观察与关闭收口测试：泵 fault 异常被 OnlyOnFaulted continuation 观察
/// （不落入 UnobservedTaskException），解码失败与 Dispose 均使挂起请求快速失败。
/// </summary>
/// <remarks>
/// C351 receive-pump fault observation and shutdown tests: a faulted pump's exception is observed
/// by the OnlyOnFaulted continuation (never surfacing as UnobservedTaskException), and both decode
/// failure and Dispose fail pending requests fast.
/// </remarks>
public sealed class RemoteMessageClientPumpTests
{
    /// <summary>泵未分类异常标记：仅用于断言该异常不会以未观察身份进入 UnobservedTaskException。</summary>
    private sealed class PumpFaultMarkerException : Exception
    {
        public PumpFaultMarkerException()
            : base("pump fault marker")
        {
        }
    }

    /// <summary>
    /// 行为注入流：ReadAsync 委托外部函数，写侧丢弃；用于驱动接收泵各故障路径。
    /// </summary>
    private sealed class ScriptedStream : Stream
    {
        private readonly Func<CancellationToken, Task<int>> _readBehavior;

        public ScriptedStream(Func<CancellationToken, Task<int>> readBehavior)
        {
            _readBehavior = readBehavior;
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override long Length
        {
            get { throw new NotSupportedException(); }
        }

        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _readBehavior(cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(_readBehavior(cancellationToken));
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// 单流传输适配器：每次 GetOrCreateStreamAsync 返回注入的流，计数 Invalidate 调用。
    /// </summary>
    private sealed class SingleStreamTransportAdapter : ITransportProtocolAdapter
    {
        private readonly Stream _stream;

        public SingleStreamTransportAdapter(Stream stream)
        {
            _stream = stream;
        }

        public int InvalidateAllCallCount
        {
            get;
            private set;
        }

        public string ProtocolName
        {
            get { return "Scripted"; }
        }

        public Task<Stream> GetOrCreateStreamAsync(RemoteServiceTarget target, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_stream);
        }

        public bool IsServiceAvailable(RemoteServiceTarget target)
        {
            return true;
        }

        public void Invalidate()
        {
            InvalidateAllCallCount++;
        }

        public void Invalidate(Stream stream)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class AllowAllCircuitBreaker : ICircuitBreaker
    {
        public bool IsAllowed(string serviceKey)
        {
            return true;
        }

        public CircuitState GetState(string serviceKey)
        {
            return CircuitState.Closed;
        }

        public void RecordFailure(string serviceKey)
        {
        }

        public void RecordSuccess(string serviceKey)
        {
        }
    }

    private sealed class AlwaysHealthyEvaluator : IEndpointHealthEvaluator
    {
        public int GetHealthScore(string serviceKey)
        {
            return 100;
        }

        public void MarkHealthy(string serviceKey)
        {
        }

        public void MarkUnavailable(string serviceKey, string reason)
        {
        }
    }

    private static RemoteMessageClient CreateClient(ITransportProtocolAdapter adapter)
    {
        return new RemoteMessageClient(
            adapter,
            new DefaultMessageCodec(new DefaultMessageCompressionRegistry(), 0, 512, 1024 * 1024, 1024 * 1024),
            new RequestResponseMatcher(),
            new DefaultProtocolVersionNegotiator(),
            Array.Empty<IRemoteCallInterceptor>(),
            null,
            new AllowAllCircuitBreaker(),
            new AlwaysHealthyEvaluator(),
            2000);
    }

    /// <summary>
    /// 验收 1：假服务端发送损坏帧（解码抛 InvalidDataException）→ 挂起请求快速失败为 ConnectionFailed。
    /// </summary>
    [Fact]
    public async Task CorruptFrame_FailsPendingRequestAsConnectionFailed()
    {
        var stream = new ScriptedStream(async cancellationToken =>
        {
            await Task.Delay(50, cancellationToken);
            throw new InvalidDataException("corrupt frame");
        });
        var adapter = new SingleStreamTransportAdapter(stream);
        var client = CreateClient(adapter);

        var result = await client.CallWithResultAsync<TestResponseMessage>(
            RemoteCallContext.Create("Social", 10000), new TestMessageObject());

        Assert.False(result.IsSuccess);
        Assert.Equal(RemoteStatusCode.ConnectionFailed, result.StatusCode);
    }

    /// <summary>
    /// 验收 2：泵抛未分类异常 → 泵任务 fault，异常经 OnlyOnFaulted continuation 观察；
    /// 强制 GC 后标记异常不出现在 UnobservedTaskException。
    /// </summary>
    [Fact]
    public async Task UnclassifiedPumpException_IsObservedAndNeverUnobserved()
    {
        var unobservedMarkers = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        void OnUnobserved(object sender, UnobservedTaskExceptionEventArgs eventArgs)
        {
            foreach (var innerException in eventArgs.Exception.InnerExceptions)
            {
                if (innerException is PumpFaultMarkerException)
                {
                    unobservedMarkers.Enqueue(innerException);
                }
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            var stream = new ScriptedStream(async cancellationToken =>
            {
                await Task.Delay(50, cancellationToken);
                throw new PumpFaultMarkerException();
            });
            var adapter = new SingleStreamTransportAdapter(stream);
            var client = CreateClient(adapter);

            var result = await client.CallWithResultAsync<TestResponseMessage>(
                RemoteCallContext.Create("Social", 10000), new TestMessageObject());

            Assert.False(result.IsSuccess);
            Assert.Equal(RemoteStatusCode.ConnectionFailed, result.StatusCode);

            // 等泵 fault 收尾（连接移出 _connections）并强制终结，未观察异常只可能在终结时上报
            await Task.Delay(200);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(50);
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        Assert.Empty(unobservedMarkers);
    }

    /// <summary>
    /// 验收 3：Dispose 客户端 → 全部挂起请求快速失败（不等满握手/调用超时），泵在超时内退出。
    /// </summary>
    [Fact]
    public async Task Dispose_FailsPendingRequestsFast_AndWaitsForPumpExit()
    {
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pumpExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new ScriptedStream(async cancellationToken =>
        {
            readStarted.TrySetResult();
            await pumpExited.Task.WaitAsync(cancellationToken);
            return 0;
        });
        var adapter = new SingleStreamTransportAdapter(stream);
        var client = CreateClient(adapter);

        var callTask = client.CallWithResultAsync<TestResponseMessage>(
            RemoteCallContext.Create("Social", 30000), new TestMessageObject());
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Dispose 等待泵退出：放行阻塞的 ReadAsync 后，Dispose 应在 500ms 超时窗口内完成收口
        var disposeTask = Task.Run(client.Dispose);
        var releaseDelay = Task.Delay(100);
        await Task.WhenAll(releaseDelay, disposeTask).WaitAsync(TimeSpan.FromSeconds(3));
        pumpExited.TrySetResult();

        var result = await callTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.IsSuccess);
        Assert.True(result.StatusCode == RemoteStatusCode.ConnectionFailed, $"actual={result.StatusCode}");
        Assert.True(adapter.InvalidateAllCallCount >= 1, "Dispose 应全量失效传输层");

        // 幂等：二次 Dispose 不抛
        client.Dispose();
    }
}
