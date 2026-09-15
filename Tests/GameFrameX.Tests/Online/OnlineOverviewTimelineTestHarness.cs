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
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Identity;
using GameFrameX.Online.Idempotency;
using GameFrameX.Online.Match;
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Overview;
using GameFrameX.Online.Party;
using GameFrameX.Online.Presence;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Session;
using GameFrameX.Online.Social;
using GameFrameX.Online.Timeline;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 在线总览 / 玩家时间线测试基座（C101）：用真实域服务 + 内存存储搭起两条只读查询链路所需的全部数据源。
    /// <para>
    /// 维护约束：基座不复制域内规则——造数一律走各域真实写入链路（身份解析登录、会话签发、Presence 状态机、
    /// 队伍创建、票据入队、资产发放、处罚下发、对局落档），保证被测查询读到的是域内真实写入的记录；
    /// 若域内写入口径变化，本基座造出的数据随之变化，测试会立刻暴露口径漂移。
    /// </para>
    /// </summary>
    internal sealed class OnlineOverviewTimelineTestHarness
    {
        /// <summary>测试用租户标识。</summary>
        internal const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        internal const long AppId = 10;

        /// <summary>测试用区服标识。</summary>
        internal const long ServerId = 100;

        /// <summary>测试用其它区服标识（跨作用域用例）。</summary>
        internal const long OtherServerId = 200;

        /// <summary>测试用 Admin 标识（处罚下发 / 撤销的必填操作者，须为正数）。</summary>
        internal const long AdminId = 9001;

        /// <summary>获取在线状态存储（Presence 域写入链路共用）。</summary>
        internal InMemoryOnlinePresenceStore PresenceStore
        {
            get;
        } = new InMemoryOnlinePresenceStore();

        /// <summary>获取会话存储。</summary>
        internal InMemoryOnlineSessionStore SessionStore
        {
            get;
        } = new InMemoryOnlineSessionStore();

        /// <summary>获取队伍存储。</summary>
        internal InMemoryOnlinePartyStore PartyStore
        {
            get;
        } = new InMemoryOnlinePartyStore();

        /// <summary>获取匹配票据存储（票据与分配记录）。</summary>
        internal InMemoryOnlineMatchTicketStore TicketStore
        {
            get;
        } = new InMemoryOnlineMatchTicketStore();

        /// <summary>获取对局存储。</summary>
        internal InMemoryOnlineMatchActorStore MatchStore
        {
            get;
        } = new InMemoryOnlineMatchActorStore();

        /// <summary>获取身份存储。</summary>
        internal InMemoryOnlineIdentityStore IdentityStore
        {
            get;
        } = new InMemoryOnlineIdentityStore();

        /// <summary>获取资产存储（钱包 / 库存 / 账本）。</summary>
        internal InMemoryOnlineAssetStore AssetStore
        {
            get;
        } = new InMemoryOnlineAssetStore();

        /// <summary>获取资产交易存储。</summary>
        internal InMemoryOnlineAssetTransactionStore TransactionStore
        {
            get;
        } = new InMemoryOnlineAssetTransactionStore();

        /// <summary>获取社交关系存储（含处罚读取面）。</summary>
        internal InMemoryOnlineSocialGraphStore SocialGraphStore
        {
            get;
        } = new InMemoryOnlineSocialGraphStore();

        /// <summary>获取事件记录器（同时充当各域写入链路的事件出口）。</summary>
        internal OnlineEventRecorder Recorder
        {
            get;
        } = new OnlineEventRecorder();

        /// <summary>
        /// 构造在线总览服务。
        /// </summary>
        /// <returns>总览服务实例。</returns>
        internal OnlineOverviewService CreateOverviewService()
        {
            return new OnlineOverviewService(PresenceStore, SessionStore, PartyStore, TicketStore, MatchStore);
        }

        /// <summary>
        /// 构造玩家时间线服务。
        /// </summary>
        /// <param name="configHitProbe">配置命中探针（可空 = 未装配，该腿空槽）。</param>
        /// <returns>时间线服务实例。</returns>
        internal OnlinePlayerTimelineService CreateTimelineService(IOnlineConfigHitProbe configHitProbe = null)
        {
            return new OnlinePlayerTimelineService(IdentityStore, SessionStore, new OnlineAssetQueryService(AssetStore, TransactionStore), MatchStore, SocialGraphStore, configHitProbe);
        }

        /// <summary>
        /// 构造玩家作用域。
        /// </summary>
        /// <param name="serverId">区服标识（默认本测试区服）。</param>
        /// <param name="playerId">玩家标识（默认 0 = 不含玩家主体位）。</param>
        /// <returns>作用域实例。</returns>
        internal static OnlineScope CreateScope(long serverId = ServerId, long playerId = 0)
        {
            return new OnlineScope(TenantId, AppId, serverId, playerId);
        }

        /// <summary>
        /// 注册玩家（身份域真实链路：解析登录 → 自动创建账号 / 玩家档案 / 用户名身份）。
        /// </summary>
        /// <param name="serverId">区服标识。</param>
        /// <param name="userName">用户名（同作用域内唯一）。</param>
        /// <returns>登录解析结果（身份 / 账号 / 玩家档案三件套）。</returns>
        internal async Task<OnlineLoginResolution> RegisterPlayerAsync(long serverId, string userName)
        {
            var outcome = await new OnlineIdentityService(IdentityStore).ResolveLoginAsync(TenantId, AppId, serverId, OnlineIdentityKind.UserName, userName);
            Assert.True(outcome.IsSuccess);
            return outcome.Data;
        }

        /// <summary>
        /// 开一条玩家会话并推进到目标状态。
        /// </summary>
        /// <param name="serverId">区服标识。</param>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="reconnecting">是否推进到「重连中」态（否则停在活跃态）。</param>
        /// <returns>会话记录。</returns>
        internal async Task<OnlineSession> OpenSessionAsync(long serverId, long playerId, bool reconnecting = false)
        {
            var issued = await new OnlineSessionTokenService(SessionStore, Recorder).IssueSessionAsync(CreateScope(serverId, playerId), 3600, OnlineMultiDevicePolicy.LatestWins, "dev-" + playerId);
            Assert.True(issued.IsSuccess);

            var sessionId = issued.Data.Session.Id;
            var manager = new OnlineSessionManager(SessionStore, Recorder);
            Assert.True((await manager.MarkConnectedAsync(sessionId, "conn-" + playerId)).IsSuccess);
            Assert.True((await manager.MarkActiveAsync(sessionId)).IsSuccess);
            if (reconnecting)
            {
                Assert.True((await manager.MarkDisconnectedAsync(sessionId, 60)).IsSuccess);
            }

            return await SessionStore.FindAsync(sessionId);
        }

        /// <summary>
        /// 把玩家置为在线（Presence 域真实写入链路）。
        /// </summary>
        /// <param name="serverId">区服标识。</param>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="sessionId">承载会话标识。</param>
        /// <returns>异步任务。</returns>
        internal async Task SetOnlineAsync(long serverId, long playerId, string sessionId)
        {
            var outcome = await new OnlinePresenceService(PresenceStore, Recorder).SetOnlineAsync(CreateScope(serverId, playerId), sessionId);
            Assert.True(outcome.IsSuccess);
        }

        /// <summary>
        /// 阻塞玩家在线状态（风控限制；须先置为在线）。
        /// </summary>
        /// <param name="serverId">区服标识。</param>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>异步任务。</returns>
        internal async Task BlockAsync(long serverId, long playerId)
        {
            var outcome = await new OnlinePresenceService(PresenceStore, Recorder).MarkBlockedAsync(CreateScope(serverId, playerId));
            Assert.True(outcome.IsSuccess);
        }

        /// <summary>
        /// 建一支队伍（队长即首位成员）。
        /// </summary>
        /// <param name="serverId">区服标识。</param>
        /// <param name="leaderId">队长玩家标识。</param>
        /// <returns>队伍记录。</returns>
        internal async Task<OnlineParty> CreatePartyAsync(long serverId, long leaderId)
        {
            var outcome = await new OnlinePartyService(PartyStore, Recorder, 2, 8, 120, 1800).CreateAsync(CreateScope(serverId, leaderId));
            Assert.True(outcome.IsSuccess);
            return outcome.Data;
        }

        /// <summary>
        /// 解散队伍（终态）。
        /// </summary>
        /// <param name="serverId">区服标识。</param>
        /// <param name="leaderId">队长玩家标识。</param>
        /// <param name="partyId">队伍标识。</param>
        /// <returns>异步任务。</returns>
        internal async Task DisbandPartyAsync(long serverId, long leaderId, string partyId)
        {
            var outcome = await new OnlinePartyService(PartyStore, Recorder, 2, 8, 120, 1800).DisbandAsync(CreateScope(serverId, leaderId), partyId);
            Assert.True(outcome.IsSuccess);
        }

        /// <summary>
        /// 入队一张单人匹配票据。
        /// </summary>
        /// <param name="serverId">区服标识。</param>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="mode">玩法模式。</param>
        /// <param name="region">区域。</param>
        /// <returns>入队后的票据。</returns>
        internal async Task<OnlineMatchTicket> EnqueueAsync(long serverId, long playerId, int mode = 1, int region = 1)
        {
            var request = new OnlineMatchTicketEnqueueRequest
            {
                PartyId = string.Empty,
                PlayerIds = new List<long> { playerId },
                Mode = mode,
                Region = region,
                SkillRange = new OnlineMatchSkillRange { Min = 100, Max = 200 },
                TeamSize = 2,
                LatencyRequirement = 0,
                CustomProperties = new Dictionary<string, string>(),
            };

            var outcome = await new OnlineMatchTicketService(TicketStore, Recorder).EnqueueAsync(CreateScope(serverId, playerId), request);
            Assert.True(outcome.IsSuccess);
            return outcome.Data;
        }

        /// <summary>
        /// 跑一轮匹配撮合（产生匹配分配记录 = 队列吞吐口径的来源）。
        /// </summary>
        /// <param name="serverId">区服标识。</param>
        /// <returns>异步任务。</returns>
        internal async Task RunMatchmakerAsync(long serverId)
        {
            var tickets = new OnlineMatchTicketService(TicketStore, Recorder);
            var coordinator = new OnlineMatchmakerCoordinator(TicketStore, tickets, Recorder);
            var outcome = await coordinator.RunOnceAsync(TenantId, AppId);
            Assert.True(outcome.IsSuccess);
        }

        /// <summary>
        /// 落档一场含指定玩家的对局。
        /// </summary>
        /// <param name="serverId">区服标识。</param>
        /// <param name="playerId">玩家标识（作为成员）。</param>
        /// <param name="matchId">对局标识。</param>
        /// <param name="state">对局状态。</param>
        /// <param name="occurredTime">建档 / 状态变更时刻（UTC 毫秒）。</param>
        /// <param name="settled">是否已结算（<c>EndedTime</c> 同步落档）。</param>
        /// <returns>落档后的对局。</returns>
        internal async Task<OnlineMatch> CreateMatchAsync(long serverId, long playerId, string matchId, OnlineMatchState state, long occurredTime, bool settled = false)
        {
            var match = new OnlineMatch
            {
                TenantId = TenantId,
                AppId = AppId,
                ServerId = serverId,
                MatchId = matchId,
                AssignmentId = "assign-" + matchId,
                Mode = 1,
                Region = 1,
                State = state,
                Members = new List<OnlineMatchMember>
                {
                    new OnlineMatchMember
                    {
                        PlayerId = playerId,
                        State = OnlineMatchMemberState.Joined,
                        JoinedTime = occurredTime,
                    },
                },
                CreatedTime = occurredTime,
                StateChangedTime = occurredTime,
                EndedTime = settled ? occurredTime : 0,
            };

            var stored = await MatchStore.CreateAsync(match);
            Assert.NotNull(stored);
            return stored;
        }

        /// <summary>
        /// 发放资产（资产域真实写入链路，产生账本流水）。
        /// </summary>
        /// <param name="serverId">区服标识。</param>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="amount">发放数额。</param>
        /// <param name="businessOrderId">业务单号（同时作为幂等键后缀）。</param>
        /// <returns>发放结果。</returns>
        internal async Task<OnlineGrantResult> GrantAsync(long serverId, long playerId, long amount, string businessOrderId)
        {
            var request = new OnlineGrantRequest(
                CreateScope(serverId, playerId),
                OnlineAssetChangeSource.MailAttachment,
                OnlineGrantOperation.Grant,
                "测试发放",
                businessOrderId,
                null,
                new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", amount) },
                "key-" + businessOrderId);

            var outcome = await CreateGrantService().ExecuteAsync(request);
            Assert.True(outcome.IsSuccess);
            return outcome.Data;
        }

        /// <summary>
        /// 下发一条处罚（处罚域真实写入链路）。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="reason">处罚原因。</param>
        /// <returns>处罚记录。</returns>
        internal async Task<OnlinePunishment> PunishAsync(long playerId, string reason)
        {
            var outcome = await new OnlinePunishmentService(SocialGraphStore, Recorder).ApplyAsync(TenantId, AppId, playerId, OnlinePunishmentKind.Mute, reason, 0, 0, AdminId);
            Assert.True(outcome.IsSuccess);
            return outcome.Data;
        }

        /// <summary>
        /// 撤销处罚（产生撤销行）。
        /// </summary>
        /// <param name="punishmentId">处罚标识。</param>
        /// <returns>异步任务。</returns>
        internal async Task RevokePunishmentAsync(string punishmentId)
        {
            var outcome = await new OnlinePunishmentService(SocialGraphStore, Recorder).RevokeAsync(TenantId, AppId, punishmentId, AdminId);
            Assert.True(outcome.IsSuccess);
        }

        /// <summary>
        /// 构造资产发放服务（幂等组件用最小等待 + 步进时钟，避免测试受真实时钟影响）。
        /// </summary>
        /// <returns>资产发放服务实例。</returns>
        private OnlineGrantService CreateGrantService()
        {
            var options = new IdempotencyOptions
            {
                ConcurrentWaitTimeoutMilliseconds = 1,
            };

            var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), new StepClock(), options);
            return new OnlineGrantService(AssetStore, TransactionStore, new OnlineIdempotencyService(coordinator, null), Recorder, null);
        }

        /// <summary>
        /// 步进时钟（每次读取前进 10 毫秒，保证写入链路产生可区分的时间戳）。
        /// </summary>
        private sealed class StepClock : IClock
        {
            /// <summary>当前毫秒值。</summary>
            private long _nowMilliseconds = 1760000000000;

            /// <summary>获取当前时刻（UTC 毫秒；每次读取自增 10）。</summary>
            public long UtcNowTime
            {
                get
                {
                    _nowMilliseconds += 10;
                    return _nowMilliseconds;
                }
            }
        }

        /// <summary>
        /// 确定性配置命中探针（假实现）。
        /// <para>
        /// 维护约束：按 (租户, App, 区服, 玩家) 四键回放登记过的记录——与真实探针的作用域语义一致，
        /// 使被测服务「把这些键传给探针」这一点可被用例间接验证；不登记即无命中（空集合，不是空指针）。
        /// </para>
        /// </summary>
        internal sealed class FakeConfigHitProbe : IOnlineConfigHitProbe
        {
            /// <summary>已登记的记录（含其归属作用域键）。</summary>
            private readonly List<Entry> _entries = new List<Entry>();

            /// <summary>
            /// 登记一条命中记录。
            /// </summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">App 标识。</param>
            /// <param name="serverId">区服标识。</param>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="record">命中记录。</param>
            internal void Add(long tenantId, long appId, long serverId, long playerId, OnlineConfigHitRecord record)
            {
                _entries.Add(new Entry
                {
                    TenantId = tenantId,
                    AppId = appId,
                    ServerId = serverId,
                    PlayerId = playerId,
                    Record = record,
                });
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<OnlineConfigHitRecord>> ListHitsAsync(long tenantId, long appId, long serverId, long playerId, CancellationToken cancellationToken = default)
            {
                var matched = new List<OnlineConfigHitRecord>();
                foreach (var entry in _entries)
                {
                    if (entry.TenantId == tenantId && entry.AppId == appId && entry.ServerId == serverId && entry.PlayerId == playerId)
                    {
                        matched.Add(entry.Record);
                    }
                }

                return Task.FromResult<IReadOnlyList<OnlineConfigHitRecord>>(matched);
            }

            /// <summary>登记项（记录 + 归属作用域键）。</summary>
            private sealed class Entry
            {
                /// <summary>获取或设置租户标识。</summary>
                public long TenantId
                {
                    get;
                    set;
                }

                /// <summary>获取或设置 App 标识。</summary>
                public long AppId
                {
                    get;
                    set;
                }

                /// <summary>获取或设置区服标识。</summary>
                public long ServerId
                {
                    get;
                    set;
                }

                /// <summary>获取或设置玩家标识。</summary>
                public long PlayerId
                {
                    get;
                    set;
                }

                /// <summary>获取或设置命中记录。</summary>
                public OnlineConfigHitRecord Record
                {
                    get;
                    set;
                }
            }
        }
    }
}
