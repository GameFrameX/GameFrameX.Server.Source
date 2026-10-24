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
/// 能力激活上下文（宿主启动期对单个能力模块开放的激活面：只读解析 + 桥发布）。
/// <para>
/// 维护约束：全部模块已完成 Register，本上下文的 <see cref="Services"/> 可枚举依赖关系之外的扩展点
///（<c>GetServices</c> 此时才可用）；单实例解析仍做来源检查（枚举之外不允许偷用未声明依赖的单实现）；
/// 桥的发布者身份 = 当前模块 manifest Id，由机制注入；撤销由宿主停止期自动完成，模块无撤销入口。
/// </para>
/// </summary>
public sealed class CapabilityActivationContext
{
    /// <summary>
    /// 只读解析面（按本能力限定来源检查；<c>GetServices</c> 在本上下文可用）。
    /// </summary>
    public CapabilityServiceResolver Services { get; }

    /// <summary>
    /// 桥发布面（发布者身份 = 本能力）。
    /// </summary>
    public CapabilityBridgePublisher Bridges { get; }

    /// <summary>
    /// 初始化 <see cref="CapabilityActivationContext"/>（仅机制构造）。
    /// </summary>
    /// <param name="services">按本能力限定的解析器。</param>
    /// <param name="bridges">桥发布面。</param>
    internal CapabilityActivationContext(CapabilityServiceResolver services, CapabilityBridgePublisher bridges)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Bridges = bridges ?? throw new ArgumentNullException(nameof(bridges));
    }
}
