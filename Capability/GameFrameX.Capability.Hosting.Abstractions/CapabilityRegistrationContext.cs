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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// 能力注册上下文（Register 期对单个能力模块开放的唯一注册面）。
/// <para>
/// 维护约束（装配红线）：
/// job 全名（<c>能力Id/jobId</c>）全宿主唯一，重复即装配失败；
/// 服务注册为描述符形态（类型键 + 命名键 + 懒工厂 + 来源 = 本能力）；
/// 单实例解析按本能力做来源检查——只允许解析自己 / 族基建 / 宿主覆盖 / 直接声明的必选与在场可选依赖，
/// 越界即装配期 fail-fast（不靠装配顺序碰巧可用）；
/// <see cref="GetServices{TService}"/> 只能在 Activate 期用（Register 期枚举到的是部分实现，结果随顺序变化），本上下文内调用直接抛出；
/// 事件订阅只能在 Register 期声明（<see cref="Subscribe{TEvent}"/>），Activate 开始后总线拒绝发布之前的积压；
/// <see cref="GetOptions{TOptions}"/> 返回机制合并校验后的最终实例（包内默认 → 配置段 → 代码 Configure → Validate）。
/// </para>
/// </summary>
public sealed class CapabilityRegistrationContext
{
    /// <summary>
    /// 逐滴答 job 注册表（注册序即执行序；装配器收集后移交调度壳）。
    /// </summary>
    public IReadOnlyList<CapabilityTickJob> TickJobs
    {
        get { return _tickJobs; }
    }

    /// <summary>
    /// 间隔 job 注册表。
    /// </summary>
    public IReadOnlyList<CapabilityIntervalJob> IntervalJobs
    {
        get { return _intervalJobs; }
    }

    /// <summary>
    /// 逐滴答 job 收集表。
    /// </summary>
    private readonly List<CapabilityTickJob> _tickJobs = new();

    /// <summary>
    /// 间隔 job 收集表。
    /// </summary>
    private readonly List<CapabilityIntervalJob> _intervalJobs = new();

    /// <summary>
    /// 已注册 job 全名集合（防重复）。
    /// </summary>
    private readonly HashSet<string> _registeredJobNames = new(StringComparer.Ordinal);

    /// <summary>
    /// 所属能力的清单（job 全名限定与解析来源检查主体）。
    /// </summary>
    private readonly CapabilityManifest _manifest;

    /// <summary>
    /// 宿主共享服务注册表（装配器逐模块注入同一实例）。
    /// </summary>
    private readonly CapabilityServices _services;

    /// <summary>
    /// 机制事件总线（Register 期订阅声明）。
    /// </summary>
    private readonly ICapabilityEventBus _eventBus;

    /// <summary>
    /// 机制合并校验后的 Options 实例（manifest 未声明 Options 根类型时为 null）。
    /// </summary>
    private readonly object _boundOptions;

    /// <summary>
    /// 钩子声明释放凭据收集器（宿主持有；停止 / 激活失败 / 装配失败时统一释放进程级静态槽位的默认声明）。
    /// </summary>
    private readonly List<IDisposable> _hookDeclarationReleases;

    /// <summary>
    /// 初始化 <see cref="CapabilityRegistrationContext"/>（仅机制装配器构造）。
    /// </summary>
    /// <param name="manifest">所属能力清单。</param>
    /// <param name="services">宿主共享服务注册表。</param>
    /// <param name="eventBus">机制事件总线。</param>
    /// <param name="boundOptions">合并校验后的 Options 实例（无参数能力为 null）。</param>
    /// <param name="hookDeclarationReleases">钩子声明释放凭据收集器（宿主持有，不可为 null）。</param>
    internal CapabilityRegistrationContext(CapabilityManifest manifest, CapabilityServices services, ICapabilityEventBus eventBus, object boundOptions, List<IDisposable> hookDeclarationReleases)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _boundOptions = boundOptions;
        _hookDeclarationReleases = hookDeclarationReleases ?? throw new ArgumentNullException(nameof(hookDeclarationReleases));
    }

    /// <summary>
    /// 注册本能力的一个默认服务（实例形态；按 <typeparamref name="TService"/> + 空命名键索引，来源 = 本能力）。
    /// </summary>
    /// <typeparam name="TService">服务类型键（调用点书写形态，可为接口）。</typeparam>
    /// <param name="instance">服务实例（不可为 null）。</param>
    public void AddService<TService>(TService instance) where TService : class
    {
        if (instance == null)
        {
            throw new ArgumentNullException(nameof(instance));
        }

        var capturedInstance = instance;
        AddRegistration<TService>(string.Empty, resolver => capturedInstance);
    }

    /// <summary>
    /// 注册本能力的一个默认服务（懒工厂形态；首次解析时构造，宿主内单例）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="factory">懒工厂（参数为按本能力限定的解析器；不可为 null）。</param>
    public void AddService<TService>(Func<CapabilityServiceResolver, TService> factory) where TService : class
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        AddRegistration(string.Empty, factory);
    }

    /// <summary>
    /// 注册本能力的一个命名服务（实例形态；同类型键多实现时的命名寻址，如 party / group 探针）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="name">命名键（非空）。</param>
    /// <param name="instance">服务实例（不可为 null）。</param>
    public void AddService<TService>(string name, TService instance) where TService : class
    {
        if (instance == null)
        {
            throw new ArgumentNullException(nameof(instance));
        }

        ValidateServiceName(name);
        var capturedInstance = instance;
        AddRegistration<TService>(name, resolver => capturedInstance);
    }

    /// <summary>
    /// 注册本能力的一个命名服务（懒工厂形态）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="name">命名键（非空）。</param>
    /// <param name="factory">懒工厂（不可为 null）。</param>
    public void AddService<TService>(string name, Func<CapabilityServiceResolver, TService> factory) where TService : class
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        ValidateServiceName(name);
        AddRegistration<TService>(name, factory);
    }

    /// <summary>
    /// 解析一个服务（缺失或来源越界即装配期 fail-fast；用于直接声明的必选 / 在场可选依赖）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <returns>服务实例。</returns>
    public TService GetService<TService>() where TService : class
    {
        return _services.ResolveScoped<TService>(_manifest.Id, string.Empty, false);
    }

    /// <summary>
    /// 按可选依赖语义解析（唯一合法缺席 = manifest 声明过的可选能力未装配，返回 null）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <returns>服务实例；声明的可选依赖未装配时为 null。</returns>
    public TService GetOptionalService<TService>() where TService : class
    {
        return _services.ResolveScoped<TService>(_manifest.Id, string.Empty, true);
    }

    /// <summary>
    /// 枚举同类型键的全部实现（Register 期不可用——本方法在注册上下文内恒抛出；
    /// 依赖关系之外的扩展点在 <see cref="CapabilityActivationContext"/> 的 <c>Services</c> 上枚举）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <returns>恒抛出 <see cref="InvalidOperationException"/>。</returns>
    public IReadOnlyList<TService> GetServices<TService>() where TService : class
    {
        return _services.ResolveAllScoped<TService>(_manifest.Id);
    }

    /// <summary>
    /// 获取机制合并校验后的 Options 实例（包内默认 → 配置段 → 代码 Configure → Validate；此后无写入入口）。
    /// </summary>
    /// <typeparam name="TOptions">Options 根类型（须与 manifest 声明一致）。</typeparam>
    /// <returns>合并校验后的实例。</returns>
    public TOptions GetOptions<TOptions>() where TOptions : class
    {
        if (_boundOptions == null)
        {
            // Localization: Capability.Hosting.OptionsTypeMismatch - Options 根类型不符。
            throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.OptionsTypeMismatch), _manifest.Id, "null", typeof(TOptions).FullName));
        }

        if (!(_boundOptions is TOptions matched))
        {
            // Localization: Capability.Hosting.OptionsTypeMismatch - Options 根类型不符。
            throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.OptionsTypeMismatch), _manifest.Id, _boundOptions.GetType().FullName, typeof(TOptions).FullName));
        }

        return matched;
    }

    /// <summary>
    /// 声明一个能力钩子与默认策略（得到可缓存的句柄；热更侧经 <see cref="CapabilityHook{TContract}.Attach"/> 挂载）。
    /// </summary>
    /// <typeparam name="TContract">钩子契约类型。</typeparam>
    /// <param name="defaultImplementation">默认策略实现（不可为 null；无挂载时生效）。</param>
    /// <returns>钩子句柄（句柄可缓存，<c>Current</c> 不可缓存）。</returns>
    public CapabilityHookHandle<TContract> DeclareHook<TContract>(TContract defaultImplementation) where TContract : class
    {
        return CapabilityHook<TContract>.Declare(defaultImplementation, _manifest.Id, _hookDeclarationReleases);
    }

    /// <summary>
    /// 声明一个无默认的宿主扩展点（宿主必须 <c>Override&lt;TService&gt;</c> 提供；缺失即装配期 fail-fast）。
    /// </summary>
    /// <typeparam name="TService">宿主服务类型键。</typeparam>
    public void RequireHostService<TService>() where TService : class
    {
        if (!_services.HasOverride(typeof(TService), string.Empty))
        {
            // Localization: Capability.Hosting.HostServiceMissing - 声明的宿主服务未提供。
            throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.HostServiceMissing), _manifest.Id, typeof(TService).FullName));
        }
    }

    /// <summary>
    /// 声明一个事件订阅（Register 期专用；Activate 开始后总线才接受发布，保证后注册订阅者不丢事件）。
    /// </summary>
    /// <typeparam name="TEvent">事件载荷类型。</typeparam>
    /// <param name="topic">主题（命名约定 <c>&lt;族&gt;.&lt;域&gt;.&lt;事件&gt;</c>，非空）。</param>
    /// <param name="handler">同步订阅体（单个订阅体异常由机制记日志后继续分发）。</param>
    public void Subscribe<TEvent>(string topic, Action<TEvent> handler)
    {
        _eventBus.Subscribe(topic, handler);
    }

    /// <summary>
    /// 注册逐滴答 job（每个调度滴答按装配序执行；单个 job 异常记结构化日志后继续当轮剩余 job）。
    /// </summary>
    /// <param name="jobId">job 标识（能力内唯一，非空）。</param>
    /// <param name="job">job 委托。</param>
    public void AddTickJob(string jobId, Func<CancellationToken, Task> job)
    {
        if (job == null)
        {
            throw new ArgumentNullException(nameof(job));
        }

        _tickJobs.Add(new CapabilityTickJob(CreateJobName(jobId), job));
    }

    /// <summary>
    /// 注册间隔 job（独立周期到点执行，与滴答序无关；异常只影响自身本轮）。
    /// </summary>
    /// <param name="jobId">job 标识（能力内唯一，非空）。</param>
    /// <param name="interval">执行周期（调度壳按 100ms 下限钳制）。</param>
    /// <param name="job">job 委托。</param>
    public void AddIntervalJob(string jobId, TimeSpan interval, Func<CancellationToken, Task> job)
    {
        if (job == null)
        {
            throw new ArgumentNullException(nameof(job));
        }

        _intervalJobs.Add(new CapabilityIntervalJob(CreateJobName(jobId), interval, job));
    }

    /// <summary>
    /// 落一条默认服务描述符（来源 = 本能力；命名键空串 = 默认实现）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="name">命名键（空串 = 默认实现）。</param>
    /// <param name="factory">懒工厂。</param>
    private void AddRegistration<TService>(string name, Func<CapabilityServiceResolver, TService> factory) where TService : class
    {
        var registration = new CapabilityServiceRegistration(typeof(TService), name ?? string.Empty, resolver => factory(resolver), _manifest.Id, false);
        _services.AddDefault(registration);
    }

    /// <summary>
    /// 校验命名键（命名注册非空；默认注册不经本方法）。
    /// </summary>
    /// <param name="name">命名键。</param>
    private static void ValidateServiceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Service name must be a non-empty string.", nameof(name));
        }
    }

    /// <summary>
    /// 生成并登记能力限定的 job 全名（重复即抛 <see cref="InvalidOperationException"/>）。
    /// </summary>
    /// <param name="jobId">job 标识。</param>
    /// <returns>job 全名（<c>能力Id/jobId</c>）。</returns>
    private string CreateJobName(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job id must be a non-empty string.", nameof(jobId));
        }

        var fullName = _manifest.Id + "/" + jobId;
        if (!_registeredJobNames.Add(fullName))
        {
            throw new InvalidOperationException("Duplicate capability job registration: " + fullName);
        }

        return fullName;
    }
}
