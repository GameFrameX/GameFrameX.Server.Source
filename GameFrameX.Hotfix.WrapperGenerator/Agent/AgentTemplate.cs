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
    /// 用于拼接生成代码的字符串构建器。
    /// </summary>
    /// <remarks>
    /// String builder for assembling generated code.
    /// </remarks>
    private static readonly StringBuilder TemplateStringBuilder = new();

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
        TemplateStringBuilder.Clear();

        foreach (var value in info.UsingSpaces)
        {
            TemplateStringBuilder.AppendLine("using " + value + ";");
        }

        TemplateStringBuilder.AppendLine();

        TemplateStringBuilder.AppendLine("namespace " + info.Space);
        TemplateStringBuilder.AppendLine("{");
        TemplateStringBuilder.AppendLine("\tpublic partial class " + info.Name + " : " + info.Super);
        TemplateStringBuilder.AppendLine("\t{");
        foreach (var infoMethod in info.Methods)
        {
            TemplateStringBuilder.AppendLine("\t\t" + infoMethod.Declare);
            TemplateStringBuilder.AppendLine("\t\t{");

            if (infoMethod.Discard)
            {
                if (infoMethod.IsThreadSafe)
                {
                    TemplateStringBuilder.AppendLine($"\t\t\t_ = base.{infoMethod.Name}{infoMethod.Typeparams}({infoMethod.ParamString});");
                }
                else
                {
                    TemplateStringBuilder.AppendLine("\t\t\tlong callChainId = GameFrameX.Core.Actors.Impl.WorkerActor.NextChainId();");
                    TemplateStringBuilder.AppendLine($"\t\t\t_ = base.Actor.WorkerActor.Enqueue(()=>base.{infoMethod.Name}{infoMethod.Typeparams}({infoMethod.ParamString}), callChainId, true, {infoMethod.TimeOut});");
                }

                TemplateStringBuilder.AppendLine($"\t\t\treturn {infoMethod.ReturnType}.CompletedTask;");
            }
            else
            {
                TemplateStringBuilder.AppendLine("\t\t\t(bool needEnqueue, long chainId)= base.Actor.WorkerActor.IsNeedEnqueue();");
                TemplateStringBuilder.AppendLine("\t\t\tif (!needEnqueue)");
                TemplateStringBuilder.AppendLine("\t\t\t{");
                TemplateStringBuilder.AppendLine($"\t\t\t\treturn {(infoMethod.IsAsync ? "await " : string.Empty)} base.{infoMethod.Name}{infoMethod.Typeparams}({infoMethod.ParamString});");
                TemplateStringBuilder.AppendLine("\t\t\t}");
                TemplateStringBuilder.AppendLine($"\t\t\treturn {(infoMethod.IsAsync ? "await " : string.Empty)} base.Actor.WorkerActor.Enqueue(()=>base.{infoMethod.Name}{infoMethod.Typeparams}({infoMethod.ParamString}), chainId, {(infoMethod.Discard ? "true" : "false")}, {infoMethod.TimeOut});");
            }

            TemplateStringBuilder.AppendLine("\t\t}");
            TemplateStringBuilder.AppendLine();
        }

        TemplateStringBuilder.AppendLine("\t}");
        TemplateStringBuilder.AppendLine("}");

        return TemplateStringBuilder.ToString();
    }
}