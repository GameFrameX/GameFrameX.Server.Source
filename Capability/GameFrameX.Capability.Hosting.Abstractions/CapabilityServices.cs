// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the full license text in the LICENSE file located at the root of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activity that endangers national security, disrupts social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of citizens as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织不承担任何责任，开发者应自行承担。
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

namespace GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// 能力服务注册表（能力族的服务共享协议载体）：组合根预置（<c>GameFrameX.Capability.Hosting.Core.CapabilityHostBuilder.UseService{TService}</c>）
/// 与能力模块注册（<see cref="CapabilityRegistrationContext.AddService{TService}"/>）共用同一张按服务类型索引的表；
/// 装配完成后经 <c>GameFrameX.Capability.Hosting.Core.CapabilityHost.Services</c> 对组合根开放取用。
/// <para>
/// 维护约束：服务键 = 调用点书写的 <c>TService</c> 类型（无接口自动映射）；同类型重复注册即装配失败（对齐
/// manifest Id 唯一 / job 全名唯一的既有口径）；模块间解析依赖拓扑定序——先序模块注册的产物对后序模块可见，
/// 反向不可见；机制包不持有任何能力类型（Hosting=0 红线）。
/// </para>
/// </summary>
/// <remarks>
/// The capability service registry (the capability-family service-sharing protocol): composition-root
/// pre-seeds (<c>GameFrameX.Capability.Hosting.Core.CapabilityHostBuilder.UseService{TService}</c>) and capability-module registrations
/// (<see cref="CapabilityRegistrationContext.AddService{TService}"/>) share one table indexed by service type;
/// after assembly it is exposed to the composition root through <c>GameFrameX.Capability.Hosting.Core.CapabilityHost.Services</c>.
/// The service key is the <c>TService</c> type as written at the call site (no automatic interface mapping);
/// duplicate registration of the same type fails assembly (mirroring the manifest-id and job-name uniqueness
/// rules); cross-module resolution relies on topological ordering — products registered by earlier modules are
/// visible to later modules and never the reverse; the mechanism packages hold no capability types.
/// </remarks>
public sealed class CapabilityServices
{
    /// <summary>
    /// 按服务类型索引的注册表。
    /// </summary>
    private readonly Dictionary<Type, object> _services = new();

    /// <summary>
    /// 注册一个服务实例（按 <typeparamref name="TService"/> 索引；同类型重复注册即抛 <see cref="InvalidOperationException"/>）。
    /// </summary>
    /// <typeparam name="TService">服务类型（调用点书写形态，可为接口）。</typeparam>
    /// <param name="service">服务实例（不可为 null）。</param>
    public void AddService<TService>(TService service)
    {
        if (service == null)
        {
            throw new ArgumentNullException(nameof(service));
        }

        if (_services.ContainsKey(typeof(TService)))
        {
            throw new InvalidOperationException("Duplicate capability service registration: " + typeof(TService).FullName);
        }

        _services[typeof(TService)] = service;
    }

    /// <summary>
    /// 解析一个服务（缺失即抛 <see cref="InvalidOperationException"/>；用于模块声明的硬依赖）。
    /// </summary>
    /// <typeparam name="TService">服务类型。</typeparam>
    /// <returns>服务实例。</returns>
    public TService GetService<TService>()
    {
        if (TryGetService<TService>(out var service))
        {
            return service;
        }

        throw new InvalidOperationException("Capability service not registered: " + typeof(TService).FullName);
    }

    /// <summary>
    /// 尝试解析一个服务（缺失返回 false 且输出 default；用于可选依赖与替换缝探测）。
    /// </summary>
    /// <typeparam name="TService">服务类型。</typeparam>
    /// <param name="service">输出：服务实例；缺失为 default。</param>
    /// <returns>命中返回 true。</returns>
    public bool TryGetService<TService>(out TService service)
    {
        if (_services.TryGetValue(typeof(TService), out var registered) && registered is TService typed)
        {
            service = typed;
            return true;
        }

        service = default;
        return false;
    }
}
