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

namespace GameFrameX.Online.Session;

/// <summary>
/// 会话生命周期状态（vault:C3 S2.4：Created → Authenticated → Connected → Active，断线入 Reconnecting，终态 Closed/Kicked/Expired）。
/// <para>
/// 维护约束：Session 负责一次连接和鉴权上下文，Presence 只表达在线事实，二者不得混用；
/// 终态（Closed/Kicked/Expired）不可逆，终态会话的 Token 一律失效；
/// 状态推进只允许经 <c>OnlineSessionManager</c>（单写者纪律），Token 服务与管理器之外不得直改 <c>OnlineSession.State</c>。
/// </para>
/// </summary>
public enum OnlineSessionState
{
    /// <summary>已创建（尚未通过鉴权）。</summary>
    Created = 1,

    /// <summary>已鉴权（Token 已签发，连接未建立）。</summary>
    Authenticated = 2,

    /// <summary>已连接（网络通道建立，尚未进入活跃交互）。</summary>
    Connected = 3,

    /// <summary>活跃中（正常交互状态）。</summary>
    Active = 4,

    /// <summary>重连窗口内（断线后在重连窗口内；超窗转终态）。</summary>
    Reconnecting = 5,

    /// <summary>终态：正常关闭（登出/服务端主动关闭）。</summary>
    Closed = 6,

    /// <summary>终态：被踢下线（顶号/管理员强踢/风控）。</summary>
    Kicked = 7,

    /// <summary>终态：过期（Token 到期未刷新）。</summary>
    Expired = 8,
}
