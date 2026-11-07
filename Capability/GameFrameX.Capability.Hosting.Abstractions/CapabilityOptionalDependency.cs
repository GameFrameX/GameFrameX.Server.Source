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
/// 可选依赖声明（manifest <see cref="CapabilityManifest.Optional"/> 条目：依赖能力 Id + 缺失时少了什么功能）。
/// <para>
/// 维护约束：可选依赖缺失是正常装配形态——模块必须完整装配并正常运行，只是对应功能不装配；
/// 做不到这一点的依赖就不是可选，应声明为必选。<see cref="AbsentFeatureLocalizationKey"/> 只描述缺失的功能
/// （如「对局结算发奖」），缺失原因与「不影响其余功能运行」由机制统一追加，各包不得自行措辞。
/// </para>
/// </summary>
public sealed class CapabilityOptionalDependency
{
    /// <summary>
    /// 初始化 <see cref="CapabilityOptionalDependency"/>。
    /// </summary>
    /// <param name="capabilityId">依赖的能力 Id（非空）。</param>
    /// <param name="absentFeatureLocalizationKey">缺失功能的本地化文案键（非空）。</param>
    public CapabilityOptionalDependency(string capabilityId, string absentFeatureLocalizationKey)
    {
        if (string.IsNullOrWhiteSpace(capabilityId))
        {
            throw new ArgumentException("Capability id must be a non-empty string.", nameof(capabilityId));
        }

        if (string.IsNullOrWhiteSpace(absentFeatureLocalizationKey))
        {
            throw new ArgumentException("Absent feature localization key must be a non-empty string.", nameof(absentFeatureLocalizationKey));
        }

        CapabilityId = capabilityId;
        AbsentFeatureLocalizationKey = absentFeatureLocalizationKey;
    }

    /// <summary>
    /// 获取依赖的能力 Id。
    /// </summary>
    public string CapabilityId { get; }

    /// <summary>
    /// 获取缺失功能的本地化文案键（只写功能，不写原因与「不影响」，二者由机制追加）。
    /// </summary>
    public string AbsentFeatureLocalizationKey { get; }
}
