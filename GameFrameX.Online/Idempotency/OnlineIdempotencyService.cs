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
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
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

using System.Text;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Idempotency;

/// <summary>
/// Online 幂等 Server 侧组装（vault:C2 S1.5 Server 半边：只组装 Foundation 原语，不实现第二套存储）。
/// <para>
/// 维护约束（红线）：作用域绑定——幂等记录以 <c>online:tenant:app:server[:player]</c> 为存储作用域，
/// 玩家级操作必须绑定玩家位，禁止跨玩家复用幂等键；请求摘要——对规范化请求文本做 SHA-256，
/// 相同键不同摘要判 Conflict（6xxx）；决策映射固定见 <see cref="OnlineIdempotencyOutcome"/>；
/// 每次判定（含拒绝）都写审计（出口未接线时跳过并留告警日志，生产装配必须接线）。
/// </para>
/// </summary>
public sealed class OnlineIdempotencyService
{
    /// <summary>
    /// Foundation 幂等协调器（判定/落定的通用原语执行方）。
    /// </summary>
    private readonly IdempotencyCoordinator _coordinator;

    /// <summary>
    /// 审计出口（可为 null：未接线时跳过审计）。
    /// </summary>
    private readonly IOnlineIdempotencyAuditSink _auditSink;

    /// <summary>
    /// 初始化 <see cref="OnlineIdempotencyService"/>。
    /// </summary>
    /// <param name="coordinator">Foundation 幂等协调器（存储/时钟/选项由其持有）。</param>
    /// <param name="auditSink">审计出口；生产装配必须接线。</param>
    public OnlineIdempotencyService(IdempotencyCoordinator coordinator, IOnlineIdempotencyAuditSink auditSink = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _auditSink = auditSink;
    }

    /// <summary>
    /// 发起一次幂等判定：校验业务键、绑定作用域、计算摘要并交由 Foundation 协调器裁决。
    /// </summary>
    /// <param name="scope">生效作用域（鉴权上下文产物）。</param>
    /// <param name="idempotencyKey">业务意图幂等键。</param>
    /// <param name="canonicalRequestText">规范化请求文本（同一业务意图的重复请求必须产生相同文本）。</param>
    /// <param name="bindPlayer">是否绑定玩家位（玩家级副作用操作必须为 <c>true</c>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Online 语义判定结果（含协议错误码映射与首次响应透出）。</returns>
    public async Task<OnlineIdempotencyOutcome> BeginAsync(OnlineScope scope, string idempotencyKey, string canonicalRequestText, bool bindPlayer, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        var scopeKey = scope.ToScopeKey(bindPlayer);

        if (!OnlineRequestContextValidator.IsIdempotencyKeyValid(idempotencyKey))
        {
            var invalidOutcome = new OnlineIdempotencyOutcome(OnlineIdempotencyOutcomeKind.InvalidKey, OnlineErrorCode.ParameterInvalid, default, scopeKey, idempotencyKey);
            await WriteAuditAsync(invalidOutcome, string.Empty, cancellationToken);
            return invalidOutcome;
        }

        var requestDigest = ComputeDigest(canonicalRequestText);
        var beginRequest = new IdempotencyBeginRequest(scopeKey, idempotencyKey, requestDigest);
        var decision = await _coordinator.BeginAsync(beginRequest, cancellationToken);

        var outcome = MapDecision(decision, scopeKey, idempotencyKey);
        await WriteAuditAsync(outcome, requestDigest, cancellationToken);
        return outcome;
    }

    /// <summary>
    /// 将执行成功的首次响应落定为完成记录（此后相同键相同请求将回放本响应）。
    /// </summary>
    /// <param name="scope">生效作用域（须与 Begin 时一致）。</param>
    /// <param name="idempotencyKey">幂等键。</param>
    /// <param name="firstResponse">首次响应字节（调用方序列化契约产物）。</param>
    /// <param name="bindPlayer">是否绑定玩家位（须与 Begin 时一致）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task CompleteAsync(OnlineScope scope, string idempotencyKey, ReadOnlyMemory<byte> firstResponse, bool bindPlayer, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        var identifier = new IdempotencyRecordIdentifier(scope.ToScopeKey(bindPlayer), idempotencyKey);
        var completionRequest = new IdempotencyCompletionRequest(identifier, firstResponse, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return _coordinator.CompleteAsync(completionRequest, cancellationToken);
    }

    /// <summary>
    /// 将执行失败的占位记录落定为失败（按 Foundation 失败重执行策略决定后续可否重新占位）。
    /// </summary>
    /// <param name="scope">生效作用域（须与 Begin 时一致）。</param>
    /// <param name="idempotencyKey">幂等键。</param>
    /// <param name="bindPlayer">是否绑定玩家位（须与 Begin 时一致）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task FailAsync(OnlineScope scope, string idempotencyKey, bool bindPlayer, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        var identifier = new IdempotencyRecordIdentifier(scope.ToScopeKey(bindPlayer), idempotencyKey);
        var failureRequest = new IdempotencyFailureRequest(identifier, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return _coordinator.FailAsync(failureRequest, cancellationToken);
    }

    /// <summary>
    /// 将 Foundation 决策映射为 Online 语义判定（映射关系为维护约束，调整须回 vault 评审）。
    /// </summary>
    /// <param name="decision">Foundation 决策。</param>
    /// <param name="scopeKey">作用域绑定键。</param>
    /// <param name="idempotencyKey">幂等键。</param>
    /// <returns>Online 语义判定结果。</returns>
    private static OnlineIdempotencyOutcome MapDecision(IdempotencyDecision decision, string scopeKey, string idempotencyKey)
    {
        switch (decision.DecisionKind)
        {
            case IdempotencyDecisionKind.Execute:
                return new OnlineIdempotencyOutcome(OnlineIdempotencyOutcomeKind.Execute, OnlineErrorCode.None, default, scopeKey, idempotencyKey);
            case IdempotencyDecisionKind.Replay:
                return new OnlineIdempotencyOutcome(OnlineIdempotencyOutcomeKind.Replay, OnlineErrorCode.None, decision.ExistingRecord?.FirstResponse ?? default, scopeKey, idempotencyKey);
            case IdempotencyDecisionKind.Conflict:
                return new OnlineIdempotencyOutcome(OnlineIdempotencyOutcomeKind.Conflict, OnlineErrorCode.VersionConflict, default, scopeKey, idempotencyKey);
            case IdempotencyDecisionKind.Busy:
                return new OnlineIdempotencyOutcome(OnlineIdempotencyOutcomeKind.Busy, OnlineErrorCode.ServiceBusy, default, scopeKey, idempotencyKey);
            default:
                return new OnlineIdempotencyOutcome(OnlineIdempotencyOutcomeKind.Busy, OnlineErrorCode.ServiceBusy, default, scopeKey, idempotencyKey);
        }
    }

    /// <summary>
    /// 计算请求摘要（SHA-256 十六进制；空文本摘要为固定占位以满足 Foundation 非空约束）。
    /// </summary>
    /// <param name="canonicalRequestText">规范化请求文本。</param>
    /// <returns>摘要字符串。</returns>
    private static string ComputeDigest(string canonicalRequestText)
    {
        var payload = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(canonicalRequestText) ? "empty" : canonicalRequestText);
        return Sha256RequestDigester.Instance.ComputeDigest(payload);
    }

    /// <summary>
    /// 写审计（出口未接线时跳过；实现约定不抛出阻断业务路径的异常）。
    /// </summary>
    /// <param name="outcome">判定结果。</param>
    /// <param name="requestDigest">请求摘要。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task WriteAuditAsync(OnlineIdempotencyOutcome outcome, string requestDigest, CancellationToken cancellationToken)
    {
        if (_auditSink == null)
        {
            return;
        }

        var record = new OnlineIdempotencyAuditRecord
        {
            OutcomeKind = outcome.Kind,
            ScopeKey = outcome.ScopeKey,
            IdempotencyKey = outcome.IdempotencyKey,
            RequestDigest = requestDigest,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        await _auditSink.RecordAsync(record, cancellationToken);
    }
}
