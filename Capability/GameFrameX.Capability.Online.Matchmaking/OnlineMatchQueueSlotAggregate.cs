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

namespace GameFrameX.Capability.Online.Matchmaking;

/// <summary>
/// 匹配队列槽位聚合行（存储层按「玩法模式 + 区域」分组的聚合结果载体）。
/// <para>
/// 维护约束：本类型只承载聚合结果，不定义统计口径——口径（排队态判定、等待秒来源、吞吐窗口边界）
/// 集中在 Online 总览服务类文档，存储聚合方法按该口径执行谓词。
/// </para>
/// </summary>
public sealed class OnlineMatchQueueSlotAggregate
{
    /// <summary>获取或设置玩法模式。</summary>
    public int Mode { get; set; }

    /// <summary>获取或设置区域。</summary>
    public int Region { get; set; }

    /// <summary>获取或设置槽位内票据数（排队聚合 = 队列深度；分配聚合 = 窗口内消费的票据数）。</summary>
    public int TicketCount { get; set; }

    /// <summary>获取或设置槽位内票据等待时长合计（秒；仅排队聚合填写，分配聚合为 0）。</summary>
    public int TotalWaitSeconds { get; set; }
}
