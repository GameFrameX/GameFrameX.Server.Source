// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯其他合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others that are prohibited by laws and regulations!
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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.Online.Assets;

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Scope;

/// <summary>
/// 资产对账服务（vault:C4 S3.10/VC-3.13：账本累加 == 余额/库存快照；支持按玩家增量与全量巡检）。
/// <para>
/// 维护约束（红线）：账本是事实源——快照与账本不一致即差异（差异 = 0 红线的守护）；
/// 对账只读，不修正（修正只能经统一入口追加交易）；巡检节奏与告警接线归运维共担项
/// （本服务输出结构化差异供消费）。
/// </para>
/// </summary>
public sealed class OnlineAssetReconciliationService
{
    /// <summary>资产存储。</summary>
    private readonly IOnlineAssetStore _assetStore;

    /// <summary>
    /// 初始化 <see cref="OnlineAssetReconciliationService"/>。
    /// </summary>
    /// <param name="assetStore">资产存储。</param>
    public OnlineAssetReconciliationService(IOnlineAssetStore assetStore)
    {
        _assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
    }

    /// <summary>
    /// 对单个玩家对账（增量巡检入口）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对账报告。</returns>
    public async Task<OnlineAssetReconciliationReport> ReconcilePlayerAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (scope.PlayerId <= 0)
        {
            throw new ArgumentException("对账必须绑定玩家主体位", nameof(scope));
        }

        var differences = await CollectDifferencesAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        return new OnlineAssetReconciliationReport(1, differences);
    }

    /// <summary>
    /// 对 (租户, App) 下全部有资产活动的玩家对账（全量巡检入口）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对账报告。</returns>
    public async Task<OnlineAssetReconciliationReport> ReconcileAllAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        var playerIds = await _assetStore.ListPlayerIdsAsync(tenantId, appId, cancellationToken);
        var differences = new List<OnlineAssetReconciliationReport.Difference>();
        foreach (var playerId in playerIds)
        {
            var playerDifferences = await CollectDifferencesAsync(tenantId, appId, playerId, cancellationToken);
            differences.AddRange(playerDifferences);
        }

        return new OnlineAssetReconciliationReport(playerIds.Count, differences);
    }

    /// <summary>
    /// 收集单玩家差异（账本累加 vs 快照，双向并集比对）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>差异列表。</returns>
    private async Task<List<OnlineAssetReconciliationReport.Difference>> CollectDifferencesAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken)
    {
        var differences = new List<OnlineAssetReconciliationReport.Difference>();
        var ledgerSums = await _assetStore.SumLedgerDeltasByAssetAsync(tenantId, appId, playerId, cancellationToken);
        var wallets = await _assetStore.ListWalletAccountsAsync(tenantId, appId, playerId, cancellationToken);
        var stacks = await _assetStore.ListInventoryStacksAsync(tenantId, appId, playerId, cancellationToken);

        var snapshotByAsset = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var wallet in wallets)
        {
            snapshotByAsset["c:" + wallet.CurrencyId] = wallet.Balance;
        }

        foreach (var stack in stacks)
        {
            snapshotByAsset["i:" + stack.ItemId] = stack.Quantity;
        }

        foreach (var pair in snapshotByAsset)
        {
            ledgerSums.TryGetValue(pair.Key, out var ledgerSum);
            if (ledgerSum != pair.Value)
            {
                differences.Add(BuildDifference(playerId, pair.Key, pair.Value, ledgerSum));
            }
        }

        foreach (var pair in ledgerSums)
        {
            if (!snapshotByAsset.ContainsKey(pair.Key))
            {
                differences.Add(BuildDifference(playerId, pair.Key, 0, pair.Value));
            }
        }

        return differences;
    }

    /// <summary>
    /// 构造差异记录（解析资产键前缀）。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="assetKey">资产键（c:/i: 前缀）。</param>
    /// <param name="snapshotAmount">快照侧数值。</param>
    /// <param name="ledgerSumAmount">账本侧数值。</param>
    /// <returns>差异记录。</returns>
    private static OnlineAssetReconciliationReport.Difference BuildDifference(long playerId, string assetKey, long snapshotAmount, long ledgerSumAmount)
    {
        var isCurrency = assetKey.StartsWith("c:", StringComparison.Ordinal);
        var assetId = assetKey.Substring(2);
        return new OnlineAssetReconciliationReport.Difference(playerId, isCurrency ? OnlineAssetKind.Currency : OnlineAssetKind.Item, assetId, snapshotAmount, ledgerSumAmount);
    }
}
