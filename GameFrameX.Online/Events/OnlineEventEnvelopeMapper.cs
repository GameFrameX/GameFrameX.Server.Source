// ==========================================================================================
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

using GameFrameX.Foundation.Idempotency;

namespace GameFrameX.Online.Events;

/// <summary>
/// Online 事件与 Foundation 事件信封的双向映射（vault:C2 红线：Online 领域语义不进 Foundation，
/// 作用域字段经 Foundation 信封 <c>Attributes</c> 稳定键承载）。
/// <para>
/// 维护约束：稳定键 <c>online.tenantId</c>/<c>online.appId</c>/<c>online.serverId</c>/<c>online.playerId</c>
/// 为跨端契约，禁止重命名或挪用；未知 Attributes 键在反向映射时原样保留于 <c>ExtraAttributes</c> 语义外（当前丢弃），
/// 新增传输字段必须走信封结构版本评审。
/// </para>
/// </summary>
public static class OnlineEventEnvelopeMapper
{
    /// <summary>
    /// Attributes 稳定键：租户标识。
    /// </summary>
    public const string TenantAttributeKey = "online.tenantId";

    /// <summary>
    /// Attributes 稳定键：App 标识。
    /// </summary>
    public const string AppAttributeKey = "online.appId";

    /// <summary>
    /// Attributes 稳定键：区服标识。
    /// </summary>
    public const string ServerAttributeKey = "online.serverId";

    /// <summary>
    /// Attributes 稳定键：玩家标识。
    /// </summary>
    public const string PlayerAttributeKey = "online.playerId";

    /// <summary>
    /// 将 Online 事件映射为 Foundation 事件信封（发布方向）。
    /// </summary>
    /// <param name="onlineEvent">Online 事件。</param>
    /// <returns>Foundation 事件信封。</returns>
    public static EventEnvelope ToEnvelope(OnlineEvent onlineEvent)
    {
        if (onlineEvent == null)
        {
            throw new ArgumentNullException(nameof(onlineEvent));
        }

        var attributes = new Dictionary<string, string>
        {
            { TenantAttributeKey, onlineEvent.TenantId.ToString() },
            { AppAttributeKey, onlineEvent.AppId.ToString() },
            { ServerAttributeKey, onlineEvent.ServerId.ToString() },
            { PlayerAttributeKey, onlineEvent.PlayerId.ToString() },
        };

        return new EventEnvelope(onlineEvent.EventId, onlineEvent.EventType, onlineEvent.OccurredTime, onlineEvent.SchemaVersion, onlineEvent.Source, onlineEvent.CorrelationId, onlineEvent.Payload, attributes);
    }

    /// <summary>
    /// 将 Foundation 事件信封还原为 Online 事件（消费方向；作用域字段缺失时回落为 0）。
    /// </summary>
    /// <param name="envelope">Foundation 事件信封。</param>
    /// <returns>Online 事件。</returns>
    public static OnlineEvent FromEnvelope(EventEnvelope envelope)
    {
        if (envelope == null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        return new OnlineEvent
        {
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            OccurredTime = envelope.OccurredTime,
            SchemaVersion = envelope.SchemaVersion,
            Source = envelope.Source,
            CorrelationId = envelope.CorrelationId,
            Payload = envelope.Payload,
            TenantId = ReadInt64(envelope, TenantAttributeKey),
            AppId = ReadInt64(envelope, AppAttributeKey),
            ServerId = ReadInt64(envelope, ServerAttributeKey),
            PlayerId = ReadInt64(envelope, PlayerAttributeKey),
        };
    }

    /// <summary>
    /// 从信封 Attributes 读取整型稳定键值。
    /// </summary>
    /// <param name="envelope">事件信封。</param>
    /// <param name="key">稳定键。</param>
    /// <returns>解析值；缺失或格式非法时为 0。</returns>
    private static long ReadInt64(EventEnvelope envelope, string key)
    {
        if (!envelope.Attributes.TryGetValue(key, out var value) || !long.TryParse(value, out var parsed))
        {
            return 0;
        }

        return parsed;
    }
}
