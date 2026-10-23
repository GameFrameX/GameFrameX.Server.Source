// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository:     https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Docs:      https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;

using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 机制层测试场景槽位（C294：模块只能经部署目录扫描发现，桩清单 / 依赖 / 行为全部由用例在
/// 组合前写入静态槽位——同一测试程序集内的全部槽位模块对每次组合同时可见，用例以
/// <see cref="CapabilityAssemblyFilter"/> 白名单圈定参与面；槽位缺省态为无依赖的惰性模块）。
/// </summary>
public sealed class MechanismScenarioSlot
{
    /// <summary>初始化槽位。</summary>
    /// <param name="defaultId">缺省能力 Id（用例可覆写）。</param>
    public MechanismScenarioSlot(string defaultId)
    {
        DefaultId = defaultId;
    }

    /// <summary>缺省能力 Id。</summary>
    public string DefaultId { get; }

    /// <summary>用例覆写的能力 Id（null = 缺省）。</summary>
    public string Id;

    /// <summary>必选依赖（缺省空）。</summary>
    public string[] Requires = Array.Empty<string>();

    /// <summary>可选依赖声明（缺省空）。</summary>
    public CapabilityOptionalDependency[] Optional = Array.Empty<CapabilityOptionalDependency>();

    /// <summary>Options 根类型（缺省 null = 不声明）。</summary>
    public Type OptionsType;

    /// <summary>manifest 声明的 admin action（缺省空）。</summary>
    public string[] Actions = Array.Empty<string>();

    /// <summary>Register 期行为（缺省空操作）。</summary>
    public Action<CapabilityRegistrationContext> Register;

    /// <summary>Activate 期行为（缺省空操作）。</summary>
    public Func<CapabilityActivationContext, CancellationToken, Task> Activate;

    /// <summary>Deactivate 期行为（缺省空操作）。</summary>
    public Action<CancellationToken> Deactivate;

    /// <summary>admin action 注册期行为（缺省空操作）。</summary>
    public Action<AdminActionRegistry> RegisterAdminActions;

    /// <summary>
    /// 构造当前槽位清单。
    /// </summary>
    /// <returns>能力清单。</returns>
    public CapabilityManifest ToManifest()
    {
        return new CapabilityManifest(Id ?? DefaultId, "1.0.0", Actions, Requires, Optional, OptionsType);
    }
}

/// <summary>
/// 机制层测试场景静态状态（四个槽位；全部机制用例须落 <see cref="CapabilityRuntimeCollection"/>
/// 串行集合，避免静态槽位被并行用例交叉覆写）。
/// </summary>
public static class MechanismScenario
{
    /// <summary>槽位一（类型名定序最先被发现）。</summary>
    public static readonly MechanismScenarioSlot First = new("mech-first");

    /// <summary>槽位二。</summary>
    public static readonly MechanismScenarioSlot Second = new("mech-second");

    /// <summary>槽位三。</summary>
    public static readonly MechanismScenarioSlot Third = new("mech-third");

    /// <summary>槽位四。</summary>
    public static readonly MechanismScenarioSlot Fourth = new("mech-fourth");

    /// <summary>
    /// 复位全部槽位（用例 finally 调用，保证槽位缺省态不泄漏到后续用例）。
    /// </summary>
    public static void Reset()
    {
        Reset(First);
        Reset(Second);
        Reset(Third);
        Reset(Fourth);
        MechanismTestFamily.LastEventPublisher = null;
    }

    /// <summary>
    /// 复位单个槽位。
    /// </summary>
    /// <param name="slot">槽位。</param>
    private static void Reset(MechanismScenarioSlot slot)
    {
        slot.Id = null;
        slot.Requires = Array.Empty<string>();
        slot.Optional = Array.Empty<CapabilityOptionalDependency>();
        slot.OptionsType = null;
        slot.Actions = Array.Empty<string>();
        slot.Register = null;
        slot.Activate = null;
        slot.Deactivate = null;
        slot.RegisterAdminActions = null;
    }
}

/// <summary>
/// 机制层测试族（发现面 = 测试程序集的部署命名约定副本 <c>GameFrameX.Tests.CapabilityStubs.dll</c>；
/// 族基建只预置管理面测试适配器——<see cref="AdminApiComposition.CreateAdminApiPlane"/> 的解析面）。
/// </summary>
public sealed class MechanismTestFamily : ICapabilityFamily
{
    /// <inheritdoc />
    public string FamilyId
    {
        get { return "mechanism-tests"; }
    }

    /// <inheritdoc />
    public string ConfigurationSectionName
    {
        get { return "MechanismTests"; }
    }

    /// <inheritdoc />
    public string DeploymentAssemblyPrefix
    {
        get { return "GameFrameX.Tests"; }
    }

    /// <summary>最近一次组合的事件发布口捕获（事件总线行为用例经它发布）。</summary>
    public static ICapabilityEventPublisher LastEventPublisher;

    /// <inheritdoc />
    public void RegisterInfrastructure(CapabilityFamilyContext context)
    {
        // 管理面测试适配器：五个机制用错误码位 + 恒放行作用域（机制口径，不绑 Online 语义）。
        var code = new AdminApiErrorCode(0, "mechanism.test");
        context.AddService(new AdminApiErrorCodes(code, code, code, code, code));
        context.AddService<IAdminApiScopeAdapter>(new MechanismTestScopeAdapter());
        LastEventPublisher = context.EventPublisher;
    }
}

/// <summary>
/// 机制测试作用域适配器（从请求体读三元组 + 恒放行——作用域守卫语义另有 Online 侧用例锚定）。
/// </summary>
public sealed class MechanismTestScopeAdapter : IAdminApiScopeAdapter
{
    /// <inheritdoc />
    public bool TryReadClaimedScope(AdminApiRequest request, out AdminApiClaimedScope claimedScope)
    {
        if (request.TryReadClaimedScope(out var tenantId, out var appId, out var serverId))
        {
            claimedScope = new AdminApiClaimedScope(tenantId, appId, serverId);
            return true;
        }

        claimedScope = null;
        return false;
    }

    /// <inheritdoc />
    public AdminApiErrorCode ValidateClaimed(AdminApiClaimedScope claimed, AdminApiClaimedScope authorized)
    {
        return null;
    }
}

/// <summary>槽位一模块（发现序最先）。</summary>
public sealed class MechanismFirstSlotModule : ICapabilityModule, IAdminActionContributor
{
    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return MechanismScenario.First.ToManifest(); }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
        MechanismScenario.First.Register?.Invoke(context);
    }

    /// <inheritdoc />
    public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        return MechanismScenario.First.Activate?.Invoke(context, cancellationToken) ?? Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        MechanismScenario.First.Deactivate?.Invoke(cancellationToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        MechanismScenario.First.RegisterAdminActions?.Invoke(registry);
    }
}

/// <summary>槽位二模块。</summary>
public sealed class MechanismSecondSlotModule : ICapabilityModule, IAdminActionContributor
{
    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return MechanismScenario.Second.ToManifest(); }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
        MechanismScenario.Second.Register?.Invoke(context);
    }

    /// <inheritdoc />
    public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        return MechanismScenario.Second.Activate?.Invoke(context, cancellationToken) ?? Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        MechanismScenario.Second.Deactivate?.Invoke(cancellationToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        MechanismScenario.Second.RegisterAdminActions?.Invoke(registry);
    }
}

/// <summary>槽位三模块。</summary>
public sealed class MechanismThirdSlotModule : ICapabilityModule, IAdminActionContributor
{
    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return MechanismScenario.Third.ToManifest(); }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
        MechanismScenario.Third.Register?.Invoke(context);
    }

    /// <inheritdoc />
    public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        return MechanismScenario.Third.Activate?.Invoke(context, cancellationToken) ?? Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        MechanismScenario.Third.Deactivate?.Invoke(cancellationToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        MechanismScenario.Third.RegisterAdminActions?.Invoke(registry);
    }
}

/// <summary>槽位四模块。</summary>
public sealed class MechanismFourthSlotModule : ICapabilityModule, IAdminActionContributor
{
    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return MechanismScenario.Fourth.ToManifest(); }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
        MechanismScenario.Fourth.Register?.Invoke(context);
    }

    /// <inheritdoc />
    public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        return MechanismScenario.Fourth.Activate?.Invoke(context, cancellationToken) ?? Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        MechanismScenario.Fourth.Deactivate?.Invoke(cancellationToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        MechanismScenario.Fourth.RegisterAdminActions?.Invoke(registry);
    }
}
