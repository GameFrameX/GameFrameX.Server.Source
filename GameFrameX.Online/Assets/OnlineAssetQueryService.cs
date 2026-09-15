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
using GameFrameX.Online.Scope;

/// <summary>
/// 资产查询服务（vault:C4：余额/库存/流水的只读查询面，供 Admin 资产流水查询与客服追溯消费）。
/// <para>
/// 维护约束：本服务只读——资产变更唯一入口是 <see cref="OnlineGrantService"/>（X5），
/// 查询路径不提供任何写通道；跨作用域查询与不存在同构 <see cref="OnlineErrorCode.ResourceNotFound"/>
/// （防存在性探测）；分页以账本序为稳定排序键（VC-1.12）；Admin 侧权限校验由 Admin 装配承接（VC-3.9 归 Admin 半边）。
/// </para>
/// </summary>
public sealed class OnlineAssetQueryService
{
    /// <summary>资产存储。</summary>
    private readonly IOnlineAssetStore _assetStore;

    /// <summary>交易记录存储。</summary>
    private readonly IOnlineAssetTransactionStore _transactionStore;

    /// <summary>
    /// 初始化 <see cref="OnlineAssetQueryService"/>。
    /// </summary>
    /// <param name="assetStore">资产存储。</param>
    /// <param name="transactionStore">交易记录存储。</param>
    public OnlineAssetQueryService(IOnlineAssetStore assetStore, IOnlineAssetTransactionStore transactionStore)
    {
        _assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
        _transactionStore = transactionStore ?? throw new ArgumentNullException(nameof(transactionStore));
    }

    /// <summary>
    /// 查询单个货币账户余额。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="currencyId">货币代码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账户快照（未发生过变更返回余额 0 的占位快照）。</returns>
    public async Task<OnlineResult<OnlineWalletAccount>> GetWalletAsync(OnlineScope scope, string currencyId, CancellationToken cancellationToken = default)
    {
        var failure = ValidatePlayerScope<OnlineWalletAccount>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(currencyId))
        {
            return OnlineResult<OnlineWalletAccount>.Fail(OnlineErrorCode.ParameterInvalid, "货币代码不得为空");
        }

        var account = await _assetStore.FindWalletAsync(scope.TenantId, scope.AppId, scope.PlayerId, currencyId, cancellationToken);
        if (account == null)
        {
            account = new OnlineWalletAccount { TenantId = scope.TenantId, AppId = scope.AppId, PlayerId = scope.PlayerId, CurrencyId = currencyId, Balance = 0, Version = 0, UpdatedTime = 0 };
        }

        return OnlineResult<OnlineWalletAccount>.Ok(account);
    }

    /// <summary>
    /// 查询玩家全部货币账户余额。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账户快照列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineWalletAccount>>> ListWalletsAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failure = ValidatePlayerScope<IReadOnlyList<OnlineWalletAccount>>(scope);
        if (failure != null)
        {
            return failure;
        }

        var accounts = await _assetStore.ListWalletAccountsAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        return OnlineResult<IReadOnlyList<OnlineWalletAccount>>.Ok(accounts);
    }

    /// <summary>
    /// 查询玩家全部道具库存。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>库存快照列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineInventoryStack>>> ListInventoryAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failure = ValidatePlayerScope<IReadOnlyList<OnlineInventoryStack>>(scope);
        if (failure != null)
        {
            return failure;
        }

        var stacks = await _assetStore.ListInventoryStacksAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        return OnlineResult<IReadOnlyList<OnlineInventoryStack>>.Ok(stacks);
    }

    /// <summary>
    /// 按账本序分页查询玩家资产流水。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cursor">分页游标（空 = 首页）。</param>
    /// <param name="maxCount">最大返回条数（1～100）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账本分页结果。</returns>
    public async Task<OnlineResult<OnlineLedgerPage>> GetLedgerPageAsync(OnlineScope scope, string cursor, int maxCount, CancellationToken cancellationToken = default)
    {
        var failure = ValidatePlayerScope<OnlineLedgerPage>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (maxCount < 1 || maxCount > 100)
        {
            return OnlineResult<OnlineLedgerPage>.Fail(OnlineErrorCode.ParameterInvalid, "每页条数必须在 1～100 之间");
        }

        long afterSequenceNumber = 0;
        if (!string.IsNullOrEmpty(cursor) && (!long.TryParse(cursor, out afterSequenceNumber) || afterSequenceNumber < 0))
        {
            return OnlineResult<OnlineLedgerPage>.Fail(OnlineErrorCode.ParameterInvalid, "游标格式非法（不透明令牌只回传不构造）");
        }

        var entries = await _assetStore.ListLedgerEntriesAsync(scope.TenantId, scope.AppId, scope.PlayerId, afterSequenceNumber, maxCount, cancellationToken);
        if (entries.Count < maxCount)
        {
            return OnlineResult<OnlineLedgerPage>.Ok(new OnlineLedgerPage(entries, new OnlinePageCursor(string.Empty, false)));
        }

        var nextPage = await _assetStore.ListLedgerEntriesAsync(scope.TenantId, scope.AppId, scope.PlayerId, entries[entries.Count - 1].SequenceNumber, 1, cancellationToken);
        var hasMore = nextPage.Count > 0;
        return OnlineResult<OnlineLedgerPage>.Ok(new OnlineLedgerPage(entries, new OnlinePageCursor(hasMore ? entries[entries.Count - 1].SequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty, hasMore)));
    }

    /// <summary>
    /// 按交易标识反查详情（来源/原因/单号/操作者/前后值全字段，VC-3.14）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="transactionId">交易标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>交易详情。</returns>
    public async Task<OnlineResult<OnlineAssetTransactionDetail>> GetTransactionDetailAsync(OnlineScope scope, string transactionId, CancellationToken cancellationToken = default)
    {
        var failure = ValidatePlayerScope<OnlineAssetTransactionDetail>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return OnlineResult<OnlineAssetTransactionDetail>.Fail(OnlineErrorCode.ParameterInvalid, "交易标识不得为空");
        }

        var transaction = await _transactionStore.FindAsync(transactionId, cancellationToken);
        if (transaction == null || transaction.TenantId != scope.TenantId || transaction.AppId != scope.AppId || transaction.PlayerId != scope.PlayerId)
        {
            return OnlineResult<OnlineAssetTransactionDetail>.Fail(OnlineErrorCode.ResourceNotFound, "交易不存在");
        }

        var entries = await _assetStore.FindLedgerEntriesByTransactionIdAsync(transactionId, cancellationToken);
        return OnlineResult<OnlineAssetTransactionDetail>.Ok(new OnlineAssetTransactionDetail(transaction, entries));
    }

    /// <summary>
    /// 校验作用域含玩家主体位。
    /// </summary>
    /// <typeparam name="TData">成功载荷类型。</typeparam>
    /// <param name="scope">作用域。</param>
    /// <returns>失败结果；合法返回 null。</returns>
    private static OnlineResult<TData> ValidatePlayerScope<TData>(OnlineScope scope)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (scope.PlayerId <= 0)
        {
            return OnlineResult<TData>.Fail(OnlineErrorCode.ParameterInvalid, "资产查询必须绑定玩家主体位");
        }

        return null;
    }
}
