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
    /// RemoveAsync 返回的任务完成时一次性回收回调已执行、Actor 已移出 ActorMap（C436：改前为 Tell 发后不管，
    /// await 返回时回调可能尚未执行）。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_AwaitCompletes_AfterRecycleCallback()
    {
        var actorId = ActorIdGenerator.GetActorId(ProbeActorType, ProbeServerIdStart);
        var actor = await ActorManager.GetOrNewAsync(actorId);
        Assert.NotNull(actor);
        var callbackFired = false;
        actor.AddOnceRecycleCallback(() => callbackFired = true);

        await ActorManager.RemoveAsync(actorId);

        Assert.True(callbackFired);
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
        var callbackFired = false;
        actor.AddOnceRecycleCallback(() => callbackFired = true);

        await ActorManager.RemoveAllAsync();

        Assert.True(callbackFired);
        await ActorManager.RemoveAsync(actorId);
    }

    /// <summary>
    /// 组件反激活失败：RemoveAllAsync 不再被单个组件异常打断（C436），回收回调仍执行。
    /// </summary>
    [Fact]
    public async Task RemoveAllAsync_WhenDeactivationFails_DoesNotThrowAndStillRunsCleanup()
    {
        var actorId = ActorIdGenerator.GetActorId(ProbeActorType, ProbeServerIdStart + 2);
        var actor = await ActorManager.GetOrNewAsync(actorId);
        var components = GetComponentsMap(actor);
        components[typeof(FailingDeactivationComponent)] = new FailingDeactivationComponent();
        var cleaned = false;
        actor.AddOnceRecycleCallback(() => cleaned = true);
        try
        {
            var exception = await Record.ExceptionAsync(() => ActorManager.RemoveAllAsync());
            Assert.Null(exception);
            Assert.True(cleaned);
        }
        finally
        {
            components.Clear();
            await ActorManager.RemoveAsync(actorId);
        }
    }

    /// <summary>
    /// 有未落库修改的 Actor 经 RemoveAsync 移除：先回存再反激活（C436 CR-M12：改前直接反激活，修改丢失）。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_ModifiedState_SavedBeforeDeactivation()
    {
        var actorId = ActorIdGenerator.GetActorId(ProbeActorType, ProbeServerIdStart + 3);
        var actor = await ActorManager.GetOrNewAsync(actorId);
        var component = new RecordingStateComponent(isModified: true, throwOnSave: false);
        GetComponentsMap(actor)[typeof(RecordingStateComponent)] = component;

        await ActorManager.RemoveAsync(actorId);

        Assert.Equal(new[] { "save", "inactive" }, component.Calls);
        Assert.False(ActorManager.HasActor(actorId));
    }

    /// <summary>
    /// 无未落库修改时不额外回存，只反激活。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_UnmodifiedState_SkipsSave()
    {
        var actorId = ActorIdGenerator.GetActorId(ProbeActorType, ProbeServerIdStart + 4);
        var actor = await ActorManager.GetOrNewAsync(actorId);
        var component = new RecordingStateComponent(isModified: false, throwOnSave: false);
        GetComponentsMap(actor)[typeof(RecordingStateComponent)] = component;

        await ActorManager.RemoveAsync(actorId);

        Assert.Equal(new[] { "inactive" }, component.Calls);
    }

    /// <summary>
    /// 回存抛异常：记 Fatal 后仍完成反激活、回收回调与出表，RemoveAsync 不抛出。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_SaveThrows_StillRecyclesAndRemoves()
    {
        var actorId = ActorIdGenerator.GetActorId(ProbeActorType, ProbeServerIdStart + 5);
        var actor = await ActorManager.GetOrNewAsync(actorId);
        var component = new RecordingStateComponent(isModified: true, throwOnSave: true);
        GetComponentsMap(actor)[typeof(RecordingStateComponent)] = component;
        var callbackFired = false;
        actor.AddOnceRecycleCallback(() => callbackFired = true);

        var exception = await Record.ExceptionAsync(() => ActorManager.RemoveAsync(actorId));

        Assert.Null(exception);
        Assert.Equal(new[] { "save", "inactive" }, component.Calls);
        Assert.True(callbackFired);
        Assert.False(ActorManager.HasActor(actorId));
    }

    /// <summary>
    /// 被隔离的 Actor：RemoveAsync 返回异常任务，调用方可感知回收未执行（改前 Tell 静默丢弃）。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_QuarantinedActor_ReturnsFaultedTask()
    {
        var actorId = ActorIdGenerator.GetActorId(ProbeActorType, ProbeServerIdStart + 6);
        var actor = await ActorManager.GetOrNewAsync(actorId);
        var quarantinedField = actor.WorkerActor.GetType().GetField("_quarantined", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(quarantinedField);
        quarantinedField.SetValue(actor.WorkerActor, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ActorManager.RemoveAsync(actorId));
        Assert.False(ActorManager.HasActor(actorId));
    }

    private static System.Collections.Concurrent.ConcurrentDictionary<Type, GameFrameX.Core.Components.BaseComponent> GetComponentsMap(Actor actor)
    {
        var field = typeof(Actor).GetField("_componentsMap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (System.Collections.Concurrent.ConcurrentDictionary<Type, GameFrameX.Core.Components.BaseComponent>)field.GetValue(actor);
    }

    private sealed class FailingDeactivationComponent : GameFrameX.Core.Components.BaseComponent
    {
        public override Task InactiveAsync()
        {
            return Task.FromException(new InvalidOperationException("deactivation failed"));
        }
    }

    /// <summary>
    /// 记录回存与反激活调用顺序的探针组件：<c>isModified</c> 决定 ReadyToInactive，<c>throwOnSave</c> 令回存抛异常。
    /// </summary>
    private sealed class RecordingStateComponent : GameFrameX.Core.Components.BaseComponent
    {
        private readonly bool _throwOnSave;
        private bool _isModified;

        public RecordingStateComponent(bool isModified, bool throwOnSave)
        {
            _isModified = isModified;
            _throwOnSave = throwOnSave;
        }

        public List<string> Calls { get; } = new List<string>();

        internal override bool ReadyToInactive
        {
            get { return !_isModified; }
        }

        public override Task WriteStateAsync()
        {
            Calls.Add("save");
            if (_throwOnSave)
            {
                return Task.FromException(new InvalidOperationException("save failed"));
            }

            _isModified = false;
            return Task.CompletedTask;
        }

        public override Task InactiveAsync()
        {
            Calls.Add("inactive");
            return Task.CompletedTask;
        }
    }
}
