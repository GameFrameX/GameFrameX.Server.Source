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
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
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
//  ==========================================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;

namespace GameFrameX.Tests.Online;


/// <summary>槽位一模块（发现序最先）。</summary>
public sealed class MechanismFirstSlotModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 获取槽位一（类型名排序后最先被发现的槽位）的能力清单：每次读取都由 <see cref="MechanismScenario.First"/> 现场生成，能力标识取用例覆写值，未覆写时为缺省 <c>mech-first</c>，版本固定 <c>1.0.0</c>，并带上用例设置的动作、必需依赖、可选依赖与参数类型。
    /// </summary>
    /// <remarks>
    /// Builds the manifest of slot first (the slot discovered first by type-name ordering) on every read from <c>MechanismScenario.First</c>: the capability identifier is the per-test override or the default <c>mech-first</c>, version is fixed at <c>1.0.0</c>, and actions, required and optional dependencies and options type come from the slot setup.
    /// </remarks>
    /// <value>槽位一当前场景对应的能力清单 / Capability manifest reflecting the current setup of slot first</value>
    public CapabilityManifest Manifest
    {
        get { return MechanismScenario.First.ToManifest(); }
    }

    /// <summary>
    /// 把注册阶段转交给槽位一的 <c>Register</c> 委托，供用例在 <c>mech-first</c> 上注入服务或记录注册顺序；用例未设置委托时不做任何注册。
    /// </summary>
    /// <remarks>
    /// Forwards the registration phase to the <c>Register</c> delegate of slot first so tests can inject services or record ordering for <c>mech-first</c>; registers nothing when no delegate is set.
    /// </remarks>
    /// <param name="context">能力注册上下文，原样传给槽位一委托 / Registration context passed unchanged to the slot first delegate</param>
    public void Register(CapabilityRegistrationContext context)
    {
        MechanismScenario.First.Register?.Invoke(context);
    }

    /// <summary>
    /// 把激活阶段转交给槽位一的 <c>Activate</c> 委托并返回其任务，用于验证 <c>mech-first</c> 的激活顺序、激活失败与回滚等机制；未设置委托时直接返回已完成任务，即视为激活成功。
    /// </summary>
    /// <remarks>
    /// Forwards activation to the <c>Activate</c> delegate of slot first and returns its task, letting tests drive ordering, failure and rollback for <c>mech-first</c>; returns a completed task (successful activation) when no delegate is set.
    /// </remarks>
    /// <param name="context">能力激活上下文，原样传给槽位一委托 / Activation context passed unchanged to the slot first delegate</param>
    /// <param name="cancellationToken">取消令牌，原样传给委托 / Cancellation token passed to the delegate</param>
    /// <returns>委托返回的任务；无委托时为已完成任务 / Task returned by the delegate, or a completed task when none is set</returns>
    public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        return MechanismScenario.First.Activate?.Invoke(context, cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>
    /// 同步调用槽位一的 <c>Deactivate</c> 委托（用例借此记录 <c>mech-first</c> 的停用顺序），随后返回已完成任务；委托为同步形式，因此没有需要等待的异步工作。
    /// </summary>
    /// <remarks>
    /// Synchronously invokes the <c>Deactivate</c> delegate of slot first (used to record the deactivation order of <c>mech-first</c>) and returns a completed task, because the delegate is synchronous and nothing needs awaiting.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌，原样传给委托 / Cancellation token passed to the delegate</param>
    /// <returns>已完成的任务 / Completed task</returns>
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        MechanismScenario.First.Deactivate?.Invoke(cancellationToken);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 把管理动作注册转交给槽位一的 <c>RegisterAdminActions</c> 委托，用于验证 <c>mech-first</c> 贡献的管理面动作；未设置委托时不注册任何动作。
    /// </summary>
    /// <remarks>
    /// Forwards admin action registration to the <c>RegisterAdminActions</c> delegate of slot first to exercise admin actions contributed by <c>mech-first</c>; registers no action when no delegate is set.
    /// </remarks>
    /// <param name="registry">管理动作注册表，原样传给槽位一委托 / Admin action registry passed unchanged to the slot first delegate</param>
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        MechanismScenario.First.RegisterAdminActions?.Invoke(registry);
    }
}

/// <summary>槽位二模块。</summary>
