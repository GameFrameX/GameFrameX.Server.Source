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
/// Options 字段生效值条目（<c>list_capability_settings</c> 元端点与装配报告的数据面）。
/// <para>
/// 维护约束：来源只有三层——默认（包内字段初始值）/ 配置段（<c>Settings</c> 按字段覆盖）/ 代码
///（<c>Configure</c> 回调改写，由机制比较回调前后值得出）；生效值为合并校验后的最终值。
/// </para>
/// </summary>
public sealed class CapabilitySettingsField
{
    /// <summary>
    /// 初始化 <see cref="CapabilitySettingsField"/>。
    /// </summary>
    /// <param name="fieldPath">字段路径（相对 Options 根类型，如 <c>Friend.FriendLimit</c>）。</param>
    /// <param name="effectiveValue">生效值。</param>
    /// <param name="source">来源（默认 / 配置段 / 代码）。</param>
    public CapabilitySettingsField(string fieldPath, object effectiveValue, CapabilitySettingsFieldSource source)
    {
        FieldPath = fieldPath;
        EffectiveValue = effectiveValue;
        Source = source;
    }

    /// <summary>
    /// 获取字段路径（相对 Options 根类型）。
    /// </summary>
    public string FieldPath { get; }

    /// <summary>
    /// 获取生效值（合并校验后的最终值）。
    /// </summary>
    public object EffectiveValue { get; }

    /// <summary>
    /// 获取来源。
    /// </summary>
    public CapabilitySettingsFieldSource Source { get; }
}
