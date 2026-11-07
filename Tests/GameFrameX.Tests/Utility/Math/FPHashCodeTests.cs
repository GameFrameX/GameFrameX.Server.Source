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

using System.Collections.Generic;
using GameFrameX.Utility.Math;

namespace GameFrameX.Tests.Utility.Math;

/// <summary>
/// 定点向量/矩阵哈希质量回归测试（CR-M13）。
/// </summary>
public class FPHashCodeTests
{
    /// <summary>
    /// 分量和相同的向量必须散列到不同桶（旧实现 (int)(x + y) 全碰撞）。
    /// </summary>
    [Fact]
    public void Vector2_ComponentOrder_AffectsHash()
    {
        Assert.NotEqual(new FPVector2(1, 2).GetHashCode(), new FPVector2(2, 1).GetHashCode());
        Assert.NotEqual(new FPVector2(1, 2).GetHashCode(), new FPVector2(3, 0).GetHashCode());
    }

    /// <summary>
    /// XOR 交换不变：分量置换必须散列到不同桶。
    /// </summary>
    [Fact]
    public void Vector3_ComponentPermutation_AffectsHash()
    {
        Assert.NotEqual(new FPVector3(1, 2, 3).GetHashCode(), new FPVector3(3, 2, 1).GetHashCode());
    }

    /// <summary>
    /// 四维向量与四元数的分量置换必须改变哈希。
    /// </summary>
    [Fact]
    public void Vector4AndQuaternion_ComponentPermutation_AffectsHash()
    {
        Assert.NotEqual(new FPVector4(1, 2, 3, 4).GetHashCode(), new FPVector4(4, 3, 2, 1).GetHashCode());
        Assert.NotEqual(new FPQuaternion(1, 2, 3, 4).GetHashCode(), new FPQuaternion(4, 3, 2, 1).GetHashCode());
    }

    /// <summary>
    /// 矩阵元素置换必须改变哈希。
    /// </summary>
    [Fact]
    public void Matrix_Permutation_AffectsHash()
    {
        Assert.NotEqual(new FPMatrix(1, 2, 3, 4, 5, 6, 7, 8, 9).GetHashCode(),
                        new FPMatrix(9, 8, 7, 6, 5, 4, 3, 2, 1).GetHashCode());
        Assert.NotEqual(new FPMatrix4x4(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16).GetHashCode(),
                        new FPMatrix4x4(16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1).GetHashCode());
    }

    /// <summary>
    /// 相等对象必须同哈希（Equals/GetHashCode 契约）。
    /// </summary>
    [Fact]
    public void EqualValues_ShareHashCode()
    {
        Assert.Equal(new FPVector2(3, 4).GetHashCode(), new FPVector2(3, 4).GetHashCode());
        Assert.Equal(new FPVector3(3, 4, 5).GetHashCode(), new FPVector3(3, 4, 5).GetHashCode());
        Assert.Equal(new FPQuaternion(3, 4, 5, 6).GetHashCode(), new FPQuaternion(3, 4, 5, 6).GetHashCode());
        Assert.Equal(new FPMatrix(1, 2, 3, 4, 5, 6, 7, 8, 9).GetHashCode(), new FPMatrix(1, 2, 3, 4, 5, 6, 7, 8, 9).GetHashCode());
    }

    /// <summary>
    /// 1 万个随机 V2 的冲突率应低于 5%（旧实现按分量和分桶，冲突率远超该值）。
    /// </summary>
    [Fact]
    public void Vector2_RandomSamples_CollisionRateBelowFivePercent()
    {
        const int sampleCount = 10000;
        var random = new System.Random(1234);
        var hashes = new HashSet<int>(sampleCount);

        for (var i = 0; i < sampleCount; i++)
        {
            hashes.Add(new FPVector2(random.Next(-1000, 1000), random.Next(-1000, 1000)).GetHashCode());
        }

        Assert.True(hashes.Count >= sampleCount * 95 / 100, $"哈希冲突过多：仅 {hashes.Count} 个不同哈希");
    }
}
