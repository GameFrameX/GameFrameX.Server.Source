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

namespace GameFrameX.Online.Party;

/// <summary>
/// 队伍生命周期状态（vault:C5 冻结：Created → Inviting → Formed → Ready → Matching → Matched，
/// 分支 Disbanded / Expired / Left / Cancelled / Failed）。
/// <para>
/// 维护约束：本枚举是队伍状态的唯一权威，迁移合法性只由 <see cref="OnlinePartyStateMachine"/> 判定，
/// 服务层不得绕过状态机直接落库（vault:C5 S4.3：迁移合法性单一判据）。
/// <c>Left</c> 表示「成员退出/被移除后人数跌破下限、队伍仍存活」——补人可回到 <c>Formed</c>；
/// 它是可恢复态，不是终态。这与「解散」语义严格区分：解散后队伍不再使用。
/// </para>
/// </summary>
public enum OnlinePartyState
{
    /// <summary>已创建（仅队长，尚未发出任何邀请）。</summary>
    Created = 0,

    /// <summary>邀请中（存在待答复邀请，人数未达下限）。</summary>
    Inviting = 1,

    /// <summary>已成队（人数达到下限）。</summary>
    Formed = 2,

    /// <summary>已就绪（全员准备，允许入队匹配）。</summary>
    Ready = 3,

    /// <summary>匹配中（已持有有效 Ticket）。</summary>
    Matching = 4,

    /// <summary>已匹配（已产出 assignment，本 change 内为队伍生命周期终点）。</summary>
    Matched = 5,

    /// <summary>已解散（队长解散，或队长退出且无可转移对象）。</summary>
    Disbanded = 6,

    /// <summary>已过期（长时间无成员变动且未进入匹配）。</summary>
    Expired = 7,

    /// <summary>人数不足（成员退出或被移除后跌破下限，队伍仍存活）。</summary>
    Left = 8,

    /// <summary>已取消（主动取消组队/匹配）。</summary>
    Cancelled = 9,

    /// <summary>匹配失败（超过 Ticket 存活期仍未凑齐，或规则不可满足）。</summary>
    Failed = 10,
}
