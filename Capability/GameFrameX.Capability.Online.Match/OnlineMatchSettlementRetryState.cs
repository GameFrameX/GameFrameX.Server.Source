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
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers should bear the responsibility themselves.
// ==========================================================================================

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 结算重试条目状态（重投生命周期：自动重试中 / 转人工复核）。
/// </summary>
public enum OnlineMatchSettlementRetryState
{
    /// <summary>
    /// 待自动重投：每次结算 tick 按失败玩家集合收窄重投（已成功玩家零重投）。
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 投递尝试达到上限（含首发）仍存在失败玩家：停止自动重投，转人工复核
    /// （<c>query_match_settlement</c> 可观测面可见，人工处置前条目保持冻结）。
    /// </summary>
    ManualReview = 1,
}
