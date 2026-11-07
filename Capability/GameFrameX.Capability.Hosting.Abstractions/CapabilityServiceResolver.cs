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

namespace GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// 能力服务解析器（懒工厂与激活上下文共用的按消费者身份限定的只读解析面）。
/// <para>
/// 维护约束：单实例解析按消费者（工厂所属能力或当前模块）做来源检查——只允许解析
/// 自己 / 族基建 / 宿主覆盖 / 直接声明的必选与在场可选依赖；
/// <see cref="GetServices{TService}"/> 只在 Activate 期可用（全部模块完成 Register 后枚举才完整）。
/// </para>
/// </summary>
public sealed class CapabilityServiceResolver
{
    /// <summary>
    /// 所属服务注册表。
    /// </summary>
    private readonly CapabilityServices _services;

    /// <summary>
    /// 消费者能力 Id（来源检查主体）。
    /// </summary>
    private readonly string _consumerCapabilityId;

    /// <summary>
    /// 初始化 <see cref="CapabilityServiceResolver"/>（仅机制构造）。
    /// </summary>
    /// <param name="services">服务注册表。</param>
    /// <param name="consumerCapabilityId">消费者能力 Id。</param>
    internal CapabilityServiceResolver(CapabilityServices services, string consumerCapabilityId)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _consumerCapabilityId = consumerCapabilityId;
    }

    /// <summary>
    /// 解析一个服务（缺失即抛 <see cref="InvalidOperationException"/>；来源越界即装配期 fail-fast）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <returns>服务实例。</returns>
    public TService GetService<TService>() where TService : class
    {
        return _services.ResolveScoped<TService>(_consumerCapabilityId, string.Empty, false);
    }

    /// <summary>
    /// 解析一个命名服务（同键多实现时的命名寻址；缺失即抛）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="name">命名键（空串 = 默认实现）。</param>
    /// <returns>服务实例。</returns>
    public TService GetService<TService>(string name) where TService : class
    {
        return _services.ResolveScoped<TService>(_consumerCapabilityId, name, false);
    }

    /// <summary>
    /// 按可选依赖语义解析（唯一合法缺席 = manifest 声明过的可选能力未装配；返回 null）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <returns>服务实例；声明过的可选依赖未装配时为 null。</returns>
    public TService GetOptionalService<TService>() where TService : class
    {
        return _services.ResolveScoped<TService>(_consumerCapabilityId, string.Empty, true);
    }

    /// <summary>
    /// 枚举同类型键的全部实现（命名 + 默认；仅 Activate 期可用，Register 期调用直接抛出）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <returns>实现集合（注册序；无实现为空集合）。</returns>
    public IReadOnlyList<TService> GetServices<TService>() where TService : class
    {
        return _services.ResolveAllScoped<TService>(_consumerCapabilityId);
    }
}
