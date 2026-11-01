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

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace GameFrameX.Hotfix.WrapperGenerator.Agent;

/// <summary>
/// 单个代理类声明的生成结果：hintName、生成源码文本与缓冲的生成期诊断。
/// 作为增量管线按值缓存的比较单元——声明未变化时整个对象命中缓存，输出阶段不重跑。
/// </summary>
/// <remarks>
/// Generation result for a single agent class declaration: hintName, generated source text, and
/// buffered generation-time diagnostics. It is the value-cached unit of the incremental pipeline:
/// unchanged declarations hit the cache as a whole and their output stage does not re-run.
/// </remarks>
internal sealed class WrapperResult
{
    /// <summary>
    /// 初始化生成结果。
    /// </summary>
    /// <remarks>
    /// Initializes the generation result.
    /// </remarks>
    /// <param name="hintName">生成文件名（hintName）/ Generated file name (hintName)</param>
    /// <param name="source">生成的源码文本；无可包装方法时为 null（不产出文件）/ Generated source text; null when there is no wrappable method (no file emitted)</param>
    /// <param name="diagnostics">生成期诊断（可能为空数组）/ Generation-time diagnostics (possibly an empty array)</param>
    public WrapperResult(string hintName, string source, Diagnostic[] diagnostics)
    {
        HintName = hintName;
        Source = source;
        Diagnostics = diagnostics;
    }

    /// <summary>
    /// 获取生成文件名（hintName）。
    /// </summary>
    /// <remarks>
    /// Gets the generated file name (hintName).
    /// </remarks>
    public string HintName { get; }

    /// <summary>
    /// 获取生成的源码文本；无可包装方法的代理为 null（仅上报诊断，不产出文件）。
    /// </summary>
    /// <remarks>
    /// Gets the generated source text; null for agents with no wrappable method (diagnostics only, no file emitted).
    /// </remarks>
    public string Source { get; }

    /// <summary>
    /// 获取该声明收集到的生成期诊断（无诊断时为空数组，绝不为 null）。
    /// </summary>
    /// <remarks>
    /// Gets the generation-time diagnostics collected for this declaration (an empty array when there are none, never null).
    /// </remarks>
    public Diagnostic[] Diagnostics { get; }
}

/// <summary>
/// 按值比较 <see cref="WrapperResult"/>：hintName、源码文本与诊断序列均等值才视为相等，
/// 供增量管线 <c>WithComparer</c> 缓存判定（文本未变的声明不触发下游输出重跑）。
/// </summary>
/// <remarks>
/// Value comparer for <see cref="WrapperResult"/>: two results are equal only when their hintNames,
/// source texts, and diagnostic sequences are all equal. Used by the incremental pipeline's
/// <c>WithComparer</c> cache so declarations whose text did not change do not re-run downstream outputs.
/// </remarks>
internal sealed class WrapperResultComparer : IEqualityComparer<WrapperResult>
{
    /// <summary>
    /// 共享单例（比较器无状态）。
    /// </summary>
    /// <remarks>
    /// Shared singleton (the comparer is stateless).
    /// </remarks>
    public static readonly WrapperResultComparer Instance = new WrapperResultComparer();

    /// <summary>
    /// 判定两个生成结果是否等值：hintName、源码文本逐字符相等，且诊断逐条 <see cref="Diagnostic.ToString"/> 等值。
    /// </summary>
    /// <remarks>
    /// Determines whether two results are equal: hintName and source text compare ordinally, and diagnostics
    /// compare equal one by one via <see cref="Diagnostic.ToString"/>.
    /// </remarks>
    /// <param name="left">左侧结果 / Left result</param>
    /// <param name="right">右侧结果 / Right result</param>
    /// <returns>等值则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if equal; otherwise <c>false</c></returns>
    public bool Equals(WrapperResult left, WrapperResult right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        if (!string.Equals(left.HintName, right.HintName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(left.Source, right.Source, StringComparison.Ordinal))
        {
            return false;
        }

        if (left.Diagnostics.Length != right.Diagnostics.Length)
        {
            return false;
        }

        // 诊断按序列化文本等值（含 ID、位置与消息），保证诊断变化也能使缓存失效
        for (var index = 0; index < left.Diagnostics.Length; index++)
        {
            if (!string.Equals(left.Diagnostics[index].ToString(), right.Diagnostics[index].ToString(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 取结果哈希码：以 hintName 为基（等值结果必同 hintName；不同 hintName 仅降低桶分布，不影响正确性）。
    /// </summary>
    /// <remarks>
    /// Gets the hash code: based on the hintName (equal results always share the hintName; different
    /// hintNames merely reduce bucket distribution and never affect correctness).
    /// </remarks>
    /// <param name="value">待哈希的结果 / The result to hash</param>
    /// <returns>哈希码 / The hash code</returns>
    public int GetHashCode(WrapperResult value)
    {
        return StringComparer.Ordinal.GetHashCode(value.HintName ?? string.Empty);
    }
}
