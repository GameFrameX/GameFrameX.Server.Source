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

namespace GameFrameX.Online.Social;

/// <summary>
/// 聊天内容审核扩展点（vault:C7 S6.7，VC-6.15：插件不影响主流程）。
/// <para>
/// 维护约束（未装配语义）：注入为 <c>null</c> = **全部放行**。审核是扩展能力不是正确性前提，
/// 未装配时若拒绝发送，等于把「有没有接审核服务」变成聊天能不能用的开关。
/// </para>
/// <para>
/// 维护约束（异常语义）：实现**允许抛异常**，<see cref="OnlineChatService"/> 会捕获并放行、
/// 同时发出审核失败事件留审计（VC-6.15）。即审核服务宕机不得导致聊天不可用——
/// 这与「挂插件」的取舍一致：宁可短暂放行未经审核的内容，也不能让全服聊天停摆；
/// 放行事件会把这段时间标记出来，便于事后回扫。**实现方不得自己吞掉异常后返回放行**，
/// 那会让这段放行窗口在审计里消失。
/// </para>
/// <para>
/// 维护约束（性能）：本方法在发送主链路上**同步等待**，实现必须自带超时与快速失败
/// （第三方审核调用建议本地缓存 + 短超时），否则审核服务的延迟会变成玩家的发送延迟。
/// </para>
/// <para>
/// 维护约束（与频控的先后）：频控在本方法**之前**生效——先挡住发送洪峰，再让审核服务承压。
/// 代价是被审核拒绝的内容会占用一个频控配额槽（误伤一次即少 1 条配额，窗口结束即恢复）；
/// 这个取舍是刻意的：反过来的话，恶意玩家可以绕过频率限制直接打满审核服务。
/// </para>
/// </summary>
public interface IOnlineChatModerationHook
{
    /// <summary>
    /// 审核一条待发送消息。
    /// </summary>
    /// <param name="message">待发送消息（状态为 <see cref="OnlineChatMessageState.Normal"/>，
    /// 尚未分配频道内序号；实现不得依赖排序字段）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>裁决结果；拒绝时消息不入库、不下发。</returns>
    Task<OnlineChatModerationVerdict> CheckAsync(OnlineChatMessage message, CancellationToken cancellationToken = default);
}
