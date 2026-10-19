// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers shall bear the responsibility themselves.
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Abstractions.Contracts;

/// <summary>
/// 好友列表条目（桥接契约 DTO，C243）：好友查询面向 Hotfix 协议面的最小读模型。
/// <para>
/// 维护约束：字段集与域层好友摘要的最小消费集对齐（标识 / 展示名 / 归属区服 / 在线即时事实 / 建立时刻）；
/// 展示名未装配玩家目录时回退标识字面量、在线未装配探针时恒为 <c>false</c>——两处缺席均为披露态，
/// 由域服务语义保证，本 DTO 不再衍生语义。
/// </para>
/// </summary>
/// <remarks>
/// The friend-list entry (the bridge contract DTO, C243): the minimal read model of the friend query
/// exposed to the Hotfix protocol face. The field set mirrors the minimal consumption set of the
/// domain friend summary (identity / display name / server / online-now fact / established-at);
/// a missing player directory degrades names to id literals and a missing presence probe reads
/// offline — both disclosed absences guaranteed by the domain service semantics.
/// </remarks>
public sealed class OnlineFriendEntry
{
    /// <summary>
    /// 获取或设置好友玩家标识。
    /// </summary>
    public long PlayerId { get; set; }

    /// <summary>
    /// 获取或设置好友展示名（未装配玩家目录时回退为标识字面量）。
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 获取或设置好友归属区服标识。
    /// </summary>
    public long ServerId { get; set; }

    /// <summary>
    /// 获取或设置好友当前是否在线（读取时即时事实；无在线探针时恒为 <c>false</c>）。
    /// </summary>
    public bool Online { get; set; }

    /// <summary>
    /// 获取或设置成为好友的时刻（UTC 毫秒）。
    /// </summary>
    public long EstablishedAtTime { get; set; }
}
