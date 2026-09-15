//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
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
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Match;

/// <summary>
/// 对局成员状态（vault:C6「断线不立即等于退出」的落点）。
/// <para>
/// 维护约束（红线）：<see cref="Disconnected"/> 是**可恢复**态而非终态——断线后成员保留在对局成员表中，
/// 直到重连窗口超时（窗口内重连回到 <see cref="Playing"/>，超窗按玩法转 <see cref="Left"/> 或托管，
/// VC-5.6 / VC-5.7）。<see cref="Left"/> 与 <see cref="Kicked"/> 为终态且不可回退。
/// </para>
/// </summary>
public enum OnlineMatchMemberState
{
    /// <summary>已加入（等待阶段，尚未准备）。</summary>
    Joined = 0,

    /// <summary>已准备。</summary>
    Ready = 1,

    /// <summary>对局中（正常在线并参与玩法）。</summary>
    Playing = 2,

    /// <summary>已断线（窗口内可重连回 <see cref="Playing"/>）。</summary>
    Disconnected = 3,

    /// <summary>已退出（终态）。</summary>
    Left = 4,

    /// <summary>已被踢出（终态）。</summary>
    Kicked = 5,
}
