// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Core.Actors;
using GameFrameX.Utility;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// ActorManager 移除路径一次性回收回调回归单测（GFX-1023）：
/// <see cref="ActorManager.RemoveAsync"/> / <see cref="ActorManager.RemoveAllAsync"/> 必须在
/// Inactive 之后执行 OnRecycleAsync，使注册在 <c>_onceRecycleCallbacks</c> 中的清理回调
/// （如 Server 侧 QuartzTimer 调度任务移除）必然触发，而非只反激活导致回调泄漏。
/// </summary>
/// <remarks>
/// 沿 <c>ComponentAgentEventDispatchTests</c> 先例挂静态状态串行 Collection（进程级 <c>ActorMap</c> 共享）。
/// </remarks>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class ActorManagerRemoveRecycleCallbackTests
{
    /// <summary>
    /// 探针Actor类型：每服单例段（type ≥ 128）且未注册任何组件，构造为裸Actor（ActiveComponentsAsync 空转），
    /// 回调触发情况只反映移除路径本身的行为。
    /// </summary>
    private const ushort ProbeActorType = 200;

    /// <summary>
    /// 探针专用服务器ID段（远离既有测试常用的 ServerId=1），避免与其他测试创建的Actor冲突。
    /// </summary>
    private const int ProbeServerIdStart = 9700;

    /// <summary>
    /// actor 队列路径依赖进程级设置；沿 <c>ComponentAgentEventDispatchTests</c> 先例做缺省初始化。
    /// </summary>
    public ActorManagerRemoveRecycleCallbackTests()
    {
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }
    }

    /// <summary>
    /// RemoveAsync 必须执行一次性回收回调并把Actor移出ActorMap（回归：旧实现只 InactiveAsync，回调永不执行）。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_RunsOnceRecycleCallbacks_AndRemovesActor()
    {
        var actorId = ActorIdGenerator.GetActorId(ProbeActorType, ProbeServerIdStart);
        var actor = await ActorManager.GetOrNewAsync(actorId);
        Assert.NotNull(actor);
        var callbackFired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        actor.AddOnceRecycleCallback(() => callbackFired.TrySetResult());

        await ActorManager.RemoveAsync(actorId);

        await callbackFired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(ActorManager.HasActor(actorId));
    }

    /// <summary>
    /// RemoveAllAsync 必须逐Actor执行一次性回收回调（回归：旧实现只 InactiveAsync，回调永不执行）。
    /// </summary>
    [Fact]
    public async Task RemoveAllAsync_RunsOnceRecycleCallbacks()
    {
        var actorId = ActorIdGenerator.GetActorId(ProbeActorType, ProbeServerIdStart + 1);
        var actor = await ActorManager.GetOrNewAsync(actorId);
        Assert.NotNull(actor);
        var callbackFired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        actor.AddOnceRecycleCallback(() => callbackFired.TrySetResult());

        await ActorManager.RemoveAllAsync();

        await callbackFired.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
