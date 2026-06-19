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

namespace GameFrameX.NetWork;

/// <summary>
/// 可靠包头标记。
/// </summary>
[Flags]
public enum ReliableHeaderFlags : ushort
{
    /// <summary>
    /// 协议版本 1。
    /// </summary>
    ProtocolVersion1 = 0x0001,

    /// <summary>
    /// 包含可靠扩展头。
    /// </summary>
    Reliable = 0x0010,

    /// <summary>
    /// ACK 控制标记。
    /// </summary>
    Ack = 0x0020,

    /// <summary>
    /// 控制包标记。
    /// </summary>
    Control = 0x0040,

    /// <summary>
    /// Resume 控制标记。
    /// </summary>
    Resume = 0x0080,

    /// <summary>
    /// 只保留最新包标记。
    /// </summary>
    LatestOnly = 0x0100,

    /// <summary>
    /// 不重试标记。
    /// </summary>
    NoRetry = 0x0200,

    /// <summary>
    /// 重复包标记。
    /// </summary>
    Duplicate = 0x0400,
}
