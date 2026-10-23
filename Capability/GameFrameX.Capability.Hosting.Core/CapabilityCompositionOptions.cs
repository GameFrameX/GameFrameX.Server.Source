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

using System.Text.Json;
using GameFrameX.Capability.Hosting.Abstractions;

namespace GameFrameX.Capability.Hosting.Core;

/// <summary>
/// 能力组合选项（<see cref="CapabilityComposition.Compose"/> 的唯一配置面：族配置段 + 装配日志出口 +
/// 双面开关 + 宿主选项 + 代码层 Options 覆盖与服务覆盖 + 管理面工厂）。
/// <para>
/// 维护约束（GFX0018 符号检查面）：<see cref="Configure{TOptions}"/> / <see cref="Override{TService}(TService)"/> /
/// <see cref="SetAdminApiPlaneFactory"/> 只在宿主组合入口合法；装配日志出口是必填参数（调用方无法遗漏）；
/// 机制只解释族配置段里的 <c>Settings</c> 子对象（能力级参数），族级键由宿主 / 族自行解读后映射到本类属性。
/// </para>
/// </summary>
public sealed class CapabilityCompositionOptions
{
    /// <summary>
    /// 代码层 Options 覆盖回调（Configure 注册序执行；按 Options 根类型精确匹配目标能力）。
    /// </summary>
    private readonly List<ConfigureEntry> _configureEntries = new();

    /// <summary>
    /// 宿主服务覆盖（Override 注册序落表；同键第二个覆盖 = 装配失败）。
    /// </summary>
    private readonly List<OverrideEntry> _overrideEntries = new();

    /// <summary>
    /// 管理面工厂（由 Hosting.AdminApi 包的 UseAdminApi 扩展设置）。
    /// </summary>
    private Func<CapabilityHost, ICapabilityAdminApiPlane> _adminApiPlaneFactory;

    /// <summary>
    /// 初始化 <see cref="CapabilityCompositionOptions"/>。
    /// </summary>
    /// <param name="familyConfigurationSection">族配置段原始 JSON（如 <c>Online</c> 段；无段传 <c>default</c>）。</param>
    /// <param name="assemblyLog">装配日志出口（必填；Launcher 桥接统一日志，测试用收集器）。</param>
    public CapabilityCompositionOptions(JsonElement familyConfigurationSection, ICapabilityAssemblyLog assemblyLog)
    {
        FamilyConfigurationSection = familyConfigurationSection;
        AssemblyLog = assemblyLog ?? throw new ArgumentNullException(nameof(assemblyLog));
    }

    /// <summary>
    /// 获取族配置段原始 JSON（机制只解释其 <c>Settings</c> 子对象）。
    /// </summary>
    public JsonElement FamilyConfigurationSection { get; }

    /// <summary>
    /// 获取装配日志出口（必填；装配期与运行期观测共用）。
    /// </summary>
    public ICapabilityAssemblyLog AssemblyLog { get; }

    /// <summary>
    /// 获取或设置是否启用业务运行面（默认 true；关闭时 <see cref="CapabilityRuntime.Host"/> 为 null——
    /// admin-only 形态，模块仍注册但宿主不激活、调度不启动）。
    /// </summary>
    public bool EnableRuntime { get; set; } = true;

    /// <summary>
    /// 获取或设置业务宿主选项（作用域三元组 + 调度间隔；作用域对管理面授权同样必填）。
    /// </summary>
    public CapabilityHostOptions HostOptions { get; set; } = new CapabilityHostOptions();

    /// <summary>
    /// 获取或设置能力装配过滤器（白名单 / 排除表；null = 全装配——空过滤器全装配红线）。
    /// </summary>
    public CapabilityAssemblyFilter Filter { get; set; }

    /// <summary>
    /// 获取或设置调度时钟（null = 系统时钟；测试注入虚拟时钟驱动 interval 到期）。
    /// </summary>
    public ISchedulerClock SchedulerClock { get; set; }

    /// <summary>
    /// 获取或设置调度观测出口（null = 缺省日志观察器；宿主接入真实指标管道时替换）。
    /// </summary>
    public ICapabilitySchedulerObserver SchedulerObserver { get; set; }

    /// <summary>
    /// 声明代码层 Options 覆盖（只改需要改的字段；合并优先级：包内默认 &lt; 配置段 &lt; 代码 Configure）。
    /// </summary>
    /// <typeparam name="TOptions">Options 根类型（须由某个已发现能力声明）。</typeparam>
    /// <param name="configure">改写回调（不可为 null）。</param>
    public void Configure<TOptions>(Action<TOptions> configure) where TOptions : class
    {
        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        _configureEntries.Add(new ConfigureEntry(typeof(TOptions), options => configure((TOptions)options)));
    }

    /// <summary>
    /// 声明一个宿主服务覆盖（实例形态；满足模块的 <c>RequireHostService</c> 或替换模块默认实现）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="instance">覆盖实现（不可为 null）。</param>
    public void Override<TService>(TService instance) where TService : class
    {
        if (instance == null)
        {
            throw new ArgumentNullException(nameof(instance));
        }

        var capturedInstance = instance;
        _overrideEntries.Add(new OverrideEntry(typeof(TService), resolver => capturedInstance));
    }

    /// <summary>
    /// 声明一个宿主服务覆盖（懒工厂形态）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="factory">覆盖懒工厂（不可为 null）。</param>
    public void Override<TService>(Func<CapabilityServiceResolver, TService> factory) where TService : class
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        _overrideEntries.Add(new OverrideEntry(typeof(TService), resolver => factory(resolver)));
    }

    /// <summary>
    /// 设置管理面工厂（Hosting.AdminApi 包的 UseAdminApi 扩展调用；机制组合时构建管理面）。
    /// </summary>
    /// <param name="factory">管理面工厂（不可为 null；入参为已装配宿主）。</param>
    public void SetAdminApiPlaneFactory(Func<CapabilityHost, ICapabilityAdminApiPlane> factory)
    {
        _adminApiPlaneFactory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// 获取代码层 Options 覆盖回调表（机制装配期读取）。
    /// </summary>
    internal IReadOnlyList<ConfigureEntry> ConfigureEntries
    {
        get { return _configureEntries; }
    }

    /// <summary>
    /// 获取宿主服务覆盖表（机制装配期读取）。
    /// </summary>
    internal IReadOnlyList<OverrideEntry> OverrideEntries
    {
        get { return _overrideEntries; }
    }

    /// <summary>
    /// 获取管理面工厂（未设置时启用管理面 = 组合失败：需引用 Hosting.AdminApi 并调用 UseAdminApi）。
    /// </summary>
    internal Func<CapabilityHost, ICapabilityAdminApiPlane> AdminApiPlaneFactory
    {
        get { return _adminApiPlaneFactory; }
    }

    /// <summary>
    /// 代码层 Options 覆盖回调条目。
    /// </summary>
    internal sealed class ConfigureEntry
    {
        /// <summary>
        /// 初始化 <see cref="ConfigureEntry"/>。
        /// </summary>
        /// <param name="optionsType">Options 根类型。</param>
        /// <param name="configure">装箱改写回调。</param>
        public ConfigureEntry(Type optionsType, Action<object> configure)
        {
            OptionsType = optionsType;
            Configure = configure;
        }

        /// <summary>
        /// 获取 Options 根类型。
        /// </summary>
        public Type OptionsType { get; }

        /// <summary>
        /// 获取装箱改写回调。
        /// </summary>
        public Action<object> Configure { get; }
    }

    /// <summary>
    /// 宿主服务覆盖条目。
    /// </summary>
    internal sealed class OverrideEntry
    {
        /// <summary>
        /// 初始化 <see cref="OverrideEntry"/>。
        /// </summary>
        /// <param name="serviceType">服务类型键。</param>
        /// <param name="factory">懒工厂。</param>
        public OverrideEntry(Type serviceType, Func<CapabilityServiceResolver, object> factory)
        {
            ServiceType = serviceType;
            Factory = factory;
        }

        /// <summary>
        /// 获取服务类型键。
        /// </summary>
        public Type ServiceType { get; }

        /// <summary>
        /// 获取懒工厂。
        /// </summary>
        public Func<CapabilityServiceResolver, object> Factory { get; }
    }
}
