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

using System.Text;

namespace GameFrameX.Hotfix.WrapperGenerator.Agent;

/// <summary>
/// 代理包装器的代码模板生成器，根据代理信息生成线程安全调用的分部类代码。
/// </summary>
/// <remarks>
/// Code template generator for agent wrappers, generating partial class code with thread-safe invocations based on agent information.
/// </remarks>
public static class AgentTemplate
{
    /// <summary>
    /// 根据代理信息生成包装器分部类的源代码。
    /// </summary>
    /// <remarks>
    /// Generates the wrapper partial class source code based on agent information.
    /// </remarks>
    /// <param name="info">代理信息，包含类名、命名空间、方法列表等元数据 / Agent information containing class name, namespace, method list, and other metadata</param>
    /// <returns>生成的 C# 源代码字符串 / Generated C# source code string</returns>
    public static string Run(AgentInfo info)
    {
        // C432：局部 StringBuilder 逐级传参——模板无任何跨调用静态状态（并发驱动运行互不共享，
        // 产出各自完整一致）；替代历史 [ThreadStatic] 静态字段形态。
        var templateStringBuilder = new StringBuilder();

        foreach (var value in info.UsingSpaces)
        {
            templateStringBuilder.AppendLine("using " + value + ";");
        }

        templateStringBuilder.AppendLine();

        templateStringBuilder.AppendLine("namespace " + info.Space);
        templateStringBuilder.AppendLine("{");
        templateStringBuilder.AppendLine("\tpublic partial class " + info.Name + " : " + info.Super);
        templateStringBuilder.AppendLine("\t{");
        foreach (var infoMethod in info.Methods)
        {
            AppendMethod(templateStringBuilder, infoMethod);
        }

        templateStringBuilder.AppendLine("\t}");
        templateStringBuilder.AppendLine("}");

        return templateStringBuilder.ToString();
    }

    /// <summary>
    /// 拼接单个方法的方法声明与方法体。
    /// </summary>
    /// <remarks>
    /// Appends the method declaration and body for a single method.
    /// </remarks>
    /// <param name="templateStringBuilder">生成代码的字符串构建器 / String builder for the generated code</param>
    /// <param name="infoMethod">方法元数据 / Method metadata</param>
    private static void AppendMethod(StringBuilder templateStringBuilder, MethodInfoData infoMethod)
    {
        templateStringBuilder.AppendLine("\t\t" + infoMethod.Declare);
        templateStringBuilder.AppendLine("\t\t{");

        if (infoMethod.Discard)
        {
            AppendDiscardBody(templateStringBuilder, infoMethod);
        }
        else
        {
            AppendInvocationBody(templateStringBuilder, infoMethod);
        }

        templateStringBuilder.AppendLine("\t\t}");
        templateStringBuilder.AppendLine();
    }

    /// <summary>
    /// 拼接丢弃返回值的方法体（线程安全直调或入队丢弃，末尾返回已完成任务）。
    /// </summary>
    /// <remarks>
    /// Appends the method body for discard (fire-and-forget) invocations.
    /// </remarks>
    /// <param name="templateStringBuilder">生成代码的字符串构建器 / String builder for the generated code</param>
    /// <param name="infoMethod">方法元数据 / Method metadata</param>
    private static void AppendDiscardBody(StringBuilder templateStringBuilder, MethodInfoData infoMethod)
    {
        if (infoMethod.IsThreadSafe)
        {
            templateStringBuilder.AppendLine($"\t\t\t_ = base.{infoMethod.Name}{infoMethod.Typeparams}({infoMethod.ParamString});");
        }
        else
        {
            templateStringBuilder.AppendLine("\t\t\tlong callChainId = GameFrameX.Core.Actors.Impl.WorkerActor.NextChainId();");
            templateStringBuilder.AppendLine($"\t\t\t_ = base.Actor.WorkerActor.EnqueueAsync(()=>base.{infoMethod.Name}{infoMethod.Typeparams}({infoMethod.ParamString}), callChainId, true{BuildTimeoutArgument(infoMethod)});");
        }

        templateStringBuilder.AppendLine($"\t\t\treturn {infoMethod.ReturnType}.CompletedTask;");
    }

    /// <summary>
    /// 拼接需要等待结果的方法体（按是否需要入队分流，直调或入队等待）。
    /// </summary>
    /// <remarks>
    /// Appends the method body for invocations that await a result.
    /// </remarks>
    /// <param name="templateStringBuilder">生成代码的字符串构建器 / String builder for the generated code</param>
    /// <param name="infoMethod">方法元数据 / Method metadata</param>
    private static void AppendInvocationBody(StringBuilder templateStringBuilder, MethodInfoData infoMethod)
    {
        var needAwait = infoMethod.IsAsync && infoMethod.ReturnType.Contains("<");
        templateStringBuilder.AppendLine("\t\t\t(bool needEnqueue, long chainId)= base.Actor.WorkerActor.IsNeedEnqueue();");
        templateStringBuilder.AppendLine("\t\t\tif (!needEnqueue)");
        templateStringBuilder.AppendLine("\t\t\t{");
        templateStringBuilder.AppendLine($"\t\t\t\treturn {(needAwait ? "await " : string.Empty)} base.{infoMethod.Name}{infoMethod.Typeparams}({infoMethod.ParamString});");
        templateStringBuilder.AppendLine("\t\t\t}");
        templateStringBuilder.AppendLine($"\t\t\treturn {(needAwait ? "await " : string.Empty)} base.Actor.WorkerActor.EnqueueAsync(()=>base.{infoMethod.Name}{infoMethod.Typeparams}({infoMethod.ParamString}), chainId, {(infoMethod.Discard ? "true" : "false")}{BuildTimeoutArgument(infoMethod)});");
    }

    /// <summary>
    /// 构建超时实参片段：显式配置 [TimeOut] 时发射「, 超时值」，未配置时省略实参走 EnqueueAsync 框架默认值。
    /// </summary>
    /// <remarks>
    /// Builds the timeout argument fragment: emits ", &lt;ms&gt;" only when [TimeOut] was explicitly configured;
    /// otherwise the argument is omitted so EnqueueAsync's framework default applies.
    /// </remarks>
    /// <param name="infoMethod">方法元数据 / Method metadata</param>
    /// <returns>超时实参片段（含前导逗号）或空串 / The timeout argument fragment (with leading comma) or an empty string</returns>
    private static string BuildTimeoutArgument(MethodInfoData infoMethod)
    {
        return infoMethod.HasTimeout ? ", " + infoMethod.TimeOut : string.Empty;
    }
}