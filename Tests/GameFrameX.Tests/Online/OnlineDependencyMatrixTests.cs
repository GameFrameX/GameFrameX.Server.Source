using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// Online 事件 topic 冻结门禁测试（C223 立项，C228 升维，C232 P6 收口）：全部 Online 事件的
    /// topic 常量与来源标识逐字节冻结（零行为变更红线）。
    /// <para>
    /// 维护约束：目录级依赖矩阵已随双工程退役终结（C232 P5 停摆、P6 删除 <c>GameFrameX.Online</c>
    /// 残量目录后正式退役）——包级方向断言由 <see cref="CapabilityPackageStructureTests"/> 全量接管，
    /// 本测试只保留 topic 冻结面。
    /// </para>
    /// </summary>
    public class OnlineDependencyMatrixTests
    {
        /// <summary>
        /// 断言事件 topic 常量与来源标识逐字节冻结（C223 零行为变更红线；值 = 2026-10-12 迁移前基线）。
        /// </summary>
        [Fact]
        public void EventTopicConstants_RemainByteFrozen()
        {
            var expected = new (string Actual, string Frozen)[]
            {
                (GameFrameX.Capability.Online.Assets.OnlineAssetEvents.AssetGranted, "Online.Asset.Granted"),
                (GameFrameX.Capability.Online.Assets.OnlineAssetEvents.AssetDeducted, "Online.Asset.Deducted"),
                (GameFrameX.Capability.Online.Assets.OnlineAssetEvents.AssetAdjusted, "Online.Asset.Adjusted"),
                (GameFrameX.Capability.Online.Assets.OnlineAssetEvents.Source, "online-assets"),
                (GameFrameX.Capability.Online.Social.OnlineChatEvents.MessageSent, "Online.Chat.MessageSent"),
                (GameFrameX.Capability.Online.Social.OnlineChatEvents.MessageRecalled, "Online.Chat.MessageRecalled"),
                (GameFrameX.Capability.Online.Social.OnlineChatEvents.ReadMarkUpdated, "Online.Chat.ReadMarkUpdated"),
                (GameFrameX.Capability.Online.Social.OnlineChatEvents.ModerationFailed, "Online.Chat.ModerationFailed"),
                (GameFrameX.Capability.Online.Social.OnlineChatEvents.Source, "online-chat"),
                (GameFrameX.Capability.Online.Operations.HotfixRollback.OnlineHotfixEvents.VersionRegistered, "Online.Hotfix.VersionRegistered"),
                (GameFrameX.Capability.Online.Operations.HotfixRollback.OnlineHotfixEvents.RolledBack, "Online.Hotfix.RolledBack"),
                (GameFrameX.Capability.Online.Operations.HotfixRollback.OnlineHotfixEvents.Source, "online-hotfix"),
                (GameFrameX.Capability.Online.Competition.Leaderboard.OnlineLeaderboardEvents.LeaderboardCreated, "Online.Leaderboard.Created"),
                (GameFrameX.Capability.Online.Competition.Leaderboard.OnlineLeaderboardEvents.ScoreUpdated, "Online.Leaderboard.ScoreUpdated"),
                (GameFrameX.Capability.Online.Competition.Leaderboard.OnlineLeaderboardEvents.Source, "online-leaderboard"),
                (GameFrameX.Capability.Online.Matchmaking.OnlineMatchEvents.TicketChanged, "Online.Match.TicketChanged"),
                (GameFrameX.Capability.Online.Matchmaking.OnlineMatchEvents.AssignmentCreated, "Online.Match.AssignmentCreated"),
                (GameFrameX.Capability.Online.Matchmaking.OnlineMatchEvents.Source, "online-matchmaker"),
                (GameFrameX.Capability.Online.Match.OnlineMatchRuntimeEvents.MatchStateChanged, "Online.Match.StateChanged"),
                (GameFrameX.Capability.Online.Match.OnlineMatchRuntimeEvents.MatchSettled, "Online.Match.Settled"),
                (GameFrameX.Capability.Online.Match.OnlineMatchRuntimeEvents.Source, "online-match"),
                (GameFrameX.Capability.Online.Social.OnlineNotificationEvents.NotificationChanged, "Online.Notification.Changed"),
                (GameFrameX.Capability.Online.Social.OnlineNotificationEvents.Source, "online-notification"),
                (GameFrameX.Capability.Online.Matchmaking.Party.OnlinePartyEvents.PartyChanged, "Online.Party.Changed"),
                (GameFrameX.Capability.Online.Matchmaking.Party.OnlinePartyEvents.PartyInviteChanged, "Online.Party.InviteChanged"),
                (GameFrameX.Capability.Online.Matchmaking.Party.OnlinePartyEvents.Source, "online-party"),
                (GameFrameX.Capability.Online.Foundation.Presence.OnlinePresenceEvents.PresenceChanged, "Online.Presence.Changed"),
                (GameFrameX.Capability.Online.Foundation.Presence.OnlinePresenceEvents.Source, "online-presence"),
                (GameFrameX.Capability.Online.Competition.Season.OnlineSeasonEvents.SeasonCreated, "Online.Season.Created"),
                (GameFrameX.Capability.Online.Competition.Season.OnlineSeasonEvents.SeasonStarted, "Online.Season.Started"),
                (GameFrameX.Capability.Online.Competition.Season.OnlineSeasonEvents.SeasonEnded, "Online.Season.Ended"),
                (GameFrameX.Capability.Online.Competition.Season.OnlineSeasonEvents.SeasonSettled, "Online.Season.Settled"),
                (GameFrameX.Capability.Online.Competition.Season.OnlineSeasonEvents.Source, "online-season"),
                (GameFrameX.Capability.Online.Foundation.Session.OnlineSessionEvents.SessionCreated, "Online.Session.Created"),
                (GameFrameX.Capability.Online.Foundation.Session.OnlineSessionEvents.SessionAuthenticated, "Online.Session.Authenticated"),
                (GameFrameX.Capability.Online.Foundation.Session.OnlineSessionEvents.SessionConnected, "Online.Session.Connected"),
                (GameFrameX.Capability.Online.Foundation.Session.OnlineSessionEvents.SessionActive, "Online.Session.Active"),
                (GameFrameX.Capability.Online.Foundation.Session.OnlineSessionEvents.SessionReconnecting, "Online.Session.Reconnecting"),
                (GameFrameX.Capability.Online.Foundation.Session.OnlineSessionEvents.SessionClosed, "Online.Session.Closed"),
                (GameFrameX.Capability.Online.Foundation.Session.OnlineSessionEvents.SessionKicked, "Online.Session.Kicked"),
                (GameFrameX.Capability.Online.Foundation.Session.OnlineSessionEvents.SessionExpired, "Online.Session.Expired"),
                (GameFrameX.Capability.Online.Foundation.Session.OnlineSessionEvents.SessionTokenRefreshed, "Online.Session.TokenRefreshed"),
                (GameFrameX.Capability.Online.Foundation.Session.OnlineSessionEvents.Source, "online-session"),
                (GameFrameX.Capability.Online.Social.OnlineSocialEvents.FriendshipChanged, "Online.Social.FriendshipChanged"),
                (GameFrameX.Capability.Online.Social.OnlineSocialEvents.BlockChanged, "Online.Social.BlockChanged"),
                (GameFrameX.Capability.Online.Social.OnlineSocialEvents.MuteChanged, "Online.Social.MuteChanged"),
                (GameFrameX.Capability.Online.Social.OnlineSocialEvents.ReportChanged, "Online.Social.ReportChanged"),
                (GameFrameX.Capability.Online.Social.OnlineSocialEvents.PunishmentChanged, "Online.Social.PunishmentChanged"),
                (GameFrameX.Capability.Online.Social.OnlineSocialEvents.GroupChanged, "Online.Social.GroupChanged"),
                (GameFrameX.Capability.Online.Social.OnlineSocialEvents.Source, "online-social"),
                (GameFrameX.Capability.Online.Competition.Tournament.OnlineTournamentEvents.TournamentCreated, "Online.Tournament.Created"),
                (GameFrameX.Capability.Online.Competition.Tournament.OnlineTournamentEvents.TournamentStarted, "Online.Tournament.Started"),
                (GameFrameX.Capability.Online.Competition.Tournament.OnlineTournamentEvents.TournamentRegistered, "Online.Tournament.Registered"),
                (GameFrameX.Capability.Online.Competition.Tournament.OnlineTournamentEvents.TournamentEnded, "Online.Tournament.Ended"),
                (GameFrameX.Capability.Online.Competition.Tournament.OnlineTournamentEvents.TournamentSettled, "Online.Tournament.Settled"),
                (GameFrameX.Capability.Online.Competition.Tournament.OnlineTournamentEvents.Source, "online-tournament"),
            };
            foreach (var (actual, frozen) in expected)
            {
                Assert.Equal(frozen, actual);
            }
        }
    }
}
