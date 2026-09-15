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
//   Any disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using GameFrameX.Online.HotfixRollback;

namespace GameFrameX.Online.Events;

/// <summary>
/// Hotfix 域事件工厂（vault:C9 S8.5；C93 信封事件，<c>Source = "online-hotfix"</c>，CorrelationId = 版本号）。
/// <para>
/// 维护约束（红线）：事件是**事实不是状态**——<c>VersionRegistered</c> 只陈述「某版本清单已登记」
/// （簿记事实，不含消息契约行明细——清单本体经版本清单存储读取）；
/// <c>RolledBack</c> 只陈述「已从 X 回滚到 Y、兼容检查摘要」，只留计数与版本号作为审计锚点，
/// 差异明细不进事件（经回滚回执与统一审计链路承载）。
/// </para>
/// </summary>
public static class OnlineHotfixEvents
{
    /// <summary>版本清单已登记（发布流水线簿记事实）。</summary>
    public const string VersionRegistered = "Online.Hotfix.VersionRegistered";

    /// <summary>Hotfix 已回滚（受控操作事实：切换成功 + 兼容检查摘要）。</summary>
    public const string RolledBack = "Online.Hotfix.RolledBack";

    /// <summary>事件来源标识。</summary>
    public const string Source = "online-hotfix";

    /// <summary>
    /// 构造版本清单登记事件。
    /// </summary>
    /// <param name="manifest">已登记的版本清单。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateVersionRegistered(OnlineHotfixProtocolManifest manifest)
    {
        var messageCount = manifest.Messages == null ? 0 : manifest.Messages.Count;
        var payload = new VersionRegisteredPayload
        {
            Version = manifest.Version ?? string.Empty,
            MessageCount = messageCount,
            RegisteredTime = manifest.RegisteredTime,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "Version", manifest.Version ?? string.Empty },
            { "MessageCount", messageCount.ToString(CultureInfo.InvariantCulture) },
            { "RegisteredTime", manifest.RegisteredTime.ToString(CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = VersionRegistered,
            OccurredTime = manifest.RegisteredTime > 0 ? manifest.RegisteredTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = manifest.TenantId,
            AppId = manifest.AppId,
            ServerId = 0,
            PlayerId = 0,
            Source = Source,
            CorrelationId = manifest.Version ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造 Hotfix 回滚事件（切换成功后发布；兼容摘要为三类差异计数）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="previousVersion">回滚前活跃版本号。</param>
    /// <param name="targetVersion">回滚到的目标版本号。</param>
    /// <param name="compatibilitySummary">协议兼容报告摘要（Removed/Changed/Added 计数文本）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateRolledBack(long tenantId, long appId, string previousVersion, string targetVersion, string compatibilitySummary)
    {
        var payload = new RolledBackPayload
        {
            PreviousVersion = previousVersion ?? string.Empty,
            TargetVersion = targetVersion ?? string.Empty,
            CompatibilitySummary = compatibilitySummary ?? string.Empty,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "PreviousVersion", payload.PreviousVersion },
            { "TargetVersion", payload.TargetVersion },
            { "CompatibilitySummary", payload.CompatibilitySummary },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = RolledBack,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = tenantId,
            AppId = appId,
            ServerId = 0,
            PlayerId = 0,
            Source = Source,
            CorrelationId = payload.TargetVersion,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 版本清单登记事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class VersionRegisteredPayload
    {
        /// <summary>版本号。</summary>
        public string Version
        {
            get;
            set;
        }

        /// <summary>协议消息契约行数。</summary>
        public int MessageCount
        {
            get;
            set;
        }

        /// <summary>登记时刻（UTC 毫秒）。</summary>
        public long RegisteredTime
        {
            get;
            set;
        }
    }

    /// <summary>
    /// Hotfix 回滚事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class RolledBackPayload
    {
        /// <summary>回滚前活跃版本号。</summary>
        public string PreviousVersion
        {
            get;
            set;
        }

        /// <summary>回滚到的目标版本号。</summary>
        public string TargetVersion
        {
            get;
            set;
        }

        /// <summary>协议兼容报告摘要（Removed/Changed/Added 计数文本）。</summary>
        public string CompatibilitySummary
        {
            get;
            set;
        }
    }
}
