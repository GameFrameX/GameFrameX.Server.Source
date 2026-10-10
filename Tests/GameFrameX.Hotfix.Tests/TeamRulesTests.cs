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

using GameFrameX.Hotfix.Logic.ServerRole.Team;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Team 业务纯规则测试（C197 Phase 2）。
/// </summary>
/// <remarks>
/// 覆盖名称 / 容量校验边界、离队后队长转移（最早加入成员）与空队解散条件。
/// </remarks>
public class TeamRulesTests
{
    /// <summary>
    /// 名称合法性：空白拒绝、边界长度接受、超长拒绝。
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("开黑队", true)]
    public void IsNameValid_BoundaryCases(string name, bool expected)
    {
        Assert.Equal(expected, TeamRules.IsNameValid(name));
    }

    /// <summary>
    /// 名称达到上限长度（32）仍合法，超限（33）非法。
    /// </summary>
    [Fact]
    public void IsNameValid_LengthLimit()
    {
        Assert.True(TeamRules.IsNameValid(new string('a', TeamRules.MaxNameLength)));
        Assert.False(TeamRules.IsNameValid(new string('a', TeamRules.MaxNameLength + 1)));
    }

    /// <summary>
    /// 容量合法性：区间 [2, 10]，边界值接受，越界拒绝。
    /// </summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void IsCapacityValid_Boundaries(int maxMemberCount, bool expected)
    {
        Assert.Equal(expected, TeamRules.IsCapacityValid(maxMemberCount));
    }

    /// <summary>
    /// 队长转移：取离队后成员列表首位（最早加入），空队返回 0。
    /// </summary>
    [Fact]
    public void ResolveNewLeader_TakesEarliestMember()
    {
        var members = new List<long> { 2, 3, 4 };
        Assert.Equal(2, TeamRules.ResolveNewLeader(members));
        Assert.Equal(0, TeamRules.ResolveNewLeader(new List<long>()));
    }

    /// <summary>
    /// 解散条件：剩余成员为 0 才解散，否则转移。
    /// </summary>
    [Fact]
    public void LeaveOutcome_DisbandsOnlyWhenEmpty()
    {
        Assert.True(TeamRules.LeaveOutcome(new List<long>()));
        Assert.False(TeamRules.LeaveOutcome(new List<long> { 2 }));
    }
}
