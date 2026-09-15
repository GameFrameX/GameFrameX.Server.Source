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

namespace GameFrameX.Online.Presence;

/// <summary>
/// 在线状态变更原因码（vault:C3 VC-2.15：状态转换必须有确定原因码，无永久 Reconnecting）。
/// <para>
/// 维护约束：每次状态转换必填并随事件透出（<c>OnlinePresenceEvents</c> 审计字段 Reason）；
/// 运营侧按原因码区分「主动离开」与「超窗离线」；新增原因码不改变状态机转换表。
/// </para>
/// </summary>
public enum OnlinePresenceChangeReason
{
    /// <summary>连接建立（Offline → Online）。</summary>
    Connected = 1,

    /// <summary>连接断开（活跃态 → Reconnecting）。</summary>
    Disconnected = 2,

    /// <summary>重连成功（Reconnecting → Online/InMatch）。</summary>
    ReconnectSucceeded = 3,

    /// <summary>重连窗口超时（Reconnecting → Offline）。</summary>
    ReconnectWindowExpired = 4,

    /// <summary>空闲超时（Online → Idle）。</summary>
    IdleTimeout = 5,

    /// <summary>操作恢复活跃（Idle → Online）。</summary>
    ActivityResumed = 6,

    /// <summary>会话关闭（活跃态 → Offline）。</summary>
    Closed = 7,

    /// <summary>风控限制（任意活跃态 → Blocked）。</summary>
    RiskBlocked = 8,

    /// <summary>风控解除（Blocked → Offline）。</summary>
    RiskReleased = 9,

    /// <summary>下游玩法驱动（Matching/InParty/InMatch 相关转换；由阶段 4/5 调用）。</summary>
    StateDriven = 10,
}
