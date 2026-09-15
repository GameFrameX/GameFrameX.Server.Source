// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
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
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Match;
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 对局同步协议测试（vault:C6 S5.5 / S5.6 / VC-5.2～VC-5.7）：
    /// 输入校验顺序、重复包与乱序包幂等、序号只由服务端推进、增量补发与重连窗口。
    /// </summary>
    public class OnlineMatchSyncProtocolTests
    {
        /// <summary>租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>App 标识。</summary>
        private const long AppId = 10;

        /// <summary>区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>玩家一（房主）。</summary>
        private const long PlayerOne = 1001;

        /// <summary>玩家二。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>基准时刻（UTC 毫秒）。</summary>
        private const long Now = 1000000L;

        /// <summary>
        /// 验证 VC-5.5：重复包按幂等处理——不重复执行、不推进序号、状态不变。
        /// </summary>
        [Fact]
        public async Task DuplicatePacket_ShouldBeIdempotent()
        {
            var harness = new Harness();
            await harness.StartRunningAsync();

            var first = await harness.SubmitAsync(PlayerOne, 1, OnlineRockPaperScissorsMove.Rock, Now + 10);
            var sequenceAfterFirst = harness.Actor.ServerSequence;
            var stateAfterFirst = harness.Actor.GetSnapshot().GameState;

            var second = await harness.SubmitAsync(PlayerOne, 1, OnlineRockPaperScissorsMove.Paper, Now + 20);

            Assert.True(first.Accepted);
            Assert.False(second.Accepted);
            Assert.True(second.IsDuplicate);
            Assert.Equal(OnlineMatchInputRejection.Duplicate, second.Rejection);
            Assert.Equal(sequenceAfterFirst, harness.Actor.ServerSequence);
            Assert.Equal(sequenceAfterFirst, second.ServerSequence);
            Assert.Equal(stateAfterFirst, harness.Actor.GetSnapshot().GameState);
        }

        /// <summary>
        /// 验证 VC-5.5：乱序包（序号跳变）被拒绝且状态不变。
        /// </summary>
        [Fact]
        public async Task OutOfOrderPacket_ShouldBeRejected()
        {
            var harness = new Harness();
            await harness.StartRunningAsync();
            await harness.SubmitAsync(PlayerOne, 1, OnlineRockPaperScissorsMove.Rock, Now + 10);
            var sequenceAfterFirst = harness.Actor.ServerSequence;

            var ack = await harness.SubmitAsync(PlayerOne, 3, OnlineRockPaperScissorsMove.Paper, Now + 20);

            Assert.False(ack.Accepted);
            Assert.Equal(OnlineMatchInputRejection.OutOfOrder, ack.Rejection);
            Assert.Equal(sequenceAfterFirst, harness.Actor.ServerSequence);
        }

        /// <summary>
        /// 验证 VC-5.2 / VC-5.3：身份取自服务端作用域，客户端自报的 PlayerId 不参与判定。
        /// </summary>
        [Fact]
        public async Task SpoofedPlayerIdInInput_ShouldBeIgnored()
        {
            var harness = new Harness();
            await harness.StartRunningAsync();

            // 作用域是玩家一，但 payload 自称玩家二。
            var spoofed = new OnlineMatchInput
            {
                PlayerId = PlayerTwo,
                ClientSequence = 1,
                ActionId = (int)OnlineRockPaperScissorsMove.Rock,
                ClientTime = Now + 10,
            };
            var ack = await harness.Actor.SubmitInputAsync(harness.ScopeOf(PlayerOne), spoofed, Now + 10);

            Assert.True(ack.IsSuccess, ack.Message);
            Assert.True(ack.Data.Accepted);

            // 玩家二的序号槽未被占用：其首个包仍应被接受。
            var secondPlayerAck = await harness.SubmitAsync(PlayerTwo, 1, OnlineRockPaperScissorsMove.Scissors, Now + 20);
            Assert.True(secondPlayerAck.Accepted);
        }

        /// <summary>
        /// 验证 VC-5.3：非成员提交被拒（作用域越权），且不产生任何状态变更。
        /// </summary>
        [Fact]
        public async Task NonMemberInput_ShouldBeScopeDenied()
        {
            var harness = new Harness();
            await harness.StartRunningAsync();
            var sequenceBefore = harness.Actor.ServerSequence;

            var input = new OnlineMatchInput
            {
                PlayerId = 9999,
                ClientSequence = 1,
                ActionId = (int)OnlineRockPaperScissorsMove.Rock,
                ClientTime = Now + 10,
            };
            var ack = await harness.Actor.SubmitInputAsync(harness.ScopeOf(9999), input, Now + 10);

            Assert.False(ack.IsSuccess);
            Assert.Equal(OnlineErrorCode.ScopeDenied, ack.Code);
            Assert.Equal(sequenceBefore, harness.Actor.ServerSequence);
        }

        /// <summary>
        /// 验证 VC-5.3：非法操作值（越出玩法契约枚举）被拒。
        /// </summary>
        [Fact]
        public async Task IllegalAction_ShouldBeRejected()
        {
            var harness = new Harness();
            await harness.StartRunningAsync();

            var ack = await harness.SubmitAsync(PlayerOne, 1, (OnlineRockPaperScissorsMove)99, Now + 10);

            Assert.False(ack.Accepted);
            Assert.Equal(OnlineMatchInputRejection.IllegalAction, ack.Rejection);
        }

        /// <summary>
        /// 验证 VC-5.2：对局未进入运行阶段时输入被拒。
        /// </summary>
        [Fact]
        public async Task InputBeforeRunning_ShouldBeRejected()
        {
            var harness = new Harness();
            await harness.CreateAsync();

            var ack = await harness.SubmitAsync(PlayerOne, 1, OnlineRockPaperScissorsMove.Rock, Now + 10);

            Assert.False(ack.Accepted);
            Assert.Equal(OnlineMatchInputRejection.StateNotPlayable, ack.Rejection);
        }

        /// <summary>
        /// 验证 VC-5.6：序号只由服务端推进（每次被接受的输入 +1，被拒的输入不变）。
        /// </summary>
        [Fact]
        public async Task ServerSequence_ShouldAdvanceOnlyOnAcceptedInput()
        {
            var harness = new Harness();
            await harness.StartRunningAsync();

            var accepted = await harness.SubmitAsync(PlayerOne, 1, OnlineRockPaperScissorsMove.Rock, Now + 10);
            var afterAccepted = harness.Actor.ServerSequence;

            await harness.SubmitAsync(PlayerOne, 1, OnlineRockPaperScissorsMove.Rock, Now + 20);

            Assert.True(accepted.ServerSequence <= afterAccepted);
            Assert.Equal(afterAccepted, harness.Actor.ServerSequence);
        }

        /// <summary>
        /// 验证 VC-5.6：增量区间为左开右闭，且终点即当前权威序号。
        /// </summary>
        [Fact]
        public async Task BuildDelta_ShouldCoverRequestedRange()
        {
            var harness = new Harness();
            await harness.StartRunningAsync();

            var anchor = await harness.SubmitAsync(PlayerOne, 1, OnlineRockPaperScissorsMove.Rock, Now + 10);
            await harness.SubmitAsync(PlayerTwo, 1, OnlineRockPaperScissorsMove.Scissors, Now + 20);

            var delta = harness.Actor.BuildDelta(anchor.ServerSequence);

            Assert.Equal(anchor.ServerSequence, delta.FromSequence);
            Assert.Equal(harness.Actor.ServerSequence, delta.ToSequence);
            Assert.NotEmpty(delta.Events);
            foreach (var item in delta.Events)
            {
                Assert.True(item.Sequence > delta.FromSequence, "增量不得包含已确认序号");
                Assert.True(item.Sequence <= delta.ToSequence, "增量不得越过权威序号");
            }
        }

        /// <summary>
        /// 验证 VC-5.6：客户端序号早于事件日志下界时增量不可用（必须回退全量快照）。
        /// </summary>
        [Fact]
        public async Task CanServeDelta_BelowEventLogLowerBound_ShouldBeFalse()
        {
            var harness = new Harness(new OnlineMatchRuntimeOptions { MaxServerEventsPerMatch = 3 });
            await harness.StartRunningAsync();

            for (var round = 1; round <= 2; round++)
            {
                await harness.SubmitAsync(PlayerOne, round, OnlineRockPaperScissorsMove.Rock, Now + (round * 10));
                await harness.SubmitAsync(PlayerTwo, round, OnlineRockPaperScissorsMove.Scissors, Now + (round * 10) + 1);
            }

            Assert.False(harness.Actor.CanServeDelta(0));
            Assert.True(harness.Actor.CanServeDelta(harness.Actor.ServerSequence));

            var reconnect = new OnlineMatchReconnectService(harness.Runtime);
            var token = await harness.DisconnectAsync(PlayerTwo, Now + 100);
            var context = new OnlineMatchReconnectContext
            {
                MatchId = Harness.MatchId,
                PlayerId = PlayerTwo,
                ReconnectToken = token,
                LastAcknowledgedSequence = 0,
                UnacknowledgedInputs = new List<OnlineMatchInput>(),
            };

            var resumed = await reconnect.ReconnectAsync(harness.ScopeOf(PlayerTwo), context, Now + 150);

            Assert.True(resumed.IsSuccess, resumed.Message);
            Assert.True(resumed.Data.Succeeded);
            Assert.NotNull(resumed.Data.Snapshot);
            Assert.Null(resumed.Data.Delta);
        }

        /// <summary>
        /// 验证 VC-5.6：窗口内重连返回快照 + 缺失增量，且增量起点即客户端已确认序号。
        /// </summary>
        [Fact]
        public async Task Reconnect_WithinWindow_ShouldReturnSnapshotAndDelta()
        {
            var harness = new Harness();
            await harness.StartRunningAsync();
            var anchor = await harness.SubmitAsync(PlayerOne, 1, OnlineRockPaperScissorsMove.Rock, Now + 10);
            await harness.SubmitAsync(PlayerTwo, 1, OnlineRockPaperScissorsMove.Scissors, Now + 20);

            var reconnect = new OnlineMatchReconnectService(harness.Runtime);
            var token = await harness.DisconnectAsync(PlayerOne, Now + 100);

            var context = new OnlineMatchReconnectContext
            {
                MatchId = Harness.MatchId,
                PlayerId = PlayerOne,
                ReconnectToken = token,
                LastAcknowledgedSequence = anchor.ServerSequence,
                UnacknowledgedInputs = new List<OnlineMatchInput>(),
            };
            var resumed = await reconnect.ReconnectAsync(harness.ScopeOf(PlayerOne), context, Now + 150);

            Assert.True(resumed.IsSuccess, resumed.Message);
            Assert.True(resumed.Data.Succeeded);
            Assert.Equal(OnlineMatchState.Running, resumed.Data.State);
            Assert.Equal(harness.Actor.ServerSequence, resumed.Data.ServerSequence);
            Assert.Equal(harness.Actor.ServerSequence, resumed.Data.Snapshot.ServerSequence);
            Assert.NotNull(resumed.Data.Delta);
            Assert.Equal(anchor.ServerSequence, resumed.Data.Delta.FromSequence);
            Assert.Equal(resumed.Data.Snapshot.ServerSequence, resumed.Data.Delta.ToSequence);
        }

        /// <summary>
        /// 验证 VC-5.2：伪造或过期令牌不得重新进入对局，且失败原因稳定。
        /// </summary>
        [Fact]
        public async Task Reconnect_WrongToken_ShouldBeRevoked()
        {
            var harness = new Harness();
            await harness.StartRunningAsync();
            await harness.DisconnectAsync(PlayerOne, Now + 100);

            var reconnect = new OnlineMatchReconnectService(harness.Runtime);
            var context = new OnlineMatchReconnectContext
            {
                MatchId = Harness.MatchId,
                PlayerId = PlayerOne,
                ReconnectToken = "rt-forged",
                LastAcknowledgedSequence = 0,
                UnacknowledgedInputs = new List<OnlineMatchInput>(),
            };

            var resumed = await reconnect.ReconnectAsync(harness.ScopeOf(PlayerOne), context, Now + 150);

            Assert.False(resumed.IsSuccess);
            Assert.Equal(OnlineErrorCode.TokenRevoked, resumed.Code);
        }

        /// <summary>
        /// 验证 VC-5.7：超过重连窗口后重连失败且原因稳定（不无限重试）。
        /// </summary>
        [Fact]
        public async Task Reconnect_BeyondWindow_ShouldTimeout()
        {
            var harness = new Harness(new OnlineMatchRuntimeOptions { ReconnectWindowSeconds = 5 });
            await harness.StartRunningAsync();
            var token = await harness.DisconnectAsync(PlayerOne, Now + 100);

            var reconnect = new OnlineMatchReconnectService(harness.Runtime);
            var context = new OnlineMatchReconnectContext
            {
                MatchId = Harness.MatchId,
                PlayerId = PlayerOne,
                ReconnectToken = token,
                LastAcknowledgedSequence = 0,
                UnacknowledgedInputs = new List<OnlineMatchInput>(),
            };

            var resumed = await reconnect.ReconnectAsync(harness.ScopeOf(PlayerOne), context, Now + 100 + 5001);

            Assert.False(resumed.IsSuccess);
            Assert.Equal(OnlineErrorCode.NetworkTimeout, resumed.Code);
        }

        /// <summary>
        /// 验证 VC-5.7：重连窗口到期后成员进入确定态（退出），不存在永久 Reconnecting。
        /// </summary>
        [Fact]
        public async Task ReconnectWindowExpired_ShouldReachDeterminateMemberState()
        {
            var harness = new Harness(new OnlineMatchRuntimeOptions { ReconnectWindowSeconds = 5 });
            await harness.StartRunningAsync();
            await harness.DisconnectAsync(PlayerOne, Now + 100);

            var ticks = await harness.Runtime.TickAllAsync(Now + 100 + 5001);

            Assert.True(ticks[0].ReconnectWindowExpired);
            var snapshot = harness.Actor.GetSnapshot();
            var member = snapshot.Members.Find(item => item.PlayerId == PlayerOne);
            Assert.NotNull(member);
            Assert.Equal(OnlineMatchMemberState.Left, member.State);
        }

        /// <summary>
        /// 验证 VC-5.2：全程无人可服务时对局收敛到确定终态而不是悬挂。
        /// </summary>
        [Fact]
        public async Task AllMembersGone_ShouldCancelMatch()
        {
            var harness = new Harness(new OnlineMatchRuntimeOptions { ReconnectWindowSeconds = 5 });
            await harness.StartRunningAsync();
            await harness.DisconnectAsync(PlayerOne, Now + 100);
            await harness.DisconnectAsync(PlayerTwo, Now + 101);

            await harness.Runtime.TickAllAsync(Now + 100 + 5001);

            Assert.Equal(OnlineMatchState.Cancelled, harness.Actor.State);
        }

        /// <summary>
        /// 构造只含指定玩家的匹配分配。
        /// </summary>
        /// <param name="playerIds">玩家集合。</param>
        /// <returns>分配实例。</returns>
        private static OnlineMatchAssignment BuildAssignment(params long[] playerIds)
        {
            return new OnlineMatchAssignment
            {
                AssignmentId = "asg-1",
                MatchId = Harness.MatchId,
                PlayerIds = new List<long>(playerIds),
                Mode = OnlineRockPaperScissorsGame.MatchMode,
                Region = 1,
                CreatedAtTime = Now,
                TicketIds = new List<string>(),
                TenantId = TenantId,
                AppId = AppId,
                ServerId = ServerId,
            };
        }

        /// <summary>
        /// 测试基座：内存存储 + 石头剪刀布玩法 + 重连辅助。
        /// </summary>
        private sealed class Harness
        {
            /// <summary>对局标识。</summary>
            public const string MatchId = "match-1";

            /// <summary>
            /// 初始化测试基座。
            /// </summary>
            /// <param name="options">运行参数（可空）。</param>
            public Harness(OnlineMatchRuntimeOptions options = null)
            {
                Store = new InMemoryOnlineMatchActorStore();
                Runtime = new OnlineMatchRuntime(Store, new OnlineEventRecorder(), options);
                Runtime.RegisterGame(new OnlineRockPaperScissorsGame());
                Reconnect = new OnlineMatchReconnectService(Runtime);
            }

            /// <summary>获取对局存储。</summary>
            public InMemoryOnlineMatchActorStore Store
            {
                get;
            }

            /// <summary>获取运行时。</summary>
            public OnlineMatchRuntime Runtime
            {
                get;
            }

            /// <summary>获取重连服务。</summary>
            public OnlineMatchReconnectService Reconnect
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
            /// 按分配建局。
            /// </summary>
            /// <returns>完成通知。</returns>
            public async Task CreateAsync()
            {
                var created = await Runtime.CreateFromAssignmentAsync(BuildAssignment(PlayerOne, PlayerTwo), Now);
                Assert.True(created.IsSuccess, created.Message);
                Actor = await Runtime.ResolveActorAsync(TenantId, AppId, MatchId);
                Assert.NotNull(Actor);
            }

            /// <summary>
            /// 建局并推进到运行阶段。
            /// </summary>
            /// <returns>完成通知。</returns>
            public async Task StartRunningAsync()
            {
                await CreateAsync();
                await Actor.SetReadyAsync(ScopeOf(PlayerOne), true, Now);
                await Actor.SetReadyAsync(ScopeOf(PlayerTwo), true, Now + 1);
                var started = await Actor.StartAsync(ScopeOf(PlayerOne), Now + 2);
                Assert.True(started.IsSuccess, started.Message);
            }

            /// <summary>
            /// 提交一次出拳意图（返回确认，不断言被接受）。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="clientSequence">客户端序号。</param>
            /// <param name="move">出拳。</param>
            /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
            /// <returns>输入确认。</returns>
            public async Task<OnlineMatchInputAck> SubmitAsync(long playerId, int clientSequence, OnlineRockPaperScissorsMove move, long nowUnixMilliseconds)
            {
                var input = new OnlineMatchInput
                {
                    PlayerId = playerId,
                    ClientSequence = clientSequence,
                    ActionId = (int)move,
                    ClientTime = nowUnixMilliseconds,
                };

                var ack = await Actor.SubmitInputAsync(ScopeOf(playerId), input, nowUnixMilliseconds);
                Assert.True(ack.IsSuccess, ack.Message);
                return ack.Data;
            }

            /// <summary>
            /// 标记断线并返回重连令牌。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
            /// <returns>重连令牌。</returns>
            public async Task<string> DisconnectAsync(long playerId, long nowUnixMilliseconds)
            {
                var disconnected = await Reconnect.DisconnectAsync(ScopeOf(playerId), MatchId, nowUnixMilliseconds);
                Assert.True(disconnected.IsSuccess, disconnected.Message);
                return disconnected.Data.Token;
            }

            /// <summary>
            /// 构造玩家级作用域。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            /// <returns>作用域。</returns>
            public OnlineScope ScopeOf(long playerId)
            {
                return new OnlineScope(TenantId, AppId, ServerId, playerId);
            }
        }
    }
}
