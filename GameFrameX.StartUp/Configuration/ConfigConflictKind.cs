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
/// 启动期配置冲突类型（C143f D4）。
/// </summary>
/// <remarks>
/// The kind of a startup configuration conflict (C143f D4).
/// </remarks>
public enum ConfigConflictKind
{
    /// <summary>
    /// 多 Role 显式形态下所选 ServerType 在文件层缺少配置段。
    /// </summary>
    /// <remarks>
    /// A selected server type has no section in the config file layer under an explicit multi-role form.
    /// </remarks>
    MissingSection,

    /// <summary>
    /// 同进程多个 Role 的同一端口字段使用了相同的非零端口值。
    /// </summary>
    /// <remarks>
    /// Multiple roles of the same process use the same non-zero value of one port field.
    /// </remarks>
    PortConflict,

    /// <summary>
    /// CLI 显式设置的字段值与所用文件段的字段值不一致（文件段为单源，CLI 值会被丢弃）。
    /// </summary>
    /// <remarks>
    /// A field explicitly set on the command line differs from the file section in use
    /// (the file section is the single source, so the CLI value would be silently dropped).
    /// </remarks>
    CliFileValueConflict,
}
