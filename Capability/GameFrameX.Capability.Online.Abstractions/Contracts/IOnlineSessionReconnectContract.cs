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
/// 会话恢复契约面（C204）：Core 物理连接会话与 Online 逻辑会话协同的最小桥接契约——
/// 登录签发（断线前凭据交付）、新连接恢复（持有证明 + 原子轮换）、断线通知（开窗）。
/// <para>
/// 维护约束：
/// ① 身份恢复凭据 = Online 会话 Token（服务端仅存 SHA-256 指纹），本契约不得另建第二凭据权威；
/// ② 作用域三元组由组合根授权作用域供给（经桥租约）——登录 / 断线的玩家位由调用方以会话绑定身份填充，
/// 恢复的玩家位由 Token 解析得出（请求体字段一律不可覆盖作用域）；
/// ③ <see cref="ResumeAsync"/> 成功即原子轮换——旧指纹即刻失效，重放旧 Token 判
/// <see cref="OnlineErrorCode.TokenRevoked"/>；
/// ④ 断线通知只对窗口语义负责（Reconnecting + 截止时刻），不触碰对局成员状态（对局域独立通知）。
/// </para>
/// </summary>
public interface IOnlineSessionReconnectContract
{
    /// <summary>
    /// 登录签发恢复凭据（LatestWins 顶替旧活跃会话；新会话直接落 Connected 并绑定当前连接）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="connectionId">当前物理连接标识（Core SessionId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>签发结果（Token 明文、会话标识、过期时刻）。</returns>
    Task<OnlineResult<OnlineSessionLoginCredential>> IssueForLoginAsync(OnlineScope scope, string connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 新连接恢复：验证 Token（指纹 / 终态 / 到期 / 作用域）→ 会话转 Connected 并替换连接标识 →
    /// 原子轮换（旧 Token 即刻失效）。恢复请求到达时新连接尚未绑定玩家——作用域玩家位可为 0
    ///（恢复主体由 Token 解析得出；>0 时须与 Token 所属会话一致，否则判
    /// <see cref="OnlineErrorCode.ScopeDenied"/>）。
    /// </summary>
    /// <param name="scope">生效作用域（三元组必填；玩家位可 0，>0 时作一致性校验）。</param>
    /// <param name="token">登录 / 上次恢复下发的 Token 明文。</param>
    /// <param name="connectionId">新物理连接标识（Core SessionId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>恢复结果（轮换新 Token、玩家位、会话标识）。</returns>
    Task<OnlineResult<OnlineSessionResumeCredential>> ResumeAsync(OnlineScope scope, string token, string connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断线通知：该玩家的活跃逻辑会话进入重连窗口（Reconnecting；已在窗口内刷新截止时刻）。
    /// 幂等——无活跃会话时静默成功（无身份即无窗口）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="reconnectWindowSeconds">重连窗口（秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    Task<OnlineResult<bool>> NotifyPlayerDisconnectedAsync(OnlineScope scope, long reconnectWindowSeconds, CancellationToken cancellationToken = default);
}

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

/// <summary>
/// 新连接恢复产物（C204）：轮换后的新凭据与恢复的身份绑定。
/// </summary>
public sealed class OnlineSessionResumeCredential
{
    /// <summary>轮换后的新 Token 明文（旧 Token 已即刻失效）。</summary>
    public string NewToken { get; set; }

    /// <summary>恢复的玩家标识。</summary>
    public long PlayerId { get; set; }

    /// <summary>逻辑会话标识。</summary>
    public string SessionId { get; set; }

    /// <summary>新 Token 过期时刻（UTC 毫秒）。</summary>
    public long ExpiresAtTime { get; set; }
}
