// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of PRC and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers shall bear the responsibility themselves.
// ==========================================================================================

using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Foundation.Idempotency;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 对局结算管线测试：Tick 驱动结算 + 发奖、发奖失败待重试、可空分发器、并发串行安全。
    /// </summary>
    public class OnlineMatchSettlementPipelineTests
    {
        /// <summary>租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>App 标识。</summary>
        private const long AppId = 10;

        /// <summary>区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>玩家一（胜方）。</summary>
        private const long PlayerOne = 1001;

        /// <summary>玩家二（负方）。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>基准时刻（UTC 毫秒）。</summary>
        private const long Now = 1000000L;

        /// <summary>幂等记录默认保留时长（24 小时，对齐生产装配）。</summary>
        private const long DefaultIdempotencyRetentionMilliseconds = 86_400_000L;

        /// <summary>对局标识。</summary>
        private const string MatchId = "match-1";

        /// <summary>
        /// 验证完局后 Tick 完成结算并发奖（样本奖励 100 / 10）。
        /// </summary>
        [Fact]
        public async Task TickAsync_AfterSettling_ShouldSettleAndGrant()
        {
            var harness = new Harness();
            await harness.PlayToSettlingAsync();

            await harness.Pipeline.TickAsync(Now + 100);

            Assert.Equal(OnlineMatchState.Completed, harness.Actor.State);
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(PlayerTwo));
            var (_, retries) = harness.Pipeline.QueryStatus();
            Assert.Empty(retries);
        }

        /// <summary>
        /// 验证重复 Tick 不重复发奖（结算幂等回放路径）。
        /// </summary>
        [Fact]
        public async Task TickAsync_Twice_ShouldNotDoubleGrant()
        {
            var harness = new Harness();
            await harness.PlayToSettlingAsync();
            await harness.Pipeline.TickAsync(Now + 100);

            await harness.Pipeline.TickAsync(Now + 200);

            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(PlayerTwo));
        }

        /// <summary>
        /// 验证发奖失败进入待重试表，修复后下一 Tick 重投成功且不双发。
        /// </summary>
        [Fact]
        public async Task TickAsync_DispatchFailure_ShouldRetryUntilFixed()
        {
            var harness = new Harness(withFailingStore: true);
            await harness.PlayToSettlingAsync();

            await harness.Pipeline.TickAsync(Now + 100);

            // 结算已成立，但发奖全部失败：待重试表可见。
            Assert.Equal(OnlineMatchState.Completed, harness.Actor.State);
            Assert.Equal(0L, await harness.BalanceOfAsync(PlayerOne));
            var (settling, retries) = harness.Pipeline.QueryStatus();
            Assert.Empty(settling);
            Assert.Single(retries);
            Assert.Equal(2, retries[0].FailedPlayerIds.Count);
            // 首发 + 同 Tick 重投各计一次（重投仍失败，留待下一 Tick）。
            Assert.Equal(2, retries[0].Attempts);

            // 修复资产存储后重投：成功且不双发。
            harness.AssetStore.FailureEnabled = false;
            await harness.Pipeline.TickAsync(Now + 200);

            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(PlayerTwo));
            var (_, retriesAfterFix) = harness.Pipeline.QueryStatus();
            Assert.Empty(retriesAfterFix);

            await harness.Pipeline.TickAsync(Now + 300);
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(PlayerTwo));
        }

        /// <summary>
        /// 验证部分失败时只重投失败玩家（M79 回归）：已成功玩家的幂等记录过期后也不被再次发奖，
        /// 失败玩家集合不收窄波及成功玩家，且尝试达上限（默认 5 次，含首发）转人工复核。
        /// </summary>
        [Fact]
        public async Task TickAsync_PartialFailure_ShouldRedispatchFailedPlayersOnly_NoDoubleGrantAfterIdempotencyExpiry()
        {
            var harness = new Harness(idempotencyRetentionMilliseconds: 100);
            harness.AssetStore.FailingPlayerIds.Add(PlayerTwo);
            await harness.PlayToSettlingAsync();

            await harness.Pipeline.TickAsync(Now + 100);

            // 玩家一首发成功、玩家二失败：重试表只登记失败玩家（首发 + 同 Tick 重投各计一次）。
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            var (_, retries) = harness.Pipeline.QueryStatus();
            Assert.Single(retries);
            Assert.Equal(new List<long> { PlayerTwo }, retries[0].FailedPlayerIds);
            Assert.Equal(2, retries[0].Attempts);
            Assert.Equal(OnlineMatchSettlementRetryState.Pending, retries[0].State);

            // 越过幂等保留时长：玩家一的完成记录已过期——旧实现整份重投会在此后对其重复发奖。
            harness.Clock.Advance(1000);
            await harness.Pipeline.TickAsync(Now + 200);
            await harness.Pipeline.TickAsync(Now + 300);
            await harness.Pipeline.TickAsync(Now + 400);

            var (_, exhausted) = harness.Pipeline.QueryStatus();
            Assert.Single(exhausted);
            Assert.Equal(5, exhausted[0].Attempts);
            Assert.Equal(OnlineMatchSettlementRetryState.ManualReview, exhausted[0].State);
            Assert.Equal(new List<long> { PlayerTwo }, exhausted[0].FailedPlayerIds);

            // 玩家一恰好一份（幂等 TTL 已过期，任何重复投递都会翻倍）；玩家二未发。
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(0L, await harness.BalanceOfAsync(PlayerTwo));

            // 人工复核冻结：后续 tick 不再自动重投。
            await harness.Pipeline.TickAsync(Now + 500);
            var (_, frozen) = harness.Pipeline.QueryStatus();
            Assert.Single(frozen);
            Assert.Equal(5, frozen[0].Attempts);
            Assert.Equal(OnlineMatchSettlementRetryState.ManualReview, frozen[0].State);
        }

        /// <summary>
        /// 验证全量失败达尝试上限后冻结为人工复核：修复资产存储也不再自动重投（人工裁决面语义）。
        /// </summary>
        [Fact]
        public async Task TickAsync_RetryExhausted_ShouldFreezeAtManualReview_AndStopAutoRedispatch()
        {
            var harness = new Harness(withFailingStore: true);
            await harness.PlayToSettlingAsync();

            await harness.Pipeline.TickAsync(Now + 100);
            await harness.Pipeline.TickAsync(Now + 200);
            await harness.Pipeline.TickAsync(Now + 300);
            await harness.Pipeline.TickAsync(Now + 400);

            var (_, retries) = harness.Pipeline.QueryStatus();
            Assert.Single(retries);
            Assert.Equal(5, retries[0].Attempts);
            Assert.Equal(OnlineMatchSettlementRetryState.ManualReview, retries[0].State);

            // 修复资产存储后仍不自动重投：人工复核条目脱离自动重试。
            harness.AssetStore.FailureEnabled = false;
            await harness.Pipeline.TickAsync(Now + 500);

            var (_, frozen) = harness.Pipeline.QueryStatus();
            Assert.Single(frozen);
            Assert.Equal(5, frozen[0].Attempts);
            Assert.Equal(OnlineMatchSettlementRetryState.ManualReview, frozen[0].State);
            Assert.Equal(0L, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(0L, await harness.BalanceOfAsync(PlayerTwo));
        }

        /// <summary>
        /// 验证分发器缺席（assets 能力被过滤）时仅结算、不发奖、不记重试。
        /// </summary>
        [Fact]
        public async Task TickAsync_WithoutDispatcher_ShouldSettleOnly()
        {
            var harness = new Harness(withDispatcher: false);
            await harness.PlayToSettlingAsync();

            await harness.Pipeline.TickAsync(Now + 100);

            Assert.Equal(OnlineMatchState.Completed, harness.Actor.State);
            Assert.Equal(0L, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(0L, await harness.BalanceOfAsync(PlayerTwo));
            var (_, retries) = harness.Pipeline.QueryStatus();
            Assert.Empty(retries);
        }

        /// <summary>
        /// 验证并发双 TickAsync 串行安全（互斥下只发一次奖）。
        /// </summary>
        [Fact]
        public async Task TickAsync_Concurrent_ShouldSerialize()
        {
            var harness = new Harness();
            await harness.PlayToSettlingAsync();

            await Task.WhenAll(harness.Pipeline.TickAsync(Now + 100), harness.Pipeline.TickAsync(Now + 101));

            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(PlayerTwo));
            var (_, retries) = harness.Pipeline.QueryStatus();
            Assert.Empty(retries);
        }

        /// <summary>
        /// 测试基座：真实资产域（可注入失败的存储装饰器）+ 对局域全栈。
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// 初始化测试基座。
            /// </summary>
            /// <param name="withFailingStore">是否装配可切换失败的资产存储。</param>
            /// <param name="withDispatcher">是否装配发奖分发器（false = 模拟 assets 能力缺席）。</param>
            /// <param name="idempotencyRetentionMilliseconds">幂等记录保留时长毫秒（默认 24 小时；缩短可模拟 TTL 过期）。</param>
            public Harness(bool withFailingStore = false, bool withDispatcher = true, long idempotencyRetentionMilliseconds = DefaultIdempotencyRetentionMilliseconds)
            {
                AssetStore = new FailingAssetStore(new InMemoryOnlineAssetStore()) { FailureEnabled = withFailingStore };
                TransactionStore = new InMemoryOnlineAssetTransactionStore();
                Publisher = new OnlineEventRecorder();
                MatchStore = new InMemoryOnlineMatchActorStore();
                ResultStore = new InMemoryOnlineMatchResultStore();
                Runtime = new OnlineMatchRuntime(MatchStore, Publisher, new OnlineMatchRuntimeOptions());
                Runtime.RegisterGame(new OnlineRockPaperScissorsGame());
                Settlement = new OnlineMatchSettlementService(Runtime, ResultStore, Publisher);

                OnlineGrantService grantService = null;
                if (withDispatcher)
                {
                    // 重试补齐依赖宿主把失败重放策略装配为 ReExecute（赛季/锦标赛结算重试测试同一装配项先例）。
                    var options = new IdempotencyOptions { ConcurrentWaitTimeoutMilliseconds = 1, FailedReplayPolicy = FailedReplayPolicy.ReExecute, RetentionMilliseconds = idempotencyRetentionMilliseconds };
                    Clock = new StepClock();
                    var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), Clock, options);
                    grantService = new OnlineGrantService(AssetStore, TransactionStore, new OnlineIdempotencyService(coordinator, null), Publisher);
                }

                Dispatcher = grantService == null ? null : new OnlineMatchResultDispatcher(grantService);
                Pipeline = new OnlineMatchSettlementPipeline(Runtime, Settlement, Dispatcher);
            }

            /// <summary>获取资产存储（可切换失败 / 可按玩家注入失败）。</summary>
            public FailingAssetStore AssetStore
            {
                get;
            }

            /// <summary>获取幂等时钟（可外部跳变，用于模拟幂等记录过期）。</summary>
            public StepClock Clock
            {
                get;
            }

            /// <summary>获取资产流水存储。</summary>
            public InMemoryOnlineAssetTransactionStore TransactionStore
            {
                get;
            }

            /// <summary>获取事件记录桩。</summary>
            public OnlineEventRecorder Publisher
            {
                get;
            }

            /// <summary>获取对局存储。</summary>
            public InMemoryOnlineMatchActorStore MatchStore
            {
                get;
            }

            /// <summary>获取结算结果存储。</summary>
            public InMemoryOnlineMatchResultStore ResultStore
            {
                get;
            }

            /// <summary>获取运行时。</summary>
            public OnlineMatchRuntime Runtime
            {
                get;
            }

            /// <summary>获取结算服务。</summary>
            public OnlineMatchSettlementService Settlement
            {
                get;
            }

            /// <summary>获取发奖分发器（可空）。</summary>
            public OnlineMatchResultDispatcher Dispatcher
            {
                get;
            }

            /// <summary>获取结算管线。</summary>
            public OnlineMatchSettlementPipeline Pipeline
            {
                get;
            }

            /// <summary>获取已装载的对局 Actor。</summary>
            public OnlineMatchActor Actor
            {
                get;
                private set;
            }

            /// <summary>
            /// 查询玩家样本资产余额。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            /// <returns>余额（无账户为 0）。</returns>
            public async Task<long> BalanceOfAsync(long playerId)
            {
                var wallet = await AssetStore.FindWalletAsync(TenantId, AppId, playerId, OnlineRockPaperScissorsGame.RewardAssetId);
                return wallet == null ? 0L : wallet.Balance;
            }

            /// <summary>
            /// 建局并打完一场对局（玩家一 2:0 获胜），对局进入结算阶段。
            /// </summary>
            /// <returns>完成通知。</returns>
            public async Task PlayToSettlingAsync()
            {
                var assignment = new GameFrameX.Capability.Online.Matchmaking.OnlineMatchAssignment
                {
                    AssignmentId = "asg-1",
                    MatchId = MatchId,
                    PlayerIds = new List<long> { PlayerOne, PlayerTwo },
                    Mode = OnlineRockPaperScissorsGame.MatchMode,
                    Region = 1,
                    CreatedAtTime = Now,
                    TicketIds = new List<string>(),
                    TenantId = TenantId,
                    AppId = AppId,
                    ServerId = ServerId,
                };
                var created = await Runtime.CreateFromAssignmentAsync(assignment, Now);
                Assert.True(created.IsSuccess, created.Message);
                Actor = await Runtime.ResolveActorAsync(TenantId, AppId, MatchId);
                Assert.NotNull(Actor);

                await Actor.SetReadyAsync(ScopeOf(PlayerOne), true, Now);
                await Actor.SetReadyAsync(ScopeOf(PlayerTwo), true, Now + 1);
                Assert.True((await Actor.StartAsync(ScopeOf(PlayerOne), Now + 2)).IsSuccess);

                await SubmitAsync(PlayerOne, (int)OnlineRockPaperScissorsMove.Rock, Now + 10);
                await SubmitAsync(PlayerTwo, (int)OnlineRockPaperScissorsMove.Scissors, Now + 20);
                await SubmitAsync(PlayerOne, (int)OnlineRockPaperScissorsMove.Rock, Now + 30);
                await SubmitAsync(PlayerTwo, (int)OnlineRockPaperScissorsMove.Scissors, Now + 40);
                Assert.Equal(OnlineMatchState.Settling, Actor.State);
            }

            /// <summary>
            /// 提交一次出拳意图（客户端序号按提交次数递增）。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="move">出拳。</param>
            /// <param name="nowUnixMilliseconds">当前时刻。</param>
            /// <returns>完成通知。</returns>
            private async Task SubmitAsync(long playerId, int move, long nowUnixMilliseconds)
            {
                _submitCount[playerId] = _submitCount.TryGetValue(playerId, out var count) ? count + 1 : 1;
                var input = new OnlineMatchInput
                {
                    PlayerId = playerId,
                    ClientSequence = _submitCount[playerId],
                    ActionId = move,
                    ClientTime = nowUnixMilliseconds,
                };

                var ack = await Actor.SubmitInputAsync(ScopeOf(playerId), input, nowUnixMilliseconds);
                Assert.True(ack.IsSuccess, ack.Message);
                Assert.True(ack.Data.Accepted, ack.Data.Message);
            }

            /// <summary>每玩家已提交次数（客户端序号派生源）。</summary>
            private readonly Dictionary<long, long> _submitCount = new();

            /// <summary>
            /// 构造玩家级作用域。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            /// <returns>作用域。</returns>
            private static OnlineScope ScopeOf(long playerId)
            {
                return new OnlineScope(TenantId, AppId, ServerId, playerId);
            }
        }

        /// <summary>
        /// 可切换失败的资产存储装饰器（发奖失败注入：ApplyAsync 按开关返回失败）。
        /// </summary>
        private sealed class FailingAssetStore : IOnlineAssetStore
        {
            /// <summary>被装饰的真实存储。</summary>
            private readonly IOnlineAssetStore _inner;

            /// <summary>
            /// 初始化装饰器。
            /// </summary>
            /// <param name="inner">真实存储。</param>
            public FailingAssetStore(IOnlineAssetStore inner)
            {
                _inner = inner;
            }

            /// <summary>
            /// 获取或设置是否注入应用失败。
            /// </summary>
            public bool FailureEnabled
            {
                get;
                set;
            }

            /// <summary>
            /// 获取按玩家注入失败的玩家集合（命中的玩家应用直接失败，不受 <see cref="FailureEnabled"/> 影响）。
            /// </summary>
            public HashSet<long> FailingPlayerIds { get; } = new HashSet<long>();

            /// <summary>
            /// 查询钱包账户；原样委托给内部真实存储，不受失败注入影响。
            /// </summary>
            /// <remarks>
            /// Finds a wallet account by delegating to the inner real store; unaffected by failure injection.
            /// </remarks>
            /// <param name="tenantId">租户标识 / Tenant identifier</param>
            /// <param name="appId">应用标识 / Application identifier</param>
            /// <param name="playerId">玩家标识 / Player identifier</param>
            /// <param name="currencyId">货币标识 / Currency identifier</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>内部存储返回的钱包账户 / Wallet account returned by the inner store</returns>
            public Task<OnlineWalletAccount> FindWalletAsync(long tenantId, long appId, long playerId, string currencyId, CancellationToken cancellationToken = default)
            {
                return _inner.FindWalletAsync(tenantId, appId, playerId, currencyId, cancellationToken);
            }

            /// <summary>
            /// 查询背包堆叠；原样委托给内部真实存储，不受失败注入影响。
            /// </summary>
            /// <remarks>
            /// Finds an inventory stack by delegating to the inner real store; unaffected by failure injection.
            /// </remarks>
            /// <param name="tenantId">租户标识 / Tenant identifier</param>
            /// <param name="appId">应用标识 / Application identifier</param>
            /// <param name="playerId">玩家标识 / Player identifier</param>
            /// <param name="itemId">物品标识 / Item identifier</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>内部存储返回的背包堆叠 / Inventory stack returned by the inner store</returns>
            public Task<OnlineInventoryStack> FindInventoryAsync(long tenantId, long appId, long playerId, string itemId, CancellationToken cancellationToken = default)
            {
                return _inner.FindInventoryAsync(tenantId, appId, playerId, itemId, cancellationToken);
            }

            /// <summary>
            /// 列出玩家全部钱包账户；原样委托给内部真实存储。
            /// </summary>
            /// <remarks>
            /// Lists all wallet accounts of the player by delegating to the inner real store.
            /// </remarks>
            /// <param name="tenantId">租户标识 / Tenant identifier</param>
            /// <param name="appId">应用标识 / Application identifier</param>
            /// <param name="playerId">玩家标识 / Player identifier</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>内部存储返回的钱包账户集合 / Wallet accounts returned by the inner store</returns>
            public Task<IReadOnlyList<OnlineWalletAccount>> ListWalletAccountsAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return _inner.ListWalletAccountsAsync(tenantId, appId, playerId, cancellationToken);
            }

            /// <summary>
            /// 列出玩家全部背包堆叠；原样委托给内部真实存储。
            /// </summary>
            /// <remarks>
            /// Lists all inventory stacks of the player by delegating to the inner real store.
            /// </remarks>
            /// <param name="tenantId">租户标识 / Tenant identifier</param>
            /// <param name="appId">应用标识 / Application identifier</param>
            /// <param name="playerId">玩家标识 / Player identifier</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>内部存储返回的背包堆叠集合 / Inventory stacks returned by the inner store</returns>
            public Task<IReadOnlyList<OnlineInventoryStack>> ListInventoryStacksAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return _inner.ListInventoryStacksAsync(tenantId, appId, playerId, cancellationToken);
            }

            /// <summary>
            /// 应用资产变更批次；开启 <c>FailureEnabled</c> 或批次归属玩家命中 <c>FailingPlayerIds</c> 时
            /// 不触达内部存储，直接返回 <c>InternalError</c> 失败结果（无流水），否则委托给内部真实存储。
            /// </summary>
            /// <remarks>
            /// Applies an asset change batch; when <c>FailureEnabled</c> is set or the batch owner hits
            /// <c>FailingPlayerIds</c> it returns an injected <c>InternalError</c> failure with no ledger entries,
            /// otherwise delegates to the inner store.
            /// </remarks>
            /// <param name="batch">资产变更批次 / Asset change batch</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>注入的失败结果或内部存储的应用结果 / Injected failure result or the inner store's apply result</returns>
            public Task<OnlineAssetApplyResult> ApplyAsync(OnlineAssetChangeBatch batch, CancellationToken cancellationToken = default)
            {
                if (FailureEnabled || FailingPlayerIds.Contains(batch.PlayerId))
                {
                    return Task.FromResult(new OnlineAssetApplyResult(false, OnlineErrorCode.InternalError, "注入的发奖失败", new List<OnlineLedgerEntry>()));
                }

                return _inner.ApplyAsync(batch, cancellationToken);
            }

            /// <summary>
            /// 分页查询资产流水；原样委托给内部真实存储。
            /// </summary>
            /// <remarks>
            /// Queries ledger entries page by page by delegating to the inner real store.
            /// </remarks>
            /// <param name="query">流水分页查询条件 / Ledger page query</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>内部存储返回的流水集合 / Ledger entries returned by the inner store</returns>
            public Task<IReadOnlyList<OnlineLedgerEntry>> ListLedgerEntriesAsync(OnlineLedgerPageQuery query, CancellationToken cancellationToken = default)
            {
                return _inner.ListLedgerEntriesAsync(query, cancellationToken);
            }

            /// <summary>
            /// 按事务标识查询资产流水；原样委托给内部真实存储，供断言注入失败后未落流水。
            /// </summary>
            /// <remarks>
            /// Finds ledger entries by transaction identifier by delegating to the inner real store.
            /// </remarks>
            /// <param name="transactionId">事务标识 / Transaction identifier</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>内部存储返回的流水集合 / Ledger entries returned by the inner store</returns>
            public Task<IReadOnlyList<OnlineLedgerEntry>> FindLedgerEntriesByTransactionIdAsync(string transactionId, CancellationToken cancellationToken = default)
            {
                return _inner.FindLedgerEntriesByTransactionIdAsync(transactionId, cancellationToken);
            }

            /// <summary>
            /// 按资产汇总玩家流水增量；原样委托给内部真实存储。
            /// </summary>
            /// <remarks>
            /// Sums the player's ledger deltas per asset by delegating to the inner real store.
            /// </remarks>
            /// <param name="tenantId">租户标识 / Tenant identifier</param>
            /// <param name="appId">应用标识 / Application identifier</param>
            /// <param name="playerId">玩家标识 / Player identifier</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>资产标识到增量合计的映射 / Mapping from asset identifier to summed delta</returns>
            public Task<IReadOnlyDictionary<string, long>> SumLedgerDeltasByAssetAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return _inner.SumLedgerDeltasByAssetAsync(tenantId, appId, playerId, cancellationToken);
            }

            /// <summary>
            /// 列出拥有资产记录的玩家标识；原样委托给内部真实存储。
            /// </summary>
            /// <remarks>
            /// Lists player identifiers that have asset records by delegating to the inner real store.
            /// </remarks>
            /// <param name="tenantId">租户标识 / Tenant identifier</param>
            /// <param name="appId">应用标识 / Application identifier</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>内部存储返回的玩家标识集合 / Player identifiers returned by the inner store</returns>
            public Task<IReadOnlyList<long>> ListPlayerIdsAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
            {
                return _inner.ListPlayerIdsAsync(tenantId, appId, cancellationToken);
            }
        }

        /// <summary>
        /// 步进时钟桩（沿用资产域测试约定：每次读取推进 10ms）。
        /// </summary>
        private sealed class StepClock : IClock
        {
            /// <summary>当前时刻。</summary>
            private long _nowMilliseconds;

            /// <summary>
            /// 一次性推进时钟（模拟时间流逝，如幂等记录过期）。
            /// </summary>
            /// <param name="deltaMilliseconds">推进毫秒数。</param>
            public void Advance(long deltaMilliseconds)
            {
                _nowMilliseconds += deltaMilliseconds;
            }

            /// <summary>
            /// 获取当前时刻（每次读取推进 10ms）。
            /// </summary>
            public long UtcNowTime
            {
                get
                {
                    _nowMilliseconds += 10;
                    return _nowMilliseconds;
                }
            }
        }
    }
}
