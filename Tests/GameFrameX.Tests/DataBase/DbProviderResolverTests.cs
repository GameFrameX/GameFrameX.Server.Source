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
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// DbProviderResolver 约定不变量的守护测试（C177）。
/// </summary>
/// <remarks>
/// Guard tests turning the provider naming-convention string coupling into a CI invariant: every
/// <see cref="DatabaseProviderType"/> member must resolve to a creatable <see cref="IDatabaseService"/>
/// (the test project references both provider projects, so any provider project/class/namespace
/// rename turns these red), and both failure branches must carry their self-diagnosing localized
/// messages. These tests prove the convention strings resolve, not that the full Init(provider)
/// chain works — that is covered by the InitCore-shared generic Init test family.
/// </remarks>
public class DbProviderResolverTests
{
    /// <summary>
    /// 遍历全部枚举成员：映射表必须可解析且实例实现 <see cref="IDatabaseService"/>（新增枚举成员未配映射即红）。
    /// </summary>
    [Fact]
    public void Create_AllEnumMembers_ResolveToDatabaseService()
    {
        foreach (var provider in Enum.GetValues<DatabaseProviderType>())
        {
            var service = DbProviderResolver.Create(provider);

            Assert.NotNull(service);
            Assert.IsAssignableFrom<IDatabaseService>(service);
        }
    }

    /// <summary>
    /// 解析结果按枚举缓存 Type 而非实例：两次 Create 返回不同实例、同一运行时类型。
    /// </summary>
    [Fact]
    public void Create_SameProviderTwice_ReturnsDistinctInstancesOfSameType()
    {
        var first = DbProviderResolver.Create(DatabaseProviderType.Mongo);
        var second = DbProviderResolver.Create(DatabaseProviderType.Mongo);

        Assert.NotSame(first, second);
        Assert.Same(first.GetType(), second.GetType());
    }

    /// <summary>
    /// 未配映射的枚举值（越界转型）触发 NotSupported：消息含枚举值名与已支持列表。
    /// </summary>
    [Fact]
    public void Create_UnmappedEnumValue_ThrowsNotSupportedWithDiagnosis()
    {
        var unmapped = (DatabaseProviderType)99;

        var exception = Assert.Throws<InvalidOperationException>(() => DbProviderResolver.Create(unmapped));

        Assert.Contains("99", exception.Message);
        Assert.Contains(nameof(DatabaseProviderType.Mongo), exception.Message);
        Assert.Contains(nameof(DatabaseProviderType.PostgreSql), exception.Message);
    }

    /// <summary>
    /// 坏类型名注入触发 NotInstalled：消息含期望的程序集限定类型名与逃生门指引。
    /// </summary>
    [Fact]
    public void CreateFromTypeName_UnresolvableTypeName_ThrowsNotInstalledWithDiagnosis()
    {
        const string badTypeName = "GameFrameX.DataBase.Mongo.NoSuchService, GameFrameX.DataBase.Mongo";

        var exception = Assert.Throws<InvalidOperationException>(() => DbProviderResolver.CreateFromTypeName(badTypeName));

        Assert.Contains(badTypeName, exception.Message);
        Assert.Contains("Init<T>", exception.Message);
    }
}
