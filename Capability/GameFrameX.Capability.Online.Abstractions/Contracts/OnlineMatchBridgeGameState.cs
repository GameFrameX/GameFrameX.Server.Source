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

namespace GameFrameX.Capability.Online.Abstractions.Contracts;

/// <summary>
/// 对局桥生命周期状态（平台 <c>OnlineMatchState</c> 的桥接投影，C245）。
/// <para>
/// 维护约束：本枚举是契约面自持投影——Abstractions 不依赖 Match 包；消费方（Hotfix 薄适配器）
/// 据此驱动房间状态映射（<see cref="Settling"/> → 房间结算中；<see cref="Completed"/> /
/// <see cref="SettlementFailed"/> / <see cref="Terminated"/> → 房间已结算，可再来一局）。
/// </para>
/// </summary>
public enum OnlineMatchBridgeGameState
{
    /// <summary>等待成员（尚未开局）。</summary>
    Waiting = 0,

    /// <summary>全员就绪（可开始）。</summary>
    Ready = 1,

    /// <summary>进行中（可提交出拳）。</summary>
    Running = 2,

    /// <summary>玩法已分出结果，等待平台结算。</summary>
    Settling = 3,

    /// <summary>结算完成（可信结果已落定，奖励/排行编排进行中或已完成）。</summary>
    Completed = 4,

    /// <summary>结算失败（确定态：无奖励分发）。</summary>
    SettlementFailed = 5,

    /// <summary>无结算终态（超时/取消/失败/中止/已释放）。</summary>
    Terminated = 6,
}
