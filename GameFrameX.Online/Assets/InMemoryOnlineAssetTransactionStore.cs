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

/// <summary>
/// 资产交易记录内存默认实现（vault:C4 S3.3：单进程/测试默认实现；生产装配以持久化实现替换）。
/// <para>
/// 维护约束（天花板）：全局锁内原子读写；保存以交易标识为键整体覆盖（状态迁移合法性归统一入口）；
/// 全量驻留内存，重启数据归持久化实现（重启恢复语义由持久化实现 + <c>RecoverAsync</c> 承载）。
/// </para>
/// </summary>
public sealed class InMemoryOnlineAssetTransactionStore : IOnlineAssetTransactionStore
{
    /// <summary>全局读写锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>交易记录表：键 = 交易标识。</summary>
    private readonly Dictionary<string, OnlineAssetTransaction> _transactions = new Dictionary<string, OnlineAssetTransaction>();

    /// <summary>按交易标识查找。</summary>
    /// <param name="transactionId">交易标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>交易记录快照；不存在返回 null。</returns>
    public Task<OnlineAssetTransaction> FindAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _transactions.TryGetValue(transactionId, out var transaction);
            return Task.FromResult(transaction == null ? null : Clone(transaction));
        }
    }

    /// <summary>保存交易记录（整体覆盖）。</summary>
    /// <param name="transaction">交易记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task SaveAsync(OnlineAssetTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (transaction == null)
        {
            throw new ArgumentNullException(nameof(transaction));
        }

        lock (_syncRoot)
        {
            _transactions[transaction.TransactionId] = Clone(transaction);
            return Task.CompletedTask;
        }
    }

    /// <summary>按状态列举交易记录（按创建时刻升序）。</summary>
    /// <param name="state">目标状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配状态的交易记录列表。</returns>
    public Task<IReadOnlyList<OnlineAssetTransaction>> ListByStateAsync(OnlineAssetTransactionState state, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var matches = new List<OnlineAssetTransaction>();
            foreach (var pair in _transactions)
            {
                if (pair.Value.State == state)
                {
                    matches.Add(pair.Value);
                }
            }

            matches.Sort((left, right) => left.CreatedTime.CompareTo(right.CreatedTime));
            return Task.FromResult<IReadOnlyList<OnlineAssetTransaction>>(matches.Select(Clone).ToList());
        }
    }

    /// <summary>
    /// 深拷贝交易记录（防御性快照：保存与读取隔离引用，模拟持久化行语义——
    /// 保存失败/中断时调用方对实例的后续突变不得污染已存状态）。
    /// </summary>
    /// <param name="source">源记录。</param>
    /// <returns>快照副本。</returns>
    private static OnlineAssetTransaction Clone(OnlineAssetTransaction source)
    {
        return new OnlineAssetTransaction
        {
            TransactionId = source.TransactionId,
            IdempotencyKey = source.IdempotencyKey,
            RequestDigest = source.RequestDigest,
            TenantId = source.TenantId,
            AppId = source.AppId,
            PlayerId = source.PlayerId,
            HomeServerId = source.HomeServerId,
            InitiatingServerId = source.InitiatingServerId,
            Source = source.Source,
            Operation = source.Operation,
            Reason = source.Reason,
            BusinessOrderId = source.BusinessOrderId,
            OperatorId = source.OperatorId,
            ExpectedChangeCount = source.ExpectedChangeCount,
            State = source.State,
            AppliedLedgerEntryIds = source.AppliedLedgerEntryIds?.ToArray() ?? Array.Empty<string>(),
            CreatedTime = source.CreatedTime,
            SettledTime = source.SettledTime,
            FailureMessage = source.FailureMessage,
        };
    }
}
