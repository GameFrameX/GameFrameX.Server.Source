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
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Match;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Timeline;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 玩家时间线服务测试（C101 / vault:C9 S8.1 · VC-8.1-c ~ VC-8.1-e：五腿合并与来源标注、全序游标分页无重漏、
    /// 分组过滤与未知分组空列表、时间窗边界、配置命中腿空槽语义、跨作用域读取与无数据同构）。
    /// </summary>
    public class OnlinePlayerTimelineServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = OnlineOverviewTimelineTestHarness.TenantId;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = OnlineOverviewTimelineTestHarness.AppId;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = OnlineOverviewTimelineTestHarness.ServerId;

        /// <summary>测试用其它区服标识。</summary>
        private const long OtherServerId = OnlineOverviewTimelineTestHarness.OtherServerId;

        /// <summary>
        /// 验证五条腿合并：身份 / 会话（Session）、资产（Asset）、对局（Match）、处罚（Penalty）各腿行均在列，
        /// 且每行都带齐行标识、事件类型、来源域与关联标识（VC-8.1-c）。
        /// </summary>
        [Fact]
        public async Task QueryAsync_ShouldMergeAllDomainLegs()
        {
            // Arrange
            var harness = new OnlineOverviewTimelineTestHarness();
            var alice = await harness.RegisterPlayerAsync(ServerId, "alice");
            var aliceSession = await harness.OpenSessionAsync(ServerId, alice.Player.Id);
            var grant = await harness.GrantAsync(ServerId, alice.Player.Id, 100, "order-timeline");
            var settledAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await harness.CreateMatchAsync(ServerId, alice.Player.Id, "match-timeline", OnlineMatchState.Completed, settledAt, settled: true);
            var punishment = await harness.PunishAsync(alice.Player.Id, "测试处罚");

            // Act
            var outcome = await harness.CreateTimelineService().QueryAsync(OnlineOverviewTimelineTestHarness.CreateScope(playerId: alice.Player.Id));

            // Assert
            Assert.True(outcome.IsSuccess);
            var entries = outcome.Data.Entries;
            Assert.NotEmpty(entries);

            // 身份腿（记录型行：档案 / 账号 / 绑定身份）——关联标识即来源记录主键
            var profileRow = FindEntry(entries, "profile:" + alice.Player.Id);
            Assert.Equal(OnlinePlayerTimelineEventTypes.PlayerProfileCreated, profileRow.EventType);
            Assert.Equal(alice.Player.Id.ToString(), profileRow.CorrelationId);

            var accountRow = FindEntry(entries, "account:" + alice.GameAccount.Id);
            Assert.Equal(OnlinePlayerTimelineEventTypes.AccountRegistered, accountRow.EventType);
            Assert.Equal(alice.GameAccount.Id.ToString(), accountRow.CorrelationId);

            var identityRow = FindEntry(entries, "identity:" + alice.Identity.Id + ":bound");
            Assert.Equal(OnlinePlayerTimelineEventTypes.IdentityBound, identityRow.EventType);
            Assert.Equal(alice.Identity.Id, identityRow.CorrelationId);

            // 会话腿（事件型行：复用会话域既有事件常量与来源域）——关联标识即会话主键
            var sessionRow = FindEntry(entries, "session:" + aliceSession.Id + ":" + aliceSession.State);
            Assert.Equal(OnlineSessionEvents.Source, sessionRow.Source);
            Assert.Equal(aliceSession.Id, sessionRow.CorrelationId);

            // 资产腿（账本流水）——关联标识即交易标识（凭它可反查交易详情）
            var assetRow = FindEntry(entries, "ledger:" + grant.Entries[0].EntryId);
            Assert.Equal(OnlineAssetEvents.AssetGranted, assetRow.EventType);
            Assert.Equal(OnlineAssetEvents.Source, assetRow.Source);
            Assert.Equal(grant.TransactionId, assetRow.CorrelationId);

            // 对局腿（已结算 → 结算事件）——关联标识即对局标识
            var matchRow = FindEntry(entries, "match:match-timeline:settled");
            Assert.Equal(OnlineMatchRuntimeEvents.MatchSettled, matchRow.EventType);
            Assert.Equal(OnlineMatchRuntimeEvents.Source, matchRow.Source);
            Assert.Equal("match-timeline", matchRow.CorrelationId);

            // 处罚腿——关联标识即处罚标识
            var penaltyRow = FindEntry(entries, "punishment:" + punishment.PunishmentId + ":applied");
            Assert.Equal(OnlineSocialEvents.PunishmentChanged, penaltyRow.EventType);
            Assert.Equal(OnlineSocialEvents.Source, penaltyRow.Source);
            Assert.Equal(punishment.PunishmentId, penaltyRow.CorrelationId);

            // 未装配探针 → 运营配置腿空槽（不从其它域推断补齐）
            Assert.DoesNotContain(entries, entry => entry.Group == OnlinePlayerTimelineGroup.LiveOps);

            // 每行带齐契约字段（供消费方按行定位原始记录）
            foreach (var entry in entries)
            {
                Assert.False(string.IsNullOrEmpty(entry.EventId));
                Assert.False(string.IsNullOrEmpty(entry.EventType));
                Assert.False(string.IsNullOrEmpty(entry.Group));
                Assert.False(string.IsNullOrEmpty(entry.Source));
                Assert.False(string.IsNullOrEmpty(entry.CorrelationId));
                Assert.False(string.IsNullOrEmpty(entry.PayloadSummary));
                Assert.True(entry.OccurredAt > 0);
            }
        }

        /// <summary>
        /// 验证全序：发生时刻倒序；同一秒内按行标识序数升序（同秒多行不产生平局，分页才可能稳定）。
        /// </summary>
        [Fact]
        public async Task QueryAsync_ShouldOrderByOccurredAtThenEventId()
        {
            // Arrange
            var harness = new OnlineOverviewTimelineTestHarness();
            var alice = await harness.RegisterPlayerAsync(ServerId, "alice");
            await harness.OpenSessionAsync(ServerId, alice.Player.Id);
            await harness.GrantAsync(ServerId, alice.Player.Id, 100, "order-order");
            var punishment = await harness.PunishAsync(alice.Player.Id, "测试处罚");
            await harness.RevokePunishmentAsync(punishment.PunishmentId);

            // Act
            var outcome = await harness.CreateTimelineService().QueryAsync(OnlineOverviewTimelineTestHarness.CreateScope(playerId: alice.Player.Id));

            // Assert
            Assert.True(outcome.IsSuccess);
            var entries = outcome.Data.Entries;
            Assert.True(entries.Count > 1);

            for (var index = 1; index < entries.Count; index++)
            {
                var previous = entries[index - 1];
                var current = entries[index];
                if (previous.OccurredAt == current.OccurredAt)
                {
                    Assert.True(string.CompareOrdinal(previous.EventId, current.EventId) < 0);
                    continue;
                }

                Assert.True(previous.OccurredAt > current.OccurredAt);
            }
        }

        /// <summary>
        /// 验证游标分页：按不透明游标逐页取完，与全量查询的行序**逐项一致**（无重复、无遗漏），
        /// 且只有末页游标为空字符串且 HasMore 为 false。
        /// </summary>
        [Fact]
        public async Task QueryAsync_ShouldPageThroughCursorWithoutGapsOrDuplicates()
        {
            // Arrange
            var harness = new OnlineOverviewTimelineTestHarness();
            var alice = await harness.RegisterPlayerAsync(ServerId, "alice");
            await harness.OpenSessionAsync(ServerId, alice.Player.Id);
            await harness.GrantAsync(ServerId, alice.Player.Id, 100, "order-page");
            await harness.CreateMatchAsync(ServerId, alice.Player.Id, "match-page", OnlineMatchState.Running, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await harness.PunishAsync(alice.Player.Id, "测试处罚");

            var service = harness.CreateTimelineService();
            var scope = OnlineOverviewTimelineTestHarness.CreateScope(playerId: alice.Player.Id);
            var whole = await service.QueryAsync(scope);
            Assert.True(whole.IsSuccess);
            var expected = CollectEventIds(whole.Data.Entries);
            Assert.True(expected.Count > 3);
            Assert.False(whole.Data.Page.HasMore);
            Assert.Equal(string.Empty, whole.Data.Page.Cursor);

            // Act
            var collected = new List<string>();
            var pageSizes = new List<int>();
            var cursor = string.Empty;
            var pages = 0;
            var insertedEventId = string.Empty;
            while (true)
            {
                var page = await service.QueryAsync(scope, new OnlinePlayerTimelineQuery { PageSize = 2, Cursor = cursor });
                Assert.True(page.IsSuccess);
                foreach (var entry in page.Data.Entries)
                {
                    collected.Add(entry.EventId);
                }

                pageSizes.Add(page.Data.Entries.Count);
                pages++;
                Assert.True(pages <= expected.Count + 2);

                if (pages == 1)
                {
                    // 翻页途中插入新记录（VC-8.1-d）：新行排在游标之后则由后续页自然带上，排在游标之前则本轮不再出现——
                    // 两种情形都不得让**既有行**重复或遗漏。
                    var inserted = await harness.GrantAsync(ServerId, alice.Player.Id, 50, "order-page-inserted");
                    insertedEventId = "ledger:" + inserted.Entries[0].EntryId;
                }

                if (!page.Data.Page.HasMore)
                {
                    Assert.Equal(string.Empty, page.Data.Page.Cursor);
                    break;
                }

                Assert.False(string.IsNullOrEmpty(page.Data.Page.Cursor));
                cursor = page.Data.Page.Cursor;
            }

            // Assert：既有行按原序各出现一次（无重复、无遗漏）；插入的新行至多出现一次（keyset 语义：不回头补页）
            var existing = new List<string>();
            var insertedCount = 0;
            foreach (var eventId in collected)
            {
                if (string.Equals(eventId, insertedEventId, StringComparison.Ordinal))
                {
                    insertedCount++;
                    continue;
                }

                existing.Add(eventId);
            }

            Assert.True(pages > 1);
            Assert.Equal(2, pageSizes[0]);
            Assert.True(insertedCount <= 1);
            Assert.Equal(expected.Count, existing.Count);
            for (var index = 0; index < expected.Count; index++)
            {
                Assert.Equal(expected[index], existing[index]);
            }
        }

        /// <summary>
        /// 验证分组过滤：命中分组只返回该分组；社交分组（本仓不落腿）与未知分组一律返回**空列表而非报错**。
        /// </summary>
        [Fact]
        public async Task QueryAsync_GroupFilter_ShouldIsolateGroupAndNeverFail()
        {
            // Arrange
            var harness = new OnlineOverviewTimelineTestHarness();
            var alice = await harness.RegisterPlayerAsync(ServerId, "alice");
            await harness.OpenSessionAsync(ServerId, alice.Player.Id);
            await harness.GrantAsync(ServerId, alice.Player.Id, 100, "order-group");
            await harness.PunishAsync(alice.Player.Id, "测试处罚");

            var service = harness.CreateTimelineService();
            var scope = OnlineOverviewTimelineTestHarness.CreateScope(playerId: alice.Player.Id);

            // Act
            var penalty = await service.QueryAsync(scope, new OnlinePlayerTimelineQuery { Group = OnlinePlayerTimelineGroup.Penalty });
            var social = await service.QueryAsync(scope, new OnlinePlayerTimelineQuery { Group = OnlinePlayerTimelineGroup.Social });
            var unknown = await service.QueryAsync(scope, new OnlinePlayerTimelineQuery { Group = "NotAGroup" });

            // Assert
            Assert.True(penalty.IsSuccess);
            Assert.NotEmpty(penalty.Data.Entries);
            foreach (var entry in penalty.Data.Entries)
            {
                Assert.Equal(OnlinePlayerTimelineGroup.Penalty, entry.Group);
            }

            Assert.True(social.IsSuccess);
            Assert.Empty(social.Data.Entries);
            Assert.False(social.Data.Page.HasMore);

            Assert.True(unknown.IsSuccess);
            Assert.Empty(unknown.Data.Entries);
        }

        /// <summary>
        /// 验证时间窗为闭区间：以某行时刻为上下界查询时该行必在结果内；窗口外该行必不在。
        /// </summary>
        [Fact]
        public async Task QueryAsync_TimeWindow_ShouldUseInclusiveBounds()
        {
            // Arrange
            var harness = new OnlineOverviewTimelineTestHarness();
            var alice = await harness.RegisterPlayerAsync(ServerId, "alice");
            await harness.OpenSessionAsync(ServerId, alice.Player.Id);
            await harness.GrantAsync(ServerId, alice.Player.Id, 100, "order-window");

            var service = harness.CreateTimelineService();
            var scope = OnlineOverviewTimelineTestHarness.CreateScope(playerId: alice.Player.Id);
            var whole = await service.QueryAsync(scope);
            Assert.True(whole.IsSuccess);
            Assert.NotEmpty(whole.Data.Entries);
            var probe = whole.Data.Entries[0];

            // Act
            var closed = await service.QueryAsync(scope, new OnlinePlayerTimelineQuery { StartTime = probe.OccurredAt, EndTime = probe.OccurredAt });
            var earlier = await service.QueryAsync(scope, new OnlinePlayerTimelineQuery { EndTime = probe.OccurredAt - 1 });
            var fromProbe = await service.QueryAsync(scope, new OnlinePlayerTimelineQuery { StartTime = probe.OccurredAt });

            // Assert
            Assert.True(closed.IsSuccess);
            Assert.Contains(closed.Data.Entries, entry => entry.EventId == probe.EventId);
            foreach (var entry in closed.Data.Entries)
            {
                Assert.Equal(probe.OccurredAt, entry.OccurredAt);
            }

            Assert.True(earlier.IsSuccess);
            Assert.DoesNotContain(earlier.Data.Entries, entry => entry.EventId == probe.EventId);

            Assert.True(fromProbe.IsSuccess);
            foreach (var entry in fromProbe.Data.Entries)
            {
                Assert.True(entry.OccurredAt >= probe.OccurredAt);
            }
        }

        /// <summary>
        /// 验证运营配置腿：未装配探针即空槽；装配后按探针给出的原值落行（来源域缺失回落到 online-config），
        /// 且探针按「(租户, App, 区服, 玩家)」四键取数——他服登记的命中不出现在本服时间线；
        /// 探针漏给行标识时以关联标识合成（否则空行标识会成为不可解码的游标，翻页整页报参数非法），
        /// 行标识与关联标识皆缺的行不可定位故跳过。
        /// </summary>
        [Fact]
        public async Task QueryAsync_ConfigHitLeg_ShouldBeEmptySlotWhenUnwiredAndScopedWhenWired()
        {
            // Arrange
            var harness = new OnlineOverviewTimelineTestHarness();
            var alice = await harness.RegisterPlayerAsync(ServerId, "alice");
            var scope = OnlineOverviewTimelineTestHarness.CreateScope(playerId: alice.Player.Id);

            var probe = new OnlineOverviewTimelineTestHarness.FakeConfigHitProbe();
            probe.Add(TenantId, AppId, ServerId, alice.Player.Id, new OnlineConfigHitRecord
            {
                EventId = "config:local",
                EventType = OnlinePlayerTimelineEventTypes.ConfigHit,
                Source = null,
                OccurredAt = 1800000000,
                CorrelationId = "cfg-local",
                PayloadSummary = "命中活动 A",
            });
            probe.Add(TenantId, AppId, ServerId, alice.Player.Id, new OnlineConfigHitRecord
            {
                EventId = "config:season",
                EventType = OnlinePlayerTimelineEventTypes.ConfigHit,
                Source = "online-season",
                OccurredAt = 1800000001,
                CorrelationId = "cfg-season",
                PayloadSummary = "命中赛季 B",
            });
            probe.Add(TenantId, AppId, ServerId, alice.Player.Id, new OnlineConfigHitRecord
            {
                EventId = string.Empty,
                EventType = OnlinePlayerTimelineEventTypes.ConfigHit,
                Source = null,
                OccurredAt = 1800000003,
                CorrelationId = "cfg-anonymous",
                PayloadSummary = "命中活动 C（探针未给行标识）",
            });
            probe.Add(TenantId, AppId, ServerId, alice.Player.Id, new OnlineConfigHitRecord
            {
                EventId = string.Empty,
                EventType = OnlinePlayerTimelineEventTypes.ConfigHit,
                Source = null,
                OccurredAt = 1800000004,
                CorrelationId = string.Empty,
                PayloadSummary = "不可定位的命中",
            });
            probe.Add(TenantId, AppId, OtherServerId, alice.Player.Id, new OnlineConfigHitRecord
            {
                EventId = "config:foreign-server",
                EventType = OnlinePlayerTimelineEventTypes.ConfigHit,
                Source = null,
                OccurredAt = 1800000002,
                CorrelationId = "cfg-foreign",
                PayloadSummary = "他服命中",
            });

            // Act
            var unwired = await harness.CreateTimelineService().QueryAsync(scope);
            var wired = await harness.CreateTimelineService(probe).QueryAsync(scope);

            // Assert
            Assert.True(unwired.IsSuccess);
            Assert.DoesNotContain(unwired.Data.Entries, entry => entry.Group == OnlinePlayerTimelineGroup.LiveOps);

            Assert.True(wired.IsSuccess);
            var local = FindEntry(wired.Data.Entries, "config:local");
            Assert.Equal(OnlinePlayerTimelineGroup.LiveOps, local.Group);
            Assert.Equal(OnlinePlayerTimelineEventTypes.SourceConfig, local.Source);
            Assert.Equal(1800000000, local.OccurredAt);
            Assert.Equal("cfg-local", local.CorrelationId);

            var season = FindEntry(wired.Data.Entries, "config:season");
            Assert.Equal("online-season", season.Source);

            // 漏给行标识 → 以关联标识合成行标识（行仍可定位，且不产出空行标识）
            var anonymous = FindEntry(wired.Data.Entries, "config:cfg-anonymous");
            Assert.Equal("cfg-anonymous", anonymous.CorrelationId);
            Assert.Equal(1800000003, anonymous.OccurredAt);

            // 行标识与关联标识皆缺 → 不可定位故整行跳过
            var liveOpsCount = 0;
            foreach (var entry in wired.Data.Entries)
            {
                Assert.False(string.IsNullOrEmpty(entry.EventId));
                if (entry.Group == OnlinePlayerTimelineGroup.LiveOps)
                {
                    liveOpsCount++;
                }
            }

            Assert.Equal(3, liveOpsCount);
            Assert.DoesNotContain(wired.Data.Entries, entry => entry.EventId == "config:foreign-server");

            // 翻页走一遍：末行含「合成行标识」时游标仍可解码（否则下一页整页参数非法）
            var cursor = string.Empty;
            var walked = 0;
            while (true)
            {
                var page = await harness.CreateTimelineService(probe).QueryAsync(scope, new OnlinePlayerTimelineQuery { PageSize = 2, Cursor = cursor });
                Assert.True(page.IsSuccess);
                walked += page.Data.Entries.Count;
                if (!page.Data.Page.HasMore)
                {
                    break;
                }

                cursor = page.Data.Page.Cursor;
            }

            Assert.Equal(wired.Data.Entries.Count, walked);
        }

        /// <summary>
        /// 验证资产腿按玩家维度取数：账本键为 (租户, App, 玩家)、无区服维度，故跨服发起（归属服 ≠ 玩家所在服）的发放
        /// 仍出现在玩家所在服的时间线里——若按归属服过滤，该行在**任何作用域**都取不到（VC-8.1-c 数据一致无缺失）。
        /// </summary>
        [Fact]
        public async Task QueryAsync_AssetLeg_ShouldFollowPlayerNotHomeServer()
        {
            // Arrange
            var harness = new OnlineOverviewTimelineTestHarness();
            var alice = await harness.RegisterPlayerAsync(ServerId, "alice");
            var crossServer = await harness.GrantAsync(OtherServerId, alice.Player.Id, 100, "order-cross-server");
            Assert.NotEqual(ServerId, crossServer.Entries[0].HomeServerId);

            // Act
            var outcome = await harness.CreateTimelineService().QueryAsync(OnlineOverviewTimelineTestHarness.CreateScope(playerId: alice.Player.Id));

            // Assert
            Assert.True(outcome.IsSuccess);
            var row = FindEntry(outcome.Data.Entries, "ledger:" + crossServer.Entries[0].EntryId);
            Assert.Equal(OnlinePlayerTimelineGroup.Asset, row.Group);
            Assert.Equal(crossServer.TransactionId, row.CorrelationId);
        }

        /// <summary>
        /// 验证反预言：他服玩家的档案不在本作用域 → 整条时间线返回空行集（不是「有处罚但无身份」的半截视图），
        /// 与「该玩家不存在」**同构**（两者返回完全一致的页形状）。
        /// </summary>
        [Fact]
        public async Task QueryAsync_CrossScope_ShouldBeIndistinguishableFromUnknownPlayer()
        {
            // Arrange
            var harness = new OnlineOverviewTimelineTestHarness();
            var dave = await harness.RegisterPlayerAsync(OtherServerId, "dave");
            await harness.PunishAsync(dave.Player.Id, "他服处罚");

            // 前置事实：该玩家在处罚读取面确实有记录（处罚没有区服维度，扣掉作用域门就会泄露存在性）
            var punishments = await harness.SocialGraphStore.ListPunishmentsByPlayerAsync(TenantId, AppId, dave.Player.Id);
            Assert.NotEmpty(punishments);

            var service = harness.CreateTimelineService();

            // Act
            var foreign = await service.QueryAsync(OnlineOverviewTimelineTestHarness.CreateScope(playerId: dave.Player.Id));
            var unknown = await service.QueryAsync(OnlineOverviewTimelineTestHarness.CreateScope(playerId: 999999));

            // Assert
            Assert.True(foreign.IsSuccess);
            Assert.Empty(foreign.Data.Entries);
            Assert.False(foreign.Data.Page.HasMore);
            Assert.Equal(string.Empty, foreign.Data.Page.Cursor);

            Assert.True(unknown.IsSuccess);
            Assert.Empty(unknown.Data.Entries);
            Assert.False(unknown.Data.Page.HasMore);
            Assert.Equal(string.Empty, unknown.Data.Page.Cursor);
        }

        /// <summary>
        /// 验证入参校验：玩家主体位缺失、页大小越界、游标非法一律参数非法；页大小小于 1 回落默认值；
        /// 作用域为空引用抛参数异常。
        /// </summary>
        [Fact]
        public async Task QueryAsync_InvalidInput_ShouldReturnParameterInvalid()
        {
            // Arrange
            var harness = new OnlineOverviewTimelineTestHarness();
            var alice = await harness.RegisterPlayerAsync(ServerId, "alice");
            var service = harness.CreateTimelineService();
            var scope = OnlineOverviewTimelineTestHarness.CreateScope(playerId: alice.Player.Id);

            // Act
            var noPlayer = await service.QueryAsync(OnlineOverviewTimelineTestHarness.CreateScope());
            var noServer = await service.QueryAsync(new OnlineScope(TenantId, AppId, 0, alice.Player.Id));
            var tooMany = await service.QueryAsync(scope, new OnlinePlayerTimelineQuery { PageSize = 201 });
            var badCursor = await service.QueryAsync(scope, new OnlinePlayerTimelineQuery { Cursor = "not-a-cursor" });
            var emptyEventId = await service.QueryAsync(scope, new OnlinePlayerTimelineQuery { Cursor = "1800000000|" });
            var defaulted = await service.QueryAsync(scope, new OnlinePlayerTimelineQuery { PageSize = 0 });

            // Assert
            Assert.Equal(OnlineErrorCode.ParameterInvalid, noPlayer.Code);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, noServer.Code);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, tooMany.Code);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, badCursor.Code);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, emptyEventId.Code);
            Assert.True(defaulted.IsSuccess);
            Assert.True(defaulted.Data.Entries.Count <= 50);
            await Assert.ThrowsAsync<ArgumentNullException>(() => service.QueryAsync(null));
        }

        /// <summary>
        /// 按行标识取出时间线行。
        /// </summary>
        /// <param name="entries">行集合。</param>
        /// <param name="eventId">行标识。</param>
        /// <returns>匹配到的行。</returns>
        private static OnlinePlayerTimelineEntry FindEntry(IReadOnlyList<OnlinePlayerTimelineEntry> entries, string eventId)
        {
            foreach (var entry in entries)
            {
                if (entry.EventId == eventId)
                {
                    return entry;
                }
            }

            Assert.Fail("时间线中未找到行 " + eventId);
            return null;
        }

        /// <summary>
        /// 按现有顺序提取行标识序列（用于分页与全量逐项比对）。
        /// </summary>
        /// <param name="entries">行集合。</param>
        /// <returns>行标识序列。</returns>
        private static List<string> CollectEventIds(IReadOnlyList<OnlinePlayerTimelineEntry> entries)
        {
            var ids = new List<string>(entries.Count);
            foreach (var entry in entries)
            {
                ids.Add(entry.EventId);
            }

            return ids;
        }
    }
}
