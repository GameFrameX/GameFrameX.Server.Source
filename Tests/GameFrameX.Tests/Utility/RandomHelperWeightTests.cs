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

using System;
using System.Collections.Generic;
using GameFrameX.Utility;

namespace GameFrameX.Tests.Utility;

/// <summary>
/// RandomHelper 权重随机与无分配随机数回归测试（CR-M12 / CR-L07）。
/// </summary>
public class RandomHelperWeightTests
{
    /// <summary>
    /// 负权重必须在入口被拒绝，并指出非法权重值。
    /// </summary>
    [Fact]
    public void Idx_WithNegativeWeight_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => RandomHelper.Idx(new[] { 1, -2, 3 }));

        Assert.Contains("-2", exception.Message);
    }

    /// <summary>
    /// 全 0 权重无可选目标，必须显式报错而非抛随机库内部异常。
    /// </summary>
    [Fact]
    public void Idx_WithAllZeroWeights_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => RandomHelper.Idx(new[] { 0, 0, 0 }));
    }

    /// <summary>
    /// 权重求和超出 int 上限时必须拒绝（旧实现静默回绕成错误分布）。
    /// </summary>
    [Fact]
    public void Idx_WithWeightSumOverflow_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => RandomHelper.Idx(new[] { int.MaxValue, int.MaxValue }));
    }

    /// <summary>
    /// weightIndex 超出条目长度时给出带索引与长度的明确异常。
    /// </summary>
    [Fact]
    public void Idx_WithWeightIndexOutOfRange_ThrowsArgumentException()
    {
        var array = new[] { new[] { 1, 2 }, new[] { 3, 4 } };

        var exception = Assert.Throws<ArgumentException>(() => RandomHelper.Idx(array, 5));

        Assert.Contains("5", exception.Message);
    }

    /// <summary>
    /// Ids 的权重重载同样受权重校验保护。
    /// </summary>
    [Fact]
    public void Ids_WithNegativeWeight_ThrowsArgumentException()
    {
        var array = new[] { new[] { 1, 1 }, new[] { 2, -1 } };

        // 显式转成 IReadOnlyList，避免命中泛型 Ids<T>(T[], int) 的「按普通元素抽样」重载。
        Assert.Throws<ArgumentException>(() => RandomHelper.Ids((IReadOnlyList<int[]>)array, 1));
    }

    /// <summary>
    /// 合法权重返回合法索引。
    /// </summary>
    [Fact]
    public void Idx_WithValidWeights_ReturnsIndexInRange()
    {
        var weights = new[] { 1, 2, 3 };

        for (var i = 0; i < 1000; i++)
        {
            Assert.InRange(RandomHelper.Idx(weights), 0, weights.Length - 1);
        }
    }

    /// <summary>
    /// 不可重复抽取的正权重条目不足时必须 fail fast（0 权重条目永不被选中）。
    /// </summary>
    [Fact]
    public void Items_NoRepeat_WithInsufficientPositiveWeights_ThrowsArgumentException()
    {
        var array = new[] { new[] { 1, 0, 5 }, new[] { 2, 0, 0 } };

        var exception = Assert.Throws<ArgumentException>(() => RandomHelper.Items((IReadOnlyList<int[]>)array, 2, false));

        Assert.Contains("1", exception.Message);
    }

    /// <summary>
    /// 正权重条目足够时，0 权重条目不会被选中。
    /// </summary>
    [Fact]
    public void Items_NoRepeat_WithZeroWeightEntry_SelectsOnlyPositiveWeights()
    {
        var array = new[] { new[] { 1, 0, 5 }, new[] { 2, 0, 0 }, new[] { 3, 0, 5 } };

        var selected = RandomHelper.Items((IReadOnlyList<int[]>)array, 2, false);

        Assert.Equal(2, selected.Count);
        Assert.DoesNotContain(selected, entry => entry[0] == 2);
        Assert.NotEqual(selected[0][0], selected[1][0]);
    }

    /// <summary>
    /// 大 m 抽样不再线性递归，不触发栈溢出。
    /// </summary>
    [Fact]
    public void RandomSelect_LargeCount_DoesNotOverflowStack()
    {
        var selected = RandomHelper.RandomSelect(100_000, 100_001);

        Assert.Equal(100_000, selected.Count);
        Assert.All(selected, value => Assert.InRange(value, 0, 100_000));
    }

    /// <summary>
    /// 小规模抽样仍保持互异且落在 [0, n) 内。
    /// </summary>
    [Fact]
    public void RandomSelect_ReturnsDistinctValuesInRange()
    {
        var selected = RandomHelper.RandomSelect(5, 10);

        Assert.Equal(5, selected.Count);
        Assert.All(selected, value => Assert.InRange(value, 0, 9));
    }

    /// <summary>
    /// NextUInt32/NextUInt64 必须零堆分配（旧实现每次 new byte[]）。
    /// </summary>
    [Fact]
    public void NextUInt32AndNextUInt64_DoNotAllocatePerCall()
    {
        for (var i = 0; i < 2000; i++)
        {
            _ = RandomHelper.NextUInt32();
            _ = RandomHelper.NextUInt64();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = RandomHelper.NextUInt32();
            _ = RandomHelper.NextUInt64();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1024, $"1000 次无符号随机分配了 {allocated} 字节");
    }
}
