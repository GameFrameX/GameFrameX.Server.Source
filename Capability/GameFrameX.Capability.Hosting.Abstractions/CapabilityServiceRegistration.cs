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
/// 能力服务注册描述符（类型键 + 命名键 + 懒工厂 + 来源 + 覆盖声明的机制注册单元）。
/// <para>
/// 维护约束：服务键 = 调用点书写的 <c>TService</c> 类型与命名键
/// （无接口自动映射、无自动构造——显式注册优于反射魔法，不上通用 DI 容器）；
/// 同键「一个默认 + 至多一个覆盖」，第二个默认或第二个覆盖均装配失败；
/// <see cref="OwnerCapabilityId"/> 是解析来源检查（Register 期只允许解析自己 / 族基建 / 宿主覆盖 /
/// 直接声明的必选与在场可选依赖）与 admin 审计面的依据。
/// </para>
/// </summary>
public sealed class CapabilityServiceRegistration
{
    /// <summary>
    /// 初始化 <see cref="CapabilityServiceRegistration"/>。
    /// </summary>
    /// <param name="serviceType">服务类型键（不可为 null）。</param>
    /// <param name="name">命名键（缺省实现传空串；不可为 null）。</param>
    /// <param name="factory">懒工厂（首次解析时构造，宿主内单例；不可为 null）。</param>
    /// <param name="ownerCapabilityId">来源（族 Id 或能力 Id；不可为 null 或空）。</param>
    /// <param name="isOverride">是否显式覆盖声明（覆盖实现解析时总是胜出）。</param>
    public CapabilityServiceRegistration(Type serviceType, string name, Func<CapabilityServiceResolver, object> factory, string ownerCapabilityId, bool isOverride)
    {
        ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
        Name = name ?? string.Empty;
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        OwnerCapabilityId = ownerCapabilityId ?? throw new ArgumentNullException(nameof(ownerCapabilityId));
        IsOverride = isOverride;
    }

    /// <summary>
    /// 获取服务类型键（调用点书写形态，可为接口）。
    /// </summary>
    public Type ServiceType { get; }

    /// <summary>
    /// 获取命名键（空串 = 默认实现）。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 获取懒工厂（首次解析时构造，宿主内单例；工厂内解析按工厂所属能力做来源检查）。
    /// </summary>
    public Func<CapabilityServiceResolver, object> Factory { get; }

    /// <summary>
    /// 获取来源（族 Id 或能力 Id；admin 审计面）。
    /// </summary>
    public string OwnerCapabilityId { get; }

    /// <summary>
    /// 获取是否为显式覆盖声明（族或宿主替换模块默认实现）。
    /// </summary>
    public bool IsOverride { get; }
}
