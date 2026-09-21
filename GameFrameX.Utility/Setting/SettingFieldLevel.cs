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
/// 应用设置字段的进程拓扑级别（C143a D19 字段分区）。
/// </summary>
/// <remarks>
/// Process topology level of an application setting field (C143a D19 field partition).
/// <para><see cref="ProcessLevel"/>：进程级字段——同进程多 Role 重复设置时必须一致，不一致则启动 fail fast；</para>
/// <para><see cref="RoleLevel"/>：Role 级字段——各 Role 配置段可不同，以最后一次设置为准。</para>
/// </remarks>
public enum SettingFieldLevel
{
    /// <summary>
    /// 进程级：多 Role 同进程下必须一致的字段（如数据库连接、雪花 ID、超时策略）。
    /// </summary>
    /// <remarks>
    /// Process-level: fields that must be identical across roles in the same process (e.g. database connection, snowflake id, timeout policies).
    /// </remarks>
    ProcessLevel = 0,

    /// <summary>
    /// Role 级：各 Role 配置段可各自取值的字段（如监听端口、Role 身份标识、模块 ID 范围）。
    /// </summary>
    /// <remarks>
    /// Role-level: fields that may differ per role configuration section (e.g. listening ports, role identity, module id ranges).
    /// </remarks>
    RoleLevel = 1,
}
