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

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.DataBase;

/// <summary>
/// 资产交易记录 GameDb 持久化适配（C249 一级 store；交易幂等边界的持久化落库）。
/// <para>
/// 键面语义：<see cref="OnlineAssetTransactionRowState.TransactionId"/> 单列唯一索引为整体覆盖键——
/// <c>SaveAsync</c> 既有交易保行主键全量覆盖（状态迁移由统一入口状态机保证合法，终态不可再迁移），
/// 新交易行 Id 序列分配 + 唯一插入（冲突读回覆盖兜底，单写者假设防御路径）；
/// <c>ListByStateAsync</c> 按创建时刻升序（并列按交易标识定序），供恢复任务与运维巡检消费。
/// 重启恢复的扫描数据源即本表（持久化形态下非终态记录重启可检回）。
/// </para>
/// </summary>
public sealed class GameDbOnlineAssetTransactionStore : IOnlineAssetTransactionStore
{
    /// <summary>
    /// 行 Id 分配序列名。
    /// </summary>
    private const string RowIdSequence = "row-id";

    /// <summary>按交易标识查找。</summary>
    /// <param name="transactionId">交易标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>交易记录；不存在返回 null。</returns>
    public async Task<OnlineAssetTransaction> FindAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        var state = await FindStateAsync(transactionId, cancellationToken).ConfigureAwait(false);
        return state == null ? null : ToEntity(state);
    }

    /// <summary>保存交易记录（以交易标识为键整体覆盖；状态迁移由统一入口保证合法）。</summary>
    /// <param name="transaction">交易记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task SaveAsync(OnlineAssetTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var existing = await FindStateAsync(transaction.TransactionId, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            var updated = ToState(transaction);
            updated.Id = existing.Id;
            await GameDb.AddOrUpdateAsync(updated, cancellationToken).ConfigureAwait(false);
            return;
        }

        var state = ToState(transaction);
        state.Id = await GameDb.NextSequenceAsync<OnlineAssetTransactionRowState>(RowIdSequence, cancellationToken).ConfigureAwait(false);
        if (await GameDb.TryAddUniqueAsync(state, candidate => candidate.TransactionId == transaction.TransactionId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // 唯一插入冲突（并发窗口落者）：读回胜者行按行主键覆盖——SaveAsync 整体覆盖语义不丢失。
        var winner = await FindStateAsync(transaction.TransactionId, cancellationToken).ConfigureAwait(false);
        if (winner == null)
        {
            throw new InvalidOperationException("唯一插入冲突后未能读回既有交易行：" + transaction.TransactionId);
        }

        state.Id = winner.Id;
        await GameDb.AddOrUpdateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>按状态列举交易记录（恢复与巡检输入；按创建时刻升序，并列按交易标识定序）。</summary>
    /// <param name="state">目标状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配状态的交易记录列表。</returns>
    public async Task<IReadOnlyList<OnlineAssetTransaction>> ListByStateAsync(OnlineAssetTransactionState state, CancellationToken cancellationToken = default)
    {
        var target = (int)state;
        var states = await GameDb.FindListAsync<OnlineAssetTransactionRowState>(candidate => candidate.State == target, cancellationToken).ConfigureAwait(false);
        return states.OrderBy(candidate => candidate.CreatedTime).ThenBy(candidate => candidate.TransactionId, StringComparer.Ordinal).Select(ToEntity).ToList();
    }

    /// <summary>按交易标识查找交易行。</summary>
    /// <param name="transactionId">交易标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>交易行；不存在返回 null。</returns>
    private static Task<OnlineAssetTransactionRowState> FindStateAsync(string transactionId, CancellationToken cancellationToken)
    {
        return GameDb.FindAsync<OnlineAssetTransactionRowState>(state => state.TransactionId == transactionId, isCreateIfNotExists: false, cancellationToken: cancellationToken);
    }

    /// <summary>交易实体 → 行（RowVersion 无版本语义，原样缺省）。</summary>
    private static OnlineAssetTransactionRowState ToState(OnlineAssetTransaction transaction)
    {
        return new OnlineAssetTransactionRowState
        {
            TransactionId = transaction.TransactionId,
            IdempotencyKey = transaction.IdempotencyKey,
            RequestDigest = transaction.RequestDigest,
            TenantId = transaction.TenantId,
            AppId = transaction.AppId,
            PlayerId = transaction.PlayerId,
            HomeServerId = transaction.HomeServerId,
            InitiatingServerId = transaction.InitiatingServerId,
            Source = (int)transaction.Source,
            Operation = (int)transaction.Operation,
            Reason = transaction.Reason,
            BusinessOrderId = transaction.BusinessOrderId,
            OperatorId = transaction.OperatorId,
            ExpectedChangeCount = transaction.ExpectedChangeCount,
            State = (int)transaction.State,
            AppliedLedgerEntryIds = transaction.AppliedLedgerEntryIds?.ToList(),
            CreatedTime = transaction.CreatedTime,
            SettledTime = transaction.SettledTime,
            FailureMessage = transaction.FailureMessage,
        };
    }

    /// <summary>交易行 → 实体。</summary>
    private static OnlineAssetTransaction ToEntity(OnlineAssetTransactionRowState state)
    {
        return new OnlineAssetTransaction
        {
            TransactionId = state.TransactionId,
            IdempotencyKey = state.IdempotencyKey,
            RequestDigest = state.RequestDigest,
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
            ExpectedChangeCount = state.ExpectedChangeCount,
            State = (OnlineAssetTransactionState)state.State,
            AppliedLedgerEntryIds = state.AppliedLedgerEntryIds?.ToList(),
            CreatedTime = state.CreatedTime,
            SettledTime = state.SettledTime,
            FailureMessage = state.FailureMessage,
        };
    }
}
