// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
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
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// MultiDbRegistry 多库字典行为的单元测试（C143a D20#2）。
/// </summary>
/// <remarks>
/// Unit tests for MultiDbRegistry dictionary behaviour (C143a D20#2).
/// Uses <see cref="NoConnectionDatabaseService"/> from <see cref="GameDbMultiDatabaseTests"/> as the service instance.
/// </remarks>
[Collection(nameof(GameDbStaticStateCollection))]
public class MultiDbRegistryTests : IDisposable
{
    /// <summary>
    /// 构造函数：每个测试前清空注册表。
    /// </summary>
    public MultiDbRegistryTests()
    {
        MultiDbRegistry.Clear();
    }

    /// <summary>
    /// 释放：测试后再次清空，避免静态状态泄漏到其它测试。
    /// </summary>
    public void Dispose()
    {
        MultiDbRegistry.Clear();
    }

    /// <summary>
    /// 注册后可按名获取同一实例。
    /// </summary>
    [Fact]
    public void Register_ThenGet_ReturnsSameInstance()
    {
        var service = new GameDbMultiDatabaseTests.NoConnectionDatabaseService();
        MultiDbRegistry.Register("gameframex", service);

        var resolved = MultiDbRegistry.Get("gameframex");

        Assert.Same(service, resolved);
    }

    /// <summary>
    /// 同名重复注册被拒绝（拒绝静默覆盖）。
    /// </summary>
    [Fact]
    public void Register_SameNameTwice_Throws()
    {
        MultiDbRegistry.Register("gameframex", new GameDbMultiDatabaseTests.NoConnectionDatabaseService());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MultiDbRegistry.Register("gameframex", new GameDbMultiDatabaseTests.NoConnectionDatabaseService()));

        Assert.Contains("gameframex", exception.Message);
    }

    /// <summary>
    /// 未注册名 Get 抛异常且消息包含已注册名提示。
    /// </summary>
    [Fact]
    public void Get_UnknownName_ThrowsWithRegisteredNames()
    {
        MultiDbRegistry.Register("gameframex", new GameDbMultiDatabaseTests.NoConnectionDatabaseService());

        var exception = Assert.Throws<InvalidOperationException>(() => MultiDbRegistry.Get("missing_database"));

        Assert.Contains("missing_database", exception.Message);
        Assert.Contains("gameframex", exception.Message);
    }

    /// <summary>
    /// TryGet 对已注册/未注册名分别返回 true/false。
    /// </summary>
    [Fact]
    public void TryGet_RegisteredAndUnknownName()
    {
        var service = new GameDbMultiDatabaseTests.NoConnectionDatabaseService();
        MultiDbRegistry.Register("gameframex", service);

        var isRegistered = MultiDbRegistry.TryGet("gameframex", out var resolved);
        var isUnknown = MultiDbRegistry.TryGet("missing_database", out var notResolved);

        Assert.True(isRegistered);
        Assert.Same(service, resolved);
        Assert.False(isUnknown);
        Assert.Null(notResolved);
    }

    /// <summary>
    /// Contains 反映注册状态（幂等初始化守卫依据）。
    /// </summary>
    [Fact]
    public void Contains_ReflectsRegistrationState()
    {
        Assert.False(MultiDbRegistry.Contains(MultiDbRegistry.ControlDatabaseName));

        MultiDbRegistry.Register(MultiDbRegistry.ControlDatabaseName, new GameDbMultiDatabaseTests.NoConnectionDatabaseService());

        Assert.True(MultiDbRegistry.Contains(MultiDbRegistry.ControlDatabaseName));
    }

    /// <summary>
    /// GetRegisteredDatabaseNames 返回全部已注册名快照。
    /// </summary>
    [Fact]
    public void GetRegisteredDatabaseNames_ReturnsSnapshot()
    {
        MultiDbRegistry.Register("gameframex", new GameDbMultiDatabaseTests.NoConnectionDatabaseService());
        MultiDbRegistry.Register(MultiDbRegistry.ControlDatabaseName, new GameDbMultiDatabaseTests.NoConnectionDatabaseService());

        var names = MultiDbRegistry.GetRegisteredDatabaseNames();

        Assert.Equal(2, names.Count);
        Assert.Contains("gameframex", names);
        Assert.Contains(MultiDbRegistry.ControlDatabaseName, names);
    }

    /// <summary>
    /// 控制库名与缺省名常量符合 D16/D18 约定（全名、无简写）。
    /// </summary>
    [Fact]
    public void WellKnownNames_MatchDesignContract()
    {
        Assert.Equal("gameframex_control", MultiDbRegistry.ControlDatabaseName);
        Assert.Equal("default", MultiDbRegistry.DefaultDatabaseName);
    }
}
