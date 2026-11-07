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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织及其贡献者不承担任何责任，开发者应自行承担。
//   The organization and contributors assume no responsibility; developers shall bear the responsibility themselves.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:    https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Abstractions.Contracts;

/// <summary>
/// 好友域桥契约面（C244，承接 C243 查询缝并扩展为全家族）：好友域全部协议行为的最小桥接契约，
/// 供 Hotfix 协议面薄适配器经 <c>CapabilityBridge&lt;IOnlineFriendDomain&gt;</c> 进程内消费。
/// <para>
/// 维护约束：
/// ① 定位一律以「玩家对」表达（协议面没有关系标识），关系标识编排归实现方（Social 域面）；
/// ② 作用域三元组由组合根授权作用域供给，玩家位由请求主体（会话绑定身份）填充，
/// 客户端字段一律不可覆盖；
/// ③ <see cref="EstablishAsync"/> 承载旧 120 域「直接添加」语义的兼容适配
///（申请 + 立即接受，含「对方有待答申请时等价于本人接受」的收敛），是 T2 裁定 R3 的唯一实现点；
/// ④ 答复 / 解除的前置判定（无待答申请 / 非好友）由本契约以
/// <see cref="OnlineErrorCode.ResourceNotFound"/> 显式失败表达，适配层据此映射旧错误码。
/// </para>
/// </summary>
/// <remarks>
/// The friend-domain bridge contract face (C244, extending the C243 query seam into the full family):
/// the minimal bridging contract for every friend-domain protocol behaviour, consumed in-process by
/// the Hotfix protocol thin adapter through <c>CapabilityBridge&lt;IOnlineFriendDomain&gt;</c>. Locating is always
/// expressed as player pairs (the protocol face carries no friendship ids — id orchestration belongs
/// to the implementing face); the scope triple comes from the composition root's authorized scope,
/// the player position is filled with the requesting session identity, and client-supplied fields can
/// never override either. <see cref="EstablishAsync"/> carries the compatible adaptation of the
/// legacy domain-120 "direct add" semantics (request + immediate accept, converging to "accepting
/// their pending request" when one exists) as the single implementation point of ruling R3; the
/// answer/remove preconditions (no answerable request / not friends) surface as explicit
/// <see cref="OnlineErrorCode.ResourceNotFound"/> failures for the adapter to map legacy codes from.
/// </remarks>
public interface IOnlineFriendDomain
{
    /// <summary>
    /// 列出好友列表（含展示名与在线即时事实；只含已建立关系）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>好友列表条目。</returns>
    Task<OnlineResult<IReadOnlyList<OnlineFriendEntry>>> ListFriendsAsync(OnlineScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出全量关系（好友 / 收到申请 / 发出申请，按关系分类投影；含静止态投影为 None 之外的各类）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>关系条目列表。</returns>
    Task<OnlineResult<IReadOnlyList<OnlineFriendRelation>>> ListRelationsAsync(OnlineScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发起好友申请（幂等收敛：已有待答 / 已是好友返回既有分类，静止态重新发起并翻转方向）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位 = 发起方）。</param>
    /// <param name="targetPlayerId">目标玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作前后的关系分类。</returns>
    Task<OnlineResult<OnlineFriendRelationChange>> RequestAsync(OnlineScope scope, long targetPlayerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 接受好友申请（前置：存在对方发来的待答申请，否则 <see cref="OnlineErrorCode.ResourceNotFound"/>）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位 = 被请求方）。</param>
    /// <param name="fromPlayerId">申请来源玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作前后的关系分类。</returns>
    Task<OnlineResult<OnlineFriendRelationChange>> AcceptAsync(OnlineScope scope, long fromPlayerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 拒绝好友申请（前置同接受；拒绝后为静止态，可重新发起）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位 = 被请求方）。</param>
    /// <param name="fromPlayerId">申请来源玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作前后的关系分类。</returns>
    Task<OnlineResult<OnlineFriendRelationChange>> RejectAsync(OnlineScope scope, long fromPlayerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 解除好友关系（前置：双方确为好友，否则 <see cref="OnlineErrorCode.ResourceNotFound"/>；幂等语义由前置判定承担）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位 = 解除发起方）。</param>
    /// <param name="targetPlayerId">对方玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作前后的关系分类。</returns>
    Task<OnlineResult<OnlineFriendRelationChange>> RemoveAsync(OnlineScope scope, long targetPlayerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 直接建立好友关系（旧 120 域「直接添加」的兼容适配）：申请 + 立即接受；
    /// 对方已有发来的待答申请时等价于本人接受该申请。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位 = 发起方）。</param>
    /// <param name="targetPlayerId">目标玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作前后的关系分类（成功后 <c>After = Friend</c>）。</returns>
    Task<OnlineResult<OnlineFriendRelationChange>> EstablishAsync(OnlineScope scope, long targetPlayerId, CancellationToken cancellationToken = default);
}
