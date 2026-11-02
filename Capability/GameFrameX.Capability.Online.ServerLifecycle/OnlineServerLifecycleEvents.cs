// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.ServerLifecycle;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Events;

/// <summary>
/// 区服生命周期事件工厂（事件是事实不是状态：载荷只放锚点标识与时刻）。
/// </summary>
public static class OnlineServerLifecycleEvents
{
    /// <summary>区服开服事件（首次登记开服锚点时发布；订阅方据此初始化区服维度玩法状态）。</summary>
    public const string ServerOpened = "online.server.opened";

    /// <summary>区服合服完成事件（归并计划真实执行成功后发布）。</summary>
    public const string ServerMerged = "online.server.merged";

    /// <summary>
    /// 事件来源标识。
    /// </summary>
    public const string Source = "online-server-lifecycle";

    /// <summary>
    /// 构造区服开服事件。
    /// </summary>
    /// <param name="serverId">区服标识。</param>
    /// <param name="openedAtUnixMilliseconds">开服时刻（UTC 毫秒）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateServerOpened(long serverId, long openedAtUnixMilliseconds)
    {
        var payload = new ServerOpenedPayload
        {
            ServerId = serverId,
            OpenedAtUnixMilliseconds = openedAtUnixMilliseconds,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "ServerId", serverId.ToString(CultureInfo.InvariantCulture) },
            { "OpenedAtUnixMilliseconds", openedAtUnixMilliseconds.ToString(CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = ServerOpened,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            ServerId = serverId,
            PlayerId = 0,
            Source = Source,
            CorrelationId = string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造区服合服完成事件。
    /// </summary>
    /// <param name="targetServerId">目标区服标识。</param>
    /// <param name="sourceServerIds">源区服标识集合。</param>
    /// <param name="planId">归并计划标识。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateServerMerged(long targetServerId, IReadOnlyList<long> sourceServerIds, string planId)
    {
        var payload = new ServerMergedPayload
        {
            PlanId = planId ?? string.Empty,
            SourceServerIds = sourceServerIds == null ? new List<long>() : new List<long>(sourceServerIds),
        };
        var auditFields = new Dictionary<string, string>
        {
            { "PlanId", payload.PlanId },
            { "TargetServerId", targetServerId.ToString(CultureInfo.InvariantCulture) },
            { "SourceServerCount", payload.SourceServerIds.Count.ToString(CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = ServerMerged,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            ServerId = targetServerId,
            PlayerId = 0,
            Source = Source,
            CorrelationId = string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>开服事件载荷。</summary>
    private sealed class ServerOpenedPayload
    {
        /// <summary>区服标识。</summary>
        public long ServerId { get; set; }

        /// <summary>开服时刻（UTC 毫秒）。</summary>
        public long OpenedAtUnixMilliseconds { get; set; }
    }

    /// <summary>合服完成事件载荷。</summary>
    private sealed class ServerMergedPayload
    {
        /// <summary>归并计划标识。</summary>
        public string PlanId { get; set; }

        /// <summary>源区服标识集合。</summary>
        public List<long> SourceServerIds { get; set; }
    }
}
