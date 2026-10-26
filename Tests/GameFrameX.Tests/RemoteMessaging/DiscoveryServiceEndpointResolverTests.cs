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
//   please refer to the LICENSE file in the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to infringe upon the legal rights, interests of others as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Discovery;
using GameFrameX.Network.RemoteMessaging.Discovery;

namespace GameFrameX.Tests.RemoteMessaging;

/// <summary>
/// DiscoveryServiceEndpointResolver 解析语义（C304）：ServerType 级确定性选择（Active 中 InstanceId
/// 序数最小者）、实例级直达（Instance 视图含 Draining、校验 Role 一致）、端点集合与 scheme 偏好。
/// </summary>
/// <remarks>
/// Resolution semantics of DiscoveryServiceEndpointResolver (C304): deterministic ServerType-level
/// selection (the ordinally smallest InstanceId among Active instances), instance-level direct
/// dialing (the Instance view includes Draining and validates the Role), the endpoint collection,
/// and the scheme preference.
/// </remarks>
public sealed class DiscoveryServiceEndpointResolverTests
{
    [Fact]
    public void Resolve_ServerTypeOnly_MultipleActiveEqualLengthIds_AlwaysReturnsSmallestId()
    {
        // 乱序登记等长数字 id：ServerType 级选择恒返回序数最小者的端点（确定性）
        var table = RouteTableTestHelper.Table(
            RouteTableTestHelper.Descriptor("Social", "2003", "tcp://10.0.0.3:7003"),
            RouteTableTestHelper.Descriptor("Social", "2001", "tcp://10.0.0.1:7001"),
            RouteTableTestHelper.Descriptor("Social", "2002", "tcp://10.0.0.2:7002"));
        var resolver = new DiscoveryServiceEndpointResolver(RouteTableTestHelper.Provider(table));

        Assert.Equal("tcp://10.0.0.1:7001", resolver.ResolveEndpoint(new RemoteServiceTarget("Social")));
        Assert.Equal("tcp://10.0.0.1:7001", resolver.ResolveEndpoint(new RemoteServiceTarget("Social")));
    }

    [Fact]
    public void Resolve_ServerTypeOnly_UnequalOrGeneratedIds_ReturnsDeterministicSmallest()
    {
        // 不等长 / 自动生成形态 id：仍按序数比较（"10" &lt; "9"，字典序而非数值序）
        var table = RouteTableTestHelper.Table(
            RouteTableTestHelper.Descriptor("Social", "9", "tcp://10.0.0.9:7009"),
            RouteTableTestHelper.Descriptor("Social", "10", "tcp://10.0.0.10:7010"));
        var resolver = new DiscoveryServiceEndpointResolver(RouteTableTestHelper.Provider(table));

        Assert.Equal("tcp://10.0.0.10:7010", resolver.ResolveEndpoint(new RemoteServiceTarget("Social")));
    }

    [Fact]
    public void Resolve_WithInstanceId_HitsDrainingInstanceAndReturnsItsEndpoint()
    {
        // 实例直达走 Instance 视图（Active + Draining）：下线中实例上的在途投递仍合法
        var table = RouteTableTestHelper.Table(
            RouteTableTestHelper.Descriptor("Social", "2001", "tcp://10.0.0.1:7001", InstanceStatus.Draining),
            RouteTableTestHelper.Descriptor("Social", "2002", "tcp://10.0.0.2:7002"));
        var resolver = new DiscoveryServiceEndpointResolver(RouteTableTestHelper.Provider(table));

        Assert.Equal("tcp://10.0.0.1:7001", resolver.ResolveEndpoint(new RemoteServiceTarget("Social", "2001")));
    }

    [Fact]
    public void Resolve_WithInstanceId_RoleMismatch_ReturnsEmpty()
    {
        // 指定 InstanceId 命中的实例 Role 与目标 ServerType 不一致 → 空串
        var table = RouteTableTestHelper.Table(
            RouteTableTestHelper.Descriptor("Game", "3001", "tcp://10.0.0.9:7009"));
        var resolver = new DiscoveryServiceEndpointResolver(RouteTableTestHelper.Provider(table));

        Assert.Equal(string.Empty, resolver.ResolveEndpoint(new RemoteServiceTarget("Social", "3001")));
    }

    [Fact]
    public void Resolve_InstanceWithKcpAdditional_PrefersKcpAndTcpSchemeReturnsPrimary()
    {
        // 端点集合 = tcp 主端点 + 附加端点；偏好序 kcp > ws > wss > tcp
        var table = RouteTableTestHelper.Table(
            RouteTableTestHelper.Descriptor("Social", "2001", "tcp://10.0.0.1:7001", additionalEndpoints: new[] { "kcp://10.0.0.1:7101" }));
        var resolver = new DiscoveryServiceEndpointResolver(RouteTableTestHelper.Provider(table));
        var target = new RemoteServiceTarget("Social");

        Assert.Equal("kcp://10.0.0.1:7101", resolver.ResolveEndpoint(target));
        Assert.Equal("tcp://10.0.0.1:7001", resolver.ResolveEndpoint(target, "tcp"));
        Assert.Equal("kcp://10.0.0.1:7101", resolver.ResolveEndpoint(target, "kcp"));
    }

    [Fact]
    public void Resolve_InstanceWithoutAdditional_PrefersTcpPrimaryEndpoint()
    {
        var table = RouteTableTestHelper.Table(
            RouteTableTestHelper.Descriptor("Social", "2001", "tcp://10.0.0.1:7001"));
        var resolver = new DiscoveryServiceEndpointResolver(RouteTableTestHelper.Provider(table));

        Assert.Equal("tcp://10.0.0.1:7001", resolver.ResolveEndpoint(new RemoteServiceTarget("Social")));
    }

    // ─────────────────────────────────────────────────────────────
    //  InstanceDescriptor 附加端点归一化
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Descriptor_NullAdditionalEndpoints_NormalizedToEmptyCollection()
    {
        var descriptor = RouteTableTestHelper.Descriptor("Social", "2001", "tcp://10.0.0.1:7001");

        Assert.NotNull(descriptor.AdditionalAdvertiseEndpoints);
        Assert.Empty(descriptor.AdditionalAdvertiseEndpoints);
    }

    [Fact]
    public void Descriptor_TcpSchemeEntries_Skipped()
    {
        var descriptor = RouteTableTestHelper.Descriptor(
            "Social", "2001", "tcp://10.0.0.1:7001", additionalEndpoints: new[] { "tcp://10.0.0.2:7002", "kcp://10.0.0.1:7101" });

        var additional = descriptor.AdditionalAdvertiseEndpoints;
        Assert.Single(additional);
        Assert.Equal("kcp://10.0.0.1:7101", additional[0]);
    }

    [Fact]
    public void Descriptor_SameSchemeTwice_FirstEntryWins()
    {
        var descriptor = RouteTableTestHelper.Descriptor(
            "Social", "2001", "tcp://10.0.0.1:7001", additionalEndpoints: new[] { "kcp://10.0.0.1:7101", "kcp://10.0.0.9:7199" });

        var additional = descriptor.AdditionalAdvertiseEndpoints;
        Assert.Single(additional);
        Assert.Equal("kcp://10.0.0.1:7101", additional[0]);
    }

    [Fact]
    public void Descriptor_StructurallyInvalidEntries_Skipped()
    {
        // 缺 scheme / 缺端口等结构非法条目在归一化期被跳过
        var descriptor = RouteTableTestHelper.Descriptor(
            "Social", "2001", "tcp://10.0.0.1:7001", additionalEndpoints: new[] { "127.0.0.1:9999", "kcp://10.0.0.1:0", "ws://10.0.0.1:7201" });

        var additional = descriptor.AdditionalAdvertiseEndpoints;
        Assert.Single(additional);
        Assert.Equal("ws://10.0.0.1:7201", additional[0]);
    }
}
