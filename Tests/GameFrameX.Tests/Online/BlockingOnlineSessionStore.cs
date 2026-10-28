// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and other related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws and regulations of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 双许可证分发，
//   This project is dual-licensed under the MIT License and Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file at the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or to infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Foundation.Session;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 故障注入存储：按会话标识把 <see cref="AddOrUpdateAsync"/> 悬挂在显式放行信号上（读路径与其余会话写透传内层）。
    /// 用于验证分片门互斥范围——目标会话的操作悬挂期间，其它会话 / 其它玩家的操作不得排队等待（GFX-1078）。
    /// </summary>
    public sealed class BlockingOnlineSessionStore : IOnlineSessionStore
    {
        /// <summary>被透传的内层存储。</summary>
        private readonly IOnlineSessionStore _inner;

        /// <summary>阻塞写已入场信号（测试等待悬挂成立）。</summary>
        private readonly TaskCompletionSource<bool> _blockedWriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>阻塞写放行信号（测试显式释放）。</summary>
        private readonly TaskCompletionSource<bool> _blockedWriteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>当前被阻塞写入的会话标识（空 = 未布防）。</summary>
        private volatile string _blockedSessionId;

        /// <summary>
        /// 初始化 <see cref="BlockingOnlineSessionStore"/>。
        /// </summary>
        /// <param name="inner">内层存储（缺省新建内存实现）。</param>
        public BlockingOnlineSessionStore(IOnlineSessionStore inner = null)
        {
            _inner = inner ?? new InMemoryOnlineSessionStore();
        }

        /// <summary>并发计数透传内层（读路径不悬挂）。</summary>
        public Task<IReadOnlyDictionary<OnlineSessionState, int>> CountNonTerminalByScopeAsync(long tenantId, long appId, long serverId, CancellationToken cancellationToken = default)
        {
            return _inner.CountNonTerminalByScopeAsync(tenantId, appId, serverId, cancellationToken);
        }

        /// <summary>布防：指定会话的写入此后全部悬挂，直至 <see cref="ReleaseBlockedWrites"/>。</summary>
        /// <param name="sessionId">目标会话标识。</param>
        public void BlockSessionWrites(string sessionId)
        {
            _blockedSessionId = sessionId;
        }

        /// <summary>等待阻塞写已入场（目标会话的写入已悬挂在内）。</summary>
        /// <returns>入场信号任务。</returns>
        public Task WaitUntilBlockedAsync()
        {
            return _blockedWriteEntered.Task;
        }

        /// <summary>放行全部阻塞写。</summary>
        public void ReleaseBlockedWrites()
        {
            _blockedWriteRelease.TrySetResult(true);
        }

        /// <summary>写入或覆盖会话：命中布防会话时悬挂至放行，其余透传内层。</summary>
        /// <param name="session">会话实体。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task AddOrUpdateAsync(OnlineSession session, CancellationToken cancellationToken = default)
        {
            if (session != null && session.Id == _blockedSessionId)
            {
                _blockedWriteEntered.TrySetResult(true);
                await _blockedWriteRelease.Task.ConfigureAwait(false);
                await _inner.AddOrUpdateAsync(session, cancellationToken).ConfigureAwait(false);
                return;
            }

            await _inner.AddOrUpdateAsync(session, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>按会话标识查找（透传）。</summary>
        /// <param name="sessionId">会话标识。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>会话实体；不存在返回 null。</returns>
        public Task<OnlineSession> FindAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            return _inner.FindAsync(sessionId, cancellationToken);
        }

        /// <summary>按 Token 指纹查找会话（透传）。</summary>
        /// <param name="tokenHash">Token SHA-256 指纹。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>会话实体；不存在返回 null。</returns>
        public Task<OnlineSession> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            return _inner.FindByTokenHashAsync(tokenHash, cancellationToken);
        }

        /// <summary>列出玩家非终态会话（透传）。</summary>
        /// <param name="tenantId">租户标识。</param>
        /// <param name="appId">应用标识。</param>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>非终态会话列表。</returns>
        public Task<IReadOnlyList<OnlineSession>> ListActiveByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
        {
            return _inner.ListActiveByPlayerAsync(tenantId, appId, playerId, cancellationToken);
        }

        /// <summary>列出全部非终态会话（透传）。</summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>全部非终态会话列表。</returns>
        public Task<IReadOnlyList<OnlineSession>> ListNonTerminalAsync(CancellationToken cancellationToken = default)
        {
            return _inner.ListNonTerminalAsync(cancellationToken);
        }

        /// <summary>移除会话（透传）。</summary>
        /// <param name="sessionId">会话标识。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            return _inner.RemoveAsync(sessionId, cancellationToken);
        }
    }
}
