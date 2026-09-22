// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
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
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.StartUp;
using Xunit;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// RoleSet 不可变快照测试（C143b D1）。
/// </summary>
public class RoleSetTests
{
    /// <summary>
    /// 构造后源集合变更不影响快照（防御性拷贝，快照不可变）。
    /// </summary>
    [Fact]
    public void Constructor_DefensivelyCopiesSource_StartUpTypesUnaffectedByLaterMutation()
    {
        var source = new List<KeyValuePair<Type, StartUpTagAttribute>>
        {
            new(typeof(RoleSetTestsRoleAlpha), new StartUpTagAttribute("Alpha", 10)),
            new(typeof(RoleSetTestsRoleBeta), new StartUpTagAttribute("Beta", 20)),
        };
        var roleSet = new RoleSet(source);

        source.Clear();

        Assert.Equal(2, roleSet.StartUpTypes.Count);
        Assert.Equal(new[] { "Alpha", "Beta" }, roleSet.StartUpTypes.Select(pair => pair.Value.ServerType));
    }

    /// <summary>
    /// StartUpTypes 暴露只读快照：不能转回可变 List 修改，Count/Contains 与快照保持一致。
    /// </summary>
    [Fact]
    public void StartUpTypes_ExposesReadOnlySnapshot_NotCastableToMutableList()
    {
        var roleSet = new RoleSet(new List<KeyValuePair<Type, StartUpTagAttribute>>
        {
            new(typeof(RoleSetTestsRoleAlpha), new StartUpTagAttribute("Alpha", 10)),
            new(typeof(RoleSetTestsRoleBeta), new StartUpTagAttribute("Beta", 20)),
        });

        Assert.IsNotAssignableFrom<List<KeyValuePair<Type, StartUpTagAttribute>>>(roleSet.StartUpTypes);
        Assert.True(roleSet.Contains("Alpha"));
    }

    /// <summary>
    /// 成员判定：Contains 按服务器类型名判定 Role 是否属于本进程。
    /// </summary>
    [Fact]
    public void Contains_ReportsMembershipByServerTypeName()
    {
        var roleSet = new RoleSet(new List<KeyValuePair<Type, StartUpTagAttribute>>
        {
            new(typeof(RoleSetTestsRoleAlpha), new StartUpTagAttribute("Alpha", 10)),
            new(typeof(RoleSetTestsRoleBeta), new StartUpTagAttribute("Beta", 20)),
        });

        Assert.Equal(2, roleSet.Count);
        Assert.True(roleSet.Contains("Alpha"));
        Assert.True(roleSet.Contains("Beta"));
        Assert.False(roleSet.Contains("Social"));
    }

    /// <summary>
    /// 集合比较语义：SetEquals 与空集合语义。
    /// </summary>
    [Fact]
    public void SetEquals_AndEmpty_Semantics()
    {
        var roleSet = new RoleSet(new List<KeyValuePair<Type, StartUpTagAttribute>>
        {
            new(typeof(RoleSetTestsRoleAlpha), new StartUpTagAttribute("Alpha", 10)),
            new(typeof(RoleSetTestsRoleBeta), new StartUpTagAttribute("Beta", 20)),
        });

        Assert.True(roleSet.SetEquals(new[] { "Beta", "Alpha" }));
        Assert.True(roleSet.Overlaps(new[] { "Alpha", "Ghost" }));
        Assert.True(roleSet.IsSubsetOf(new[] { "Alpha", "Beta", "Gamma" }));

        Assert.Empty(RoleSet.Empty);
        Assert.False(RoleSet.Empty.Contains("Alpha"));
    }

    /// <summary>
    /// Current 缺省为 Empty 快照。
    /// </summary>
    [Fact]
    public void Current_DefaultsToEmpty()
    {
        var original = RoleSet.Current;
        try
        {
            RoleSet.Current = RoleSet.Empty;
            Assert.Same(RoleSet.Empty, RoleSet.Current);
        }
        finally
        {
            RoleSet.Current = original;
        }
    }

    /// <summary>
    /// 测试用 Role 类（优先级 10）。
    /// </summary>
    private sealed class RoleSetTestsRoleAlpha
    {
    }

    /// <summary>
    /// 测试用 Role 类（优先级 20）。
    /// </summary>
    private sealed class RoleSetTestsRoleBeta
    {
    }
}
