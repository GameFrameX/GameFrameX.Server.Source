// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers shall bear the responsibility themselves.
//  ==========================================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Abstractions.Contracts;

/// <summary>
/// 好友查询契约面（C243 首个 Hotfix 桥接契约）：只读好友列表查询的最小契约，
/// 供 Hotfix 协议面薄适配器经 <c>OnlineFriendBridgeHolder</c> 进程内消费。
/// <para>
/// 维护约束：本契约只承载查询——好友写行为（申请 / 答复 / 删除）的协议面迁移归后续域 change；
/// 作用域三元组由组合根授权作用域供给，玩家位由请求主体（ActorId）填充，客户端字段一律不可覆盖；
/// 实现方为 Capability 层域服务（好友关系生命周期的唯一写者语义见各域服务）。
/// </para>
/// </summary>
/// <remarks>
/// The friend-query contract face (the first Hotfix bridge contract, C243): the minimal read-only
/// friend-list contract consumed in-process by the Hotfix protocol thin adapter through
/// <c>OnlineFriendBridgeHolder</c>. This contract carries queries only — migrating the friend write
/// behaviours onto the protocol face belongs to later domain changes; the scope triple comes from the
/// composition root's authorized scope, the player position is filled with the requesting actor id,
/// and client-supplied fields can never override either.
/// </remarks>
public interface IOnlineFriendQuery
{
    /// <summary>
    /// 列出指定玩家的好友列表（含展示名与在线即时事实）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>好友列表条目。</returns>
    Task<OnlineResult<IReadOnlyList<OnlineFriendEntry>>> ListFriendsAsync(OnlineScope scope, CancellationToken cancellationToken = default);
}
