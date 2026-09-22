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
/// 启动期配置冲突异常（C143f D4：fail fast，携带全部冲突字段与来源段）。
/// </summary>
/// <remarks>
/// Thrown at process startup when the configuration layers conflict (C143f D4: fail fast).
/// Carries every detected <see cref="ConfigFieldConflict"/> so the error message lists
/// the conflicting fields together with the source section each side came from.
/// </remarks>
public sealed class ConfigConflictException : Exception
{
    /// <summary>
    /// 初始化 <see cref="ConfigConflictException"/> 类的新实例。
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="ConfigConflictException"/> class.
    /// </remarks>
    /// <param name="conflicts">全部冲突项 / Every detected conflict</param>
    /// <exception cref="ArgumentException">当 <paramref name="conflicts"/> 为空集合时抛出 / Thrown when <paramref name="conflicts"/> is empty</exception>
    public ConfigConflictException(IReadOnlyList<ConfigFieldConflict> conflicts)
        : base(BuildMessage(conflicts))
    {
        Conflicts = conflicts;
    }

    /// <summary>
    /// 获取全部冲突项（冲突字段 + 来源段 + 值）。
    /// </summary>
    /// <remarks>
    /// Gets every detected conflict (conflicting field + source section + value).
    /// </remarks>
    public IReadOnlyList<ConfigFieldConflict> Conflicts { get; }

    /// <summary>
    /// 构建异常消息：逐行列出冲突字段与来源段。
    /// </summary>
    /// <remarks>
    /// Builds the exception message: one line per conflicting field and its sources.
    /// </remarks>
    /// <param name="conflicts">全部冲突项 / Every detected conflict</param>
    /// <returns>异常消息 / The exception message</returns>
    /// <exception cref="ArgumentException">当 <paramref name="conflicts"/> 为空集合时抛出 / Thrown when <paramref name="conflicts"/> is empty</exception>
    private static string BuildMessage(IReadOnlyList<ConfigFieldConflict> conflicts)
    {
        if (conflicts == null || conflicts.Count == 0)
        {
            throw new ArgumentException("At least one conflict is required.", nameof(conflicts));
        }

        var lines = new List<string>(conflicts.Count);
        foreach (var conflict in conflicts)
        {
            lines.Add(conflict.ToString());
        }

        return $"Startup configuration conflicts detected (C143f D4, fail fast): {string.Join("; ", lines)}";
    }
}
