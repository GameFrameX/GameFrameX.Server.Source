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

using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Localization.Core;
using Tedd.RandomUtils;

namespace GameFrameX.Utility;

/// <summary>
/// 随机数帮助类
/// 
/// 功能概述：
/// - 提供线程安全的随机数生成功能
/// - 支持多种数据类型的随机数生成（int、long、double、byte[]等）
/// - 提供便捷的集合随机选择方法
/// - 支持随机选择单个或多个元素
/// - 支持获取随机索引
/// 
/// 核心特性：
/// - 线程安全：使用 Random.Shared 确保多线程环境下的安全性
/// - 高性能：避免重复创建 Random 实例
/// - 类型安全：支持泛型方法，编译时类型检查
/// - 参数验证：使用 .NET 静态方法进行参数检查
/// 
/// 应用场景：
/// - 游戏开发中的随机事件生成
/// - 数据采样和随机测试
/// - 随机算法实现
/// - 集合元素的随机选择
/// 
/// 使用示例：
/// <code>
/// // 生成随机整数
/// int randomInt = RandomHelper.Next(100);
/// 
/// // 从数组中随机选择元素
/// string[] items = {"A", "B", "C"};
/// string selected = RandomHelper.RandomSelect(items);
/// 
/// // 随机选择多个元素
/// string[] multipleItems = RandomHelper.Items(items, 2);
/// </code>
/// 
/// 注意事项：
/// - 所有方法都进行了严格的参数验证
/// - 空集合或数组会抛出 ArgumentOutOfRangeException
/// - 负数参数会抛出相应的异常
/// </summary>
public static class RandomHelper
{
    private static readonly Random RandomShared = Random.Shared;
    /*/// <summary>
    /// 设置随机数种子。
    /// </summary>
    /// <param name="seed">随机数种子。</param>
    public static void SetSeed(int seed)
    {
        _random = new System.Random(seed);
    }*/

    /// <summary>
    /// 获取UInt32范围内的随机数。
    /// </summary>
    /// <returns>一个大于等于零且小于 UInt32.MaxValue 的 32 位无符号整数。</returns>
    public static uint NextUInt32()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomShared.NextBytes(bytes);
        return BitConverter.ToUInt32(bytes);
    }

    /// <summary>
    /// 返回非负随机数。
    /// </summary>
    /// <returns>大于等于零且小于 System.Int32.MaxValue 的 32 位带符号整数。</returns>
    public static int Next()
    {
        return RandomShared.Next();
    }

    /// <summary>
    /// 返回一个小于所指定最大值的非负随机数。
    /// </summary>
    /// <param name="maxValue">要生成的随机数的上界（随机数不能取该上界值）。maxValue 必须大于等于零。</param>
    /// <returns>大于等于零且小于 maxValue 的 32 位带符号整数，即：返回值的范围通常包括零但不包括 maxValue。不过，如果 maxValue 等于零，则返回 maxValue。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 maxValue 小于 0 时抛出此异常</exception>
    public static int Next(int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return RandomShared.Next(maxValue);
    }

    /// <summary>
    /// 返回一个指定范围内的随机数。
    /// </summary>
    /// <param name="minValue">返回的随机数的下界（随机数可取该下界值）。</param>
    /// <param name="maxValue">返回的随机数的上界（随机数不能取该上界值）。maxValue 必须大于等于 minValue。</param>
    /// <returns>一个大于等于 minValue 且小于 maxValue 的 32 位带符号整数，即：返回的值范围包括 minValue 但不包括 maxValue。如果 minValue 等于 maxValue，则返回 minValue。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 minValue 大于 maxValue 时抛出此异常</exception>
    public static int Next(int minValue, int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        return RandomShared.Next(minValue, maxValue);
    }

    /// <summary>
    /// 返回非负随机数。
    /// </summary>
    /// <returns>大于等于零且小于 System.Int64.MaxValue 的 64 位带符号整数。</returns>
    public static long NextInt64()
    {
        return RandomShared.NextInt64();
    }

    /// <summary>
    /// 返回一个小于所指定最大值的非负随机数。
    /// </summary>
    /// <param name="maxValue">要生成的随机数的上界（随机数不能取该上界值）。maxValue 必须大于等于零。</param>
    /// <returns>大于等于零且小于 maxValue 的 64 位带符号整数，即：返回值的范围通常包括零但不包括 maxValue。不过，如果 maxValue 等于零，则返回 maxValue。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 maxValue 小于 0 时抛出此异常</exception>
    public static long NextInt64(int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return RandomShared.NextInt64(maxValue);
    }

    /// <summary>
    /// 返回一个指定范围内的随机数。
    /// </summary>
    /// <param name="minValue">返回的随机数的下界（随机数可取该下界值）。</param>
    /// <param name="maxValue">返回的随机数的上界（随机数不能取该上界值）。maxValue 必须大于等于 minValue。</param>
    /// <returns>一个大于等于 minValue 且小于 maxValue 的 64 位带符号整数，即：返回的值范围包括 minValue 但不包括 maxValue。如果 minValue 等于 maxValue，则返回 minValue。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 minValue 大于 maxValue 时抛出此异常</exception>
    public static long NextInt64(long minValue, long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        return RandomShared.NextInt64(minValue, maxValue);
    }

    /// <summary>
    /// 获取UInt64范围内的随机数。
    /// </summary>
    /// <returns>一个大于等于零且小于 UInt64.MaxValue 的 64 位无符号整数。</returns>
    public static ulong NextUInt64()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomShared.NextBytes(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    /// <summary>
    /// 返回一个介于 0.0 和 1.0 之间的随机数。
    /// </summary>
    /// <returns>大于等于 0.0 并且小于 1.0 的双精度浮点数。</returns>
    public static double NextDouble()
    {
        return RandomShared.NextDouble();
    }

    /// <summary>
    /// 返回一个小于所指定最大值的非负随机数。
    /// </summary>
    /// <param name="maxValue">要生成的随机数的上界（随机数不能取该上界值）。maxValue 必须大于等于零。</param>
    /// <returns>大于等于 0.0 且小于 maxValue 的双精度浮点数。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 maxValue 小于 0 时抛出此异常</exception>
    public static double NextDouble(double maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return RandomShared.NextDouble() * maxValue;
    }

    /// <summary>
    /// 返回一个指定范围内的随机数。
    /// </summary>
    /// <param name="minValue">返回的随机数的下界（随机数可取该下界值）。</param>
    /// <param name="maxValue">返回的随机数的上界（随机数不能取该上界值）。maxValue 必须大于等于 minValue。</param>
    /// <returns>一个大于等于 minValue 且小于 maxValue 的双精度浮点数。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 minValue 大于 maxValue 时抛出此异常</exception>
    public static double NextDouble(double minValue, double maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        return RandomShared.NextDouble() * (maxValue - minValue) + minValue;
    }

    /// <summary>
    /// 返回一个小于所指定最大值的非负随机数。
    /// </summary>
    /// <param name="maxValue">要生成的随机数的上界（随机数不能取该上界值）。maxValue 必须大于等于零。</param>
    /// <returns>大于等于零且小于 maxValue 的 64 位带符号整数。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 maxValue 小于 0 时抛出此异常</exception>
    public static long NextLong(long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return RandomShared.NextInt64(maxValue);
    }

    /// <summary>
    /// 返回一个指定范围内的随机数。
    /// </summary>
    /// <param name="minValue">返回的随机数的下界（随机数可取该下界值）。</param>
    /// <param name="maxValue">返回的随机数的上界（随机数不能取该上界值）。maxValue 必须大于等于 minValue。</param>
    /// <returns>一个大于等于 minValue 且小于 maxValue 的 64 位带符号整数。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 minValue 大于 maxValue 时抛出此异常</exception>
    public static long NextLong(long minValue, long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        return RandomShared.NextInt64(minValue, maxValue);
    }

    /// <summary>
    /// 用随机数填充指定字节数组的元素。
    /// </summary>
    /// <param name="buffer">包含随机数的字节数组。</param>
    /// <exception cref="ArgumentNullException">当 buffer 为 null 时抛出此异常</exception>
    public static void NextBytes(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        RandomShared.NextBytes(buffer);
    }

    /// <summary>
    /// 用随机数填充指定字节数组的元素。
    /// </summary>
    /// <param name="buffer">包含随机数的字节数组。</param>
    public static void NextBytes(Span<byte> buffer)
    {
        // Span<byte> 是值类型，不需要 null 检查
        RandomShared.NextBytes(buffer);
    }

    /// <summary>
    /// 随机选择
    /// </summary>
    /// <param name="items">选择项</param>
    /// <typeparam name="T">选择项类型</typeparam>
    /// <returns>选择结果</returns>
    /// <exception cref="ArgumentNullException">当 items 为 null 时抛出此异常</exception>
    /// <exception cref="ArgumentException">当 items 为空集合时抛出此异常</exception>
    public static T RandomSelect<T>(IList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Count, nameof(items));
        return items[Next(items.Count)];
    }

    /// <summary>
    /// 随机选择
    /// </summary>
    /// <param name="items">选择项</param>
    /// <typeparam name="T">选择项类型</typeparam>
    /// <returns>选择结果</returns>
    /// <exception cref="ArgumentNullException">当 items 为 null 时抛出此异常</exception>
    /// <exception cref="ArgumentException">当 items 为空数组时抛出此异常</exception>
    public static T RandomSelect<T>(T[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Length, nameof(items));
        return items[Next(items.Length)];
    }

    /// <summary>
    /// 随机选择
    /// </summary>
    /// <param name="items">选择项</param>
    /// <typeparam name="T">选择项类型</typeparam>
    /// <returns>选择结果</returns>
    /// <exception cref="ArgumentNullException">当 items 为 null 时抛出此异常</exception>
    /// <exception cref="ArgumentException">当 items 为空列表时抛出此异常</exception>
    public static T RandomSelect<T>(List<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Count, nameof(items));
        return items[Next(items.Count)];
    }

    /// <summary>
    /// 随机选择索引
    /// </summary>
    /// <param name="items">选择项</param>
    /// <typeparam name="T">选择项类型</typeparam>
    /// <returns>选择结果索引</returns>
    /// <exception cref="ArgumentNullException">当 items 为 null 时抛出此异常</exception>
    /// <exception cref="ArgumentException">当 items 为空集合时抛出此异常</exception>
    public static int Idx<T>(IList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Count, nameof(items));
        return Next(items.Count);
    }

    /// <summary>
    /// 随机选择索引
    /// </summary>
    /// <param name="items">选择项</param>
    /// <typeparam name="T">选择项类型</typeparam>
    /// <returns>选择结果索引</returns>
    /// <exception cref="ArgumentNullException">当 items 为 null 时抛出此异常</exception>
    /// <exception cref="ArgumentException">当 items 为空数组时抛出此异常</exception>
    public static int Idx<T>(T[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Length, nameof(items));
        return Next(items.Length);
    }

    /// <summary>
    /// 随机选择索引
    /// </summary>
    /// <param name="items">选择项</param>
    /// <typeparam name="T">选择项类型</typeparam>
    /// <returns>选择结果索引</returns>
    /// <exception cref="ArgumentNullException">当 items 为 null 时抛出此异常</exception>
    /// <exception cref="ArgumentException">当 items 为空列表时抛出此异常</exception>
    public static int Idx<T>(List<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Count, nameof(items));
        return Next(items.Count);
    }

    /// <summary>
    /// 随机选择多个索引
    /// </summary>
    /// <param name="items">选择项</param>
    /// <param name="count">选择数量</param>
    /// <typeparam name="T">选择项类型</typeparam>
    /// <returns>选择结果索引数组</returns>
    /// <exception cref="ArgumentNullException">当 items 为 null 时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当 items 为空集合或 count 小于 0 时抛出此异常</exception>
    public static int[] Ids<T>(IList<T> items, int count)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Count, nameof(items));
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = Next(items.Count);
        }

        return result;
    }

    /// <summary>
    /// 随机选择多个索引
    /// </summary>
    /// <param name="items">选择项</param>
    /// <param name="count">选择数量</param>
    /// <typeparam name="T">选择项类型</typeparam>
    /// <returns>选择结果索引数组</returns>
    /// <exception cref="ArgumentNullException">当 items 为 null 时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当 items 为空数组或 count 小于 0 时抛出此异常</exception>
    public static int[] Ids<T>(T[] items, int count)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Length, nameof(items));
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = Next(items.Length);
        }

        return result;
    }

    /// <summary>
    /// 随机选择多个索引
    /// </summary>
    /// <param name="items">选择项</param>
    /// <param name="count">选择数量</param>
    /// <typeparam name="T">选择项类型</typeparam>
    /// <returns>选择结果索引数组</returns>
    /// <exception cref="ArgumentNullException">当 items 为 null 时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当 items 为空列表或 count 小于 0 时抛出此异常</exception>
    public static int[] Ids<T>(List<T> items, int count)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Count, nameof(items));
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = Next(items.Count);
        }

        return result;
    }

    /// <summary>
    /// 随机选择多个项目
    /// </summary>
    /// <param name="items">选择项</param>
    /// <param name="count">选择数量</param>
    /// <typeparam name="T">选择项类型</typeparam>
    /// <returns>选择结果数组</returns>
    /// <exception cref="ArgumentNullException">当 items 为 null 时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当 items 为空集合或 count 小于 0 时抛出此异常</exception>
    public static T[] Items<T>(IList<T> items, int count)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Count, nameof(items));
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var result = new T[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = items[Next(items.Count)];
        }

        return result;
    }

    /// <summary>
    /// 随机选择多个项目
    /// </summary>
    /// <param name="items">选择项</param>
    /// <param name="count">选择数量</param>
    /// <typeparam name="T">选择项类型</typeparam>
    /// <returns>选择结果数组</returns>
    /// <exception cref="ArgumentNullException">当 items 为 null 时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当 items 为空数组或 count 小于 0 时抛出此异常</exception>
    public static T[] Items<T>(T[] items, int count)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Length, nameof(items));
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var result = new T[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = items[Next(items.Length)];
        }

        return result;
    }

    /// <summary>
    /// 随机选择多个项目
    /// </summary>
    /// <param name="items">选择项</param>
    /// <param name="count">选择数量</param>
    /// <typeparam name="T">选择项类型</typeparam>
    /// <returns>选择结果数组</returns>
    /// <exception cref="ArgumentNullException">当 items 为 null 时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当 items 为空列表或 count 小于 0 时抛出此异常</exception>
    public static T[] Items<T>(List<T> items, int count)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Count, nameof(items));
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var result = new T[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = items[Next(items.Count)];
        }

        return result;
    }


    /// <summary>
    /// 从1~n中随机选取m个数，m小于n
    /// </summary>
    /// <param name="m">需要选取的数量</param>
    /// <param name="n">范围上限</param>
    /// <returns>包含随机选取的m个数的集合</returns>
    /// <exception cref="ArgumentOutOfRangeException">当m或n小于0，或m大于n时抛出此异常</exception>
    public static HashSet<int> RandomSelect(int m, int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(m);
        ArgumentOutOfRangeException.ThrowIfNegative(n);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(m, n);

        // 经典迭代抽样（Floyd 算法）：从 n-m 递增到 n-1，每次在 [0, i] 取候选，
        // 命中已选项则改取 i。与旧递归实现同分布，但无 m 层递归（大 m 不再栈溢出）。
        var selected = new HashSet<int>(m);
        for (var i = n - m; i < n; i++)
        {
            var candidate = ConcurrentCryptoRandom.Next(0, i + 1);
            selected.Add(selected.Contains(candidate) ? i : candidate);
        }

        return selected;
    }

    /// <summary>
    /// 根据权重随机选取，如果需求数量超过权重和（除以权重最大公约数后的），那么按照权重比例加入id，剩余数量再进行随机
    /// 不可重复随机num一定小于等于id数量
    /// </summary>
    /// <param name="weightStr">权重字符串；行内字段数以 weightIndex 为准（Ids 为 id+weight，Items 为 id+中间字段+weight）</param>
    /// <param name="num">需要选取的数量</param>
    /// <param name="weightIndex">权重索引</param>
    /// <param name="canRepeat">是否允许重复选取</param>
    /// <returns>包含随机选取的结果数组列表</returns>
    /// <exception cref="ArgumentNullException">当weightStr为null时抛出此异常</exception>
    /// <exception cref="ArgumentException">当weightStr为空或格式错误、权重非法，或不可重复抽取的正权重条目不足时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当num或weightIndex小于0时抛出此异常</exception>
    private static List<int[]> RandomSelect(string weightStr, int num, int weightIndex, bool canRepeat = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(weightStr);
        ArgumentOutOfRangeException.ThrowIfNegative(num);
        ArgumentOutOfRangeException.ThrowIfNegative(weightIndex);

        var array = weightStr.SplitTo2IntArray();
        return RandomSelect(array, num, weightIndex, canRepeat);
    }

    /// <summary>
    /// 根据权重随机选取，如果需求数量超过权重和（除以权重最大公约数后的），那么按照权重比例加入id，剩余数量再进行随机
    /// 不可重复随机num一定小于等于id数量
    /// </summary>
    /// <param name="array">权重数组，每个元素为[id, weight]</param>
    /// <param name="num">需要选取的数量</param>
    /// <param name="weightIndex">权重索引</param>
    /// <param name="canRepeat">是否允许重复选取</param>
    /// <returns>包含随机选取的结果数组列表</returns>
    /// <exception cref="ArgumentNullException">当array为null时抛出此异常</exception>
    /// <exception cref="ArgumentException">当array为空数组、权重非法，或不可重复选取且需求数量大于正权重条目数时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当num或weightIndex小于0时抛出此异常</exception>
    private static List<int[]> RandomSelect(IReadOnlyList<int[]> array, int num, int weightIndex, bool canRepeat = true)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfZero(array.Count, nameof(array));
        ArgumentOutOfRangeException.ThrowIfNegative(num);
        ArgumentOutOfRangeException.ThrowIfNegative(weightIndex);

        if (canRepeat)
        {
            // 可重复
            return CanRepeatRandom(array, num, weightIndex);
        }

        // 不可重复，需求数量不应超过id数量
        if (num > array.Count)
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Utility.RandomHelper.CantRepeatRandomArgError, num, array.Count));
        }

        return NoRepeatRandom(num, weightIndex, array);
    }

    /// <summary>
    /// 读取并校验条目中的权重值。
    /// </summary>
    /// <param name="entry">权重条目。</param>
    /// <param name="weightIndex">权重索引。</param>
    /// <returns>校验通过的权重值。</returns>
    /// <exception cref="ArgumentException">当条目为空、索引越界或权重为负数时抛出。</exception>
    private static int ReadWeight(int[] entry, int weightIndex)
    {
        if (entry == null || weightIndex >= entry.Length)
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Utility.RandomHelper.WeightIndexOutOfRange, weightIndex, entry == null ? 0 : entry.Length));
        }

        var weight = entry[weightIndex];
        if (weight < 0)
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Utility.RandomHelper.NegativeWeight, weight));
        }

        return weight;
    }

    /// <summary>
    /// 校验权重数组并返回总和（拒绝负权重、全 0、超大总和与越界索引）。
    /// </summary>
    /// <param name="array">权重数组，每个元素为[id, weight]。</param>
    /// <param name="weightIndex">权重索引。</param>
    /// <returns>校验通过的总权重。</returns>
    /// <exception cref="ArgumentException">权重非法时抛出。</exception>
    private static int ValidateAndSumWeights(IReadOnlyList<int[]> array, int weightIndex)
    {
        long totalWeight = 0;
        foreach (var entry in array)
        {
            totalWeight += ReadWeight(entry, weightIndex);
        }

        return EnsureTotalWeight(totalWeight);
    }

    /// <summary>
    /// 校验纯权重数组并返回总和。
    /// </summary>
    /// <param name="weights">权重数组。</param>
    /// <returns>校验通过的总权重。</returns>
    /// <exception cref="ArgumentException">权重非法时抛出。</exception>
    private static int ValidateAndSumWeightValues(int[] weights)
    {
        long totalWeight = 0;
        foreach (var weight in weights)
        {
            if (weight < 0)
            {
                throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Utility.RandomHelper.NegativeWeight, weight));
            }

            totalWeight += weight;
        }

        return EnsureTotalWeight(totalWeight);
    }

    /// <summary>
    /// 校验总权重：不能为 0，且必须落在 int 随机域内。
    /// </summary>
    /// <param name="totalWeight">累加后的总权重。</param>
    /// <returns>int 范围内的总权重。</returns>
    /// <exception cref="ArgumentException">总权重为 0 或超过 int 上限时抛出。</exception>
    private static int EnsureTotalWeight(long totalWeight)
    {
        if (totalWeight == 0)
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Utility.RandomHelper.ZeroTotalWeight));
        }

        if (totalWeight > int.MaxValue)
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Utility.RandomHelper.WeightSumOverflow, totalWeight));
        }

        return (int)totalWeight;
    }

    /// <summary>
    /// 统计权重为正的条目数量（不可重复抽样最多能选出这么多个目标）。
    /// </summary>
    /// <param name="array">权重数组，每个元素为[id, weight]。</param>
    /// <param name="weightIndex">权重索引。</param>
    /// <returns>正权重条目数。</returns>
    private static int CountPositiveWeights(IReadOnlyList<int[]> array, int weightIndex)
    {
        var count = 0;
        foreach (var entry in array)
        {
            if (ReadWeight(entry, weightIndex) > 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 不可重复随机选取
    /// </summary>
    /// <param name="num">需要选取的数量</param>
    /// <param name="weightIndex">权重索引</param>
    /// <param name="array">权重数组，每个元素为[id, weight]</param>
    /// <returns>包含随机选取的结果数组列表</returns>
    /// <exception cref="ArgumentException">权重非法，或正权重条目数少于 num 时抛出。</exception>
    private static List<int[]> NoRepeatRandom(int num, int weightIndex, IReadOnlyList<int[]> array)
    {
        // 先整表校验，避免非法权重在逐次抽样中被隐式放大成晦涩的库异常。
        ValidateAndSumWeights(array, weightIndex);

        // 0 权重条目永不被选中：不可重复抽样要求目标数不超过正权重条目数，
        // 否则后续轮次的子集权重和会耗尽（旧实现会返回重复项，属静默错误）。
        var positiveWeightCount = CountPositiveWeights(array, weightIndex);
        if (num > positiveWeightCount)
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Utility.RandomHelper.InsufficientPositiveWeights, num, positiveWeightCount));
        }

        var results = new List<int[]>();
        var idxSet = new HashSet<int>();
        for (var i = 0; i < num; i++)
        {
            var totalWeight = 0;
            for (var j = 0; j < array.Count; j++)
            {
                if (!idxSet.Contains(j))
                {
                    totalWeight += array[j][weightIndex];
                }
            }

            // positiveWeightCount 预检保证每一轮仍有正权重可选，子集和恒 > 0。
            var r = ConcurrentCryptoRandom.Next(totalWeight);
            var temp = 0;
            var idx = 0;
            for (var j = 0; j < array.Count; j++)
            {
                if (!idxSet.Contains(j))
                {
                    temp += array[j][weightIndex];
                    if (temp > r)
                    {
                        idx = j;
                        break;
                    }
                }
            }

            idxSet.Add(idx);
            results.Add(array[idx]);
        }

        return results;
    }

    /// <summary>
    /// 可重复随机选取
    /// </summary>
    /// <param name="array">权重数组，每个元素为[id, weight]</param>
    /// <param name="num">需要选取的数量</param>
    /// <param name="weightIndex">权重索引</param>
    /// <returns>包含随机选取的结果数组列表</returns>
    private static List<int[]> CanRepeatRandom(IReadOnlyList<int[]> array, int num, int weightIndex)
    {
        var totalWeight = ValidateAndSumWeights(array, weightIndex);

        var results = new List<int[]>(num);
        for (var i = 0; i < num; i++)
        {
            results.Add(SingleRandom(array, totalWeight, weightIndex));
        }

        return results;
    }

    /// <summary>
    /// 根据权重独立随机选取
    /// </summary>
    /// <param name="weightStr">权重字符串，格式为"id1+weight1;id2+weight2;..."</param>
    /// <param name="num">需要选取的数量</param>
    /// <param name="weightIndex">权重索引</param>
    /// <returns>包含随机选取的结果数组列表</returns>
    private static List<int[]> CanRepeatRandom(string weightStr, int num, int weightIndex)
    {
        var array = weightStr.SplitTo2IntArray();
        return CanRepeatRandom(array, num, weightIndex);
    }

    /// <summary>
    /// 单次随机选取
    /// </summary>
    /// <param name="array">权重数组，每个元素为[id, weight]</param>
    /// <param name="totalWeight">总权重</param>
    /// <param name="weightIndex">权重索引</param>
    /// <returns>随机选取的结果数组</returns>
    private static int[] SingleRandom(IReadOnlyList<int[]> array, int totalWeight, int weightIndex)
    {
        var r = ConcurrentCryptoRandom.Next(totalWeight);
        var temp = 0;
        foreach (var arr in array)
        {
            temp += ReadWeight(arr, weightIndex);
            if (temp > r)
            {
                return arr;
            }
        }

        return array[0];
    }

    /// <summary>
    /// 根据权重随机选取一个id
    /// </summary>
    /// <param name="weights">权重数组</param>
    /// <returns>随机选取的id索引</returns>
    /// <exception cref="ArgumentNullException">当weights为null时抛出此异常</exception>
    /// <exception cref="ArgumentException">当weights为空数组、含负权重或权重和非法时抛出此异常</exception>
    public static int Idx(int[] weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentOutOfRangeException.ThrowIfZero(weights.Length, nameof(weights));

        var totalWight = ValidateAndSumWeightValues(weights);
        var r = ConcurrentCryptoRandom.Next(totalWight);
        var temp = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            temp += weights[i];
            if (temp > r)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// 根据权重随机选取一个id
    /// </summary>
    /// <param name="array">数据列表，每个元素为[id, weight]</param>
    /// <param name="weightIndex">权重索引</param>
    /// <returns>随机选取的id索引</returns>
    /// <exception cref="ArgumentNullException">当array为null时抛出此异常</exception>
    /// <exception cref="ArgumentException">当array为空数组、权重非法或 weightIndex 越界时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当weightIndex小于0时抛出此异常</exception>
    public static int Idx(IReadOnlyList<int[]> array, int weightIndex = 1)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfZero(array.Count, nameof(array));
        ArgumentOutOfRangeException.ThrowIfNegative(weightIndex);

        var totalWeight = ValidateAndSumWeights(array, weightIndex);

        var r = ConcurrentCryptoRandom.Next(totalWeight);
        var temp = 0;
        for (var i = 0; i < array.Count; i++)
        {
            var arr = array[i];
            temp += ReadWeight(arr, weightIndex);
            if (temp > r)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// 随机获取指定数量的id
    /// </summary>
    /// <param name="array">数据列表，每个元素为[id, weight]</param>
    /// <param name="num">需要选取的数量</param>
    /// <param name="isCanRepeat">是否允许重复选取</param>
    /// <returns>包含随机选取的id列表</returns>
    /// <exception cref="ArgumentNullException">当array为null时抛出此异常</exception>
    /// <exception cref="ArgumentException">当array为空数组、权重非法（负权重/全 0/总和溢出/索引越界），或不可重复抽取的正权重条目不足时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当num小于0时抛出此异常</exception>
    public static List<int> Ids(IReadOnlyList<int[]> array, int num, bool isCanRepeat = true)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfZero(array.Count, nameof(array));
        ArgumentOutOfRangeException.ThrowIfNegative(num);

        return RandomSelect(array.ToArray(), num, 1, isCanRepeat).Select(t => t[0]).ToList();
    }

    /// <summary>
    /// 随机获取指定数量的id
    /// </summary>
    /// <param name="str">权重字符串，格式为"id1+weight1;id2+weight2;..."</param>
    /// <param name="num">需要选取的数量</param>
    /// <param name="isCanRepeat">是否允许重复选取</param>
    /// <returns>包含随机选取的id列表</returns>
    /// <exception cref="ArgumentNullException">当str为null时抛出此异常</exception>
    /// <exception cref="ArgumentException">当str为空或格式错误、权重非法（负权重/全 0/总和溢出），或不可重复抽取的正权重条目不足时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当num小于0时抛出此异常</exception>
    public static List<int> Ids(string str, int num, bool isCanRepeat = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(str);
        ArgumentOutOfRangeException.ThrowIfNegative(num);

        return RandomSelect(str, num, 1, isCanRepeat).Select(t => t[0]).ToList();
    }

    /// <summary>
    /// 随机获取指定数量的项目
    /// </summary>
    /// <param name="str">权重字符串，Items 格式为"id1+中间字段1+weight1;id2+中间字段2+weight2;..."</param>
    /// <param name="num">需要选取的数量</param>
    /// <param name="isCanRepeat">是否允许重复选取</param>
    /// <returns>包含随机选取的项目列表</returns>
    /// <exception cref="ArgumentNullException">当str为null时抛出此异常</exception>
    /// <exception cref="ArgumentException">当str为空或格式错误、权重非法（负权重/全 0/总和溢出），或不可重复抽取的正权重条目不足时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当num小于0时抛出此异常</exception>
    public static List<int[]> Items(string str, int num, bool isCanRepeat = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(str);
        ArgumentOutOfRangeException.ThrowIfNegative(num);

        return RandomSelect(str, num, 2, isCanRepeat);
    }

    /// <summary>
    /// 随机获取指定数量的项目
    /// </summary>
    /// <param name="array">数据列表，每个元素为[id, 中间字段, weight]（权重取索引 2）</param>
    /// <param name="num">需要选取的数量</param>
    /// <param name="isCanRepeat">是否允许重复选取</param>
    /// <returns>包含随机选取的项目列表</returns>
    /// <exception cref="ArgumentNullException">当array为null时抛出此异常</exception>
    /// <exception cref="ArgumentException">当array为空数组、权重非法（负权重/全 0/总和溢出/索引越界），或不可重复抽取的正权重条目不足时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当num小于0时抛出此异常</exception>
    public static List<int[]> Items(IReadOnlyList<int[]> array, int num, bool isCanRepeat = true)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfZero(array.Count, nameof(array));
        ArgumentOutOfRangeException.ThrowIfNegative(num);

        return RandomSelect(array.ToArray(), num, 2, isCanRepeat);
    }

    /// <summary>
    /// 求多个数的最大公约数
    /// </summary>
    /// <param name="input">输入的整数数组</param>
    /// <returns>最大公约数</returns>
    /// <exception cref="ArgumentNullException">当input为null时抛出此异常</exception>
    public static int Gcd(params int[] input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Length == 0)
        {
            return 1;
        }

        if (input.Length == 1)
        {
            return input[0];
        }

        var n = input[0];
        for (var i = 1; i < input.Length; i++)
        {
            n = Gcd(n, input[i]);
        }

        return n;
    }

    /// <summary>
    /// 求两个数的最大公约数
    /// </summary>
    /// <param name="a">第一个整数</param>
    /// <param name="b">第二个整数</param>
    /// <returns>最大公约数</returns>
    public static int Gcd(int a, int b)
    {
        if (a < b)
        {
            (b, a) = (a, b);
        }

        if (b == 0)
        {
            return a;
        }

        return Gcd(b, a % b);
    }
}