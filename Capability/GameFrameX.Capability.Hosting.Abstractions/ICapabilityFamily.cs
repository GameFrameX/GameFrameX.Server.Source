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
/// 能力族契约（一族能力的组合根骨架：族标识 + 配置段名 + 部署程序集前缀 + 族基建注册面）。
/// <para>
/// 维护约束（机制开放红线）：族实现落在本族脊柱包（如 Online.Abstractions），机制层（Hosting 三件套）
/// 零能力硬编码——第二个能力族只需实现本接口，无需改动机制；<see cref="RegisterInfrastructure"/> 在
/// 模块扫描与注册之前执行，族基建（作用域 / 事件出口 / 幂等 / 审计 / store 覆盖）经
/// <see cref="CapabilityFamilyContext"/> 以描述符形态注册。
/// </para>
/// </summary>
public interface ICapabilityFamily
{
    /// <summary>
    /// 获取族标识（如 <c>online</c>；族基建与宿主覆盖的来源标记）。
    /// </summary>
    string FamilyId { get; }

    /// <summary>
    /// 获取配置段名（如 <c>Online</c>；诊断与错误路径前缀用，机制不解释其余族级键）。
    /// </summary>
    string ConfigurationSectionName { get; }

    /// <summary>
    /// 获取部署程序集前缀（部署目录命名约定扫描圈定 <c>{前缀}.*.dll</c>，如 <c>GameFrameX.Capability.Online</c>）。
    /// </summary>
    string DeploymentAssemblyPrefix { get; }

    /// <summary>
    /// 注册族基建（在模块扫描与注册之前执行一次）。
    /// </summary>
    /// <param name="context">族基建注册面（配置段 + 事件发布口 + 服务注册 / 覆盖）。</param>
    void RegisterInfrastructure(CapabilityFamilyContext context);
}
