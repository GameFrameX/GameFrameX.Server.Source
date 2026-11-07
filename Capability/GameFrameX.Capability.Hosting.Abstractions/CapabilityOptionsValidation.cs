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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// Options 校验问题收集器（<see cref="ICapabilityOptions.Validate"/> 的写出面）。
/// <para>
/// 维护约束：问题必须附字段路径（如 <c>Friend.FriendLimit</c>）；任一问题 = 装配期 fail-fast，
/// 机制把全部问题一次报全（改一次配置即可看到全部问题）。
/// </para>
/// </summary>
public sealed class CapabilityOptionsValidation
{
    /// <summary>
    /// 问题清单（字段路径 + 描述）。
    /// </summary>
    private readonly List<string> _problems = new();

    /// <summary>
    /// 初始化 <see cref="CapabilityOptionsValidation"/>。
    /// </summary>
    public CapabilityOptionsValidation()
    {
    }

    /// <summary>
    /// 获取是否通过校验（无任何问题）。
    /// </summary>
    public bool IsValid
    {
        get { return _problems.Count == 0; }
    }

    /// <summary>
    /// 获取全部问题（字段路径 + 描述）。
    /// </summary>
    public IReadOnlyList<string> Problems
    {
        get { return _problems; }
    }

    /// <summary>
    /// 记录一个问题。
    /// </summary>
    /// <param name="fieldPath">字段路径（相对 Options 根类型，如 <c>Friend.FriendLimit</c>）。</param>
    /// <param name="message">问题描述。</param>
    public void AddError(string fieldPath, string message)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new ArgumentException("Field path must be a non-empty string.", nameof(fieldPath));
        }

        _problems.Add(fieldPath + ": " + (message ?? string.Empty));
    }

    /// <summary>
    /// 要求字段为正数（非正即记录问题）。
    /// </summary>
    /// <param name="fieldPath">字段路径。</param>
    /// <param name="value">字段值。</param>
    public void RequirePositive(string fieldPath, int value)
    {
        if (value <= 0)
        {
            AddError(fieldPath, "must be positive (actual: " + value + ")");
        }
    }

    /// <summary>
    /// 要求字段为正数（非正即记录问题）。
    /// </summary>
    /// <param name="fieldPath">字段路径。</param>
    /// <param name="value">字段值。</param>
    public void RequirePositive(string fieldPath, long value)
    {
        if (value <= 0)
        {
            AddError(fieldPath, "must be positive (actual: " + value + ")");
        }
    }

    /// <summary>
    /// 要求字符串字段非空（null 或空白即记录问题）。
    /// </summary>
    /// <param name="fieldPath">字段路径。</param>
    /// <param name="value">字段值。</param>
    public void RequireNonEmpty(string fieldPath, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(fieldPath, "must be a non-empty string");
        }
    }
}
