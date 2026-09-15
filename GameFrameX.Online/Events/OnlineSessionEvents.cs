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

namespace GameFrameX.Online.Events;

using System.Text;
using System.Text.Json;
using GameFrameX.Online.Session;

/// <summary>
/// 会话域事件工厂（vault:C3 S2.8：Session 生命周期事件经 C93 事件信封供 Party/Friend/Admin 消费）。
/// <para>
/// 维护约束：EventType 命名 <c>Online.Session.{动作}</c>，新增事件类型必须同步 Admin 镜像侧登记；
/// Payload 为 UTF-8 JSON（SchemaVersion=1），审计字段（SessionId/PlayerId/Reason）经
/// <see cref="OnlineEvent.PayloadAuditFields"/> 透出——踢下线审计必须含 SessionId 与原因（VC-2.3），
/// 操作者（Operator）由装配层写入 Reason 承载；事件是事实不是状态，消费端不得回写会话。
/// </para>
/// </summary>
public static class OnlineSessionEvents
{
    /// <summary>会话创建（Created）。</summary>
    public const string SessionCreated = "Online.Session.Created";

    /// <summary>会话通过鉴权（Authenticated，Token 已签发）。</summary>
    public const string SessionAuthenticated = "Online.Session.Authenticated";

    /// <summary>会话建立连接（Connected）。</summary>
    public const string SessionConnected = "Online.Session.Connected";

    /// <summary>会话进入活跃（Active）。</summary>
    public const string SessionActive = "Online.Session.Active";

    /// <summary>会话断线进入重连窗口（Reconnecting）。</summary>
    public const string SessionReconnecting = "Online.Session.Reconnecting";

    /// <summary>会话正常关闭（Closed）。</summary>
    public const string SessionClosed = "Online.Session.Closed";

    /// <summary>会话被踢下线（Kicked；审计含原因与操作者上下文）。</summary>
    public const string SessionKicked = "Online.Session.Kicked";

    /// <summary>会话过期（Expired）。</summary>
    public const string SessionExpired = "Online.Session.Expired";

    /// <summary>Token 刷新轮换（TokenRefreshed；旧 Token 即刻失效）。</summary>
    public const string SessionTokenRefreshed = "Online.Session.TokenRefreshed";

    /// <summary>事件来源标识。</summary>
    public const string Source = "online-session";

    /// <summary>
    /// 构造会话生命周期事件。
    /// </summary>
    /// <param name="eventType">事件类型（本类常量）。</param>
    /// <param name="session">目标会话。</param>
    /// <param name="reason">原因描述（审计承载；踢下线事件由装配层写入操作者）。</param>
    /// <param name="correlationId">关联标识（可空；接续请求上下文）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent Create(string eventType, OnlineSession session, string reason, string correlationId = null)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        var payload = new SessionEventPayload
        {
            SessionId = session.Id,
            PlayerId = session.PlayerId,
            State = ((int)session.State).ToString(System.Globalization.CultureInfo.InvariantCulture),
            CloseReason = ((int)session.CloseReason).ToString(System.Globalization.CultureInfo.InvariantCulture),
            TokenGeneration = session.TokenGeneration,
            Reason = reason ?? string.Empty,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "SessionId", session.Id },
            { "PlayerId", session.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "Reason", reason ?? string.Empty },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = eventType,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = session.TenantId,
            AppId = session.AppId,
            ServerId = session.ServerId,
            PlayerId = session.PlayerId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 会话事件负载（SchemaVersion=1；字段命名蛇形，供跨端消费稳定契约）。
    /// </summary>
    private sealed class SessionEventPayload
    {
        /// <summary>会话标识。</summary>
        public string SessionId
        {
            get;
            set;
        }

        /// <summary>玩家标识。</summary>
        public long PlayerId
        {
            get;
            set;
        }

        /// <summary>会话状态（数值字符串）。</summary>
        public string State
        {
            get;
            set;
        }

        /// <summary>终态原因码（数值字符串；0 = 未终态）。</summary>
        public string CloseReason
        {
            get;
            set;
        }

        /// <summary>Token 轮换代数。</summary>
        public int TokenGeneration
        {
            get;
            set;
        }

        /// <summary>原因描述。</summary>
        public string Reason
        {
            get;
            set;
        }
    }
}
