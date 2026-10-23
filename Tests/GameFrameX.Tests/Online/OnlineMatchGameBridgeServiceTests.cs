// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and applicable international regulations.
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

using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Match;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 对局桥服务测试（C245 样例垂直切片）：
    /// 确定性开局幂等 / 出拳保密投影 / 拒绝码映射 / 新局号语义。
    /// </summary>
    public class OnlineMatchGameBridgeServiceTests
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

        /// <summary>非成员玩家。</summary>
        private const long Outsider = 9999;

        /// <summary>房间标识。</summary>
        private const long RoomId = 5001;

        /// <summary>
        /// 验证开局成功：进入 Running、MatchId 确定性派生、成员表 2 人。
        /// </summary>
        [Fact]
        public async Task StartRoomGame_ShouldRunWithDeterministicMatchId()
        {
            var harness = new Harness();

            var started = await harness.Bridge.StartRoomGameAsync(harness.ScopeOf(PlayerOne), RoomId, 1, harness.Players);

            Assert.True(started.IsSuccess, started.Message);
            Assert.Equal(OnlineMatchBridgeGameState.Running, started.Data.State);
            Assert.Equal("rps-s100-r5001-g1", started.Data.MatchId);
            Assert.Equal(OnlineRockPaperScissorsGame.MatchMode, started.Data.Mode);
            Assert.Equal(2, started.Data.Players.Count);
        }

        /// <summary>
        /// 验证重复开局幂等：同房间同局号复用既有对局（同 MatchId）。
        /// </summary>
        [Fact]
        public async Task StartRoomGame_TwiceSameRound_ShouldReuseMatch()
        {
            var harness = new Harness();
            var first = await harness.Bridge.StartRoomGameAsync(harness.ScopeOf(PlayerOne), RoomId, 1, harness.Players);

            var second = await harness.Bridge.StartRoomGameAsync(harness.ScopeOf(PlayerTwo), RoomId, 1, harness.Players);

            Assert.True(second.IsSuccess, second.Message);
            Assert.Equal(first.Data.MatchId, second.Data.MatchId);
            Assert.Equal(OnlineMatchBridgeGameState.Running, second.Data.State);
        }

        /// <summary>
        /// 验证请求者不在玩家集合内被拒绝。
        /// </summary>
        [Fact]
        public async Task StartRoomGame_RequesterNotInPlayers_ShouldBeScopeDenied()
        {
            var harness = new Harness();

            var started = await harness.Bridge.StartRoomGameAsync(harness.ScopeOf(Outsider), RoomId, 1, harness.Players);

            Assert.False(started.IsSuccess);
            Assert.Equal(OnlineErrorCode.ScopeDenied, started.Code);
        }

        /// <summary>
        /// 验证成员表与既有对局不一致时拒绝（ParameterInvalid）。
        /// </summary>
        [Fact]
        public async Task StartRoomGame_MemberMismatch_ShouldBeParameterInvalid()
        {
            var harness = new Harness();
            await harness.Bridge.StartRoomGameAsync(harness.ScopeOf(PlayerOne), RoomId, 1, harness.Players);

            var swapped = await harness.Bridge.StartRoomGameAsync(harness.ScopeOf(PlayerOne), RoomId, 1, new List<long> { PlayerOne, Outsider });

            Assert.False(swapped.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, swapped.Code);
        }

        /// <summary>
        /// 验证非法出拳值被拒绝。
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        public async Task SubmitGesture_InvalidValue_ShouldBeParameterInvalid(int gesture)
        {
            var harness = new Harness();
            await harness.Bridge.StartRoomGameAsync(harness.ScopeOf(PlayerOne), RoomId, 1, harness.Players);

            var submitted = await harness.Bridge.SubmitGestureAsync(harness.ScopeOf(PlayerOne), RoomId, 1, gesture);

            Assert.False(submitted.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, submitted.Code);
        }

        /// <summary>
        /// 验证单人出拳后的保密投影：HasGesture 为真但不回显出拳值。
        /// </summary>
        [Fact]
        public async Task SubmitGesture_OneSide_ShouldHideGesture()
        {
            var harness = new Harness();
            await harness.Bridge.StartRoomGameAsync(harness.ScopeOf(PlayerOne), RoomId, 1, harness.Players);

            var submitted = await harness.Bridge.SubmitGestureAsync(harness.ScopeOf(PlayerOne), RoomId, 1, (int)OnlineRockPaperScissorsMove.Rock);

            Assert.True(submitted.IsSuccess, submitted.Message);
            var mover = submitted.Data.Players.Find(item => item.PlayerId == PlayerOne);
            var other = submitted.Data.Players.Find(item => item.PlayerId == PlayerTwo);
            Assert.True(mover.HasGesture);
            Assert.Equal(0, mover.Gesture);
            Assert.False(other.HasGesture);
            Assert.Equal(1, submitted.Data.Round);
        }

        /// <summary>
        /// 验证双方出拳后回合结算：真实出拳回显、局数推进、胜局计入。
        /// </summary>
        [Fact]
        public async Task SubmitGesture_BothSides_ShouldResolveRoundAndReveal()
        {
            var harness = new Harness();
            await harness.Bridge.StartRoomGameAsync(harness.ScopeOf(PlayerOne), RoomId, 1, harness.Players);
            await harness.Bridge.SubmitGestureAsync(harness.ScopeOf(PlayerOne), RoomId, 1, (int)OnlineRockPaperScissorsMove.Rock);

            var submitted = await harness.Bridge.SubmitGestureAsync(harness.ScopeOf(PlayerTwo), RoomId, 1, (int)OnlineRockPaperScissorsMove.Scissors);

            Assert.True(submitted.IsSuccess, submitted.Message);
            Assert.Equal(2, submitted.Data.Round);
            var winner = submitted.Data.Players.Find(item => item.PlayerId == PlayerOne);
            var loser = submitted.Data.Players.Find(item => item.PlayerId == PlayerTwo);
            Assert.Equal((int)OnlineRockPaperScissorsMove.Rock, winner.Gesture);
            Assert.Equal((int)OnlineRockPaperScissorsMove.Scissors, loser.Gesture);
            Assert.Equal(1, winner.Wins);
            Assert.Equal(0, loser.Wins);
            Assert.Equal(0, submitted.Data.WinnerPlayerId);
        }

        /// <summary>
        /// 验证同玩家本局重复出拳被拒绝（DuplicateRequest）。
        /// </summary>
        [Fact]
        public async Task SubmitGesture_DuplicateInRound_ShouldBeDuplicateRequest()
        {
            var harness = new Harness();
            await harness.Bridge.StartRoomGameAsync(harness.ScopeOf(PlayerOne), RoomId, 1, harness.Players);
            var first = await harness.Bridge.SubmitGestureAsync(harness.ScopeOf(PlayerOne), RoomId, 1, (int)OnlineRockPaperScissorsMove.Rock);
            Assert.True(first.IsSuccess, first.Message);

            var second = await harness.Bridge.SubmitGestureAsync(harness.ScopeOf(PlayerOne), RoomId, 1, (int)OnlineRockPaperScissorsMove.Paper);

            Assert.False(second.IsSuccess);
            Assert.Equal(OnlineErrorCode.DuplicateRequest, second.Code);
        }

        /// <summary>
        /// 验证非成员查询被拒绝。
        /// </summary>
        [Fact]
        public async Task GetGameInfo_NonMember_ShouldBeScopeDenied()
        {
            var harness = new Harness();
            await harness.Bridge.StartRoomGameAsync(harness.ScopeOf(PlayerOne), RoomId, 1, harness.Players);

            var info = await harness.Bridge.GetGameInfoAsync(harness.ScopeOf(Outsider), RoomId, 1);

            Assert.False(info.IsSuccess);
            Assert.Equal(OnlineErrorCode.ScopeDenied, info.Code);
        }

        /// <summary>
        /// 验证对局不存在返回 ResourceNotFound。
        /// </summary>
        [Fact]
        public async Task GetGameInfo_NoMatch_ShouldBeResourceNotFound()
        {
            var harness = new Harness();

            var info = await harness.Bridge.GetGameInfoAsync(harness.ScopeOf(PlayerOne), RoomId, 9);

            Assert.False(info.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, info.Code);
        }

        /// <summary>
        /// 验证旧局终态后同局号再开局被拒绝（StateEnded——对局不复活）。
        /// </summary>
        [Fact]
        public async Task StartRoomGame_AfterMatchEnded_SameRound_ShouldBeStateEnded()
        {
            var harness = new Harness();
            await harness.PlayToCompletionAsync();

            var restarted = await harness.Bridge.StartRoomGameAsync(harness.ScopeOf(PlayerOne), RoomId, 1, harness.Players);

            Assert.False(restarted.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateEnded, restarted.Code);
        }

        /// <summary>
        /// 验证新局号开局成功：确定性派生新 MatchId（语义 = 新对局）。
        /// </summary>
        [Fact]
        public async Task StartRoomGame_NewRound_ShouldCreateNewMatch()
        {
            var harness = new Harness();
            var first = await harness.Bridge.StartRoomGameAsync(harness.ScopeOf(PlayerOne), RoomId, 1, harness.Players);
            await harness.PlayToCompletionAsync();

            var restarted = await harness.Bridge.StartRoomGameAsync(harness.ScopeOf(PlayerOne), RoomId, 2, harness.Players);

            Assert.True(restarted.IsSuccess, restarted.Message);
            Assert.Equal("rps-s100-r5001-g2", restarted.Data.MatchId);
            Assert.NotEqual(first.Data.MatchId, restarted.Data.MatchId);
            Assert.Equal(OnlineMatchBridgeGameState.Running, restarted.Data.State);
        }

        /// <summary>
        /// 测试基座：复用结算测试装配（真实资产域 + InMemory 对局域），桥服务挂接运行时。
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// 初始化测试基座。
            /// </summary>
            public Harness()
            {
                Publisher = new OnlineEventRecorder();
                Runtime = new OnlineMatchRuntime(new InMemoryOnlineMatchActorStore(), Publisher, new OnlineMatchRuntimeOptions());
                Game = new OnlineRockPaperScissorsGame();
                Runtime.RegisterGame(Game);
                Settlement = new OnlineMatchSettlementService(Runtime, new InMemoryOnlineMatchResultStore(), Publisher);
                Bridge = new OnlineMatchGameBridgeService(Runtime, Game);
                Players = new List<long> { PlayerOne, PlayerTwo };
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

            /// <summary>获取样例玩法。</summary>
            public OnlineRockPaperScissorsGame Game
            {
                get;
            }

            /// <summary>获取结算服务。</summary>
            public OnlineMatchSettlementService Settlement
            {
                get;
            }

            /// <summary>获取对局桥服务。</summary>
            public OnlineMatchGameBridgeService Bridge
            {
                get;
            }

            /// <summary>获取玩家集合。</summary>
            public List<long> Players
            {
                get;
            }

            /// <summary>
            /// 经桥打完一场对局（玩家一 2:0 获胜）并结算收口为 Completed 终态。
            /// </summary>
            /// <returns>完成通知。</returns>
            public async Task PlayToCompletionAsync()
            {
                Assert.True((await Bridge.StartRoomGameAsync(ScopeOf(PlayerOne), RoomId, 1, Players)).IsSuccess);
                Assert.True((await Bridge.SubmitGestureAsync(ScopeOf(PlayerOne), RoomId, 1, (int)OnlineRockPaperScissorsMove.Rock)).IsSuccess);
                Assert.True((await Bridge.SubmitGestureAsync(ScopeOf(PlayerTwo), RoomId, 1, (int)OnlineRockPaperScissorsMove.Scissors)).IsSuccess);
                Assert.True((await Bridge.SubmitGestureAsync(ScopeOf(PlayerOne), RoomId, 1, (int)OnlineRockPaperScissorsMove.Paper)).IsSuccess);
                Assert.True((await Bridge.SubmitGestureAsync(ScopeOf(PlayerTwo), RoomId, 1, (int)OnlineRockPaperScissorsMove.Rock)).IsSuccess);

                var settled = await Settlement.SettleAsync(TenantId, AppId, "rps-s100-r5001-g1", 2000000L);
                Assert.True(settled.IsSuccess, settled.Message);
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
