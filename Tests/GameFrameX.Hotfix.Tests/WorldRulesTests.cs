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

using GameFrameX.Apps.ServerRole.World.Entity;
using GameFrameX.Hotfix.Logic.ServerRole.World;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// World 业务纯规则测试（C197 Phase 2）。
/// </summary>
/// <remarks>
/// 覆盖标题合法性边界、过期时间校验与公告过期判定。
/// </remarks>
public class WorldRulesTests
{
    /// <summary>
    /// 标题合法性：空白与超长拒绝、边界长度接受。
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("维护公告", true)]
    public void IsTitleValid_BoundaryCases(string title, bool expected)
    {
        Assert.Equal(expected, WorldRules.IsTitleValid(title));
    }

    /// <summary>
    /// 标题达到上限长度（64）仍合法，超限（65）非法。
    /// </summary>
    [Fact]
    public void IsTitleValid_LengthLimit()
    {
        Assert.True(WorldRules.IsTitleValid(new string('标', WorldRules.MaxTitleLength)));
        Assert.False(WorldRules.IsTitleValid(new string('标', WorldRules.MaxTitleLength + 1)));
    }

    /// <summary>
    /// 发布过期时间校验：须严格晚于当前时间。
    /// </summary>
    [Theory]
    [InlineData(1001, 1000, true)]
    [InlineData(1000, 1000, false)]
    [InlineData(999, 1000, false)]
    [InlineData(0, 1000, false)]
    public void IsExpireValid_MustBeFuture(long expireUnixTime, long now, bool expected)
    {
        Assert.Equal(expected, WorldRules.IsExpireValid(expireUnixTime, now));
    }

    /// <summary>
    /// 公告过期判定：当前时间达到过期时间即视为过期。
    /// </summary>
    [Fact]
    public void IsExpired_AtOrAfterExpireTime()
    {
        var announcement = new WorldAnnouncementState { ExpireUnixTime = 2000 };
        Assert.False(WorldRules.IsExpired(announcement, 1999));
        Assert.True(WorldRules.IsExpired(announcement, 2000));
        Assert.True(WorldRules.IsExpired(announcement, 2001));
    }

    /// <summary>
    /// 发布过期时间上限：距今不超过 30 天（2,592,000 秒），边界当天整秒通过、超 1 秒拒绝。
    /// </summary>
    [Fact]
    public void IsExpireValid_CappedAtThirtyDays()
    {
        const long now = 1_000_000;
        Assert.True(WorldRules.IsExpireValid(now + WorldRules.MaxExpireSeconds, now));
        Assert.False(WorldRules.IsExpireValid(now + WorldRules.MaxExpireSeconds + 1, now));
    }
}
