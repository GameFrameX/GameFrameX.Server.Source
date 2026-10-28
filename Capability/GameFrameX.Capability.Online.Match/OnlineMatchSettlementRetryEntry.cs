// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers shall bear the responsibility themselves.
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 结算重试视图（admin 可观测面）：一条待重试发奖记录的对外只读投影。
/// </summary>
public sealed class OnlineMatchSettlementRetryEntry
{
    /// <summary>
    /// 获取或设置结算结果标识（幂等边界，待重试表按它去重）。
    /// </summary>
    public string MatchResultId { get; set; }

    /// <summary>
    /// 获取或设置对局标识。
    /// </summary>
    public string MatchId { get; set; }

    /// <summary>
    /// 获取或设置发奖失败玩家列表（逐玩家隔离）。
    /// </summary>
    public List<long> FailedPlayerIds { get; set; } = new List<long>();

    /// <summary>
    /// 获取或设置累计投递尝试次数。
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// 获取或设置重试状态（Pending = 自动重试中；ManualReview = 尝试达上限转人工复核，停止自动重投）。
    /// </summary>
    public OnlineMatchSettlementRetryState State { get; set; } = OnlineMatchSettlementRetryState.Pending;
}
