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

using GameFrameX.Hotfix.Logic.ServerRole.Guild;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Guild 业务纯规则测试（C197 Phase 2）。
/// </summary>
/// <remarks>
/// 覆盖公会名称校验边界与经验升级阈值换算（等级 = 1 + 经验 / 100）。
/// </remarks>
public class GuildRulesTests
{
    /// <summary>
    /// 名称合法性：空白与长度越界（&lt;2 或 &gt;16）拒绝，边界长度接受。
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("a", false)]
    [InlineData("ab", true)]
    [InlineData("王者公会", true)]
    public void IsGuildNameValid_BoundaryCases(string name, bool expected)
    {
        Assert.Equal(expected, GuildRules.IsGuildNameValid(name));
    }

    /// <summary>
    /// 名称达到上限长度（16）仍合法，超限（17）非法。
    /// </summary>
    [Fact]
    public void IsGuildNameValid_LengthLimit()
    {
        Assert.True(GuildRules.IsGuildNameValid(new string('a', GuildRules.MaxNameLength)));
        Assert.False(GuildRules.IsGuildNameValid(new string('a', GuildRules.MaxNameLength + 1)));
    }

    /// <summary>
    /// 经验升级阈值：0 经验为 1 级，每累计 100 经验升一级（累计阈值口径）。
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(199, 2)]
    [InlineData(1000, 11)]
    public void ExpToLevel_CumulativeThresholds(long exp, int expectedLevel)
    {
        Assert.Equal(expectedLevel, GuildRules.ExpToLevel(exp));
    }

    /// <summary>
    /// 审批经验增量：每次审批固定 +10，10 次审批（100 经验）恰好升到 2 级。
    /// </summary>
    [Fact]
    public void ApproveExpGain_TenApprovalsReachLevelTwo()
    {
        long exp = 0;
        for (var approve = 1; approve <= 10; approve++)
        {
            exp += GuildRules.ApplyExpGain;
        }

        Assert.Equal(100, exp);
        Assert.Equal(2, GuildRules.ExpToLevel(exp));
    }
}
