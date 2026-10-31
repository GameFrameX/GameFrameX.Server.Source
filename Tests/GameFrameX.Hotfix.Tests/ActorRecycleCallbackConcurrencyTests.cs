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
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Apps.Player.Player.Component;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Components;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Localization.Providers;
using GameFrameX.Utility;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Actor 一次性回收回调线程安全回归单测（GFX-1178 / CR-L09）：
/// <see cref="Actor.AddOnceRecycleCallback"/> 是可在任意线程调用的公共 API，
/// 与 <see cref="Actor.OnRecycleAsync"/> 排空并发时必须无异常且每个回调恰执行一次——
/// 旧实现的非线程安全 <c>LinkedList</c> 在并发注册/回收下是未定义行为。
/// </summary>
/// <remarks>
/// 沿 <c>ActorManagerRemoveRecycleCallbackTests</c> 先例挂静态状态串行 Collection（进程级共享状态）。
/// </remarks>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class ActorRecycleCallbackConcurrencyTests
{
    /// <summary>
    /// 探针Actor类型：每服单例段（type ≥ 128）且未注册任何组件，回调行为只反映回调队列本身。
    /// </summary>
    private const ushort ProbeActorType = 201;

    private static bool _environmentInitialized;

    public ActorRecycleCallbackConcurrencyTests()
    {
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }
    }

    private static async Task InitializeEnvironmentOnceAsync()
    {
        if (_environmentInitialized)
        {
            return;
        }

        await ComponentRegister.InitAsync(typeof(PlayerComponent).Assembly);
        LocalizationService.RegisterProvider(new AssemblyResourceProvider(typeof(GameFrameX.Localization.Keys.CoreExceptions.HotfixModule).Assembly));
        _environmentInitialized = true;
    }

    [Fact]
    public async Task RecycleCallback_RegisteredThenRecycled_ExecutesEachExactlyOnce()
    {
        await InitializeEnvironmentOnceAsync();
        var actor = new Actor(ActorIdGenerator.GetActorId(ProbeActorType, 9741), ProbeActorType);

        var executedCount = 0;
        actor.AddOnceRecycleCallback(() => Interlocked.Increment(ref executedCount));
        actor.AddOnceRecycleCallback(() => Interlocked.Increment(ref executedCount));
        actor.AddOnceRecycleCallback(() => throw new InvalidOperationException("callback-boom"));

        await actor.OnRecycleAsync();
        await actor.OnRecycleAsync();

        // 含一个抛异常回调：异常被隔离，其余回调恰执行一次，二次回收不重复执行
        Assert.Equal(2, executedCount);
    }

    [Fact]
    public async Task RecycleCallback_ConcurrentRegisterAndRecycle_NoExceptionAllExecutedOnce()
    {
        await InitializeEnvironmentOnceAsync();
        var actor = new Actor(ActorIdGenerator.GetActorId(ProbeActorType, 9742), ProbeActorType);

        const int producerCount = 8;
        const int perProducerCount = 1250;
        var totalCallbacks = producerCount * perProducerCount;
        var executedCount = 0;

        var producers = Enumerable.Range(0, producerCount).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < perProducerCount; i++)
            {
                actor.AddOnceRecycleCallback(() => Interlocked.Increment(ref executedCount));
            }
        })).ToArray();

        await Task.WhenAll(producers);

        // 生产者全部入队后排空：每个回调恰执行一次
        await actor.OnRecycleAsync();
        Assert.Equal(totalCallbacks, executedCount);

        await actor.OnRecycleAsync();
        Assert.Equal(totalCallbacks, executedCount);
    }

    [Fact]
    public async Task RecycleCallback_DrainRacingWithProducers_AllExecutedExactlyOnce()
    {
        await InitializeEnvironmentOnceAsync();
        var actor = new Actor(ActorIdGenerator.GetActorId(ProbeActorType, 9743), ProbeActorType);

        const int producerCount = 4;
        const int perProducerCount = 2500;
        var totalCallbacks = producerCount * perProducerCount;
        var executedCount = 0;

        var producers = Enumerable.Range(0, producerCount).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < perProducerCount; i++)
            {
                actor.AddOnceRecycleCallback(() => Interlocked.Increment(ref executedCount));
                if (i % 500 == 0)
                {
                    await Task.Yield();
                }
            }
        })).ToArray();

        // 排空与注册并发进行：ConcurrentQueue 下取出即执行，回调不丢不重
        while (executedCount < totalCallbacks || producers.Any(task => !task.IsCompleted))
        {
            await actor.OnRecycleAsync();
            await Task.Yield();
        }

        await Task.WhenAll(producers);
        await actor.OnRecycleAsync();

        Assert.Equal(totalCallbacks, executedCount);
    }
}
