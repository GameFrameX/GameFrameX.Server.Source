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
/// 对局桥玩家视图：平台对局成员在协议适配层的保密投影条目。
/// </summary>
/// <remarks>
/// The per-player secrecy-projected view entry of a platform match member for the protocol
/// adaptation layer.
/// </remarks>
public sealed class OnlineMatchBridgePlayerView
{
    /// <summary>
    /// 获取或设置玩家标识。
    /// </summary>
    public long PlayerId { get; set; }

    /// <summary>
    /// 获取或设置本局是否已出拳（不回显出拳值）。
    /// </summary>
    public bool HasGesture { get; set; }

    /// <summary>
    /// 获取或设置累计胜局数（平台权威计分；现网协议无比分字段，协议映射层不外发——样例能力边界）。
    /// </summary>
    public int Wins { get; set; }

    /// <summary>
    /// 获取或设置出拳值（0 = 未出拳或本局未结算不回显；已结算局回显最近一次真实出拳：
    /// 1 = 石头，2 = 剪刀，3 = 布）。
    /// </summary>
    public int Gesture { get; set; }
}
