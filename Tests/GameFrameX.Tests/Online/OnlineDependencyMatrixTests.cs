using System.Text.RegularExpressions;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Competition.Leaderboard;
using GameFrameX.Capability.Online.Competition.Season;
using GameFrameX.Capability.Online.Competition.Tournament;
using GameFrameX.Capability.Online.Foundation.Presence;
using GameFrameX.Capability.Online.Foundation.Session;
using GameFrameX.Capability.Online.Matchmaking;
using GameFrameX.Capability.Online.Matchmaking.Party;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Capability.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// Online 事件 topic 冻结门禁测试（C223 立项，C228 升维，C232 P6 收口，C294 重冻为全小写
    /// <c>online.&lt;域&gt;.&lt;事件&gt;</c> 命名约定）：全部 Online 事件的 topic 常量与来源标识逐字节冻结，
    /// 且运营审计桥订阅面与冻结表一一对应（竞技域 topic 以字面量订阅，须与竞技包常量同值）。
    /// <para>
    /// 维护约束：目录级依赖矩阵已随双工程退役终结——包级方向断言由 <see cref="CapabilityPackageStructureTests"/>
    /// 全量接管，本测试只保留 topic 冻结面。
    /// </para>
    /// </summary>
    public class OnlineDependencyMatrixTests
    {
        /// <summary>
        /// topic 命名约定：<c>online.&lt;域&gt;.&lt;事件&gt;</c>，三段全小写字母（EventType == topic）。
        /// </summary>
        private static readonly Regex TopicConvention = new Regex(@"^online\.[a-z]+\.[a-z]+$");

        /// <summary>
        /// 冻结 topic 表（Member = 常量的「类型名.成员名」，用于把审计桥源码中的常量订阅解析回值；
        /// Actual = 生产常量；Frozen = C294 冻结值）。
        /// </summary>
        private static readonly (string Member, string Actual, string Frozen)[] FrozenTopics =
        {
            (nameof(OnlineAssetEvents) + "." + nameof(OnlineAssetEvents.AssetGranted), OnlineAssetEvents.AssetGranted, "online.asset.granted"),
            (nameof(OnlineAssetEvents) + "." + nameof(OnlineAssetEvents.AssetDeducted), OnlineAssetEvents.AssetDeducted, "online.asset.deducted"),
            (nameof(OnlineAssetEvents) + "." + nameof(OnlineAssetEvents.AssetAdjusted), OnlineAssetEvents.AssetAdjusted, "online.asset.adjusted"),
            (nameof(OnlineChatEvents) + "." + nameof(OnlineChatEvents.MessageSent), OnlineChatEvents.MessageSent, "online.chat.messagesent"),
            (nameof(OnlineChatEvents) + "." + nameof(OnlineChatEvents.MessageRecalled), OnlineChatEvents.MessageRecalled, "online.chat.messagerecalled"),
            (nameof(OnlineChatEvents) + "." + nameof(OnlineChatEvents.ReadMarkUpdated), OnlineChatEvents.ReadMarkUpdated, "online.chat.readmarkupdated"),
            (nameof(OnlineChatEvents) + "." + nameof(OnlineChatEvents.ModerationFailed), OnlineChatEvents.ModerationFailed, "online.chat.moderationfailed"),
            (nameof(OnlineHotfixEvents) + "." + nameof(OnlineHotfixEvents.VersionRegistered), OnlineHotfixEvents.VersionRegistered, "online.hotfix.versionregistered"),
            (nameof(OnlineHotfixEvents) + "." + nameof(OnlineHotfixEvents.RolledBack), OnlineHotfixEvents.RolledBack, "online.hotfix.rolledback"),
            (nameof(OnlineLeaderboardEvents) + "." + nameof(OnlineLeaderboardEvents.LeaderboardCreated), OnlineLeaderboardEvents.LeaderboardCreated, "online.leaderboard.created"),
            (nameof(OnlineLeaderboardEvents) + "." + nameof(OnlineLeaderboardEvents.ScoreUpdated), OnlineLeaderboardEvents.ScoreUpdated, "online.leaderboard.scoreupdated"),
            (nameof(OnlineMatchEvents) + "." + nameof(OnlineMatchEvents.TicketChanged), OnlineMatchEvents.TicketChanged, "online.match.ticketchanged"),
            (nameof(OnlineMatchEvents) + "." + nameof(OnlineMatchEvents.AssignmentCreated), OnlineMatchEvents.AssignmentCreated, "online.match.assignmentcreated"),
            (nameof(GameFrameX.Capability.Online.Match.OnlineMatchRuntimeEvents) + "." + nameof(GameFrameX.Capability.Online.Match.OnlineMatchRuntimeEvents.MatchStateChanged), GameFrameX.Capability.Online.Match.OnlineMatchRuntimeEvents.MatchStateChanged, "online.match.statechanged"),
            (nameof(GameFrameX.Capability.Online.Match.OnlineMatchRuntimeEvents) + "." + nameof(GameFrameX.Capability.Online.Match.OnlineMatchRuntimeEvents.MatchSettled), GameFrameX.Capability.Online.Match.OnlineMatchRuntimeEvents.MatchSettled, "online.match.settled"),
            (nameof(OnlineNotificationEvents) + "." + nameof(OnlineNotificationEvents.NotificationChanged), OnlineNotificationEvents.NotificationChanged, "online.notification.changed"),
            (nameof(OnlinePartyEvents) + "." + nameof(OnlinePartyEvents.PartyChanged), OnlinePartyEvents.PartyChanged, "online.party.changed"),
            (nameof(OnlinePartyEvents) + "." + nameof(OnlinePartyEvents.PartyInviteChanged), OnlinePartyEvents.PartyInviteChanged, "online.party.invitechanged"),
            (nameof(OnlinePresenceEvents) + "." + nameof(OnlinePresenceEvents.PresenceChanged), OnlinePresenceEvents.PresenceChanged, "online.presence.changed"),
            (nameof(OnlineSeasonEvents) + "." + nameof(OnlineSeasonEvents.SeasonCreated), OnlineSeasonEvents.SeasonCreated, "online.season.created"),
            (nameof(OnlineSeasonEvents) + "." + nameof(OnlineSeasonEvents.SeasonStarted), OnlineSeasonEvents.SeasonStarted, "online.season.started"),
            (nameof(OnlineSeasonEvents) + "." + nameof(OnlineSeasonEvents.SeasonEnded), OnlineSeasonEvents.SeasonEnded, "online.season.ended"),
            (nameof(OnlineSeasonEvents) + "." + nameof(OnlineSeasonEvents.SeasonSettled), OnlineSeasonEvents.SeasonSettled, "online.season.settled"),
            (nameof(OnlineSessionEvents) + "." + nameof(OnlineSessionEvents.SessionCreated), OnlineSessionEvents.SessionCreated, "online.session.created"),
            (nameof(OnlineSessionEvents) + "." + nameof(OnlineSessionEvents.SessionAuthenticated), OnlineSessionEvents.SessionAuthenticated, "online.session.authenticated"),
            (nameof(OnlineSessionEvents) + "." + nameof(OnlineSessionEvents.SessionConnected), OnlineSessionEvents.SessionConnected, "online.session.connected"),
            (nameof(OnlineSessionEvents) + "." + nameof(OnlineSessionEvents.SessionActive), OnlineSessionEvents.SessionActive, "online.session.active"),
            (nameof(OnlineSessionEvents) + "." + nameof(OnlineSessionEvents.SessionReconnecting), OnlineSessionEvents.SessionReconnecting, "online.session.reconnecting"),
            (nameof(OnlineSessionEvents) + "." + nameof(OnlineSessionEvents.SessionClosed), OnlineSessionEvents.SessionClosed, "online.session.closed"),
            (nameof(OnlineSessionEvents) + "." + nameof(OnlineSessionEvents.SessionKicked), OnlineSessionEvents.SessionKicked, "online.session.kicked"),
            (nameof(OnlineSessionEvents) + "." + nameof(OnlineSessionEvents.SessionExpired), OnlineSessionEvents.SessionExpired, "online.session.expired"),
            (nameof(OnlineSessionEvents) + "." + nameof(OnlineSessionEvents.SessionTokenRefreshed), OnlineSessionEvents.SessionTokenRefreshed, "online.session.tokenrefreshed"),
            (nameof(OnlineSocialEvents) + "." + nameof(OnlineSocialEvents.FriendshipChanged), OnlineSocialEvents.FriendshipChanged, "online.social.friendshipchanged"),
            (nameof(OnlineSocialEvents) + "." + nameof(OnlineSocialEvents.BlockChanged), OnlineSocialEvents.BlockChanged, "online.social.blockchanged"),
            (nameof(OnlineSocialEvents) + "." + nameof(OnlineSocialEvents.MuteChanged), OnlineSocialEvents.MuteChanged, "online.social.mutechanged"),
            (nameof(OnlineSocialEvents) + "." + nameof(OnlineSocialEvents.ReportChanged), OnlineSocialEvents.ReportChanged, "online.social.reportchanged"),
            (nameof(OnlineSocialEvents) + "." + nameof(OnlineSocialEvents.PunishmentChanged), OnlineSocialEvents.PunishmentChanged, "online.social.punishmentchanged"),
            (nameof(OnlineSocialEvents) + "." + nameof(OnlineSocialEvents.GroupChanged), OnlineSocialEvents.GroupChanged, "online.social.groupchanged"),
            (nameof(OnlineTournamentEvents) + "." + nameof(OnlineTournamentEvents.TournamentCreated), OnlineTournamentEvents.TournamentCreated, "online.tournament.created"),
            (nameof(OnlineTournamentEvents) + "." + nameof(OnlineTournamentEvents.TournamentStarted), OnlineTournamentEvents.TournamentStarted, "online.tournament.started"),
            (nameof(OnlineTournamentEvents) + "." + nameof(OnlineTournamentEvents.TournamentRegistered), OnlineTournamentEvents.TournamentRegistered, "online.tournament.registered"),
            (nameof(OnlineTournamentEvents) + "." + nameof(OnlineTournamentEvents.TournamentEnded), OnlineTournamentEvents.TournamentEnded, "online.tournament.ended"),
            (nameof(OnlineTournamentEvents) + "." + nameof(OnlineTournamentEvents.TournamentSettled), OnlineTournamentEvents.TournamentSettled, "online.tournament.settled"),
        };

        /// <summary>
        /// 冻结来源标识表（Actual = 生产常量；Frozen = 冻结值；来源标识不随 topic 重命名变化）。
        /// </summary>
        private static readonly (string Actual, string Frozen)[] FrozenSources =
        {
            (OnlineAssetEvents.Source, "online-assets"),
            (OnlineChatEvents.Source, "online-chat"),
            (OnlineHotfixEvents.Source, "online-hotfix"),
            (OnlineLeaderboardEvents.Source, "online-leaderboard"),
            (OnlineMatchEvents.Source, "online-matchmaker"),
            (GameFrameX.Capability.Online.Match.OnlineMatchRuntimeEvents.Source, "online-match"),
            (OnlineNotificationEvents.Source, "online-notification"),
            (OnlinePartyEvents.Source, "online-party"),
            (OnlinePresenceEvents.Source, "online-presence"),
            (OnlineSeasonEvents.Source, "online-season"),
            (OnlineSessionEvents.Source, "online-session"),
            (OnlineSocialEvents.Source, "online-social"),
            (OnlineTournamentEvents.Source, "online-tournament"),
        };

        /// <summary>
        /// 断言事件 topic 常量与来源标识逐字节冻结（C294 重冻基线：topic 全小写三段，来源标识沿用迁移前基线）。
        /// </summary>
        [Fact]
        public void EventTopicConstants_RemainByteFrozen()
        {
            foreach (var (member, actual, frozen) in FrozenTopics)
            {
                Assert.True(string.Equals(frozen, actual, StringComparison.Ordinal), $"{member} 偏离冻结值：期望 {frozen}，实际 {actual}");
            }

            foreach (var (actual, frozen) in FrozenSources)
            {
                Assert.Equal(frozen, actual);
            }
        }

        /// <summary>
        /// 断言每个冻结 topic 符合 <c>online.&lt;域&gt;.&lt;事件&gt;</c> 全小写命名约定，且 topic 之间无重复
        /// （订阅方按 topic 精确匹配，大小写或重名漂移会让订阅静默失效）。
        /// </summary>
        [Fact]
        public void FrozenTopics_FollowLowercaseThreeSegmentConvention()
        {
            var violations = FrozenTopics.Where(topic => !TopicConvention.IsMatch(topic.Frozen)).Select(topic => topic.Member + " = " + topic.Frozen).ToList();
            Assert.True(violations.Count == 0, "topic 不符合 online.<域>.<事件> 全小写约定：" + Environment.NewLine + string.Join(Environment.NewLine, violations));

            var duplicates = FrozenTopics.GroupBy(topic => topic.Frozen, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
            Assert.True(duplicates.Count == 0, "topic 重复：" + string.Join(", ", duplicates));
        }

        /// <summary>
        /// 断言运营审计桥的竞技域字面量订阅与竞技包常量同值（Operations 不引用 Competition 程序集，
        /// 只能以字面量订阅；常量改名而字面量未同步 = 竞技事件静默漏审计）。
        /// </summary>
        [Fact]
        public void OperationsAuditBridge_CompetitionLiterals_EqualCompetitionConstants()
        {
            var subscriptions = ReadOperationsAuditSubscriptions();
            var literals = subscriptions.Where(subscription => subscription.Literal != null)
                .Select(subscription => subscription.Literal)
                .OrderBy(topic => topic, StringComparer.Ordinal)
                .ToArray();
            var competitionConstants = new[]
                {
                    OnlineLeaderboardEvents.LeaderboardCreated,
                    OnlineLeaderboardEvents.ScoreUpdated,
                    OnlineSeasonEvents.SeasonCreated,
                    OnlineSeasonEvents.SeasonStarted,
                    OnlineSeasonEvents.SeasonEnded,
                    OnlineSeasonEvents.SeasonSettled,
                    OnlineTournamentEvents.TournamentCreated,
                    OnlineTournamentEvents.TournamentStarted,
                    OnlineTournamentEvents.TournamentRegistered,
                    OnlineTournamentEvents.TournamentEnded,
                    OnlineTournamentEvents.TournamentSettled,
                }
                .OrderBy(topic => topic, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(competitionConstants, literals);
        }

        /// <summary>
        /// 断言运营审计桥的订阅面恰好覆盖全部冻结 topic（每个 topic 一次，无漏订无多订；
        /// 常量订阅按「类型名.成员名」解析回冻结值，字面量订阅直接取值）。
        /// </summary>
        [Fact]
        public void OperationsAuditBridge_SubscribesEveryFrozenTopicExactlyOnce()
        {
            var valueByMember = FrozenTopics.ToDictionary(topic => topic.Member, topic => topic.Actual, StringComparer.Ordinal);
            var subscribed = new List<string>();
            var unresolved = new List<string>();
            foreach (var subscription in ReadOperationsAuditSubscriptions())
            {
                if (subscription.Literal != null)
                {
                    subscribed.Add(subscription.Literal);
                }
                else if (valueByMember.TryGetValue(subscription.Member, out var value))
                {
                    subscribed.Add(value);
                }
                else
                {
                    unresolved.Add(subscription.Member);
                }
            }

            Assert.True(unresolved.Count == 0, "审计桥订阅了冻结表之外的 topic 常量：" + string.Join(", ", unresolved));
            Assert.Equal(
                FrozenTopics.Select(topic => topic.Frozen).OrderBy(topic => topic, StringComparer.Ordinal).ToArray(),
                subscribed.OrderBy(topic => topic, StringComparer.Ordinal).ToArray());
        }

        /// <summary>
        /// 扫描运营能力模块源码中的审计桥订阅（<c>context.Subscribe&lt;OnlineEvent&gt;(topic, ...)</c>），
        /// 返回每个订阅的字面量 topic 或常量「类型名.成员名」。
        /// </summary>
        /// <returns>订阅集合（Literal 非 null = 字面量订阅；否则 Member 为常量引用）。</returns>
        private static List<(string Literal, string Member)> ReadOperationsAuditSubscriptions()
        {
            var moduleFile = Path.Combine(FindRepositoryRoot(), "Capability", "GameFrameX.Capability.Online.Operations", "OnlineOperationsCapabilityModule.cs");
            Assert.True(File.Exists(moduleFile), "未找到运营能力模块源码：" + moduleFile);
            var code = new Regex(@"//[^\n]*").Replace(File.ReadAllText(moduleFile), string.Empty);
            var subscribePattern = new Regex(@"context\.Subscribe<OnlineEvent>\(\s*(?:""([^""]*)""|([A-Za-z_]\w*)\.([A-Za-z_]\w*))\s*,");
            var subscriptions = new List<(string Literal, string Member)>();
            foreach (var match in subscribePattern.Matches(code).Cast<System.Text.RegularExpressions.Match>())
            {
                if (match.Groups[1].Success)
                {
                    subscriptions.Add((match.Groups[1].Value, null));
                }
                else
                {
                    subscriptions.Add((null, match.Groups[2].Value + "." + match.Groups[3].Value));
                }
            }

            Assert.NotEmpty(subscriptions);
            return subscriptions;
        }

        /// <summary>
        /// 从测试程序集位置向上定位仓库根（含 Server.slnx 的目录）。
        /// </summary>
        /// <returns>仓库根路径。</returns>
        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Server.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            Assert.Fail("未定位到仓库根（测试须在仓库内运行）");
            return string.Empty;
        }
    }
}
