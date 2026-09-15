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
/// 聊天频道类型（vault:C7 S6.5：四类频道，成员资格来源各不相同，隐私边界由此划开）。
/// <para>
/// 维护约束（成员资格来源，VC-6.8/6.9 的落点）：
/// <list type="table">
/// <item><term><see cref="Direct"/></term><description>成员 = 参与双方（无向对，A→B 与 B→A 落在**同一频道**，
/// 否则历史会裂成两条单向流）；成员集合随频道记录落库。</description></item>
/// <item><term><see cref="Party"/></term><description>成员 = 队伍成员，由队伍域通过
/// <see cref="IOnlineChannelMembershipProbe"/> 提供（Chat 域不反向依赖队伍域）。</description></item>
/// <item><term><see cref="Group"/></term><description>成员 = 群组成员，同一探针提供。</description></item>
/// <item><term><see cref="Global"/></term><description>成员 = 同租户同 App 的全体玩家，**不看 ServerId**——
/// 全局频道是 App 级公共空间，按区服切分会割裂公共舆论场。</description></item>
/// </list>
/// </para>
/// <para>
/// 维护约束：成员资格是**读取前置**，非成员一律 <c>ResourceNotFound</c>（反预言，不泄露频道存在性）；
/// 新增频道类型必须同时给出成员资格来源与隐私边界，否则不得落地。
/// </para>
/// </summary>
public enum OnlineChatChannelKind
{
    /// <summary>
    /// 定向私聊频道（参与双方；受屏蔽、禁言、封禁三重裁决）。
    /// </summary>
    Direct = 0,

    /// <summary>
    /// 队伍频道（绑定 PartyId；受禁言、封禁裁决，不受屏蔽影响——同队已成事实，屏蔽不拆队）。
    /// </summary>
    Party = 1,

    /// <summary>
    /// 群组频道（绑定 GroupId；受禁言、封禁裁决）。
    /// </summary>
    Group = 2,

    /// <summary>
    /// 全局频道（App 级公共空间；受禁言、封禁、频控与内容审核裁决）。
    /// </summary>
    Global = 3,
}
