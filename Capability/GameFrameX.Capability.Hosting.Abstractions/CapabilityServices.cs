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
/// 能力服务注册表（描述符表形态：族基建预置、宿主覆盖与能力模块注册共用同一张
/// 「类型键 + 命名键」索引表；装配完成后经能力宿主的服务面对组合根开放取用）。
/// <para>
/// 维护约束（装配红线）：同键「一个默认 + 至多一个覆盖」——第二个默认或第二个覆盖均装配失败；
/// 解析时覆盖实现总是胜出（与注册先后无关，「组合根先预置、模块再探测」的时序约定已废）；
/// Register 期与懒工厂内的单实例解析按消费者做来源检查（自己 / 族基建 / 宿主覆盖 / 直接声明的必选与
/// 在场可选依赖），越界即装配期 fail-fast；<c>GetServices</c> 枚举只在 Activate 期可用；
/// 机制包不持有任何能力类型（零能力硬编码红线）。
/// </para>
/// </summary>
public sealed class CapabilityServices
{
    /// <summary>
    /// 注册与解析共用锁（懒工厂首次构造在锁内执行；同线程重入解析安全）。
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    /// 注册描述符表（注册序；审计与元端点数据源，被覆盖的默认实现保留在表内）。
    /// </summary>
    private readonly List<CapabilityServiceRegistration> _registrations = new();

    /// <summary>
    /// 键 → 胜出注册条目索引（键 = 类型键 + 命名键；覆盖注册即替换胜出条目）。
    /// </summary>
    private readonly Dictionary<string, RegistrationEntry> _entriesByKey = new(StringComparer.Ordinal);

    /// <summary>
    /// 消费者能力 Id → 允许解析的来源 Id 集合（族 Id 恒在允许集）。
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _allowedOwnersByConsumer = new(StringComparer.Ordinal);

    /// <summary>
    /// 消费者能力 Id → 已声明但未装配的可选依赖 Id 列表（每个启用模块都有条目，可为空列表）。
    /// <para>维护约束：模块作用域的可选解析只有在该列表非空时才允许返回 null——对方能力在场却没注册该服务
    /// 必须 fail-fast（设计 §4.9.1），不能静默丢功能。</para>
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<string>> _unassembledOptionalByConsumer = new(StringComparer.Ordinal);

    /// <summary>
    /// 懒工厂解析链（正在构造中的键描述，按进入顺序；仅在 <see cref="_sync"/> 内读写——工厂在锁内执行，链天然单线程）。
    /// </summary>
    private readonly List<string> _resolutionChain = new();

    /// <summary>
    /// 族 Id（族基建与宿主覆盖的来源标记；族基建注册前由装配器注入）。
    /// </summary>
    private string _familyCapabilityId;

    /// <summary>
    /// 是否已进入 Activate 期（<c>GetServices</c> 枚举开关）。
    /// </summary>
    private bool _activationStarted;

    /// <summary>
    /// 注册条目（描述符 + 懒实例缓存）。
    /// </summary>
    private sealed class RegistrationEntry
    {
        /// <summary>
        /// 注册描述符。
        /// </summary>
        public CapabilityServiceRegistration Registration;

        /// <summary>
        /// 懒构造的实例（首次解析前为 null）。
        /// </summary>
        public object Instance;

        /// <summary>
        /// 是否已完成首次解析。
        /// </summary>
        public bool Resolved;

        /// <summary>
        /// 是否正在执行懒工厂（同键重入 = 循环依赖）。
        /// </summary>
        public bool Resolving;
    }

    /// <summary>
    /// 初始化 <see cref="CapabilityServices"/>（空表；族 Id 与解析策略由装配器随后注入）。
    /// </summary>
    public CapabilityServices()
    {
    }

    /// <summary>
    /// 获取全部注册描述符（注册序；admin 元端点与审计面数据源）。
    /// </summary>
    public IReadOnlyList<CapabilityServiceRegistration> Registrations
    {
        get
        {
            lock (_sync)
            {
                return _registrations.ToArray();
            }
        }
    }

    /// <summary>
    /// 解析默认命名服务（宿主侧取用：装配完成后的组合根 / 管理面；不做来源检查）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <returns>服务实例。</returns>
    public TService GetService<TService>() where TService : class
    {
        return GetService<TService>(string.Empty);
    }

    /// <summary>
    /// 解析命名服务（宿主侧取用；缺失即抛 <see cref="InvalidOperationException"/>）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="name">命名键（空串 = 默认实现）。</param>
    /// <returns>服务实例。</returns>
    public TService GetService<TService>(string name) where TService : class
    {
        var entry = FindEntry(typeof(TService), name ?? string.Empty);
        if (entry == null)
        {
            // Localization: Capability.Hosting.ServiceNotRegistered - 能力服务未注册。
            throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.ServiceNotRegistered), BuildKeyDescription(typeof(TService), name)));
        }

        return (TService)GetInstance(entry);
    }

    /// <summary>
    /// 按可选语义解析默认命名服务（宿主侧取用；缺失返回 null）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <returns>服务实例；未注册为 null。</returns>
    public TService GetOptionalService<TService>() where TService : class
    {
        var entry = FindEntry(typeof(TService), string.Empty);
        return entry == null ? null : (TService)GetInstance(entry);
    }

    /// <summary>
    /// 枚举同类型键的全部实现（宿主侧取用；注册序；同键被覆盖的默认实现不列出）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <returns>实现集合（无实现为空集合）。</returns>
    public IReadOnlyList<TService> GetServices<TService>() where TService : class
    {
        return ResolveAllScoped<TService>(null);
    }

    /// <summary>
    /// 注入族 Id（族基建注册前由装配器调用一次）。
    /// </summary>
    /// <param name="familyCapabilityId">族 Id。</param>
    internal void SetFamilyCapabilityId(string familyCapabilityId)
    {
        lock (_sync)
        {
            _familyCapabilityId = familyCapabilityId;
        }
    }

    /// <summary>
    /// 注册默认实现（能力模块或族基建；同键第二个默认 = 装配失败；键上已有覆盖时覆盖仍胜出）。
    /// </summary>
    /// <param name="registration">注册描述符（<see cref="CapabilityServiceRegistration.IsOverride"/> 为 false）。</param>
    internal void AddDefault(CapabilityServiceRegistration registration)
    {
        if (registration == null)
        {
            throw new ArgumentNullException(nameof(registration));
        }

        if (registration.IsOverride)
        {
            throw new ArgumentException("Registration must be a default (non-override) registration.", nameof(registration));
        }

        lock (_sync)
        {
            if (TryFindEntryLocked(registration.ServiceType, registration.Name, out var existing))
            {
                if (!existing.Registration.IsOverride)
                {
                    // Localization: Capability.Hosting.ServiceRegistrationDuplicate - 能力服务重复注册。
                    throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.ServiceRegistrationDuplicate), BuildKeyDescription(registration.ServiceType, registration.Name)));
                }

                // 覆盖先注册（族基建 / 宿主覆盖先于模块默认是常态）：默认入审计表，键上仍解析覆盖实现。
                _registrations.Add(registration);
                return;
            }

            _registrations.Add(registration);
            _entriesByKey[BuildKey(registration.ServiceType, registration.Name)] = new RegistrationEntry { Registration = registration, };
        }
    }

    /// <summary>
    /// 注册覆盖实现（族或宿主；同键第二个覆盖 = 装配失败；解析时覆盖胜出——与注册先后无关）。
    /// </summary>
    /// <param name="registration">覆盖描述符（<see cref="CapabilityServiceRegistration.IsOverride"/> 为 true）。</param>
    internal void AddOverride(CapabilityServiceRegistration registration)
    {
        if (registration == null)
        {
            throw new ArgumentNullException(nameof(registration));
        }

        if (!registration.IsOverride)
        {
            throw new ArgumentException("Registration must be an override registration.", nameof(registration));
        }

        lock (_sync)
        {
            if (TryFindEntryLocked(registration.ServiceType, registration.Name, out var existingOverride) && existingOverride.Registration.IsOverride)
            {
                // Localization: Capability.Hosting.ServiceOverrideDuplicate - 能力服务覆盖重复。
                throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.ServiceOverrideDuplicate), BuildKeyDescription(registration.ServiceType, registration.Name)));
            }

            _registrations.Add(registration);
            _entriesByKey[BuildKey(registration.ServiceType, registration.Name)] = new RegistrationEntry { Registration = registration, };
        }
    }

    /// <summary>
    /// 查找键上的覆盖实现（宿主扩展点声明校验：<c>RequireHostService</c> 需要）。
    /// </summary>
    /// <param name="serviceType">服务类型键。</param>
    /// <param name="name">命名键。</param>
    /// <returns>存在覆盖实现返回 true。</returns>
    internal bool HasOverride(Type serviceType, string name)
    {
        lock (_sync)
        {
            return TryFindEntryLocked(serviceType, name, out var entry) && entry.Registration.IsOverride;
        }
    }

    /// <summary>
    /// 注入解析来源策略（依赖检查完成后、Register 循环前由装配器调用一次）。
    /// </summary>
    /// <param name="allowedOwnersByConsumer">消费者能力 Id → 允许解析的来源 Id 集合。</param>
    /// <param name="unassembledOptionalByConsumer">消费者能力 Id → 已声明但未装配的可选依赖 Id 列表（每个启用模块一条）。</param>
    internal void SetResolutionPolicy(IReadOnlyDictionary<string, HashSet<string>> allowedOwnersByConsumer, IReadOnlyDictionary<string, List<string>> unassembledOptionalByConsumer)
    {
        lock (_sync)
        {
            _allowedOwnersByConsumer.Clear();
            foreach (var pair in allowedOwnersByConsumer)
            {
                _allowedOwnersByConsumer[pair.Key] = pair.Value;
            }

            _unassembledOptionalByConsumer.Clear();
            foreach (var pair in unassembledOptionalByConsumer)
            {
                _unassembledOptionalByConsumer[pair.Key] = pair.Value;
            }
        }
    }

    /// <summary>
    /// 标记进入 Activate 期（宿主启动时调用；开启 <c>GetServices</c> 枚举）。
    /// </summary>
    internal void MarkActivationStarted()
    {
        lock (_sync)
        {
            _activationStarted = true;
        }
    }

    /// <summary>
    /// 按消费者限定的单实例解析（来源检查 + 懒构造；消费者为 null 表示宿主侧取用，跳过来源检查）。
    /// <para>可选语义未命中时：消费者是启用模块且声明的可选依赖全部已装配（或未声明任何可选依赖）→
    /// 抛 <see cref="InvalidOperationException"/>（能力在场但服务未注册）；存在未装配的已声明可选依赖 → 返回 null。
    /// 非模块消费者（族基建懒工厂）没有 manifest 可选声明，保持返回 null。</para>
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="consumerCapabilityId">消费者能力 Id（null = 宿主侧）。</param>
    /// <param name="name">命名键。</param>
    /// <param name="optional">是否可选语义（声明的可选能力缺席时返回 null 而非抛出）。</param>
    /// <returns>服务实例；可选语义且声明的可选能力缺席时为 null。</returns>
    internal TService ResolveScoped<TService>(string consumerCapabilityId, string name, bool optional) where TService : class
    {
        RegistrationEntry entry;
        IReadOnlyList<string> unassembledOptional = null;
        lock (_sync)
        {
            entry = TryFindEntryLocked(typeof(TService), name, out var found) ? found : null;
            if (entry == null && consumerCapabilityId != null)
            {
                _unassembledOptionalByConsumer.TryGetValue(consumerCapabilityId, out unassembledOptional);
            }
        }

        if (entry == null)
        {
            if (optional && (unassembledOptional == null || unassembledOptional.Count > 0))
            {
                return null;
            }

            if (optional)
            {
                // Localization: Capability.Hosting.OptionalServiceNotRegistered - 可选依赖均已装配但服务未注册。
                throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.OptionalServiceNotRegistered), consumerCapabilityId, BuildKeyDescription(typeof(TService), name)));
            }

            // Localization: Capability.Hosting.ServiceNotRegistered - 能力服务未注册。
            throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.ServiceNotRegistered), BuildKeyDescription(typeof(TService), name)));
        }

        ValidateResolutionScope(consumerCapabilityId, entry.Registration);
        return (TService)GetInstance(entry);
    }

    /// <summary>
    /// 按消费者限定的多实现枚举（仅 Activate 期；不做来源检查——枚举即依赖关系之外扩展点的合法用法；
    /// 同键被覆盖的默认实现不列出）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="consumerCapabilityId">消费者能力 Id（诊断用）。</param>
    /// <returns>实现集合（注册序）。</returns>
    internal IReadOnlyList<TService> ResolveAllScoped<TService>(string consumerCapabilityId) where TService : class
    {
        lock (_sync)
        {
            if (!_activationStarted)
            {
                // Localization: Capability.Hosting.ServicesEnumerationForbidden - GetServices 只能在 Activate 期调用。
                throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.ServicesEnumerationForbidden), consumerCapabilityId ?? "host", typeof(TService).FullName));
            }

            var result = new List<TService>();
            for (var i = 0; i < _registrations.Count; i++)
            {
                var registration = _registrations[i];
                if (registration.ServiceType != typeof(TService))
                {
                    continue;
                }

                var winner = _entriesByKey[BuildKey(registration.ServiceType, registration.Name)];
                if (!ReferenceEquals(winner.Registration, registration))
                {
                    continue;
                }

                result.Add((TService)GetInstance(winner));
            }

            return result;
        }
    }

    /// <summary>
    /// 解析来源检查（消费者只允许解析自己 / 族基建 / 宿主覆盖 / 直接声明的必选与在场可选依赖的产物）。
    /// </summary>
    /// <param name="consumerCapabilityId">消费者能力 Id（null = 宿主侧，跳过检查）。</param>
    /// <param name="registration">命中描述符。</param>
    private void ValidateResolutionScope(string consumerCapabilityId, CapabilityServiceRegistration registration)
    {
        if (consumerCapabilityId == null)
        {
            return;
        }

        lock (_sync)
        {
            if (string.Equals(registration.OwnerCapabilityId, _familyCapabilityId, StringComparison.Ordinal))
            {
                return;
            }

            if (_allowedOwnersByConsumer.TryGetValue(consumerCapabilityId, out var allowed) && allowed.Contains(registration.OwnerCapabilityId))
            {
                return;
            }
        }

        // Localization: Capability.Hosting.ServiceResolutionForbidden - Register 期越界解析。
        throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.ServiceResolutionForbidden), consumerCapabilityId, registration.ServiceType.FullName, registration.OwnerCapabilityId));
    }

    /// <summary>
    /// 懒构造并缓存实例（首次解析时执行工厂；工厂内解析按工厂所属能力做来源检查）。
    /// <para>维护约束：工厂执行期间条目标记为「解析中」，同键重入即循环依赖——直接抛 <see cref="InvalidOperationException"/>
    /// 并列出完整解析链；否则可重入的锁会让 A→B→A 无限递归直至不可捕获的栈溢出。</para>
    /// </summary>
    /// <param name="entry">注册条目。</param>
    /// <returns>服务实例。</returns>
    private object GetInstance(RegistrationEntry entry)
    {
        lock (_sync)
        {
            if (entry.Resolved)
            {
                return entry.Instance;
            }

            var keyDescription = BuildKeyDescription(entry.Registration.ServiceType, entry.Registration.Name);
            if (entry.Resolving)
            {
                var chain = new List<string>(_resolutionChain) { keyDescription, };
                // Localization: Capability.Hosting.ServiceResolutionCycle - 懒工厂循环解析。
                throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.ServiceResolutionCycle), string.Join(" -> ", chain)));
            }

            entry.Resolving = true;
            _resolutionChain.Add(keyDescription);
            try
            {
                var resolver = new CapabilityServiceResolver(this, entry.Registration.OwnerCapabilityId);
                var instance = entry.Registration.Factory(resolver);
                if (instance == null)
                {
                    throw new InvalidOperationException("Capability service factory returned null: " + keyDescription);
                }

                entry.Instance = instance;
                entry.Resolved = true;
                return instance;
            }
            finally
            {
                entry.Resolving = false;
                _resolutionChain.RemoveAt(_resolutionChain.Count - 1);
            }
        }
    }

    /// <summary>
    /// 查找注册条目（线程安全）。
    /// </summary>
    /// <param name="serviceType">服务类型键。</param>
    /// <param name="name">命名键。</param>
    /// <returns>注册条目；未注册为 null。</returns>
    private RegistrationEntry FindEntry(Type serviceType, string name)
    {
        lock (_sync)
        {
            return TryFindEntryLocked(serviceType, name, out var entry) ? entry : null;
        }
    }

    /// <summary>
    /// 查找注册条目（须在锁内调用）。
    /// </summary>
    /// <param name="serviceType">服务类型键。</param>
    /// <param name="name">命名键。</param>
    /// <param name="entry">输出：注册条目。</param>
    /// <returns>命中返回 true。</returns>
    private bool TryFindEntryLocked(Type serviceType, string name, out RegistrationEntry entry)
    {
        return _entriesByKey.TryGetValue(BuildKey(serviceType, name), out entry);
    }

    /// <summary>
    /// 生成键（类型全名 + 命名键；Ordinal）。
    /// </summary>
    /// <param name="serviceType">服务类型键。</param>
    /// <param name="name">命名键。</param>
    /// <returns>索引键。</returns>
    private static string BuildKey(Type serviceType, string name)
    {
        return serviceType.FullName + "|" + (name ?? string.Empty);
    }

    /// <summary>
    /// 生成键描述（诊断与错误信息用）。
    /// </summary>
    /// <param name="serviceType">服务类型键。</param>
    /// <param name="name">命名键。</param>
    /// <returns>键描述（类型全名，命名键非空时附命名键）。</returns>
    private static string BuildKeyDescription(Type serviceType, string name)
    {
        var displayName = serviceType.FullName;
        if (!string.IsNullOrEmpty(name))
        {
            displayName += "[" + name + "]";
        }

        return displayName;
    }
}
