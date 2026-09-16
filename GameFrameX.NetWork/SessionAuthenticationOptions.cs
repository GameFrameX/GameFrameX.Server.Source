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
/// 会话首消息鉴权配置。
/// </summary>
/// <remarks>
/// Session first-message authentication options.
/// KCP 等无握手传输在服务端收到未知会话标识的任意数据包即建连，伪造会话标识的 UDP 洪泛会制造会话泄漏。
/// 本配置定义鉴权防线语义：会话建立后必须在 <see cref="Timeout"/> 窗口内完成鉴权（收到
/// <see cref="AuthenticatedByMessageIds"/> 内的消息即视为鉴权完成），否则由服务端主动关闭；
/// 未鉴权期间仅放行心跳与 <see cref="AllowedMessageIds"/> 白名单消息，白名单外消息按协议违规立即关闭。
/// 超时窗口自会话建立起算、不因收发消息重置，避免白名单内慢速洪泛无限续命；
/// 与 <c>UseClearIdleSession</c> 空闲清理协同：本超时是短窗主动防线，空闲清理是长窗兜底。
/// </remarks>
public sealed class SessionAuthenticationOptions
{
    /// <summary>
    /// 获取或设置鉴权超时窗口。
    /// </summary>
    /// <remarks>
    /// Gets or sets the authentication timeout window.
    /// 会话建立后在该窗口内未收到 <see cref="AuthenticatedByMessageIds"/> 内的消息即被服务端关闭。
    /// 默认 30 秒，需覆盖正常登录流程（含选角停留）；窗口从会话建立起算，不因白名单消息重置。
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 获取或设置超时扫描间隔。
    /// </summary>
    /// <remarks>
    /// Gets or sets the interval used to scan for authentication timeouts.
    /// 实际关闭时间最晚比 <see cref="Timeout"/> 晚一个扫描间隔；测试可调小以缩短验证周期。
    /// </remarks>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 获取未鉴权期间允许进入业务处理的消息码白名单。
    /// </summary>
    /// <remarks>
    /// Gets the message-id whitelist allowed before authentication completes.
    /// 应包含登录流程全部请求消息（如账号登录、角色列表、角色创建、角色登录）；
    /// 白名单为空表示未鉴权会话除心跳外全部拦截（fail-closed 严格默认）。
    /// </remarks>
    public HashSet<int> AllowedMessageIds { get; } = new HashSet<int>();

    /// <summary>
    /// 获取「鉴权完成」消息码集合。
    /// </summary>
    /// <remarks>
    /// Gets the message ids that mark the session as authenticated.
    /// 未鉴权会话收到集合内消息即标记为已鉴权，此后所有消息正常放行；
    /// 集合应包含于 <see cref="AllowedMessageIds"/>（鉴权完成消息本身必须可进入业务处理，
    /// 由业务 handler 完成绑定，如角色登录消息在业务层绑定 ActorId）。
    /// </remarks>
    public HashSet<int> AuthenticatedByMessageIds { get; } = new HashSet<int>();
}
