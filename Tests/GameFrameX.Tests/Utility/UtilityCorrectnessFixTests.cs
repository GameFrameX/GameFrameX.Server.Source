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
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Utility;
using GameFrameX.Utility.Math;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.Utility;

/// <summary>
/// GFX-1174（C434）Utility 核心正确性修复的回归测试：
/// ActorIdGenerator 上界与解析域校验（CR-M01）、LNumber 除法负大被除数与除零（CR-M04）、
/// FP 除零符号（CR-M11）、FPVector2/3/4 零向量归一化与 ClampMagnitude（CR-M11）。
/// </summary>
public class UtilityCorrectnessFixTests
{
    #region CR-M01 ActorIdGenerator

    [Theory]
    [InlineData(10000)]
    [InlineData(16384)]
    public void GetActorId_WithServerIdAboveMax_Throws(int serverId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ActorIdGenerator.GetActorId(GlobalConst.ActorTypePlayer, serverId));
    }

    [Fact]
    public void GetActorId_WithActorTypeNone_Throws()
    {
        Assert.Throws<ArgumentException>(() => ActorIdGenerator.GetActorId(GlobalConst.ActorTypeNone, 1000));
    }

    [Fact]
    public void GetActorId_WithMaxServerId_DoesNotProduceNegativeId()
    {
        var actorId = ActorIdGenerator.GetActorId(GlobalConst.ActorTypePlayer, GlobalConst.MaxServerId);
        Assert.True(actorId > 0);
        Assert.Equal(GlobalConst.MaxServerId, ActorIdGenerator.GetServerId(actorId));
    }

    [Fact]
    public void GetServerId_OnCorruptedLayout_Throws()
    {
        // serverId 位段 = 10000（> MaxServerId 9999）：位布局已损坏，解析点必须报错而不是给出错误答案
        var corruptedActorId = 10000L << GlobalConst.ServerIdOrModuleIdMask;
        Assert.Throws<ArgumentException>(() => ActorIdGenerator.GetServerId(corruptedActorId));
        Assert.Throws<ArgumentException>(() => ActorIdGenerator.GetActorType(corruptedActorId));
    }

    [Fact]
    public void GetServerId_OnValidLayout_RoundTrips()
    {
        var actorId = ActorIdGenerator.GetActorId(GlobalConst.ActorTypePlayer, 1000);
        Assert.Equal(1000, ActorIdGenerator.GetServerId(actorId));
        Assert.Equal(GlobalConst.ActorTypePlayer, ActorIdGenerator.GetActorType(actorId));
    }

    #endregion

    #region CR-M04 LNumber 除法

    [Fact]
    public void Divide_NegativeLargeDividend_ReturnsCorrectQuotient()
    {
        // 负大被除数（|Raw| ≥ 2^48）此前绕过 BigInteger 路径，左移 15 位溢出得到错值
        var lhs = new LNumber { Raw = -(1L << 50) };
        var rhs = new LNumber { Raw = 2L << LNumber.FRACTION_BITS }; // 数值 2

        var result = lhs / rhs;

        // BigInteger 参考值：((-2^50 << 15) / 2^15 + 1) >> 1 = -2^49
        Assert.Equal(-(1L << 49), result.Raw);
    }

    [Fact]
    public void Divide_ByZero_Throws()
    {
        var lhs = new LNumber { Raw = 1L << LNumber.FRACTION_BITS };
        Assert.Throws<DivideByZeroException>(() => lhs / LNumber.Zero);
    }

    [Fact]
    public void Mod_ByZero_Throws()
    {
        var lhs = new LNumber { Raw = 1L << LNumber.FRACTION_BITS };
        Assert.Throws<DivideByZeroException>(() => lhs % LNumber.Zero);
    }

    #endregion

    #region CR-M11 FP 除零符号与向量零向量

    [Fact]
    public void Division_NegativeByZero_ShouldReturnMinValue()
    {
        FP fpNegative = -1;
        FP fpZero = 0;

        var result = fpNegative / fpZero;

        Assert.Equal(FP.MinValue.RawValue, result.RawValue);
    }

    [Fact]
    public void FPVector2_Normalize_ZeroVector_ReturnsZero()
    {
        var normalized = FPVector2.Normalize(new FPVector2(FP.Zero, FP.Zero));
        Assert.Equal(0, normalized.x.RawValue);
        Assert.Equal(0, normalized.y.RawValue);

        var clamped = FPVector2.ClampMagnitude(new FPVector2(FP.Zero, FP.Zero), 10);
        Assert.Equal(0, clamped.x.RawValue);
        Assert.Equal(0, clamped.y.RawValue);
    }

    [Fact]
    public void FPVector3_Normalize_ZeroVector_ReturnsZero()
    {
        var normalized = FPVector3.Normalize(new FPVector3(FP.Zero, FP.Zero, FP.Zero));
        Assert.Equal(0, normalized.x.RawValue);
        Assert.Equal(0, normalized.y.RawValue);
        Assert.Equal(0, normalized.z.RawValue);

        var clamped = FPVector3.ClampMagnitude(new FPVector3(FP.Zero, FP.Zero, FP.Zero), 10);
        Assert.Equal(0, clamped.x.RawValue);
        Assert.Equal(0, clamped.y.RawValue);
        Assert.Equal(0, clamped.z.RawValue);
    }

    [Fact]
    public void FPVector4_Normalize_ZeroVector_ReturnsZero()
    {
        var normalized = FPVector4.Normalize(new FPVector4(FP.Zero, FP.Zero, FP.Zero, FP.Zero));
        Assert.Equal(0, normalized.x.RawValue);
        Assert.Equal(0, normalized.y.RawValue);
        Assert.Equal(0, normalized.z.RawValue);
        Assert.Equal(0, normalized.w.RawValue);

        var clamped = FPVector4.ClampMagnitude(new FPVector4(FP.Zero, FP.Zero, FP.Zero, FP.Zero), 10);
        Assert.Equal(0, clamped.x.RawValue);
        Assert.Equal(0, clamped.y.RawValue);
        Assert.Equal(0, clamped.z.RawValue);
        Assert.Equal(0, clamped.w.RawValue);
    }

    [Fact]
    public void FPVector3_Normalize_NonZeroVector_StillUnitLength()
    {
        var normalized = FPVector3.Normalize(new FPVector3(3, 4, 0));
        var length = FP.Sqrt(normalized.x * normalized.x + normalized.y * normalized.y + normalized.z * normalized.z);
        Assert.True(FP.Abs(length - FP.One) < FP.One / 10);
    }

    #endregion
}
