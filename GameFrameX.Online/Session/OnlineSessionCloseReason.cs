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
/// 会话终态原因码（vault:C3 S2.5/VC-2.15：断网超窗等场景必须有确定原因码，无永久 Reconnecting）。
/// <para>
/// 维护约束：会话进入终态时必填；原因码参与 Session 终态事件审计（PayloadAuditFields.Reason），
/// 客户端按原因码区分重登路径（如 ReplacedByNewSession 提示顶号）。
/// </para>
/// </summary>
public enum OnlineSessionCloseReason
{
    /// <summary>无（会话未进入终态）。</summary>
    None = 0,

    /// <summary>玩家主动登出。</summary>
    LoggedOut = 1,

    /// <summary>管理员/风控强制踢下线。</summary>
    KickedByOperator = 2,

    /// <summary>Token 到期未刷新。</summary>
    Expired = 3,

    /// <summary>被新会话顶替（多端 LatestWins 策略）。</summary>
    ReplacedByNewSession = 4,

    /// <summary>Token 被吊销（主动登出/安全处置）。</summary>
    Revoked = 5,

    /// <summary>重连窗口超时（断线后未在窗口内重连）。</summary>
    ReconnectWindowExpired = 6,

    /// <summary>服务端关闭（重启/停服；重启后 Session 失效策略由装配层配置）。</summary>
    ServerShutdown = 7,

    /// <summary>账号注销。</summary>
    AccountDeactivated = 8,
}
