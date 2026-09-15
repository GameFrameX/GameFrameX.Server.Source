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
/// 群组状态（vault:C7 S6.4：群组生命周期只有「活跃」与「已解散」两态）。
/// <para>
/// 维护约束：<see cref="Disbanded"/> 是唯一终态，出边为空——解散后不可复活，也不存在「重建同标识群组」
/// 的语义（任何新建群组一律分配新的群组标识）。解散**不**清空成员集合与邀请集合：历史归属关系原样保留
/// 供审计与追溯，读取方按状态过滤；清空会让「谁在何时属于哪个群」不可考。
/// </para>
/// </summary>
public enum OnlineGroupState
{
    /// <summary>
    /// 活跃（成员变更、邀请、角色调整、Metadata 写入等一切写操作仅在活跃态开放）。
    /// </summary>
    Active = 0,

    /// <summary>
    /// 已解散（终态；仅群主可触发，重复解散幂等返回既有终态快照）。
    /// </summary>
    Disbanded = 1,
}
