// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository:     https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Docs:      https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Abstractions;

namespace GameFrameX.Tests.Online;

/// <summary>
/// Options 绑定测试根类型（机制层测试槽位声明为 OptionsType：字段初始值即「包内默认」层，
/// 用例经族配置段 <c>Settings.&lt;能力Id&gt;</c> 与 <c>Configure&lt;T&gt;</c> 逐层覆盖，验证合并优先级、字段来源与 fail-fast）。
/// </summary>
public sealed class CapabilityBindingTestOptions : ICapabilityOptions
{
    /// <summary>获取或设置字段 Alpha（默认层验证位；默认 1，必须为正数）。</summary>
    public int Alpha { get; set; } = 1;

    /// <summary>获取或设置字段 Beta（配置段层验证位；默认 2，必须为正数）。</summary>
    public int Beta { get; set; } = 2;

    /// <summary>获取或设置字段 Gamma（代码层验证位；默认 3，必须为正数）。</summary>
    public int Gamma { get; set; } = 3;

    /// <summary>获取或设置嵌套参数段（嵌套字段路径验证位）。</summary>
    public NestedSection Nested { get; set; } = new NestedSection();

    /// <summary>
    /// 校验 Alpha、Beta、Gamma 与 Nested.Depth 均为正数；嵌套段为 null 时按 0 计入，从而报告校验失败。
    /// </summary>
    /// <remarks>
    /// Validates that Alpha, Beta, Gamma and Nested.Depth are all positive; a null nested section counts as 0 and is therefore reported as a validation failure.
    /// </remarks>
    /// <param name="validation">收集校验失败的能力选项校验器 / Capability options validation collector that records failures</param>
    public void Validate(CapabilityOptionsValidation validation)
    {
        validation.RequirePositive("Alpha", Alpha);
        validation.RequirePositive("Beta", Beta);
        validation.RequirePositive("Gamma", Gamma);
        validation.RequirePositive("Nested.Depth", Nested == null ? 0 : Nested.Depth);
    }

    /// <summary>
    /// 嵌套参数段（模拟能力 Options 的子对象，如 Social 的 Friend 段）。
    /// </summary>
    public sealed class NestedSection
    {
        /// <summary>获取或设置嵌套深度（默认 4，必须为正数）。</summary>
        public int Depth { get; set; } = 4;
    }
}
