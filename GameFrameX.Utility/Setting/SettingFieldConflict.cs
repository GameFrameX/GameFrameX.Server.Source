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
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Utility.Setting;

/// <summary>
/// 单个进程级设置字段的冲突描述（C143a D19）。
/// </summary>
/// <remarks>
/// Describes the conflict of a single process-level setting field between two configuration sources (C143a D19).
/// </remarks>
public sealed class SettingFieldConflict
{
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <remarks>
    /// Constructor that specifies the field name, both segments and both values.
    /// </remarks>
    /// <param name="fieldName">冲突字段名 / The conflicting field name</param>
    /// <param name="expectedSegment">期望值来源段（当前生效配置的 ServerType）/ Source segment of the expected value (ServerType of the current setting)</param>
    /// <param name="expectedValue">期望值（当前生效配置的字段值）/ Expected value (field value of the current setting)</param>
    /// <param name="actualSegment">实际值来源段（新设置配置的 ServerType）/ Source segment of the actual value (ServerType of the incoming setting)</param>
    /// <param name="actualValue">实际值（新设置配置的字段值）/ Actual value (field value of the incoming setting)</param>
    public SettingFieldConflict(string fieldName, string expectedSegment, object expectedValue, string actualSegment, object actualValue)
    {
        FieldName = fieldName;
        ExpectedSegment = expectedSegment;
        ExpectedValue = expectedValue;
        ActualSegment = actualSegment;
        ActualValue = actualValue;
    }

    /// <summary>
    /// 冲突字段名
    /// </summary>
    /// <remarks>
    /// The conflicting field name.
    /// </remarks>
    /// <value>冲突字段名 / The conflicting field name</value>
    public string FieldName { get; }

    /// <summary>
    /// 期望值来源段
    /// </summary>
    /// <remarks>
    /// Source segment (ServerType) of the expected value.
    /// </remarks>
    /// <value>来源段 / Source segment</value>
    public string ExpectedSegment { get; }

    /// <summary>
    /// 期望值
    /// </summary>
    /// <remarks>
    /// Expected field value from the current setting.
    /// </remarks>
    /// <value>期望值 / Expected value</value>
    public object ExpectedValue { get; }

    /// <summary>
    /// 实际值来源段
    /// </summary>
    /// <remarks>
    /// Source segment (ServerType) of the actual value.
    /// </remarks>
    /// <value>来源段 / Source segment</value>
    public string ActualSegment { get; }

    /// <summary>
    /// 实际值
    /// </summary>
    /// <remarks>
    /// Actual field value from the incoming setting.
    /// </remarks>
    /// <value>实际值 / Actual value</value>
    public object ActualValue { get; }

    /// <summary>
    /// 判断字段是否为敏感字段（其值不得出现在异常消息中）。
    /// </summary>
    /// <remarks>
    /// Determines whether the field is sensitive (its value must not appear in exception messages).
    /// Matches credential-like names such as <see cref="AppSetting.DataBasePassword"/>; value details are preserved on the conflict object itself.
    /// </remarks>
    private static bool IsSensitiveFieldName(string fieldName)
    {
        return fieldName != null && fieldName.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 返回冲突描述文本："段 X 字段 Y 期望值 A 实际值 B"；敏感字段（如密码）只输出字段名与来源段，不输出值。
    /// </summary>
    /// <remarks>
    /// Returns the conflict description text; sensitive fields (e.g. passwords) output only the field name and source segments, never the values.
    /// </remarks>
    /// <returns>冲突描述文本 / Conflict description text</returns>
    public override string ToString()
    {
        if (IsSensitiveFieldName(FieldName))
        {
            return $"segment '{ActualSegment ?? "null"}' field '{FieldName}' has a conflicting value (from segment '{ExpectedSegment ?? "null"}'); values are omitted for a sensitive field";
        }

        return $"segment '{ActualSegment}' field '{FieldName}' expected value '{ExpectedValue ?? "null"}' (from segment '{ExpectedSegment ?? "null"}') but actual value '{ActualValue ?? "null"}'";
    }
}
