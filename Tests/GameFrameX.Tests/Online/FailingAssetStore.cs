// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Contracts;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 发放通道瞬时故障注入资产存储（VC-7.6-b 证据件）：让指定玩家的落账前 N 次返回可重试失败，
    /// 之后恢复正常——模拟生产上单玩家发放通道抖动（依赖不可用），而**其余玩家照常到账**。
    /// <para>
    /// 走的是统一入口的软失败分支（存储返回 Failed），因此幂等键会被入口释放、重试会重新执行——
    /// 这正是「重试补齐且不重复」需要验证的那条路径；异常中断分支（落账状态未知）由 C95 用例覆盖。
    /// </para>
    /// </summary>
    internal sealed class FailingAssetStore : IOnlineAssetStore
    {
        /// <summary>被包装存储。</summary>
        private readonly IOnlineAssetStore _inner;

        /// <summary>
        /// 初始化 <see cref="FailingAssetStore"/>。
        /// </summary>
        /// <param name="inner">被包装存储。</param>
        public FailingAssetStore(IOnlineAssetStore inner)
        {
            _inner = inner;
        }

        /// <summary>
        /// 获取或设置被注入故障的玩家标识。
        /// </summary>
        public long FailingPlayerId
        {
            get;
            set;
        }

        /// <summary>
        /// 获取或设置注入的失败次数（达到次数后该玩家恢复可发放）。
        /// </summary>
        public int FailTimes
        {
            get;
            set;
        }

        /// <summary>
        /// 获取实际注入的失败次数（断言故障确实发生过，避免用例静默退化为全成功）。
        /// </summary>
        public int FailuresInjected
        {
            get;
            private set;
        }

        /// <summary>
        /// 对目标玩家的前 <see cref="FailTimes"/> 次落账注入依赖不可用的软失败，之后转发内部存储正常执行。
        /// </summary>
        /// <remarks>
        /// Injects a retryable dependency-unavailable soft failure for the first
        /// <see cref="FailTimes"/> applies of the target player, then forwards
        /// to the inner store unchanged.
        /// </remarks>
        /// <param name="batch">变更批次 / The change batch</param>
        /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
        /// <returns>注入失败时返回依赖不可用的失败结果，否则为内部存储的应用结果 / The injected dependency-unavailable failure, otherwise the inner store's apply result</returns>
        public Task<OnlineAssetApplyResult> ApplyAsync(OnlineAssetChangeBatch batch, CancellationToken cancellationToken = default)
        {
            if (batch.PlayerId == FailingPlayerId && FailuresInjected < FailTimes)
            {
                FailuresInjected++;
                return Task.FromResult(new OnlineAssetApplyResult(false, OnlineErrorCode.DependencyUnavailable, "注入：发放通道瞬时不可用", null));
            }

            return _inner.ApplyAsync(batch, cancellationToken);
        }

        /// <summary>
        /// 直接转发内部存储查找货币账户快照。
        /// </summary>
        /// <remarks>
        /// Forwards directly to the inner store to find the wallet account snapshot.
        /// </remarks>
        /// <param name="tenantId">租户标识 / Tenant id</param>
        /// <param name="appId">App 标识 / App id</param>
        /// <param name="playerId">玩家标识 / Player id</param>
        /// <param name="currencyId">货币代码 / Currency code</param>
        /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
        /// <returns>内部存储返回的账户快照（从未变更时为 null）/ The wallet account snapshot from the inner store (null when no change has occurred)</returns>
        public Task<OnlineWalletAccount> FindWalletAsync(long tenantId, long appId, long playerId, string currencyId, CancellationToken cancellationToken = default)
        {
            return _inner.FindWalletAsync(tenantId, appId, playerId, currencyId, cancellationToken);
        }

        /// <summary>
        /// 直接转发内部存储查找道具库存堆栈快照。
        /// </summary>
        /// <remarks>
        /// Forwards directly to the inner store to find the inventory stack snapshot.
        /// </remarks>
        /// <param name="tenantId">租户标识 / Tenant id</param>
        /// <param name="appId">App 标识 / App id</param>
        /// <param name="playerId">玩家标识 / Player id</param>
        /// <param name="itemId">道具标识 / Item id</param>
        /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
        /// <returns>内部存储返回的库存快照（从未变更时为 null）/ The inventory stack snapshot from the inner store (null when no change has occurred)</returns>
        public Task<OnlineInventoryStack> FindInventoryAsync(long tenantId, long appId, long playerId, string itemId, CancellationToken cancellationToken = default)
        {
            return _inner.FindInventoryAsync(tenantId, appId, playerId, itemId, cancellationToken);
        }

        /// <summary>
        /// 直接转发内部存储列举玩家全部货币账户快照。
        /// </summary>
        /// <remarks>
        /// Forwards directly to the inner store to list all wallet account snapshots of the player.
        /// </remarks>
        /// <param name="tenantId">租户标识 / Tenant id</param>
        /// <param name="appId">App 标识 / App id</param>
        /// <param name="playerId">玩家标识 / Player id</param>
        /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
        /// <returns>内部存储返回的账户快照列表 / The wallet account snapshot list from the inner store</returns>
        public Task<IReadOnlyList<OnlineWalletAccount>> ListWalletAccountsAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
        {
            return _inner.ListWalletAccountsAsync(tenantId, appId, playerId, cancellationToken);
        }

        /// <summary>
        /// 直接转发内部存储列举玩家全部道具库存快照。
        /// </summary>
        /// <remarks>
        /// Forwards directly to the inner store to list all inventory stack snapshots of the player.
        /// </remarks>
        /// <param name="tenantId">租户标识 / Tenant id</param>
        /// <param name="appId">App 标识 / App id</param>
        /// <param name="playerId">玩家标识 / Player id</param>
        /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
        /// <returns>内部存储返回的库存快照列表 / The inventory stack snapshot list from the inner store</returns>
        public Task<IReadOnlyList<OnlineInventoryStack>> ListInventoryStacksAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
        {
            return _inner.ListInventoryStacksAsync(tenantId, appId, playerId, cancellationToken);
        }

        /// <summary>
        /// 直接转发内部存储按账本序分页列举玩家账本条目。
        /// </summary>
        /// <remarks>
        /// Forwards directly to the inner store to list ledger entries page by ledger sequence.
        /// </remarks>
        /// <param name="tenantId">租户标识 / Tenant id</param>
        /// <param name="appId">App 标识 / App id</param>
        /// <param name="playerId">玩家标识 / Player id</param>
        /// <param name="afterSequenceNumber">游标（起始序，不含）/ Cursor (exclusive starting sequence)</param>
        /// <param name="maxCount">最大返回条数 / Maximum number of entries to return</param>
        /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
        /// <returns>内部存储返回的账本条目列表 / The ledger entry list from the inner store</returns>
        public Task<IReadOnlyList<OnlineLedgerEntry>> ListLedgerEntriesAsync(long tenantId, long appId, long playerId, long afterSequenceNumber, int maxCount, CancellationToken cancellationToken = default)
        {
            return _inner.ListLedgerEntriesAsync(tenantId, appId, playerId, afterSequenceNumber, maxCount, cancellationToken);
        }

        /// <summary>
        /// 直接转发内部存储按交易标识反查账本条目。
        /// </summary>
        /// <remarks>
        /// Forwards directly to the inner store to find ledger entries by transaction id.
        /// </remarks>
        /// <param name="transactionId">交易标识 / Transaction id</param>
        /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
        /// <returns>内部存储返回的该交易全部账本条目 / All ledger entries of the transaction from the inner store</returns>
        public Task<IReadOnlyList<OnlineLedgerEntry>> FindLedgerEntriesByTransactionIdAsync(string transactionId, CancellationToken cancellationToken = default)
        {
            return _inner.FindLedgerEntriesByTransactionIdAsync(transactionId, cancellationToken);
        }

        /// <summary>
        /// 直接转发内部存储聚合玩家账本带符号数额。
        /// </summary>
        /// <remarks>
        /// Forwards directly to the inner store to sum signed ledger deltas per asset.
        /// </remarks>
        /// <param name="tenantId">租户标识 / Tenant id</param>
        /// <param name="appId">App 标识 / App id</param>
        /// <param name="playerId">玩家标识 / Player id</param>
        /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
        /// <returns>内部存储返回的各资产账本累加 / The per-asset ledger sums from the inner store</returns>
        public Task<IReadOnlyDictionary<string, long>> SumLedgerDeltasByAssetAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
        {
            return _inner.SumLedgerDeltasByAssetAsync(tenantId, appId, playerId, cancellationToken);
        }

        /// <summary>
        /// 直接转发内部存储列举 (租户, App) 下发生过资产活动的玩家。
        /// </summary>
        /// <remarks>
        /// Forwards directly to the inner store to list players with asset activity under the (tenant, app) scope.
        /// </remarks>
        /// <param name="tenantId">租户标识 / Tenant id</param>
        /// <param name="appId">App 标识 / App id</param>
        /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
        /// <returns>内部存储返回的玩家标识列表 / The player id list from the inner store</returns>
        public Task<IReadOnlyList<long>> ListPlayerIdsAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
        {
            return _inner.ListPlayerIdsAsync(tenantId, appId, cancellationToken);
        }
    }
}
