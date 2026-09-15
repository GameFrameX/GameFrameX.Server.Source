//  ==========================================================================================
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
//  ==========================================================================================

namespace GameFrameX.Online.Session;

/// <summary>
/// 会话域内存默认存储（vault:C3 S2.4：单进程/测试默认实现；生产装配以持久化实现替换）。
/// <para>
/// 维护约束（天花板）：全量驻留内存，无 TTL 自动清理（清理经 <c>OnlineSessionManager.SweepAsync</c> 主动触发）；
/// 全局锁保护——Token 轮换与终态写为低频操作，粗粒度锁足够，持久化实现按原子更新替代；
/// 进程重启即失忆（VC-2.14 重启失效策略的内存形态 = 全部失效），持久化语义由生产实现承载。
/// </para>
/// </summary>
public sealed class InMemoryOnlineSessionStore : IOnlineSessionStore
{
    /// <summary>全局读写锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>会话表：键 = SessionId。</summary>
    private readonly Dictionary<string, OnlineSession> _sessions = new Dictionary<string, OnlineSession>();

    /// <summary>Token 指纹索引：键 = TokenHash，值 = SessionId。</summary>
    private readonly Dictionary<string, string> _tokenIndex = new Dictionary<string, string>();

    /// <summary>
    /// 各会话当前已入册的指纹（索引维护的事实源）。
    /// ponytail: 调用方可能原位变更会话对象后回写（引用共享），不能拿实体当前字段反推旧索引键，必须另行记账。
    /// </summary>
    private readonly Dictionary<string, string> _indexedHashBySessionId = new Dictionary<string, string>();

    /// <summary>写入或覆盖会话（同步维护 Token 指纹索引：旧指纹失效）。</summary>
    /// <param name="session">会话实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task AddOrUpdateAsync(OnlineSession session, CancellationToken cancellationToken = default)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        lock (_syncRoot)
        {
            if (_indexedHashBySessionId.TryGetValue(session.Id, out var indexedHash) && !string.IsNullOrEmpty(indexedHash))
            {
                _tokenIndex.Remove(indexedHash);
            }

            var currentHash = session.CurrentTokenHash ?? string.Empty;
            _sessions[session.Id] = session;
            _indexedHashBySessionId[session.Id] = currentHash;
            if (!string.IsNullOrEmpty(currentHash))
            {
                _tokenIndex[currentHash] = session.Id;
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>按会话标识查找。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话实体；不存在返回 null。</returns>
    public Task<OnlineSession> FindAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _sessions.TryGetValue(sessionId, out var session);
            return Task.FromResult(session);
        }
    }

    /// <summary>按 Token 指纹查找会话。</summary>
    /// <param name="tokenHash">Token SHA-256 指纹。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话实体；不存在返回 null。</returns>
    public Task<OnlineSession> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (tokenHash == null || !_tokenIndex.TryGetValue(tokenHash, out var sessionId))
            {
                return Task.FromResult<OnlineSession>(null);
            }

            _sessions.TryGetValue(sessionId, out var session);
            return Task.FromResult(session);
        }
    }

    /// <summary>列出玩家在 (TenantId, AppId) 下的非终态会话。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>非终态会话列表。</returns>
    public Task<IReadOnlyList<OnlineSession>> ListActiveByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var result = new List<OnlineSession>();
            foreach (var session in _sessions.Values)
            {
                if (session.TenantId == tenantId && session.AppId == appId && session.PlayerId == playerId && !session.State.IsTerminal())
                {
                    result.Add(session);
                }
            }

            return Task.FromResult<IReadOnlyList<OnlineSession>>(result);
        }
    }

    /// <summary>列出全部非终态会话。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全部非终态会话列表。</returns>
    public Task<IReadOnlyList<OnlineSession>> ListNonTerminalAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var result = new List<OnlineSession>();
            foreach (var session in _sessions.Values)
            {
                if (!session.State.IsTerminal())
                {
                    result.Add(session);
                }
            }

            return Task.FromResult<IReadOnlyList<OnlineSession>>(result);
        }
    }

    /// <summary>移除会话（同步清理指纹索引与记账）。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (_indexedHashBySessionId.TryGetValue(sessionId, out var indexedHash))
            {
                if (!string.IsNullOrEmpty(indexedHash))
                {
                    _tokenIndex.Remove(indexedHash);
                }

                _indexedHashBySessionId.Remove(sessionId);
            }

            _sessions.Remove(sessionId);
        }

        return Task.CompletedTask;
    }
}
