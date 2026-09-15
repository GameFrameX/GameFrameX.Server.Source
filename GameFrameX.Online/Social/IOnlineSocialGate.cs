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
/// 跨域社交裁决契约（vault:C7 关键约束：Block「在 Chat / Party / Matchmaker 三处统一生效」的接口载体）。
/// <para>
/// 维护约束（依赖方向）：本接口**定义在 Social 域**，由 Party 与 Matchmaking **依赖**本接口，
/// Social 域不反向依赖二者——依赖箭头单向，无环（方案复审 P0-1）。
/// Party / Matchmaking 侧的注入是**可选**的（<c>null</c> = 不启用社会裁决，既有行为不变），
/// 沿用 C97 <c>IOnlinePartyPresenceProbe</c> 可空探针先例。
/// </para>
/// <para>
/// 维护约束（唯一入口）：实现必须落在 <see cref="OnlineSocialDecisionService"/>，
/// **禁止**在 Party / Matchmaking 内自建屏蔽或处罚判定（会出现绕过与行为不一致）。
/// 参数刻意不复用 <c>OnlineScope</c>：Party 与 Matchmaker 持有的是票据 / 邀请上的裸标识，
/// 强行拼 Scope 会诱导调用方伪造玩家主体位，反而放大越权风险。
/// </para>
/// <para>
/// 拒绝码语义（调用方如何映射）：<c>AccountBanned</c> = 封禁；
/// <c>RiskControlRejected</c> = 被屏蔽（不暴露是对方屏蔽还是自己屏蔽，屏蔽是单向私密事实）；
/// <c>RateLimitExceeded</c> = 命中风控频次。调用方**直接透传**裁决码，不得改写。
/// </para>
/// </summary>
public interface IOnlineSocialGate
{
    /// <summary>
    /// 裁决一次定向社交互动是否允许（屏蔽按双向生效、封禁拒绝全部、禁言只拒绝私聊）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="fromPlayerId">发起互动的玩家。</param>
    /// <param name="toPlayerId">互动的目标玩家。</param>
    /// <param name="purpose">互动目的（决定裁决力度）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>裁决结果；调用方在 <see cref="OnlineSocialDecision.Allowed"/> 为假时拒绝本次互动。</returns>
    Task<OnlineSocialDecision> EvaluateAsync(long tenantId, long appId, long fromPlayerId, long toPlayerId, OnlineSocialInteractionPurpose purpose, CancellationToken cancellationToken = default);
}
