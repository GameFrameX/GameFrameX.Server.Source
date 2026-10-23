//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and other related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws and regulations of the People's Republic of China and international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file at the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations.

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Abstractions.Contracts;

/// <summary>
/// 登录签发的会话恢复凭据（C204）：Token 明文只在签发响应出现一次，服务端仅存指纹。
/// </summary>
public sealed class OnlineSessionLoginCredential
{
    /// <summary>Token 明文（"otk-" 前缀）。</summary>
    public string Token { get; set; }

    /// <summary>逻辑会话标识（"sess-" 前缀）。</summary>
    public string SessionId { get; set; }

    /// <summary>Token 过期时刻（UTC 毫秒）。</summary>
    public long ExpiresAtTime { get; set; }
}
