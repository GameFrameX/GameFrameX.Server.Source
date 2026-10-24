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

using System.Text.Json;

namespace GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// 族基建注册面（<see cref="ICapabilityFamily.RegisterInfrastructure"/> 的上下文：族配置段 + 事件发布口 + 服务注册 / 覆盖）。
/// <para>
/// 维护约束：族基建注册先于模块扫描与注册，来源标记 = 族 Id（对全部模块可解析）；
/// 族级配置键（如 <c>UseGameDbStores</c>）由族自行从 <see cref="ConfigurationSection"/> 解读，
/// 机制只解释 <c>Settings</c> 子对象（能力级参数）；store 覆盖走 <see cref="Override{TService}(TService)"/>
/// （一个默认 + 至多一个覆盖）。
/// </para>
/// </summary>
public sealed class CapabilityFamilyContext
{
    /// <summary>
    /// 获取族配置段（原始 JSON；<c>default</c> 表示进程配置中没有该段）。
    /// </summary>
    public JsonElement ConfigurationSection { get; }

    /// <summary>
    /// 获取机制事件发布口（族业务发布面委托到机制总线的接线位）。
    /// </summary>
    public ICapabilityEventPublisher EventPublisher { get; }

    /// <summary>
    /// 服务注册表。
    /// </summary>
    private readonly CapabilityServices _services;

    /// <summary>
    /// 族 Id（注册来源标记）。
    /// </summary>
    private readonly string _familyId;

    /// <summary>
    /// 初始化 <see cref="CapabilityFamilyContext"/>（仅机制装配器构造）。
    /// </summary>
    /// <param name="familyId">族 Id。</param>
    /// <param name="configurationSection">族配置段（原始 JSON）。</param>
    /// <param name="eventPublisher">机制事件发布口。</param>
    /// <param name="services">服务注册表。</param>
    internal CapabilityFamilyContext(string familyId, JsonElement configurationSection, ICapabilityEventPublisher eventPublisher, CapabilityServices services)
    {
        _familyId = familyId;
        ConfigurationSection = configurationSection;
        EventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// 注册一个族基建默认服务（实例形态；来源 = 族 Id，对全部模块可解析）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="instance">服务实例（不可为 null）。</param>
    public void AddService<TService>(TService instance) where TService : class
    {
        AddService(string.Empty, instance);
    }

    /// <summary>
    /// 注册一个族基建默认服务（懒工厂形态）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="factory">懒工厂（不可为 null）。</param>
    public void AddService<TService>(Func<CapabilityServiceResolver, TService> factory) where TService : class
    {
        AddService(string.Empty, factory);
    }

    /// <summary>
    /// 注册一个族基建命名服务（实例形态）。
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

        var capturedInstance = instance;
        AddRegistration<TService>(name, resolver => capturedInstance, false);
    }

    /// <summary>
    /// 注册一个族基建命名服务（懒工厂形态）。
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

        AddRegistration<TService>(name, factory, false);
    }

    /// <summary>
    /// 声明一个族级覆盖（替换模块默认实现，如 GameDb store；同键第二个覆盖 = 装配失败）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="instance">覆盖实现（不可为 null）。</param>
    public void Override<TService>(TService instance) where TService : class
    {
        Override(instance == null ? null : new Func<CapabilityServiceResolver, TService>(resolver => instance));
    }

    /// <summary>
    /// 声明一个族级覆盖（懒工厂形态）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="factory">覆盖懒工厂（不可为 null）。</param>
    public void Override<TService>(Func<CapabilityServiceResolver, TService> factory) where TService : class
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        AddRegistration<TService>(string.Empty, factory, true);
    }

    /// <summary>
    /// 落一条注册描述符（来源 = 族 Id）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="name">命名键（空串 = 默认实现）。</param>
    /// <param name="factory">懒工厂。</param>
    /// <param name="isOverride">是否覆盖。</param>
    private void AddRegistration<TService>(string name, Func<CapabilityServiceResolver, TService> factory, bool isOverride) where TService : class
    {
        if (!string.IsNullOrEmpty(name) && string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Service name must be a non-empty string.", nameof(name));
        }

        var registration = new CapabilityServiceRegistration(typeof(TService), name ?? string.Empty, resolver => factory(resolver), _familyId, isOverride);
        if (isOverride)
        {
            _services.AddOverride(registration);
        }
        else
        {
            _services.AddDefault(registration);
        }
    }
}
