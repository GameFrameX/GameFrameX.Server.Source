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
/// 提供源代码生成器的日志记录扩展方法。
/// </summary>
/// <remarks>
/// Provides logging extension methods for source code generators.
/// </remarks>
public static class Logger
{
    /// <summary>
    /// 向源代码生成上下文报告错误诊断信息。
    /// </summary>
    /// <remarks>
    /// Reports an error diagnostic to the source production context.
    /// </remarks>
    /// <param name="context">源代码生成上下文 / Source production context</param>
    /// <param name="msg">错误消息 / Error message</param>
    public static void LogError(this SourceProductionContext context, string msg)
    {
        var invalidXmlWarning = new DiagnosticDescriptor("Error",
                                                         "Code Generator Error",
                                                         "{0}",
                                                         "CodeGenerator",
                                                         DiagnosticSeverity.Error,
                                                         true);
        context.ReportDiagnostic(Diagnostic.Create(invalidXmlWarning, Location.None, msg));
    }
}