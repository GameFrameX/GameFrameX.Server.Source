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
using GameFrameX.Foundation.Logger;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.SuperSocket.Connection;
using GameFrameX.SuperSocket.Primitives;
using GameFrameX.SuperSocket.Server.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Middleware;
using GameFrameX.SuperSocket.Server.Abstractions.Session;

namespace GameFrameX.NetWork;

/// <summary>
/// 会话首消息鉴权中间件——为无握手的传输（如 KCP）提供主动鉴权防线。
/// </summary>
/// <remarks>
/// Session first-message authentication middleware.
/// KCP 等裸协议下服务端收到未知 <c>RemoteEndPoint:Conv</c> 的任意 UDP 包即建连，伪造 conv 的洪泛会制造会话泄漏
/// （仅有 <c>IdleTimeout</c> 空闲清理长窗兜底）。本中间件按 <see cref="SessionAuthenticationOptions"/> 定义的状态机补齐防线：
/// <list type="bullet">
/// <item>会话建立（<see cref="RegisterSession"/>）登记未鉴权状态与鉴权超时 deadline；deadline 自建立起算、不因收发消息重置。</item>
/// <item>未鉴权期间仅放行心跳与白名单消息（<see cref="ShouldAllowPackageAsync"/>）；白名单外消息按协议违规立即关闭会话，不进入业务消息处理。</item>
/// <item>收到 <c>AuthenticatedByMessageIds</c> 内的消息即标记鉴权完成，此后所有消息正常放行；重复白名单消息幂等放行。</item>
/// <item>超时窗口内未完成鉴权的会话由周期扫描（Timer）主动关闭（<see cref="CloseReason.TimeOut"/>）。</item>
/// </list>
/// 与 <c>UseClearIdleSession</c> 空闲清理协同：本超时（默认 30s）是短窗主动防线，空闲清理（默认 60s）是长窗兜底。
/// 中间件只应挂载到需要防线的传输 server builder（如 KCP），不会影响未挂载传输（TCP / WebSocket）的会话。
/// </remarks>
public sealed class SessionAuthenticationMiddleware : MiddlewareBase
{
    /// <summary>
    /// 单个会话的鉴权追踪状态。
    /// </summary>
    /// <remarks>
    /// Per-session authentication tracking state.
    /// <see cref="Authenticated"/> 仅在鉴权完成时单向置 <c>true</c>（false → true），bool 写入原子，
    /// 与超时扫描的竞态最坏后果是会话已被关闭后又标记完成，不影响正确性。
    /// </remarks>
    private sealed class SessionAuthenticationState
    {
        /// <summary>追踪的会话实例 / The tracked session instance.</summary>
        public IAppSession Session { get; }

        /// <summary>鉴权超时截止时间（UTC）/ The authentication deadline in UTC.</summary>
        public DateTimeOffset Deadline { get; }

        /// <summary>是否已完成鉴权 / Whether the session has completed authentication.</summary>
        public volatile bool Authenticated;

        public SessionAuthenticationState(IAppSession session, DateTimeOffset deadline)
        {
            Session = session;
            Deadline = deadline;
        }
    }

    private readonly SessionAuthenticationOptions _options;
    private readonly ConcurrentDictionary<string, SessionAuthenticationState> _states = new ConcurrentDictionary<string, SessionAuthenticationState>();
    private Timer _timer;

    /// <summary>
    /// 初始化会话首消息鉴权中间件。
    /// </summary>
    /// <param name="options">鉴权配置 / The authentication options</param>
    public SessionAuthenticationMiddleware(SessionAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options, nameof(options));
        _options = options;
    }

    /// <summary>
    /// 随服务器启动超时扫描定时器。
    /// </summary>
    /// <param name="server">服务器实例 / The server instance</param>
    public override void Start(IServer server)
    {
        var interval = _options.ScanInterval;
        // ponytail: 固定周期全表扫描，未鉴权会话极大时为 O(n)每周期；当前 MaxClientCount 上限（千级）下足够，升级路径为最小堆/时间轮。
        _timer = new Timer(OnScanCallback, null, interval, interval);
    }

    /// <summary>
    /// 随服务器关闭停止并释放扫描定时器。
    /// </summary>
    /// <param name="server">服务器实例 / The server instance</param>
    public override void Shutdown(IServer server)
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// 登记新建立的会话：置为未鉴权并计算鉴权 deadline。
    /// </summary>
    /// <param name="session">新建立的会话 / The newly established session</param>
    /// <returns>固定返回 <c>true</c>（不在注册期拒绝会话，拒绝语义由超时与协议违规关闭承担）</returns>
    public override ValueTask<bool> RegisterSession(IAppSession session)
    {
        _states[session.SessionId] = new SessionAuthenticationState(session, DateTimeOffset.UtcNow.Add(_options.Timeout));
        return new ValueTask<bool>(true);
    }

    /// <summary>
    /// 注销会话的鉴权追踪状态。
    /// </summary>
    /// <param name="session">已关闭的会话 / The closed session</param>
    /// <returns>固定返回 <c>true</c></returns>
    public override ValueTask<bool> UnRegisterSession(IAppSession session)
    {
        _states.TryRemove(session.SessionId, out _);
        return new ValueTask<bool>(true);
    }

    /// <summary>
    /// 判断消息是否允许进入业务处理（消息处理层统一鉴权拦截入口）。
    /// </summary>
    /// <param name="session">会话对象 / The session</param>
    /// <param name="message">收到的消息 / The received message</param>
    /// <returns><c>true</c> 表示放行进入业务 PackageHandler；<c>false</c> 表示拦截（会话已按协议违规关闭）</returns>
    public ValueTask<bool> ShouldAllowPackageAsync(IAppSession session, IMessage message)
    {
        if (!_states.TryGetValue(session.SessionId, out var state) || state.Authenticated)
        {
            // 无追踪状态（会话已注销的竞态窗口）或已鉴权：放行
            return ValueTask.FromResult(true);
        }

        if (message is not INetworkMessagePackage package)
        {
            // 非 NetworkMessagePackage 的 IMessage 不携带消息头，无法按消息码鉴权，直接放行交由业务处理
            return ValueTask.FromResult(true);
        }

        // 心跳是框架级保活语义，未鉴权期也必须放行（否则误伤合法客户端探活）
        if (package.Header.OperationType == (byte)MessageOperationType.HeartBeat)
        {
            return ValueTask.FromResult(true);
        }

        var messageId = package.Header.MessageId;
        if (_options.AuthenticatedByMessageIds.Contains(messageId))
        {
            state.Authenticated = true;
            return ValueTask.FromResult(true);
        }

        if (_options.AllowedMessageIds.Contains(messageId))
        {
            return ValueTask.FromResult(true);
        }

        LogHelper.Warning("Session authentication rejected and closing: SessionId: {sessionId}, RemoteEndPoint: {remoteEndPoint}, MessageId: {messageId}", session.SessionId, session.RemoteEndPoint, messageId);
        return CloseAndRejectAsync(session);
    }

    /// <summary>
    /// 显式标记会话为已鉴权（供测试与后续业务侧接线使用）。
    /// </summary>
    /// <param name="sessionId">会话唯一标识 / The session identifier</param>
    public void MarkAuthenticated(string sessionId)
    {
        if (_states.TryGetValue(sessionId, out var state))
        {
            state.Authenticated = true;
        }
    }

    /// <summary>
    /// 获取当前仍在鉴权追踪中的会话数（含已鉴权未注销的会话）。
    /// </summary>
    /// <returns>追踪会话数 / The tracked session count</returns>
    public int GetTrackedSessionCount()
    {
        return _states.Count;
    }

    /// <summary>
    /// 按协议违规关闭会话并返回拦截结论。
    /// </summary>
    /// <param name="session">要关闭的会话 / The session to close</param>
    /// <returns>固定返回 <c>false</c>（拦截）</returns>
    private async ValueTask<bool> CloseAndRejectAsync(IAppSession session)
    {
        try
        {
            await session.CloseAsync(CloseReason.ProtocolError);
        }
        catch (Exception exception)
        {
            // 会话可能已在关闭竞态中，关闭失败不影响拦截结论
            LogHelper.Error("Failed to close unauthenticated session: SessionId: {sessionId}, exception: {exception}", session.SessionId, exception.Message);
        }

        return false;
    }

    /// <summary>
    /// 超时扫描回调：派发关闭超时未鉴权会话的异步处理（定时器回调必须为同步 void）。
    /// </summary>
    /// <param name="state">定时器状态（未使用）/ The timer state (unused)</param>
    private void OnScanCallback(object state)
    {
        // 暂停周期避免重入（与 ClearIdleSessionMiddleware 同构）
        _timer.Change(Timeout.Infinite, Timeout.Infinite);

        try
        {
            CloseTimedOutSessionsAsync().DoNotAwait();
        }
        catch (Exception exception)
        {
            LogHelper.Error("Error happened when scanning authentication timeout sessions: {exception}", exception.Message);
            ResumeScanTimer();
        }
    }

    /// <summary>
    /// 关闭全部超时未鉴权的会话，完成后恢复扫描周期。
    /// </summary>
    /// <remarks>
    /// Closes all sessions that have not completed authentication within the timeout window, then resumes the scan timer.
    /// 方法内部捕获全部异常，确保 <see cref="ResumeScanTimer"/> 总被执行，扫描不会停摆。
    /// </remarks>
    private async Task CloseTimedOutSessionsAsync()
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var pair in _states)
            {
                var tracked = pair.Value;
                if (tracked.Authenticated || now < tracked.Deadline)
                {
                    continue;
                }

                LogHelper.Warning("Session authentication timeout, closing: SessionId: {sessionId}, RemoteEndPoint: {remoteEndPoint}", tracked.Session.SessionId, tracked.Session.RemoteEndPoint);
                try
                {
                    await tracked.Session.CloseAsync(CloseReason.TimeOut);
                }
                catch (Exception exception)
                {
                    LogHelper.Error("Failed to close authentication-timeout session: SessionId: {sessionId}, exception: {exception}", tracked.Session.SessionId, exception.Message);
                }
            }
        }
        catch (Exception exception)
        {
            LogHelper.Error("Error happened when closing authentication timeout sessions: {exception}", exception.Message);
        }
        finally
        {
            ResumeScanTimer();
        }
    }

    /// <summary>
    /// 恢复超时扫描定时器周期。
    /// </summary>
    private void ResumeScanTimer()
    {
        var interval = _options.ScanInterval;
        _timer?.Change(interval, interval);
    }
}
