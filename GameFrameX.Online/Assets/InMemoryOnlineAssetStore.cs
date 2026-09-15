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
using GameFrameX.Online.Contracts;

/// <summary>
/// 资产存储内存默认实现（vault:C4 S3.3：单进程/测试默认实现；生产装配以持久化实现替换）。
/// <para>
/// 维护约束（天花板）：应用在<b>玩家维度分片锁</b>内原子完成（对齐 vault 风险缓解「按玩家维度分片事务」，
/// 不同玩家互不阻塞；VC-3.5 并发扣除的内存形态——并发正确性目标，非容量目标）；
/// 先全量校验（同批次资产唯一、应用后下限 0）后统一落账，失败净效应为 0（VC-3.6）；
/// 账本条目落账即事实，本实现无任何修改/删除路径（VC-3.7）；全量驻留内存，
/// 重启数据归持久化实现。快照与已提交版本分离记账（防调用方原位变更快照对象反推状态）。
/// </para>
/// </summary>
public sealed class InMemoryOnlineAssetStore : IOnlineAssetStore
{
    /// <summary>分片锁注册表守卫锁。</summary>
    private readonly object _registryLock = new object();

    /// <summary>玩家分片锁注册表：键 = tenant:app:player。</summary>
    private readonly Dictionary<string, object> _playerLocks = new Dictionary<string, object>();

    /// <summary>货币账户快照：键 = tenant:app:player:货币代码。</summary>
    private readonly Dictionary<string, OnlineWalletAccount> _wallets = new Dictionary<string, OnlineWalletAccount>();

    /// <summary>道具库存快照：键 = tenant:app:player:道具标识。</summary>
    private readonly Dictionary<string, OnlineInventoryStack> _inventories = new Dictionary<string, OnlineInventoryStack>();

    /// <summary>账本条目（按玩家归组，组内按账本序升序）：键 = tenant:app:player。</summary>
    private readonly Dictionary<string, List<OnlineLedgerEntry>> _ledgerByPlayer = new Dictionary<string, List<OnlineLedgerEntry>>();

    /// <summary>交易 → 账本条目反查索引（追加时维护）。</summary>
    private readonly Dictionary<string, List<OnlineLedgerEntry>> _ledgerByTransaction = new Dictionary<string, List<OnlineLedgerEntry>>();

    /// <summary>玩家维度账本序计数器：键 = tenant:app:player。</summary>
    private readonly Dictionary<string, long> _sequenceByPlayer = new Dictionary<string, long>();

    /// <summary>查找货币账户快照。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="currencyId">货币代码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账户快照；未发生过变更返回 null。</returns>
    public Task<OnlineWalletAccount> FindWalletAsync(long tenantId, long appId, long playerId, string currencyId, CancellationToken cancellationToken = default)
    {
        lock (_registryLock)
        {
            _wallets.TryGetValue(BuildAssetKey(tenantId, appId, playerId, OnlineAssetKind.Currency, currencyId), out var account);
            return Task.FromResult(account);
        }
    }

    /// <summary>查找道具库存快照。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="itemId">道具标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>库存快照；未发生过变更返回 null。</returns>
    public Task<OnlineInventoryStack> FindInventoryAsync(long tenantId, long appId, long playerId, string itemId, CancellationToken cancellationToken = default)
    {
        lock (_registryLock)
        {
            _inventories.TryGetValue(BuildAssetKey(tenantId, appId, playerId, OnlineAssetKind.Item, itemId), out var stack);
            return Task.FromResult(stack);
        }
    }

    /// <summary>列举玩家全部货币账户快照。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账户快照列表（按货币代码字典序）。</returns>
    public Task<IReadOnlyList<OnlineWalletAccount>> ListWalletAccountsAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        lock (_registryLock)
        {
            var prefix = BuildPlayerPrefix(tenantId, appId, playerId);
            var accounts = new List<OnlineWalletAccount>();
            foreach (var pair in _wallets)
            {
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    accounts.Add(pair.Value);
                }
            }

            accounts.Sort((left, right) => string.CompareOrdinal(left.CurrencyId, right.CurrencyId));
            return Task.FromResult<IReadOnlyList<OnlineWalletAccount>>(accounts);
        }
    }

    /// <summary>列举玩家全部道具库存快照。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>库存快照列表（按道具标识字典序）。</returns>
    public Task<IReadOnlyList<OnlineInventoryStack>> ListInventoryStacksAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        lock (_registryLock)
        {
            var prefix = BuildPlayerPrefix(tenantId, appId, playerId);
            var stacks = new List<OnlineInventoryStack>();
            foreach (var pair in _inventories)
            {
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    stacks.Add(pair.Value);
                }
            }

            stacks.Sort((left, right) => string.CompareOrdinal(left.ItemId, right.ItemId));
            return Task.FromResult<IReadOnlyList<OnlineInventoryStack>>(stacks);
        }
    }

    /// <summary>原子应用变更批次（玩家分片锁内先全量校验后统一落账）。</summary>
    /// <param name="batch">变更批次。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>应用结果（失败净效应为 0）。</returns>
    public Task<OnlineAssetApplyResult> ApplyAsync(OnlineAssetChangeBatch batch, CancellationToken cancellationToken = default)
    {
        if (batch == null)
        {
            throw new ArgumentNullException(nameof(batch));
        }

        var playerLock = GetPlayerLock(batch.TenantId, batch.AppId, batch.PlayerId);
        lock (playerLock)
        {
            // 校验 1：同批次同一 (类别, 资产标识) 只允许一行。
            var seenAssets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in batch.Lines)
            {
                if (!seenAssets.Add(BuildAssetKey(batch.TenantId, batch.AppId, batch.PlayerId, line.AssetKind, line.AssetId)))
                {
                    return Task.FromResult(new OnlineAssetApplyResult(false, OnlineErrorCode.ParameterInvalid, "同一批次内资产重复：" + line.AssetId, Array.Empty<OnlineLedgerEntry>()));
                }
            }

            // 校验 2：全量预演——应用后下限 0（先算后写，任何一行不满足则整批拒绝）。
            var occurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var planned = new List<OnlineLedgerEntry>(batch.Lines.Count);
            foreach (var line in batch.Lines)
            {
                long amountBefore;
                if (line.AssetKind == OnlineAssetKind.Currency)
                {
                    amountBefore = _wallets.TryGetValue(BuildAssetKey(batch.TenantId, batch.AppId, batch.PlayerId, line.AssetKind, line.AssetId), out var wallet) ? wallet.Balance : 0;
                }
                else
                {
                    amountBefore = _inventories.TryGetValue(BuildAssetKey(batch.TenantId, batch.AppId, batch.PlayerId, line.AssetKind, line.AssetId), out var stack) ? stack.Quantity : 0;
                }

                var amountAfter = amountBefore + line.Amount;
                if (amountAfter < 0)
                {
                    return Task.FromResult(new OnlineAssetApplyResult(false, OnlineErrorCode.StateOperationForbidden, "资产不足：" + line.AssetId + "（当前 " + amountBefore + "，拟变更 " + line.Amount + "）", Array.Empty<OnlineLedgerEntry>()));
                }

                planned.Add(BuildEntry(batch, line, amountBefore, amountAfter, occurredTime));
            }

            // 落账：快照同步 + 账本追加 + 索引维护（分片锁内原子，中途不可见）。
            var playerKey = BuildPlayerKey(batch.TenantId, batch.AppId, batch.PlayerId);
            foreach (var entry in planned)
            {
                if (entry.AssetKind == OnlineAssetKind.Currency)
                {
                    var assetKey = BuildAssetKey(batch.TenantId, batch.AppId, batch.PlayerId, entry.AssetKind, entry.AssetId);
                    if (!_wallets.TryGetValue(assetKey, out var wallet))
                    {
                        wallet = new OnlineWalletAccount { TenantId = entry.TenantId, AppId = entry.AppId, PlayerId = entry.PlayerId, CurrencyId = entry.AssetId, Balance = 0, Version = 0 };
                        _wallets[assetKey] = wallet;
                    }

                    wallet.Balance = entry.AmountAfter;
                    wallet.Version++;
                    wallet.UpdatedTime = occurredTime;
                }
                else
                {
                    var assetKey = BuildAssetKey(batch.TenantId, batch.AppId, batch.PlayerId, entry.AssetKind, entry.AssetId);
                    if (!_inventories.TryGetValue(assetKey, out var stack))
                    {
                        stack = new OnlineInventoryStack { TenantId = entry.TenantId, AppId = entry.AppId, PlayerId = entry.PlayerId, ItemId = entry.AssetId, Quantity = 0, Version = 0 };
                        _inventories[assetKey] = stack;
                    }

                    stack.Quantity = entry.AmountAfter;
                    stack.Version++;
                    stack.UpdatedTime = occurredTime;
                }

                if (!_ledgerByPlayer.TryGetValue(playerKey, out var playerLedger))
                {
                    playerLedger = new List<OnlineLedgerEntry>();
                    _ledgerByPlayer[playerKey] = playerLedger;
                }

                playerLedger.Add(entry);
                if (!_ledgerByTransaction.TryGetValue(entry.TransactionId, out var transactionLedger))
                {
                    transactionLedger = new List<OnlineLedgerEntry>();
                    _ledgerByTransaction[entry.TransactionId] = transactionLedger;
                }

                transactionLedger.Add(entry);
            }

            return Task.FromResult(new OnlineAssetApplyResult(true, OnlineErrorCode.None, string.Empty, planned));
        }
    }

    /// <summary>按账本序分页列举玩家账本条目。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="afterSequenceNumber">游标（起始序，不含；0 = 从头）。</param>
    /// <param name="maxCount">最大返回条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账本条目列表（按序升序）。</returns>
    public Task<IReadOnlyList<OnlineLedgerEntry>> ListLedgerEntriesAsync(long tenantId, long appId, long playerId, long afterSequenceNumber, int maxCount, CancellationToken cancellationToken = default)
    {
        lock (_registryLock)
        {
            if (!_ledgerByPlayer.TryGetValue(BuildPlayerKey(tenantId, appId, playerId), out var ledger))
            {
                return Task.FromResult<IReadOnlyList<OnlineLedgerEntry>>(Array.Empty<OnlineLedgerEntry>());
            }

            var matches = new List<OnlineLedgerEntry>();
            foreach (var entry in ledger)
            {
                if (entry.SequenceNumber > afterSequenceNumber)
                {
                    matches.Add(entry);
                    if (matches.Count >= maxCount)
                    {
                        break;
                    }
                }
            }

            return Task.FromResult<IReadOnlyList<OnlineLedgerEntry>>(matches);
        }
    }

    /// <summary>按交易标识反查账本条目。</summary>
    /// <param name="transactionId">交易标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该交易的全部账本条目。</returns>
    public Task<IReadOnlyList<OnlineLedgerEntry>> FindLedgerEntriesByTransactionIdAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        lock (_registryLock)
        {
            if (!_ledgerByTransaction.TryGetValue(transactionId, out var entries))
            {
                return Task.FromResult<IReadOnlyList<OnlineLedgerEntry>>(Array.Empty<OnlineLedgerEntry>());
            }

            return Task.FromResult<IReadOnlyList<OnlineLedgerEntry>>(entries.ToList());
        }
    }

    /// <summary>聚合玩家账本带符号数额。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>各资产账本累加。</returns>
    public Task<IReadOnlyDictionary<string, long>> SumLedgerDeltasByAssetAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        lock (_registryLock)
        {
            var sums = new Dictionary<string, long>();
            if (_ledgerByPlayer.TryGetValue(BuildPlayerKey(tenantId, appId, playerId), out var ledger))
            {
                foreach (var entry in ledger)
                {
                    var assetKey = (entry.AssetKind == OnlineAssetKind.Currency ? "c:" : "i:") + entry.AssetId;
                    sums.TryGetValue(assetKey, out var current);
                    sums[assetKey] = current + entry.Delta;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, long>>(sums);
        }
    }

    /// <summary>列举发生过资产活动的玩家。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家标识列表（升序去重）。</returns>
    public Task<IReadOnlyList<long>> ListPlayerIdsAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        lock (_registryLock)
        {
            var prefix = tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + appId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":";
            var playerIds = new HashSet<long>();
            foreach (var pair in _ledgerByPlayer)
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var segments = pair.Key.Split(':');
                if (segments.Length == 3 && long.TryParse(segments[2], out var playerId))
                {
                    playerIds.Add(playerId);
                }
            }

            var ordered = playerIds.ToList();
            ordered.Sort();
            return Task.FromResult<IReadOnlyList<long>>(ordered);
        }
    }

    /// <summary>
    /// 构造账本条目并分配玩家维度账本序（必须在玩家分片锁内调用）。
    /// </summary>
    /// <param name="batch">变更批次。</param>
    /// <param name="line">变更行。</param>
    /// <param name="amountBefore">变更前数量。</param>
    /// <param name="amountAfter">变更后数量。</param>
    /// <param name="occurredTime">落账时刻。</param>
    /// <returns>账本条目。</returns>
    private OnlineLedgerEntry BuildEntry(OnlineAssetChangeBatch batch, OnlineAssetChangeLine line, long amountBefore, long amountAfter, long occurredTime)
    {
        var playerKey = BuildPlayerKey(batch.TenantId, batch.AppId, batch.PlayerId);
        _sequenceByPlayer.TryGetValue(playerKey, out var sequence);
        sequence++;
        _sequenceByPlayer[playerKey] = sequence;
        return new OnlineLedgerEntry("led-" + Guid.NewGuid().ToString("N"), batch.TransactionId, batch.TenantId, batch.AppId, batch.PlayerId, batch.HomeServerId, batch.InitiatingServerId, batch.Source, batch.Operation, batch.Reason, batch.BusinessOrderId, batch.OperatorId, line.AssetKind, line.AssetId, amountBefore, line.Amount, amountAfter, batch.CompensatesTransactionId, sequence, occurredTime);
    }

    /// <summary>获取玩家分片锁（不存在则注册）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>分片锁对象。</returns>
    private object GetPlayerLock(long tenantId, long appId, long playerId)
    {
        var playerKey = BuildPlayerKey(tenantId, appId, playerId);
        lock (_registryLock)
        {
            if (!_playerLocks.TryGetValue(playerKey, out var playerLock))
            {
                playerLock = new object();
                _playerLocks[playerKey] = playerLock;
            }

            return playerLock;
        }
    }

    /// <summary>构造玩家键（tenant:app:player）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>玩家键。</returns>
    private static string BuildPlayerKey(long tenantId, long appId, long playerId)
    {
        return tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + appId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + playerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>构造玩家前缀键（tenant:app:player:）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>前缀键。</returns>
    private static string BuildPlayerPrefix(long tenantId, long appId, long playerId)
    {
        return BuildPlayerKey(tenantId, appId, playerId) + ":";
    }

    /// <summary>构造资产键（tenant:app:player:类别:资产标识）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="assetKind">资产类别。</param>
    /// <param name="assetId">资产标识。</param>
    /// <returns>资产键。</returns>
    private static string BuildAssetKey(long tenantId, long appId, long playerId, OnlineAssetKind assetKind, string assetId)
    {
        return BuildPlayerPrefix(tenantId, appId, playerId) + (assetKind == OnlineAssetKind.Currency ? "c" : "i") + ":" + assetId;
    }
}
