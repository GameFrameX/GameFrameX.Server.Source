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
/// 输入拒绝原因（vault:C6 VC-5.3 / VC-5.4 / VC-5.5：非法输入、重复包、乱序包都必须有稳定原因码）。
/// <para>
/// 维护约束（红线）：任何未被接受的输入都必须带非 <see cref="None"/> 的原因，
/// 且拒绝**不得改变对局状态**（拒绝路径不推进 <c>ServerSequence</c>，也不记录服务器事件）。
/// 本枚举不复用 <c>OnlineErrorCode</c>（该枚举成员值已被跨仓快照锁定），
/// 由对局入口在返回给调用方时映射到对应错误码。
/// </para>
/// </summary>
public enum OnlineMatchInputRejection
{
    /// <summary>无（输入已被接受）。</summary>
    None = 0,

    /// <summary>重复包（客户端序号不大于已接受的最大客户端序号，VC-5.4）。</summary>
    Duplicate = 1,

    /// <summary>乱序包（客户端序号跳跃，超出允许的连续窗口，VC-5.5）。</summary>
    OutOfOrder = 2,

    /// <summary>提交者不是本对局成员（VC-5.3 越权入局/越权操作）。</summary>
    NotMember = 3,

    /// <summary>提交者已退出或被踢出（终态成员不可参与玩法）。</summary>
    MemberLeft = 4,

    /// <summary>提交者处于断线态，需先重连（VC-5.6）。</summary>
    MemberDisconnected = 5,

    /// <summary>对局当前阶段不接受输入（仅 <see cref="OnlineMatchState.Running"/> 接受）。</summary>
    StateNotPlayable = 6,

    /// <summary>对局已结束（VC-5.7 / VC-5.12：结束后不再接受任何输入）。</summary>
    MatchEnded = 7,

    /// <summary>玩法级非法输入（越权回合、非本回合玩家、动作不可识别等，VC-5.3）。</summary>
    IllegalAction = 8,

    /// <summary>作用域不匹配（跨租户/App/Server，任务书红线 P0-3）。</summary>
    ScopeDenied = 9,
}
