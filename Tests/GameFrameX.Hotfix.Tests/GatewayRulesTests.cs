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

using System.Collections.Generic;
using GameFrameX.Apps.ServerRole.Gateway.Entity;
using GameFrameX.Hotfix.Logic.ServerRole.Gateway;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Gateway 业务纯规则测试（C197 Phase 2）。
/// </summary>
/// <remarks>
/// 覆盖权重合法性、心跳存活边界与可用集加权随机选择（注入 roll/now，确定性）。
/// </remarks>
public class GatewayRulesTests
{
    /// <summary>
    /// 权重合法性：大于 0 接受，0 与负数拒绝。
    /// </summary>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    public void IsWeightValid_BoundaryCases(int weight, bool expected)
    {
        Assert.Equal(expected, GatewayRules.IsWeightValid(weight));
    }

    /// <summary>
    /// 心跳存活边界：距最近心跳恰 30 秒仍存活，31 秒失活。
    /// </summary>
    [Fact]
    public void IsAlive_Boundary()
    {
        var entry = new TargetEntryState { LastHeartbeatUnixTime = 1000 };

        Assert.True(GatewayRules.IsAlive(entry, 1000 + GatewayRules.HeartbeatTimeoutSeconds));
        Assert.False(GatewayRules.IsAlive(entry, 1000 + GatewayRules.HeartbeatTimeoutSeconds + 1));
    }

    /// <summary>
    /// 加权选择：无候选或全部心跳超时返回 null。
    /// </summary>
    [Fact]
    public void PickTarget_ReturnsNullWhenNoneAlive()
    {
        var now = 5000L;
        Assert.Null(GatewayRules.PickTarget(new List<TargetEntryState>(), now, 0.0));

        var stale = new List<TargetEntryState>
        {
            new TargetEntryState { TargetId = 1, Weight = 10, LastHeartbeatUnixTime = now - GatewayRules.HeartbeatTimeoutSeconds - 1 },
        };
        Assert.Null(GatewayRules.PickTarget(stale, now, 0.99));
    }

    /// <summary>
    /// 加权选择：单一存活候选无论 roll 取值必中。
    /// </summary>
    [Fact]
    public void PickTarget_SingleCandidateAlwaysPicked()
    {
        var now = 5000L;
        var only = new TargetEntryState { TargetId = 7, Weight = 1, LastHeartbeatUnixTime = now };
        var candidates = new List<TargetEntryState> { only };

        Assert.Same(only, GatewayRules.PickTarget(candidates, now, 0.0));
        Assert.Same(only, GatewayRules.PickTarget(candidates, now, 0.999));
    }

    /// <summary>
    /// 加权选择：权重 3:1 时 roll 按累积权重区间落位（roll&lt;0.75 命中前者，否则命中后者）。
    /// </summary>
    [Fact]
    public void PickTarget_WeightedCumulativeSelection()
    {
        var now = 5000L;
        var heavy = new TargetEntryState { TargetId = 1, Weight = 3, LastHeartbeatUnixTime = now };
        var light = new TargetEntryState { TargetId = 2, Weight = 1, LastHeartbeatUnixTime = now };
        var candidates = new List<TargetEntryState> { heavy, light };

        Assert.Same(heavy, GatewayRules.PickTarget(candidates, now, 0.0));
        Assert.Same(heavy, GatewayRules.PickTarget(candidates, now, 0.7499));
        Assert.Same(light, GatewayRules.PickTarget(candidates, now, 0.75));
        Assert.Same(light, GatewayRules.PickTarget(candidates, now, 0.9999));
    }

    /// <summary>
    /// 加权选择：心跳超时的候选不参与权重累积（存活低权重目标可被选中）。
    /// </summary>
    [Fact]
    public void PickTarget_SkipsStaleCandidates()
    {
        var now = 5000L;
        var stale = new TargetEntryState { TargetId = 1, Weight = 100, LastHeartbeatUnixTime = now - GatewayRules.HeartbeatTimeoutSeconds - 5 };
        var alive = new TargetEntryState { TargetId = 2, Weight = 1, LastHeartbeatUnixTime = now };
        var candidates = new List<TargetEntryState> { stale, alive };

        Assert.Same(alive, GatewayRules.PickTarget(candidates, now, 0.0));
        Assert.Same(alive, GatewayRules.PickTarget(candidates, now, 0.9999));
    }
}
