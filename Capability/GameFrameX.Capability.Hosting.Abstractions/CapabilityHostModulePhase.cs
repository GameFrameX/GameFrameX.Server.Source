// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:    https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:      https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// 宿主基础设施模块的激活相位（组合选项的 <c>AddHostModule</c> 声明）：
/// 机制在依赖拓扑定序时为相位注入合成定序边——<see cref="BeforeCapabilities"/> 模块先于全部能力模块激活，
/// <see cref="AfterCapabilities"/> 模块晚于全部能力模块与全部 Before 模块激活；停用一律按激活逆序。
/// <para>
/// 维护约束：合成边只参与定序，不进入服务解析来源策略（<c>AllowedOwnersByConsumer</c>）——
/// 宿主模块与能力模块之间的服务可见性仍由 <c>Requires</c> / <c>Optional</c> 显式声明决定。
/// </para>
/// </summary>
public enum CapabilityHostModulePhase
{
    /// <summary>
    /// 早段宿主模块：在全部能力模块激活之前激活（承载旧栈早段固定序列，如数据库初始化、发现层激活、组件注册、热更装载）。
    /// </summary>
    BeforeCapabilities = 0,

    /// <summary>
    /// 晚段宿主模块：在全部能力模块与全部早段宿主模块激活之后激活（承载依赖能力桥就绪的收尾序列，如网络监听开放、ServiceOnline 分发）。
    /// </summary>
    AfterCapabilities = 1,
}
