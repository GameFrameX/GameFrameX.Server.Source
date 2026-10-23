// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================
using System;
using System.Text.Json;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Audit;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Foundation.Idempotency;

namespace GameFrameX.Capability.Online.Abstractions;

/// <summary>
/// Online 能力族组合根骨架（C294，实现 <see cref="ICapabilityFamily"/>；机制层零能力硬编码，
/// 族基建收敛在族脊柱包内）。族标识 <c>online</c>，配置段 <c>Online</c>，部署程序集前缀
/// <c>GameFrameX.Capability.Online</c>。
/// <para>
/// 维护约束（族基建预置，语句级迁移自旧组合根 <c>BuildScannedRuntime</c>）：授权作用域（三元组取自
/// 族配置段 <c>TenantId</c>/<c>AppId</c>/<c>ServerId</c>，缺失或非法 = 装配期 fail-fast——作用域是
/// 数据隔离根边界，无默认值可回落）、Online 事件出口（<see cref="IOnlineEventPublisher"/> 委托机制
/// 事件总线）、幂等（<see cref="OnlineIdempotencyService"/>，InMemory 协调器 + 失败重放策略 ReExecute）、
/// 统一审计（<see cref="OnlineAuditService"/> 具型 + <see cref="IOnlineAuditReader"/> 读面，同源单例）。
/// 上述服务来源 = 族 Id，对全部能力模块可解析。
/// </para>
/// <para>
/// 不在本族预置的宿主扩展点：Hotfix 版本切换执行器（<c>IOnlineHotfixRollbackExecutor</c>，宿主经
/// <c>Override</c> 满足 Operations 模块的 <c>RequireHostService</c>）；GameDb 一级 store 覆盖
///（<c>Online.UseGameDbStores</c>，宿主经 <c>Override&lt;TStore&gt;</c> 预置五个 GameDb 适配器——
/// 族脊柱包不引用 Persistence 包，覆盖走宿主侧路径）。
/// </para>
/// </summary>
public sealed class OnlineCapabilityFamily : ICapabilityFamily
{
    /// <summary>
    /// 获取族标识（族基建与宿主覆盖的来源标记）。
    /// </summary>
    public string FamilyId
    {
        get { return "online"; }
    }

    /// <summary>
    /// 获取配置段名（诊断与错误路径前缀；机制只解释 <c>Settings</c> 子对象，其余族级键由本族解读）。
    /// </summary>
    public string ConfigurationSectionName
    {
        get { return "Online"; }
    }

    /// <summary>
    /// 获取部署程序集前缀（部署目录命名约定扫描圈定 <c>GameFrameX.Capability.Online.*.dll</c>）。
    /// </summary>
    public string DeploymentAssemblyPrefix
    {
        get { return "GameFrameX.Capability.Online"; }
    }

    /// <summary>
    /// 注册族基建（机制在模块扫描与注册之前调用一次；来源 = 族 Id，对全部模块可解析）。
    /// </summary>
    /// <param name="context">族基建注册面（配置段 + 事件发布口 + 服务注册）。</param>
    public void RegisterInfrastructure(CapabilityFamilyContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        // 授权作用域：三元组取自族配置段；缺失 / 非正数 = 装配期 fail-fast（无默认值可回落）。
        var tenantId = ReadScopeKey(context.ConfigurationSection, "TenantId");
        var appId = ReadScopeKey(context.ConfigurationSection, "AppId");
        var serverId = ReadScopeKey(context.ConfigurationSection, "ServerId");
        var scope = new OnlineScope(tenantId, appId, serverId);
        context.AddService(scope);

        // 事件出口：业务发布面委托机制事件总线（主题 = 事件类型字符串，Activate 开始前拒绝发布）。
        context.AddService<IOnlineEventPublisher>(new OnlineEventPublisher(context.EventPublisher));

        // 幂等：InMemory 协调器 + 失败重放策略 ReExecute（语句级迁移自旧组合根预置）。
        var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), SystemClock.Instance,
                                                     new IdempotencyOptions { FailedReplayPolicy = FailedReplayPolicy.ReExecute, });
        var idempotency = new OnlineIdempotencyService(coordinator);
        context.AddService(idempotency);

        // 管理面适配器：错误码表 / 作用域守卫 / 幂等包裹由族提供（机制管理面组装按类型键解析，缺失即 AdapterMissing fail-fast）；
        // 作用域守卫与幂等包裹必须绑定本族同一实例，保证 admin 判定与业务面同源。
        context.AddService(OnlineAdminApiAdapters.ErrorCodes);
        context.AddService(OnlineAdminApiAdapters.CreateScopeAdapter(scope));
        context.AddService(OnlineAdminApiAdapters.CreateIdempotencyAdapter(idempotency));

        // 统一审计：具型 + 读面同源单例（Assets 审计留痕与 Operations 审计桥共享同一实例）。
        var audit = new OnlineAuditService(new InMemoryOnlineAuditStore());
        context.AddService(audit);
        context.AddService<IOnlineAuditReader>(audit);
    }

    /// <summary>
    /// 读取族配置段的作用域键（正整数；缺失、类型不符或非正数 = 装配期 fail-fast）。
    /// </summary>
    /// <param name="section">族配置段（原始 JSON）。</param>
    /// <param name="key">键名。</param>
    /// <returns>键值。</returns>
    private static long ReadScopeKey(JsonElement section, string key)
    {
        if (section.ValueKind != JsonValueKind.Object
            || !section.TryGetProperty(key, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var parsed)
            || parsed <= 0)
        {
            throw new InvalidOperationException("Online family configuration section requires positive integer key '" + key + "' (path: Online." + key + ").");
        }

        return parsed;
    }
}
