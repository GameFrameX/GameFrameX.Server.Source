// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
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
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using Microsoft.CodeAnalysis;

namespace GameFrameX.Hotfix.WrapperGenerator.Utils;

/// <summary>
/// 提供源代码生成器的诊断工厂：以稳定 ID 创建携带源位置的错误/警告诊断。
/// </summary>
/// <remarks>
/// Provides the diagnostic factory for source generators: creates error/warning diagnostics
/// with stable identifiers and source locations.
/// </remarks>
public static class Logger
{
    /// <summary>
    /// 契约违规（Error）诊断 ID。GFW 前缀与架构分析器 GFX0001-0021 错开，
    /// 可经 <c>dotnet_diagnostic_GFW0001.severity</c>（.editorconfig）按 ID 抑制或升级。
    /// </summary>
    /// <remarks>
    /// Diagnostic identifier for contract violations (Error). The GFW prefix avoids collisions
    /// with analyzer IDs GFX0001-0021, enabling per-ID suppression via
    /// <c>dotnet_diagnostic_GFW0001.severity</c> (.editorconfig).
    /// </remarks>
    public const string ErrorDiagnosticIdentifier = "GFW0001";

    /// <summary>
    /// 契约警示（Warning）诊断 ID。GFW 前缀与分析器 GFX 系列错开，可按 ID 配置严重级别。
    /// </summary>
    /// <remarks>
    /// Diagnostic identifier for contract warnings (Warning). The GFW prefix avoids collisions
    /// with the analyzer GFX series so the severity can be configured per ID.
    /// </remarks>
    public const string WarningDiagnosticIdentifier = "GFW0002";

    /// <summary>
    /// 诊断类别（与分析器 GFX 系列的类别命名约定一致，取生成器自身全名）。
    /// </summary>
    /// <remarks>
    /// Diagnostic category (the generator's own full name, following the GFX analyzer category convention).
    /// </remarks>
    public const string DiagnosticCategory = "GameFrameX.Hotfix.WrapperGenerator";

    /// <summary>
    /// 契约违规（Error）的静态描述符：全部 Error 诊断共用，避免每次调用分配新实例。
    /// </summary>
    /// <remarks>
    /// Static descriptor for contract violations (Error): shared by all Error diagnostics to avoid per-call allocations.
    /// </remarks>
    internal static readonly DiagnosticDescriptor ErrorDescriptor = new DiagnosticDescriptor(
        ErrorDiagnosticIdentifier,
        "Hotfix wrapper generator contract violation",
        "{0}",
        DiagnosticCategory,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// 契约警示（Warning）的静态描述符：全部 Warning 诊断共用，避免每次调用分配新实例。
    /// </summary>
    /// <remarks>
    /// Static descriptor for contract warnings (Warning): shared by all Warning diagnostics to avoid per-call allocations.
    /// </remarks>
    internal static readonly DiagnosticDescriptor WarningDescriptor = new DiagnosticDescriptor(
        WarningDiagnosticIdentifier,
        "Hotfix wrapper generator contract warning",
        "{0}",
        DiagnosticCategory,
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    /// 创建携带源位置的错误诊断。诊断在增量管线缓存阶段构造、随结果模型传递，输出阶段统一上报。
    /// </summary>
    /// <remarks>
    /// Creates an error diagnostic carrying a source location. Diagnostics are constructed in the
    /// incremental pipeline caching stage, travel with the result model, and are reported in the output stage.
    /// </remarks>
    /// <param name="message">错误消息 / Error message</param>
    /// <param name="location">违规成员的源位置（通常为方法声明位置）/ Source location of the violating member (usually the method declaration)</param>
    /// <returns>错误诊断 / The error diagnostic</returns>
    public static Diagnostic Error(string message, Location location)
    {
        return Diagnostic.Create(ErrorDescriptor, location ?? Location.None, message);
    }

    /// <summary>
    /// 创建携带源位置的警告诊断。诊断在增量管线缓存阶段构造、随结果模型传递，输出阶段统一上报。
    /// </summary>
    /// <remarks>
    /// Creates a warning diagnostic carrying a source location. Diagnostics are constructed in the
    /// incremental pipeline caching stage, travel with the result model, and are reported in the output stage.
    /// </remarks>
    /// <param name="message">警告消息 / Warning message</param>
    /// <param name="location">违规成员的源位置（通常为方法声明位置）/ Source location of the violating member (usually the method declaration)</param>
    /// <returns>警告诊断 / The warning diagnostic</returns>
    public static Diagnostic Warning(string message, Location location)
    {
        return Diagnostic.Create(WarningDescriptor, location ?? Location.None, message);
    }
}
