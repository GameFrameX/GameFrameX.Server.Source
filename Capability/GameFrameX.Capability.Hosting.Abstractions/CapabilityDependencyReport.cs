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
/// 能力依赖状态条目（装配报告与 <c>list_capabilities</c> 的依赖数据面）。
/// <para>
/// 维护约束：必选缺失不会出现在报告里（装配期 fail-fast，报告只承载成功形态与可选缺失）；
/// 可选缺失必须携带缺席原因与缺失功能文案，运维不依赖日志也能查到少了什么。
/// </para>
/// </summary>
public sealed class CapabilityDependencyReport
{
    /// <summary>
    /// 初始化 <see cref="CapabilityDependencyReport"/>。
    /// </summary>
    /// <param name="capabilityId">依赖的能力 Id。</param>
    /// <param name="kind">依赖种类（必选 / 可选）。</param>
    /// <param name="state">装配状态。</param>
    /// <param name="absenceReason">缺席原因（已装配时为 None）。</param>
    public CapabilityDependencyReport(string capabilityId, CapabilityDependencyKind kind, CapabilityDependencyAssemblyState state, CapabilityDependencyAbsenceReason absenceReason)
    {
        CapabilityId = capabilityId;
        Kind = kind;
        State = state;
        AbsenceReason = absenceReason;
    }

    /// <summary>
    /// 获取依赖的能力 Id。
    /// </summary>
    public string CapabilityId { get; }

    /// <summary>
    /// 获取依赖种类。
    /// </summary>
    public CapabilityDependencyKind Kind { get; }

    /// <summary>
    /// 获取装配状态。
    /// </summary>
    public CapabilityDependencyAssemblyState State { get; }

    /// <summary>
    /// 获取缺席原因（已装配时为 None；区分白名单未收录 / 排除表命中 / 部署目录未发现）。
    /// </summary>
    public CapabilityDependencyAbsenceReason AbsenceReason { get; }
}
