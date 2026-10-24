using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Hotfix.Logic.ServerRole.Match;
using GameFrameX.Proto.Proto;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 匹配域桥薄适配器测试（C246 立项，C294 改为能力桥契约）：420 族错误码映射表（T2 裁定 R4/R8/R15/R16）、
/// 作用域三元组来自授权作用域提供者桥 + 玩家位来自会话、桥缺席 / 撤销显式段、本地校验（模式 / 评分）与入场新面映射。
/// 桥由桩能力模块在真实组合的激活期发布，宿主停机自动撤销（<see cref="HotfixBridgeHarness"/>）。
/// </summary>
[Collection(HotfixBridgeTestCollection.Name)]
public sealed class MatchDomainBridgeTests
{
    /// <summary>玩家一。</summary>
    private const long PlayerOne = 2001;

    /// <summary>
    /// 匹配域桥未发布：入队按依赖不可用显式失败，MatchId 为 0。
    /// </summary>
    [Fact]
    public async Task Enqueue_NoBridge_ReturnsDependencyUnavailable()
    {
        var response = new RespMatchEnqueue();
        await MatchDomainBridge.EnqueueAsync(PlayerOne, new ReqMatchEnqueue { Mode = MatchMode.OneVsOne, Rating = 1000 }, response);

        Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
        Assert.Equal(0, response.MatchId);
    }

    /// <summary>
    /// 本地校验：模式非法 420004、评分非法 420005，均不触达域桥。
    /// </summary>
    [Fact]
    public async Task Enqueue_LocalValidation_RejectsModeAndRating()
    {
        var face = new StubMatchDomain();
        await HotfixBridgeHarness.RunWithMatchDomainAsync(face, async runtime =>
        {
            var badMode = new RespMatchEnqueue();
            await MatchDomainBridge.EnqueueAsync(PlayerOne, new ReqMatchEnqueue { Mode = (MatchMode)9, Rating = 1000 }, badMode);
            Assert.Equal((int)MatchErrorCode.ModeInvalid, badMode.ErrorCode);
            Assert.Null(face.LastEnqueueScope);

            var badRating = new RespMatchEnqueue();
            await MatchDomainBridge.EnqueueAsync(PlayerOne, new ReqMatchEnqueue { Mode = MatchMode.OneVsOne, Rating = 0 }, badRating);
            Assert.Equal((int)MatchErrorCode.RatingInvalid, badRating.ErrorCode);
            Assert.Null(face.LastEnqueueScope);
        });
    }

    /// <summary>
    /// 匹配域桥在场：作用域三元组取授权提供者、玩家位取会话 playerId（R2），入队参数与回执如实映射。
    /// </summary>
    [Fact]
    public async Task Enqueue_Published_FillsSessionPlayerPositionAndMapsResult()
    {
        var face = new StubMatchDomain { EnqueueMatchId = 77 };
        await HotfixBridgeHarness.RunWithMatchDomainAsync(face, async runtime =>
        {
            var response = new RespMatchEnqueue();
            await MatchDomainBridge.EnqueueAsync(PlayerOne, new ReqMatchEnqueue { Mode = MatchMode.OneVsOne, Rating = 1000 }, response);

            Assert.Equal(0, response.ErrorCode);
            Assert.Equal(77, response.MatchId);
            // 作用域玩家位 = 会话 playerId（R2），授权提供者的玩家位不可覆盖。
            Assert.NotNull(face.LastEnqueueScope);
            Assert.Equal(PlayerOne, face.LastEnqueueScope.PlayerId);
            Assert.Equal(HotfixBridgeStubScopeProvider.TenantId, face.LastEnqueueScope.TenantId);
            Assert.Equal(HotfixBridgeStubScopeProvider.AppId, face.LastEnqueueScope.AppId);
            Assert.Equal(HotfixBridgeStubScopeProvider.ServerId, face.LastEnqueueScope.ServerId);
            Assert.Equal((int)MatchMode.OneVsOne, face.LastEnqueueMode);
            Assert.Equal(1000, face.LastEnqueueRating);
        });
    }

    /// <summary>
    /// 重复排队：映射旧 420002（已在排队）。
    /// </summary>
    [Fact]
    public async Task Enqueue_Duplicate_MapsLegacyAlreadyQueuing()
    {
        var face = new StubMatchDomain { EnqueueFailWith = OnlineErrorCode.DuplicateRequest };
        await HotfixBridgeHarness.RunWithMatchDomainAsync(face, async runtime =>
        {
            var response = new RespMatchEnqueue();
            await MatchDomainBridge.EnqueueAsync(PlayerOne, new ReqMatchEnqueue { Mode = MatchMode.OneVsOne, Rating = 1000 }, response);

            Assert.Equal((int)MatchErrorCode.AlreadyQueuing, response.ErrorCode);
        });
    }

    /// <summary>
    /// 入队其余失败（含限流）走 Online 显式段。
    /// </summary>
    [Fact]
    public async Task Enqueue_FaceFailure_MapsOnlineBand()
    {
        var face = new StubMatchDomain { EnqueueFailWith = OnlineErrorCode.RateLimitExceeded };
        await HotfixBridgeHarness.RunWithMatchDomainAsync(face, async runtime =>
        {
            var response = new RespMatchEnqueue();
            await MatchDomainBridge.EnqueueAsync(PlayerOne, new ReqMatchEnqueue { Mode = MatchMode.OneVsOne, Rating = 1000 }, response);

            Assert.Equal(-(int)OnlineErrorCode.RateLimitExceeded, response.ErrorCode);
        });
    }

    /// <summary>
    /// 宿主停机撤桥后再入队：按依赖不可用显式失败（旧「租约撤销」语义 = 宿主 StopAsync）。
    /// </summary>
    [Fact]
    public async Task Enqueue_BridgeRetractedByHostStop_ReturnsDependencyUnavailable()
    {
        var face = new StubMatchDomain { EnqueueMatchId = 77 };
        await HotfixBridgeHarness.RunWithMatchDomainAsync(face, async runtime =>
        {
            await runtime.StopAsync();

            var response = new RespMatchEnqueue();
            await MatchDomainBridge.EnqueueAsync(PlayerOne, new ReqMatchEnqueue { Mode = MatchMode.OneVsOne, Rating = 1000 }, response);

            Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
            Assert.Equal(0, response.MatchId);
            Assert.Null(face.LastEnqueueScope);
        });
    }

    /// <summary>
    /// 匹配域桥在场但授权作用域提供者桥缺席：作用域三元组不可得，按适配器契约
    /// （MatchDomainBridge.RequireLease / OnlineAuthorizedScopeReader 红线 ③）以依赖不可用显式失败，域面不被调用。
    /// </summary>
    [Fact]
    public async Task Enqueue_ScopeProviderAbsent_ReturnsDependencyUnavailableWithoutCallingFace()
    {
        var face = new StubMatchDomain { EnqueueMatchId = 77 };
        await HotfixBridgeHarness.RunWithMatchDomainAsync(face, async runtime =>
        {
            var response = new RespMatchEnqueue();
            await MatchDomainBridge.EnqueueAsync(PlayerOne, new ReqMatchEnqueue { Mode = MatchMode.OneVsOne, Rating = 1000 }, response);

            Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
            Assert.Equal(0, response.MatchId);
            Assert.Equal(0, face.CallCount);
        }, publishScopeProvider: false);
    }

    /// <summary>
    /// 取消成功：成功码 0，作用域玩家位 = 会话 playerId。
    /// </summary>
    [Fact]
    public async Task Cancel_Cancelled_MapsSuccess()
    {
        var face = new StubMatchDomain { CancelCancelled = true };
        await HotfixBridgeHarness.RunWithMatchDomainAsync(face, async runtime =>
        {
            var response = new RespMatchCancel();
            await MatchDomainBridge.CancelAsync(PlayerOne, response);

            Assert.Equal(0, response.ErrorCode);
            Assert.Equal(PlayerOne, face.LastCancelScope.PlayerId);
        });
    }

    /// <summary>
    /// 取消遇终态回执：映射旧 420003（未在排队）。
    /// </summary>
    [Fact]
    public async Task Cancel_TerminalReceipt_MapsLegacyNotQueuing()
    {
        var face = new StubMatchDomain { CancelCancelled = false };
        await HotfixBridgeHarness.RunWithMatchDomainAsync(face, async runtime =>
        {
            var response = new RespMatchCancel();
            await MatchDomainBridge.CancelAsync(PlayerOne, response);

            Assert.Equal((int)MatchErrorCode.NotQueuing, response.ErrorCode);
        });
    }

    /// <summary>
    /// 取消失败走 Online 显式段。
    /// </summary>
    [Fact]
    public async Task Cancel_FaceFailure_MapsOnlineBand()
    {
        var face = new StubMatchDomain { CancelFailWith = OnlineErrorCode.DependencyUnavailable };
        await HotfixBridgeHarness.RunWithMatchDomainAsync(face, async runtime =>
        {
            var response = new RespMatchCancel();
            await MatchDomainBridge.CancelAsync(PlayerOne, response);

            Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
        });
    }

    /// <summary>
    /// 状态查询：排队位与 MatchId 如实映射。
    /// </summary>
    [Fact]
    public async Task Status_Published_MapsQueuingAndMatchId()
    {
        var face = new StubMatchDomain
        {
            StatusQueuing = false,
            StatusMatched = true,
            StatusMatchId = 88,
        };
        await HotfixBridgeHarness.RunWithMatchDomainAsync(face, async runtime =>
        {
            var response = new RespMatchStatus();
            await MatchDomainBridge.StatusAsync(PlayerOne, response);

            Assert.Equal(0, response.ErrorCode);
            Assert.False(response.Queuing);
            Assert.Equal(88, response.MatchId);
            Assert.Equal(PlayerOne, face.LastStatusScope.PlayerId);
        });
    }

    /// <summary>
    /// 匹配域桥未发布：状态查询按依赖不可用显式失败。
    /// </summary>
    [Fact]
    public async Task Status_NoBridge_ReturnsDependencyUnavailable()
    {
        var response = new RespMatchStatus();
        await MatchDomainBridge.StatusAsync(PlayerOne, response);

        Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
    }

    /// <summary>
    /// 入场新面：回执字段与入参如实映射。
    /// </summary>
    [Fact]
    public async Task Enter_Published_MapsEntryFields()
    {
        var face = new StubMatchDomain
        {
            EntryMatchId = 88,
            EntryStarted = true,
            EntryPlayerIds = new List<long> { 2001, 2002 },
        };
        await HotfixBridgeHarness.RunWithMatchDomainAsync(face, async runtime =>
        {
            var response = new RespMatchEnter();
            await MatchDomainBridge.EnterAsync(PlayerOne, new ReqMatchEnter { MatchId = 88 }, response);

            Assert.Equal(0, response.ErrorCode);
            Assert.Equal(88, response.MatchId);
            Assert.True(response.Started);
            Assert.Equal(88, face.LastEnterMatchId);
            Assert.Equal(PlayerOne, face.LastEnterScope.PlayerId);
        });
    }

    /// <summary>
    /// 入场失败一律走 Online 显式段。
    /// </summary>
    [Fact]
    public async Task Enter_FaceFailure_MapsOnlineBand()
    {
        var face = new StubMatchDomain { EntryFailWith = OnlineErrorCode.ResourceNotFound };
        await HotfixBridgeHarness.RunWithMatchDomainAsync(face, async runtime =>
        {
            var response = new RespMatchEnter();
            await MatchDomainBridge.EnterAsync(PlayerOne, new ReqMatchEnter { MatchId = 404 }, response);

            Assert.Equal(-(int)OnlineErrorCode.ResourceNotFound, response.ErrorCode);
        });
    }

    /// <summary>
    /// 匹配域桩：记录最近一次调用、调用次数与可配置结果。
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

        /// <summary>域面被调用的总次数。</summary>
        public int CallCount { get; private set; }

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
            CallCount++;
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
            CallCount++;
            LastCancelScope = scope;
            if (CancelFailWith.HasValue)
            {
                return Task.FromResult(OnlineResult<OnlineMatchDomainCancel>.Fail(CancelFailWith.Value, "stub"));
            }

            return Task.FromResult(OnlineResult<OnlineMatchDomainCancel>.Ok(new OnlineMatchDomainCancel { Cancelled = CancelCancelled }));
        }

        public Task<OnlineResult<OnlineMatchDomainStatus>> GetStatusAsync(OnlineScope scope, CancellationToken cancellationToken = default)
        {
            CallCount++;
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
            CallCount++;
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
            CallCount++;
            return Task.FromResult(OnlineResult<OnlineMatchDomainAssignment>.Fail(OnlineErrorCode.ResourceNotFound, "stub"));
        }

        public Task<OnlineResult<OnlineMatchDomainDisconnect>> NotifyPlayerDisconnectedAsync(OnlineScope scope, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(OnlineResult<OnlineMatchDomainDisconnect>.Ok(new OnlineMatchDomainDisconnect { Marked = false }));
        }

        public Task<OnlineResult<OnlineMatchDomainResume>> TryResumeForPlayerAsync(OnlineScope scope, long lastAcknowledgedSequence, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(OnlineResult<OnlineMatchDomainResume>.Ok(new OnlineMatchDomainResume { Resumed = false }));
        }
    }
}
