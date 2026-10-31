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
//   Any legal disputes and liabilities arising from secondary development based on this project
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

using GameFrameX.Utility.Math;

namespace GameFrameX.Tests.Utility.Math;

/// <summary>
/// FPRandom 回归测试（CR-M10：懒初始化、球内采样、Next 边界与模偏）。
/// </summary>
public class FPRandomTests
{
    /// <summary>
    /// 未显式调用 Init 时静态成员也必须可用（旧实现直接 NRE）。
    /// </summary>
    [Fact]
    public void Value_BeforeExplicitInit_DoesNotThrow()
    {
        var value = (decimal)FPRandom.value;

        Assert.InRange(value, 0m, 1m);
    }

    /// <summary>
    /// 球内采样：全部样本模长不超过 1（旧实现返回立方体点，模长可达 √3）。
    /// </summary>
    [Fact]
    public void InsideUnitSphere_AllSamplesStayWithinUnitBall()
    {
        for (var i = 0; i < 2000; i++)
        {
            var point = FPRandom.insideUnitSphere;
            var magnitude = (decimal)point.magnitude;

            Assert.True(magnitude <= 1.0001m, $"第 {i} 个样本模长 {magnitude} 超出单位球");
        }
    }

    /// <summary>
    /// 球内采样不应退化为原点或单点：应同时存在靠近中心与外层壳的样本。
    /// </summary>
    [Fact]
    public void InsideUnitSphere_CoversInnerAndOuterRegions()
    {
        var hasInner = false;
        var hasOuter = false;

        for (var i = 0; i < 2000 && !(hasInner && hasOuter); i++)
        {
            var magnitude = (decimal)FPRandom.insideUnitSphere.magnitude;
            hasInner |= magnitude < 0.25m;
            hasOuter |= magnitude > 0.75m;
        }

        Assert.True(hasInner, "未采到靠近球心的样本");
        Assert.True(hasOuter, "未采到靠近球壳的样本");
    }

    /// <summary>
    /// min == max 时返回 min，而不是抛 DivideByZeroException。
    /// </summary>
    [Fact]
    public void Next_WithEqualBounds_ReturnsBoundary()
    {
        var random = FPRandom.New(1);

        Assert.Equal(7, random.Next(7, 7));
        Assert.Equal(-3, random.Next(-3, -3));
    }

    /// <summary>
    /// 三桶抽样应接近均匀（拒绝采样消除模偏）；旧实现的模偏在 6 万次下可被稳定检出。
    /// </summary>
    [Fact]
    public void Next_ThreeBuckets_AreEvenlyDistributed()
    {
        const int sampleCount = 60000;
        var random = FPRandom.New(42);
        var buckets = new int[3];

        for (var i = 0; i < sampleCount; i++)
        {
            buckets[random.Next(0, 3)]++;
        }

        const int expected = sampleCount / 3;
        const int tolerance = expected / 50; // 2%
        foreach (var count in buckets)
        {
            Assert.InRange(count, expected - tolerance, expected + tolerance);
        }
    }

    /// <summary>
    /// 跨度超过 31 位随机域时不应溢出或抛异常（旧实现 range 溢出为负数）。
    /// </summary>
    [Fact]
    public void Next_WithFullIntRange_ReturnsValueInRange()
    {
        var random = FPRandom.New(7);

        for (var i = 0; i < 100; i++)
        {
            var value = random.Next(int.MinValue, int.MaxValue);
            Assert.InRange(value, int.MinValue, int.MaxValue - 1);
        }
    }

    /// <summary>
    /// 静态 Range 在未显式 Init 时也可用。
    /// </summary>
    [Fact]
    public void Range_WithoutExplicitInit_ReturnsValueInRange()
    {
        var value = FPRandom.Range(10, 20);

        Assert.InRange(value, 10, 19);
    }
}
