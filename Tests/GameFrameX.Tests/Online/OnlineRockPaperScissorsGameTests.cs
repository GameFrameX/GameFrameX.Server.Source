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
using System.Text.Json;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Match;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 石头剪刀布样例玩法测试（vault:C6 S5.4 / VC-5.12：服务端裁决胜负、超时按规则推进、
    /// 结算只依赖服务端状态）。
    /// </summary>
    public class OnlineRockPaperScissorsGameTests
    {
        /// <summary>玩家一。</summary>
        private const long PlayerOne = 1001;

        /// <summary>玩家二。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>基准时刻（UTC 毫秒）。</summary>
        private const long Now = 1000000L;

        /// <summary>
        /// 验证 S5.4：初始状态为两名玩家、第 1 局、无出拳。
        /// </summary>
        [Fact]
        public void CreateInitialState_ShouldSeedBothPlayers()
        {
            var game = new OnlineRockPaperScissorsGame();
            var match = BuildMatch();

            var state = Deserialize(game.CreateInitialState(match));

            Assert.Equal(1, state.Round);
            Assert.Equal(2, state.Players.Count);
            Assert.Equal(0, state.RoundDeadlineTime);
            Assert.All(state.Players, item => Assert.Equal(0, item.Wins));
        }

        /// <summary>
        /// 验证 VC-5.12：双方出拳后由服务端裁决胜负并计分。
        /// </summary>
        [Fact]
        public void ApplyInput_BothSubmitted_ShouldResolveRound()
        {
            var game = new OnlineRockPaperScissorsGame();
            var match = BuildMatch();

            var firstStep = game.ApplyInput(match, BuildMember(PlayerOne), BuildInput(PlayerOne, OnlineRockPaperScissorsMove.Rock));
            Assert.True(firstStep.Accepted, firstStep.Message);
            match.GameState = firstStep.GameState;

            // 只出一手时不得结算（也不得泄露对手出拳）。
            var secondStep = game.ApplyInput(match, BuildMember(PlayerTwo), BuildInput(PlayerTwo, OnlineRockPaperScissorsMove.Scissors));
            Assert.True(secondStep.Accepted, secondStep.Message);
            match.GameState = secondStep.GameState;

            var state = Deserialize(match.GameState);
            Assert.Equal(PlayerOne, state.WinnerPlayerId);
            Assert.Equal(2, state.Round);
            Assert.Equal(1, state.Players.Find(item => item.PlayerId == PlayerOne).Wins);
            Assert.Equal(0, state.Players.Find(item => item.PlayerId == PlayerTwo).Wins);
            Assert.All(state.Players, item => Assert.Equal(OnlineRockPaperScissorsMove.None, item.Gesture));
            Assert.Contains(secondStep.ServerEvents, item => item.EventType == "RoundResolved");
        }

        /// <summary>
        /// 验证 VC-5.3：同一局重复出拳被拒（客户端不得改主意）。
        /// </summary>
        [Fact]
        public void ApplyInput_TwiceInSameRound_ShouldBeRejected()
        {
            var game = new OnlineRockPaperScissorsGame();
            var match = BuildMatch();
            var firstStep = game.ApplyInput(match, BuildMember(PlayerOne), BuildInput(PlayerOne, OnlineRockPaperScissorsMove.Rock));
            match.GameState = firstStep.GameState;

            var secondStep = game.ApplyInput(match, BuildMember(PlayerOne), BuildInput(PlayerOne, OnlineRockPaperScissorsMove.Paper));

            Assert.False(secondStep.Accepted);
            Assert.Equal(OnlineMatchInputRejection.Duplicate, secondStep.Rejection);
        }

        /// <summary>
        /// 验证 VC-5.2：越出玩法契约的出拳值被拒。
        /// </summary>
        [Fact]
        public void ApplyInput_OutOfContractGesture_ShouldBeRejected()
        {
            var game = new OnlineRockPaperScissorsGame();
            var match = BuildMatch();
            var input = BuildInput(PlayerOne, OnlineRockPaperScissorsMove.None);
            input.ActionId = 42;

            var step = game.ApplyInput(match, BuildMember(PlayerOne), input);

            Assert.False(step.Accepted);
            Assert.Equal(OnlineMatchInputRejection.IllegalAction, step.Rejection);
        }

        /// <summary>
        /// 验证 VC-5.12：超时未出拳方判负，已出拳方赢下该局。
        /// </summary>
        [Fact]
        public void Advance_OneSubmitted_ShouldAwardRoundByTimeout()
        {
            var game = new OnlineRockPaperScissorsGame();
            var match = BuildMatch();

            var step = game.ApplyInput(match, BuildMember(PlayerOne), BuildInput(PlayerOne, OnlineRockPaperScissorsMove.Rock));
            match.GameState = step.GameState;

            var emitted = new List<OnlineMatchServerEvent>();
            Assert.True(game.Advance(match, Now, emitted));
            Assert.Empty(emitted);

            Assert.False(game.Advance(match, Now + OnlineRockPaperScissorsGame.RoundTimeoutMilliseconds - 1, emitted));

            Assert.True(game.Advance(match, Now + OnlineRockPaperScissorsGame.RoundTimeoutMilliseconds + 1, emitted));
            var resolved = Assert.Single(emitted);
            Assert.Equal("RoundResolved", resolved.EventType);

            var state = Deserialize(match.GameState);
            Assert.Equal(1, state.Players.Find(item => item.PlayerId == PlayerOne).Wins);
        }

        /// <summary>
        /// 验证 VC-5.12：双方均不出拳时对局仍收敛到确定结果（局数耗尽 → 平局），不永久挂起。
        /// </summary>
        [Fact]
        public void Advance_NoSubmission_ShouldConvergeToDraw()
        {
            var game = new OnlineRockPaperScissorsGame();
            var match = BuildMatch();

            var now = Now;
            for (var round = 0; round < OnlineRockPaperScissorsGame.MaxRounds; round++)
            {
                var emitted = new List<OnlineMatchServerEvent>();
                Assert.True(game.Advance(match, now, emitted));

                now += OnlineRockPaperScissorsGame.RoundTimeoutMilliseconds + 1;
                Assert.True(game.Advance(match, now, emitted));
                Assert.Single(emitted);
            }

            Assert.True(game.IsCompleted(match));

            var result = game.BuildResult(match);
            Assert.Equal(2, result.Entries.Count);
            Assert.All(result.Entries, item => Assert.False(item.IsWinner));
            Assert.All(result.Entries, item => Assert.Equal(1, item.Rank));
            Assert.All(result.Entries, item => Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, item.Rewards[0].Amount));
        }

        /// <summary>
        /// 验证 S5.7 前置：先胜两局即终结，结算条目名次与奖励匹配。
        /// </summary>
        [Fact]
        public void BuildResult_WinnerTakesTwoRounds()
        {
            var game = new OnlineRockPaperScissorsGame();
            var match = BuildMatch();
            Play(match, game, 1, OnlineRockPaperScissorsMove.Paper, OnlineRockPaperScissorsMove.Rock);
            Play(match, game, 2, OnlineRockPaperScissorsMove.Scissors, OnlineRockPaperScissorsMove.Paper);

            Assert.True(game.IsCompleted(match));

            var result = game.BuildResult(match);
            var winner = result.Entries.Find(item => item.PlayerId == PlayerOne);
            var loser = result.Entries.Find(item => item.PlayerId == PlayerTwo);

            Assert.True(winner.IsWinner);
            Assert.Equal(1, winner.Rank);
            Assert.Equal(2, winner.Score);
            Assert.Equal(OnlineAssetKind.Currency, winner.Rewards[0].AssetKind);
            Assert.Equal(OnlineRockPaperScissorsGame.RewardAssetId, winner.Rewards[0].AssetId);
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, winner.Rewards[0].Amount);
            Assert.False(loser.IsWinner);
            Assert.Equal(2, loser.Rank);
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, loser.Rewards[0].Amount);
        }

        /// <summary>
        /// 验证 S5.4：玩家数不足玩法下限时不得开局（由 Actor 依据 MinPlayers 判定）。
        /// </summary>
        [Fact]
        public void ModeContract_ShouldBeTwoPlayerMatch()
        {
            var game = new OnlineRockPaperScissorsGame();

            Assert.Equal(OnlineRockPaperScissorsGame.MatchMode, game.Mode);
            Assert.Equal(2, game.MinPlayers);
            Assert.Equal(2, game.MaxPlayers);
            Assert.Equal(3, OnlineRockPaperScissorsGame.MaxRounds);
        }

        /// <summary>
        /// 打出一局（双方各出拳一次）。
        /// </summary>
        /// <param name="match">对局。</param>
        /// <param name="game">玩法。</param>
        /// <param name="clientSequence">客户端序号（本测试不校验序号，仅保证递增）。</param>
        /// <param name="firstMove">玩家一出拳。</param>
        /// <param name="secondMove">玩家二出拳。</param>
        private static void Play(OnlineMatch match, OnlineRockPaperScissorsGame game, int clientSequence, OnlineRockPaperScissorsMove firstMove, OnlineRockPaperScissorsMove secondMove)
        {
            var firstInput = BuildInput(PlayerOne, firstMove);
            firstInput.ClientSequence = clientSequence;
            var firstStep = game.ApplyInput(match, BuildMember(PlayerOne), firstInput);
            Assert.True(firstStep.Accepted, firstStep.Message);
            match.GameState = firstStep.GameState;

            var secondInput = BuildInput(PlayerTwo, secondMove);
            secondInput.ClientSequence = clientSequence;
            var secondStep = game.ApplyInput(match, BuildMember(PlayerTwo), secondInput);
            Assert.True(secondStep.Accepted, secondStep.Message);
            match.GameState = secondStep.GameState;
        }

        /// <summary>
        /// 构造含两名成员的对局（已进入运行阶段，玩法状态由 <see cref="IOnlineMatchGame.CreateInitialState"/> 产出）。
        /// </summary>
        /// <returns>对局实例。</returns>
        private static OnlineMatch BuildMatch()
        {
            var match = new OnlineMatch
            {
                TenantId = 1,
                AppId = 10,
                ServerId = 100,
                MatchId = "match-1",
                Mode = OnlineRockPaperScissorsGame.MatchMode,
                Region = 1,
                State = OnlineMatchState.Running,
                Members = new List<OnlineMatchMember>
                {
                    BuildMember(PlayerOne),
                    BuildMember(PlayerTwo),
                },
                Events = new List<OnlineMatchServerEvent>(),
            };

            match.GameState = new OnlineRockPaperScissorsGame().CreateInitialState(match);
            return match;
        }

        /// <summary>
        /// 构造成员。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>成员实例。</returns>
        private static OnlineMatchMember BuildMember(long playerId)
        {
            return new OnlineMatchMember
            {
                PlayerId = playerId,
                State = OnlineMatchMemberState.Playing,
            };
        }

        /// <summary>
        /// 构造输入意图。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="move">出拳。</param>
        /// <returns>输入实例。</returns>
        private static OnlineMatchInput BuildInput(long playerId, OnlineRockPaperScissorsMove move)
        {
            return new OnlineMatchInput
            {
                PlayerId = playerId,
                ClientSequence = 1,
                ActionId = (int)move,
                ClientTime = Now,
            };
        }

        /// <summary>
        /// 反序列化玩法状态（断言服务端内部状态用；该形态不下发客户端）。
        /// </summary>
        /// <param name="gameState">玩法状态字节。</param>
        /// <returns>玩法状态。</returns>
        private static OnlineRockPaperScissorsState Deserialize(byte[] gameState)
        {
            return JsonSerializer.Deserialize<OnlineRockPaperScissorsState>(gameState);
        }
    }
}
