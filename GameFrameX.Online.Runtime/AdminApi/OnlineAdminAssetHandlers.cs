// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应开源许可证与规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   Any legal disputes or liabilities arising from secondary development based on this project
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://cnb.cool/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Text.Json;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Audit;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Runtime.AdminApi;

/// <summary>
/// 资产域 admin 命令与查询（grant_asset / revoke_asset / query_wallet_ledger / query_asset_audit / query_transaction_detail）。
/// <para>
/// 维护约束：发放与撤销统一走 <see cref="OnlineGrantService"/>（内建幂等，Admin 线缆 IdempotencyKey = BusinessOrderNumber
/// 或 Admin 生成的前缀键，直接透传）；撤销按既有交易明细反向建变更行（无独立撤销 API）；资产审计查询映射统一审计面后
/// 按 <c>Online.Asset.*</c> 事件前缀过滤（页内过滤，时间过滤由审计面承接）。
/// </para>
/// </summary>
public sealed class OnlineAdminAssetHandlers
{
    /// <summary>
    /// 宿主。
    /// </summary>
    private readonly OnlineRuntimeHost _host;

    /// <summary>
    /// 初始化 <see cref="OnlineAdminAssetHandlers"/>。
    /// </summary>
    /// <param name="host">宿主。</param>
    public OnlineAdminAssetHandlers(OnlineRuntimeHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// 注册资产域 action。
    /// </summary>
    /// <param name="dispatcher">调度器。</param>
    public void Register(OnlineAdminApiDispatcher dispatcher)
    {
        dispatcher.Register("grant_asset", GrantAssetAsync, false);
        dispatcher.Register("revoke_asset", RevokeAssetAsync, false);
        dispatcher.Register("query_wallet_ledger", QueryWalletLedgerAsync, false);
        dispatcher.Register("query_asset_audit", QueryAssetAuditAsync, false);
        dispatcher.Register("query_transaction_detail", QueryTransactionDetailAsync, false);
    }

    /// <summary>
    /// grant_asset：向玩家发放资产（GrantItems：ItemOrCurrencyType + Quantity）。
    /// <para>
    /// 线缆映射：ItemOrCurrencyType 同时承担类型语义与资产标识——值等于 <see cref="OnlineAssetKind"/> 成员名
    /// （Currency/Item，大小写不敏感）时按该类型入账，否则一律按道具；资产标识恒为该字符串原文。
    /// </para>
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受理回执。</returns>
    public async Task<object> GrantAssetAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var playerScope = OnlineAdminApiContract.ReadPlayerScope(request, scope);
        var changes = ReadGrantItems(request);
        var reason = OnlineAdminApiContract.RequireString(request, "Reason");
        var businessOrderNumber = request.ReadString("BusinessOrderNumber");
        var idempotencyKey = request.ReadString("IdempotencyKey");
        var grantRequest = new OnlineGrantRequest(playerScope, OnlineAssetChangeSource.AdminOperation, OnlineGrantOperation.Grant, reason,
            ReadBusinessOrderId(request, businessOrderNumber, idempotencyKey), "admin", changes, idempotencyKey ?? string.Empty, scope.ServerId, request.ReadRequestId());
        var result = OnlineAdminApiContract.Unwrap(await _host.GrantService.ExecuteAsync(grantRequest, cancellationToken).ConfigureAwait(false));
        return new GrantAckResponse
        {
            TransactionId = result.TransactionId,
            IdempotencyKey = idempotencyKey ?? string.Empty,
            Status = result.State.ToString(),
            OccurredAt = result.Entries != null && result.Entries.Count > 0 ? result.Entries[result.Entries.Count - 1].OccurredTime : OnlineAdminApiContract.NowSeconds() * 1000,
        };
    }

    /// <summary>
    /// revoke_asset：按既有交易撤销发放（读取交易明细反向建变更行）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受理回执。</returns>
    public async Task<object> RevokeAssetAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var playerScope = OnlineAdminApiContract.ReadPlayerScope(request, scope);
        var transactionId = OnlineAdminApiContract.RequireString(request, "TransactionId");
        var reason = OnlineAdminApiContract.RequireString(request, "Reason");
        var businessOrderNumber = request.ReadString("BusinessOrderNumber");
        var idempotencyKey = request.ReadString("IdempotencyKey");
        var detail = OnlineAdminApiContract.Unwrap(await _host.AssetQuery.GetTransactionDetailAsync(playerScope, transactionId, cancellationToken).ConfigureAwait(false));
        if (detail == null || detail.Transaction == null)
        {
            throw new OnlineServiceException(OnlineErrorCode.ResourceNotFound, "Transaction not found: " + transactionId);
        }

        var changes = new List<OnlineAssetChangeLine>();
        foreach (var entry in detail.Entries)
        {
            changes.Add(new OnlineAssetChangeLine(entry.AssetKind, entry.AssetId, -entry.Delta));
        }

        var revokeRequest = new OnlineGrantRequest(playerScope, OnlineAssetChangeSource.AdminOperation, OnlineGrantOperation.Revoke, reason,
            ReadBusinessOrderId(request, businessOrderNumber, idempotencyKey), "admin", changes, idempotencyKey ?? string.Empty, scope.ServerId, request.ReadRequestId());
        var result = OnlineAdminApiContract.Unwrap(await _host.GrantService.ExecuteAsync(revokeRequest, cancellationToken).ConfigureAwait(false));
        return new GrantAckResponse
        {
            TransactionId = result.TransactionId,
            IdempotencyKey = idempotencyKey ?? string.Empty,
            Status = result.State.ToString(),
            OccurredAt = OnlineAdminApiContract.NowSeconds() * 1000,
        };
    }

    /// <summary>
    /// query_wallet_ledger：玩家资产账本分页（时间 / 类型过滤为页内过滤——账本面游标分页为唯一稳定分页点）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账本页。</returns>
    public async Task<object> QueryWalletLedgerAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var playerScope = OnlineAdminApiContract.ReadPlayerScope(request, scope);
        var cursor = OnlineAdminApiContract.ReadCursor(request);
        var pageSize = OnlineAdminApiContract.ReadPageSize(request);
        var currencyOrItemType = OnlineAdminApiContract.ReadOptionalString(request, "CurrencyOrItemType");
        var startTime = OnlineAdminApiContract.ReadOptionalInt64(request, "StartTime");
        var endTime = OnlineAdminApiContract.ReadOptionalInt64(request, "EndTime");
        var page = OnlineAdminApiContract.Unwrap(await _host.AssetQuery.GetLedgerPageAsync(playerScope, cursor, pageSize, cancellationToken).ConfigureAwait(false));
        var items = new List<LedgerEntryResponse>();
        foreach (var entry in page.Entries)
        {
            if (!string.IsNullOrEmpty(currencyOrItemType) && !string.Equals(entry.AssetId, currencyOrItemType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (startTime.HasValue && entry.OccurredTime < startTime.Value)
            {
                continue;
            }

            if (endTime.HasValue && entry.OccurredTime > endTime.Value)
            {
                continue;
            }

            items.Add(new LedgerEntryResponse
            {
                TransactionId = entry.TransactionId,
                PlayerId = entry.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CurrencyOrItemType = entry.AssetId,
                BeforeValue = entry.AmountBefore,
                AfterValue = entry.AmountAfter,
                Source = entry.Source.ToString(),
                Reason = entry.Reason ?? string.Empty,
                BusinessOrderNumber = entry.BusinessOrderId ?? string.Empty,
                OperatorId = entry.OperatorId ?? string.Empty,
                OccurredAt = entry.OccurredTime,
            });
        }

        return new LedgerQueryResponse
        {
            Items = items,
            NextCursor = page.Page != null && page.Page.HasMore ? page.Page.Cursor : null,
            HasMore = page.Page != null && page.Page.HasMore,
        };
    }

    /// <summary>
    /// query_asset_audit：资产域审计查询（统一审计面 + <c>Online.Asset.*</c> 事件前缀过滤）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>审计页。</returns>
    public async Task<object> QueryAssetAuditAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var query = new OnlineAuditQuery
        {
            PlayerId = OnlineAdminApiContract.ReadOptionalInt64(request, "PlayerId"),
            OperatorId = OnlineAdminApiContract.ReadOptionalString(request, "OperatorId"),
            StartTime = OnlineAdminApiContract.ReadOptionalInt64(request, "StartTime"),
            EndTime = OnlineAdminApiContract.ReadOptionalInt64(request, "EndTime"),
            Cursor = OnlineAdminApiContract.ReadCursor(request),
            PageSize = OnlineAdminApiContract.ReadPageSize(request),
        };
        var page = OnlineAdminApiContract.Unwrap(await _host.Audit.QueryAsync(scope, query, cancellationToken).ConfigureAwait(false));
        var items = new List<AssetAuditEntryResponse>();
        foreach (var record in page.Records)
        {
            if (record.EventType == null || !record.EventType.StartsWith("Online.Asset.", StringComparison.Ordinal))
            {
                continue;
            }

            items.Add(new AssetAuditEntryResponse
            {
                TransactionId = ReadSanitizedField(record, "TransactionId") ?? record.EventId,
                CommandType = record.EventType,
                OperatorId = record.OperatorId ?? string.Empty,
                PlayerId = record.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Reason = record.Reason ?? string.Empty,
                BusinessOrderNumber = ReadSanitizedField(record, "BusinessOrderId") ?? string.Empty,
                OccurredAt = record.OccurredTime,
            });
        }

        return new AssetAuditQueryResponse
        {
            Items = items,
            NextCursor = page.Cursor != null && page.Cursor.HasMore ? page.Cursor.Cursor : null,
            HasMore = page.Cursor != null && page.Cursor.HasMore,
        };
    }

    /// <summary>
    /// query_transaction_detail：交易明细（含变更行快照）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>交易明细。</returns>
    public async Task<object> QueryTransactionDetailAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var transactionId = OnlineAdminApiContract.RequireString(request, "TransactionId");
        var storedTransaction = await _host.AssetTransactionStore.FindAsync(transactionId, cancellationToken).ConfigureAwait(false);
        if (storedTransaction == null || storedTransaction.TenantId != scope.TenantId || storedTransaction.AppId != scope.AppId)
        {
            throw new OnlineServiceException(OnlineErrorCode.ResourceNotFound, "Transaction not found: " + transactionId);
        }

        // 线缆契约：query_transaction_detail 只携带交易标识；玩家位由交易记录回查补全（查询面按玩家位校验）。
        var playerScope = new OnlineScope(scope.TenantId, scope.AppId, scope.ServerId, storedTransaction.PlayerId);
        var detail = OnlineAdminApiContract.Unwrap(await _host.AssetQuery.GetTransactionDetailAsync(playerScope, transactionId, cancellationToken).ConfigureAwait(false));
        if (detail == null || detail.Transaction == null)
        {
            throw new OnlineServiceException(OnlineErrorCode.ResourceNotFound, "Transaction not found: " + transactionId);
        }

        var grantItems = new List<TransactionGrantItemResponse>();
        foreach (var entry in detail.Entries)
        {
            grantItems.Add(new TransactionGrantItemResponse
            {
                ItemOrCurrencyType = entry.AssetId,
                Quantity = entry.Delta,
                BeforeValue = entry.AmountBefore,
                AfterValue = entry.AmountAfter,
            });
        }

        var transaction = detail.Transaction;
        return new TransactionDetailResponse
        {
            TransactionId = transaction.TransactionId,
            IdempotencyKey = transaction.IdempotencyKey ?? string.Empty,
            Status = transaction.State.ToString(),
            Source = transaction.Source.ToString(),
            Reason = transaction.Reason ?? string.Empty,
            BusinessOrderNumber = transaction.BusinessOrderId ?? string.Empty,
            OperatorId = transaction.OperatorId ?? string.Empty,
            PlayerId = transaction.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            OccurredAt = transaction.CreatedTime,
            GrantItems = grantItems,
        };
    }

    /// <summary>
    /// 解析业务单号（审计追溯红线要求非空：线缆 BusinessOrderNumber → IdempotencyKey（Admin 契约回退链）→ 请求标识兜底）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="businessOrderNumber">线缆业务单号。</param>
    /// <param name="idempotencyKey">线缆幂等键。</param>
    /// <returns>非空业务单号。</returns>
    private static string ReadBusinessOrderId(OnlineAdminApiRequest request, string businessOrderNumber, string idempotencyKey)
    {
        if (!string.IsNullOrEmpty(businessOrderNumber))
        {
            return businessOrderNumber;
        }

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            return idempotencyKey;
        }

        return "ADMIN-" + request.ReadRequestId();
    }

    /// <summary>
    /// 解析 GrantItems 变更行（空数组非法）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>变更行列表。</returns>
    private static List<OnlineAssetChangeLine> ReadGrantItems(OnlineAdminApiRequest request)
    {
        var changes = new List<OnlineAssetChangeLine>();
        if (OnlineAdminApiContract.TryGetElement(request.Root, "GrantItems", out var arrayElement) && arrayElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arrayElement.EnumerateArray())
            {
                string itemOrCurrencyType = null;
                long quantity = 0;
                foreach (var property in item.EnumerateObject())
                {
                    if (string.Equals(property.Name, "ItemOrCurrencyType", StringComparison.OrdinalIgnoreCase))
                    {
                        itemOrCurrencyType = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText();
                    }
                    else if (string.Equals(property.Name, "Quantity", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Number)
                    {
                        property.Value.TryGetInt64(out quantity);
                    }
                }

                if (string.IsNullOrEmpty(itemOrCurrencyType) || quantity <= 0)
                {
                    throw new OnlineServiceException(OnlineErrorCode.ParameterInvalid, "GrantItems entries require ItemOrCurrencyType (string) and Quantity (positive number).");
                }

                var assetKind = Enum.TryParse<OnlineAssetKind>(itemOrCurrencyType, true, out var parsedKind) ? parsedKind : OnlineAssetKind.Item;
                changes.Add(new OnlineAssetChangeLine(assetKind, itemOrCurrencyType, quantity));
            }
        }

        if (changes.Count == 0)
        {
            throw new OnlineServiceException(OnlineErrorCode.ParameterInvalid, "GrantItems must contain at least one entry.");
        }

        return changes;
    }

    /// <summary>
    /// 读取审计记录的脱敏字段。
    /// </summary>
    /// <param name="record">审计记录。</param>
    /// <param name="name">字段名。</param>
    /// <returns>字段值；无返回 null。</returns>
    private static string ReadSanitizedField(OnlineAuditRecord record, string name)
    {
        if (record.SanitizedFields != null && record.SanitizedFields.TryGetValue(name, out var value))
        {
            return value;
        }

        return null;
    }

    /// <summary>
    /// 发放/撤销受理回执（对齐 Admin OnlineAssetGrantAckResponse）。
    /// </summary>
    public sealed class GrantAckResponse
    {
        /// <summary>获取或设置交易标识。</summary>
        public string TransactionId
        {
            get;
            set;
        }

        /// <summary>获取或设置幂等键回显。</summary>
        public string IdempotencyKey
        {
            get;
            set;
        }

        /// <summary>获取或设置交易状态名。</summary>
        public string Status
        {
            get;
            set;
        }

        /// <summary>获取或设置生效时刻（UTC 毫秒）。</summary>
        public long OccurredAt
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 账本条目（对齐 Admin OnlineAssetLedgerEntryResponse）。
    /// </summary>
    public sealed class LedgerEntryResponse
    {
        /// <summary>获取或设置交易标识。</summary>
        public string TransactionId
        {
            get;
            set;
        }

        /// <summary>获取或设置玩家标识（字符串形态）。</summary>
        public string PlayerId
        {
            get;
            set;
        }

        /// <summary>获取或设置资产标识。</summary>
        public string CurrencyOrItemType
        {
            get;
            set;
        }

        /// <summary>获取或设置变更前值。</summary>
        public long BeforeValue
        {
            get;
            set;
        }

        /// <summary>获取或设置变更后值。</summary>
        public long AfterValue
        {
            get;
            set;
        }

        /// <summary>获取或设置变更来源名。</summary>
        public string Source
        {
            get;
            set;
        }

        /// <summary>获取或设置变更原因。</summary>
        public string Reason
        {
            get;
            set;
        }

        /// <summary>获取或设置业务单号。</summary>
        public string BusinessOrderNumber
        {
            get;
            set;
        }

        /// <summary>获取或设置操作者。</summary>
        public string OperatorId
        {
            get;
            set;
        }

        /// <summary>获取或设置发生时刻（UTC 毫秒）。</summary>
        public long OccurredAt
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 账本查询页（对齐 Admin OnlineAssetLedgerQueryResultResponse）。
    /// </summary>
    public sealed class LedgerQueryResponse
    {
        /// <summary>获取或设置条目列表。</summary>
        public List<LedgerEntryResponse> Items
        {
            get;
            set;
        }

        /// <summary>获取或设置下一页游标。</summary>
        public string NextCursor
        {
            get;
            set;
        }

        /// <summary>获取或设置是否还有更多。</summary>
        public bool HasMore
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 资产审计条目（对齐 Admin OnlineAssetAuditEntryResponse）。
    /// </summary>
    public sealed class AssetAuditEntryResponse
    {
        /// <summary>获取或设置交易标识（脱敏字段缺省时回退事件标识）。</summary>
        public string TransactionId
        {
            get;
            set;
        }

        /// <summary>获取或设置命令类型（事件类型名）。</summary>
        public string CommandType
        {
            get;
            set;
        }

        /// <summary>获取或设置操作者。</summary>
        public string OperatorId
        {
            get;
            set;
        }

        /// <summary>获取或设置玩家标识（字符串形态）。</summary>
        public string PlayerId
        {
            get;
            set;
        }

        /// <summary>获取或设置原因。</summary>
        public string Reason
        {
            get;
            set;
        }

        /// <summary>获取或设置业务单号。</summary>
        public string BusinessOrderNumber
        {
            get;
            set;
        }

        /// <summary>获取或设置发生时刻（UTC 毫秒）。</summary>
        public long OccurredAt
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 资产审计查询页（对齐 Admin OnlineAssetAuditQueryResultResponse）。
    /// </summary>
    public sealed class AssetAuditQueryResponse
    {
        /// <summary>获取或设置条目列表。</summary>
        public List<AssetAuditEntryResponse> Items
        {
            get;
            set;
        }

        /// <summary>获取或设置下一页游标。</summary>
        public string NextCursor
        {
            get;
            set;
        }

        /// <summary>获取或设置是否还有更多。</summary>
        public bool HasMore
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 交易变更行（对齐 Admin OnlineAssetTransactionGrantItemResponse）。
    /// </summary>
    public sealed class TransactionGrantItemResponse
    {
        /// <summary>获取或设置资产标识。</summary>
        public string ItemOrCurrencyType
        {
            get;
            set;
        }

        /// <summary>获取或设置数额（发放正 / 撤销负）。</summary>
        public long Quantity
        {
            get;
            set;
        }

        /// <summary>获取或设置变更前值。</summary>
        public long BeforeValue
        {
            get;
            set;
        }

        /// <summary>获取或设置变更后值。</summary>
        public long AfterValue
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 交易明细（对齐 Admin OnlineAssetTransactionDetailResponse）。
    /// </summary>
    public sealed class TransactionDetailResponse
    {
        /// <summary>获取或设置交易标识。</summary>
        public string TransactionId
        {
            get;
            set;
        }

        /// <summary>获取或设置幂等键。</summary>
        public string IdempotencyKey
        {
            get;
            set;
        }

        /// <summary>获取或设置状态名。</summary>
        public string Status
        {
            get;
            set;
        }

        /// <summary>获取或设置来源名。</summary>
        public string Source
        {
            get;
            set;
        }

        /// <summary>获取或设置原因。</summary>
        public string Reason
        {
            get;
            set;
        }

        /// <summary>获取或设置业务单号。</summary>
        public string BusinessOrderNumber
        {
            get;
            set;
        }

        /// <summary>获取或设置操作者。</summary>
        public string OperatorId
        {
            get;
            set;
        }

        /// <summary>获取或设置玩家标识（字符串形态）。</summary>
        public string PlayerId
        {
            get;
            set;
        }

        /// <summary>获取或设置发生时刻（UTC 毫秒）。</summary>
        public long OccurredAt
        {
            get;
            set;
        }

        /// <summary>获取或设置变更行列表。</summary>
        public List<TransactionGrantItemResponse> GrantItems
        {
            get;
            set;
        }
    }
}
