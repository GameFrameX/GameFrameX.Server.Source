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
/// 跨 Role 消息链路语义等价测试（C143c D9，AC-2）。
/// </summary>
/// <remarks>
/// Topology equivalence suite (C143c D9, AC-2): every representative chain runs in the
/// All-in-One topology and in the design-source §D9 three-process topology, and both
/// assert the SAME fixed expectations — per-role receive order, final business state
/// snapshot, and the D3 delivery branch sequence — so semantic equivalence between the
/// topologies is enforced by the CI gate (topology-equivalence.yml).
/// The All-in-One cases additionally prove the D3 acceptance property: case 1 local
/// delivery is hit by 100% of the hops.
/// </remarks>
public class TopologyEquivalenceTests
{
    /// <summary>
    /// 等价链路种类。
    /// </summary>
    public enum EquivalenceChainKind
    {
        /// <summary>Gate→Game 会话建立 / Gate→Game session establishment</summary>
        GateToGameSessionEstablish = 1,

        /// <summary>Game→Social 好友查询 / Game→Social friend query</summary>
        GameToSocialFriendQuery = 2,

        /// <summary>Game→Match 匹配请求 / Game→Match match request</summary>
        GameToMatchJoinRequest = 3,

        /// <summary>Match→Game 结算回调 / Match→Game settlement callback</summary>
        MatchToGameSettlementCallback = 4,
    }

    /// <summary>
    /// 等价拓扑种类。
    /// </summary>
    public enum EquivalenceTopologyKind
    {
        /// <summary>All-in-One 单进程 / All-in-One single process</summary>
        AllInOne = 1,

        /// <summary>三进程（设计源 §D9：Gate | Game+Social | Match）/ Three processes (§D9)</summary>
        MultiProcess = 2,
    }

    /// <summary>
    /// 常规链路跳超时（毫秒）——远大于处理耗时，两拓扑都应成功。
    /// </summary>
    private const int ComfortableHopTimeoutMilliseconds = 5000;

    /// <summary>
    /// 紧跳超时（毫秒）——远小于处理器延时，两拓扑都应失败。
    /// </summary>
    private const int TightHopTimeoutMilliseconds = 80;

    /// <summary>
    /// 处理器延时（毫秒）——制造必超时的处理耗时。
    /// </summary>
    private const int HandlerDelayMilliseconds = 400;

    /// <summary>
    /// 四条代表性链路 × 两拓扑：消息序、最终状态、投递分支全部符合固定预期（8 用例）。
    /// </summary>
    [Theory]
    [InlineData(EquivalenceChainKind.GateToGameSessionEstablish, EquivalenceTopologyKind.AllInOne)]
    [InlineData(EquivalenceChainKind.GateToGameSessionEstablish, EquivalenceTopologyKind.MultiProcess)]
    [InlineData(EquivalenceChainKind.GameToSocialFriendQuery, EquivalenceTopologyKind.AllInOne)]
    [InlineData(EquivalenceChainKind.GameToSocialFriendQuery, EquivalenceTopologyKind.MultiProcess)]
    [InlineData(EquivalenceChainKind.GameToMatchJoinRequest, EquivalenceTopologyKind.AllInOne)]
    [InlineData(EquivalenceChainKind.GameToMatchJoinRequest, EquivalenceTopologyKind.MultiProcess)]
    [InlineData(EquivalenceChainKind.MatchToGameSettlementCallback, EquivalenceTopologyKind.AllInOne)]
    [InlineData(EquivalenceChainKind.MatchToGameSettlementCallback, EquivalenceTopologyKind.MultiProcess)]
    public async Task CrossRoleChain_BothTopologies_PreserveOrderStateAndDeliveryBranch(
        EquivalenceChainKind chainKind,
        EquivalenceTopologyKind topologyKind)
    {
        using (var world = CreateWorld(topologyKind))
        {
            var result = await RunChainAsync(world, chainKind, ComfortableHopTimeoutMilliseconds);

            AssertExpectedArrivalOrder(chainKind, result);
            AssertExpectedStateSnapshot(chainKind, result);
            AssertExpectedDeliveryBranches(chainKind, topologyKind, result);
        }
    }

    /// <summary>
    /// 超时行为等价：相同紧超时参数下，两拓扑对本地跳（Game→Social）与跨进程跳（Gate→Game）都同样失败。
    /// </summary>
    [Fact]
    public async Task CrossRoleChain_WithTightTimeout_FailsInBothTopologies()
    {
        using (var allInOneWorld = CreateWorld(EquivalenceTopologyKind.AllInOne))
        using (var multiProcessWorld = CreateWorld(EquivalenceTopologyKind.MultiProcess))
        {
            // 本地跳（两拓扑内 Game→Social 均为 case 1）
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                CrossRoleChains.RunGameToSocialFriendQueryAsync(allInOneWorld, TightHopTimeoutMilliseconds, HandlerDelayMilliseconds));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                CrossRoleChains.RunGameToSocialFriendQueryAsync(multiProcessWorld, TightHopTimeoutMilliseconds, HandlerDelayMilliseconds));

            // 跨进程跳（三进程拓扑内 Gate→Game 经环回转发缝）
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                CrossRoleChains.RunGateToGameSessionEstablishAsync(allInOneWorld, TightHopTimeoutMilliseconds, HandlerDelayMilliseconds));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                CrossRoleChains.RunGateToGameSessionEstablishAsync(multiProcessWorld, TightHopTimeoutMilliseconds, HandlerDelayMilliseconds));
        }
    }

    /// <summary>
    /// 创建指定拓扑的世界。
    /// </summary>
    private static TopologyWorld CreateWorld(EquivalenceTopologyKind topologyKind)
    {
        if (topologyKind == EquivalenceTopologyKind.AllInOne)
        {
            return TopologyWorld.CreateAllInOne(new List<string>
            {
                CrossRoleChains.GateRoleName,
                CrossRoleChains.GameRoleName,
                CrossRoleChains.SocialRoleName,
                CrossRoleChains.MatchRoleName,
            });
        }

        return TopologyWorld.CreateMultiProcess(
            CrossRoleChains.GateRoleName,
            CrossRoleChains.GameRoleName,
            CrossRoleChains.SocialRoleName,
            CrossRoleChains.MatchRoleName);
    }

    /// <summary>
    /// 运行指定链路。
    /// </summary>
    private static Task<ChainExecutionResult> RunChainAsync(TopologyWorld world, EquivalenceChainKind chainKind, int hopTimeoutMilliseconds)
    {
        if (chainKind == EquivalenceChainKind.GateToGameSessionEstablish)
        {
            return CrossRoleChains.RunGateToGameSessionEstablishAsync(world, hopTimeoutMilliseconds);
        }

        if (chainKind == EquivalenceChainKind.GameToSocialFriendQuery)
        {
            return CrossRoleChains.RunGameToSocialFriendQueryAsync(world, hopTimeoutMilliseconds);
        }

        if (chainKind == EquivalenceChainKind.GameToMatchJoinRequest)
        {
            return CrossRoleChains.RunGameToMatchJoinRequestAsync(world, hopTimeoutMilliseconds);
        }

        return CrossRoleChains.RunMatchToGameSettlementCallbackAsync(world, hopTimeoutMilliseconds);
    }

    /// <summary>
    /// 断言每 Role 到达序符合发送序，且无关 Role 零到达（无误路由）。
    /// </summary>
    private static void AssertExpectedArrivalOrder(EquivalenceChainKind chainKind, ChainExecutionResult result)
    {
        if (chainKind == EquivalenceChainKind.GateToGameSessionEstablish)
        {
            Assert.Equal(Repeat("SessionEstablishRequest", 3), result.ArrivalOrderByRole[CrossRoleChains.GameRoleName]);
            Assert.Equal(Repeat("SessionEstablishResponse", 3), result.ArrivalOrderByRole[CrossRoleChains.GateRoleName]);
            Assert.Empty(result.ArrivalOrderByRole[CrossRoleChains.SocialRoleName]);
            Assert.Empty(result.ArrivalOrderByRole[CrossRoleChains.MatchRoleName]);
        }
        else if (chainKind == EquivalenceChainKind.GameToSocialFriendQuery)
        {
            Assert.Equal(Repeat("FriendListRequest", 3), result.ArrivalOrderByRole[CrossRoleChains.SocialRoleName]);
            Assert.Equal(Repeat("FriendListResponse", 3), result.ArrivalOrderByRole[CrossRoleChains.GameRoleName]);
            Assert.Empty(result.ArrivalOrderByRole[CrossRoleChains.GateRoleName]);
            Assert.Empty(result.ArrivalOrderByRole[CrossRoleChains.MatchRoleName]);
        }
        else if (chainKind == EquivalenceChainKind.GameToMatchJoinRequest)
        {
            Assert.Equal(Repeat("MatchJoinRequest", 3), result.ArrivalOrderByRole[CrossRoleChains.MatchRoleName]);
            Assert.Equal(Repeat("MatchJoinResponse", 3), result.ArrivalOrderByRole[CrossRoleChains.GameRoleName]);
            Assert.Empty(result.ArrivalOrderByRole[CrossRoleChains.GateRoleName]);
            Assert.Empty(result.ArrivalOrderByRole[CrossRoleChains.SocialRoleName]);
        }
        else
        {
            Assert.Equal(Repeat("SettlementCallbackMessage", 3), result.ArrivalOrderByRole[CrossRoleChains.GameRoleName]);
            Assert.Equal(Repeat("SettlementAckMessage", 3), result.ArrivalOrderByRole[CrossRoleChains.MatchRoleName]);
            Assert.Empty(result.ArrivalOrderByRole[CrossRoleChains.GateRoleName]);
            Assert.Empty(result.ArrivalOrderByRole[CrossRoleChains.SocialRoleName]);
        }
    }

    /// <summary>
    /// 断言最终业务状态快照等于固定预期（两拓扑共用同一常量 ⇒ 等价）。
    /// </summary>
    private static void AssertExpectedStateSnapshot(EquivalenceChainKind chainKind, ChainExecutionResult result)
    {
        if (chainKind == EquivalenceChainKind.GateToGameSessionEstablish)
        {
            Assert.Equal(
                "Game|SessionEstablished=1001,1002,1003;Gate|SessionAcknowledged=1001,1002,1003",
                result.StateSnapshot);
        }
        else if (chainKind == EquivalenceChainKind.GameToSocialFriendQuery)
        {
            Assert.Equal(
                "Game|FriendQueryResult=1001:2001+3001,1002:2002+3002,1003:2003+3003;Social|FriendListServed=1001,1002,1003",
                result.StateSnapshot);
        }
        else if (chainKind == EquivalenceChainKind.GameToMatchJoinRequest)
        {
            Assert.Equal(
                "Game|MatchTicketReceived=1001:ticket-1001,1002:ticket-1002,1003:ticket-1003;Match|MatchTicketIssued=1001:ticket-1001,1002:ticket-1002,1003:ticket-1003",
                result.StateSnapshot);
        }
        else
        {
            Assert.Equal(
                "Game|SettlementApplied=5001:2000,5002:2001,5003:2002;Match|SettlementAcknowledged=5001:2000,5002:2001,5003:2002",
                result.StateSnapshot);
        }
    }

    /// <summary>
    /// 断言投递分支序列：All-in-One 恒 case 1（D3 验收：100% 命中）；三进程拓扑按 cell 划分分支。
    /// </summary>
    private static void AssertExpectedDeliveryBranches(
        EquivalenceChainKind chainKind,
        EquivalenceTopologyKind topologyKind,
        ChainExecutionResult result)
    {
        // 3 次请求 + 3 次应答
        Assert.Equal(6, result.Deliveries.Count);

        if (topologyKind == EquivalenceTopologyKind.AllInOne)
        {
            // D3 验收：All-in-One 形态下 case 1 本地直投被 100% 命中
            Assert.All(result.Deliveries, delivery => Assert.Equal(RoleRouteDelivery.LocalActor, delivery));
            return;
        }

        // 三进程拓扑：Gate | Game+Social | Match —— 链路二的全部跳都在 Game+Social cell 内，其余链路全部跨 cell
        var expectedBranch = chainKind == EquivalenceChainKind.GameToSocialFriendQuery
            ? RoleRouteDelivery.LocalActor
            : RoleRouteDelivery.RemoteForwarded;
        Assert.All(result.Deliveries, delivery => Assert.Equal(expectedBranch, delivery));
    }

    /// <summary>
    /// 构造重复的消息名序列。
    /// </summary>
    private static List<string> Repeat(string messageName, int count)
    {
        var names = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            names.Add(messageName);
        }

        return names;
    }
}
