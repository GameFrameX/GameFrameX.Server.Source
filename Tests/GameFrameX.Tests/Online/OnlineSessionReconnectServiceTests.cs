using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Foundation.Session;
using GameFrameX.Capability.Online.Abstractions.Tokens;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 会话恢复服务测试：登录签发（LatestWins 顶替）、新连接恢复（验证 + 原子轮换 + 连接替换）、
    /// 重放 / 过期 / 错主体拒绝、并发恢复互斥、恢复失败不产生半恢复态与断线开窗。
    /// </summary>
    public class OnlineSessionReconnectServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>测试用玩家标识。</summary>
        private const long PlayerId = 1001;

        /// <summary>另一玩家标识（错主体用例）。</summary>
        private const long OtherPlayerId = 1002;

        [Fact]
        public async Task IssueForLogin_ShouldReturnCredentialAndKickOldActiveSession()
        {
            var harness = new Harness();
            var first = await harness.IssueLoginAsync("conn-1");
            Assert.NotEmpty(first.Token);

            var second = await harness.IssueLoginAsync("conn-2");
            Assert.NotEmpty(second.Token);
            Assert.NotEqual(first.Token, second.Token);

            // LatestWins：旧会话被顶替（Kicked + ReplacedByNewSession），旧 Token 失效。
            var oldValidation = await harness.Tokens.ValidateTokenAsync(first.Token);
            Assert.False(oldValidation.IsSuccess);
            Assert.Equal(OnlineErrorCode.TokenRevoked, oldValidation.Code);

            var kicked = await harness.Store.FindAsync(first.SessionId);
            Assert.Equal(OnlineSessionState.Kicked, kicked.State);
            Assert.Equal(OnlineSessionCloseReason.ReplacedByNewSession, kicked.CloseReason);

            // 新会话随签发直接落 Connected 并绑定当前连接。
            var connected = await harness.Store.FindAsync(second.SessionId);
            Assert.Equal(OnlineSessionState.Connected, connected.State);
            Assert.Equal("conn-2", connected.ConnectionId);
        }

        [Fact]
        public async Task Resume_WithinWindow_ShouldRotateTokenAndReplaceConnection()
        {
            var harness = new Harness();
            var issued = await harness.IssueLoginAsync("conn-1");
            var marked = await harness.Service.NotifyPlayerDisconnectedAsync(harness.PlayerScope(), 30);
            Assert.True(marked.IsSuccess);
            var duringWindow = await harness.Store.FindAsync(issued.SessionId);
            Assert.Equal(OnlineSessionState.Reconnecting, duringWindow.State);

            var resumed = await harness.Service.ResumeAsync(harness.ScopeWithPlayer(0), issued.Token, "conn-2");
            Assert.True(resumed.IsSuccess, resumed.Message);
            Assert.Equal(PlayerId, resumed.Data.PlayerId);
            Assert.NotEqual(issued.Token, resumed.Data.NewToken);

            var after = await harness.Store.FindAsync(issued.SessionId);
            Assert.Equal(OnlineSessionState.Connected, after.State);
            Assert.Equal("conn-2", after.ConnectionId);
            Assert.Equal(0, after.ReconnectDeadlineAtTime);

            // 旧凭据即刻失效（原子消费）：重放判 TokenRevoked。
            var replay = await harness.Service.ResumeAsync(harness.ScopeWithPlayer(0), issued.Token, "conn-3");
            Assert.False(replay.IsSuccess);
            Assert.Equal(OnlineErrorCode.TokenRevoked, replay.Code);
        }

        [Fact]
        public async Task Resume_ExpiredToken_ShouldBeRejected()
        {
            var harness = new Harness(tokenTimeToLiveSeconds: 1);
            var issued = await harness.IssueLoginAsync("conn-1");

            // 有效期 1 秒，基准时刻推到 2 秒后（经存储内会话的到期时刻直接改写模拟）。
            var session = await harness.Store.FindAsync(issued.SessionId);
            session.TokenExpiresAtTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 2000;
            await harness.Store.AddOrUpdateAsync(session);

            var resumed = await harness.Service.ResumeAsync(harness.ScopeWithPlayer(0), issued.Token, "conn-2");
            Assert.False(resumed.IsSuccess);
            Assert.Equal(OnlineErrorCode.TokenExpired, resumed.Code);
        }

        [Fact]
        public async Task Resume_WrongPlayerScope_ShouldBeRejected()
        {
            var harness = new Harness();
            var issued = await harness.IssueLoginAsync("conn-1");

            // 已绑定玩家位的连接拿别人的凭据恢复 → 错主体拒绝。
            var resumed = await harness.Service.ResumeAsync(harness.ScopeWithPlayer(OtherPlayerId), issued.Token, "conn-2");
            Assert.False(resumed.IsSuccess);
            Assert.Equal(OnlineErrorCode.ScopeDenied, resumed.Code);
        }

        [Fact]
        public async Task Resume_TerminalSession_ShouldBeRejected()
        {
            var harness = new Harness();
            var issued = await harness.IssueLoginAsync("conn-1");
            var kicked = await harness.Tokens.KickAsync(new OnlineTokenKickRequest { SessionId = issued.SessionId, Scope = harness.PlayerScope(), Reason = "test" });
            Assert.True(kicked.Kicked);

            var resumed = await harness.Service.ResumeAsync(harness.ScopeWithPlayer(0), issued.Token, "conn-2");
            Assert.False(resumed.IsSuccess);
            // 顶号 / 踢线清空指纹：凭据按指纹不可寻址 → TokenRevoked（2xxx 段显式拒绝）。
            Assert.Equal(OnlineErrorCode.TokenRevoked, resumed.Code);
        }

        [Fact]
        public async Task Resume_ConcurrentlyWithSameToken_ShouldAdmitExactlyOne()
        {
            var harness = new Harness();
            var issued = await harness.IssueLoginAsync("conn-1");

            var first = await harness.Service.ResumeAsync(harness.ScopeWithPlayer(0), issued.Token, "conn-a");
            var second = await harness.Service.ResumeAsync(harness.ScopeWithPlayer(0), issued.Token, "conn-b");

            // 并发恢复显式分出先后：恰好一个成功（旧指纹已轮换），另一个 TokenRevoked，不产生双活凭据。
            var successes = new[] { first, second }.Count(result => result.IsSuccess);
            Assert.Equal(1, successes);
            var failure = first.IsSuccess ? second : first;
            Assert.Equal(OnlineErrorCode.TokenRevoked, failure.Code);
        }

        [Fact]
        public async Task Resume_RotationFailure_ShouldNotAdvanceSessionState()
        {
            var harness = new Harness();
            var issued = await harness.IssueLoginAsync("conn-1");
            var marked = await harness.Service.NotifyPlayerDisconnectedAsync(harness.PlayerScope(), 30);
            Assert.True(marked.IsSuccess);
            var before = await harness.Store.FindAsync(issued.SessionId);

            // 故障注入：恢复流程内第 2 次按指纹查找（= 轮换内部再验证）返回 null，模拟指纹被并发轮换 / 吊销；
            // 令牌 / 会话 / 恢复三服务统一构建在包装存储之上，注入才能命中轮换与连接替换两条内部路径。
            var injected = new FaultInjectingOnlineSessionStore(harness.Store) { SkipTokenHashLookupNumber = 2 };
            var armedHarness = new Harness(sessionStore: injected);
            var resumed = await armedHarness.Service.ResumeAsync(harness.ScopeWithPlayer(0), issued.Token, "conn-2");
            Assert.False(resumed.IsSuccess);
            Assert.Equal(OnlineErrorCode.TokenRevoked, resumed.Code);

            // 半恢复态禁止：状态 / 连接绑定 / 重连窗与恢复前完全一致（GFX-1076 M54）。
            var after = await harness.Store.FindAsync(issued.SessionId);
            Assert.Equal(OnlineSessionState.Reconnecting, after.State);
            Assert.Equal(before.ConnectionId, after.ConnectionId);
            Assert.Equal(before.ReconnectDeadlineAtTime, after.ReconnectDeadlineAtTime);
        }

        [Fact]
        public async Task Resume_ConnectionReplaceFailureAfterRotation_ShouldFailClosedWithoutHalfResume()
        {
            var harness = new Harness();
            var issued = await harness.IssueLoginAsync("conn-1");
            var marked = await harness.Service.NotifyPlayerDisconnectedAsync(harness.PlayerScope(), 30);
            Assert.True(marked.IsSuccess);

            // 故障注入：按会话标识查找一律 miss（= 连接替换步的会话定位），模拟推进时会话已不可寻址；
            // 令牌 / 会话 / 恢复三服务统一构建在包装存储之上，注入才能命中轮换与连接替换两条内部路径。
            var injected = new FaultInjectingOnlineSessionStore(harness.Store) { SkipAllFindById = true };
            var armedHarness = new Harness(sessionStore: injected);
            var resumed = await armedHarness.Service.ResumeAsync(harness.ScopeWithPlayer(0), issued.Token, "conn-2");
            Assert.False(resumed.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, resumed.Code);

            // fail-closed：会话仍 Reconnecting（未被推进为 Connected），无连接错绑；
            // 旧 Token 已随轮换一次性消费，客户端重试判 TokenRevoked 须重新登录。
            var after = await harness.Store.FindAsync(issued.SessionId);
            Assert.Equal(OnlineSessionState.Reconnecting, after.State);

            var replay = await harness.Tokens.ValidateTokenAsync(issued.Token);
            Assert.False(replay.IsSuccess);
            Assert.Equal(OnlineErrorCode.TokenRevoked, replay.Code);
        }

        [Fact]
        public async Task NotifyPlayerDisconnected_NoActiveSession_ShouldSucceedSilently()
        {
            var harness = new Harness();
            var notified = await harness.Service.NotifyPlayerDisconnectedAsync(harness.PlayerScope(), 30);
            Assert.True(notified.IsSuccess);
        }

        /// <summary>
        /// 测试基座：内存存储 + 令牌 / 会话 / 恢复三服务。
        /// </summary>
        private sealed class Harness
        {
            public Harness(int tokenTimeToLiveSeconds = OnlineSessionReconnectService.DefaultTokenTimeToLiveSeconds, IOnlineSessionStore sessionStore = null)
            {
                Recorder = new OnlineEventRecorder();
                Store = new InMemoryOnlineSessionStore();
                var effectiveStore = sessionStore ?? Store;
                Tokens = new OnlineSessionTokenService(effectiveStore, Recorder);
                Sessions = new OnlineSessionManager(effectiveStore, Recorder);
                Service = new OnlineSessionReconnectService(Tokens, Sessions, effectiveStore, tokenTimeToLiveSeconds);
            }

            public OnlineEventRecorder Recorder { get; }

            public InMemoryOnlineSessionStore Store { get; }

            public OnlineSessionTokenService Tokens { get; }

            public OnlineSessionManager Sessions { get; }

            public OnlineSessionReconnectService Service { get; }

            public async Task<OnlineSessionLoginCredential> IssueLoginAsync(string connectionId)
            {
                var issued = await Service.IssueForLoginAsync(PlayerScope(), connectionId);
                Assert.True(issued.IsSuccess, issued.Message);
                return issued.Data;
            }

            public OnlineScope PlayerScope()
            {
                return new OnlineScope(TenantId, AppId, ServerId, PlayerId);
            }

            public OnlineScope ScopeWithPlayer(long playerId)
            {
                return new OnlineScope(TenantId, AppId, ServerId, playerId);
            }
        }

        /// <summary>
        /// 故障注入存储包装：在委托内存存储之上按调用序号制造「指纹不可寻址 / 会话不可寻址」故障，
        /// 供恢复路径失败语义做确定性回归（只拦截读，不改动共享实体，底层事实保持原样）。
        /// </summary>
        private sealed class FaultInjectingOnlineSessionStore : IOnlineSessionStore
        {
            private readonly IOnlineSessionStore _innerStore;
            private int _tokenHashLookupCount;

            public FaultInjectingOnlineSessionStore(IOnlineSessionStore innerStore)
            {
                _innerStore = innerStore;
            }

            /// <summary>第 N 次（1 起）按指纹查找返回 null（0 = 不注入）；模拟指纹被并发轮换 / 吊销。</summary>
            public int SkipTokenHashLookupNumber { get; set; }

            /// <summary>所有按会话标识查找返回 null；模拟会话不可寻址（连接替换步失败）。</summary>
            public bool SkipAllFindById { get; set; }

            public Task<OnlineSession> FindAsync(string sessionId, CancellationToken cancellationToken = default)
            {
                return SkipAllFindById ? Task.FromResult<OnlineSession>(null) : _innerStore.FindAsync(sessionId, cancellationToken);
            }

            public Task<OnlineSession> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
            {
                var lookupNumber = Interlocked.Increment(ref _tokenHashLookupCount);
                if (lookupNumber == SkipTokenHashLookupNumber)
                {
                    return Task.FromResult<OnlineSession>(null);
                }

                return _innerStore.FindByTokenHashAsync(tokenHash, cancellationToken);
            }

            public Task AddOrUpdateAsync(OnlineSession session, CancellationToken cancellationToken = default)
            {
                return _innerStore.AddOrUpdateAsync(session, cancellationToken);
            }

            public Task<IReadOnlyList<OnlineSession>> ListActiveByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return _innerStore.ListActiveByPlayerAsync(tenantId, appId, playerId, cancellationToken);
            }

            public Task<IReadOnlyList<OnlineSession>> ListNonTerminalAsync(CancellationToken cancellationToken = default)
            {
                return _innerStore.ListNonTerminalAsync(cancellationToken);
            }

            public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
            {
                return _innerStore.RemoveAsync(sessionId, cancellationToken);
            }
        }
    }
}
