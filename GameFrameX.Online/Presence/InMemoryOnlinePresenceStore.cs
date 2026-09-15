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

namespace GameFrameX.Online.Presence;

/// <summary>
/// 在线状态内存默认存储（vault:C3 S2.5：单进程/测试默认实现；生产装配以持久化实现替换）。
/// <para>
/// 维护约束（天花板）：全量驻留内存，超窗/关闭清理经 <c>OnlinePresenceService</c> 主动触发；
/// 全局锁保护——读写均为轻量操作，粗粒度锁足够；进程重启即全部 Offline（重启失效的内存形态）。
/// </para>
/// </summary>
public sealed class InMemoryOnlinePresenceStore : IOnlinePresenceStore
{
    /// <summary>全局读写锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>在线状态表：键 = (TenantId, AppId, PlayerId)。</summary>
    private readonly Dictionary<string, OnlinePresenceRecord> _records = new Dictionary<string, OnlinePresenceRecord>();

    /// <summary>写入或覆盖在线状态记录。</summary>
    /// <param name="record">在线状态记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task SetAsync(OnlinePresenceRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        lock (_syncRoot)
        {
            _records[BuildKey(record.TenantId, record.AppId, record.PlayerId)] = record;
        }

        return Task.CompletedTask;
    }

    /// <summary>按 (TenantId, AppId, PlayerId) 查找在线状态记录。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>在线状态记录；无记录返回 null。</returns>
    public Task<OnlinePresenceRecord> FindAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _records.TryGetValue(BuildKey(tenantId, appId, playerId), out var record);
            return Task.FromResult(record);
        }
    }

    /// <summary>移除在线状态记录（即 Offline）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task RemoveAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _records.Remove(BuildKey(tenantId, appId, playerId));
        }

        return Task.CompletedTask;
    }

    /// <summary>列出 (TenantId, AppId) 下全部在线状态记录。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>在线状态记录列表。</returns>
    public Task<IReadOnlyList<OnlinePresenceRecord>> ListByAppAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var result = new List<OnlinePresenceRecord>();
            foreach (var record in _records.Values)
            {
                if (record.TenantId == tenantId && record.AppId == appId)
                {
                    result.Add(record);
                }
            }

            return Task.FromResult<IReadOnlyList<OnlinePresenceRecord>>(result);
        }
    }

    /// <summary>构造在线状态记录键。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>复合键字符串。</returns>
    private static string BuildKey(long tenantId, long appId, long playerId)
    {
        return tenantId + ":" + appId + ":" + playerId;
    }
}
