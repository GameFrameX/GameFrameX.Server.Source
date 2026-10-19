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

namespace GameFrameX.Capability.Online.Abstractions.Contracts;

/// <summary>
/// 对局桥游戏视图（C245）：平台对局状态在协议适配层的保密投影快照——
/// 出拳保密（未结算局不回显真实出拳）、胜负只由服务端产出。
/// <para>
/// 维护约束：本视图是**投影**而非权威状态——权威状态在平台对局 Actor；投影规则
/// （当前局 Gesture 一律 0、已结算局回显最近一次真实出拳、整局胜者仅终局非 0）
/// 由玩法插件实现（玩法属主自行决定对外可见性），桥与协议层只做字段级转发。
/// </para>
/// </summary>
/// <remarks>
/// The bridge game view (C245): the secrecy-projected snapshot of a platform match for the
/// protocol adaptation layer — gestures stay hidden until their round resolves, and outcomes are
/// produced by the server only. This is a projection, never the authoritative state (which lives
/// in the platform match actor); the projection rules are implemented by the game plugin (the
/// gameplay owner decides outward visibility) while the bridge and protocol layers only forward
/// fields.
/// </remarks>
public sealed class OnlineMatchBridgeGameView
{
    /// <summary>
    /// 获取或设置房间标识（对局标识派生源之一，回显请求侧关联键）。
    /// </summary>
    public long RoomId { get; set; }

    /// <summary>
    /// 获取或设置平台对局标识（确定性派生，同房间同局号恒同值）。
    /// </summary>
    public string MatchId { get; set; }

    /// <summary>
    /// 获取或设置玩法模式（样例首版 = 410 石头剪刀布）。
    /// </summary>
    public int Mode { get; set; }

    /// <summary>
    /// 获取或设置对局生命周期状态（桥接投影）。
    /// </summary>
    public OnlineMatchBridgeGameState State { get; set; }

    /// <summary>
    /// 获取或设置当前局数（从 1 起）。
    /// </summary>
    public int Round { get; set; }

    /// <summary>
    /// 获取或设置整局胜者玩家标识（未终局或平局为 0；服务端裁决）。
    /// </summary>
    public long WinnerPlayerId { get; set; }

    /// <summary>
    /// 获取或设置玩家视图列表（保密投影）。
    /// </summary>
    public List<OnlineMatchBridgePlayerView> Players { get; set; } = new List<OnlineMatchBridgePlayerView>();
}
