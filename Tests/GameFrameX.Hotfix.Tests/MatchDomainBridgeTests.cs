using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Hotfix.Logic.ServerRole.Match;
using GameFrameX.Proto.Proto;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 匹配域桥薄适配器测试：420 族错误码映射表、
/// 作用域玩家位来源、租约失效显式段、本地校验（模式 / 评分）与入场新面映射。
/// </summary>
public sealed class MatchDomainBridgeTests : IDisposable
{
    /// <summary>测试用租户标识。</summary>
    private const long TenantId = 1;

    /// <summary>测试用应用标识。</summary>
    private const long AppId = 1;

    /// <summary>测试用区服标识。</summary>
    private const long ServerId = 1001;

    /// <summary>玩家一。</summary>
    private const long PlayerOne = 2001;

    public MatchDomainBridgeTests()
    {
        OnlineMatchDomainBridgeHolder.Retract();
    }

    public void Dispose()
    {
        OnlineMatchDomainBridgeHolder.Retract();
    }

    /// <summary>
    /// 构造玩家作用域。
    /// </summary>
    private static OnlineScope PlayerScope(long playerId)
    {
        return new OnlineScope(TenantId, AppId, ServerId, playerId);
    }

    [Fact]
    public async Task Enqueue_NoLease_ReturnsDependencyUnavailable()
    {
        var response = new RespMatchEnqueue();
        await MatchDomainBridge.EnqueueAsync(PlayerOne, new ReqMatchEnqueue { Mode = MatchMode.OneVsOne, Rating = 1000 }, response);

        Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
        Assert.Equal(0, response.MatchId);
    }

    [Fact]
    public async Task Enqueue_LocalValidation_RejectsModeAndRating()
    {
        var face = new StubMatchDomain();
        OnlineMatchDomainBridgeHolder.Publish(face, PlayerScope(0));

        var badMode = new RespMatchEnqueue();
        await MatchDomainBridge.EnqueueAsync(PlayerOne, new ReqMatchEnqueue { Mode = (MatchMode)9, Rating = 1000 }, badMode);
        Assert.Equal((int)MatchErrorCode.ModeInvalid, badMode.ErrorCode);
        Assert.Null(face.LastEnqueueScope);

        var badRating = new RespMatchEnqueue();
        await MatchDomainBridge.EnqueueAsync(PlayerOne, new ReqMatchEnqueue { Mode = MatchMode.OneVsOne, Rating = 0 }, badRating);
        Assert.Equal((int)MatchErrorCode.RatingInvalid, badRating.ErrorCode);
        Assert.Null(face.LastEnqueueScope);
    }

    [Fact]
    public async Task Enqueue_Published_FillsSessionPlayerPositionAndMapsResult()
    {
        var face = new StubMatchDomain { EnqueueMatchId = 77 };
        OnlineMatchDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespMatchEnqueue();
        await MatchDomainBridge.EnqueueAsync(PlayerOne, new ReqMatchEnqueue { Mode = MatchMode.OneVsOne, Rating = 1000 }, response);

        Assert.Equal(0, response.ErrorCode);
        Assert.Equal(77, response.MatchId);
        // 作用域玩家位 = 会话 playerId，lease 值不可覆盖。
        Assert.NotNull(face.LastEnqueueScope);
        Assert.Equal(PlayerOne, face.LastEnqueueScope.PlayerId);
        Assert.Equal(TenantId, face.LastEnqueueScope.TenantId);
        Assert.Equal((int)MatchMode.OneVsOne, face.LastEnqueueMode);
        Assert.Equal(1000, face.LastEnqueueRating);
    }

    [Fact]
    public async Task Enqueue_Duplicate_MapsLegacyAlreadyQueuing()
    {
        var face = new StubMatchDomain { EnqueueFailWith = OnlineErrorCode.DuplicateRequest };
        OnlineMatchDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespMatchEnqueue();
        await MatchDomainBridge.EnqueueAsync(PlayerOne, new ReqMatchEnqueue { Mode = MatchMode.OneVsOne, Rating = 1000 }, response);

        Assert.Equal((int)MatchErrorCode.AlreadyQueuing, response.ErrorCode);
    }

    [Fact]
    public async Task Enqueue_FaceFailure_MapsOnlineBand()
    {
        var face = new StubMatchDomain { EnqueueFailWith = OnlineErrorCode.RateLimitExceeded };
        OnlineMatchDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespMatchEnqueue();
        await MatchDomainBridge.EnqueueAsync(PlayerOne, new ReqMatchEnqueue { Mode = MatchMode.OneVsOne, Rating = 1000 }, response);

        Assert.Equal(-(int)OnlineErrorCode.RateLimitExceeded, response.ErrorCode);
    }

    [Fact]
    public async Task Cancel_Cancelled_MapsSuccess()
    {
        var face = new StubMatchDomain { CancelCancelled = true };
        OnlineMatchDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespMatchCancel();
        await MatchDomainBridge.CancelAsync(PlayerOne, response);

        Assert.Equal(0, response.ErrorCode);
        Assert.Equal(PlayerOne, face.LastCancelScope.PlayerId);
    }

    [Fact]
    public async Task Cancel_TerminalReceipt_MapsLegacyNotQueuing()
    {
        var face = new StubMatchDomain { CancelCancelled = false };
        OnlineMatchDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespMatchCancel();
        await MatchDomainBridge.CancelAsync(PlayerOne, response);

        Assert.Equal((int)MatchErrorCode.NotQueuing, response.ErrorCode);
    }

    [Fact]
    public async Task Cancel_FaceFailure_MapsOnlineBand()
    {
        var face = new StubMatchDomain { CancelFailWith = OnlineErrorCode.DependencyUnavailable };
        OnlineMatchDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespMatchCancel();
        await MatchDomainBridge.CancelAsync(PlayerOne, response);

        Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
    }

    [Fact]
    public async Task Status_Published_MapsQueuingAndMatchId()
    {
        var face = new StubMatchDomain
        {
            StatusQueuing = false,
            StatusMatched = true,
            StatusMatchId = 88,
        };
        OnlineMatchDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespMatchStatus();
        await MatchDomainBridge.StatusAsync(PlayerOne, response);

        Assert.Equal(0, response.ErrorCode);
        Assert.False(response.Queuing);
        Assert.Equal(88, response.MatchId);
        Assert.Equal(PlayerOne, face.LastStatusScope.PlayerId);
    }

    [Fact]
    public async Task Status_NoLease_ReturnsDependencyUnavailable()
    {
        var response = new RespMatchStatus();
        await MatchDomainBridge.StatusAsync(PlayerOne, response);

        Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
    }

    [Fact]
    public async Task Enter_Published_MapsEntryFields()
    {
        var face = new StubMatchDomain
        {
            EntryMatchId = 88,
            EntryStarted = true,
            EntryPlayerIds = new List<long> { 2001, 2002 },
        };
        OnlineMatchDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespMatchEnter();
        await MatchDomainBridge.EnterAsync(PlayerOne, new ReqMatchEnter { MatchId = 88 }, response);

        Assert.Equal(0, response.ErrorCode);
        Assert.Equal(88, response.MatchId);
        Assert.True(response.Started);
        Assert.Equal(88, face.LastEnterMatchId);
        Assert.Equal(PlayerOne, face.LastEnterScope.PlayerId);
    }

    [Fact]
    public async Task Enter_FaceFailure_MapsOnlineBand()
    {
        var face = new StubMatchDomain { EntryFailWith = OnlineErrorCode.ResourceNotFound };
        OnlineMatchDomainBridgeHolder.Publish(face, PlayerScope(0));

        var response = new RespMatchEnter();
        await MatchDomainBridge.EnterAsync(PlayerOne, new ReqMatchEnter { MatchId = 404 }, response);

        Assert.Equal(-(int)OnlineErrorCode.ResourceNotFound, response.ErrorCode);
    }

    /// <summary>
    /// 匹配域桩：记录最近一次调用与可配置结果。
    /// </summary>
    private sealed class StubMatchDomain : IOnlineMatchDomain
    {
        /// <summary>入队回填 MatchId。</summary>
        public long EnqueueMatchId { get; set; }

        /// <summary>入队失败码。</summary>
        public OnlineErrorCode? EnqueueFailWith { get; set; }

        /// <summary>取消回填 Cancelled。</summary>
        public bool CancelCancelled { get; set; }

        /// <summary>取消失败码。</summary>
        public OnlineErrorCode? CancelFailWith { get; set; }

        /// <summary>状态回填字段。</summary>
        public bool StatusQueuing { get; set; }

        /// <summary>状态回填字段。</summary>
        public bool StatusMatched { get; set; }

        /// <summary>状态回填字段。</summary>
        public long StatusMatchId { get; set; }

        /// <summary>入场回填字段。</summary>
        public long EntryMatchId { get; set; }

        /// <summary>入场回填字段。</summary>
        public bool EntryStarted { get; set; }

        /// <summary>入场回填字段。</summary>
        public IReadOnlyList<long> EntryPlayerIds { get; set; } = Array.Empty<long>();

        /// <summary>入场失败码。</summary>
        public OnlineErrorCode? EntryFailWith { get; set; }

        /// <summary>最近一次入队作用域。</summary>
        public OnlineScope LastEnqueueScope { get; private set; }

        /// <summary>最近一次取消作用域。</summary>
        public OnlineScope LastCancelScope { get; private set; }

        /// <summary>最近一次状态作用域。</summary>
        public OnlineScope LastStatusScope { get; private set; }

        /// <summary>最近一次入场作用域。</summary>
        public OnlineScope LastEnterScope { get; private set; }

        /// <summary>最近一次入队模式。</summary>
        public int LastEnqueueMode { get; private set; }

        /// <summary>最近一次入队评分。</summary>
        public int LastEnqueueRating { get; private set; }

        /// <summary>最近一次入场 MatchId。</summary>
        public long LastEnterMatchId { get; private set; }

        public Task<OnlineResult<OnlineMatchDomainEnqueue>> EnqueueAsync(OnlineScope scope, int mode, int rating, CancellationToken cancellationToken = default)
        {
            LastEnqueueScope = scope;
            LastEnqueueMode = mode;
            LastEnqueueRating = rating;
            if (EnqueueFailWith.HasValue)
            {
                return Task.FromResult(OnlineResult<OnlineMatchDomainEnqueue>.Fail(EnqueueFailWith.Value, "stub"));
            }

            return Task.FromResult(OnlineResult<OnlineMatchDomainEnqueue>.Ok(new OnlineMatchDomainEnqueue { MatchId = EnqueueMatchId }));
        }

        public Task<OnlineResult<OnlineMatchDomainCancel>> CancelAsync(OnlineScope scope, CancellationToken cancellationToken = default)
        {
            LastCancelScope = scope;
            if (CancelFailWith.HasValue)
            {
                return Task.FromResult(OnlineResult<OnlineMatchDomainCancel>.Fail(CancelFailWith.Value, "stub"));
            }

            return Task.FromResult(OnlineResult<OnlineMatchDomainCancel>.Ok(new OnlineMatchDomainCancel { Cancelled = CancelCancelled }));
        }

        public Task<OnlineResult<OnlineMatchDomainStatus>> GetStatusAsync(OnlineScope scope, CancellationToken cancellationToken = default)
        {
            LastStatusScope = scope;
            return Task.FromResult(OnlineResult<OnlineMatchDomainStatus>.Ok(new OnlineMatchDomainStatus
            {
                Queuing = StatusQueuing,
                Matched = StatusMatched,
                MatchId = StatusMatchId,
            }));
        }

        public Task<OnlineResult<OnlineMatchDomainEntry>> EnterAsync(OnlineScope scope, long matchId, CancellationToken cancellationToken = default)
        {
            LastEnterScope = scope;
            LastEnterMatchId = matchId;
            if (EntryFailWith.HasValue)
            {
                return Task.FromResult(OnlineResult<OnlineMatchDomainEntry>.Fail(EntryFailWith.Value, "stub"));
            }

            return Task.FromResult(OnlineResult<OnlineMatchDomainEntry>.Ok(new OnlineMatchDomainEntry
            {
                MatchId = EntryMatchId,
                Started = EntryStarted,
                PlayerIds = EntryPlayerIds,
            }));
        }

        public Task<OnlineResult<OnlineMatchDomainAssignment>> ResolveAssignmentAsync(long tenantId, long appId, string assignmentId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OnlineResult<OnlineMatchDomainAssignment>.Fail(OnlineErrorCode.ResourceNotFound, "stub"));
        }

        public Task<OnlineResult<OnlineMatchDomainDisconnect>> NotifyPlayerDisconnectedAsync(OnlineScope scope, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OnlineResult<OnlineMatchDomainDisconnect>.Ok(new OnlineMatchDomainDisconnect { Marked = false }));
        }

        public Task<OnlineResult<OnlineMatchDomainResume>> TryResumeForPlayerAsync(OnlineScope scope, long lastAcknowledgedSequence, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OnlineResult<OnlineMatchDomainResume>.Ok(new OnlineMatchDomainResume { Resumed = false }));
        }
    }
}
