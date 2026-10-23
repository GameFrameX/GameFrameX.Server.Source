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
//   or infringe upon the legal rights and interests of any legal persons as prohibited by law!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers shall bear the responsibility themselves.
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Hotfix.Logic.Game.Room;

namespace GameFrameX.Hotfix.Logic.Game.RockPaperScissors;

/// <summary>
/// 对局桥观察视图后的房间状态推进动作（notes §1.3 Room ↔ Match 映射的决策结果）。
/// </summary>
public enum RockPaperScissorsMatchRoomAction
{
    /// <summary>不推进（组合不满足映射条件）。</summary>
    None = 0,

    /// <summary>Playing → Settling（桥视图 Settling 驱动）。</summary>
    EnterSettling = 1,

    /// <summary>Playing/Settling → Settled（桥视图终态 Completed/SettlementFailed/Terminated 驱动）。</summary>
    MarkSettled = 2,
}
