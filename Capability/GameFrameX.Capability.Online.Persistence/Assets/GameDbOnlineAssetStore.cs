// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Persistence.Assets;

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.DataBase;

/// <summary>
/// 资产账本 GameDb 持久化适配（一级 store；余额/流水重启不丢的资产红线落地）。
/// <para>
/// <see cref="ApplyAsync"/> 玩家分片事务语义：进程内按玩家分片信号量串行
/// （不同玩家互不阻塞；单写者假设承接 store 契约——多进程部署由装配层按玩家归属路由，不引入
/// 分布式协调）；分片内「先全量校验（同批次资产唯一、应用后下限 0）后统一落账，失败净效应 0」。
/// 落账顺序 = 账本条目先行（不可变事实源）→ 快照随后（派生面）：崩溃残留在账本与快照之间时，
/// 以账本重算的对账服务（既有交付面）收敛，余额/流水不丢。
/// </para>
/// <para>
/// 不可变红线：账本行只经插入路径写入（<c>SaveOneAsync</c>），本适配零账本修改/删除调用；
/// 纠正只能追加反转条目（契约面既有约定）。玩家维度账本序由 <c>NextSequenceAsync</c>
/// 玩家键控序列分配（"tenant:app:player"；原子单调、重启不复用）。
/// </para>
/// <para>
/// 列举保真：账本分页排序键为数值账本序（provider 原生排序，无 collation 分歧）；
/// 快照列举（货币/道具字典序）为 provider culture-aware 排序与 InMemory ordinal 不同，采用
/// 客户端 ordinal 排序逐字节对齐 InMemory 语义。
/// </para>
/// </summary>
public sealed class GameDbOnlineAssetStore : IOnlineAssetStore
{
    /// <summary>
    /// 快照行与账本行的行 Id 分配序列名。
    /// </summary>
    private const string RowIdSequence = "row-id";

    /// <summary>
    /// 玩家分片锁注册表守卫锁。
    /// </summary>
    private readonly object _registryLock = new();

    /// <summary>
    /// 玩家分片信号量注册表：键 = "tenant:app:player"（进程内并发正确性目标，同 InMemory 分片锁）。
    /// </summary>
    private readonly Dictionary<string, SemaphoreSlim> _playerGates = new();

    /// <summary>查找货币账户快照。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="currencyId">货币代码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账户快照；从未发生过变更返回 null（视余额为 0）。</returns>
    public async Task<OnlineWalletAccount> FindWalletAsync(long tenantId, long appId, long playerId, string currencyId, CancellationToken cancellationToken = default)
    {
        var state = await FindWalletStateAsync(BuildWalletKey(tenantId, appId, playerId, currencyId), cancellationToken).ConfigureAwait(false);
        return state == null ? null : ToEntity(state);
    }

    /// <summary>查找道具库存堆栈快照。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="itemId">道具标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>库存快照；从未发生过变更返回 null（视数量为 0）。</returns>
    public async Task<OnlineInventoryStack> FindInventoryAsync(long tenantId, long appId, long playerId, string itemId, CancellationToken cancellationToken = default)
    {
        var state = await FindInventoryStateAsync(BuildInventoryKey(tenantId, appId, playerId, itemId), cancellationToken).ConfigureAwait(false);
        return state == null ? null : ToEntity(state);
    }

    /// <summary>列举玩家全部货币账户快照（按货币代码 ordinal 字典序）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账户快照列表。</returns>
    public async Task<IReadOnlyList<OnlineWalletAccount>> ListWalletAccountsAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        var states = await GameDb.FindListAsync<OnlineWalletAccountState>(state => state.TenantId == tenantId && state.AppId == appId && state.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
        return states.OrderBy(state => state.CurrencyId, StringComparer.Ordinal).Select(ToEntity).ToList();
    }

    /// <summary>列举玩家全部道具库存快照（按道具标识 ordinal 字典序）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>库存快照列表。</returns>
    public async Task<IReadOnlyList<OnlineInventoryStack>> ListInventoryStacksAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        var states = await GameDb.FindListAsync<OnlineInventoryStackState>(state => state.TenantId == tenantId && state.AppId == appId && state.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
        return states.OrderBy(state => state.ItemId, StringComparer.Ordinal).Select(ToEntity).ToList();
    }

    /// <summary>
    /// 原子应用变更批次（玩家分片信号量内先全量校验后统一落账；失败净效应为 0）。
    /// </summary>
    /// <param name="batch">变更批次。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>应用结果。</returns>
    public async Task<OnlineAssetApplyResult> ApplyAsync(OnlineAssetChangeBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var gate = GetPlayerGate(batch.TenantId, batch.AppId, batch.PlayerId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 校验 1：同批次同一 (类别, 资产标识) 只允许一行。
            var duplicate = ValidateBatchAssetUniqueness(batch);
            if (duplicate != null)
            {
                return duplicate;
            }

            // 校验 2：全量预演——应用后下限 0（先算后写，任何一行不满足则整批拒绝，净效应为 0）。
            var occurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var (rejection, planned) = await PlanEntriesAsync(batch, occurredTime, cancellationToken).ConfigureAwait(false);
            if (rejection != null)
            {
                return rejection;
            }

            // 落账：账本条目先行（不可变事实源）→ 快照随后（派生面）；玩家分片信号量内串行，中途对外不可见。
            await CommitEntriesAsync(planned, occurredTime, cancellationToken).ConfigureAwait(false);
            return new OnlineAssetApplyResult(true, OnlineErrorCode.None, string.Empty, planned);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>按账本序分页列举玩家账本条目（稳定排序键 = 数值账本序，升序）。</summary>
    /// <param name="query">分页查询载荷。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账本条目列表（按序升序）。</returns>
    public async Task<IReadOnlyList<OnlineLedgerEntry>> ListLedgerEntriesAsync(OnlineLedgerPageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var afterSequenceNumber = query.AfterSequenceNumber;
        var states = await GameDb.FindSortAscendingAsync<OnlineLedgerEntryState>(
            state => state.TenantId == query.TenantId && state.AppId == query.AppId && state.PlayerId == query.PlayerId && state.SequenceNumber > afterSequenceNumber,
            state => state.SequenceNumber, 0, query.MaxCount, cancellationToken).ConfigureAwait(false);
        return states.Select(ToEntity).ToList();
    }

    /// <summary>按交易标识反查账本条目（按账本序升序）。</summary>
    /// <param name="transactionId">交易标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该交易的全部账本条目（含反转条目）。</returns>
    public async Task<IReadOnlyList<OnlineLedgerEntry>> FindLedgerEntriesByTransactionIdAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        var states = await GameDb.FindListAsync<OnlineLedgerEntryState>(state => state.TransactionId == transactionId, cancellationToken).ConfigureAwait(false);
        return states.OrderBy(state => state.SequenceNumber).Select(ToEntity).ToList();
    }

    /// <summary>聚合玩家账本带符号数额（对账的账本侧事实源；键 = 类别前缀 + 资产标识）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>各资产账本累加。</returns>
    public async Task<IReadOnlyDictionary<string, long>> SumLedgerDeltasByAssetAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        var states = await GameDb.FindListAsync<OnlineLedgerEntryState>(state => state.TenantId == tenantId && state.AppId == appId && state.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
        var sums = new Dictionary<string, long>();
        foreach (var state in states)
        {
            var assetKey = (state.AssetKind == (int)OnlineAssetKind.Currency ? "c:" : "i:") + state.AssetId;
            sums.TryGetValue(assetKey, out var current);
            sums[assetKey] = current + state.Delta;
        }

        return sums;
    }

    /// <summary>列举 (租户, App) 下发生过资产活动（账本非空）的玩家（对账遍历输入；升序去重）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家标识列表。</returns>
    public async Task<IReadOnlyList<long>> ListPlayerIdsAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        var states = await GameDb.FindListAsync<OnlineLedgerEntryState>(state => state.TenantId == tenantId && state.AppId == appId, cancellationToken).ConfigureAwait(false);
        return states.Select(state => state.PlayerId).Distinct().OrderBy(playerId => playerId).ToList();
    }

    /// <summary>
    /// 校验 1：同批次同一 (类别, 资产标识) 只允许一行（纯内存判定，无存储访问）。
    /// </summary>
    /// <param name="batch">变更批次。</param>
    /// <returns>存在重复时的失败结果；无重复返回 null。</returns>
    private static OnlineAssetApplyResult ValidateBatchAssetUniqueness(OnlineAssetChangeBatch batch)
    {
        var seenAssets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in batch.Lines)
        {
            if (!seenAssets.Add(BuildAssetKey(batch.TenantId, batch.AppId, batch.PlayerId, line.AssetKind, line.AssetId)))
            {
                return new OnlineAssetApplyResult(false, OnlineErrorCode.ParameterInvalid, "同一批次内资产重复：" + line.AssetId, Array.Empty<OnlineLedgerEntry>());
            }
        }

        return null;
    }

    /// <summary>
    /// 校验 2：全量预演——应用后下限 0（玩家分片信号量内读取快照并构造账本条目）。
    /// </summary>
    /// <param name="batch">变更批次。</param>
    /// <param name="occurredTime">落账时刻。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>（拒绝结果, 预演条目）二元组：拒绝时条目为 null；可落账时拒绝为 null。</returns>
    private async Task<(OnlineAssetApplyResult Rejection, List<OnlineLedgerEntry> Planned)> PlanEntriesAsync(OnlineAssetChangeBatch batch, long occurredTime, CancellationToken cancellationToken)
    {
        var planned = new List<OnlineLedgerEntry>(batch.Lines.Count);
        foreach (var line in batch.Lines)
        {
            var amountBefore = await ReadAmountBeforeAsync(batch, line, cancellationToken).ConfigureAwait(false);
            var amountAfter = amountBefore + line.Amount;
            if (amountAfter < 0)
            {
                return (new OnlineAssetApplyResult(false, OnlineErrorCode.StateOperationForbidden, "资产不足：" + line.AssetId + "（当前 " + amountBefore + "，拟变更 " + line.Amount + "）", Array.Empty<OnlineLedgerEntry>()), null);
            }

            var entry = await BuildEntryAsync(batch, line, amountBefore, amountAfter, occurredTime, cancellationToken).ConfigureAwait(false);
            planned.Add(entry);
        }

        return (null, planned);
    }

    /// <summary>读取资产变更前数量（未发生过变更按 0 计；玩家分片信号量内调用）。</summary>
    /// <param name="batch">变更批次。</param>
    /// <param name="line">变更行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>变更前数量。</returns>
    private static async Task<long> ReadAmountBeforeAsync(OnlineAssetChangeBatch batch, OnlineAssetChangeLine line, CancellationToken cancellationToken)
    {
        if (line.AssetKind == OnlineAssetKind.Currency)
        {
            var wallet = await FindWalletStateAsync(BuildWalletKey(batch.TenantId, batch.AppId, batch.PlayerId, line.AssetId), cancellationToken).ConfigureAwait(false);
            return wallet?.Balance ?? 0;
        }

        var stack = await FindInventoryStateAsync(BuildInventoryKey(batch.TenantId, batch.AppId, batch.PlayerId, line.AssetId), cancellationToken).ConfigureAwait(false);
        return stack?.Quantity ?? 0;
    }

    /// <summary>
    /// 构造账本条目并分配玩家维度账本序（玩家键控数据库序列接管内存计数器；分片信号量内调用）。
    /// </summary>
    /// <param name="batch">变更批次。</param>
    /// <param name="line">变更行。</param>
    /// <param name="amountBefore">变更前数量。</param>
    /// <param name="amountAfter">变更后数量。</param>
    /// <param name="occurredTime">落账时刻。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账本条目。</returns>
    private static async Task<OnlineLedgerEntry> BuildEntryAsync(OnlineAssetChangeBatch batch, OnlineAssetChangeLine line, long amountBefore, long amountAfter, long occurredTime, CancellationToken cancellationToken)
    {
        var sequence = await GameDb.NextSequenceAsync<OnlineLedgerEntryState>(BuildLedgerSequenceKey(batch.TenantId, batch.AppId, batch.PlayerId), cancellationToken).ConfigureAwait(false);
        return new OnlineLedgerEntry(
            "led-" + Guid.NewGuid().ToString("N"),
            batch.TransactionId,
            new OnlineLedgerHeader
            {
                TenantId = batch.TenantId,
                AppId = batch.AppId,
                PlayerId = batch.PlayerId,
                HomeServerId = batch.HomeServerId,
                InitiatingServerId = batch.InitiatingServerId,
                Source = batch.Source,
                Operation = batch.Operation,
                Reason = batch.Reason,
                BusinessOrderId = batch.BusinessOrderId,
                OperatorId = batch.OperatorId,
            },
            line.AssetKind,
            line.AssetId,
            amountBefore,
            line.Amount,
            amountAfter,
            batch.CompensatesTransactionId,
            sequence,
            occurredTime);
    }

    /// <summary>
    /// 落账：账本条目先行（事实源）→ 快照随后（派生面；玩家分片信号量内调用）。
    /// </summary>
    /// <param name="planned">预演构造的账本条目。</param>
    /// <param name="occurredTime">落账时刻。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task CommitEntriesAsync(List<OnlineLedgerEntry> planned, long occurredTime, CancellationToken cancellationToken)
    {
        foreach (var entry in planned)
        {
            var state = ToState(entry);
            state.Id = await GameDb.NextSequenceAsync<OnlineLedgerEntryState>(RowIdSequence, cancellationToken).ConfigureAwait(false);
            await GameDb.SaveOneAsync(state, cancellationToken).ConfigureAwait(false);
            if (entry.AssetKind == OnlineAssetKind.Currency)
            {
                await UpsertWalletAsync(entry, occurredTime, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await UpsertInventoryStackAsync(entry, occurredTime, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>同步货币账户快照（既有行保行主键覆盖，新行唯一插入建档；分片信号量内调用）。</summary>
    /// <param name="entry">账本条目。</param>
    /// <param name="occurredTime">落账时刻。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task UpsertWalletAsync(OnlineLedgerEntry entry, long occurredTime, CancellationToken cancellationToken)
    {
        var uniqueKey = BuildWalletKey(entry.TenantId, entry.AppId, entry.PlayerId, entry.AssetId);
        var existing = await FindWalletStateAsync(uniqueKey, cancellationToken).ConfigureAwait(false);
        var version = (existing?.Version ?? 0) + 1;
        if (existing != null)
        {
            var updated = new OnlineWalletAccountState
            {
                Id = existing.Id,
                UniqueKey = uniqueKey,
                TenantId = entry.TenantId,
                AppId = entry.AppId,
                PlayerId = entry.PlayerId,
                CurrencyId = entry.AssetId,
                Balance = entry.AmountAfter,
                Version = version,
                UpdatedTime = occurredTime,
            };
            await GameDb.AddOrUpdateAsync(updated, cancellationToken).ConfigureAwait(false);
            return;
        }

        var rowId = await GameDb.NextSequenceAsync<OnlineWalletAccountState>(RowIdSequence, cancellationToken).ConfigureAwait(false);
        var created = new OnlineWalletAccountState
        {
            Id = rowId,
            UniqueKey = uniqueKey,
            TenantId = entry.TenantId,
            AppId = entry.AppId,
            PlayerId = entry.PlayerId,
            CurrencyId = entry.AssetId,
            Balance = entry.AmountAfter,
            Version = version,
            UpdatedTime = occurredTime,
        };
        if (!await GameDb.TryAddUniqueAsync(created, state => state.UniqueKey == uniqueKey, cancellationToken).ConfigureAwait(false))
        {
            // 唯一建档冲突（并发窗口落者）：读回胜者行按行主键覆盖（单写者假设下的防御路径）。
            var winner = await FindWalletStateAsync(uniqueKey, cancellationToken).ConfigureAwait(false);
            if (winner == null)
            {
                throw new InvalidOperationException("唯一插入冲突后未能读回既有钱包行：" + uniqueKey);
            }

            created.Id = winner.Id;
            await GameDb.AddOrUpdateAsync(created, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>同步道具库存快照（既有行保行主键覆盖，新行唯一插入建档；分片信号量内调用）。</summary>
    /// <param name="entry">账本条目。</param>
    /// <param name="occurredTime">落账时刻。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task UpsertInventoryStackAsync(OnlineLedgerEntry entry, long occurredTime, CancellationToken cancellationToken)
    {
        var uniqueKey = BuildInventoryKey(entry.TenantId, entry.AppId, entry.PlayerId, entry.AssetId);
        var existing = await FindInventoryStateAsync(uniqueKey, cancellationToken).ConfigureAwait(false);
        var version = (existing?.Version ?? 0) + 1;
        var stack = new OnlineInventoryStackState
        {
            UniqueKey = uniqueKey,
            TenantId = entry.TenantId,
            AppId = entry.AppId,
            PlayerId = entry.PlayerId,
            ItemId = entry.AssetId,
            Quantity = entry.AmountAfter,
            Version = version,
            UpdatedTime = occurredTime,
        };
        if (existing != null)
        {
            stack.Id = existing.Id;
            await GameDb.AddOrUpdateAsync(stack, cancellationToken).ConfigureAwait(false);
            return;
        }

        stack.Id = await GameDb.NextSequenceAsync<OnlineInventoryStackState>(RowIdSequence, cancellationToken).ConfigureAwait(false);
        if (!await GameDb.TryAddUniqueAsync(stack, state => state.UniqueKey == uniqueKey, cancellationToken).ConfigureAwait(false))
        {
            var winner = await FindInventoryStateAsync(uniqueKey, cancellationToken).ConfigureAwait(false);
            if (winner == null)
            {
                throw new InvalidOperationException("唯一插入冲突后未能读回既有库存行：" + uniqueKey);
            }

            stack.Id = winner.Id;
            await GameDb.AddOrUpdateAsync(stack, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>获取玩家分片信号量（不存在则注册）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>分片信号量。</returns>
    private SemaphoreSlim GetPlayerGate(long tenantId, long appId, long playerId)
    {
        var playerKey = BuildLedgerSequenceKey(tenantId, appId, playerId);
        lock (_registryLock)
        {
            if (!_playerGates.TryGetValue(playerKey, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _playerGates[playerKey] = gate;
            }

            return gate;
        }
    }

    /// <summary>查找钱包快照行。</summary>
    private static Task<OnlineWalletAccountState> FindWalletStateAsync(string uniqueKey, CancellationToken cancellationToken)
    {
        return GameDb.FindAsync<OnlineWalletAccountState>(state => state.UniqueKey == uniqueKey, isCreateIfNotExists: false, cancellationToken: cancellationToken);
    }

    /// <summary>查找库存快照行。</summary>
    private static Task<OnlineInventoryStackState> FindInventoryStateAsync(string uniqueKey, CancellationToken cancellationToken)
    {
        return GameDb.FindAsync<OnlineInventoryStackState>(state => state.UniqueKey == uniqueKey, isCreateIfNotExists: false, cancellationToken: cancellationToken);
    }

    /// <summary>构造玩家键控账本序序列名（"tenant:app:player"）。</summary>
    private static string BuildLedgerSequenceKey(long tenantId, long appId, long playerId)
    {
        return tenantId + ":" + appId + ":" + playerId;
    }

    /// <summary>构造钱包物化复合键（"tenant:app:player:货币代码"）。</summary>
    private static string BuildWalletKey(long tenantId, long appId, long playerId, string currencyId)
    {
        return tenantId + ":" + appId + ":" + playerId + ":" + currencyId;
    }

    /// <summary>构造库存物化复合键（"tenant:app:player:道具标识"）。</summary>
    private static string BuildInventoryKey(long tenantId, long appId, long playerId, string itemId)
    {
        return tenantId + ":" + appId + ":" + playerId + ":" + itemId;
    }

    /// <summary>构造批次内资产去重键（与 InMemory BuildAssetKey 同构）。</summary>
    private static string BuildAssetKey(long tenantId, long appId, long playerId, OnlineAssetKind assetKind, string assetId)
    {
        return tenantId + ":" + appId + ":" + playerId + ":" + (assetKind == OnlineAssetKind.Currency ? "c" : "i") + ":" + assetId;
    }

    /// <summary>账本条目 → 行（只追加路径；行 Id 由序列分配）。</summary>
    private static OnlineLedgerEntryState ToState(OnlineLedgerEntry entry)
    {
        return new OnlineLedgerEntryState
        {
            EntryId = entry.EntryId,
            TransactionId = entry.TransactionId,
            TenantId = entry.TenantId,
            AppId = entry.AppId,
            PlayerId = entry.PlayerId,
            HomeServerId = entry.HomeServerId,
            InitiatingServerId = entry.InitiatingServerId,
            Source = (int)entry.Source,
            Operation = (int)entry.Operation,
            Reason = entry.Reason,
            BusinessOrderId = entry.BusinessOrderId,
            OperatorId = entry.OperatorId,
            AssetKind = (int)entry.AssetKind,
            AssetId = entry.AssetId,
            AmountBefore = entry.AmountBefore,
            Delta = entry.Delta,
            AmountAfter = entry.AmountAfter,
            CompensatesTransactionId = entry.CompensatesTransactionId,
            SequenceNumber = entry.SequenceNumber,
            OccurredTime = entry.OccurredTime,
        };
    }

    /// <summary>账本行 → 实体。</summary>
    private static OnlineLedgerEntry ToEntity(OnlineLedgerEntryState state)
    {
        return new OnlineLedgerEntry(
            state.EntryId,
            state.TransactionId,
            new OnlineLedgerHeader
            {
                TenantId = state.TenantId,
                AppId = state.AppId,
                PlayerId = state.PlayerId,
                HomeServerId = state.HomeServerId,
                InitiatingServerId = state.InitiatingServerId,
                Source = (OnlineAssetChangeSource)state.Source,
                Operation = (OnlineGrantOperation)state.Operation,
                Reason = state.Reason,
                BusinessOrderId = state.BusinessOrderId,
                OperatorId = state.OperatorId,
            },
            (OnlineAssetKind)state.AssetKind,
            state.AssetId,
            state.AmountBefore,
            state.Delta,
            state.AmountAfter,
            state.CompensatesTransactionId,
            state.SequenceNumber,
            state.OccurredTime);
    }

    /// <summary>钱包行 → 实体。</summary>
    private static OnlineWalletAccount ToEntity(OnlineWalletAccountState state)
    {
        return new OnlineWalletAccount
        {
            TenantId = state.TenantId,
            AppId = state.AppId,
            PlayerId = state.PlayerId,
            CurrencyId = state.CurrencyId,
            Balance = state.Balance,
            Version = state.Version,
            UpdatedTime = state.UpdatedTime,
        };
    }

    /// <summary>库存行 → 实体。</summary>
    private static OnlineInventoryStack ToEntity(OnlineInventoryStackState state)
    {
        return new OnlineInventoryStack
        {
            TenantId = state.TenantId,
            AppId = state.AppId,
            PlayerId = state.PlayerId,
            ItemId = state.ItemId,
            Quantity = state.Quantity,
            Version = state.Version,
            UpdatedTime = state.UpdatedTime,
        };
    }
}
