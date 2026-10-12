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
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
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

using System.Text.RegularExpressions;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// Online 依赖矩阵门禁测试（C223）：扫描 <c>GameFrameX.Online</c> 全部源码的 using 指令
    /// （含外围命名空间相对 using 的语义解析），按允许方向表断言目录依赖为严格向下 DAG。
    /// <para>
    /// 维护约束：新增跨目录引用必须先修改 <see cref="AllowedEdges"/> 允许方向表，否则本测试红；
    /// 同层横向（叶子能力簇 Assets/Social/Matchmaking/Party 互引）恒为零；
    /// 事件 topic 常量与来源标识冻结（零行为变更红线）。
    /// </para>
    /// </summary>
    public class OnlineDependencyMatrixTests
    {
        /// <summary>
        /// GameFrameX.Online 工程目录名（用于从测试程序集定位源码根）。
        /// </summary>
        private const string OnlineProjectDirectory = "GameFrameX.Online";

        /// <summary>
        /// 受矩阵治理的目录全集（22 个，与包族拓扑一一对应）。
        /// </summary>
        private static readonly HashSet<string> Directories = new()
        {
            "Assets", "Audit", "Contracts", "Events", "GameEvents", "HotfixRollback", "Idempotency",
            "Identity", "Leaderboard", "Match", "Matchmaking", "Overview", "Party", "Presence",
            "Scope", "Season", "Session", "Social", "Storage", "Timeline", "Tokens", "Tournament",
        };

        /// <summary>
        /// 允许方向表（src → 允许引用的目录集合）。镜像目标包族拓扑：
        /// 脊柱（Contracts/Scope/Idempotency/Tokens/Events）→ 地基（Identity/Session/Presence/Storage，
        /// 簇内允许）→ 叶子肢体（Assets/Social/Matchmaking/Party，互引恒零）→ 上层肢体
        /// （Match；Competition 簇 Leaderboard/Season/Tournament 簇内允许）→ 聚合
        /// （Audit/HotfixRollback/GameEvents/Timeline/Overview，Operations 簇内允许）。
        /// </summary>
        private static readonly Dictionary<string, string[]> AllowedEdges = new()
        {
            ["Scope"] = new[] { "Contracts" },
            ["Idempotency"] = new[] { "Contracts", "Scope" },
            ["Tokens"] = new[] { "Scope" },
            ["Events"] = Array.Empty<string>(),
            ["Identity"] = new[] { "Contracts", "Scope" },
            ["Session"] = new[] { "Contracts", "Events", "Identity", "Scope", "Tokens" },
            ["Presence"] = new[] { "Contracts", "Events", "Scope" },
            ["Storage"] = new[] { "Contracts", "Scope" },
            ["Assets"] = new[] { "Contracts", "Events", "Idempotency", "Scope" },
            ["Social"] = new[] { "Contracts", "Events", "Scope" },
            ["Matchmaking"] = new[] { "Contracts", "Events", "Scope" },
            ["Party"] = new[] { "Contracts", "Events", "Scope" },
            ["Match"] = new[] { "Assets", "Contracts", "Events", "Matchmaking", "Scope" },
            ["Leaderboard"] = new[] { "Contracts", "Events", "Idempotency", "Match", "Scope" },
            ["Season"] = new[] { "Assets", "Contracts", "Events", "Leaderboard", "Scope" },
            ["Tournament"] = new[] { "Assets", "Contracts", "Events", "Leaderboard", "Scope" },
            ["Audit"] = new[] { "Contracts", "Events", "Scope" },
            ["HotfixRollback"] = new[] { "Audit", "Contracts", "Events", "Idempotency", "Scope" },
            ["GameEvents"] = new[] { "Assets", "Contracts", "Events", "Match", "Matchmaking", "Session", "Social" },
            ["Timeline"] = new[] { "Assets", "Contracts", "Events", "Identity", "Match", "Scope", "Session", "Social" },
            ["Overview"] = new[] { "Contracts", "Match", "Matchmaking", "Party", "Presence", "Scope", "Session" },
        };

        /// <summary>
        /// 叶子能力簇目录（包族中互为独立叶子包，横向互引恒为零）。
        /// </summary>
        private static readonly string[] LeafCapabilityDirectories = { "Assets", "Social", "Matchmaking", "Party" };

        /// <summary>
        /// 过渡期豁免名单（解环看板）：每条 = 一条现存 Events→域环边；
        /// 对应环解除后必须同步删除豁免行（测试对「豁免仍被引用」做强校验，防止看板腐化）。
        /// 终态（C223 T5）= 空集。
        /// </summary>
        private static readonly HashSet<(string From, string To)> Exemptions = new()
        {
        };

        /// <summary>
        /// 断言目录依赖矩阵无表外引用、豁免名单无腐化。
        /// </summary>
        [Fact]
        public void DirectoryDependencyMatrix_RemainsDownwardDagBeyondExemptions()
        {
            var edges = CollectDirectoryEdges();
            var violations = new List<string>();
            foreach (var edge in edges)
            {
                if (Exemptions.Contains((edge.From, edge.To)))
                {
                    continue;
                }

                if (!AllowedEdges.TryGetValue(edge.From, out var allowed) || !allowed.Contains(edge.To))
                {
                    violations.Add($"{edge.From} -> {edge.To}（{edge.Location}）");
                }
            }

            var stale = edges.Count == 0
                ? Exemptions.Select(exemption => $"{exemption.From} -> {exemption.To}").ToList()
                : Exemptions.Where(exemption => !edges.Any(edge => edge.From == exemption.From && edge.To == exemption.To))
                    .Select(exemption => $"{exemption.From} -> {exemption.To}").ToList();

            Assert.True(violations.Count == 0, "依赖矩阵违规（须先修改 AllowedEdges 或解除环）：" + Environment.NewLine + string.Join(Environment.NewLine, violations));
            Assert.True(stale.Count == 0, "豁免名单已腐化（环已解除的行必须删除）：" + Environment.NewLine + string.Join(Environment.NewLine, stale));
        }

        /// <summary>
        /// 断言叶子能力簇（Assets/Social/Matchmaking/Party）横向互引恒为零。
        /// </summary>
        [Fact]
        public void LeafCapabilityDirectories_HaveNoCrossClusterReferences()
        {
            var edges = CollectDirectoryEdges();
            var violations = edges.Where(edge => LeafCapabilityDirectories.Contains(edge.From) && LeafCapabilityDirectories.Contains(edge.To)).ToList();

            Assert.True(violations.Count == 0, "叶子能力簇横向互引必须为零：" + Environment.NewLine + string.Join(Environment.NewLine, violations.Select(edge => $"{edge.From} -> {edge.To}（{edge.Location}）")));
        }

        /// <summary>
        /// 断言 Events 目录对全部域目录零类型引用（含 using 之外的限定名形态，防相对 using 绕过）。
        /// </summary>
        [Fact]
        public void EventsDirectory_HasNoDomainTypeReferences()
        {
            var domainDirs = Directories.Where(dir => dir != "Events" && dir != "Contracts" && dir != "Scope" && dir != "Idempotency" && dir != "Tokens").ToList();
            var pattern = new Regex(@"\b(" + string.Join("|", domainDirs) + @")\.[A-Z]\w*");
            var violations = new List<string>();
            foreach (var file in EnumerateOnlineSourceFiles().Where(file => Path.GetDirectoryName(file)?.Split(Path.DirectorySeparatorChar).Last() == "Events"))
            {
                var code = StripCommentsAndStrings(File.ReadAllText(file));
                foreach (Match match in pattern.Matches(code))
                {
                    violations.Add($"{Path.GetFileName(file)}: {match.Value}");
                }
            }

            Assert.True(violations.Count == 0, "Events 目录残留域类型限定引用：" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        /// <summary>
        /// 断言事件 topic 常量与来源标识逐字节冻结（C223 零行为变更红线；值 = 2026-10-12 迁移前基线）。
        /// </summary>
        [Fact]
        public void EventTopicConstants_RemainByteFrozen()
        {
            var expected = new (string Actual, string Frozen)[]
            {
                (GameFrameX.Online.Assets.OnlineAssetEvents.AssetGranted, "Online.Asset.Granted"),
                (GameFrameX.Online.Assets.OnlineAssetEvents.AssetDeducted, "Online.Asset.Deducted"),
                (GameFrameX.Online.Assets.OnlineAssetEvents.AssetAdjusted, "Online.Asset.Adjusted"),
                (GameFrameX.Online.Assets.OnlineAssetEvents.Source, "online-assets"),
                (GameFrameX.Online.Social.OnlineChatEvents.MessageSent, "Online.Chat.MessageSent"),
                (GameFrameX.Online.Social.OnlineChatEvents.MessageRecalled, "Online.Chat.MessageRecalled"),
                (GameFrameX.Online.Social.OnlineChatEvents.ReadMarkUpdated, "Online.Chat.ReadMarkUpdated"),
                (GameFrameX.Online.Social.OnlineChatEvents.ModerationFailed, "Online.Chat.ModerationFailed"),
                (GameFrameX.Online.Social.OnlineChatEvents.Source, "online-chat"),
                (GameFrameX.Online.HotfixRollback.OnlineHotfixEvents.VersionRegistered, "Online.Hotfix.VersionRegistered"),
                (GameFrameX.Online.HotfixRollback.OnlineHotfixEvents.RolledBack, "Online.Hotfix.RolledBack"),
                (GameFrameX.Online.HotfixRollback.OnlineHotfixEvents.Source, "online-hotfix"),
                (GameFrameX.Online.Leaderboard.OnlineLeaderboardEvents.LeaderboardCreated, "Online.Leaderboard.Created"),
                (GameFrameX.Online.Leaderboard.OnlineLeaderboardEvents.ScoreUpdated, "Online.Leaderboard.ScoreUpdated"),
                (GameFrameX.Online.Leaderboard.OnlineLeaderboardEvents.Source, "online-leaderboard"),
                (GameFrameX.Online.Matchmaking.OnlineMatchEvents.TicketChanged, "Online.Match.TicketChanged"),
                (GameFrameX.Online.Matchmaking.OnlineMatchEvents.AssignmentCreated, "Online.Match.AssignmentCreated"),
                (GameFrameX.Online.Matchmaking.OnlineMatchEvents.Source, "online-matchmaker"),
                (GameFrameX.Online.Match.OnlineMatchRuntimeEvents.MatchStateChanged, "Online.Match.StateChanged"),
                (GameFrameX.Online.Match.OnlineMatchRuntimeEvents.MatchSettled, "Online.Match.Settled"),
                (GameFrameX.Online.Match.OnlineMatchRuntimeEvents.Source, "online-match"),
                (GameFrameX.Online.Social.OnlineNotificationEvents.NotificationChanged, "Online.Notification.Changed"),
                (GameFrameX.Online.Social.OnlineNotificationEvents.Source, "online-notification"),
                (GameFrameX.Online.Party.OnlinePartyEvents.PartyChanged, "Online.Party.Changed"),
                (GameFrameX.Online.Party.OnlinePartyEvents.PartyInviteChanged, "Online.Party.InviteChanged"),
                (GameFrameX.Online.Party.OnlinePartyEvents.Source, "online-party"),
                (GameFrameX.Online.Presence.OnlinePresenceEvents.PresenceChanged, "Online.Presence.Changed"),
                (GameFrameX.Online.Presence.OnlinePresenceEvents.Source, "online-presence"),
                (GameFrameX.Online.Season.OnlineSeasonEvents.SeasonCreated, "Online.Season.Created"),
                (GameFrameX.Online.Season.OnlineSeasonEvents.SeasonStarted, "Online.Season.Started"),
                (GameFrameX.Online.Season.OnlineSeasonEvents.SeasonEnded, "Online.Season.Ended"),
                (GameFrameX.Online.Season.OnlineSeasonEvents.SeasonSettled, "Online.Season.Settled"),
                (GameFrameX.Online.Season.OnlineSeasonEvents.Source, "online-season"),
                (GameFrameX.Online.Session.OnlineSessionEvents.SessionCreated, "Online.Session.Created"),
                (GameFrameX.Online.Session.OnlineSessionEvents.SessionAuthenticated, "Online.Session.Authenticated"),
                (GameFrameX.Online.Session.OnlineSessionEvents.SessionConnected, "Online.Session.Connected"),
                (GameFrameX.Online.Session.OnlineSessionEvents.SessionActive, "Online.Session.Active"),
                (GameFrameX.Online.Session.OnlineSessionEvents.SessionReconnecting, "Online.Session.Reconnecting"),
                (GameFrameX.Online.Session.OnlineSessionEvents.SessionClosed, "Online.Session.Closed"),
                (GameFrameX.Online.Session.OnlineSessionEvents.SessionKicked, "Online.Session.Kicked"),
                (GameFrameX.Online.Session.OnlineSessionEvents.SessionExpired, "Online.Session.Expired"),
                (GameFrameX.Online.Session.OnlineSessionEvents.SessionTokenRefreshed, "Online.Session.TokenRefreshed"),
                (GameFrameX.Online.Session.OnlineSessionEvents.Source, "online-session"),
                (GameFrameX.Online.Social.OnlineSocialEvents.FriendshipChanged, "Online.Social.FriendshipChanged"),
                (GameFrameX.Online.Social.OnlineSocialEvents.BlockChanged, "Online.Social.BlockChanged"),
                (GameFrameX.Online.Social.OnlineSocialEvents.MuteChanged, "Online.Social.MuteChanged"),
                (GameFrameX.Online.Social.OnlineSocialEvents.ReportChanged, "Online.Social.ReportChanged"),
                (GameFrameX.Online.Social.OnlineSocialEvents.PunishmentChanged, "Online.Social.PunishmentChanged"),
                (GameFrameX.Online.Social.OnlineSocialEvents.GroupChanged, "Online.Social.GroupChanged"),
                (GameFrameX.Online.Social.OnlineSocialEvents.Source, "online-social"),
                (GameFrameX.Online.Tournament.OnlineTournamentEvents.TournamentCreated, "Online.Tournament.Created"),
                (GameFrameX.Online.Tournament.OnlineTournamentEvents.TournamentStarted, "Online.Tournament.Started"),
                (GameFrameX.Online.Tournament.OnlineTournamentEvents.TournamentRegistered, "Online.Tournament.Registered"),
                (GameFrameX.Online.Tournament.OnlineTournamentEvents.TournamentEnded, "Online.Tournament.Ended"),
                (GameFrameX.Online.Tournament.OnlineTournamentEvents.TournamentSettled, "Online.Tournament.Settled"),
                (GameFrameX.Online.Tournament.OnlineTournamentEvents.Source, "online-tournament"),
            };
            foreach (var (actual, frozen) in expected)
            {
                Assert.Equal(frozen, actual);
            }
        }

        /// <summary>
        /// 采集全部源码文件的目录依赖边（using 语义解析：全限定 + 外围命名空间相对解析）。
        /// </summary>
        private static List<(string From, string To, string Location)> CollectDirectoryEdges()
        {
            var usingDirective = new Regex(@"^[ \t]*using\s+(?:static\s+)?([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*;", RegexOptions.Multiline);
            var namespaceDirective = new Regex(@"namespace\s+([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)");
            var edges = new List<(string From, string To, string Location)>();
            foreach (var file in EnumerateOnlineSourceFiles())
            {
                var relative = Path.GetRelativePath(FindOnlineSourceRoot(), file);
                var segments = relative.Split(Path.DirectorySeparatorChar);
                if (segments.Length < 2)
                {
                    continue;
                }

                var from = segments[0];
                if (!Directories.Contains(from))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                var namespaceMatch = namespaceDirective.Match(text);
                foreach (Match match in usingDirective.Matches(text))
                {
                    var target = match.Groups[1].Value;
                    var line = text[..match.Index].Count(ch => ch == '\n') + 1;
                    string to;
                    if (target.StartsWith("GameFrameX.Online.", StringComparison.Ordinal))
                    {
                        to = target.Split('.')[2];
                    }
                    else if (namespaceMatch.Success && namespaceMatch.Groups[1].Value.StartsWith("GameFrameX.Online", StringComparison.Ordinal) && !target.Contains('.'))
                    {
                        // 相对 using（如 using Scope;）经外围命名空间解析为兄弟目录。
                        to = target;
                    }
                    else
                    {
                        continue;
                    }

                    if (Directories.Contains(to) && to != from)
                    {
                        edges.Add((from, to, $"{relative}:{line}"));
                    }
                }
            }

            return edges;
        }

        /// <summary>
        /// 枚举 GameFrameX.Online 全部源码文件（排除 bin/obj 产物）。
        /// </summary>
        private static IEnumerable<string> EnumerateOnlineSourceFiles()
        {
            var root = FindOnlineSourceRoot();
            Assert.False(root == null, "未定位到 GameFrameX.Online 源码根（测试须在仓库内运行）");
            return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("bin") && !path.Split(Path.DirectorySeparatorChar).Contains("obj"));
        }

        /// <summary>
        /// 从测试程序集位置向上定位 GameFrameX.Online 源码根。
        /// </summary>
        private static string FindOnlineSourceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, OnlineProjectDirectory);
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, OnlineProjectDirectory + ".csproj")))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        /// <summary>
        /// 剥离注释与字符串字面量（限定名守卫扫描前预处理，避免误报常量与注释）。
        /// </summary>
        private static string StripCommentsAndStrings(string text)
        {
            text = new Regex(@"@""(?:[^""]|"""")*""").Replace(text, string.Empty);
            text = new Regex(@"""(?:[^""\\\n]|\\.)*""").Replace(text, string.Empty);
            text = new Regex(@"//[^\n]*").Replace(text, string.Empty);
            text = new Regex(@"/\*.*?\*/", RegexOptions.Singleline).Replace(text, string.Empty);
            return text;
        }
    }
}
