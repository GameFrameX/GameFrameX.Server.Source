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
/// 队伍成员离开原因（vault:C5 S4.7：退出/踢出/队长退出/解散/离线移除必须可区分）。
/// <para>
/// 维护约束：本枚举是「成员为何不在队伍里」的审计依据，只进入事件载荷与审计视图，不写入成员列表
/// （成员移除即条目消失）。新增原因必须同步 <c>OnlinePartyEvents</c> 的原因码映射与 VC-4.6/4.7 用例。
/// </para>
/// </summary>
public enum OnlinePartyLeaveReason
{
    /// <summary>主动退出。</summary>
    Left = 0,

    /// <summary>被队长踢出。</summary>
    Kicked = 1,

    /// <summary>队长退出（触发转移或解散）。</summary>
    LeaderExit = 2,

    /// <summary>队伍解散。</summary>
    Disbanded = 3,

    /// <summary>离线移除（入队前按在线事实清理）。</summary>
    OfflineRemoved = 4,
}
