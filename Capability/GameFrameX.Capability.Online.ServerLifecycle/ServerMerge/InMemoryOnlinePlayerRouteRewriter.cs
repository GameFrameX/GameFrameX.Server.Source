// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Keys;

/// <summary>
/// 内存实现 PlayerRoute 重指向端口（单进程自托管 / 演练夹具：路由表按 (playerId → serverId) 承载）。
/// </summary>
public sealed class InMemoryOnlinePlayerRouteRewriter : IOnlinePlayerRouteRewriter
{
    /// <summary>
    /// 路由表守卫锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 路由表（键 = 玩家标识，值 = 归属区服标识）。
    /// </summary>
    private readonly Dictionary<long, long> _routesByPlayer = new();

    /// <summary>
    /// 登记一条玩家路由（演练夹具数据面）。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="serverId">归属区服标识。</param>
    public void RegisterRoute(long playerId, long serverId)
    {
        lock (_lock)
        {
            _routesByPlayer[playerId] = serverId;
        }
    }

    /// <summary>
    /// 查询玩家路由归属。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>归属区服标识；未登记返回 0。</returns>
    public long FindRoute(long playerId)
    {
        lock (_lock)
        {
            return _routesByPlayer.TryGetValue(playerId, out var serverId) ? serverId : 0;
        }
    }

    /// <inheritdoc />
    public Task<long> RewriteRoutesAsync(IReadOnlyList<long> sourceServerIds, long targetServerId, bool dryRun, CancellationToken cancellationToken)
    {
        if (sourceServerIds == null)
        {
            throw new ArgumentNullException(nameof(sourceServerIds));
        }

        var sourceSet = new HashSet<long>(sourceServerIds);
        long affected = 0;
        lock (_lock)
        {
            affected = _routesByPlayer.Values.Count(sourceSet.Contains);

            if (!dryRun && affected > 0)
            {
                foreach (var playerId in _routesByPlayer.Keys.ToList().Where(playerId => sourceSet.Contains(_routesByPlayer[playerId])))
                {
                    _routesByPlayer[playerId] = targetServerId;
                }
            }
        }

        return Task.FromResult(affected);
    }
}
