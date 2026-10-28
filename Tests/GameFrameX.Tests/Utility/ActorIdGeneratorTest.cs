//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
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


using System.Collections.Concurrent;
using GameFrameX.Utility;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests;

/// <summary>
/// ActorIdGenerator 类的单元测试：并发唯一性、位布局回读与时间戳不超前。
/// </summary>
public class ActorIdGeneratorTest
{
    /// <summary>
    /// 多 Actor ID 时间戳位共 30 位。
    /// </summary>
    private const long TimestampBitMask = (1L << 30) - 1;

    private static long GetEmbeddedSecond(long actorId)
    {
        return (actorId >> GlobalConst.TimestampMask) & TimestampBitMask;
    }

    private static long GetCurrentSecond()
    {
        return (long)(DateTime.UtcNow - IdGenerator.UtcTimeStart).TotalSeconds;
    }

    /// <summary>
    /// 并发场景下 GetActorId 与 GetUniqueId 生成的 ID 不得重复。
    /// </summary>
    [Fact]
    public void GetActorId_ShouldGenerateUniqueIdsUnderConcurrency()
    {
        const int parallelism = 4;
        const int idsPerTask = 2000;
        var actorIds = new ConcurrentBag<long>();
        var uniqueIds = new ConcurrentBag<long>();

        Parallel.For(0, parallelism, _ =>
        {
            for (var i = 0; i < idsPerTask; i++)
            {
                actorIds.Add(ActorIdGenerator.GetActorId(GlobalConst.ActorTypePlayer, 1000));
                uniqueIds.Add(ActorIdGenerator.GetUniqueId());
            }
        });

        Assert.Equal(parallelism * idsPerTask, actorIds.Distinct().Count());
        Assert.Equal(parallelism * idsPerTask, uniqueIds.Distinct().Count());
    }

    /// <summary>
    /// 生成的 ActorId 必须能通过 GetServerId / GetActorType 还原生成时传入的 serverId 与 type。
    /// </summary>
    [Theory]
    [InlineData(1, 1000)]
    [InlineData(127, 1000)]
    [InlineData(1, 9999)]
    [InlineData(100, 5000)]
    public void GetActorId_ShouldRoundTripServerIdAndActorType(ushort actorType, int serverId)
    {
        var actorId = ActorIdGenerator.GetActorId(actorType, serverId);

        Assert.Equal(serverId, ActorIdGenerator.GetServerId(actorId));
        Assert.Equal(actorType, ActorIdGenerator.GetActorType(actorId));
    }

    /// <summary>
    /// GetUniqueIdByModule 生成的高 14 位必须是传入的模块值。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(999)]
    public void GetUniqueIdByModule_ShouldEmbedModuleInHighBits(ushort module)
    {
        var uniqueId = ActorIdGenerator.GetUniqueIdByModule(module);

        Assert.Equal(module, uniqueId >> GlobalConst.ServerIdOrModuleIdMask);
    }

    /// <summary>
    /// 同一模块的 UniqueId 必须严格递增（时间戳与自增均单调）。
    /// </summary>
    [Fact]
    public void GetUniqueIdByModule_ShouldBeMonotonicallyIncreasingWithinSameModule()
    {
        var previous = ActorIdGenerator.GetUniqueIdByModule(1);

        for (var i = 0; i < 1000; i++)
        {
            var current = ActorIdGenerator.GetUniqueIdByModule(1);
            Assert.True(current > previous, $"UniqueId 必须严格递增：{current} 应大于 {previous}");
            previous = current;
        }
    }

    /// <summary>
    /// 单秒容量耗尽后等待下一秒，生成的 ID 时间戳不得超前墙上时钟（不得借用未来的秒）。
    /// </summary>
    [Fact]
    public void GetActorId_TimestampShouldNeverRunAheadOfWallClock()
    {
        // 请求量超过三个整秒的容量，必然跨越至少两次容量耗尽
        var totalIds = 3 * (GlobalConst.MaxActorIncrease + 1);
        long maximumObservedSecond = long.MinValue;

        for (var i = 0; i < totalIds; i++)
        {
            var actorId = ActorIdGenerator.GetActorId(GlobalConst.ActorTypePlayer, 1000);
            var embeddedSecond = GetEmbeddedSecond(actorId);
            if (embeddedSecond > maximumObservedSecond)
            {
                maximumObservedSecond = embeddedSecond;
            }

            Assert.True(embeddedSecond <= GetCurrentSecond(),
                $"ID 时间戳 {embeddedSecond} 超前于墙上时钟 {GetCurrentSecond()}：不允许借用未来的秒");
        }

        // 三秒容量必然至少推进了两个秒边界
        Assert.True(maximumObservedSecond >= GetCurrentSecond() - 1);
    }
}
