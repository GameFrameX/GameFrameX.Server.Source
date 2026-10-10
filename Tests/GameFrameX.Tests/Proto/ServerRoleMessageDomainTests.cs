// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
// 
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
// 
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
// 
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
// 
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System.Reflection;
using GameFrameX.Core.Abstractions.Attribute;
using GameFrameX.Network.Abstractions;
using GameFrameX.Proto.Proto;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.Proto;

/// <summary>
/// ServerRole 垂直切片护栏测试（C197）。
/// </summary>
/// <remarks>
/// 守护三条装配不变式：①全程序集消息 id（MessageTypeHandler）唯一——17 个 Role 域号
/// （600–900）与既有域号（含负域号内置协议）任何碰撞都在此拦截，而非运行期 handler
/// 注册冲突；②每个 Role 的真实业务消息清单与规格一致且落在自己的域号段——防域号漂移
/// 串扰其它 Role / 既有子系统；③每条业务消息恰好装配一个 Hotfix handler，且 handler →
/// Agent → Component 链路存在并挂 server Actor——防「消息无 handler」与「handler 挂错
/// 组件」两类消费侧可见缺陷。
/// </remarks>
public class ServerRoleMessageDomainTests
{
    /// <summary>
    /// C197 Phase 2 消息清单（Role, 域号, 该 Role 全部请求消息名）——与 change.md 业务矩阵一致。
    /// </summary>
    private static readonly (string Role, int Domain, string[] Requests)[] RoleMessages =
    {
        ("Account", 200, new[] { "ReqAccountRegister", "ReqAccountQuery", "ReqAccountChangePassword" }),
        ("Login", 210, new[] { "ReqLoginIssueToken", "ReqLoginVerifyToken", "ReqLoginRevokeToken" }),
        ("Auth", 220, new[] { "ReqAuthGrant", "ReqAuthRevoke", "ReqAuthHas", "ReqAuthListPermissions" }),
        ("Gateway", 230, new[] { "ReqGatewayRegisterTarget", "ReqGatewayHeartbeat", "ReqGatewayResolveTarget", "ReqGatewayUnregisterTarget" }),
        ("Match", 420, new[] { "ReqMatchEnqueue", "ReqMatchCancel", "ReqMatchStatus" }),
        ("Room", 430, new[] { "ReqRoomAllocate", "ReqRoomRelease", "ReqRoomQuery" }),
        ("Scene", 440, new[] { "ReqSceneEnter", "ReqSceneLeave", "ReqSceneQuery" }),
        ("World", 450, new[] { "ReqWorldPublish", "ReqWorldRevoke", "ReqWorldQuery" }),
        ("Battle", 460, new[] { "ReqBattleCreate", "ReqBattleQuery" }),
        ("Mail", 650, new[] { "ReqMailEnqueue", "ReqMailQueryPending", "ReqMailMarkDelivered", "ReqMailMarkFailed" }),
        ("Friend", 660, new[] { "ReqFriendSendRequest", "ReqFriendAccept", "ReqFriendReject", "ReqFriendRemove", "ReqFriendRelationList" }),
        ("Team", 670, new[] { "ReqTeamCreate", "ReqTeamJoin", "ReqTeamLeave", "ReqTeamKick", "ReqTeamDisband", "ReqTeamList" }),
        ("Guild", 680, new[] { "ReqGuildCreate", "ReqGuildApply", "ReqGuildApprove", "ReqGuildLeave", "ReqGuildKick", "ReqGuildDisband", "ReqGuildQuery" }),
        ("Chat", 690, new[] { "ReqChatJoin", "ReqChatLeave", "ReqChatSpeak", "ReqChatPullHistory" }),
        ("Trade", 800, new[] { "ReqTradePlace", "ReqTradeCancel", "ReqTradeBook" }),
        ("Auction", 810, new[] { "ReqAuctionListLot", "ReqAuctionBid", "ReqAuctionBuyout", "ReqAuctionSettle", "ReqAuctionQueryLots" }),
        ("Gm", 900, new[] { "ReqGmAddPenalty", "ReqGmRevokePenalty", "ReqGmQueryPenalties" }),
    };

    private static readonly Type HotfixAssemblyAnchor = typeof(GameFrameX.Hotfix.HotfixHandler);
    private static readonly Type ProtoAssemblyAnchor = typeof(ReqChatJoin);

    private static IEnumerable<Type> ProtoMessageTypes()
    {
        return ProtoAssemblyAnchor.Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<MessageTypeHandlerAttribute>() != null);
    }

    /// <summary>
    /// 全程序集消息 id 唯一：任何两个消息类型的 MessageTypeHandler id 不得相同
    /// （碰撞意味着热更注册表 TryAdd 冲突或消息派发错路由）。
    /// </summary>
    [Fact]
    public void MessageTypeIds_AreUniqueAcrossProtoAssembly()
    {
        var duplicates = ProtoMessageTypes()
            .Select(type => new { Name = type.Name, Id = type.GetCustomAttribute<MessageTypeHandlerAttribute>()!.MessageId })
            .GroupBy(entry => entry.Id)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(entry => entry.Name))}")
            .ToList();

        Assert.True(duplicates.Count == 0, $"重复消息 id:\n{string.Join('\n', duplicates)}");
    }

    /// <summary>
    /// 每个 Role 的业务消息清单与规格一致，且全部落在自己的域号段（id 高 16 位 = 域号）。
    /// </summary>
    [Theory]
    [MemberData(nameof(RoleMessageCases))]
    public void RoleMessages_MatchSpecAndStayInOwnDomain(string role, int domain, string requestNames)
    {
        var expectedRequests = requestNames.Split('|');
        var roleProtoTypes = ProtoAssemblyAnchor.Assembly.GetTypes()
            .Where(type => type.Namespace == "GameFrameX.Proto.Proto"
                           && type.GetCustomAttribute<MessageTypeHandlerAttribute>() is { } attribute
                           && (attribute.MessageId >> 16) == domain)
            .Select(type => type.Name)
            .OrderBy(name => name)
            .ToList();

        // 域内消息全部归属该 Role 的消息清单（Req/Resp 成对，Resp 名 = "Resp" + Req 名去 "Req" 前缀）
        var expectedAll = expectedRequests
            .SelectMany(name => new[] { name, "Resp" + name.Substring(3) })
            .OrderBy(name => name)
            .ToList();

        Assert.True(expectedAll.SequenceEqual(roleProtoTypes),
            $"{role}(域 {domain}) 消息清单与规格不符。规格: [{string.Join(", ", expectedAll)}] 实际: [{string.Join(", ", roleProtoTypes)}]");
    }

    /// <summary>
    /// 每条业务请求消息在 Hotfix 程序集恰好装配一个 MessageMapping handler，
    /// 且 handler → Agent → Component 全链路存在、组件挂 server Actor（ActorTypeServer）。
    /// </summary>
    [Theory]
    [MemberData(nameof(RoleMessageCases))]
    public void RoleRequestHandlers_ExactlyOneWithAgentComponentChain(string role, int domain, string requestNames)
    {
        var agentsSeen = new HashSet<Type>();
        foreach (var requestName in requestNames.Split('|'))
        {
            var reqType = ProtoAssemblyAnchor.Assembly.GetType($"GameFrameX.Proto.Proto.{requestName}");
            Assert.True(reqType != null, $"缺少 {requestName} 消息");

            var handlers = HotfixAssemblyAnchor.Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<MessageMappingAttribute>()?.MessageType == reqType)
                .ToList();
            Assert.True(handlers.Count == 1, $"{requestName} 的 MessageMapping handler 数量应为 1，实际 {handlers.Count}");
            var handler = handlers[0];

            // handler : GlobalRpcComponentHandler<RoleComponentAgent, Req, Resp>
            var baseGeneric = handler.BaseType;
            Assert.NotNull(baseGeneric);
            Assert.True(baseGeneric!.IsGenericType, $"{requestName}Handler 应为 GlobalRpcComponentHandler 派生");
            Assert.Equal("GlobalRpcComponentHandler`3", baseGeneric.GetGenericTypeDefinition().Name);
            var agentType = baseGeneric.GetGenericArguments()[0];
            agentsSeen.Add(agentType);
        }

        // 同一 Role 的全部 handler 绑定同一个 Agent，且 Agent → Component 挂 server Actor
        var agent = Assert.Single(agentsSeen);
        Assert.True(agent.BaseType is { IsGenericType: true }, $"{role} 的 Agent 应为 StateComponentAgent 派生");
        var componentType = agent.BaseType!.GetGenericArguments()[0];
        var componentTypeAttribute = componentType.GetCustomAttribute<ComponentTypeAttribute>();
        Assert.True(componentTypeAttribute != null, $"{role}Component 缺少 ComponentType");
        Assert.Equal(GlobalConst.ActorTypeServer, componentTypeAttribute!.Type);
    }

    public static IEnumerable<object[]> RoleMessageCases()
    {
        return RoleMessages.Select(entry => new object[] { entry.Role, entry.Domain, string.Join('|', entry.Requests) });
    }
}
