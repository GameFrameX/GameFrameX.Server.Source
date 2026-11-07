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
//  ==========================================================================================


using System.Text;

namespace GameFrameX.Capability.Hosting.Core;

/// <summary>
/// 能力装配汇总异常（依赖检查不通过时携带全部问题条目一次抛出；运维改一次配置即可看到全部问题）。
/// <para>
/// 维护约束：抛出前机制已把每条问题逐条打 Error；<see cref="Problems"/> 与日志逐条对应
///（必选缺失含缺席原因、环路含完整路径）。
/// </para>
/// </summary>
public sealed class CapabilityAssemblyException : Exception
{
    /// <summary>
    /// 初始化 <see cref="CapabilityAssemblyException"/>。
    /// </summary>
    /// <param name="problems">全部问题条目（已本地化；至少一条）。</param>
    public CapabilityAssemblyException(IReadOnlyList<string> problems)
        : base(BuildMessage(problems))
    {
        Problems = problems;
    }

    /// <summary>
    /// 初始化 <see cref="CapabilityAssemblyException"/>（保留触发问题的原始异常，供诊断堆栈与底层异常对象）。
    /// </summary>
    /// <param name="problems">全部问题条目（已本地化；至少一条）。</param>
    /// <param name="innerException">触发问题的原始异常。</param>
    public CapabilityAssemblyException(IReadOnlyList<string> problems, Exception innerException)
        : base(BuildMessage(problems), innerException)
    {
        Problems = problems;
    }

    /// <summary>
    /// 获取全部问题条目（已本地化）。
    /// </summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>
    /// 构造异常消息（逐条换行）。
    /// </summary>
    /// <param name="problems">问题条目。</param>
    /// <returns>汇总消息。</returns>
    private static string BuildMessage(IReadOnlyList<string> problems)
    {
        var message = new StringBuilder();
        for (var i = 0; i < problems.Count; i++)
        {
            if (i > 0)
            {
                message.AppendLine();
            }

            message.Append(problems[i]);
        }

        return message.ToString();
    }
}
