// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.StartUp.Configuration;

/// <summary>
/// 启动期配置冲突描述：冲突类型 + 字段名 + 双方来源与值（C143f D4）。
/// </summary>
/// <remarks>
/// Describes one startup configuration conflict (C143f D4): the conflict kind, the field name,
/// and both sources with their values, so the fail-fast error can name the conflicting field
/// and where each side of the conflict came from.
/// </remarks>
public sealed class ConfigFieldConflict
{
    /// <summary>
    /// 初始化 <see cref="ConfigFieldConflict"/> 类的新实例。
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="ConfigFieldConflict"/> class.
    /// </remarks>
    /// <param name="kind">冲突类型 / The conflict kind</param>
    /// <param name="fieldName">冲突字段名 / The conflicting field name</param>
    /// <param name="firstSource">第一方来源描述（如 "file[Game]" 或 "CLI"）/ The first source description (e.g. "file[Game]" or "CLI")</param>
    /// <param name="firstValue">第一方值 / The first source value</param>
    /// <param name="secondSource">第二方来源描述 / The second source description</param>
    /// <param name="secondValue">第二方值 / The second source value</param>
    public ConfigFieldConflict(ConfigConflictKind kind, string fieldName, string firstSource, object firstValue, string secondSource, object secondValue)
    {
        Kind = kind;
        FieldName = fieldName;
        FirstSource = firstSource;
        FirstValue = firstValue;
        SecondSource = secondSource;
        SecondValue = secondValue;
    }

    /// <summary>
    /// 获取冲突类型。
    /// </summary>
    /// <remarks>
    /// Gets the conflict kind.
    /// </remarks>
    public ConfigConflictKind Kind { get; }

    /// <summary>
    /// 获取冲突字段名。
    /// </summary>
    /// <remarks>
    /// Gets the conflicting field name.
    /// </remarks>
    public string FieldName { get; }

    /// <summary>
    /// 获取第一方来源描述（如 "file[Game]"、"CLI"）。
    /// </summary>
    /// <remarks>
    /// Gets the first source description (e.g. "file[Game]", "CLI").
    /// </remarks>
    public string FirstSource { get; }

    /// <summary>
    /// 获取第一方值。
    /// </summary>
    /// <remarks>
    /// Gets the first source value.
    /// </remarks>
    public object FirstValue { get; }

    /// <summary>
    /// 获取第二方来源描述。
    /// </summary>
    /// <remarks>
    /// Gets the second source description.
    /// </remarks>
    public string SecondSource { get; }

    /// <summary>
    /// 获取第二方值。
    /// </summary>
    /// <remarks>
    /// Gets the second source value.
    /// </remarks>
    public object SecondValue { get; }

    /// <summary>
    /// 渲染为可读的冲突描述行。
    /// </summary>
    /// <remarks>
    /// Renders a readable one-line description of the conflict.
    /// </remarks>
    /// <returns>冲突描述行 / The one-line conflict description</returns>
    public override string ToString()
    {
        return $"{Kind} {FieldName}: {FirstSource}={RenderValue(FirstValue)} vs {SecondSource}={RenderValue(SecondValue)}";
    }

    /// <summary>
    /// 渲染值文本；null 渲染为 "(unset)"。
    /// </summary>
    /// <remarks>
    /// Renders the value text; null renders as "(unset)".
    /// </remarks>
    /// <param name="value">要渲染的值 / The value to render</param>
    /// <returns>值文本 / The value text</returns>
    private static string RenderValue(object value)
    {
        return value == null ? "(unset)" : value.ToString();
    }
}
