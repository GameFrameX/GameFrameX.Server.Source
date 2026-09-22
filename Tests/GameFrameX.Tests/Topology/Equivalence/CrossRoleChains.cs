// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.NetWork.RemoteMessaging.Routing;

namespace GameFrameX.Tests.Topology.Equivalence;

/// <summary>
/// 语义等价用例集的 4 条代表性跨 Role 消息链路（C143c D9）。
/// </summary>
/// <remarks>
/// The four representative cross-role chains of the equivalence suite (C143c D9):
/// Gate→Game session establishment, Game→Social friend query, Game→Match match request
/// and Match→Game settlement callback. Every chain installs its role handlers on the
/// world, then issues three sequential request round trips (deterministic player ids),
/// so receive order and final state are fully deterministic and comparable across
/// topologies. Reply hops are routed from inside the request handler — the same
/// request/reply shape business code will use through the routing seam.
/// All state values are derived deterministically from the request ids: equivalence
/// assertions compare fixed strings, never random values.
/// </remarks>
public static class CrossRoleChains
{
    /// <summary>
    /// Gate Role 名。
    /// </summary>
    public const string GateRoleName = "Gate";

    /// <summary>
    /// Game Role 名。
    /// </summary>
    public const string GameRoleName = "Game";

    /// <summary>
    /// Social Role 名。
    /// </summary>
    public const string SocialRoleName = "Social";

    /// <summary>
    /// Match Role 名。
    /// </summary>
    public const string MatchRoleName = "Match";

    /// <summary>
    /// 链路使用的玩家 Id 序列（确定性）。
    /// </summary>
    public static readonly long[] ChainPlayerIds = new long[] { 1001, 1002, 1003 };

    /// <summary>
    /// 链路使用的对局 Id 序列（确定性）。
    /// </summary>
    public static readonly long[] ChainMatchIds = new long[] { 5001, 5002, 5003 };

    /// <summary>
    /// 链路一：Gate→Game 会话建立（Game 应答回 Gate）。
    /// </summary>
    /// <param name="world">拓扑世界 / The topology world</param>
    /// <param name="hopTimeoutMilliseconds">跳超时（毫秒）/ The hop timeout in milliseconds</param>
    /// <param name="gameHandlerDelayMilliseconds">Game 处理延时（毫秒，超时等价对用）/ The Game handler delay in milliseconds (for the timeout equivalence pair)</param>
    /// <returns>链路执行结果 / The chain execution result</returns>
    public static async Task<ChainExecutionResult> RunGateToGameSessionEstablishAsync(TopologyWorld world, int hopTimeoutMilliseconds, int gameHandlerDelayMilliseconds = 0)
    {
        var establishedPlayerIds = new List<long>();
        var acknowledgedPlayerIds = new List<long>();

        world.GetMailbox(GameRoleName).SetHandler(async envelope =>
        {
            if (envelope.Message is EquivalenceTestMessages.SessionEstablishRequest request)
            {
                if (gameHandlerDelayMilliseconds > 0)
                {
                    await Task.Delay(gameHandlerDelayMilliseconds);
                }

                establishedPlayerIds.Add(request.PlayerId);
                world.SetRoleState(GameRoleName, "SessionEstablished", string.Join(",", establishedPlayerIds));
                await world.RouteAsync(
                    GameRoleName,
                    GateRoleName,
                    new EquivalenceTestMessages.SessionEstablishResponse { PlayerId = request.PlayerId, Accepted = true },
                    hopTimeoutMilliseconds);
            }
        });

        world.GetMailbox(GateRoleName).SetHandler(envelope =>
        {
            if (envelope.Message is EquivalenceTestMessages.SessionEstablishResponse response)
            {
                acknowledgedPlayerIds.Add(response.PlayerId);
                world.SetRoleState(GateRoleName, "SessionAcknowledged", string.Join(",", acknowledgedPlayerIds));
            }

            return Task.CompletedTask;
        });

        foreach (var playerId in ChainPlayerIds)
        {
            await world.RouteAsync(
                GateRoleName,
                GameRoleName,
                new EquivalenceTestMessages.SessionEstablishRequest { PlayerId = playerId },
                hopTimeoutMilliseconds);
        }

        return world.CaptureResult();
    }

    /// <summary>
    /// 链路二：Game→Social 好友查询（Social 应答回 Game）。
    /// </summary>
    /// <param name="world">拓扑世界 / The topology world</param>
    /// <param name="hopTimeoutMilliseconds">跳超时（毫秒）/ The hop timeout in milliseconds</param>
    /// <param name="socialHandlerDelayMilliseconds">Social 处理延时（毫秒，超时等价对用）/ The Social handler delay in milliseconds (for the timeout equivalence pair)</param>
    /// <returns>链路执行结果 / The chain execution result</returns>
    public static async Task<ChainExecutionResult> RunGameToSocialFriendQueryAsync(TopologyWorld world, int hopTimeoutMilliseconds, int socialHandlerDelayMilliseconds = 0)
    {
        var servedPlayerIds = new List<long>();
        var receivedFriendLists = new List<string>();

        world.GetMailbox(SocialRoleName).SetHandler(async envelope =>
        {
            if (envelope.Message is EquivalenceTestMessages.FriendListRequest request)
            {
                if (socialHandlerDelayMilliseconds > 0)
                {
                    await Task.Delay(socialHandlerDelayMilliseconds);
                }

                servedPlayerIds.Add(request.PlayerId);
                world.SetRoleState(SocialRoleName, "FriendListServed", string.Join(",", servedPlayerIds));
                var friendPlayerIds = new long[] { request.PlayerId + 1000, request.PlayerId + 2000 };
                await world.RouteAsync(
                    SocialRoleName,
                    GameRoleName,
                    new EquivalenceTestMessages.FriendListResponse { PlayerId = request.PlayerId, FriendPlayerIds = friendPlayerIds },
                    hopTimeoutMilliseconds);
            }
        });

        world.GetMailbox(GameRoleName).SetHandler(envelope =>
        {
            if (envelope.Message is EquivalenceTestMessages.FriendListResponse response)
            {
                receivedFriendLists.Add($"{response.PlayerId}:{string.Join("+", response.FriendPlayerIds)}");
                world.SetRoleState(GameRoleName, "FriendQueryResult", string.Join(",", receivedFriendLists));
            }

            return Task.CompletedTask;
        });

        foreach (var playerId in ChainPlayerIds)
        {
            await world.RouteAsync(
                GameRoleName,
                SocialRoleName,
                new EquivalenceTestMessages.FriendListRequest { PlayerId = playerId },
                hopTimeoutMilliseconds);
        }

        return world.CaptureResult();
    }

    /// <summary>
    /// 链路三：Game→Match 匹配请求（Match 应答回 Game）。
    /// </summary>
    /// <param name="world">拓扑世界 / The topology world</param>
    /// <param name="hopTimeoutMilliseconds">跳超时（毫秒）/ The hop timeout in milliseconds</param>
    /// <returns>链路执行结果 / The chain execution result</returns>
    public static async Task<ChainExecutionResult> RunGameToMatchJoinRequestAsync(TopologyWorld world, int hopTimeoutMilliseconds)
    {
        var issuedTickets = new List<string>();
        var receivedTickets = new List<string>();

        world.GetMailbox(MatchRoleName).SetHandler(async envelope =>
        {
            if (envelope.Message is EquivalenceTestMessages.MatchJoinRequest request)
            {
                var ticketId = $"ticket-{request.PlayerId}";
                issuedTickets.Add($"{request.PlayerId}:{ticketId}");
                world.SetRoleState(MatchRoleName, "MatchTicketIssued", string.Join(",", issuedTickets));
                await world.RouteAsync(
                    MatchRoleName,
                    GameRoleName,
                    new EquivalenceTestMessages.MatchJoinResponse { PlayerId = request.PlayerId, TicketId = ticketId },
                    hopTimeoutMilliseconds);
            }
        });

        world.GetMailbox(GameRoleName).SetHandler(envelope =>
        {
            if (envelope.Message is EquivalenceTestMessages.MatchJoinResponse response)
            {
                receivedTickets.Add($"{response.PlayerId}:{response.TicketId}");
                world.SetRoleState(GameRoleName, "MatchTicketReceived", string.Join(",", receivedTickets));
            }

            return Task.CompletedTask;
        });

        foreach (var playerId in ChainPlayerIds)
        {
            await world.RouteAsync(
                GameRoleName,
                MatchRoleName,
                new EquivalenceTestMessages.MatchJoinRequest { PlayerId = playerId },
                hopTimeoutMilliseconds);
        }

        return world.CaptureResult();
    }

    /// <summary>
    /// 链路四：Match→Game 结算回调（Game 确认回 Match）。
    /// </summary>
    /// <param name="world">拓扑世界 / The topology world</param>
    /// <param name="hopTimeoutMilliseconds">跳超时（毫秒）/ The hop timeout in milliseconds</param>
    /// <returns>链路执行结果 / The chain execution result</returns>
    public static async Task<ChainExecutionResult> RunMatchToGameSettlementCallbackAsync(TopologyWorld world, int hopTimeoutMilliseconds)
    {
        var appliedSettlements = new List<string>();
        var acknowledgedSettlements = new List<string>();

        world.GetMailbox(GameRoleName).SetHandler(async envelope =>
        {
            if (envelope.Message is EquivalenceTestMessages.SettlementCallbackMessage callback)
            {
                appliedSettlements.Add($"{callback.MatchId}:{callback.WinnerPlayerId}");
                world.SetRoleState(GameRoleName, "SettlementApplied", string.Join(",", appliedSettlements));
                await world.RouteAsync(
                    GameRoleName,
                    MatchRoleName,
                    new EquivalenceTestMessages.SettlementAckMessage { MatchId = callback.MatchId, WinnerPlayerId = callback.WinnerPlayerId },
                    hopTimeoutMilliseconds);
            }
        });

        world.GetMailbox(MatchRoleName).SetHandler(envelope =>
        {
            if (envelope.Message is EquivalenceTestMessages.SettlementAckMessage acknowledgement)
            {
                acknowledgedSettlements.Add($"{acknowledgement.MatchId}:{acknowledgement.WinnerPlayerId}");
                world.SetRoleState(MatchRoleName, "SettlementAcknowledged", string.Join(",", acknowledgedSettlements));
            }

            return Task.CompletedTask;
        });

        for (var index = 0; index < ChainMatchIds.Length; index++)
        {
            await world.RouteAsync(
                MatchRoleName,
                GameRoleName,
                new EquivalenceTestMessages.SettlementCallbackMessage { MatchId = ChainMatchIds[index], WinnerPlayerId = 2000 + index },
                hopTimeoutMilliseconds);
        }

        return world.CaptureResult();
    }
}
