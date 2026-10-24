// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   Any legal disputes and liabilities arising from secondary development based on this project
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool
//   CNB Repository: https://cnb.cool
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Apps.Player.Player.Entity;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.DataBase;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Launcher.Online;

/// <summary>
/// 宿主侧玩家目录实现（<see cref="IOnlinePlayerDirectory"/> 的数据源落地，C294）：
/// 以本进程业务库的 <see cref="PlayerState"/>（玩家展示名权威归属）为目录数据源——
/// 按名字搜索 = 展示名包含匹配（稳定序 = 玩家标识升序），批量解析 = 按标识集合读取。
/// <para>
/// 维护约束：组合发生在业务库初始化之后（模板序列：数据库 → 热更装载 → 能力组合），数据源可达性由
/// 模板顺序保证；查询直读业务库（无缓存）——目录只服务好友搜索与展示名解析，量级与索引以业务库现状为准；
/// 空白关键字不返回结果（契约约定）；<see cref="PlayerState.Name"/> 为 null 的档案不出现在结果中。
/// </para>
/// </summary>
/// <remarks>
/// The host-side player-directory implementation (the data-source landing of
/// <see cref="IOnlinePlayerDirectory"/>, C294): the directory source is this process's business-database
/// <see cref="PlayerState"/> (the authoritative home of the player display name) — name search is a
/// display-name contains-match (stable order = player id ascending) and batch resolution reads by the
/// id set. Composition happens after the business database is initialized (template order: databases →
/// hotfix load → capability composition), so the source is reachable by construction; queries read the
/// business database directly (no cache) — the directory only serves friend search and display-name
/// resolution; blank keywords return no results (per the contract); profiles with a null
/// <see cref="PlayerState.Name"/> never appear in the results.
/// </remarks>
public sealed class HostPlayerDirectory : IOnlinePlayerDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<OnlinePlayerDirectoryEntry>> SearchByNameAsync(long tenantId, long appId, string keyword, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword) || limit <= 0)
        {
            return new List<OnlinePlayerDirectoryEntry>();
        }

        var trimmedKeyword = keyword.Trim();
        var states = await GameDb.FindListAsync<PlayerState>(state => state.Name != null && state.Name.Contains(trimmedKeyword), cancellationToken);
        return states.OrderBy(state => state.Id)
            .Take(limit)
            .Select(state => new OnlinePlayerDirectoryEntry
            {
                PlayerId = state.Id,
                Name = state.Name,
                ServerId = GlobalSettings.CurrentSetting == null ? 0 : GlobalSettings.CurrentSetting.ServerId,
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OnlinePlayerDirectoryEntry>> FindAsync(long tenantId, long appId, IReadOnlyList<long> playerIds, CancellationToken cancellationToken = default)
    {
        if (playerIds == null || playerIds.Count == 0)
        {
            return new List<OnlinePlayerDirectoryEntry>();
        }

        var states = await GameDb.FindByIdsAsync<PlayerState>(playerIds, cancellationToken);
        return states.Where(state => state.Name != null)
            .OrderBy(state => state.Id)
            .Select(state => new OnlinePlayerDirectoryEntry
            {
                PlayerId = state.Id,
                Name = state.Name,
                ServerId = GlobalSettings.CurrentSetting == null ? 0 : GlobalSettings.CurrentSetting.ServerId,
            })
            .ToList();
    }
}
