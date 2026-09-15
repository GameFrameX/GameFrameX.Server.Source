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
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Leaderboard;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 并发写榜竞态注入榜单存储（VC-7.5-c 证据件）：在第 N 次重置尝试的临界区内、真正调用 CAS 清空**之前**，
    /// 先落一笔真实的新成绩——精确复现「读序 → 快照 → 清空」之间被并发写入插入的时序，
    /// 用于验证 CAS 拒绝清空、重读重拍快照后重试成功，以及重试耗尽时的零损失回退。
    /// <para>
    /// 仅注写入存储边界：被插入的成绩不经投影器（生产路径上这一笔会伴随事件与缓存失效），
    /// 因为本用例要验证的不变量是存储层 CAS 本身，而非写榜链路的可观测副作用（后者由 C102 用例覆盖）。
    /// </para>
    /// </summary>
    internal sealed class InterleavingLeaderboardStore : IOnlineLeaderboardStore
    {
        /// <summary>被包装存储。</summary>
        private readonly IOnlineLeaderboardStore _inner;

        /// <summary>竞态写入的基准时刻（UTC 毫秒）。</summary>
        private const long InterleaveBaseTime = 2000000L;

        /// <summary>
        /// 初始化 <see cref="InterleavingLeaderboardStore"/>。
        /// </summary>
        /// <param name="inner">被包装存储。</param>
        public InterleavingLeaderboardStore(IOnlineLeaderboardStore inner)
        {
            _inner = inner;
        }

        /// <summary>
        /// 获取或设置在前若干次重置尝试中注入竞态写入的次数（0 = 不注入，透传）。
        /// </summary>
        public int InterleaveOnResetAttempts
        {
            get;
            set;
        }

        /// <summary>
        /// 获取重置尝试次数（断言 CAS 确实被拒绝并触发了重试）。
        /// </summary>
        public int ResetAttempts
        {
            get;
            private set;
        }

        /// <summary>
        /// 获取竞态写入的条目序列（第 N 次注入取第 N 项；每项须为独立玩家，保证条目集合真的发生变化）。
        /// </summary>
        public List<(long PlayerId, long Score)> LateEntries
        {
            get;
        } = new List<(long PlayerId, long Score)>();

        /// <inheritdoc />
        public Task<bool> TryResetAsync(OnlineLeaderboard leaderboard, IReadOnlyList<OnlineLeaderboardEntry> expectedEntries, CancellationToken cancellationToken = default)
        {
            ResetAttempts++;
            if (ResetAttempts > InterleaveOnResetAttempts || LateEntries.Count < ResetAttempts)
            {
                return _inner.TryResetAsync(leaderboard, expectedEntries, cancellationToken);
            }

            return InterleaveThenResetAsync(leaderboard, expectedEntries, ResetAttempts - 1, cancellationToken);
        }

        /// <inheritdoc />
        public Task<OnlineLeaderboard> CreateAsync(OnlineLeaderboard leaderboard, CancellationToken cancellationToken = default)
        {
            return _inner.CreateAsync(leaderboard, cancellationToken);
        }

        /// <inheritdoc />
        public Task<OnlineLeaderboard> FindAsync(long tenantId, long appId, string leaderboardId, CancellationToken cancellationToken = default)
        {
            return _inner.FindAsync(tenantId, appId, leaderboardId, cancellationToken);
        }

        /// <inheritdoc />
        public Task<OnlineLeaderboardEntry> FindEntryAsync(long tenantId, long appId, string leaderboardId, long playerId, CancellationToken cancellationToken = default)
        {
            return _inner.FindEntryAsync(tenantId, appId, leaderboardId, playerId, cancellationToken);
        }

        /// <inheritdoc />
        public Task<OnlineLeaderboardApplyResult> ApplySubmissionAsync(OnlineLeaderboard leaderboard, OnlineLeaderboardScoreSubmission submission, CancellationToken cancellationToken = default)
        {
            return _inner.ApplySubmissionAsync(leaderboard, submission, cancellationToken);
        }

        /// <inheritdoc />
        public Task<List<OnlineLeaderboardEntry>> ListOrderedEntriesAsync(OnlineLeaderboard leaderboard, CancellationToken cancellationToken = default)
        {
            return _inner.ListOrderedEntriesAsync(leaderboard, cancellationToken);
        }

        /// <summary>
        /// 先落一笔竞态成绩、再执行真正的 CAS 清空（此时 expectedEntries 已过期，清空必然被拒）。
        /// </summary>
        /// <param name="leaderboard">目标榜单。</param>
        /// <param name="expectedEntries">调用方读到的条目集合。</param>
        /// <param name="lateEntryIndex">竞态写入条目下标。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>CAS 清空结果（预期 false）。</returns>
        private async Task<bool> InterleaveThenResetAsync(OnlineLeaderboard leaderboard, IReadOnlyList<OnlineLeaderboardEntry> expectedEntries, int lateEntryIndex, CancellationToken cancellationToken)
        {
            var late = LateEntries[lateEntryIndex];
            var submission = new OnlineLeaderboardScoreSubmission
            {
                PlayerId = late.PlayerId,
                IncomingScore = late.Score,
                SourceKind = OnlineLeaderboardScoreSource.MatchResult,
                SourceMatchResultId = "mrs-late-" + (lateEntryIndex + 1),
                SubmittedTime = InterleaveBaseTime + lateEntryIndex,
            };
            await _inner.ApplySubmissionAsync(leaderboard, submission, cancellationToken);

            return await _inner.TryResetAsync(leaderboard, expectedEntries, cancellationToken);
        }
    }
}
