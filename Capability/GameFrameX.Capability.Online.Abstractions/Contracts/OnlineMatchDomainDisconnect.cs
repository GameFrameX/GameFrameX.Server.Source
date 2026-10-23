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
/// 匹配域断线通知回执（C204）：Marked=false 表示玩家无进行中对局（或对局已结束 / 成员已离局），
/// 非失败——断线通知是 best-effort 协同事件。
/// </summary>
public sealed class OnlineMatchDomainDisconnect
{
    /// <summary>是否标记了断线成员（服务端留存 rt- 令牌，不下发客户端）。</summary>
    public bool Marked { get; set; }

    /// <summary>被标记的协议数值对局标识（未标记为 0）。</summary>
    public long MatchId { get; set; }
}
