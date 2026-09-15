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

namespace GameFrameX.Online.Social;

/// <summary>
/// 好友关系存储的单进程默认实现（持久化与多实例协调归 Server 仓运行时装配，X4）。
/// <para>
/// 维护约束：无向对唯一性与状态 CAS 都在同一临界区内完成——<see cref="SaveIfAbsentAsync"/> 的
/// 「查 + 写」不可拆分，否则并发添加会落出两条记录（VC-6.1 失效）。
/// </para>
/// <para>
/// 天花板（ponytail）：全局单锁 + 全表线性扫描，好友关系上万后需换分片锁 + 按玩家索引；
/// 当前量级下正确性优先。
/// </para>
/// </summary>
public sealed class InMemoryOnlineFriendStore : IOnlineFriendStore
{
    /// <summary>并发保护锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>关系表（键 = 作用域 + 无向对）。</summary>
    private readonly Dictionary<string, OnlineFriendship> _friendshipsByPair = new Dictionary<string, OnlineFriendship>(StringComparer.Ordinal);

    /// <summary>关系标识索引。</summary>
    private readonly Dictionary<string, string> _pairKeyByFriendshipId = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<OnlineFriendship> SaveIfAbsentAsync(OnlineFriendship friendship, CancellationToken cancellationToken = default)
    {
        if (friendship == null)
        {
            throw new ArgumentNullException(nameof(friendship));
        }

        var pairKey = BuildPairKey(friendship.TenantId, friendship.AppId, friendship.LowPlayerId, friendship.HighPlayerId);
        lock (_syncRoot)
        {
            OnlineFriendship existing;
            if (_friendshipsByPair.TryGetValue(pairKey, out existing))
            {
                return Task.FromResult(existing.Copy());
            }

            // 存档存副本而非调用方实例（调用方复用该实例不会回写库内记录）。
            var stored = friendship.Copy();
            _friendshipsByPair[pairKey] = stored;
            _pairKeyByFriendshipId[friendship.FriendshipId ?? string.Empty] = pairKey;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <inheritdoc />
    public Task<OnlineFriendship> FindAsync(long tenantId, long appId, long leftPlayerId, long rightPlayerId, CancellationToken cancellationToken = default)
    {
        long low;
        long high;
        OnlineFriendship.Canonicalize(leftPlayerId, rightPlayerId, out low, out high);
        var pairKey = BuildPairKey(tenantId, appId, low, high);
        lock (_syncRoot)
        {
            OnlineFriendship found;
            if (_friendshipsByPair.TryGetValue(pairKey, out found))
            {
                return Task.FromResult(found.Copy());
            }

            return Task.FromResult<OnlineFriendship>(null);
        }
    }

    /// <inheritdoc />
    public Task<OnlineFriendship> UpdateStateAsync(long tenantId, long appId, string friendshipId, OnlineFriendshipState expectedState, OnlineFriendshipState newState, long nowUnixMilliseconds, bool responded, CancellationToken cancellationToken = default)
    {
        var lookupKey = friendshipId ?? string.Empty;
        lock (_syncRoot)
        {
            string pairKey;
            if (!_pairKeyByFriendshipId.TryGetValue(lookupKey, out pairKey))
            {
                return Task.FromResult<OnlineFriendship>(null);
            }

            OnlineFriendship current;
            if (!_friendshipsByPair.TryGetValue(pairKey, out current))
            {
                return Task.FromResult<OnlineFriendship>(null);
            }

            if (current.TenantId != tenantId || current.AppId != appId)
            {
                return Task.FromResult<OnlineFriendship>(null);
            }

            if (current.State != expectedState)
            {
                // CAS 失败：不留任何写入痕迹，调用方据 null 判定「状态已被并发改写」。
                return Task.FromResult<OnlineFriendship>(null);
            }

            current.State = newState;
            current.UpdatedAtTime = nowUnixMilliseconds;
            if (responded)
            {
                current.RespondedAtTime = nowUnixMilliseconds;
            }

            return Task.FromResult(current.Copy());
        }
    }

    /// <inheritdoc />
    public Task<OnlineFriendship> RenewRequestAsync(long tenantId, long appId, string friendshipId, OnlineFriendshipState expectedState, long requesterId, long addresseeId, long nowUnixMilliseconds, long expiresAtTime, CancellationToken cancellationToken = default)
    {
        var lookupKey = friendshipId ?? string.Empty;
        lock (_syncRoot)
        {
            string pairKey;
            if (!_pairKeyByFriendshipId.TryGetValue(lookupKey, out pairKey))
            {
                return Task.FromResult<OnlineFriendship>(null);
            }

            OnlineFriendship current;
            if (!_friendshipsByPair.TryGetValue(pairKey, out current))
            {
                return Task.FromResult<OnlineFriendship>(null);
            }

            if (current.TenantId != tenantId || current.AppId != appId || current.State != expectedState)
            {
                return Task.FromResult<OnlineFriendship>(null);
            }

            // 三项同临界区落定：状态 + 方向 + 有效期（拆开写会让权限判据读到半成品）。
            current.State = OnlineFriendshipState.Requested;
            current.RequesterId = requesterId;
            current.AddresseeId = addresseeId;
            current.UpdatedAtTime = nowUnixMilliseconds;
            current.ExpiresAtTime = expiresAtTime;
            return Task.FromResult(current.Copy());
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OnlineFriendship>> ListByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlineFriendship>();
        lock (_syncRoot)
        {
            foreach (var pair in _friendshipsByPair)
            {
                var friendship = pair.Value;
                if (friendship.TenantId != tenantId || friendship.AppId != appId)
                {
                    continue;
                }

                if (friendship.Contains(playerId))
                {
                    result.Add(friendship.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineFriendship>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OnlineFriendship>> ListByStateAsync(long tenantId, long appId, OnlineFriendshipState state, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlineFriendship>();
        lock (_syncRoot)
        {
            foreach (var pair in _friendshipsByPair)
            {
                var friendship = pair.Value;
                if (friendship.TenantId == tenantId && friendship.AppId == appId && friendship.State == state)
                {
                    result.Add(friendship.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineFriendship>>(result);
    }

    /// <summary>
    /// 构造无向对索引键（作用域 + 规范化玩家对）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="lowPlayerId">低标识侧玩家。</param>
    /// <param name="highPlayerId">高标识侧玩家。</param>
    /// <returns>索引键。</returns>
    private static string BuildPairKey(long tenantId, long appId, long lowPlayerId, long highPlayerId)
    {
        return "friend:" + tenantId + ":" + appId + ":" + lowPlayerId + ":" + highPlayerId;
    }
}
