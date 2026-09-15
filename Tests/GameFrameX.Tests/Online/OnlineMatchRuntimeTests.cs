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
using GameFrameX.Online.Events;
using GameFrameX.Online.Match;
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// Match Runtime 与对局 Actor 生命周期测试（vault:C6 S5.1～S5.4 / VC-5.10 / VC-5.11 / VC-5.13）：
    /// 分配建局、准备开局、阶段超时、终态释放与作用域隔离。
    /// </summary>
    public class OnlineMatchRuntimeTests
    {
        /// <summary>租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>App 标识。</summary>
        private const long AppId = 10;

        /// <summary>区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>玩家一。</summary>
        private const long PlayerOne = 1001;

        /// <summary>玩家二。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>基准时刻（UTC 毫秒）。</summary>
        private const long Now = 1000000L;

        /// <summary>对局标识。</summary>
        private const string MatchId = "match-1";

        /// <summary>
        /// 验证 VC-5.1 前半段：分配建局后成员已就位并进入等待阶段。
        /// </summary>
        [Fact]
        public async Task CreateFromAssignmentAsync_ShouldSeedMembersAndEnterWaiting()
        {
            var harness = new Harness();

            var created = await harness.Runtime.CreateFromAssignmentAsync(BuildAssignment(PlayerOne, PlayerTwo), Now);

            Assert.True(created.IsSuccess, created.Message);
            Assert.Equal(MatchId, created.Data.MatchId);
            Assert.Equal(OnlineMatchState.Waiting, created.Data.State);
            Assert.Equal(OnlineRockPaperScissorsGame.MatchMode, created.Data.Mode);
            Assert.Equal(2, created.Data.Members.Count);
            Assert.Equal(1, harness.Runtime.ActiveActorCount);
        }

        /// <summary>
        /// 验证：未注册玩法的模式不得建局（避免产出无法推进的对局）。
        /// </summary>
        [Fact]
        public async Task CreateFromAssignmentAsync_UnknownMode_ShouldFail()
        {
            var harness = new Harness();
            var assignment = BuildAssignment(PlayerOne, PlayerTwo);
            assignment.Mode = 999999;

            var created = await harness.Runtime.CreateFromAssignmentAsync(assignment, Now);

            Assert.False(created.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, created.Code);
            Assert.Equal(0, harness.Runtime.ActiveActorCount);
        }

        /// <summary>
        /// 验证 VC-5.1：全员准备 → 房主开局 → 回合输入 → 分出胜负后进入结算阶段，且全程发布状态变更事件。
        /// </summary>
        [Fact]
        public async Task FullFlow_ShouldReachSettlingAndPublishStateChanged()
        {
            var harness = new Harness();
            await harness.CreateAsync();

            await harness.ReadyAndStartAsync(Now);

            Assert.Equal(OnlineMatchState.Running, harness.Actor.State);

            // 三局两胜：玩家一连续两局出石头胜剪刀，直接终结对局。
            await harness.SubmitAsync(PlayerOne, 1, OnlineRockPaperScissorsMove.Rock, Now + 10);
            await harness.SubmitAsync(PlayerTwo, 1, OnlineRockPaperScissorsMove.Scissors, Now + 20);
            await harness.SubmitAsync(PlayerOne, 2, OnlineRockPaperScissorsMove.Rock, Now + 30);
            var finalAck = await harness.SubmitAsync(PlayerTwo, 2, OnlineRockPaperScissorsMove.Scissors, Now + 40);

            Assert.True(finalAck.Accepted);
            Assert.Equal(OnlineMatchState.Settling, harness.Actor.State);
            Assert.Contains(harness.Publisher.Events, item => item.EventType == OnlineMatchRuntimeEvents.MatchStateChanged);
            Assert.Contains(harness.Publisher.Events, item => item.EventType == OnlineMatchRuntimeEvents.MatchStateChanged && item.Source == OnlineMatchRuntimeEvents.Source);
        }

        /// <summary>
        /// 验证 VC-5.12：等待阶段超时进入 Timeout，并在保留期后释放为 Closed（无僵尸对局）。
        /// </summary>
        [Fact]
        public async Task TickAsync_StageTimeout_ShouldReachTimeoutThenReleaseActor()
        {
            var harness = new Harness(new OnlineMatchRuntimeOptions
            {
                WaitingTimeoutSeconds = 1,
                EndedRetentionSeconds = 1,
            });
            await harness.CreateAsync();

            var timedOut = await harness.Runtime.TickAllAsync(Now + 2000);

            Assert.True(timedOut[0].TimedOut);
            Assert.Equal(OnlineMatchState.Timeout, harness.Actor.State);

            var closed = await harness.Runtime.TickAllAsync(Now + 4000);

            Assert.True(closed[0].Closable);
            Assert.Equal(OnlineMatchState.Closed, harness.Actor.State);
            Assert.Equal(0, harness.Runtime.ActiveActorCount);
            Assert.Equal(1, harness.Runtime.ReleasedActorCount);
            Assert.Null(await harness.Store.FindAsync(TenantId, AppId, MatchId));
        }

        /// <summary>
        /// 验证 VC-5.13：跨租户作用域不得读写他人对局。
        /// </summary>
        [Fact]
        public async Task CrossTenantScope_ShouldBeDenied()
        {
            var harness = new Harness();
            await harness.CreateAsync();

            var foreign = new OnlineScope(TenantId + 1, AppId, ServerId, PlayerOne);
            var result = await harness.Actor.SetReadyAsync(foreign, true, Now);

            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ScopeDenied, result.Code);
        }

        /// <summary>
        /// 验证 S5.3：仅房主可开始对局（房主 = 最早加入的在局成员）。
        /// </summary>
        [Fact]
        public async Task StartAsync_NonHost_ShouldBeForbidden()
        {
            var harness = new Harness();
            await harness.CreateAsync();
            await harness.Actor.SetReadyAsync(harness.ScopeOf(PlayerOne), true, Now);
            await harness.Actor.SetReadyAsync(harness.ScopeOf(PlayerTwo), true, Now);

            var started = await harness.Actor.StartAsync(harness.ScopeOf(PlayerTwo), Now);

            Assert.False(started.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, started.Code);
            Assert.Equal(OnlineMatchState.Ready, harness.Actor.State);
        }

        /// <summary>
        /// 验证 S5.3：成员未齐或未全部准备时不得开局。
        /// </summary>
        [Fact]
        public async Task StartAsync_NotAllReady_ShouldBeRejected()
        {
            var harness = new Harness();
            await harness.CreateAsync();
            await harness.Actor.SetReadyAsync(harness.ScopeOf(PlayerOne), true, Now);

            var started = await harness.Actor.StartAsync(harness.ScopeOf(PlayerOne), Now);

            Assert.False(started.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, started.Code);
        }

        /// <summary>
        /// 验证 S5.3：踢出后成员退出对局且不再是有效参与者。
        /// </summary>
        [Fact]
        public async Task KickAsync_ShouldRemoveMemberFromActiveSet()
        {
            var harness = new Harness();
            await harness.CreateAsync();

            var kicked = await harness.Actor.KickAsync(harness.ScopeOf(PlayerOne), PlayerTwo, Now + 5);

            Assert.True(kicked.IsSuccess, kicked.Message);
            var member = kicked.Data.Members.Find(item => item.PlayerId == PlayerTwo);
            Assert.NotNull(member);
            Assert.Equal(OnlineMatchMemberState.Kicked, member.State);
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
                MatchId = MatchId,
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
        /// 测试基座：内存存储 + 事件记录桩 + 石头剪刀布玩法。
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// 初始化测试基座。
            /// </summary>
            /// <param name="options">运行参数（可空）。</param>
            public Harness(OnlineMatchRuntimeOptions options = null)
            {
                Store = new InMemoryOnlineMatchActorStore();
                Publisher = new OnlineEventRecorder();
                Runtime = new OnlineMatchRuntime(Store, Publisher, options);
                Runtime.RegisterGame(new OnlineRockPaperScissorsGame());
            }

            /// <summary>获取对局存储。</summary>
            public InMemoryOnlineMatchActorStore Store
            {
                get;
            }

            /// <summary>获取事件记录桩。</summary>
            public OnlineEventRecorder Publisher
            {
                get;
            }

            /// <summary>获取运行时。</summary>
            public OnlineMatchRuntime Runtime
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
            /// 全员准备并由房主开局。
            /// </summary>
            /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
            /// <returns>完成通知。</returns>
            public async Task ReadyAndStartAsync(long nowUnixMilliseconds)
            {
                await Actor.SetReadyAsync(ScopeOf(PlayerOne), true, nowUnixMilliseconds);
                await Actor.SetReadyAsync(ScopeOf(PlayerTwo), true, nowUnixMilliseconds + 1);
                var started = await Actor.StartAsync(ScopeOf(PlayerOne), nowUnixMilliseconds + 2);
                Assert.True(started.IsSuccess, started.Message);
            }

            /// <summary>
            /// 提交一次出拳意图。
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
