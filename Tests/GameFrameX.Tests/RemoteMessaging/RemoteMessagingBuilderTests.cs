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

using GameFrameX.Network.RemoteMessaging;
using GameFrameX.Network.RemoteMessaging.Contracts;

namespace GameFrameX.Tests.RemoteMessaging;

/// <summary>
/// RemoteMessagingBuilder 单元测试：实例链构建语义与路由表 fail-fast。
/// </summary>
/// <remarks>
/// Unit tests for RemoteMessagingBuilder: instance-chain build semantics and route-table fail-fast.
/// </remarks>
public class RemoteMessagingBuilderTests
{
    [Fact]
    public void Build_WithRouteTable_ShouldReturnNonNullInstance()
    {
        var client = new RemoteMessagingBuilder()
            .LoadFromEnvironment()
            .WithRouteTable(RouteTableTestHelper.EmptyProvider())
            .Build();

        Assert.NotNull(client);
    }

    [Fact]
    public void Build_WithRouteTable_ShouldReturnDifferentInstances()
    {
        var provider = RouteTableTestHelper.EmptyProvider();
        var client1 = new RemoteMessagingBuilder().WithRouteTable(provider).Build();
        var client2 = new RemoteMessagingBuilder().WithRouteTable(provider).Build();

        Assert.NotSame(client1, client2);
    }

    [Fact]
    public void Build_WithRouteTable_ShouldImplementIRemoteMessageClient()
    {
        var client = new RemoteMessagingBuilder().WithRouteTable(RouteTableTestHelper.EmptyProvider()).Build();

        Assert.IsAssignableFrom<IRemoteMessageClient>(client);
    }

    [Fact]
    public void Build_WithoutRouteTable_ShouldThrowInvalidOperationException()
    {
        // fail-fast：路由表是端点唯一事实源，未绑定时构建直接失败（无环境变量回落）。
        Assert.Throws<InvalidOperationException>(() => new RemoteMessagingBuilder().LoadFromEnvironment().Build());
    }

    [Fact]
    public void WithRouteTable_Null_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RemoteMessagingBuilder().WithRouteTable(null));
    }
}
