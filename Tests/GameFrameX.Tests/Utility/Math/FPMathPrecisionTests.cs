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
/// FP 三角函数精度回归测试（CR-M08：Sin 线性插值）。
/// </summary>
public class FPMathPrecisionTests
{
    /// <summary>
    /// 插值后的精度应远优于最近邻查表（最近邻误差约 4e-6，插值误差约 1e-9）。
    /// </summary>
    private static readonly FP Tolerance = (FP)0.000001m;

    /// <summary>
    /// Sin(π/6) 应精确逼近 0.5；最近邻查表实现会偏差约 2e-6。
    /// </summary>
    [Fact]
    public void Sin_AtThirtyDegrees_MatchesHalfWithinInterpolationTolerance()
    {
        var angle = FP.Pi / (FP)6;

        var actual = FP.Sin(angle);

        Assert.True(FP.Abs(actual - (FP)0.5m) <= Tolerance, $"Sin(π/6) = {(decimal)actual}，期望 0.5");
    }

    /// <summary>
    /// 第一、二象限抽样对照 double 参考值，确认插值分支未破坏镜像路径。
    /// </summary>
    [Fact]
    public void Sin_AcrossHalfPeriod_StaysWithinToleranceOfDoubleReference()
    {
        for (var i = 0; i <= 3141; i++)
        {
            var radians = i / 1000.0;
            var actual = (double)(decimal)FP.Sin((FP)(decimal)radians);
            var expected = System.Math.Sin(radians);

            Assert.True(System.Math.Abs(actual - expected) <= 1e-6, $"角度 {radians}：actual={actual} expected={expected}");
        }
    }

    /// <summary>
    /// Sin 的翻转分支（横向镜像）应保持对称性与单调性。
    /// </summary>
    [Fact]
    public void Sin_IsAntisymmetricAndMonotonicOnFirstQuadrant()
    {
        var previous = FP.Zero;
        for (var i = 1; i <= 100; i++)
        {
            var angle = FP.PiOver2 / (FP)(101 * 2) * (FP)i;
            var value = FP.Sin(angle);
            Assert.True(value >= previous, $"Sin 在 {i} 步非单调：{ (decimal)value } < { (decimal)previous }");
            Assert.True(FP.Abs(FP.Sin(-angle) + value) <= Tolerance, $"Sin 在 {i} 步不满足奇对称");
            previous = value;
        }
    }

    /// <summary>
    /// 全周期角度扫描：Sin 不越界、不抛异常，结果恒在 [-1, 1]。
    /// 覆盖 LUT 顶格、翻转边界与负角度镜像路径。
    /// </summary>
    [Fact]
    public void Sin_OverFullPeriod_StaysBoundedAndDoesNotThrow()
    {
        for (var i = -20000; i <= 20000; i++)
        {
            var angle = (FP)(decimal)(i / 1000.0);

            var value = FP.Sin(angle);
            Assert.True(value >= -FP.One && value <= FP.One, $"Sin({i / 1000.0}) = {(decimal)value} 越界");
            _ = FP.Cos(angle);
        }
    }

    /// <summary>
    /// Sqrt 负数入参：本地化消息必须进 Message、ParamName 必须是 "x"（CR-L05）。
    /// </summary>
    [Fact]
    public void Sqrt_WithNegativeValue_ReportsParamNameAndMessage()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => FP.Sqrt((FP)(-1)));

        Assert.Equal("x", exception.ParamName);
        Assert.DoesNotContain("{", exception.Message);
    }

    /// <summary>
    /// Acos 越界入参：本地化消息必须进 Message、ParamName 必须是 "x"（CR-L05）。
    /// </summary>
    [Fact]
    public void Acos_WithOutOfRangeValue_ReportsParamNameAndMessage()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => FP.Acos((FP)2));

        Assert.Equal("x", exception.ParamName);
        Assert.DoesNotContain("{", exception.Message);
    }
}
