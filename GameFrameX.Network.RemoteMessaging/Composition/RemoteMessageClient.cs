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

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using GameFrameX.Foundation.Localization.Core;
namespace GameFrameX.Network.RemoteMessaging.Composition;

/// <summary>
/// 统一远程消息调用客户端。串联服务解析、连接管理、编解码、请求匹配、拦截器、重试、熔断和健康探测。
/// </summary>
/// <remarks>
/// Unified remote message call client. Orchestrates service resolution, connection management, encoding/decoding, request-response matching, interceptors, retry, circuit breaking, and health probing.
/// </remarks>
internal sealed class RemoteMessageClient : IRemoteMessageClient
{
    private readonly ConcurrentDictionary<Stream, ConnectionContext> _connections = new();
    private readonly ICircuitBreaker _circuitBreaker;
    private readonly IEndpointHealthEvaluator _healthEvaluator;
    private readonly IRemoteCallInterceptor[] _interceptors;
    private readonly IMessageCodec _messageCodec;
    private readonly IProtocolVersionNegotiator _protocolVersionNegotiator;
    private readonly IRequestResponseMatcher _requestResponseMatcher;
    private readonly IRetryPolicy _retryPolicy;
    private readonly ITransportProtocolAdapter _transportProtocolAdapter;
    private readonly int _handshakeTimeoutMs;

    /// <summary>
    /// 初始化统一远程消息调用客户端。
    /// </summary>
    /// <remarks>
    /// Initializes the unified remote message call client.
    /// </remarks>
    /// <param name="transportProtocolAdapter">传输协议适配器 / Transport protocol adapter</param>
    /// <param name="messageCodec">消息编解码器 / Message codec</param>
    /// <param name="requestResponseMatcher">请求-响应匹配器 / Request-response matcher</param>
    /// <param name="protocolVersionNegotiator">协议版本协商器 / Protocol version negotiator</param>
    /// <param name="interceptors">远程调用拦截器数组 / Array of remote call interceptors</param>
    /// <param name="retryPolicy">重试策略（可为 null） / Retry policy (can be null)</param>
    /// <param name="circuitBreaker">熔断器 / Circuit breaker</param>
    public RemoteMessageClient(
        ITransportProtocolAdapter transportProtocolAdapter,
        IMessageCodec messageCodec,
        IRequestResponseMatcher requestResponseMatcher,
        IProtocolVersionNegotiator protocolVersionNegotiator,
        IRemoteCallInterceptor[] interceptors,
        IRetryPolicy retryPolicy,
        ICircuitBreaker circuitBreaker,
        IEndpointHealthEvaluator healthEvaluator,
        int handshakeTimeoutMs)
    {
        _transportProtocolAdapter = transportProtocolAdapter;
        _messageCodec = messageCodec;
        _requestResponseMatcher = requestResponseMatcher ?? throw new ArgumentNullException(nameof(requestResponseMatcher));
        _protocolVersionNegotiator = protocolVersionNegotiator;
        _interceptors = interceptors ?? Array.Empty<IRemoteCallInterceptor>();
        _retryPolicy = retryPolicy;
        _circuitBreaker = circuitBreaker;
        _healthEvaluator = healthEvaluator;
        _handshakeTimeoutMs = handshakeTimeoutMs > 0 ? handshakeTimeoutMs : 2000;
    }

    /// <summary>
    /// 发送请求并等待响应（使用默认超时）。
    /// </summary>
    /// <remarks>
    /// Sends a request and waits for a response (using default timeout).
    /// </remarks>
    /// <typeparam name="TResponse">响应消息类型 / The response message type</typeparam>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    /// <param name="requestMessage">请求消息对象 / The request message object</param>
    /// <returns>响应消息对象；超时或失败时返回 null / The response message object; returns null on timeout or failure</returns>
    public Task<TResponse> CallAsync<TResponse>(
        string serviceName,
        MessageObject requestMessage)
        where TResponse : class, IResponseMessage
    {
        return CallAsync<TResponse>(serviceName, requestMessage, RemoteCallContext.DefaultTimeoutMs);
    }

    /// <summary>
    /// 发送请求并等待响应（使用默认超时，支持取消）。
    /// </summary>
    /// <remarks>
    /// Sends a request and waits for a response (using default timeout, with cancellation support).
    /// </remarks>
    /// <typeparam name="TResponse">响应消息类型 / The response message type</typeparam>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    /// <param name="requestMessage">请求消息对象 / The request message object</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>响应消息对象；超时或失败时返回 null / The response message object; returns null on timeout or failure</returns>
    public Task<TResponse> CallAsync<TResponse>(
        string serviceName,
        MessageObject requestMessage,
        CancellationToken cancellationToken)
        where TResponse : class, IResponseMessage
    {
        return CallAsync<TResponse>(serviceName, requestMessage, RemoteCallContext.DefaultTimeoutMs, cancellationToken);
    }

    /// <summary>
    /// 发送请求并等待响应（指定超时）。
    /// </summary>
    /// <remarks>
    /// Sends a request and waits for a response (with specified timeout).
    /// </remarks>
    /// <typeparam name="TResponse">响应消息类型 / The response message type</typeparam>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    /// <param name="requestMessage">请求消息对象 / The request message object</param>
    /// <param name="timeoutMs">超时毫秒数 / The timeout duration in milliseconds</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>响应消息对象；超时或失败时返回 null / The response message object; returns null on timeout or failure</returns>
    public async Task<TResponse> CallAsync<TResponse>(
        string serviceName,
        MessageObject requestMessage,
        int timeoutMs,
        CancellationToken cancellationToken = default)
        where TResponse : class, IResponseMessage
    {
        var context = RemoteCallContext.Create(serviceName, timeoutMs, cancellationToken);
        var result = await CallWithResultAsync<TResponse>(context, requestMessage);
        return result.Response;
    }

    /// <summary>
    /// 发送请求并返回结构化结果（使用默认超时）。
    /// </summary>
    /// <remarks>
    /// Sends a request and returns a structured result (using default timeout).
    /// </remarks>
    /// <typeparam name="TResponse">响应消息类型 / The response message type</typeparam>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    /// <param name="requestMessage">请求消息对象 / The request message object</param>
    /// <returns>结构化调用结果 / The structured call result</returns>
    public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(
        string serviceName,
        MessageObject requestMessage)
        where TResponse : class, IResponseMessage
    {
        var context = RemoteCallContext.Create(serviceName);
        return CallWithResultAsync<TResponse>(context, requestMessage);
    }

    /// <summary>
    /// 发送请求并返回结构化结果（指定超时）。
    /// </summary>
    /// <remarks>
    /// Sends a request and returns a structured result (with specified timeout).
    /// </remarks>
    /// <typeparam name="TResponse">响应消息类型 / The response message type</typeparam>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    /// <param name="requestMessage">请求消息对象 / The request message object</param>
    /// <param name="timeoutMs">超时毫秒数 / The timeout duration in milliseconds</param>
    /// <returns>结构化调用结果 / The structured call result</returns>
    public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(
        string serviceName,
        MessageObject requestMessage,
        int timeoutMs)
        where TResponse : class, IResponseMessage
    {
        var context = RemoteCallContext.Create(serviceName, timeoutMs);
        return CallWithResultAsync<TResponse>(context, requestMessage);
    }

    /// <summary>
    /// 发送请求并返回结构化结果（指定超时和取消令牌）。
    /// </summary>
    /// <remarks>
    /// Sends a request and returns a structured result (with specified timeout and cancellation token).
    /// </remarks>
    /// <typeparam name="TResponse">响应消息类型 / The response message type</typeparam>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    /// <param name="requestMessage">请求消息对象 / The request message object</param>
    /// <param name="timeoutMs">超时毫秒数 / The timeout duration in milliseconds</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>结构化调用结果 / The structured call result</returns>
    public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(
        string serviceName,
        MessageObject requestMessage,
        int timeoutMs,
        CancellationToken cancellationToken)
        where TResponse : class, IResponseMessage
    {
        var context = RemoteCallContext.Create(serviceName, timeoutMs, cancellationToken);
        return CallWithResultAsync<TResponse>(context, requestMessage);
    }

    /// <summary>
    /// 发送幂等请求并返回结构化结果（自动启用重试）。适用于读操作和查询接口。
    /// </summary>
    /// <remarks>
    /// Sends an idempotent request and returns a structured result (retry automatically enabled). Suitable for read operations and query interfaces.
    /// </remarks>
    /// <typeparam name="TResponse">响应消息类型 / The response message type</typeparam>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    /// <param name="requestMessage">请求消息对象 / The request message object</param>
    /// <param name="timeoutMs">超时毫秒数 / The timeout duration in milliseconds</param>
    /// <param name="maxRetryCount">最大重试次数 / Maximum retry count</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>结构化调用结果 / The structured call result</returns>
    public Task<RemoteCallResult<TResponse>> CallWithRetryAsync<TResponse>(
        string serviceName,
        MessageObject requestMessage,
        int timeoutMs = RemoteCallContext.DefaultTimeoutMs,
        int maxRetryCount = RemoteCallContext.DefaultMaxRetryCount,
        CancellationToken cancellationToken = default)
        where TResponse : class, IResponseMessage
    {
        var context = RemoteCallContext.CreateIdempotent(serviceName, timeoutMs, maxRetryCount, cancellationToken);
        return CallWithResultAsync<TResponse>(context, requestMessage);
    }

    /// <summary>
    /// 发送非幂等请求并返回结构化结果（禁用重试）。适用于写操作和状态变更接口。
    /// </summary>
    /// <remarks>
    /// Sends a non-idempotent request and returns a structured result (retry disabled). Suitable for write operations and state-changing interfaces.
    /// </remarks>
    /// <typeparam name="TResponse">响应消息类型 / The response message type</typeparam>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    /// <param name="requestMessage">请求消息对象 / The request message object</param>
    /// <param name="timeoutMs">超时毫秒数 / The timeout duration in milliseconds</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>结构化调用结果 / The structured call result</returns>
    public Task<RemoteCallResult<TResponse>> CallWithoutRetryAsync<TResponse>(
        string serviceName,
        MessageObject requestMessage,
        int timeoutMs = RemoteCallContext.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
        where TResponse : class, IResponseMessage
    {
        var context = RemoteCallContext.CreateNonIdempotent(serviceName, timeoutMs, cancellationToken);
        return CallWithResultAsync<TResponse>(context, requestMessage);
    }

    /// <summary>
    /// 发送请求并返回结构化结果（携带环境参数）。环境参数可通过拦截器读取，用于日志记录、链路追踪等。
    /// </summary>
    /// <remarks>
    /// Sends a request and returns a structured result (with environment parameters). Environment parameters can be read by interceptors for logging, tracing, etc.
    /// </remarks>
    /// <typeparam name="TResponse">响应消息类型 / The response message type</typeparam>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    /// <param name="requestMessage">请求消息对象 / The request message object</param>
    /// <param name="metadata">环境参数字典（如 ServerId、PlayerId 等） / Environment parameter dictionary (e.g., ServerId, PlayerId)</param>
    /// <param name="timeoutMs">超时毫秒数 / The timeout duration in milliseconds</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>结构化调用结果 / The structured call result</returns>
    public Task<RemoteCallResult<TResponse>> CallWithMetadataAsync<TResponse>(
        string serviceName,
        MessageObject requestMessage,
        Dictionary<string, string> metadata,
        int timeoutMs = RemoteCallContext.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
        where TResponse : class, IResponseMessage
    {
        var context = RemoteCallContext.CreateWithMetadata(serviceName, metadata, timeoutMs, cancellationToken);
        return CallWithResultAsync<TResponse>(context, requestMessage);
    }

    /// <summary>
    /// 发送请求并返回结构化结果（包含状态码、耗时、重试信息）。
    /// </summary>
    /// <remarks>
    /// Sends a request and returns a structured result containing status code, elapsed time, and retry information.
    /// </remarks>
    /// <typeparam name="TResponse">响应消息类型 / The response message type</typeparam>
    /// <param name="context">调用上下文（含超时、重试策略、追踪信息） / The call context (including timeout, retry strategy, and tracing info)</param>
    /// <param name="requestMessage">请求消息对象 / The request message object</param>
    /// <returns>结构化调用结果 / The structured call result</returns>
    public async Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(
        RemoteCallContext context,
        MessageObject requestMessage)
        where TResponse : class, IResponseMessage
    {
        var stopwatch = Stopwatch.StartNew();
        var attemptCount = 0;

        while (true)
        {
            attemptCount++;
            stopwatch.Restart();
            _requestResponseMatcher?.CleanupExpired();

            var preconditionFailure = CheckPreconditions<TResponse>(context, stopwatch);
            if (preconditionFailure != null)
            {
                return preconditionFailure;
            }

            var attempt = await ExecuteAttemptAsync<TResponse>(context, requestMessage, stopwatch, attemptCount);
            if (attempt.ShouldRetry)
            {
                await Task.Delay(_retryPolicy.GetRetryDelayMs(attemptCount));
                continue;
            }

            return attempt.Result;
        }
    }

    /// <summary>
    /// 执行调用前置检查（熔断器、端点健康评分）。
    /// </summary>
    /// <remarks>
    /// Performs pre-call checks (circuit breaker, endpoint health score). Returns a failure result, or null when the call may proceed.
    /// </remarks>
    /// <typeparam name="TResponse">响应消息类型 / The response message type</typeparam>
    /// <param name="context">调用上下文 / The call context</param>
    /// <param name="stopwatch">计时器 / The elapsed-time stopwatch</param>
    /// <returns>失败结果；通过检查时返回 null / A failure result; null when the call may proceed</returns>
    private RemoteCallResult<TResponse> CheckPreconditions<TResponse>(RemoteCallContext context, Stopwatch stopwatch)
        where TResponse : class, IResponseMessage
    {
        // 熔断检查
        if (!_circuitBreaker.IsAllowed(context.ServiceName))
        {
            return RemoteCallResult<TResponse>.Fail(
                RemoteStatusCode.CircuitOpen,
                $"Circuit breaker is open for service: {context.ServiceName}",
                stopwatch.ElapsedMilliseconds,
                context.TraceId);
        }

        // 健康探测检查
        var healthScore = _healthEvaluator.GetHealthScore(context.ServiceName);
        if (healthScore <= 0)
        {
            return RemoteCallResult<TResponse>.Fail(
                RemoteStatusCode.ServiceUnavailable,
                $"Service health score is {healthScore}, considered unavailable: {context.ServiceName}",
                stopwatch.ElapsedMilliseconds,
                context.TraceId);
        }

        return null;
    }

    /// <summary>
    /// 执行一次请求尝试（拦截器、协议检查、信号量临界区、编码写入与响应匹配），并返回是否应重试。
    /// </summary>
    /// <remarks>
    /// Performs a single request attempt (interceptors, protocol check, semaphore critical section, encoding/write, response matching). Returns the result and whether the caller should retry.
    /// </remarks>
    /// <typeparam name="TResponse">响应消息类型 / The response message type</typeparam>
    /// <param name="context">调用上下文 / The call context</param>
    /// <param name="requestMessage">请求消息对象 / The request message object</param>
    /// <param name="stopwatch">计时器 / The elapsed-time stopwatch</param>
    /// <param name="attemptCount">当前尝试序号（从 1 起）/ The current attempt number (1-based)</param>
    /// <returns>本次尝试的结果与是否应重试 / The attempt result and whether the caller should retry</returns>
    private async Task<(RemoteCallResult<TResponse> Result, bool ShouldRetry)> ExecuteAttemptAsync<TResponse>(
        RemoteCallContext context, MessageObject requestMessage, Stopwatch stopwatch, int attemptCount)
        where TResponse : class, IResponseMessage
    {
        Stream stream = null;
        try
        {
            await RunBeforeInterceptorsAsync(context, requestMessage);


            stream = await _transportProtocolAdapter.GetOrCreateStreamAsync(context.ServiceName, context.CancellationToken);
            if (stream == null)
            {
                RecordFailures(context.ServiceName, "Failed to create connection");
                return (
                           RemoteCallResult<TResponse>.Fail(
                               RemoteStatusCode.ConnectionFailed,
                               "Failed to create connection",
                               stopwatch.ElapsedMilliseconds,
                               context.TraceId),
                           false);
            }

            var connection = GetOrCreateConnection(stream);

            // 连接级协议版本握手（每连接单飞一次）：拒绝抛 ProtocolVersionMismatchException，混布超时回落旧版本
            await EnsureHandshakeAsync(connection, context.ServiceName);

            if (_protocolVersionNegotiator != null && !_protocolVersionNegotiator.IsMessageCompatible(requestMessage.GetType(), connection.NegotiatedRemoteVersion))
            {
                return (
                           RemoteCallResult<TResponse>.Fail(
                               RemoteStatusCode.ProtocolVersionIncompatible,
                               $"Protocol version incompatible for message type: {requestMessage.GetType().Name}",
                               stopwatch.ElapsedMilliseconds,
                               context.TraceId),
                           false);
            }
            var requestUniqueId = _requestResponseMatcher.RegisterPendingRequest(context.TimeoutMs);
            PrepareRequestMessage(requestMessage, requestUniqueId);

            using (var requestBuffer = _messageCodec.Encode(requestMessage))
            {
                // 写入门控按连接独立：同一连接上的并发写串行化，不同连接完全并行
                await connection.WriteAsync(requestBuffer.Memory, context.CancellationToken);
            }

            // 响应由该连接的接收泵经请求-响应匹配器投递，等待方不再内联占用连接读取
            return (await WaitForResponseAsync<TResponse>(connection, requestUniqueId, context, requestMessage, stopwatch, attemptCount), false);
        }
        catch (RemoteEndpointNotFoundException endpointNotFoundException)
        {
            stopwatch.Stop();
            RecordFailures(context.ServiceName, endpointNotFoundException.Message);
            await RunExceptionInterceptorsAsync(context, requestMessage, endpointNotFoundException, stopwatch.ElapsedMilliseconds);
            return (
                       RemoteCallResult<TResponse>.Fail(
                           RemoteStatusCode.EndpointNotFound,
                           "Failed to resolve service endpoint",
                           stopwatch.ElapsedMilliseconds,
                           context.TraceId),
                       false);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            var cancellation = ToCancellationOutcome(context);

            RecordFailures(context.ServiceName, cancellation.errorMessage);
            await RunExceptionInterceptorsAsync(context, requestMessage, new TimeoutException(cancellation.errorMessage), stopwatch.ElapsedMilliseconds);

            return (
                       RemoteCallResult<TResponse>.Fail(cancellation.statusCode, cancellation.errorMessage, stopwatch.ElapsedMilliseconds, context.TraceId),
                       ShouldRetry(context, cancellation.statusCode, attemptCount));
        }

        catch (GameFrameX.Network.RemoteMessaging.Versioning.ProtocolVersionMismatchException versionMismatch)
        {
            stopwatch.Stop();

            // 拒绝即断连：失效该连接避免半开复用（服务端回拒绝包后亦已关闭会话）
            if (stream != null)
            {
                _transportProtocolAdapter.Invalidate(stream);
            }

            RecordFailures(context.ServiceName, versionMismatch.Message);
            await RunExceptionInterceptorsAsync(context, requestMessage, versionMismatch, stopwatch.ElapsedMilliseconds);
            return (
                       RemoteCallResult<TResponse>.Fail(
                           RemoteStatusCode.ProtocolVersionIncompatible,
                           versionMismatch.Message,
                           stopwatch.ElapsedMilliseconds,
                           context.TraceId),
                       false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // 连接级异常仅失效当前连接（池内其余连接与其它 endpoint 不受影响）；其余异常保守全量失效
            if (stream != null && IsConnectionException(ex))
            {
                _transportProtocolAdapter.Invalidate(stream);
            }
            else
            {
                _transportProtocolAdapter.Invalidate();
            }


            RecordFailures(context.ServiceName, ex.Message);
            await RunExceptionInterceptorsAsync(context, requestMessage, ex, stopwatch.ElapsedMilliseconds);

            var statusCode = IsConnectionException(ex) ? RemoteStatusCode.ConnectionFailed : RemoteStatusCode.UnknownError;
            return (
                       RemoteCallResult<TResponse>.Fail(statusCode, ex.Message, stopwatch.ElapsedMilliseconds, context.TraceId),
                       ShouldRetry(context, statusCode, attemptCount));

        }
    }

    /// <summary>
    /// 在超时窗口内等待请求-响应匹配器投递的响应（响应由连接接收泵解码并路由）。
    /// </summary>
    /// <remarks>
    /// Waits within the timeout window for the response delivered by the request-response matcher
    /// (responses are decoded and routed by the connection receive pump).
    /// </remarks>
    /// <typeparam name="TResponse">响应消息类型 / The response message type</typeparam>
    /// <param name="connection">连接上下文 / The connection context</param>
    /// <param name="requestUniqueId">请求唯一 ID / The unique ID of the request</param>
    /// <param name="context">调用上下文 / The call context</param>
    /// <param name="requestMessage">请求消息对象 / The request message object</param>
    /// <param name="stopwatch">计时器 / The elapsed-time stopwatch</param>
    /// <param name="attemptCount">当前尝试序号（从 1 起）/ The current attempt number (1-based)</param>
    /// <returns>匹配到的响应结果 / The matched response result</returns>
    private async Task<RemoteCallResult<TResponse>> WaitForResponseAsync<TResponse>(
        ConnectionContext connection, int requestUniqueId, RemoteCallContext context, MessageObject requestMessage, Stopwatch stopwatch, int attemptCount)
        where TResponse : class, IResponseMessage
    {
        connection.TrackPending(requestUniqueId);
        try
        {
            MessageObject responseMessage;
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken))
            {
                timeoutCts.CancelAfter(context.TimeoutMs);
                responseMessage = await _requestResponseMatcher.WaitResponseAsync(requestUniqueId, timeoutCts.Token);
            }

            stopwatch.Stop();
            if (responseMessage == null)
            {
                // 挂起条目丢失（连接死亡竞态）：按连接关闭处理
                RecordFailures(context.ServiceName, "Connection closed during response");
                return RemoteCallResult<TResponse>.Fail(
                    RemoteStatusCode.ConnectionClosed,
                    "Connection closed during response",
                    stopwatch.ElapsedMilliseconds,
                    context.TraceId);
            }

            if (responseMessage is not TResponse typedResponse)
            {
                return RemoteCallResult<TResponse>.Fail(
                    RemoteStatusCode.UnexpectedResponse,
                    "Response type mismatch",
                    stopwatch.ElapsedMilliseconds,
                    context.TraceId);
            }

            RecordSuccess(context.ServiceName);
            await RunAfterInterceptorsAsync(context, requestMessage, responseMessage, stopwatch.ElapsedMilliseconds);
            return RemoteCallResult<TResponse>.Ok(typedResponse, stopwatch.ElapsedMilliseconds, context.TraceId, attemptCount - 1);
        }
        finally
        {
            connection.UntrackPending(requestUniqueId);
        }
    }

    /// <summary>
    /// 获取或创建指定流的连接上下文。首次见到某条连接时启动其专属接收泵。
    /// </summary>
    /// <remarks>
    /// Gets or creates the connection context for the given stream. A dedicated receive pump is started
    /// the first time a connection is seen.
    /// </remarks>
    /// <param name="stream">传输流 / The transport stream</param>
    /// <returns>连接上下文 / The connection context</returns>
    private ConnectionContext GetOrCreateConnection(Stream stream)
    {
        return _connections.GetOrAdd(stream, addedStream =>
        {
            var connection = new ConnectionContext(addedStream, _requestResponseMatcher);
            _ = RunReceivePumpAsync(connection);
            return connection;
        });
    }

    /// <summary>
    /// 确保连接完成协议版本握手（每连接单飞一次）。major 不符抛 <see cref="GameFrameX.Network.RemoteMessaging.Versioning.ProtocolVersionMismatchException"/>；
    /// 握手超时按旧版本节点放行（混布窗口兼容，告警一次）。
    /// </summary>
    /// <remarks>
    /// Ensures the connection has completed the protocol version handshake (single-flight per
    /// connection). A major mismatch throws <see cref="GameFrameX.Network.RemoteMessaging.Versioning.ProtocolVersionMismatchException"/>;
    /// a handshake timeout falls back to the legacy peer version (mixed-window compatibility, one warning).
    /// </remarks>
    /// <param name="connection">连接上下文 / The connection context</param>
    /// <param name="serviceName">目标服务名（用于告警） / The target service name (for warning logs)</param>
    private async Task EnsureHandshakeAsync(ConnectionContext connection, string serviceName)
    {
        await connection.GetOrStartHandshakeAsync(() => PerformHandshakeAsync(connection, serviceName));
    }

    /// <summary>
    /// 执行一次协议版本握手：发送本端版本 → 等待响应 → major 比对。
    /// </summary>
    /// <remarks>
    /// Performs one protocol version handshake: send the local version, await the reply,
    /// and compare majors.
    /// </remarks>
    /// <param name="connection">连接上下文 / The connection context</param>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    private async Task PerformHandshakeAsync(ConnectionContext connection, string serviceName)
    {
        var local = GameFrameX.Network.RemoteMessaging.Versioning.RemoteMessagingProtocolVersion.Current;
        var request = new GameFrameX.Network.RemoteMessaging.Versioning.ReqProtocolVersionNegotiation
        {
            Major = local.Major,
            Minor = local.Minor,
        };

        // C272 节点间鉴权：共享密钥已配置时附带每连接随机 nonce 与 HMAC 凭据；
        // 未配置时保持无凭据字段（与旧版本节点线缆逐字节等价）
        if (GameFrameX.Network.RemoteMessaging.Versioning.InterNodeAuthentication.IsEnabled)
        {
            request.Nonce = GameFrameX.Network.RemoteMessaging.Versioning.InterNodeAuthentication.CreateNonce();
            request.AuthCredential = GameFrameX.Network.RemoteMessaging.Versioning.InterNodeAuthentication.ComputeCredential(request.Nonce, local.Major, local.Minor);
        }

        var requestUniqueId = _requestResponseMatcher.RegisterPendingRequest(_handshakeTimeoutMs);
        PrepareRequestMessage(request, requestUniqueId);
        connection.TrackPending(requestUniqueId);
        try
        {
            using (var requestBuffer = _messageCodec.Encode(request))
            {
                // 握手帧与业务帧共用写入门控与 14 字节编解码帧（传输无关）
                await connection.WriteAsync(requestBuffer.Memory, CancellationToken.None);
            }

            MessageObject response;
            using (var handshakeCts = new CancellationTokenSource(_handshakeTimeoutMs))
            {
                response = await _requestResponseMatcher.WaitResponseAsync(requestUniqueId, handshakeCts.Token);
            }

            if (response is GameFrameX.Network.RemoteMessaging.Versioning.RespProtocolVersionNegotiation handshakeResponse)
            {
                var remote = new GameFrameX.Network.RemoteMessaging.Versioning.ProtocolVersion(handshakeResponse.Major, handshakeResponse.Minor);
                if (!handshakeResponse.Accepted ||
                    (_protocolVersionNegotiator != null && !_protocolVersionNegotiator.IsConnectionCompatible(local, remote)))
                {
                    // Localization: RemoteMessaging.Versioning.HandshakeRejected - 协议版本握手被拒绝：本端 {0}，远端 {1}。连接已关闭（跨 major 混布禁止）。
                    throw new GameFrameX.Network.RemoteMessaging.Versioning.ProtocolVersionMismatchException(
                        LocalizationService.GetString(Localization.Keys.RemoteMessaging.Versioning.HandshakeRejected, local, remote),
                        local,
                        remote);
                }

                connection.SetNegotiatedRemoteVersion(remote);
                return;
            }

            if (response == null)
            {
                // 挂起条目丢失（连接死亡竞态）：按连接关闭处理，由外层既有异常路径失效重试
                throw new IOException(LocalizationService.GetString(Localization.Keys.RemoteMessaging.ClientModule.ConnectionClosed));
            }

            // 对握手唯一 ID 返回了非握手响应：协议异常，按不兼容拒绝（避免误放行）
            throw new GameFrameX.Network.RemoteMessaging.Versioning.ProtocolVersionMismatchException(
                LocalizationService.GetString(Localization.Keys.RemoteMessaging.Versioning.HandshakeRejected, local, new GameFrameX.Network.RemoteMessaging.Versioning.ProtocolVersion(0, 0)),
                local,
                new GameFrameX.Network.RemoteMessaging.Versioning.ProtocolVersion(0, 0));
        }
        catch (OperationCanceledException)
        {
            if (GameFrameX.Network.RemoteMessaging.Versioning.InterNodeAuthentication.IsEnabled)
            {
                // C272：鉴权启用时超时不再回落旧版本——回落是可利用的降级面（丢弃握手响应即可绕过凭据协商），
                // 按连接失败处理，由外层既有异常路径失效重试
                // Localization: RemoteMessaging.Versioning.HandshakeAuthTimeoutFailClosed - 服务 '{0}' 的协议版本握手在 {1}ms 后超时；节点间鉴权已启用，不回落旧版本，按连接失败处理。
                throw new IOException(LocalizationService.GetString(
                    Localization.Keys.RemoteMessaging.Versioning.HandshakeAuthTimeoutFailClosed,
                    serviceName,
                    _handshakeTimeoutMs));
            }

            // 混布窗口：旧节点不识别握手消息 ID（丢帧不回包），超时按旧版本放行并告警（每连接一次）
            // Localization: RemoteMessaging.Versioning.HandshakeLegacyFallback - 服务 '{0}' 的协议版本握手在 {1}ms 后超时；按旧版本节点（{2}）放行。该告警仅滚动升级窗口内属预期，全量升级完成后应消失。
            LogHelper.Warning(LocalizationService.GetString(
                Localization.Keys.RemoteMessaging.Versioning.HandshakeLegacyFallback,
                serviceName,
                _handshakeTimeoutMs,
                GameFrameX.Network.RemoteMessaging.Versioning.RemoteMessagingProtocolVersion.Legacy));
            connection.SetNegotiatedRemoteVersion(GameFrameX.Network.RemoteMessaging.Versioning.RemoteMessagingProtocolVersion.Legacy);
        }
        finally
        {
            connection.UntrackPending(requestUniqueId);
        }
    }

    /// <summary>
    /// 连接接收泵：持续解码该连接上的响应帧并经请求-响应匹配器投递给等待方。
    /// 泵退出（连接关闭或解码异常）时精准失效该连接，并快速失败其上全部挂起请求。
    /// </summary>
    /// <remarks>
    /// Connection receive pump: continuously decodes response frames on the connection and delivers them
    /// to waiters via the request-response matcher. When the pump exits (connection closed or decode failure),
    /// the connection is invalidated precisely and all its pending requests fail fast.
    /// </remarks>
    /// <param name="connection">连接上下文 / The connection context</param>
    private async Task RunReceivePumpAsync(ConnectionContext connection)
    {
        try
        {
            while (true)
            {
                var message = await _messageCodec.DecodeAsync(connection.Stream, CancellationToken.None);
                if (message == null)
                {
                    break;
                }

                _requestResponseMatcher.TryComplete(message.UniqueId, message);
            }
        }
        catch (Exception exception) when (exception is IOException || exception is SocketException || exception is ObjectDisposedException || exception is OperationCanceledException)
        {
            // 连接级失败（对端关闭 / 网络异常 / 关闭竞态 / 取消）：按连接死亡处理（finally 已精准失效并快速失败挂起请求，
            // 受影响调用的指标经既有链路上报：FailAllPending → 等待方异常 → 调用方拦截器 OnException）
        }
        catch (Exception exception) when (exception is InvalidDataException || exception is NotSupportedException)
        {
            // 解码/协议失败（帧损坏、未注册压缩算法）：连接死亡处理 + 连接级可见性（C270，原裸 catch 全吞）
            // Localization: RemoteMessaging.ClientModule.ReceivePumpDecodeFailure - 连接接收泵解码失败（帧损坏或协议异常），连接已按死亡处理并快速失败挂起请求
            LogHelper.Warning(exception, LocalizationService.GetString(Localization.Keys.RemoteMessaging.ClientModule.ReceivePumpDecodeFailure));
        }
        catch (Exception exception)
        {
            // 未分类异常（如 OOM）不吞：记录后向上抛出（C270）——泵任务 fault，可经 UnobservedTaskException/崩溃转储观测
            // Localization: RemoteMessaging.ClientModule.ReceivePumpFatalError - 连接接收泵遭遇未分类异常（不吞掉，记录后向上抛出）
            LogHelper.Error(exception, LocalizationService.GetString(Localization.Keys.RemoteMessaging.ClientModule.ReceivePumpFatalError));
            throw;
        }
        finally
        {
            connection.MarkDead();
            _connections.TryRemove(connection.Stream, out _);
            _transportProtocolAdapter.Invalidate(connection.Stream);
            connection.FailAllPending(new IOException(LocalizationService.GetString(Localization.Keys.RemoteMessaging.ClientModule.ConnectionClosed)));
        }
    }

    /// <summary>
    /// 连接上下文：封装单条连接的写入门控与该连接上的挂起请求追踪。
    /// </summary>
    /// <remarks>
    /// Connection context: encapsulates the write gate of a single connection and tracking of its pending requests.
    /// </remarks>
    private sealed class ConnectionContext
    {
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly object _handshakeLock = new();
        private Task _handshakeTask;
        private readonly IRequestResponseMatcher _matcher;
        private readonly ConcurrentDictionary<int, byte> _pendingUniqueIds = new();
        private volatile bool _dead;

        public ConnectionContext(Stream stream, IRequestResponseMatcher matcher)
        {
            Stream = stream;
            _matcher = matcher;
        }
        public Stream Stream { get; }

        /// <summary>
        /// 获取握手协商出的远端协议版本（握手完成前为 <see cref="GameFrameX.Network.RemoteMessaging.Versioning.RemoteMessagingProtocolVersion.Legacy"/> 占位；
        /// <see cref="RemoteMessageClient"/> 保证消息级检查发生在握手之后）。
        /// </summary>
        /// <remarks>
        /// Gets the remote protocol version negotiated by the handshake (the
        /// <see cref="GameFrameX.Network.RemoteMessaging.Versioning.RemoteMessagingProtocolVersion.Legacy"/>
        /// placeholder before the handshake completes; <see cref="RemoteMessageClient"/> guarantees the
        /// message-level check runs after the handshake).
        /// </remarks>
        public GameFrameX.Network.RemoteMessaging.Versioning.ProtocolVersion NegotiatedRemoteVersion { get; private set; } = GameFrameX.Network.RemoteMessaging.Versioning.RemoteMessagingProtocolVersion.Legacy;

        /// <summary>
        /// 记录握手协商出的远端协议版本（仅握手流程调用）。
        /// </summary>
        /// <remarks>
        /// Records the remote protocol version negotiated by the handshake (called by the handshake flow only).
        /// </remarks>
        public void SetNegotiatedRemoteVersion(GameFrameX.Network.RemoteMessaging.Versioning.ProtocolVersion version)
        {
            NegotiatedRemoteVersion = version;
        }

        /// <summary>
        /// 获取或启动本连接的单飞握手任务：首个调用者执行握手，并发调用者等待同一任务结果。
        /// </summary>
        /// <remarks>
        /// Gets or starts this connection's single-flight handshake task: the first caller performs
        /// the handshake while concurrent callers await the same task's outcome.
        /// </remarks>
        public Task GetOrStartHandshakeAsync(Func<Task> starter)
        {
            lock (_handshakeLock)
            {
                // 单飞：GetOrAdd 工厂竞态与并发首调用都收敛到同一个握手任务，握手帧每连接恰发一次
                _handshakeTask ??= starter();
                return _handshakeTask;
            }
        }

        /// <summary>
        /// 向连接写入一个完整帧。同一连接上的并发写经写入门控串行化，帧间不交错。
        /// </summary>
        /// <remarks>
        /// Writes one complete frame to the connection. Concurrent writes on the same connection are
        /// serialized through the write gate so frames never interleave.
        /// </remarks>
        /// <param name="buffer">已编码帧缓冲 / The encoded frame buffer</param>
        /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
        public async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_dead)
            {
                throw new IOException(LocalizationService.GetString(Localization.Keys.RemoteMessaging.ClientModule.ConnectionClosed));
            }

            await _writeGate.WaitAsync(cancellationToken);
            try
            {
                await Stream.WriteAsync(buffer, cancellationToken);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public void TrackPending(int uniqueId)
        {
            _pendingUniqueIds[uniqueId] = 0;
        }

        public void UntrackPending(int uniqueId)
        {
            _pendingUniqueIds.TryRemove(uniqueId, out _);
        }

        public void MarkDead()
        {
            _dead = true;
        }

        /// <summary>
        /// 以连接关闭异常快速失败该连接上全部挂起请求（等待方立即收到可重试的连接级错误）。
        /// </summary>
        /// <remarks>
        /// Fails all pending requests on this connection fast with a connection-closed exception
        /// (waiters immediately receive a retryable connection-level error).
        /// </remarks>
        /// <param name="exception">失败原因异常 / The failure exception</param>
        public void FailAllPending(Exception exception)
        {
            foreach (var uniqueId in _pendingUniqueIds.Keys)
            {
                if (_pendingUniqueIds.TryRemove(uniqueId, out _))
                {
                    _matcher.TryFail(uniqueId, exception);
                }
            }
        }
    }

    /// <summary>
    /// 将取消异常映射为状态码与错误消息（区分主动取消与超时）。
    /// </summary>
    /// <remarks>
    /// Maps a cancellation exception to a status code and error message (distinguishing explicit cancellation from timeout).
    /// </remarks>
    /// <param name="context">调用上下文 / The call context</param>
    /// <returns>状态码与错误消息 / The status code and error message</returns>
    private static (RemoteStatusCode statusCode, string errorMessage) ToCancellationOutcome(RemoteCallContext context)
    {
        if (context.CancellationToken.IsCancellationRequested)
        {
            return (RemoteStatusCode.Cancelled, "Request was cancelled");
        }

        return (RemoteStatusCode.Timeout, $"Request timed out after {context.TimeoutMs}ms");
    }

    /// <summary>
    /// 单向发送消息，不等待响应。
    /// </summary>
    /// <remarks>
    /// Sends a message one-way without waiting for a response.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    /// <param name="requestMessage">请求消息对象 / The request message object</param>
    /// <param name="timeoutMs">发送超时毫秒数 / The send timeout duration in milliseconds</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    public async Task SendOneWayAsync(
        string serviceName,
        MessageObject requestMessage,
        int timeoutMs = RemoteCallContext.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        if (!_circuitBreaker.IsAllowed(serviceName))
        {
            // Localization: RemoteMessaging.Client.SendOneWayCircuitBreakerOpen - SendOneWayAsync: 熔断器已打开, Service: {0}
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.RemoteMessaging.ClientModule.SendOneWayCircuitBreakerOpen, serviceName));
            return;
        }

        var healthScore = _healthEvaluator.GetHealthScore(serviceName);
        if (healthScore <= 0)
        {
            // Localization: RemoteMessaging.Client.SendOneWayHealthScoreLow - SendOneWayAsync: 服务健康评分过低, Service: {0}, Score: {1}
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.RemoteMessaging.ClientModule.SendOneWayHealthScoreLow, serviceName, healthScore));
            return;
        }

        var context = RemoteCallContext.Create(serviceName, timeoutMs, cancellationToken);

        try
        {
            await RunBeforeInterceptorsAsync(context, requestMessage);


            var stream = await _transportProtocolAdapter.GetOrCreateStreamAsync(serviceName, cancellationToken);
            if (stream == null)
            {
                RecordFailures(serviceName, "SendOneWayAsync: Failed to create connection");
                return;
            }

            var connection = GetOrCreateConnection(stream);

            // 连接级协议版本握手（与 RPC 路径同一单飞门）；拒绝时按单向发送口径记告警并放弃本次发送
            try
            {
                await EnsureHandshakeAsync(connection, serviceName);
            }
            catch (GameFrameX.Network.RemoteMessaging.Versioning.ProtocolVersionMismatchException versionMismatch)
            {
                _transportProtocolAdapter.Invalidate(stream);
                RecordFailures(serviceName, versionMismatch.Message);
                // Localization: RemoteMessaging.Versioning.HandshakeRejected - 协议版本握手被拒绝：本端 {0}，远端 {1}。连接已关闭（跨 major 混布禁止）。
                LogHelper.Warning(LocalizationService.GetString(Localization.Keys.RemoteMessaging.Versioning.HandshakeRejected, versionMismatch.Local, versionMismatch.Remote));
                return;
            }

            if (_protocolVersionNegotiator != null && !_protocolVersionNegotiator.IsMessageCompatible(requestMessage.GetType(), connection.NegotiatedRemoteVersion))
            {
                // Localization: RemoteMessaging.Client.SendOneWayProtocolVersionIncompatible - SendOneWayAsync: 协议版本不兼容, MessageType: {0}
                LogHelper.Warning(LocalizationService.GetString(Localization.Keys.RemoteMessaging.ClientModule.SendOneWayProtocolVersionIncompatible, requestMessage.GetType().Name));
                return;
            }
            var requestUniqueId = _requestResponseMatcher.RegisterPendingRequest(timeoutMs);
            PrepareRequestMessage(requestMessage, requestUniqueId);

            using var requestBuffer = _messageCodec.Encode(requestMessage);
            await connection.WriteAsync(requestBuffer.Memory, cancellationToken);

            RecordSuccess(serviceName);
            await RunAfterInterceptorsAsync(context, requestMessage, null, 0);
        }
        catch (OperationCanceledException)
        {
            RecordFailures(serviceName, cancellationToken.IsCancellationRequested ? "SendOneWayAsync cancelled" : "SendOneWayAsync timed out");
        }
        catch (RemoteEndpointNotFoundException endpointNotFoundException)
        {
            RecordFailures(serviceName, $"SendOneWayAsync endpoint not found: {endpointNotFoundException.Message}");
        }
        catch (Exception ex)
        {
            // 单向发送无等待方与流引用，写失败保守全量失效（与旧行为一致）
            _transportProtocolAdapter.Invalidate();
            RecordFailures(serviceName, $"SendOneWayAsync error: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查目标服务是否可用。
    /// </summary>
    /// <remarks>
    /// Checks whether the target service is available.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    /// <returns>服务是否可用 / Whether the service is available</returns>
    public Task<bool> IsServiceAvailableAsync(string serviceName)
    {
        if (!_circuitBreaker.IsAllowed(serviceName))
        {
            return Task.FromResult(false);
        }

        var healthScore = _healthEvaluator.GetHealthScore(serviceName);
        if (healthScore <= 0)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_transportProtocolAdapter.IsServiceAvailable(serviceName));
    }

    private void RecordSuccess(string serviceName)
    {
        _circuitBreaker.RecordSuccess(serviceName);
        _healthEvaluator.MarkHealthy(serviceName);
    }

    private void RecordFailures(string serviceName, string reason)
    {
        _circuitBreaker.RecordFailure(serviceName);
        _healthEvaluator.MarkUnavailable(serviceName, reason);
    }

    private bool ShouldRetry(RemoteCallContext context, RemoteStatusCode statusCode, int attemptCount)
    {
        return _retryPolicy != null && _retryPolicy.ShouldRetry(context, statusCode, attemptCount);
    }

    private static bool IsConnectionException(Exception ex)
    {
        return ex is IOException || ex is SocketException;
    }

    private async Task RunBeforeInterceptorsAsync(RemoteCallContext context, MessageObject request)
    {
        foreach (var interceptor in _interceptors)
        {
            await interceptor.OnBeforeCallAsync(context, request);
        }
    }

    private async Task RunAfterInterceptorsAsync(RemoteCallContext context, MessageObject request, MessageObject response, long elapsedMs)
    {
        foreach (var interceptor in _interceptors)
        {
            await interceptor.OnAfterCallAsync(context, request, response, elapsedMs);
        }
    }

    private async Task RunExceptionInterceptorsAsync(RemoteCallContext context, MessageObject request, Exception exception, long elapsedMs)
    {
        foreach (var interceptor in _interceptors)
        {
            try
            {
                await interceptor.OnExceptionAsync(context, request, exception, elapsedMs);
            }
            catch (Exception ex)
            {
                // Localization: RemoteMessaging.Client.InterceptorOnExceptionFailed - 拦截器 OnExceptionAsync 失败
                LogHelper.Error(ex, LocalizationService.GetString(Localization.Keys.RemoteMessaging.ClientModule.InterceptorOnExceptionFailed));
            }
        }
    }

    /// <summary>
    /// 准备请求消息的元数据（消息ID、唯一ID）。
    /// </summary>
    /// <remarks>
    /// Prepares request message metadata (message ID, unique ID).
    /// </remarks>
    private static void PrepareRequestMessage(MessageObject message, int uniqueId)
    {
        MessageProtoHelper.SetMessageId(message);
        message.SetUniqueId(uniqueId);
    }
}