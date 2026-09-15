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
/// 玩家在线状态（vault:C3 S2.5：Presence 只表达在线事实，与 Session 生命周期分离）。
/// <para>
/// 维护约束：状态集合与转换表固化于 <see cref="OnlinePresenceStateMachine"/>（阶段 4/5 依赖，
/// 改动必须走 vault 契约变更）；<see cref="Offline"/> 为「无有效连接」——Presence 存储中无记录即 Offline；
/// Admin 管理员连接不进入 Presence（X6/VC-2.6）。
/// </para>
/// </summary>
public enum OnlinePresenceState
{
    /// <summary>离线（无有效连接；Presence 无记录的缺省语义）。</summary>
    Offline = 0,

    /// <summary>在线（已连接且空闲）。</summary>
    Online = 1,

    /// <summary>空闲（长时间无操作，可配置阈值）。</summary>
    Idle = 2,

    /// <summary>匹配中（在匹配队列中；由阶段 4 组队/匹配驱动）。</summary>
    Matching = 3,

    /// <summary>已组队（由阶段 4 Party 驱动）。</summary>
    InParty = 4,

    /// <summary>对局中（由阶段 5 Match 驱动）。</summary>
    InMatch = 5,

    /// <summary>重连窗口内（断线后在重连窗口内；超窗转 Offline）。</summary>
    Reconnecting = 6,

    /// <summary>被风控限制。</summary>
    Blocked = 7,
}
