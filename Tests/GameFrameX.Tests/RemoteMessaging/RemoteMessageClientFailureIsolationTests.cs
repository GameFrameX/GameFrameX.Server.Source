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

using GameFrameX.Network.RemoteMessaging.Composition;
using GameFrameX.Network.RemoteMessaging.Contracts;
using GameFrameX.Network.RemoteMessaging.Discovery;
using GameFrameX.Network.RemoteMessaging.Resilience;
using GameFrameX.Network.RemoteMessaging.Transport;
using System.Collections.Concurrent;
using GameFrameX.Network.RemoteMessaging.Observability;
using GameFrameX.Network.RemoteMessaging.Versioning;


namespace GameFrameX.Tests.RemoteMessaging;

/// <summary>
/// C304 失败语义定案测试：熔断 / 健康统计键按 <see cref="RemoteServiceTarget.ToString()"/> 拆分到实例维度；
/// <see cref="RemoteStatusCode.EndpointNotFound"/> 不计入熔断与健康评分且不可重试。
/// </summary>
/// <remarks>
/// C304 failure-semantics tests: the circuit-breaker / health keys split to the instance dimension
/// by <see cref="RemoteServiceTarget.ToString()"/>, and <see cref="RemoteStatusCode.EndpointNotFound"/>
/// never counts toward the circuit breaker or health score and is not retryable.
/// </remarks>
public sealed class RemoteMessageClientFailureIsolationTests
{
    private sealed class RecordingCircuitBreaker : ICircuitBreaker
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> FailureCounts = new(StringComparer.Ordinal);
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> SuccessCounts = new(StringComparer.Ordinal);
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> Opened = new(StringComparer.Ordinal);
        public bool IsAllowed(string serviceKey)
        {
            return !Opened.TryGetValue(serviceKey, out var open) || !open;
        }

        public CircuitState GetState(string serviceKey)
        {
            return Opened.TryGetValue(serviceKey, out var open) && open ? CircuitState.Open : CircuitState.Closed;
        }

        public void RecordFailure(string serviceKey)
        {
            FailureCounts.AddOrUpdate(serviceKey, 1, static (_, count) => count + 1);
        }

        public void RecordSuccess(string serviceKey)
        {
            SuccessCounts.AddOrUpdate(serviceKey, 1, static (_, count) => count + 1);
        }
    }

    private sealed class RecordingHealthEvaluator : IEndpointHealthEvaluator
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> UnavailableCounts = new(StringComparer.Ordinal);

        public int GetHealthScore(string serviceKey)
        {
            return 100;
        }

        public void MarkHealthy(string serviceKey)
        {
        }

        public void MarkUnavailable(string serviceKey, string reason)
        {
            UnavailableCounts.AddOrUpdate(serviceKey, 1, static (_, count) => count + 1);
        }
    }

    /// <summary>
    /// 始终抛连接异常的传输面（记失败键用），可设定拒绝的目标集合。
    /// </summary>
    private sealed class FailingTransportAdapter : ITransportProtocolAdapter
    {
        private readonly Func<RemoteServiceTarget, Exception> _failureFactory;

        public FailingTransportAdapter(Func<RemoteServiceTarget, Exception> failureFactory)
        {
            _failureFactory = failureFactory;
        }

        public string ProtocolName
        {
            get { return "Failing"; }
        }

        public Task<Stream> GetOrCreateStreamAsync(RemoteServiceTarget target, CancellationToken cancellationToken = default)
        {
            throw _failureFactory(target);
        }

        public bool IsServiceAvailable(RemoteServiceTarget target)
        {
            return false;
        }

        public void Invalidate()
        {
        }

        public void Invalidate(Stream stream)
        {
        }


        public void Dispose()
        {
        }
    }

    private static (RemoteMessageClient Client, RecordingCircuitBreaker Circuit, RecordingHealthEvaluator Health, AttemptCountingRetryPolicy Retry) CreateClient(ITransportProtocolAdapter adapter, bool enableRetry = false)
    {
        var circuit = new RecordingCircuitBreaker();
        var health = new RecordingHealthEvaluator();
        var retry = new AttemptCountingRetryPolicy(enableRetry);
        var client = new RemoteMessageClient(
            adapter,
            new DefaultMessageCodec(new DefaultMessageCompressionRegistry(), 0, 512, 1024 * 1024, 1024 * 1024),
            new RequestResponseMatcher(),
            new DefaultProtocolVersionNegotiator(),
            Array.Empty<IRemoteCallInterceptor>(),
            enableRetry ? retry : null,
            circuit,
            health,
            2000);
        return (client, circuit, health, retry);
    }

    /// <summary>
    /// 方法 3：实例 <c>Social/2001</c> 熔断后，<c>Social/2002</c> 与 ServerType 级 <c>Social</c> 仍然放行（失败键按实例拆分）。
    /// </summary>
    [Fact]
    public async Task CircuitBreakerKeys_SplitByInstance_InstanceFailureDoesNotAffectSiblingOrRole()
    {
        var adapter = new FailingTransportAdapter(target => new IOException($"boom {target}"));
        var (client, circuit, _, _) = CreateClient(adapter);

        var first = await client.CallWithResultAsync<TestResponseMessage>(
            RemoteCallContext.Create("Social", 500), new TestMessageObject());
        var second = await client.CallWithResultAsync<TestResponseMessage>(
            new RemoteCallContext { ServiceName = "Social", TargetInstanceId = "2001", TimeoutMs = 500 }, new TestMessageObject());

        Assert.False(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal(1, circuit.FailureCounts["Social"]);
        Assert.Equal(1, circuit.FailureCounts["Social/2001"]);
        Assert.False(circuit.FailureCounts.ContainsKey("Social/2002")); // 兄弟实例零失败记录
    }

    /// <summary>
    /// 方法 3：健康评分同样按实例维度拆分——实例失败只标记该实例不可用。
    /// </summary>
    [Fact]
    public async Task HealthEvaluatorKeys_SplitByInstance_InstanceFailureMarksOnlyThatInstance()
    {
        var adapter = new FailingTransportAdapter(target => new IOException($"boom {target}"));
        var (client, _, health, _) = CreateClient(adapter);

        var result = await client.CallWithResultAsync<TestResponseMessage>(
            new RemoteCallContext { ServiceName = "Social", TargetInstanceId = "2002", TimeoutMs = 500 }, new TestMessageObject());

        Assert.False(result.IsSuccess);
        Assert.Equal(1, health.UnavailableCounts["Social/2002"]);
        Assert.False(health.UnavailableCounts.ContainsKey("Social/2001"));
        Assert.False(health.UnavailableCounts.ContainsKey("Social"));
    }

    /// <summary>
    /// 方法 4：固定路由表为空 ⇒ EndpointNotFound——不触发熔断 RecordFailure、不触发健康 MarkUnavailable、不重试（尝试次数恒 1）。
    /// </summary>
    [Fact]
    public async Task EndpointNotFound_DoesNotRecordFailures_AndNeverRetries()
    {
        // 空路由表：SchemeDispatch 经唯一解析器解析不到任何端点 → RemoteEndpointNotFoundException → EndpointNotFound
        var transportAdapter = new SchemeDispatchTransportAdapter(
            new DiscoveryServiceEndpointResolver(RouteTableTestHelper.EmptyProvider()),
            new TransportProtocolAdapterRegistry());

        // 直接构造客户端注入熔断 / 健康 spy（internal 可见）
        var (client, circuit, health, retry) = CreateClient(transportAdapter, enableRetry: true);

        var result = await client.CallWithResultAsync<TestResponseMessage>(
            new RemoteCallContext { ServiceName = "Social", TargetInstanceId = "2001", TimeoutMs = 500, AllowRetry = true, MaxRetryCount = 2 }, new TestMessageObject());

        Assert.Equal(RemoteStatusCode.EndpointNotFound, result.StatusCode);
        Assert.Equal(0, circuit.FailureCounts.Values.Sum());
        Assert.Equal(0, health.UnavailableCounts.Values.Sum());
        Assert.Equal(0, retry.Attempts); // 重试策略从未被询问（catch 路径硬性返回不重试）——尝试次数恒 1
    }

    /// <summary>
    /// 计数重试策略：记录 ShouldRetry 询问次数（每次尝试询问一次）。
    /// </summary>
    private sealed class AttemptCountingRetryPolicy : IRetryPolicy
    {
        private readonly bool _enabled;

        public AttemptCountingRetryPolicy(bool enabled)
        {
            _enabled = enabled;
        }

        public int Attempts
        {
            get;
            private set;
        }

        public bool ShouldRetry(RemoteCallContext context, RemoteStatusCode statusCode, int attemptCount)
        {
            if (_enabled)
            {
                Attempts++;
            }

            return _enabled && (statusCode == RemoteStatusCode.Timeout || statusCode == RemoteStatusCode.ConnectionFailed);
        }

        public int GetRetryDelayMs(int attemptCount)
        {
            return 1;
        }
    }
}
