// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
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

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 排行榜分数累计策略（vault:C8 S7.1「分数来源」语义的一部分：同玩家多次可信写入如何合并为榜上分数）。
/// <para>
/// 维护约束（红线）：策略裁决必须发生在存储临界区内（<c>InMemoryOnlineLeaderboardStore.ApplySubmissionAsync</c>），
/// 与条目落档同一临界区，保证并发投递下「读旧值 → 算新值 → 写入」不被交错撕裂；
/// 服务层不得先读后写绕开该临界区。
/// </para>
/// </summary>
public enum OnlineLeaderboardScoreUpdatePolicy
{
    /// <summary>
    /// 保留按榜向更优的分数（Descending 榜取更大值，Ascending 榜取更小值；首笔写入直接生效）。
    /// </summary>
    Best = 0,

    /// <summary>
    /// 累加全部写入分数（赛季积分类榜单；幂等重放不计入——重放在幂等层被拦截，不进入本策略）。
    /// </summary>
    Sum = 1,

    /// <summary>
    /// 以最近一次写入覆盖（连胜 / 当前段位类榜单）。
    /// </summary>
    Latest = 2,
}
