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
/// 频道成员资格探针（vault:C7 S6.5：Party / Group 频道成员资格的**唯一**来源）。
/// <para>
/// 维护约束（为什么是探针而不是把成员抄进频道记录）：队伍与群组的成员随时在变，
/// 把成员集合冗余进频道记录就必须在每次成员变动时同步刷新——漏一次刷新，
/// 已退队的玩家就还能读到队伍频道消息（隐私泄漏），且没有任何错误会暴露它。
/// 因此每次读取都向归属域取**当前事实**。
/// </para>
/// <para>
/// 维护约束（依赖方向）：探针由归属域实现（队伍域、群组域各自实现本接口），
/// Chat 域只依赖本接口，不反向依赖二者——依赖箭头单向，无环。
/// </para>
/// <para>
/// 维护约束（未装配 = 拒绝，fail closed）：注入为 <c>null</c> 时 Party / Group 频道的成员资格
/// **无法验证**，服务层必须拒绝（<c>StateNotReady</c>）而不是放行。放行等于把频道内容
/// 广播给任意知道频道标识的玩家——成员资格不可验证时必须按「不可访问」处理。
/// </para>
/// </summary>
public interface IOnlineChannelMembershipProbe
{
    /// <summary>
    /// 判定玩家当前是否为绑定主体的成员。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="kind">频道类型（只会是 <see cref="OnlineChatChannelKind.Party"/> 或
    /// <see cref="OnlineChatChannelKind.Group"/>；Direct 与 Global 不走本探针）。</param>
    /// <param name="boundId">绑定主体标识（PartyId 或 GroupId）。</param>
    /// <param name="playerId">待判定的玩家。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前是成员返回 <c>true</c>。</returns>
    Task<bool> IsChannelMemberAsync(long tenantId, long appId, OnlineChatChannelKind kind, string boundId, long playerId, CancellationToken cancellationToken = default);
}
