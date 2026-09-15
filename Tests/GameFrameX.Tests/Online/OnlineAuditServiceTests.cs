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
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameFrameX.Online.Audit;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 统一审计链路服务测试（C105 / vault:C9 S8.2 · VC-8.3/VC-8.4 审计半边 + VC-8.16 服务端半边：
    /// 审计完整性校验（操作者/原因/标识/类型/作用域/域白名单逐维度拒绝）、重复接入幂等（不重复落档无副作用）、
    /// 脱敏落档与检索（掩码逐键断言、无明文敏感值）、跨域联查与各维度过滤、keyset 全序分页不重不漏、
    /// App 级与区服级审计共存、跨作用域读取与无数据同构（反预言））。
    /// </summary>
    public class OnlineAuditServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 9101;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 9102;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 9103;

        /// <summary>测试用其它租户标识（反预言用例）。</summary>
        private const long OtherTenantId = 9901;

        /// <summary>测试用其它 App 标识（反预言用例）。</summary>
        private const long OtherAppId = 9902;

        /// <summary>
        /// 构造一条完整可接入的审计条目（各用例在此基础上按需破坏单一维度）。
        /// </summary>
        /// <param name="eventId">审计标识。</param>
        /// <param name="domain">审计域。</param>
        /// <param name="occurredTime">业务时刻（UTC 毫秒）。</param>
        /// <returns>完整审计条目。</returns>
        private static OnlineAuditEntry CreateEntry(string eventId, string domain, long occurredTime)
        {
            return new OnlineAuditEntry
            {
                Domain = domain,
                EventId = eventId,
                EventType = "admin.audit." + domain.ToLowerInvariant(),
                OccurredTime = occurredTime,
                TenantId = TenantId,
                AppId = AppId,
                ServerId = ServerId,
                PlayerId = 42,
                OperatorId = "admin-001",
                OperatorName = "运维一号",
                Reason = "测试用受控操作留痕",
                Source = "online-admin",
                CorrelationId = "corr-" + eventId,
            };
        }

        /// <summary>
        /// 构造被测服务（InMemory 存储 + C93 默认脱敏器）。
        /// </summary>
        /// <returns>统一审计服务。</returns>
        private static OnlineAuditService CreateService()
        {
            return new OnlineAuditService(new InMemoryOnlineAuditStore());
        }

        /// <summary>
        /// VC-8.3 审计半边：完整审计（操作者/原因/时间）跨域接入后可被统一检索逐条定位——
        /// 支付、处罚、受控操作三域一次查询串起且全序倒序。
        /// </summary>
        [Fact]
        public async Task Ingest_CompleteAudit_CrossDomainSearchable()
        {
            var service = CreateService();

            var payment = CreateEntry("audit-payment-001", OnlineAuditDomain.Payment, 1_700_000_000_000L);
            var penalty = CreateEntry("audit-penalty-001", OnlineAuditDomain.Penalty, 1_700_000_001_000L);
            var operation = CreateEntry("audit-operation-001", OnlineAuditDomain.Operation, 1_700_000_002_000L);

            foreach (var entry in new[] { payment, penalty, operation })
            {
                var result = await service.IngestAsync(entry);
                Assert.True(result.IsSuccess);
                Assert.False(result.Data.IsDuplicate);
            }

            var query = await service.QueryAsync(new OnlineScope(TenantId, AppId, ServerId));
            Assert.True(query.IsSuccess);
            var records = query.Data.Records;
            Assert.Equal(3, records.Count);

            // 全序 = 发生时刻倒序；每条含操作者/原因/时间（VC-8.3 审计三要素完整）。
            Assert.Equal("audit-operation-001", records[0].EventId);
            Assert.Equal("audit-penalty-001", records[1].EventId);
            Assert.Equal("audit-payment-001", records[2].EventId);
            foreach (var record in records)
            {
                Assert.Equal("admin-001", record.OperatorId);
                Assert.Equal("测试用受控操作留痕", record.Reason);
                Assert.True(record.OccurredTime > 0);
            }
        }

        /// <summary>
        /// VC-8.3 审计半边：缺操作者的审计条目被拒绝（宁拒毋缺——不完整审计进不了链路）。
        /// </summary>
        [Fact]
        public async Task Ingest_MissingOperator_Rejected()
        {
            var service = CreateService();
            var entry = CreateEntry("audit-x-001", OnlineAuditDomain.Reward, 1_700_000_000_000L);
            entry.OperatorId = string.Empty;

            var result = await service.IngestAsync(entry);
            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, result.Code);

            var query = await service.QueryAsync(new OnlineScope(TenantId, AppId, ServerId));
            Assert.Empty(query.Data.Records);
        }

        /// <summary>
        /// VC-8.3 审计半边：缺原因的审计条目被拒绝。
        /// </summary>
        [Fact]
        public async Task Ingest_MissingReason_Rejected()
        {
            var service = CreateService();
            var entry = CreateEntry("audit-x-002", OnlineAuditDomain.Reward, 1_700_000_000_000L);
            entry.Reason = null;

            var result = await service.IngestAsync(entry);
            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, result.Code);
        }

        /// <summary>
        /// VC-8.3 审计半边：未知域被白名单拒绝（防各域私造域值分裂链路）。
        /// </summary>
        [Fact]
        public async Task Ingest_UnknownDomain_Rejected()
        {
            var service = CreateService();
            var entry = CreateEntry("audit-x-003", "SomePrivateDomain", 1_700_000_000_000L);

            var result = await service.IngestAsync(entry);
            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, result.Code);
        }

        /// <summary>
        /// VC-8.3 审计半边：缺审计标识 / 缺事件类型 / 缺租户或 App 的条目分别被拒绝（参数非法）。
        /// </summary>
        /// <param name="mutate">按维度破坏条目的委托。</param>
        [Theory]
        [InlineData("eventId")]
        [InlineData("eventType")]
        [InlineData("tenant")]
        [InlineData("app")]
        public async Task Ingest_MissingRequiredDimensions_Rejected(string dimension)
        {
            var service = CreateService();
            var entry = CreateEntry("audit-x-004", OnlineAuditDomain.Mail, 1_700_000_000_000L);
            switch (dimension)
            {
                case "eventId":
                    entry.EventId = string.Empty;
                    break;
                case "eventType":
                    entry.EventType = string.Empty;
                    break;
                case "tenant":
                    entry.TenantId = 0;
                    break;
                case "app":
                    entry.AppId = 0;
                    break;
            }

            var result = await service.IngestAsync(entry);
            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, result.Code);
        }

        /// <summary>
        /// VC-8.4 审计半边：同一 EventId 重复接入幂等——第二次回执 IsDuplicate=true（同构成功），
        /// 存储仍只有一条、检索不重复、无副作用。
        /// </summary>
        [Fact]
        public async Task Ingest_DuplicateEventId_IdempotentReplay()
        {
            var service = CreateService();
            var entry = CreateEntry("audit-idem-001", OnlineAuditDomain.Payment, 1_700_000_000_000L);

            var first = await service.IngestAsync(entry);
            Assert.True(first.IsSuccess);
            Assert.False(first.Data.IsDuplicate);

            var second = await service.IngestAsync(entry);
            Assert.True(second.IsSuccess);
            Assert.Equal("audit-idem-001", second.Data.EventId);
            Assert.True(second.Data.IsDuplicate);

            var third = await service.IngestAsync(entry);
            Assert.True(third.IsSuccess);
            Assert.True(third.Data.IsDuplicate);

            var query = await service.QueryAsync(new OnlineScope(TenantId, AppId, ServerId));
            var matched = query.Data.Records.Where(r => r.EventId == "audit-idem-001").ToList();
            Assert.Single(matched);
        }

        /// <summary>
        /// VC-8.16 服务端半边：敏感键（token/手机号/邮箱/口令）落档前被脱敏——存储与检索结果逐键断言为掩码，
        /// 非敏感键原样保留；检索全程无明文敏感值。
        /// </summary>
        [Fact]
        public async Task Ingest_SensitiveFields_MaskedInStorageAndRetrieval()
        {
            var service = CreateService();
            var entry = CreateEntry("audit-mask-001", OnlineAuditDomain.Penalty, 1_700_000_000_000L);
            entry.PayloadAuditFields = new Dictionary<string, string>
            {
                { "SessionToken", "otk-plaintext-secret-value" },
                { "ContactPhone", "13800138000" },
                { "BindEmail", "player@example.com" },
                { "LoginPassword", "p@ssw0rd" },
                { "OrderAmount", "648" },
                { "OrderId", "order-2026-001" },
            };

            var ingest = await service.IngestAsync(entry);
            Assert.True(ingest.IsSuccess);

            var query = await service.QueryAsync(new OnlineScope(TenantId, AppId, ServerId));
            var record = Assert.Single(query.Data.Records);

            Assert.Equal(OnlineEventSanitizer.MaskedValue, record.SanitizedFields["SessionToken"]);
            Assert.Equal(OnlineEventSanitizer.MaskedValue, record.SanitizedFields["ContactPhone"]);
            Assert.Equal(OnlineEventSanitizer.MaskedValue, record.SanitizedFields["BindEmail"]);
            Assert.Equal(OnlineEventSanitizer.MaskedValue, record.SanitizedFields["LoginPassword"]);
            Assert.Equal("648", record.SanitizedFields["OrderAmount"]);
            Assert.Equal("order-2026-001", record.SanitizedFields["OrderId"]);

            // 检索结果整体不含任何明文敏感值（VC-8.16：无明文敏感数据）。
            foreach (var pair in record.SanitizedFields)
            {
                Assert.DoesNotContain("otk-plaintext-secret-value", pair.Value);
                Assert.DoesNotContain("13800138000", pair.Value);
                Assert.DoesNotContain("player@example.com", pair.Value);
                Assert.DoesNotContain("p@ssw0rd", pair.Value);
            }
        }

        /// <summary>
        /// 跨域检索：域集合过滤（跨域联查）命中所选域并排除未选域；空集合等价于不限域。
        /// </summary>
        [Fact]
        public async Task Query_DomainFilter_CrossDomainJoin()
        {
            var service = CreateService();
            await service.IngestAsync(CreateEntry("dj-payment", OnlineAuditDomain.Payment, 1_700_000_000_100L));
            await service.IngestAsync(CreateEntry("dj-penalty", OnlineAuditDomain.Penalty, 1_700_000_000_200L));
            await service.IngestAsync(CreateEntry("dj-mail", OnlineAuditDomain.Mail, 1_700_000_000_300L));

            var joined = await service.QueryAsync(new OnlineScope(TenantId, AppId, ServerId), new OnlineAuditQuery
            {
                Domains = new List<string> { OnlineAuditDomain.Payment, OnlineAuditDomain.Penalty },
            });
            Assert.Equal(2, joined.Data.Records.Count);
            Assert.All(joined.Data.Records, r => Assert.NotEqual(OnlineAuditDomain.Mail, r.Domain));

            var all = await service.QueryAsync(new OnlineScope(TenantId, AppId, ServerId), new OnlineAuditQuery
            {
                Domains = new List<string>(),
            });
            Assert.Equal(3, all.Data.Records.Count);
        }

        /// <summary>
        /// 检索各维度过滤：玩家 / 操作者 / 事件类型 / 关联键 / 区服 / 时间窗（闭区间）各自独立生效。
        /// </summary>
        [Fact]
        public async Task Query_OptionalFilters_EachDimensionEffective()
        {
            var service = CreateService();
            var byPlayer = CreateEntry("f-player", OnlineAuditDomain.Operation, 1_700_000_000_000L);
            byPlayer.PlayerId = 777;
            await service.IngestAsync(byPlayer);

            var byOperator = CreateEntry("f-operator", OnlineAuditDomain.Operation, 1_700_000_000_100L);
            byOperator.OperatorId = "admin-777";
            await service.IngestAsync(byOperator);

            var byEventType = CreateEntry("f-type", OnlineAuditDomain.Operation, 1_700_000_000_200L);
            byEventType.EventType = "online.session.kicked";
            await service.IngestAsync(byEventType);

            var byCorrelation = CreateEntry("f-corr", OnlineAuditDomain.Operation, 1_700_000_000_300L);
            byCorrelation.CorrelationId = "trace-abc";
            await service.IngestAsync(byCorrelation);

            var appLevel = CreateEntry("f-app-level", OnlineAuditDomain.RemoteConfig, 1_700_000_000_400L);
            appLevel.ServerId = 0;
            await service.IngestAsync(appLevel);

            var scope = new OnlineScope(TenantId, AppId, ServerId);

            var playerQuery = await service.QueryAsync(scope, new OnlineAuditQuery { PlayerId = 777 });
            Assert.Equal("f-player", Assert.Single(playerQuery.Data.Records).EventId);

            var operatorQuery = await service.QueryAsync(scope, new OnlineAuditQuery { OperatorId = "admin-777" });
            Assert.Equal("f-operator", Assert.Single(operatorQuery.Data.Records).EventId);

            var typeQuery = await service.QueryAsync(scope, new OnlineAuditQuery { EventType = "online.session.kicked" });
            Assert.Equal("f-type", Assert.Single(typeQuery.Data.Records).EventId);

            var correlationQuery = await service.QueryAsync(scope, new OnlineAuditQuery { CorrelationId = "trace-abc" });
            Assert.Equal("f-corr", Assert.Single(correlationQuery.Data.Records).EventId);

            var serverQuery = await service.QueryAsync(scope, new OnlineAuditQuery { ServerId = 0 });
            Assert.Equal("f-app-level", Assert.Single(serverQuery.Data.Records).EventId);

            // 时间窗为闭区间：[t, t] 命中恰好落在边界上的记录。
            var windowQuery = await service.QueryAsync(scope, new OnlineAuditQuery
            {
                StartTime = 1_700_000_000_100L,
                EndTime = 1_700_000_000_200L,
            });
            Assert.Equal(2, windowQuery.Data.Records.Count);
        }

        /// <summary>
        /// 分页：全序 keyset 游标翻页不重不漏（同毫秒多行由 EventId 打破平局）；非法游标与越界页大小拒绝。
        /// </summary>
        [Fact]
        public async Task Query_Pagination_NoOverlapNoMiss_StableOrder()
        {
            var service = CreateService();
            // 5 条中 2 条同毫秒（验证 EventId 打破平局的全序稳定性）。
            await service.IngestAsync(CreateEntry("page-a", OnlineAuditDomain.Payment, 1_700_000_000_500L));
            await service.IngestAsync(CreateEntry("page-b", OnlineAuditDomain.Payment, 1_700_000_000_400L));
            await service.IngestAsync(CreateEntry("page-c", OnlineAuditDomain.Reward, 1_700_000_000_400L));
            await service.IngestAsync(CreateEntry("page-d", OnlineAuditDomain.Reward, 1_700_000_000_300L));
            await service.IngestAsync(CreateEntry("page-e", OnlineAuditDomain.Mail, 1_700_000_000_200L));

            var scope = new OnlineScope(TenantId, AppId, ServerId);
            var seen = new List<string>();
            string cursor = null;
            while (true)
            {
                var page = await service.QueryAsync(scope, new OnlineAuditQuery { Cursor = cursor, PageSize = 2 });
                Assert.True(page.IsSuccess);
                seen.AddRange(page.Data.Records.Select(r => r.EventId));
                if (!page.Data.Cursor.HasMore)
                {
                    break;
                }

                cursor = page.Data.Cursor.Cursor;
            }

            // 倒序全序：晚毫秒在前，同毫秒按 EventId 序数升序（page-b < page-c）。
            Assert.Equal(new[] { "page-a", "page-b", "page-c", "page-d", "page-e" }, seen);
            Assert.Equal(5, seen.Distinct().Count());

            var badCursor = await service.QueryAsync(scope, new OnlineAuditQuery { Cursor = "not-a-valid-cursor" });
            Assert.False(badCursor.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, badCursor.Code);

            var oversized = await service.QueryAsync(scope, new OnlineAuditQuery { PageSize = 201 });
            Assert.False(oversized.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, oversized.Code);
        }

        /// <summary>
        /// 反预言（作用域越界）：跨租户/跨 App 检索与「无数据」（空存储同作用域）在页形状上逐项一致
        /// （空行集 + HasMore=false + 空游标），不泄露其它作用域审计的存在性。
        /// </summary>
        [Fact]
        public async Task Query_CrossScope_EmptyIsomorphicWithNoData()
        {
            var service = CreateService();
            await service.IngestAsync(CreateEntry("iso-001", OnlineAuditDomain.Payment, 1_700_000_000_000L));

            var otherTenant = await service.QueryAsync(new OnlineScope(OtherTenantId, AppId, ServerId));
            var otherApp = await service.QueryAsync(new OnlineScope(TenantId, OtherAppId, ServerId));
            var noData = await CreateService().QueryAsync(new OnlineScope(TenantId, AppId, ServerId));

            foreach (var page in new[] { otherTenant, otherApp, noData })
            {
                Assert.True(page.IsSuccess);
                Assert.Empty(page.Data.Records);
                Assert.False(page.Data.Cursor.HasMore);
                Assert.Equal(string.Empty, page.Data.Cursor.Cursor);
            }
        }

        /// <summary>
        /// 作用域锚定语义：QueryAsync 只锚定 TenantId/AppId，缺任一即拒绝；
        /// 区服维度完全由查询条件承载（App 级 ServerId=0 与区服级审计在默认查询中共存）。
        /// </summary>
        [Fact]
        public async Task Query_ScopeAnchors_TenantAppOnly()
        {
            var service = CreateService();
            await service.IngestAsync(CreateEntry("anchor-server", OnlineAuditDomain.Operation, 1_700_000_000_000L));

            var appLevel = CreateEntry("anchor-app", OnlineAuditDomain.RemoteConfig, 1_700_000_000_100L);
            appLevel.ServerId = 0;
            await service.IngestAsync(appLevel);

            // scope.ServerId=任意值不影响结果集（锚定语义只看 TenantId/AppId）。
            var scopeWithServer = new OnlineScope(TenantId, AppId, 12345);
            var page = await service.QueryAsync(scopeWithServer);
            Assert.Equal(2, page.Data.Records.Count);

            var missingTenant = await service.QueryAsync(new OnlineScope(0, AppId, ServerId));
            Assert.False(missingTenant.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, missingTenant.Code);

            var missingApp = await service.QueryAsync(new OnlineScope(TenantId, 0, ServerId));
            Assert.False(missingApp.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, missingApp.Code);
        }

        /// <summary>
        /// 落档防御性拷贝：接入后修改调用方持有的 PayloadAuditFields 字典，不改变已落档记录的脱敏投影
        /// （持久化行语义——调用方引用不得别名存储内部状态）。
        /// </summary>
        [Fact]
        public async Task Ingest_DefensiveCopy_CallerMutationDoesNotLeakIntoStore()
        {
            var service = CreateService();
            var entry = CreateEntry("defensive-001", OnlineAuditDomain.Payment, 1_700_000_000_000L);
            var payload = new Dictionary<string, string> { { "OrderAmount", "100" } };
            entry.PayloadAuditFields = payload;

            await service.IngestAsync(entry);
            payload["OrderAmount"] = "999999";

            var query = await service.QueryAsync(new OnlineScope(TenantId, AppId, ServerId));
            Assert.Equal("100", Assert.Single(query.Data.Records).SanitizedFields["OrderAmount"]);
        }
    }
}
