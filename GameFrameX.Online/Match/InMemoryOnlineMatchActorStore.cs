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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Online.Match;

/// <summary>
/// 进程内对局存储（本阶段部署在同一进程内，vault:C6「不提前拆出独立 Match 服务」）。
/// <para>
/// 维护约束（红线）：创建与 CAS 更新在**同一把锁**下完成比对与写入，
/// 这是 VC-5.13「并发多局无串局」的前提。任何把「读版本」与「写版本」拆到两个临界区的改写，
/// 都会重新打开两个 Actor 同时推进同一对局的竞态窗口。
/// </para>
/// <para>
/// 天花板（ponytail）：单锁全串行，对局基数上万后 Tick 扫描会成为热点；
/// 升级路径是按 (TenantId, AppId) 分片加锁并保留同样的 CAS 语义，调用方无感。
/// </para>
/// </summary>
public sealed class InMemoryOnlineMatchActorStore : IOnlineMatchActorStore
{
    /// <summary>全局读写锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>对局表：键 = (TenantId, AppId, MatchId)。</summary>
    private readonly Dictionary<string, OnlineMatch> _matches = new Dictionary<string, OnlineMatch>();

    /// <inheritdoc />
    public Task<OnlineMatch> CreateAsync(OnlineMatch match, CancellationToken cancellationToken = default)
    {
        if (match == null)
        {
            throw new ArgumentNullException(nameof(match));
        }

        lock (_syncRoot)
        {
            var key = BuildKey(match.TenantId, match.AppId, match.MatchId);
            if (_matches.ContainsKey(key))
            {
                return Task.FromResult<OnlineMatch>(null);
            }

            var stored = match.Copy();
            stored.Version = 1;
            _matches[key] = stored;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <inheritdoc />
    public Task<OnlineMatch> FindAsync(long tenantId, long appId, string matchId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(FindInternal(tenantId, appId, matchId));
        }
    }

    /// <inheritdoc />
    public Task<OnlineMatch> UpdateAsync(OnlineMatch match, CancellationToken cancellationToken = default)
    {
        if (match == null)
        {
            throw new ArgumentNullException(nameof(match));
        }

        lock (_syncRoot)
        {
            var key = BuildKey(match.TenantId, match.AppId, match.MatchId);
            if (!_matches.TryGetValue(key, out var current))
            {
                return Task.FromResult<OnlineMatch>(null);
            }

            if (current.Version != match.Version)
            {
                return Task.FromResult<OnlineMatch>(null);
            }

            var stored = match.Copy();
            stored.Version = current.Version + 1;
            _matches[key] = stored;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(long tenantId, long appId, string matchId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var key = BuildKey(tenantId, appId, matchId);
            if (!_matches.TryGetValue(key, out var current))
            {
                return Task.FromResult(false);
            }

            if (current.State != OnlineMatchState.Closed)
            {
                return Task.FromResult(false);
            }

            _matches.Remove(key);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OnlineMatch>> ListAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlineMatch>();
        lock (_syncRoot)
        {
            foreach (var pair in _matches)
            {
                if (pair.Value.TenantId == tenantId && pair.Value.AppId == appId)
                {
                    result.Add(pair.Value.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineMatch>>(result);
    }

    /// <summary>
    /// 按标识查找并返回副本（须在锁内调用）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="matchId">对局标识。</param>
    /// <returns>对局副本；不存在返回 null。</returns>
    private OnlineMatch FindInternal(long tenantId, long appId, string matchId)
    {
        if (string.IsNullOrEmpty(matchId))
        {
            return null;
        }

        return _matches.TryGetValue(BuildKey(tenantId, appId, matchId), out var match) ? match.Copy() : null;
    }

    /// <summary>
    /// 构造作用域隔离键。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="matchId">对局标识。</param>
    /// <returns>复合键。</returns>
    private static string BuildKey(long tenantId, long appId, string matchId)
    {
        return string.Concat(
            tenantId.ToString(CultureInfo.InvariantCulture),
            ":",
            appId.ToString(CultureInfo.InvariantCulture),
            ":",
            matchId ?? string.Empty);
    }
}
