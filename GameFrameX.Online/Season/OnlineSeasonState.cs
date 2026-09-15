// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

namespace GameFrameX.Online.Season;

/// <summary>
/// 赛季生命周期状态（vault:C8 S7.3：开始/结束时间、分数重置、历史快照与奖励结算的阶段划分）。
/// <para>
/// 维护约束（红线）：状态只经 <see cref="OnlineSeasonStateMachine"/> 的合法边迁移，禁止服务层拼装迁移条件。
/// <see cref="Ended"/> 的语义是「历史快照已生成 **且** 榜单已重置」——进入该态即意味着旧榜可回溯（VC-7.5），
/// 因此结算前置不是「时间到了」而是「状态到了」；
/// <see cref="Settled"/> 是终态，表示赛季奖励已全部发放完成，无出边。
/// </para>
/// </summary>
public enum OnlineSeasonState
{
    /// <summary>
    /// 已定义未开始（到 <c>StartTime</c> 由运行时调度器驱动进入 <see cref="Active"/>，本组件不以时钟隐式迁移）。
    /// </summary>
    Scheduled = 1,

    /// <summary>
    /// 进行中：榜单按 C102 可信写入链路接收分数，累积本届成绩。
    /// </summary>
    Active = 2,

    /// <summary>
    /// 已结束：历史快照已生成、榜单已重置为空，等待赛季奖励结算。
    /// </summary>
    Ended = 3,

    /// <summary>
    /// 奖励结算完成（终态）：本届全部奖励已发放到位，快照与成绩长期保留（赛季结束不删除历史数据）。
    /// </summary>
    Settled = 4,
}
