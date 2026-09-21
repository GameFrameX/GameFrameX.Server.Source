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
/// 同进程多 Role 下进程级设置字段冲突异常（C143a D19）。
/// </summary>
/// <remarks>
/// Thrown by <see cref="GlobalSettings.SetCurrentSetting"/> when a process-level field
/// of the incoming setting differs from the current setting (C143a D19).
/// The message lists every conflicting field with its source segments and both values;
/// sensitive fields (e.g. <see cref="AppSetting.DataBasePassword"/>) omit their values from the message.
public sealed class SettingConflictException : Exception
{
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <remarks>
    /// Constructor that builds the message from the conflict list.
    /// </remarks>
    /// <param name="conflicts">进程级字段冲突列表 / The list of process-level field conflicts</param>
    public SettingConflictException(IReadOnlyList<SettingFieldConflict> conflicts)
        : base(BuildMessage(conflicts))
    {
        Conflicts = conflicts;
    }

    /// <summary>
    /// 进程级字段冲突列表
    /// </summary>
    /// <remarks>
    /// The list of process-level field conflicts.
    /// </remarks>
    /// <value>冲突列表（只读）/ The read-only conflict list</value>
    public IReadOnlyList<SettingFieldConflict> Conflicts { get; }

    /// <summary>
    /// 构建包含全部冲突字段与来源段的异常消息。
    /// </summary>
    /// <remarks>
    /// Builds the exception message containing every conflicting field and its source segments.
    /// </remarks>
    /// <param name="conflicts">冲突列表 / The conflict list</param>
    /// <returns>异常消息 / The exception message</returns>
    private static string BuildMessage(IReadOnlyList<SettingFieldConflict> conflicts)
    {
        var count = conflicts?.Count ?? 0;
        var detail = count > 0 ? string.Join("; ", conflicts) : string.Empty;
        return $"Process-level setting conflict detected ({count} field(s)): {detail}";
    }
}
