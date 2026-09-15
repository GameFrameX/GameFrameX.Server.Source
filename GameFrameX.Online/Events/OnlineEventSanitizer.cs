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

namespace GameFrameX.Online.Events;

/// <summary>
/// Online 事件审计脱敏器（vault:C2：敏感字段被脱敏，无明文泄露，VC-1.17）。
/// <para>
/// 维护约束：敏感键判定按字段名大小写不敏感包含匹配（如 token/secret/password/phone/email）；
/// 命中键的值整体替换为固定掩码，不保留长度信息；默认敏感键集合的收窄须回安全评审，扩充可随装配追加。
/// </para>
/// </summary>
public sealed class OnlineEventSanitizer
{
    /// <summary>
    /// 默认敏感键集合（大小写不敏感包含匹配）。
    /// </summary>
    private static readonly string[] DefaultSensitiveKeys = new string[]
    {
        "token",
        "secret",
        "password",
        "passwd",
        "phone",
        "mobile",
        "email",
        "idcard",
        "identity",
    };

    /// <summary>
    /// 敏感值固定掩码（不泄露长度）。
    /// </summary>
    public const string MaskedValue = "***";

    /// <summary>
    /// 获取追加固件（装配期注入的额外敏感键）。
    /// </summary>
    private readonly string[] _additionalSensitiveKeys;

    /// <summary>
    /// 初始化 <see cref="OnlineEventSanitizer"/>。
    /// </summary>
    /// <param name="additionalSensitiveKeys">追加敏感键（在默认集合之外扩充；传空数组仅用默认集）。</param>
    public OnlineEventSanitizer(params string[] additionalSensitiveKeys)
    {
        _additionalSensitiveKeys = additionalSensitiveKeys ?? Array.Empty<string>();
    }

    /// <summary>
    /// 生成事件的脱敏审计视图（敏感字段投影值替换为掩码，不携带原始载荷）。
    /// </summary>
    /// <param name="onlineEvent">原始事件。</param>
    /// <returns>脱敏审计视图。</returns>
    public OnlineEventAuditView CreateAuditView(OnlineEvent onlineEvent)
    {
        if (onlineEvent == null)
        {
            throw new ArgumentNullException(nameof(onlineEvent));
        }

        Dictionary<string, string> sanitized;
        if (onlineEvent.PayloadAuditFields == null || onlineEvent.PayloadAuditFields.Count == 0)
        {
            sanitized = new Dictionary<string, string>();
        }
        else
        {
            sanitized = new Dictionary<string, string>(onlineEvent.PayloadAuditFields.Count, StringComparer.Ordinal);
            foreach (var pair in onlineEvent.PayloadAuditFields)
            {
                sanitized[pair.Key] = IsSensitiveKey(pair.Key) ? MaskedValue : pair.Value;
            }
        }

        return new OnlineEventAuditView
        {
            EventId = onlineEvent.EventId,
            EventType = onlineEvent.EventType,
            OccurredTime = onlineEvent.OccurredTime,
            SchemaVersion = onlineEvent.SchemaVersion,
            TenantId = onlineEvent.TenantId,
            AppId = onlineEvent.AppId,
            ServerId = onlineEvent.ServerId,
            PlayerId = onlineEvent.PlayerId,
            Source = onlineEvent.Source,
            CorrelationId = onlineEvent.CorrelationId,
            SanitizedFields = sanitized,
        };
    }

    /// <summary>
    /// 判定字段名是否命中敏感键（默认集 + 追加集，大小写不敏感包含匹配）。
    /// </summary>
    /// <param name="fieldName">字段名。</param>
    /// <returns>命中返回 <c>true</c>。</returns>
    private bool IsSensitiveKey(string fieldName)
    {
        foreach (var key in DefaultSensitiveKeys)
        {
            if (fieldName != null && fieldName.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        foreach (var key in _additionalSensitiveKeys)
        {
            if (!string.IsNullOrEmpty(key) && fieldName != null && fieldName.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
