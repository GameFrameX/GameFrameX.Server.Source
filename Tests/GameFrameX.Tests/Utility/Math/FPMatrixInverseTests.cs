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
/// FPMatrix 求逆回归测试（CR-M09：单求逆实现 + 奇异哨兵）。
/// </summary>
public class FPMatrixInverseTests
{
    /// <summary>
    /// 单位矩阵求逆仍为单位矩阵。
    /// </summary>
    [Fact]
    public void Invert_Identity_ReturnsIdentity()
    {
        var identity = FPMatrix.Identity;

        FPMatrix.Invert(ref identity, out var inverse);

        Assert.Equal(FP.One, inverse.M11);
        Assert.Equal(FP.One, inverse.M22);
        Assert.Equal(FP.One, inverse.M33);
        Assert.Equal(FP.Zero, inverse.M12);
        Assert.Equal(FP.Zero, inverse.M13);
        Assert.Equal(FP.Zero, inverse.M21);
        Assert.Equal(FP.Zero, inverse.M23);
        Assert.Equal(FP.Zero, inverse.M31);
        Assert.Equal(FP.Zero, inverse.M32);
    }

    /// <summary>
    /// 对角矩阵求逆得到对角倒数（旧 Invert 无奇异检查，此处同时验证直除精度）。
    /// </summary>
    [Fact]
    public void Invert_DiagonalMatrix_ReturnsReciprocals()
    {
        var matrix = new FPMatrix(2, 0, 0, 0, 4, 0, 0, 0, 8);

        FPMatrix.Invert(ref matrix, out var inverse);

        Assert.Equal((FP)0.5m, inverse.M11);
        Assert.Equal((FP)0.25m, inverse.M22);
        Assert.Equal((FP)0.125m, inverse.M33);
        Assert.Equal(FP.Zero, inverse.M21);
    }

    /// <summary>
    /// 奇异矩阵（第三行为前两行线性组合）应输出 PositiveInfinity 哨兵，而不是 1/0 产生的垃圾值。
    /// </summary>
    [Fact]
    public void Invert_SingularMatrix_FillsPositiveInfinitySentinel()
    {
        var matrix = new FPMatrix(1, 2, 3, 2, 4, 6, 7, 8, 9);

        FPMatrix.Invert(ref matrix, out var inverse);

        Assert.Equal(FP.PositiveInfinity, inverse.M11);
        Assert.Equal(FP.PositiveInfinity, inverse.M12);
        Assert.Equal(FP.PositiveInfinity, inverse.M13);
        Assert.Equal(FP.PositiveInfinity, inverse.M21);
        Assert.Equal(FP.PositiveInfinity, inverse.M22);
        Assert.Equal(FP.PositiveInfinity, inverse.M23);
        Assert.Equal(FP.PositiveInfinity, inverse.M31);
        Assert.Equal(FP.PositiveInfinity, inverse.M32);
        Assert.Equal(FP.PositiveInfinity, inverse.M33);
    }

    /// <summary>
    /// 已过时的 Inverse 与 Invert 必须是同一实现（不再有 1024 缩放的第二套真相）。
    /// </summary>
    [Fact]
    public void Inverse_DelegatesToInvert()
    {
        var matrix = new FPMatrix(2, 1, 0, 1, 3, 1, 0, 1, 4);

        FPMatrix.Invert(ref matrix, out var viaInvert);

#pragma warning disable CS0618 // 本用例专门验证过时 API 的委托语义
        var viaInverse = FPMatrix.Inverse(matrix);
        FPMatrix.Inverse(ref matrix, out var viaInverseRef);
#pragma warning restore CS0618

        Assert.Equal(viaInvert.M11, viaInverse.M11);
        Assert.Equal(viaInvert.M23, viaInverse.M23);
        Assert.Equal(viaInvert.M33, viaInverse.M33);
        Assert.Equal(viaInvert.M11, viaInverseRef.M11);
        Assert.Equal(viaInvert.M33, viaInverseRef.M33);
    }

    /// <summary>
    /// 非奇异矩阵求逆后与原矩阵相乘应逼近单位矩阵。
    /// </summary>
    [Fact]
    public void Invert_MultipliedByOriginal_ApproximatesIdentity()
    {
        var matrix = new FPMatrix(4, 7, 2, 3, 6, 1, 2, 5, 3);

        FPMatrix.Invert(ref matrix, out var inverse);
        var product = FPMatrix.Multiply(matrix, inverse);

        Assert.True(FP.Abs(product.M11 - FP.One) <= (FP)0.00001m);
        Assert.True(FP.Abs(product.M22 - FP.One) <= (FP)0.00001m);
        Assert.True(FP.Abs(product.M33 - FP.One) <= (FP)0.00001m);
        Assert.True(FP.Abs(product.M12) <= (FP)0.00001m);
        Assert.True(FP.Abs(product.M13) <= (FP)0.00001m);
        Assert.True(FP.Abs(product.M21) <= (FP)0.00001m);
        Assert.True(FP.Abs(product.M23) <= (FP)0.00001m);
        Assert.True(FP.Abs(product.M31) <= (FP)0.00001m);
        Assert.True(FP.Abs(product.M32) <= (FP)0.00001m);
    }
}
