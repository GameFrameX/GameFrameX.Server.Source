// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
//
//  使用本项目须严格遵守相应法律法规与开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file at the root of the source code for the full license text.
//
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations.
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes or liabilities arising from secondary development based on this project
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
/// {{Name}} 垂直切片域内护栏测试（gfx-role 模板生成）。
/// </summary>
/// <remarks>
/// 守护本域两条不变式（<see cref="ServerRoleMessageDomainTests"/> 三不变式的本域子集）：
/// ①本域消息 id 高 16 位 = {{Domain}} 且清单恰为规格消息对（Req/Resp 成对，新增消息对须同步本清单）；
/// ②每条请求消息恰好装配一个 MessageMapping handler，且 handler → Agent → Component 链路存在并挂 ActorTypeServer。
/// 全程序集消息 id 唯一性由 <see cref="ServerRoleMessageDomainTests"/> 全局兜底。
/// </remarks>
public class {{Name}}MessageDomainTests
{
    private const int Domain = {{Domain}};

    private static readonly Type HotfixAssemblyAnchor = typeof(GameFrameX.Hotfix.HotfixHandler);
    private static readonly Type ProtoAssemblyAnchor = typeof(Req{{Name}}Echo);

    /// <summary>
    /// 本域消息清单恰为规格消息对：全部落在本域号段，且无规格外消息混入。
    /// </summary>
    [Fact]
    public void DomainMessages_MatchSpecAndStayInOwnDomain()
    {
        var expected = new[] { "Req{{Name}}Echo", "Resp{{Name}}Echo" }.OrderBy(name => name).ToList();

        var actual = ProtoAssemblyAnchor.Assembly.GetTypes()
            .Where(type => type.Namespace == "GameFrameX.Proto.Proto"
                           && type.GetCustomAttribute<MessageTypeHandlerAttribute>() is { } attribute
                           && (attribute.MessageId >> 16) == Domain)
            .Select(type => type.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(expected.SequenceEqual(actual),
            $"{{Name}}(域 {Domain}) 消息清单与规格不符。规格: [{string.Join(", ", expected)}] 实际: [{string.Join(", ", actual)}]");
    }

    /// <summary>
    /// 每条业务请求消息恰好装配一个 MessageMapping handler，且 handler → Agent → Component 全链路存在、组件挂 server Actor。
    /// </summary>
    [Fact]
    public void RequestHandlers_ExactlyOneWithAgentComponentChain()
    {
        var reqType = ProtoAssemblyAnchor.Assembly.GetType($"GameFrameX.Proto.Proto.Req{{Name}}Echo");
        Assert.True(reqType != null, "缺少 Req{{Name}}Echo 消息（proto 导出了吗？）");

        var handlers = HotfixAssemblyAnchor.Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<MessageMappingAttribute>()?.MessageType == reqType)
            .ToList();
        Assert.True(handlers.Count == 1, $"Req{{Name}}Echo 的 MessageMapping handler 数量应为 1，实际 {handlers.Count}");
        var handler = handlers[0];

        // handler : GlobalRpcComponentHandler<RoleComponentAgent, Req, Resp>
        var baseGeneric = handler.BaseType;
        Assert.True(baseGeneric != null && baseGeneric.IsGenericType, "Req{{Name}}EchoHandler 应为 GlobalRpcComponentHandler 派生");
        Assert.Equal("GlobalRpcComponentHandler`3", baseGeneric!.GetGenericTypeDefinition().Name);
        var agentType = baseGeneric.GetGenericArguments()[0];

        // Agent → Component 挂 server Actor
        Assert.True(agentType.BaseType is { IsGenericType: true }, "{{Name}}ComponentAgent 应为 StateComponentAgent 派生");
        var componentType = agentType.BaseType!.GetGenericArguments()[0];
        var componentTypeAttribute = componentType.GetCustomAttribute<ComponentTypeAttribute>();
        Assert.True(componentTypeAttribute != null, "{{Name}}Component 缺少 ComponentType");
        Assert.Equal(GlobalConst.ActorTypeServer, componentTypeAttribute!.Type);
    }
}
