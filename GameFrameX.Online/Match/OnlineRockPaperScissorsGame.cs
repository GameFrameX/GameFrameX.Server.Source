//  ==========================================================================================
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
//  ==========================================================================================

using System.Collections.Generic;
using System.Text.Json;
using GameFrameX.Online.Assets;

namespace GameFrameX.Online.Match;

/// <summary>
/// 石头剪刀布样例玩法（vault:C6「Online Sample：使用石头剪刀布或小型回合制玩法作为第一版样例」）。
/// <para>
/// 规则：两人对局，三局两胜（先得 <see cref="WinTarget"/> 局者胜，最多 <see cref="MaxRounds"/> 局）；
/// 每局双方各提交一次出拳，服务端判定胜负；单局无人出拳则本局作废。
/// 单局超时（<see cref="RoundTimeoutMilliseconds"/>）由服务端按规则推进：已出拳方判该局胜，双方均未出拳则本局作废——
/// 这使「玩家不做操作」也收敛到确定结果，而非永久挂起（VC-5.12）。
/// </para>
/// <para>
/// 维护约束（红线）：本类不做任何鉴权与身份判定（由 Actor 负责），也不产出 <c>MatchResultId</c>
/// （由结算服务统一分配）——玩法只回答「这一手合法吗、现在谁赢了」。
/// </para>
/// </summary>
public sealed class OnlineRockPaperScissorsGame : IOnlineMatchGame
{
    /// <summary>模式标识（与现网客户端契约 <c>_0410_RockPaperScissors</c> 对齐）。</summary>
    public const int MatchMode = 410;

    /// <summary>获胜所需局数。</summary>
    public const int WinTarget = 2;

    /// <summary>最大局数（先胜 <see cref="WinTarget"/> 局即终局）。</summary>
    public const int MaxRounds = (WinTarget * 2) - 1;

    /// <summary>单局时限（毫秒）。</summary>
    public const long RoundTimeoutMilliseconds = 30000L;

    /// <summary>样例奖励资产标识。</summary>
    public const string RewardAssetId = "sample-coin";

    /// <summary>胜方奖励数量。</summary>
    public const long WinnerRewardAmount = 100L;

    /// <summary>参与奖数量（负方与平局同样发放，用于覆盖逐玩家独立发奖路径）。</summary>
    public const long ParticipantRewardAmount = 10L;

    /// <summary>出拳提交事件。</summary>
    private const string GestureSubmittedEventType = "GestureSubmitted";

    /// <summary>单局结算事件。</summary>
    private const string RoundResolvedEventType = "RoundResolved";

    /// <summary>
    /// 获取模式标识。
    /// </summary>
    public int Mode
    {
        get
        {
            return MatchMode;
        }
    }

    /// <summary>
    /// 获取最小玩家数。
    /// </summary>
    public int MinPlayers
    {
        get
        {
            return 2;
        }
    }

    /// <summary>
    /// 获取最大玩家数。
    /// </summary>
    public int MaxPlayers
    {
        get
        {
            return 2;
        }
    }

    /// <summary>
    /// 构造初始玩法状态：为全部在局成员建计分位，进入第 1 局。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <returns>序列化后的初始状态。</returns>
    public byte[] CreateInitialState(OnlineMatch match)
    {
        var state = new OnlineRockPaperScissorsState
        {
            Round = 1,
            WinnerPlayerId = 0,
            RoundDeadlineTime = 0,
            Players = new List<OnlineRockPaperScissorsState.OnlineRockPaperScissorsPlayerState>(),
        };

        if (match.Members != null)
        {
            foreach (var member in match.Members)
            {
                if (member == null || member.State == OnlineMatchMemberState.Left || member.State == OnlineMatchMemberState.Kicked)
                {
                    continue;
                }

                state.Players.Add(new OnlineRockPaperScissorsState.OnlineRockPaperScissorsPlayerState
                {
                    PlayerId = member.PlayerId,
                    Wins = 0,
                    Gesture = OnlineRockPaperScissorsMove.None,
                });
            }
        }

        return Serialize(state);
    }

    /// <summary>
    /// 处理一次出拳意图（只接受本局未出拳的成员；出拳值非契约枚举一律拒绝）。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <param name="member">提交成员（身份已由 Actor 校验）。</param>
    /// <param name="input">输入意图（<see cref="OnlineMatchInput.ActionId"/> = 出拳值）。</param>
    /// <returns>处理结果。</returns>
    public OnlineMatchGameStepResult ApplyInput(OnlineMatch match, OnlineMatchMember member, OnlineMatchInput input)
    {
        var state = Deserialize(match.GameState);
        if (state == null || state.Players == null || state.Players.Count == 0)
        {
            return Reject(OnlineMatchInputRejection.IllegalAction, "对局状态不可用");
        }

        var me = FindPlayer(state, member.PlayerId);
        if (me == null)
        {
            return Reject(OnlineMatchInputRejection.NotMember, "玩家不在本局计分表中");
        }

        if (!IsGestureValid(input.ActionId))
        {
            return Reject(OnlineMatchInputRejection.IllegalAction, "非法出拳");
        }

        if (me.Gesture != OnlineRockPaperScissorsMove.None)
        {
            return Reject(OnlineMatchInputRejection.Duplicate, "本局已提交过出拳");
        }

        me.Gesture = (OnlineRockPaperScissorsMove)input.ActionId;

        var submittedCount = 0;
        foreach (var player in state.Players)
        {
            if (player.Gesture != OnlineRockPaperScissorsMove.None)
            {
                submittedCount++;
            }
        }

        var emittedEvents = new List<OnlineMatchServerEvent>
        {
            BuildEvent(GestureSubmittedEventType, new GestureSubmittedPayload
            {
                Round = state.Round,
                PlayerId = member.PlayerId,
                SubmittedCount = submittedCount,
                PlayerCount = state.Players.Count,
            }),
        };

        if (submittedCount >= state.Players.Count)
        {
            emittedEvents.Add(BuildEvent(RoundResolvedEventType, SettleRound(state, ResolveWinner(state), false)));
        }

        return new OnlineMatchGameStepResult
        {
            Accepted = true,
            Rejection = OnlineMatchInputRejection.None,
            GameState = Serialize(state),
            ServerEvents = emittedEvents,
            Message = "已接受",
        };
    }

    /// <summary>
    /// 时间推进：布防回合时钟；到点按超时规则判定本局（已出拳方胜，双方未出拳则本局作废）。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="emittedEvents">本次推进要追加的服务器事件。</param>
    /// <returns>状态是否被改写。</returns>
    public bool Advance(OnlineMatch match, long nowUnixMilliseconds, List<OnlineMatchServerEvent> emittedEvents)
    {
        var state = Deserialize(match.GameState);
        if (state == null || state.Players == null || state.Players.Count == 0)
        {
            return false;
        }

        if (state.RoundDeadlineTime == 0)
        {
            state.RoundDeadlineTime = nowUnixMilliseconds + RoundTimeoutMilliseconds;
            match.GameState = Serialize(state);
            return true;
        }

        if (nowUnixMilliseconds < state.RoundDeadlineTime)
        {
            return false;
        }

        emittedEvents.Add(BuildEvent(RoundResolvedEventType, SettleRound(state, ResolveWinner(state), true)));
        match.GameState = Serialize(state);
        return true;
    }

    /// <summary>
    /// 判定是否已分出结果（有人达到胜局数，或局数已超过上限）。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <returns>已分出结果返回 <c>true</c>。</returns>
    public bool IsCompleted(OnlineMatch match)
    {
        var state = Deserialize(match.GameState);
        if (state == null || state.Players == null || state.Players.Count == 0)
        {
            return false;
        }

        if (state.Round > MaxRounds)
        {
            return true;
        }

        foreach (var player in state.Players)
        {
            if (player.Wins >= WinTarget)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 产出结算结果：名次按胜局数，胜方得胜方奖励、负方与平局得参与奖。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <returns>结算结果；状态缺失返回 null。</returns>
    public OnlineMatchResult BuildResult(OnlineMatch match)
    {
        var state = Deserialize(match.GameState);
        if (state == null || state.Players == null || state.Players.Count == 0)
        {
            return null;
        }

        var winnerPlayerId = FindWinner(state);
        var result = new OnlineMatchResult
        {
            Mode = match.Mode,
            Region = match.Region,
            Entries = new List<OnlineMatchResultEntry>(),
        };

        foreach (var player in state.Players)
        {
            var isWinner = winnerPlayerId != 0 && player.PlayerId == winnerPlayerId;
            result.Entries.Add(new OnlineMatchResultEntry
            {
                PlayerId = player.PlayerId,
                Rank = winnerPlayerId == 0 || isWinner ? 1 : 2,
                IsWinner = isWinner,
                Score = player.Wins,
                Rewards = new List<OnlineAssetChangeLine>
                {
                    new OnlineAssetChangeLine(
                        OnlineAssetKind.Currency,
                        RewardAssetId,
                        isWinner ? WinnerRewardAmount : ParticipantRewardAmount),
                },
            });
        }

        return result;
    }

    /// <summary>
    /// 解析本局胜负：仅出一拳者胜；两拳相同为平局；否则按克制关系判定。
    /// </summary>
    /// <param name="state">玩法状态。</param>
    /// <returns>胜者玩家标识；平局或无法判定返回 0。</returns>
    private static long ResolveWinner(OnlineRockPaperScissorsState state)
    {
        if (state.Players.Count < 2)
        {
            return 0;
        }

        var first = state.Players[0];
        var second = state.Players[1];

        if (first.Gesture == OnlineRockPaperScissorsMove.None && second.Gesture == OnlineRockPaperScissorsMove.None)
        {
            return 0;
        }

        if (first.Gesture == OnlineRockPaperScissorsMove.None)
        {
            return second.PlayerId;
        }

        if (second.Gesture == OnlineRockPaperScissorsMove.None)
        {
            return first.PlayerId;
        }

        if (first.Gesture == second.Gesture)
        {
            return 0;
        }

        return Beats(first.Gesture, second.Gesture) ? first.PlayerId : second.PlayerId;
    }

    /// <summary>
    /// 收束本局：计分、清空出拳、进入下一局（并解除回合时钟布防）。
    /// </summary>
    /// <param name="state">玩法状态。</param>
    /// <param name="winnerPlayerId">本局胜者（0 = 平局）。</param>
    /// <param name="timedOut">是否由超时触发。</param>
    /// <returns>本局结算事件载荷。</returns>
    private static RoundResolvedPayload SettleRound(OnlineRockPaperScissorsState state, long winnerPlayerId, bool timedOut)
    {
        var resolvedRound = state.Round;
        var plays = new List<RoundPlayPayload>();
        var scores = new List<RoundScorePayload>();
        foreach (var player in state.Players)
        {
            plays.Add(new RoundPlayPayload
            {
                PlayerId = player.PlayerId,
                Gesture = player.Gesture,
            });

            if (winnerPlayerId != 0 && player.PlayerId == winnerPlayerId)
            {
                player.Wins++;
            }
        }

        state.WinnerPlayerId = winnerPlayerId;
        state.Round = resolvedRound + 1;
        state.RoundDeadlineTime = 0;

        foreach (var player in state.Players)
        {
            scores.Add(new RoundScorePayload
            {
                PlayerId = player.PlayerId,
                Wins = player.Wins,
            });

            player.Gesture = OnlineRockPaperScissorsMove.None;
        }

        return new RoundResolvedPayload
        {
            Round = resolvedRound,
            WinnerPlayerId = winnerPlayerId,
            TimedOut = timedOut,
            Plays = plays,
            Scores = scores,
        };
    }

    /// <summary>
    /// 判定胜负并返回胜者标识（平局返回 0）。
    /// </summary>
    /// <param name="state">玩法状态。</param>
    /// <returns>胜者玩家标识；平局返回 0。</returns>
    private static long FindWinner(OnlineRockPaperScissorsState state)
    {
        long winnerPlayerId = 0;
        var bestWins = -1;
        var bestCount = 0;
        foreach (var player in state.Players)
        {
            if (player.Wins > bestWins)
            {
                bestWins = player.Wins;
                winnerPlayerId = player.PlayerId;
                bestCount = 1;
            }
            else if (player.Wins == bestWins)
            {
                bestCount++;
            }
        }

        return bestCount > 1 || bestWins <= 0 ? 0 : winnerPlayerId;
    }

    /// <summary>
    /// 判定出拳值是否在契约枚举范围内。
    /// </summary>
    /// <param name="actionId">出拳值。</param>
    /// <returns>合法返回 <c>true</c>。</returns>
    private static bool IsGestureValid(int actionId)
    {
        return actionId >= (int)OnlineRockPaperScissorsMove.Rock && actionId <= (int)OnlineRockPaperScissorsMove.Paper;
    }

    /// <summary>
    /// 判定 <paramref name="left"/> 是否克制 <paramref name="right"/>。
    /// </summary>
    /// <param name="left">左方出拳。</param>
    /// <param name="right">右方出拳。</param>
    /// <returns>左方胜返回 <c>true</c>。</returns>
    private static bool Beats(OnlineRockPaperScissorsMove left, OnlineRockPaperScissorsMove right)
    {
        return (left == OnlineRockPaperScissorsMove.Rock && right == OnlineRockPaperScissorsMove.Scissors)
            || (left == OnlineRockPaperScissorsMove.Scissors && right == OnlineRockPaperScissorsMove.Paper)
            || (left == OnlineRockPaperScissorsMove.Paper && right == OnlineRockPaperScissorsMove.Rock);
    }

    /// <summary>
    /// 在计分表中查找玩家。
    /// </summary>
    /// <param name="state">玩法状态。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>玩家状态；不存在返回 null。</returns>
    private static OnlineRockPaperScissorsState.OnlineRockPaperScissorsPlayerState FindPlayer(OnlineRockPaperScissorsState state, long playerId)
    {
        foreach (var player in state.Players)
        {
            if (player.PlayerId == playerId)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>
    /// 构造拒绝结果。
    /// </summary>
    /// <param name="rejection">拒绝原因。</param>
    /// <param name="message">说明。</param>
    /// <returns>处理结果。</returns>
    private static OnlineMatchGameStepResult Reject(OnlineMatchInputRejection rejection, string message)
    {
        return new OnlineMatchGameStepResult
        {
            Accepted = false,
            Rejection = rejection,
            Message = message,
        };
    }

    /// <summary>
    /// 构造服务器事件（序号与发生时刻由 Actor 统一分配）。
    /// </summary>
    /// <typeparam name="TPayload">载荷类型。</typeparam>
    /// <param name="eventType">事件类型。</param>
    /// <param name="payload">载荷。</param>
    /// <returns>服务器事件。</returns>
    private static OnlineMatchServerEvent BuildEvent<TPayload>(string eventType, TPayload payload)
    {
        return new OnlineMatchServerEvent
        {
            EventType = eventType,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
        };
    }

    /// <summary>
    /// 序列化玩法状态。
    /// </summary>
    /// <param name="state">玩法状态。</param>
    /// <returns>字节数组。</returns>
    private static byte[] Serialize(OnlineRockPaperScissorsState state)
    {
        return JsonSerializer.SerializeToUtf8Bytes(state);
    }

    /// <summary>
    /// 反序列化玩法状态。
    /// </summary>
    /// <param name="gameState">字节数组。</param>
    /// <returns>玩法状态；为空或损坏返回 null。</returns>
    private static OnlineRockPaperScissorsState Deserialize(byte[] gameState)
    {
        if (gameState == null || gameState.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OnlineRockPaperScissorsState>(gameState);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 出拳提交事件负载（不含出拳值——未结算前不得泄露，防窥屏）。
    /// </summary>
    private sealed class GestureSubmittedPayload
    {
        /// <summary>当前局数。</summary>
        public int Round
        {
            get;
            set;
        }

        /// <summary>提交者玩家标识。</summary>
        public long PlayerId
        {
            get;
            set;
        }

        /// <summary>本局已提交人数。</summary>
        public int SubmittedCount
        {
            get;
            set;
        }

        /// <summary>本局参与人数。</summary>
        public int PlayerCount
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 单局结算事件负载（已结算，故可携带真实出拳）。
    /// </summary>
    private sealed class RoundResolvedPayload
    {
        /// <summary>被结算的局数。</summary>
        public int Round
        {
            get;
            set;
        }

        /// <summary>本局胜者（0 = 平局）。</summary>
        public long WinnerPlayerId
        {
            get;
            set;
        }

        /// <summary>是否由超时触发。</summary>
        public bool TimedOut
        {
            get;
            set;
        }

        /// <summary>本局双方出拳。</summary>
        public List<RoundPlayPayload> Plays
        {
            get;
            set;
        }

        /// <summary>结算后的累计胜局。</summary>
        public List<RoundScorePayload> Scores
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 单局出拳负载。
    /// </summary>
    private sealed class RoundPlayPayload
    {
        /// <summary>玩家标识。</summary>
        public long PlayerId
        {
            get;
            set;
        }

        /// <summary>出拳。</summary>
        public OnlineRockPaperScissorsMove Gesture
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 结算后计分负载。
    /// </summary>
    private sealed class RoundScorePayload
    {
        /// <summary>玩家标识。</summary>
        public long PlayerId
        {
            get;
            set;
        }

        /// <summary>累计胜局。</summary>
        public int Wins
        {
            get;
            set;
        }
    }
}
