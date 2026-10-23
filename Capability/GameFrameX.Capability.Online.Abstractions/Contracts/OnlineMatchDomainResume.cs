//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes and liabilities arising from secondary development,
//   本组织不承担任何责任，开发者须自行承担。
//   The organization shall not be responsible for any disputes; developers shall bear the responsibility.
//  ==========================================================================================

using System.Collections.Generic;

namespace GameFrameX.Capability.Online.Abstractions.Contracts;

/// <summary>
/// 匹配域恢复回执：按「票据 → 分配登记 → 运行时对局成员表」逐级核对后的恢复产物
///（快照 + 可用增量 / 回退说明）。
/// </summary>
public sealed class OnlineMatchDomainResume
{
    /// <summary>是否恢复（或幂等确认）了对局成员——false 仅在无对局可恢复时出现。</summary>
    public bool Resumed { get; set; }

    /// <summary>协议数值对局标识。</summary>
    public long MatchId { get; set; }

    /// <summary>对局状态（OnlineMatchState 数值）。</summary>
    public int MatchState { get; set; }

    /// <summary>恢复点服务器序号（快照切片序号）。</summary>
    public long ServerSequence { get; set; }

    /// <summary>增量是否可用（false = 客户端必须以快照重建本地状态）。</summary>
    public bool DeltaAvailable { get; set; }

    /// <summary>成员状态投影（含各成员当前状态）。</summary>
    public IReadOnlyList<OnlineMatchDomainResumeMember> Members { get; set; }

    /// <summary>玩法私有状态载荷（不透明字节；服务端权威快照切片）。</summary>
    public byte[] GameState { get; set; }

    /// <summary>处置说明（增量 / 快照使用方式）。</summary>
    public string Instruction { get; set; }
}
