// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
/// 对局样例桥契约面（C245，D4 RPS 垂直切片）：房间制协议入口到平台对局运行时的最小桥接契约，
/// 供 Hotfix 协议面薄适配器经 <c>CapabilityBridge&lt;IOnlineMatchGameBridge&gt;</c> 进程内消费。
/// <para>
/// 维护约束：本契约首版只承载样例玩法（模式 410 石头剪刀布）——创建/入场的泛化（匹配票据、
/// 观战、跨服）随 C246 匹配域接入另行收敛，不预先发明抽象；作用域三元组由组合根授权作用域供给，
/// 玩家位由请求主体（会话身份）填充，客户端字段一律不可覆盖；实现方为 Capability 层对局域服务
/// （对局生命周期的唯一属主语义见 <c>OnlineMatchRuntime</c> 族文档）。
/// </para>
/// </summary>
/// <remarks>
/// The sample match bridge contract face (C245, the D4 RPS vertical slice): the minimal bridging
/// contract from the room-shaped protocol entries to the platform match runtime, consumed in-process
/// by the Hotfix protocol thin adapter through <c>CapabilityBridge&lt;IOnlineMatchGameBridge&gt;</c>. This contract
/// carries the sample game (mode 410 rock-paper-scissors) only for the first version — generalizing
/// creation/entry (matchmaking tickets, spectating, cross-server) converges with the C246 match
/// domain bridge instead of being invented here. The scope triple comes from the composition root's
/// authorized scope, the player position is filled with the requesting session identity, and
/// client-supplied fields can never override either; the implementation side is the Capability match
/// domain service (the sole owner of the match lifecycle — see the <c>OnlineMatchRuntime</c> family docs).
/// </remarks>
public interface IOnlineMatchGameBridge
{
    /// <summary>
    /// 以房间标识 + 局号开启一局平台对局（幂等：确定性对局标识，重复开启复用既有对局）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位——请求者须为房间成员）。</param>
    /// <param name="roomId">房间标识（对局标识派生源之一）。</param>
    /// <param name="round">局号（房间维度递增；同房间同局号幂等）。</param>
    /// <param name="playerIds">本局玩家集合（房间成员表；首个为房主）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>开局后的对局视图；失败返回对应错误码。</returns>
    Task<OnlineResult<OnlineMatchBridgeGameView>> StartRoomGameAsync(OnlineScope scope, long roomId, int round, IReadOnlyList<long> playerIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 提交一次出拳意图（服务端裁决；当前局已出拳按重复拒绝）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位——提交者须为对局成员）。</param>
    /// <param name="roomId">房间标识。</param>
    /// <param name="round">局号。</param>
    /// <param name="gesture">出拳值（1 = 石头，2 = 剪刀，3 = 布；其余值非法）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提交后的对局视图（保密投影：未结算局不回显真实出拳）。</returns>
    Task<OnlineResult<OnlineMatchBridgeGameView>> SubmitGestureAsync(OnlineScope scope, long roomId, int round, int gesture, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询对局视图（保密投影；客户端经本查询收敛 tick 驱动的变更）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位——查询者须为对局成员）。</param>
    /// <param name="roomId">房间标识。</param>
    /// <param name="round">局号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对局视图；对局不存在返回 <see cref="OnlineErrorCode.ResourceNotFound"/>。</returns>
    Task<OnlineResult<OnlineMatchBridgeGameView>> GetGameInfoAsync(OnlineScope scope, long roomId, int round, CancellationToken cancellationToken = default);
}
