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

using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Idempotency;
using GameFrameX.Online.Scope;

/// <summary>
/// 统一资产入口服务（vault:C4 S3.3/S3.4/S3.5：发放/扣除/撤销/补发/人工调整的唯一提交管道，X5 红线）。
/// <para>
/// 维护约束（资产红线）：
/// (1) 幂等先行——每笔交易先经 C93 <see cref="OnlineIdempotencyService"/>（Foundation 原语）判定，
/// 相同业务意图重试回放首次结果（VC-3.1～3.4），同键不同请求体判冲突（VC-1.4）；
/// (2) 原子应用——变更批次在存储层玩家分片事务内先全量校验后落账（VC-3.5/3.6）；
/// (3) 失败补偿——应用阶段异常时按已落账条目数裁决：零条目判 Failed；不足期望行数（部分应用）
/// 追加反转条目净效应归零（账本不可变，纠正只追加，VC-3.6/3.7）；补偿失败滞留
/// <see cref="OnlineAssetTransactionState.CompensationPending"/> 并告警（VC-3.11 语义）；
/// (4) 恢复——<see cref="RecoverAsync"/> 以非终态交易为扫描输入重启续判（VC-3.12）；
/// (5) 事件与告警——成功落账后发布资产变更事实；资产不足/重复意图/补偿动作经告警出口留痕（VC-3.15）。
/// </para>
/// </summary>
public sealed class OnlineGrantService
{
    /// <summary>资产存储（唯一写入口的执行方）。</summary>
    private readonly IOnlineAssetStore _assetStore;

    /// <summary>交易记录存储。</summary>
    private readonly IOnlineAssetTransactionStore _transactionStore;

    /// <summary>幂等判定（C93 组装，Foundation 原语执行方）。</summary>
    private readonly OnlineIdempotencyService _idempotencyService;

    /// <summary>事件发布出口（资产变更事实）。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>告警出口（可空 = 未接线时跳过）。</summary>
    private readonly IOnlineAssetAlertSink _alertSink;

    /// <summary>
    /// 初始化 <see cref="OnlineGrantService"/>。
    /// </summary>
    /// <param name="assetStore">资产存储。</param>
    /// <param name="transactionStore">交易记录存储。</param>
    /// <param name="idempotencyService">幂等判定服务。</param>
    /// <param name="eventPublisher">事件发布出口。</param>
    /// <param name="alertSink">告警出口（生产装配必须接线）。</param>
    public OnlineGrantService(IOnlineAssetStore assetStore, IOnlineAssetTransactionStore transactionStore, OnlineIdempotencyService idempotencyService, IOnlineEventPublisher eventPublisher, IOnlineAssetAlertSink alertSink = null)
    {
        _assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
        _transactionStore = transactionStore ?? throw new ArgumentNullException(nameof(transactionStore));
        _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _alertSink = alertSink;
    }

    /// <summary>
    /// 提交一笔统一资产交易（六类来源的唯一入口；幂等先行、原子应用、失败补偿）。
    /// </summary>
    /// <param name="request">统一入口请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功负载含交易标识与落账条目；重复请求回放首次结果（<c>IsReplay = true</c>）。</returns>
    public async Task<OnlineResult<OnlineGrantResult>> ExecuteAsync(OnlineGrantRequest request, CancellationToken cancellationToken = default)
    {
        var validation = ValidateRequest(request);
        if (validation != null)
        {
            return validation;
        }

        var scope = request.Scope;
        var canonicalRequestText = BuildCanonicalRequestText(request);
        var outcome = await _idempotencyService.BeginAsync(scope, request.IdempotencyKey, canonicalRequestText, true, cancellationToken);
        if (!outcome.CanExecute)
        {
            if (outcome.Kind == OnlineIdempotencyOutcomeKind.Replay)
            {
                await AlertAsync(OnlineAssetAlertKind.DuplicateRequestObserved, string.Empty, scope.ToScopeKey(true), string.Empty, "幂等键命中首次结果：" + request.IdempotencyKey, cancellationToken);
                return await BuildReplayResultAsync(outcome, cancellationToken);
            }

            return OnlineResult<OnlineGrantResult>.Fail(outcome.ErrorCode, "幂等判定拒绝：" + outcome.Kind);
        }

        var homeServerId = request.HomeServerId > 0 ? request.HomeServerId : scope.ServerId;
        var transaction = new OnlineAssetTransaction
        {
            TransactionId = "tx-" + Guid.NewGuid().ToString("N"),
            IdempotencyKey = request.IdempotencyKey,
            RequestDigest = string.Empty,
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            PlayerId = scope.PlayerId,
            HomeServerId = homeServerId,
            InitiatingServerId = scope.ServerId,
            Source = request.Source,
            Operation = request.Operation,
            Reason = request.Reason,
            BusinessOrderId = request.BusinessOrderId,
            OperatorId = request.OperatorId,
            State = OnlineAssetTransactionState.Executing,
            ExpectedChangeCount = request.Changes.Count,
            AppliedLedgerEntryIds = Array.Empty<string>(),
            CreatedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            FailureMessage = string.Empty,
        };
        await _transactionStore.SaveAsync(transaction, cancellationToken);

        OnlineAssetApplyResult apply;
        var applyInterrupted = false;
        try
        {
            var batch = new OnlineAssetChangeBatch(transaction.TransactionId, transaction.TenantId, transaction.AppId, transaction.PlayerId, transaction.HomeServerId, transaction.InitiatingServerId, transaction.Source, transaction.Operation, transaction.Reason, transaction.BusinessOrderId, transaction.OperatorId, request.Changes, string.Empty);
            apply = await _assetStore.ApplyAsync(batch, cancellationToken);
        }
        catch (Exception ex)
        {
            // 应用阶段异常：落账状态未知（可能零条目/部分条目/全部条目），统一走中断裁决——按已落账条目数判定。
            applyInterrupted = true;
            transaction.FailureMessage = "应用阶段异常：" + ex.Message;
            await _transactionStore.SaveAsync(transaction, cancellationToken);
            apply = null;
        }

        if (applyInterrupted)
        {
            return await SettleInterruptedAsync(transaction, cancellationToken);
        }

        if (!apply.Success)
        {
            transaction.State = OnlineAssetTransactionState.Failed;
            transaction.FailureMessage = apply.Message;
            transaction.SettledTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _transactionStore.SaveAsync(transaction, cancellationToken);
            await FailIdempotencyAsync(scope, request.IdempotencyKey, cancellationToken);
            // 应用结果不含失败资产明细（下限校验面向整批）：资产位留空，明细经 Message 承载。
            await AlertAsync(OnlineAssetAlertKind.NegativeBalanceAttempt, transaction.TransactionId, scope.ToScopeKey(true), string.Empty, apply.Message, cancellationToken);
            return OnlineResult<OnlineGrantResult>.Fail(apply.ErrorCode, apply.Message);
        }

        return await SettleSucceededAsync(transaction, apply.Entries, cancellationToken);
    }

    /// <summary>
    /// 重启恢复：扫描非终态交易并续判（Executing 按已落账条目数裁决成败；CompensationPending 续跑补偿）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次恢复落定的交易数。</returns>
    public async Task<int> RecoverAsync(CancellationToken cancellationToken = default)
    {
        var recovered = 0;
        var executing = await _transactionStore.ListByStateAsync(OnlineAssetTransactionState.Executing, cancellationToken);
        foreach (var transaction in executing)
        {
            await SettleInterruptedAsync(transaction, cancellationToken);
            recovered++;
        }

        var pending = await _transactionStore.ListByStateAsync(OnlineAssetTransactionState.CompensationPending, cancellationToken);
        foreach (var transaction in pending)
        {
            if (await CompensateAsync(transaction, cancellationToken))
            {
                recovered++;
            }
        }

        return recovered;
    }

    /// <summary>
    /// 成功落定：交易置 Succeeded、幂等记录完成（承载回放令牌）、发布资产变更事实。
    /// </summary>
    /// <param name="transaction">交易记录。</param>
    /// <param name="entries">落账条目。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功结果。</returns>
    private async Task<OnlineResult<OnlineGrantResult>> SettleSucceededAsync(OnlineAssetTransaction transaction, IReadOnlyList<OnlineLedgerEntry> entries, CancellationToken cancellationToken)
    {
        transaction.State = OnlineAssetTransactionState.Succeeded;
        transaction.SettledTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        transaction.AppliedLedgerEntryIds = entries.Select(entry => entry.EntryId).ToList();
        await _transactionStore.SaveAsync(transaction, cancellationToken);

        var scope = new OnlineScope(transaction.TenantId, transaction.AppId, transaction.InitiatingServerId, transaction.PlayerId);
        await _idempotencyService.CompleteAsync(scope, transaction.IdempotencyKey, EncodeReplayToken(transaction.TransactionId, OnlineAssetTransactionState.Succeeded), true, cancellationToken);

        var changeEvent = OnlineAssetEvents.Create(transaction, entries);
        await _eventPublisher.PublishAsync(changeEvent, cancellationToken);

        return OnlineResult<OnlineGrantResult>.Ok(new OnlineGrantResult { TransactionId = transaction.TransactionId, State = transaction.State, IsReplay = false, Entries = entries });
    }

    /// <summary>
    /// 中断裁决（应用阶段异常或恢复期 Executing 交易）：按已落账条目数判定——
    /// 零条目判 Failed；达到期望行数（原子落账后中断）按成功落定；不足期望行数（部分应用）走补偿净效应归零。
    /// </summary>
    /// <param name="transaction">交易记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>裁决后的失败结果（成功落定分支返回成功结果）。</returns>
    private async Task<OnlineResult<OnlineGrantResult>> SettleInterruptedAsync(OnlineAssetTransaction transaction, CancellationToken cancellationToken)
    {
        var appliedEntries = await _assetStore.FindLedgerEntriesByTransactionIdAsync(transaction.TransactionId, cancellationToken);
        var scope = new OnlineScope(transaction.TenantId, transaction.AppId, transaction.InitiatingServerId, transaction.PlayerId);
        if (appliedEntries.Count == 0)
        {
            transaction.State = OnlineAssetTransactionState.Failed;
            transaction.SettledTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _transactionStore.SaveAsync(transaction, cancellationToken);
            await FailIdempotencyAsync(scope, transaction.IdempotencyKey, cancellationToken);
            return OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.InternalError, "交易执行中断（零落账，判失败；可换新幂等键重试）");
        }

        if (appliedEntries.Count >= transaction.ExpectedChangeCount)
        {
            // 原子落账完成后中断：条目齐全，按成功落定（幂等完成承载回放令牌，重试回放而非重复执行）。
            return await SettleSucceededAsync(transaction, appliedEntries, cancellationToken);
        }

        // 部分应用：追加反转条目净效应归零（账本不可变，纠正只追加）。
        transaction.State = OnlineAssetTransactionState.CompensationPending;
        await _transactionStore.SaveAsync(transaction, cancellationToken);
        if (await CompensateAsync(transaction, cancellationToken))
        {
            return OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.InternalError, "交易执行中断已补偿（净效应 0；可换新幂等键重试）");
        }

        return OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.InternalError, "交易执行中断且补偿失败（滞留待补偿，已告警）");
    }

    /// <summary>
    /// 补偿：对已落账条目按资产聚合追加反转批次（来源 SystemCompensation，指向原交易）；成功后原交易置 Compensated。
    /// </summary>
    /// <param name="transaction">待补偿交易。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>补偿成功返回 true；失败滞留 CompensationPending 并告警。</returns>
    private async Task<bool> CompensateAsync(OnlineAssetTransaction transaction, CancellationToken cancellationToken)
    {
        var appliedEntries = await _assetStore.FindLedgerEntriesByTransactionIdAsync(transaction.TransactionId, cancellationToken);
        if (appliedEntries.Count == 0)
        {
            transaction.State = OnlineAssetTransactionState.Failed;
            transaction.SettledTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _transactionStore.SaveAsync(transaction, cancellationToken);
            return true;
        }

        var reversalLines = new List<OnlineAssetChangeLine>();
        foreach (var group in appliedEntries.GroupBy(entry => (entry.AssetKind, entry.AssetId)))
        {
            reversalLines.Add(new OnlineAssetChangeLine(group.Key.AssetKind, group.Key.AssetId, -group.Sum(entry => entry.Delta)));
        }

        var compensationTransaction = new OnlineAssetTransaction
        {
            TransactionId = "tx-" + Guid.NewGuid().ToString("N"),
            IdempotencyKey = "compensate-" + transaction.TransactionId,
            RequestDigest = string.Empty,
            TenantId = transaction.TenantId,
            AppId = transaction.AppId,
            PlayerId = transaction.PlayerId,
            HomeServerId = transaction.HomeServerId,
            InitiatingServerId = transaction.InitiatingServerId,
            Source = OnlineAssetChangeSource.SystemCompensation,
            Operation = OnlineGrantOperation.Adjust,
            Reason = "补偿反转 " + transaction.TransactionId,
            BusinessOrderId = transaction.BusinessOrderId,
            OperatorId = "system",
            State = OnlineAssetTransactionState.Executing,
            ExpectedChangeCount = reversalLines.Count,
            AppliedLedgerEntryIds = Array.Empty<string>(),
            CreatedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            FailureMessage = string.Empty,
        };
        var batch = new OnlineAssetChangeBatch(compensationTransaction.TransactionId, compensationTransaction.TenantId, compensationTransaction.AppId, compensationTransaction.PlayerId, compensationTransaction.HomeServerId, compensationTransaction.InitiatingServerId, compensationTransaction.Source, compensationTransaction.Operation, compensationTransaction.Reason, compensationTransaction.BusinessOrderId, compensationTransaction.OperatorId, reversalLines, transaction.TransactionId);
        var apply = await _assetStore.ApplyAsync(batch, cancellationToken);
        if (!apply.Success)
        {
            await AlertAsync(OnlineAssetAlertKind.CompensationFailed, transaction.TransactionId, new OnlineScope(transaction.TenantId, transaction.AppId, transaction.InitiatingServerId, transaction.PlayerId).ToScopeKey(true), string.Empty, "补偿批次被拒：" + apply.Message, cancellationToken);
            return false;
        }

        compensationTransaction.State = OnlineAssetTransactionState.Succeeded;
        compensationTransaction.SettledTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        compensationTransaction.AppliedLedgerEntryIds = apply.Entries.Select(entry => entry.EntryId).ToList();
        await _transactionStore.SaveAsync(compensationTransaction, cancellationToken);

        transaction.State = OnlineAssetTransactionState.Compensated;
        transaction.SettledTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _transactionStore.SaveAsync(transaction, cancellationToken);

        var scope = new OnlineScope(transaction.TenantId, transaction.AppId, transaction.InitiatingServerId, transaction.PlayerId);
        await FailIdempotencyAsync(scope, transaction.IdempotencyKey, cancellationToken);
        await AlertAsync(OnlineAssetAlertKind.CompensatedAfterApplyFailure, transaction.TransactionId, scope.ToScopeKey(true), string.Empty, "已追加反转条目，净效应 0", cancellationToken);
        return true;
    }

    /// <summary>
    /// 构造幂等回放结果（按回放令牌定位交易并从账本重取条目——账本不可变，重取即首次事实）。
    /// </summary>
    /// <param name="outcome">幂等判定（Replay）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>回放结果。</returns>
    private async Task<OnlineResult<OnlineGrantResult>> BuildReplayResultAsync(OnlineIdempotencyOutcome outcome, CancellationToken cancellationToken)
    {
        var token = Encoding.UTF8.GetString(outcome.FirstResponse.ToArray());
        var separatorIndex = token.LastIndexOf('|');
        if (separatorIndex <= 0 || separatorIndex >= token.Length - 1)
        {
            return OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.InternalError, "幂等回放令牌损坏");
        }

        var transactionId = token.Substring(0, separatorIndex);
        var entries = await _assetStore.FindLedgerEntriesByTransactionIdAsync(transactionId, cancellationToken);
        return OnlineResult<OnlineGrantResult>.Ok(new OnlineGrantResult { TransactionId = transactionId, State = OnlineAssetTransactionState.Succeeded, IsReplay = true, Entries = entries });
    }

    /// <summary>
    /// 校验统一入口请求（作用域主体、来源与操作配对、变更行合法性）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>失败结果；合法返回 null。</returns>
    private static OnlineResult<OnlineGrantResult> ValidateRequest(OnlineGrantRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Scope.PlayerId <= 0)
        {
            return OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.ParameterInvalid, "资产操作必须绑定玩家主体位");
        }

        if (request.Source == OnlineAssetChangeSource.SystemCompensation)
        {
            return OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.ParameterInvalid, "业务调用方禁止指定系统补偿来源");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.ParameterInvalid, "变更原因不得为空（审计追溯红线）");
        }

        if (string.IsNullOrWhiteSpace(request.BusinessOrderId))
        {
            return OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.ParameterInvalid, "业务单号不得为空（审计追溯红线）");
        }

        if ((request.Operation == OnlineGrantOperation.Revoke || request.Operation == OnlineGrantOperation.Adjust) && string.IsNullOrWhiteSpace(request.OperatorId))
        {
            return OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.ParameterInvalid, "撤销与人工调整必须携带操作者（人工资金动作留痕红线）");
        }

        if (request.Changes.Count == 0)
        {
            return OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.ParameterInvalid, "变更行不得为空");
        }

        var seenAssets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in request.Changes)
        {
            if (line == null || string.IsNullOrWhiteSpace(line.AssetId))
            {
                return OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.ParameterInvalid, "变更行资产标识不得为空");
            }

            if (line.Amount == 0)
            {
                return OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.ParameterInvalid, "变更数额不得为 0");
            }

            if (!seenAssets.Add(line.AssetKind + ":" + line.AssetId))
            {
                return OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.ParameterInvalid, "同一交易内资产重复：" + line.AssetId);
            }
        }

        return null;
    }

    /// <summary>
    /// 构造规范化请求文本（幂等请求摘要输入：同一业务意图的重复请求必须产生相同文本，
    /// 排序消除变更行顺序差异；文本变化即同键不同意图 → 冲突）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>规范化文本。</returns>
    private static string BuildCanonicalRequestText(OnlineGrantRequest request)
    {
        var builder = new StringBuilder();
        builder.Append("v1|").Append(request.Scope.TenantId).Append(':').Append(request.Scope.AppId).Append(':').Append(request.Scope.PlayerId);
        builder.Append('|').Append((int)request.Operation).Append('|').Append((int)request.Source);
        builder.Append('|').Append(request.BusinessOrderId).Append('|').Append(request.Reason);
        var orderedLines = request.Changes.OrderBy(line => line.AssetKind).ThenBy(line => line.AssetId, StringComparer.Ordinal);
        foreach (var line in orderedLines)
        {
            builder.Append('|').Append((int)line.AssetKind).Append(':').Append(line.AssetId).Append(':').Append(line.Amount);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 幂等失败落定（容错：恢复上下文里幂等记录可能已过 TTL 或跨存储失联——
    /// Foundation 存储对缺失记录抛 <see cref="InvalidOperationException"/>；
    /// 记录缺失 = 回放保护自然失效（调用方可换新幂等键重试），不得因此中断恢复循环）。
    /// </summary>
    /// <param name="scope">作用域。</param>
    /// <param name="idempotencyKey">幂等键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task FailIdempotencyAsync(OnlineScope scope, string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            await _idempotencyService.FailAsync(scope, idempotencyKey, true, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // 幂等记录缺失（过期/失联）：无回放可失败，交易终态为准，安全跳过。
        }
    }

    /// <summary>
    /// 编码幂等回放令牌（交易标识 + 终态的 UTF-8 文本；回放时定位交易并重取账本条目）。
    /// </summary>
    /// <param name="transactionId">交易标识。</param>
    /// <param name="state">终态。</param>
    /// <returns>令牌字节。</returns>
    private static ReadOnlyMemory<byte> EncodeReplayToken(string transactionId, OnlineAssetTransactionState state)
    {
        return new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(transactionId + "|" + (int)state));
    }

    /// <summary>
    /// 发出资产域告警（出口未接线时跳过；实现不得抛出阻断业务路径的异常）。
    /// </summary>
    /// <param name="kind">告警类型。</param>
    /// <param name="transactionId">关联交易。</param>
    /// <param name="scopeKey">作用域键。</param>
    /// <param name="assetId">关联资产。</param>
    /// <param name="detail">事实描述。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task AlertAsync(OnlineAssetAlertKind kind, string transactionId, string scopeKey, string assetId, string detail, CancellationToken cancellationToken)
    {
        if (_alertSink == null)
        {
            return;
        }

        var record = new OnlineAssetAlertRecord
        {
            Kind = kind,
            TransactionId = transactionId ?? string.Empty,
            ScopeKey = scopeKey ?? string.Empty,
            AssetId = assetId ?? string.Empty,
            Detail = detail ?? string.Empty,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        await _alertSink.AlertAsync(record, cancellationToken);
    }
}
