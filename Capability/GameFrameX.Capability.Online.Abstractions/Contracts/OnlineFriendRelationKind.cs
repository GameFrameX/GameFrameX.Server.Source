// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织及其贡献者不承担任何责任，开发者应自行承担。
//   The organization and contributors assume no responsibility; developers shall bear the responsibility themselves.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:    https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Abstractions.Contracts;

/// <summary>
/// 玩家视角的好友关系分类（C244 域桥契约）：把存储态 + 方向事实投影为「该玩家看到的关系种类」。
/// <para>
/// 维护约束：这是契约面的最小读模型——不暴露关系标识 / 时间戳等存储细节，
/// 协议适配层据此完成错误码映射（已是好友 / 申请等待中 / 申请不存在等）；
/// 静止态（拒绝 / 过期 / 已删除）一律投影为 <see cref="None"/>（对协议面等价于「无关系」）。
/// </para>
/// </summary>
/// <remarks>
/// The player-view friendship classification (the C244 domain-bridge contract): projects the storage
/// state plus the direction facts into "the kind of relation this player sees". This is the minimal
/// read model of the contract face — storage details (friendship ids / timestamps) stay hidden, and
/// protocol adapters map error codes from it (already friends / request pending / request absent);
/// quiescent states (rejected / expired / removed) all project to <see cref="None"/> (equivalent to
/// "no relation" on the protocol face).
/// </remarks>
public enum OnlineFriendRelationKind
{
    /// <summary>无关系（不存在记录，或处于拒绝 / 过期 / 已删除静止态）。</summary>
    None = 0,

    /// <summary>已是好友（已建立）。</summary>
    Friend = 1,

    /// <summary>收到申请待答复（待答记录的被请求方是自己）。</summary>
    IncomingRequest = 2,

    /// <summary>已发出申请待答复（待答记录的发起方是自己）。</summary>
    OutgoingRequest = 3,
}
