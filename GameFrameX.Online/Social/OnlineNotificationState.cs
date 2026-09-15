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
/// 通知生命周期状态（vault:C7 S6.8 冻结 7 态）。
/// <para>
/// 维护约束：状态迁移唯一判据是 <see cref="OnlineNotificationStateMachine.TryTransition"/>；
/// <see cref="Read"/> 与 <see cref="Expired"/> 是仅有的两个终态。首次推送、重试与离线补发共用同一套状态，
/// 不另立「补发中」之类的影子状态——否则同一条通知会被两条路径各投一次
/// （VC-6.12「通知不重复消费」的判定依据）。
/// </para>
/// </summary>
public enum OnlineNotificationState
{
    /// <summary>
    /// 已创建（入队已落库，尚未尝试推送）。
    /// </summary>
    Created = 0,

    /// <summary>
    /// 排队中（推送在途标记：推送开始时落定，推送结束时必然离开本态）。
    /// </summary>
    Queued = 1,

    /// <summary>
    /// 已投递（推送出口返回成功；玩家可读、可标记已读）。
    /// </summary>
    Delivered = 2,

    /// <summary>
    /// 玩家已读（终态；属玩家可见历史，列表默认保留）。
    /// </summary>
    Read = 3,

    /// <summary>
    /// 已过期（终态；通知被丢弃，不再推送也不返回给玩家，VC-6.13）。
    /// </summary>
    Expired = 4,

    /// <summary>
    /// 推送失败且不再重试（失败不可重试，或重试次数已耗尽）。
    /// </summary>
    Failed = 5,

    /// <summary>
    /// 推送失败待重试（由重试扫描或离线补发重投）。
    /// </summary>
    Retrying = 6,
}
