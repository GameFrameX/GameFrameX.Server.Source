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


using System.Buffers;
using System.Net;
using GameFrameX.NetWork;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.Messages;
using GameFrameX.SuperSocket.Connection;
using GameFrameX.SuperSocket.Primitives;
using GameFrameX.SuperSocket.ProtoBase;
using GameFrameX.SuperSocket.Server.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using Xunit;

namespace GameFrameX.Tests.NetWork;

/// <summary>
/// 会话首消息鉴权中间件单元测试 / Session first-message authentication middleware unit tests
/// </summary>
/// <remarks>
/// 覆盖 GFX-822（KCP 接入生产前补齐首消息鉴权防线）状态机语义：
/// 未鉴权白名单放行 / 白名单外按协议违规关闭并拦截 / 心跳放行 / 鉴权完成消息升级 / 显式标记 / 超时主动关闭 / 已鉴权不受超时影响。
/// </remarks>
public class SessionAuthenticationMiddlewareTests : IDisposable
{
    private readonly SessionAuthenticationMiddleware _middleware;
    private readonly SessionAuthenticationOptions _options;

    public SessionAuthenticationMiddlewareTests()
    {
        _options = new SessionAuthenticationOptions
        {
            Timeout = TimeSpan.FromMilliseconds(150),
            ScanInterval = TimeSpan.FromMilliseconds(50),
        };
        _options.AllowedMessageIds.Add(3000);
        _options.AuthenticatedByMessageIds.Add(3001);
        _middleware = new SessionAuthenticationMiddleware(_options);
    }

    public void Dispose()
    {
        _middleware.Shutdown(null);
    }

    /// <summary>
    /// 未鉴权会话收到白名单消息应放行进入业务处理。
    /// </summary>
    [Fact]
    public async Task ShouldAllowPackageAsync_UnauthenticatedWhitelistedMessage_ShouldAllow()
    {
        var session = new FakeAppSession("session-1");
        await _middleware.RegisterSession(session);

        var allowed = await _middleware.ShouldAllowPackageAsync(session, CreatePackage(3000));

        Assert.True(allowed);
        Assert.Equal(0, session.CloseCallCount);
    }

    /// <summary>
    /// 未鉴权会话收到白名单外消息应拦截，并按协议违规关闭会话（不进入业务消息处理）。
    /// </summary>
    [Fact]
    public async Task ShouldAllowPackageAsync_UnauthenticatedNonWhitelistedMessage_ShouldRejectAndClose()
    {
        var session = new FakeAppSession("session-2");
        await _middleware.RegisterSession(session);

        var allowed = await _middleware.ShouldAllowPackageAsync(session, CreatePackage(99999));

        Assert.False(allowed);
        Assert.Equal(1, session.CloseCallCount);
        Assert.Equal(CloseReason.ProtocolError, session.LastCloseReason);
    }

    /// <summary>
    /// 未鉴权会话的心跳消息应放行（框架级保活语义，不因未鉴权被拦截）。
    /// </summary>
    [Fact]
    public async Task ShouldAllowPackageAsync_UnauthenticatedHeartBeat_ShouldAllow()
    {
        var session = new FakeAppSession("session-3");
        await _middleware.RegisterSession(session);

        var heartBeat = CreatePackage(99999);
        heartBeat.Header.OperationType = (byte)MessageOperationType.HeartBeat;

        var allowed = await _middleware.ShouldAllowPackageAsync(session, heartBeat);

        Assert.True(allowed);
        Assert.Equal(0, session.CloseCallCount);
    }

    /// <summary>
    /// 收到鉴权完成消息后，会话应标记为已鉴权，此后任意消息（含白名单外）正常放行。
    /// </summary>
    [Fact]
    public async Task ShouldAllowPackageAsync_AfterAuthenticatedByMessage_AnyMessageShouldAllow()
    {
        var session = new FakeAppSession("session-4");
        await _middleware.RegisterSession(session);

        var firstAllowed = await _middleware.ShouldAllowPackageAsync(session, CreatePackage(3001));
        var businessAllowed = await _middleware.ShouldAllowPackageAsync(session, CreatePackage(88888));
        var repeatedWhitelistAllowed = await _middleware.ShouldAllowPackageAsync(session, CreatePackage(3000));

        Assert.True(firstAllowed);
        Assert.True(businessAllowed);
        Assert.True(repeatedWhitelistAllowed);
        Assert.Equal(0, session.CloseCallCount);
    }

    /// <summary>
    /// 显式 MarkAuthenticated 与收到鉴权完成消息等效：标记后任意消息放行，且重复标记幂等。
    /// </summary>
    [Fact]
    public async Task MarkAuthenticated_ThenAnyMessageShouldAllow()
    {
        var session = new FakeAppSession("session-5");
        await _middleware.RegisterSession(session);

        _middleware.MarkAuthenticated("session-5");
        _middleware.MarkAuthenticated("session-5");

        var allowed = await _middleware.ShouldAllowPackageAsync(session, CreatePackage(77777));

        Assert.True(allowed);
        Assert.Equal(0, session.CloseCallCount);
    }

    /// <summary>
    /// 超时窗口内未完成鉴权的会话应被周期扫描主动关闭（CloseReason.TimeOut），追踪状态注销。
    /// </summary>
    [Fact]
    public async Task Scan_AfterTimeout_ShouldCloseUnauthenticatedSession()
    {
        var session = new FakeAppSession("session-6");
        await _middleware.RegisterSession(session);
        _middleware.Start(null);

        var closed = await WaitUntilAsync(() => session.CloseCallCount > 0, TimeSpan.FromSeconds(5));
        Assert.True(closed);
        Assert.Equal(CloseReason.TimeOut, session.LastCloseReason);

        // 会话关闭后追踪状态应被注销（模拟 SuperSocket 管线回调 UnRegisterSession）
        await _middleware.UnRegisterSession(session);
        Assert.Equal(0, _middleware.GetTrackedSessionCount());
    }

    /// <summary>
    /// 已完成鉴权的会话即使超过超时窗口也不应被扫描关闭（超时只针对未鉴权会话）。
    /// </summary>
    [Fact]
    public async Task Scan_AfterTimeout_ShouldNotCloseAuthenticatedSession()
    {
        var session = new FakeAppSession("session-7");
        await _middleware.RegisterSession(session);
        _middleware.MarkAuthenticated("session-7");
        _middleware.Start(null);

        // 等待超过 Timeout（150ms）加若干扫描周期
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(0, session.CloseCallCount);
    }

    /// <summary>
    /// 构造指定消息码的测试消息包。
    /// </summary>
    private static FakeNetworkMessagePackage CreatePackage(int messageId)
    {
        var package = new FakeNetworkMessagePackage();
        package.SetHeader(messageId);
        return package;
    }

    /// <summary>
    /// 轮询等待条件成立或超时。
    /// </summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        return condition();
    }

    /// <summary>
    /// IAppSession 测试替身：仅实现 SessionId 与 CloseAsync 记录，其余成员不被鉴权中间件使用。
    /// </summary>
    private sealed class FakeAppSession : IAppSession
    {
        private int _closeCallCount;
        private CloseReason _lastCloseReason;

        public FakeAppSession(string sessionId)
        {
            SessionId = sessionId;
        }

        public string SessionId { get; }

        public bool IsConnected => CloseCallCount == 0;

        public int CloseCallCount => _closeCallCount;

        public CloseReason LastCloseReason => _lastCloseReason;

        public event AsyncEventHandler Connected
        {
            add { }
            remove { }
        }

        public event AsyncEventHandler<CloseEventArgs> Closed
        {
            add { }
            remove { }
        }

        public ValueTask CloseAsync(CloseReason reason)
        {
            _lastCloseReason = reason;
            Interlocked.Increment(ref _closeCallCount);
            return ValueTask.CompletedTask;
        }

        public object DataContext { get; set; }

        public DateTimeOffset StartTime => DateTimeOffset.UtcNow;

        public DateTimeOffset LastActiveTime => DateTimeOffset.UtcNow;

        public IConnection Connection => null;

        public EndPoint RemoteEndPoint => null;

        public EndPoint LocalEndPoint => null;

        public IServerInfo Server => null;

        public SessionState State => SessionState.Connected;

        public void Reset()
        {
        }

        public object this[object name]
        {
            get => null;
            set { }
        }

        public void Initialize(IServerInfo server, IConnection connection)
        {
        }

        public ValueTask SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync(ReadOnlySequence<byte> data, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync<TPackage>(IPackageEncoder<TPackage> packageEncoder, TPackage package, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// INetworkMessagePackage 测试替身：携带可设置的 MessageObjectHeader。
    /// </summary>
    private sealed class FakeNetworkMessagePackage : INetworkMessagePackage
    {
        private readonly MessageObjectHeader _header = new MessageObjectHeader();

        public void SetHeader(int messageId)
        {
            _header.MessageId = messageId;
        }

        public Type MessageType => typeof(object);

        public byte[] MessageData => Array.Empty<byte>();

        public MessageObjectHeader Header => _header;

        INetworkMessageHeader INetworkMessagePackage.Header => _header;

        public INetworkMessage DeserializeMessageObject()
        {
            return null;
        }

        public void SetMessageData(byte[] messageData)
        {
        }

        public void Clear()
        {
        }

        public string ToFormatMessageString(long actorId = default)
        {
            return string.Empty;
        }
    }
}
