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

namespace GameFrameX.Online.Matchmaking;

/// <summary>
/// 匹配票据状态（vault:C5 S4.5：队列状态必须唯一且可解释）。
/// <para>
/// 维护约束（红线）：<see cref="Queued"/> 是唯一的活动态，其余四态均为终态且不可回退——
/// 「取消后不再收到旧结果」（VC-4.2）依赖于此：一旦离开 Queued，票据不再参与任何成组，
/// 已有 assignment 也不会被后续取消改写。状态迁移的唯一入口是票据存储的
/// <c>CommitMatchAsync</c> 与 <c>UpdateStateAsync</c>（条件更新，不满足期望态则不生效）。
/// </para>
/// </summary>
public enum OnlineMatchTicketState
{
    /// <summary>排队中（唯一活动态）。</summary>
    Queued = 0,

    /// <summary>已匹配（已产出 assignment）。</summary>
    Matched = 1,

    /// <summary>已取消（玩家主动取消）。</summary>
    Cancelled = 2,

    /// <summary>已过期（超过 ExpiresAt 未凑齐）。</summary>
    Expired = 3,

    /// <summary>已失败（规则不可满足或校验失败）。</summary>
    Failed = 4,
}
