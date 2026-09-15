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
/// 进程内结算结果存储（首次结果胜出语义）。
/// <para>
/// 维护约束（红线）：存在性检查与写入在**同一把锁**下完成——
/// 这是「并发结算同一对局只成功一次」（VC-5.8、量化指标「重复结算生效次数 = 0」）的实现前提。
/// 任何先查后写的两段式改写都会重新打开重复结算窗口。
/// </para>
/// <para>
/// 天花板（ponytail）：单锁全串行，结果基数上万后成为热点；
/// 升级路径是按 (TenantId, AppId) 分片加锁或改用唯一约束的持久化存储，语义不变。
/// </para>
/// </summary>
public sealed class InMemoryOnlineMatchResultStore : IOnlineMatchResultStore
{
    /// <summary>全局读写锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>结果表：键 = (TenantId, AppId, MatchId)。</summary>
    private readonly Dictionary<string, OnlineMatchResult> _results = new Dictionary<string, OnlineMatchResult>();

    /// <inheritdoc />
    public Task<OnlineMatchResult> FindByMatchAsync(long tenantId, long appId, string matchId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(matchId))
        {
            return Task.FromResult<OnlineMatchResult>(null);
        }

        lock (_syncRoot)
        {
            return Task.FromResult(_results.TryGetValue(BuildKey(tenantId, appId, matchId), out var result) ? result.Copy() : null);
        }
    }

    /// <inheritdoc />
    public Task<OnlineMatchResult> CommitAsync(OnlineMatchResult result, CancellationToken cancellationToken = default)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        lock (_syncRoot)
        {
            var key = BuildKey(result.TenantId, result.AppId, result.MatchId);
            if (_results.TryGetValue(key, out var existing))
            {
                return Task.FromResult(existing.Copy());
            }

            _results[key] = result.Copy();
            return Task.FromResult(result.Copy());
        }
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
