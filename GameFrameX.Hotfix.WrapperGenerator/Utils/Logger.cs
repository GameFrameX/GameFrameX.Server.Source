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