// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to infringe the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB 仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System;
using GameFrameX.Discovery;
using GameFrameX.Tests.DataBase;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.Discovery;

/// <summary>
/// DiscoveryRuntimeResolver 约定不变量的守护测试。
/// </summary>
/// <remarks>
/// Guard tests turning the discovery naming-convention string coupling into a CI invariant: every
/// <see cref="DatabaseProviderType"/> member must resolve to a runtime type exposing
/// <c>public static void Activate(DiscoveryActivationOptions)</c> (the test project references
/// both provider projects, so any provider project/class/namespace rename turns these red), and
/// both failure branches must carry their self-diagnosing localized messages. The enum walk drives
/// the resolver with a ConnectionName-less options: the runtime's own first validation
/// (ArgumentException on ConnectionName) is the proof that the mapping resolved, the entry point
/// matched the exact signature, and the reflective invocation reached the real runtime — all
/// before any process-wide activation state mutates. Direct <c>*DiscoveryRuntime.Activate</c>
/// calls remain the test escape hatch (the generic <c>Init&lt;T&gt;</c> equivalent).
/// Mirrors the isolation stance: joined into the GameDbStaticStateCollection in case a
/// future runtime change moves validation behind state mutation.
/// </remarks>
[Collection(nameof(GameDbStaticStateCollection))]
public sealed class DiscoveryRuntimeResolverTests
{
    /// <summary>
    /// 遍历全部枚举成员：映射表必须可解析且目标存在精确签名的激活入口（新增枚举成员未配映射即红）。
    /// </summary>
    /// <remarks>
    /// Walks every enum member through the resolver. The runtime-level ArgumentException (raised on
    /// the unset ConnectionName, before the one-shot activation guard) proves type resolution,
    /// entry-point signature match and reflective invocation for each member without activating
    /// anything for real.
    /// </remarks>
    [Fact]
    public void Activate_AllEnumMembers_ReachRuntimeActivationEntry()
    {
        foreach (var provider in Enum.GetValues<DatabaseProviderType>())
        {
            var options = new DiscoveryActivationOptions
            {
                Provider = provider,
                HostedRoleNames = new[] { "Game" },
            };

            // 反射解包后应原样上抛运行时自身的 ConnectionName 校验异常（ParamName = options）。
            var exception = Assert.Throws<ArgumentException>(() => DiscoveryRuntimeResolver.Activate(options));

            Assert.Equal("options", exception.ParamName);
        }
    }

    /// <summary>
    /// 未配映射的枚举值（越界转型）触发 NotSupported：消息含枚举值名与已支持列表。
    /// </summary>
    [Fact]
    public void Activate_UnmappedEnumValue_ThrowsNotSupportedWithDiagnosis()
    {
        var unmapped = (DatabaseProviderType)99;

        var exception = Assert.Throws<InvalidOperationException>(() => DiscoveryRuntimeResolver.Activate(new DiscoveryActivationOptions { Provider = unmapped }));

        Assert.Contains("99", exception.Message);
        Assert.Contains(nameof(DatabaseProviderType.Mongo), exception.Message);
        Assert.Contains(nameof(DatabaseProviderType.PostgreSql), exception.Message);
    }

    /// <summary>
    /// 坏类型名注入触发 NotInstalled：消息含期望的程序集限定类型名与直调逃生门指引。
    /// </summary>
    [Fact]
    public void ActivateFromTypeName_UnresolvableTypeName_ThrowsNotInstalledWithDiagnosis()
    {
        const string badTypeName = "GameFrameX.DataBase.Mongo.Discovery.NoSuchRuntime, GameFrameX.DataBase.Mongo";

        var exception = Assert.Throws<InvalidOperationException>(() => DiscoveryRuntimeResolver.ActivateFromTypeName(badTypeName, new DiscoveryActivationOptions()));

        Assert.Contains(badTypeName, exception.Message);
        Assert.Contains("DiscoveryRuntime.Activate", exception.Message);
    }
}
