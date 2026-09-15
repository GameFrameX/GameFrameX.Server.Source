// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Events;
using GameFrameX.Online.GameEvents;
using GameFrameX.Online.Match;
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Session;
using GameFrameX.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// C104 游戏事件测试基座：真实事件存储 + 真实死信汇 + 真实摄取器 / 投影器 / 指标复算服务。
    /// </summary>
    internal sealed class OnlineGameEventTestHarness
    {
        /// <summary>租户标识。</summary>
        public const long TenantId = 1;

        /// <summary>App 标识。</summary>
        public const long AppId = 10;

        /// <summary>另一 App 标识（跨 App 反预言用例）。</summary>
        public const long OtherAppId = 11;

        /// <summary>区服标识。</summary>
        public const long ServerId = 100;

        /// <summary>玩家一。</summary>
        public const long PlayerOne = 1001;

        /// <summary>玩家二（举报场景中作为被举报方）。</summary>
        public const long PlayerTwo = 1002;

        /// <summary>基准时刻（UTC 毫秒）。</summary>
        public const long Now = 1000000L;

        /// <summary>
        /// 初始化测试基座。
        /// </summary>
        public OnlineGameEventTestHarness()
        {
            EventStore = new InMemoryOnlineGameEventStore();
            DeadLetterSink = new InMemoryOnlineGameEventDeadLetterSink();
            Ingestor = new OnlineGameEventIngestor(EventStore, DeadLetterSink);
            Projector = new OnlineGameEventProjector(Ingestor);
            MetricsService = new OnlineGameEventMetricsService(EventStore, DeadLetterSink);
        }

        /// <summary>获取事件存储（只含受理事件）。</summary>
        public InMemoryOnlineGameEventStore EventStore
        {
            get;
        }

        /// <summary>获取死信汇（只含拒绝事件）。</summary>
        public InMemoryOnlineGameEventDeadLetterSink DeadLetterSink
        {
            get;
        }

        /// <summary>获取摄取器。</summary>
        public OnlineGameEventIngestor Ingestor
        {
            get;
        }

        /// <summary>获取六类事件投影器。</summary>
        public OnlineGameEventProjector Projector
        {
            get;
        }

        /// <summary>获取指标复算服务。</summary>
        public OnlineGameEventMetricsService MetricsService
        {
            get;
        }

        /// <summary>
        /// 构造一个标准游戏事件信封（默认为登记表内的合法事件；版本 / 字段 / 作用域可覆写以构造脏事件）。
        /// </summary>
        /// <param name="eventName">事件名。</param>
        /// <param name="fields">载荷字段投影（可空，取登记表必需字段的全零填充）。</param>
        /// <param name="schemaVersion">Schema 版本。</param>
        /// <param name="tenantId">租户标识。</param>
        /// <param name="appId">App 标识。</param>
        /// <param name="occurredTime">事件发生时刻（UTC 毫秒）。</param>
        /// <param name="playerId">玩家标识（信封维度）。</param>
        /// <returns>事件信封实例。</returns>
        public static OnlineEvent BuildEnvelope(string eventName, Dictionary<string, string> fields = null, int schemaVersion = 1, long tenantId = TenantId, long appId = AppId, long occurredTime = Now, long playerId = PlayerOne)
        {
            return new OnlineEvent
            {
                EventId = "evt-test-" + System.Guid.NewGuid().ToString("N"),
                EventType = eventName,
                OccurredTime = occurredTime,
                SchemaVersion = schemaVersion,
                TenantId = tenantId,
                AppId = appId,
                ServerId = ServerId,
                PlayerId = playerId,
                Source = OnlineGameEventSchema.Source,
                CorrelationId = "corr-test",
                Payload = JsonSerializer.SerializeToUtf8Bytes(fields ?? new Dictionary<string, string>()),
                PayloadAuditFields = fields,
            };
        }

        /// <summary>
        /// 按登记表为指定事件名构造**必需字段齐备**的载荷（用于构造除目标缺陷外全部合法的用例）。
        /// </summary>
        /// <param name="eventName">事件名。</param>
        /// <param name="omitField">需要刻意缺失的必需字段（可空表示不缺失）。</param>
        /// <returns>载荷字段投影。</returns>
        public static Dictionary<string, string> CompleteFields(string eventName, string omitField = null)
        {
            var fields = new Dictionary<string, string>();
            var descriptor = OnlineGameEventSchema.Find(eventName);
            Assert.NotNull(descriptor);
            foreach (var required in descriptor.RequiredFields)
            {
                if (required == omitField)
                {
                    continue;
                }

                fields[required] = "1";
            }

            return fields;
        }

        /// <summary>
        /// 读取存储中的全部事件（断言「拒绝事件不进存储」用）。
        /// </summary>
        /// <returns>事件列表。</returns>
        public Task<List<OnlineEvent>> StoredEventsAsync()
        {
            return EventStore.ListAsync(TenantId, AppId, 0, long.MaxValue);
        }

        /// <summary>
        /// 读取死信（断言「拒绝事件必进死信」用）。
        /// </summary>
        /// <returns>死信列表。</returns>
        public Task<List<OnlineGameEventDeadLetter>> DeadLettersAsync()
        {
            return DeadLetterSink.ListAsync(TenantId, AppId, 0, long.MaxValue);
        }

        /// <summary>
        /// 构造会话领域对象（默认本 App、玩家一）。
        /// </summary>
        /// <param name="state">会话状态。</param>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="sessionId">会话标识。</param>
        /// <returns>会话实例。</returns>
        public static OnlineSession Session(OnlineSessionState state, long playerId = PlayerOne, string sessionId = "sess-1")
        {
            return new OnlineSession
            {
                Id = sessionId,
                TenantId = TenantId,
                AppId = AppId,
                ServerId = ServerId,
                GameAccountId = playerId,
                PlayerId = playerId,
                DeviceId = "device-1",
                ConnectionId = "conn-1",
                State = state,
                CreatedAtTime = Now,
                AuthenticatedAtTime = Now + 1,
                ConnectedAtTime = Now + 2,
                LastHeartbeatAtTime = Now + 3,
                DisconnectAtTime = Now + 4,
            };
        }

        /// <summary>
        /// 构造匹配票据领域对象。
        /// </summary>
        /// <param name="state">票据状态。</param>
        /// <returns>票据实例。</returns>
        public static OnlineMatchTicket Ticket(OnlineMatchTicketState state = OnlineMatchTicketState.Queued)
        {
            return new OnlineMatchTicket
            {
                TicketId = "ticket-1",
                PartyId = "party-1",
                PlayerIds = new List<long> { PlayerOne, PlayerTwo },
                TenantId = TenantId,
                AppId = AppId,
                ServerId = ServerId,
                Mode = 7,
                Region = 1,
                State = state,
                CreatedAtTime = Now,
            };
        }

        /// <summary>
        /// 构造对局领域对象。
        /// </summary>
        /// <param name="state">对局状态。</param>
        /// <param name="matchId">对局标识。</param>
        /// <returns>对局实例。</returns>
        public static OnlineMatch Match(OnlineMatchState state, string matchId = "match-1")
        {
            return new OnlineMatch
            {
                TenantId = TenantId,
                AppId = AppId,
                ServerId = ServerId,
                MatchId = matchId,
                AssignmentId = "assign-1",
                Mode = 7,
                Region = 1,
                State = state,
                Members = new List<OnlineMatchMember>(),
                CreatedTime = Now,
                StateChangedTime = Now + 1,
            };
        }

        /// <summary>
        /// 构造对局结果领域对象。
        /// </summary>
        /// <param name="outcome">结果终态。</param>
        /// <param name="entries">条目（玩家, 是否胜方）序列。</param>
        /// <returns>对局结果实例。</returns>
        public static OnlineMatchResult MatchResult(OnlineMatchState outcome, params (long PlayerId, bool IsWinner)[] entries)
        {
            var resultEntries = new List<OnlineMatchResultEntry>();
            foreach (var item in entries)
            {
                resultEntries.Add(new OnlineMatchResultEntry
                {
                    PlayerId = item.PlayerId,
                    Rank = resultEntries.Count + 1,
                    IsWinner = item.IsWinner,
                    Score = 100 - resultEntries.Count,
                    Rewards = new List<OnlineAssetChangeLine>(),
                });
            }

            return new OnlineMatchResult
            {
                MatchResultId = "mrs-1",
                MatchId = "match-1",
                TenantId = TenantId,
                AppId = AppId,
                ServerId = ServerId,
                Mode = 7,
                Region = 1,
                Outcome = outcome,
                Entries = resultEntries,
                SettledTime = Now,
            };
        }

        /// <summary>
        /// 构造资产交易领域对象。
        /// </summary>
        /// <param name="source">资产变更来源。</param>
        /// <param name="operation">操作类型。</param>
        /// <param name="state">交易状态。</param>
        /// <returns>资产交易实例。</returns>
        public static OnlineAssetTransaction Transaction(OnlineAssetChangeSource source, OnlineGrantOperation operation, OnlineAssetTransactionState state = OnlineAssetTransactionState.Succeeded)
        {
            return new OnlineAssetTransaction
            {
                TransactionId = "txn-1",
                IdempotencyKey = "idem-1",
                RequestDigest = "digest-1",
                TenantId = TenantId,
                AppId = AppId,
                PlayerId = PlayerOne,
                HomeServerId = ServerId,
                InitiatingServerId = ServerId,
                Source = source,
                Operation = operation,
                Reason = "测试",
                BusinessOrderId = "order-1",
                ExpectedChangeCount = 1,
                State = state,
                CreatedTime = Now,
                SettledTime = Now + 1,
            };
        }

        /// <summary>
        /// 构造举报工单领域对象。
        /// </summary>
        /// <param name="scene">举报场景。</param>
        /// <returns>举报工单实例。</returns>
        public static OnlineReportCase ReportCase(OnlineReportScene scene)
        {
            return new OnlineReportCase
            {
                ReportId = "report-1",
                TenantId = TenantId,
                AppId = AppId,
                ReporterId = PlayerOne,
                ReportedPlayerId = PlayerTwo,
                Scene = scene,
                Reason = OnlineReportReason.Harassment,
                MatchId = "match-1",
                ChatMessageId = "msg-1",
                ChannelId = "channel-1",
                State = OnlineReportState.Submitted,
                CreatedAtTime = Now,
                UpdatedAtTime = Now,
            };
        }

        /// <summary>
        /// 构造处罚领域对象。
        /// </summary>
        /// <param name="revoked">是否已撤销。</param>
        /// <returns>处罚实例。</returns>
        public static OnlinePunishment Punishment(bool revoked = false)
        {
            return new OnlinePunishment
            {
                PunishmentId = "punish-1",
                TenantId = TenantId,
                AppId = AppId,
                PlayerId = PlayerTwo,
                Kind = OnlinePunishmentKind.Ban,
                Reason = "测试处罚",
                EffectiveAtTime = Now,
                ExpiresAtTime = Now + 86400000,
                Revoked = revoked,
                CreatedAtTime = Now,
            };
        }
    }
}
