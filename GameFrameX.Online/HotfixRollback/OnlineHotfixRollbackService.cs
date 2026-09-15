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
//   Any disputes or liabilities arising from secondary development based on this project
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

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Audit;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Idempotency;
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.HotfixRollback;

/// <summary>
/// Hotfix 回滚服务（vault:C9 S8.5 / Server gfx-doc C29 T3：服务端配合面——版本协议清单登记、
/// 协议兼容性检查（回滚前置闸门）与 Hotfix 回滚受控命令；VC-8.9「协议兼容检查通过」服务端半边）。
/// <para>
/// 维护约束（红线）：
/// ① <b>回滚命令顺序固定</b>——参数校验（4001 宁拒毋缺）→ 幂等先行（C93，重放透传首次结果）→
/// 读清单（目标未登记 4002 / 无活跃版本 5001 / 目标 = 当前 5003）→ 协议兼容检查（不兼容 6002 拒绝）→
/// <b>审计先行</b>（C105 统一审计，Domain = Operation，审计失败 1001 拒绝且切换未发生——「无审计不执行」）→
/// 执行器回滚（未装配 / 失败 8002）→ 切活跃版本 → 事件发布 → 幂等落定；
/// ② <b>登记 / 激活不落审计</b>——登记是发布流水线簿记、激活是装配面初始化入口，回滚是唯一受控操作
/// （操作者显式命令 + 审计）；回滚审计标识从命令幂等键确定性派生（<c>hotfix-rollback-{幂等键}</c>，
/// 重复执行同一命令命中同一条审计）；
/// ③ <b>作用域两键锚定</b>——清单与活跃指针是 App 级资产，服务内统一以 (TenantId, AppId) 定位并归一
/// ServerId = 0（对齐 C105 审计口径）；多实例逐实例回滚编排归运维（OL-025），本服务单进程语义；
/// ④ <b>依赖方向</b>——实际 DLL 加载切换经 <see cref="IOnlineHotfixRollbackExecutor"/> 承载（X4 装配面），
/// 本模块不引用 Core。
/// </para>
/// </summary>
public sealed class OnlineHotfixRollbackService
{
    /// <summary>
    /// 版本清单存储（登记簿 + 活跃版本指针的唯一写入面）。
    /// </summary>
    private readonly IOnlineHotfixVersionStore _store;

    /// <summary>
    /// 幂等判定（C93 组装；回滚命令的重复执行透传首次结果）。
    /// </summary>
    private readonly OnlineIdempotencyService _idempotencyService;

    /// <summary>
    /// 统一审计链路（回滚受控操作的审计先行落点）。
    /// </summary>
    private readonly OnlineAuditService _auditService;

    /// <summary>
    /// 回滚执行器（可空 = 未装配；回滚命令执行时未装配拒绝 8002）。
    /// </summary>
    private readonly IOnlineHotfixRollbackExecutor _executor;

    /// <summary>
    /// 事件发布出口（可空 = 未接线时跳过，C103 先例）。
    /// </summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>
    /// 初始化 <see cref="OnlineHotfixRollbackService"/>。
    /// </summary>
    /// <param name="store">版本清单存储。</param>
    /// <param name="idempotencyService">幂等判定服务。</param>
    /// <param name="auditService">统一审计服务（回滚审计先行的必要依赖，不可为空）。</param>
    /// <param name="executor">回滚执行器（运行时装配注入；未装配时回滚命令拒绝）。</param>
    /// <param name="eventPublisher">事件发布出口（未接线时跳过事件发布）。</param>
    public OnlineHotfixRollbackService(IOnlineHotfixVersionStore store, OnlineIdempotencyService idempotencyService, OnlineAuditService auditService, IOnlineHotfixRollbackExecutor executor = null, IOnlineEventPublisher eventPublisher = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _executor = executor;
        _eventPublisher = eventPublisher;
    }

    /// <summary>
    /// 登记一份版本协议清单（发布流水线簿记：不改变活跃版本、不落审计；重复登记幂等回执不覆盖）。
    /// </summary>
    /// <param name="manifest">待登记清单。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新登记返回 <c>true</c>；同 (作用域, 版本号) 已存在返回 <c>false</c>；清单非法返回 4001。</returns>
    public async Task<OnlineResult<bool>> RegisterAsync(OnlineHotfixProtocolManifest manifest, CancellationToken cancellationToken = default)
    {
        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var validation = ValidateManifest(manifest);
        if (validation != null)
        {
            return validation;
        }

        var isNew = await _store.RegisterAsync(manifest, cancellationToken).ConfigureAwait(false);
        if (isNew && _eventPublisher != null)
        {
            await _eventPublisher.PublishAsync(OnlineHotfixEvents.CreateVersionRegistered(manifest), cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<bool>.Ok(isNew);
    }

    /// <summary>
    /// 激活一个已登记版本为活跃版本（装配面初始化入口：不落审计、不经幂等）。
    /// </summary>
    /// <param name="scope">生效作用域（只用 TenantId/AppId 两键定位）。</param>
    /// <param name="version">目标版本号（须已登记）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>激活成功（重复激活同版本幂等返回成功）；作用域缺失 4001、版本未登记 4002。</returns>
    public async Task<OnlineResult<bool>> ActivateAsync(OnlineScope scope, string version, CancellationToken cancellationToken = default)
    {
        var scopeValidation = ValidateTwoKeyScope(scope);
        if (scopeValidation != null)
        {
            return scopeValidation;
        }

        if (string.IsNullOrWhiteSpace(version) || version.IndexOf('|') >= 0)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "版本号必填且禁止包含竖线（回放令牌分隔符）");
        }

        var manifest = await _store.FindAsync(scope.TenantId, scope.AppId, version, cancellationToken).ConfigureAwait(false);
        if (manifest == null)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ResourceNotFound, "目标版本未登记清单，无法激活：" + version);
        }

        await _store.SetActiveAsync(scope.TenantId, scope.AppId, version, cancellationToken).ConfigureAwait(false);
        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 协议兼容预检（只读演练入口：回滚演练 Runbook / Admin 预检调用；不经幂等、不落审计、不触执行器）。
    /// </summary>
    /// <param name="scope">生效作用域（只用 TenantId/AppId 两键定位）。</param>
    /// <param name="targetVersion">目标回滚版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>兼容检查报告；作用域缺失 4001、目标未登记 4002、无活跃版本 5001。</returns>
    public async Task<OnlineResult<OnlineProtocolCompatibilityReport>> CheckProtocolCompatibilityAsync(OnlineScope scope, string targetVersion, CancellationToken cancellationToken = default)
    {
        var scopeValidation = ValidateTwoKeyScope(scope);
        if (scopeValidation != null)
        {
            return OnlineResult<OnlineProtocolCompatibilityReport>.Fail(scopeValidation.Code, scopeValidation.Message);
        }

        var preparation = await PrepareTargetAndCurrentAsync(scope.TenantId, scope.AppId, targetVersion, cancellationToken).ConfigureAwait(false);
        if (!preparation.IsSuccess)
        {
            return OnlineResult<OnlineProtocolCompatibilityReport>.Fail(preparation.Code, preparation.Message);
        }

        return OnlineResult<OnlineProtocolCompatibilityReport>.Ok(OnlineProtocolCompatibilityChecker.Check(preparation.Data.Current, preparation.Data.Target));
    }

    /// <summary>
    /// 执行 Hotfix 回滚受控命令（幂等先行 → 清单守卫 → 协议兼容闸门 → 审计先行 → 执行器 → 切活跃 → 事件 → 幂等落定）。
    /// </summary>
    /// <param name="request">回滚命令请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>回滚回执（重复命令幂等回放首次结果，<c>IsReplay = true</c>）；
    /// 参数缺失 4001 / 目标未登记 4002 / 无活跃版本 5001 / 目标=当前 5003 /
    /// 协议不兼容 6002 / 审计失败 1001 / 执行器未装配或失败 8002。</returns>
    public async Task<OnlineResult<OnlineHotfixRollbackResult>> RollbackAsync(OnlineHotfixRollbackRequest request, CancellationToken cancellationToken = default)
    {
        var validation = ValidateRollbackRequest(request);
        if (validation != null)
        {
            return validation;
        }

        var anchor = new OnlineScope(request.Scope.TenantId, request.Scope.AppId, 0);
        var outcome = await _idempotencyService.BeginAsync(anchor, request.IdempotencyKey, BuildCanonicalRequestText(anchor, request), false, cancellationToken).ConfigureAwait(false);
        if (!outcome.CanExecute)
        {
            return await ResolveNonExecuteOutcomeAsync(request, outcome, cancellationToken).ConfigureAwait(false);
        }

        var preparation = await PrepareRollbackAsync(anchor, request, cancellationToken).ConfigureAwait(false);
        if (!preparation.IsSuccess)
        {
            return OnlineResult<OnlineHotfixRollbackResult>.Fail(preparation.Code, preparation.Message);
        }

        return await ExecuteRollbackAsync(anchor, request, preparation.Data, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 回滚命令的清单守卫链（目标可寻 4002 / 有活跃 5001 / 目标≠当前 5003 / 协议兼容 6002；
    /// 任一守卫失败把幂等键落定失败后返回错误——失败命令不得占住幂等占位）。
    /// </summary>
    /// <param name="anchor">两键锚定作用域。</param>
    /// <param name="request">回滚命令请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>守卫通过返回回滚准备物（目标清单 + 当前清单 + 兼容报告）；否则返回对应错误。</returns>
    private async Task<OnlineResult<RollbackPreparation>> PrepareRollbackAsync(OnlineScope anchor, OnlineHotfixRollbackRequest request, CancellationToken cancellationToken)
    {
        var guard = await PrepareTargetAndCurrentAsync(anchor.TenantId, anchor.AppId, request.TargetVersion, cancellationToken).ConfigureAwait(false);
        if (!guard.IsSuccess)
        {
            await FailIdempotencyAsync(anchor, request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            return OnlineResult<RollbackPreparation>.Fail(guard.Code, guard.Message);
        }

        var preparation = guard.Data;
        if (string.Equals(preparation.Current.Version, preparation.Target.Version, StringComparison.Ordinal))
        {
            await FailIdempotencyAsync(anchor, request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            return OnlineResult<RollbackPreparation>.Fail(OnlineErrorCode.StateOperationForbidden, "目标版本即当前活跃版本，无需回滚：" + request.TargetVersion);
        }

        preparation.Report = OnlineProtocolCompatibilityChecker.Check(preparation.Current, preparation.Target);
        if (!preparation.Report.IsCompatible)
        {
            await FailIdempotencyAsync(anchor, request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            return OnlineResult<RollbackPreparation>.Fail(OnlineErrorCode.VersionConflict, "协议兼容检查不通过，拒绝回滚（差异摘要：" + BuildCompatibilitySummary(preparation.Report) + "）");
        }

        return OnlineResult<RollbackPreparation>.Ok(preparation);
    }

    /// <summary>
    /// 回滚执行链（审计先行 → 执行器 → 切活跃 → 事件 → 幂等落定）。
    /// </summary>
    /// <param name="anchor">两键锚定作用域。</param>
    /// <param name="request">回滚命令请求。</param>
    /// <param name="preparation">守卫链产物（目标清单 + 当前清单 + 兼容报告）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>回滚回执。</returns>
    private async Task<OnlineResult<OnlineHotfixRollbackResult>> ExecuteRollbackAsync(OnlineScope anchor, OnlineHotfixRollbackRequest request, RollbackPreparation preparation, CancellationToken cancellationToken)
    {
        var auditFailure = await IngestRollbackAuditAsync(anchor, request, preparation, cancellationToken).ConfigureAwait(false);
        if (auditFailure != null)
        {
            await FailIdempotencyAsync(anchor, request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            return auditFailure;
        }

        if (_executor == null)
        {
            await FailIdempotencyAsync(anchor, request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            return OnlineResult<OnlineHotfixRollbackResult>.Fail(OnlineErrorCode.DependencyUnavailable, "回滚执行器未装配（运行时装配 X4 须注入 IOnlineHotfixRollbackExecutor）");
        }

        try
        {
            await _executor.RollbackAsync(request.TargetVersion, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await FailIdempotencyAsync(anchor, request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            return OnlineResult<OnlineHotfixRollbackResult>.Fail(OnlineErrorCode.DependencyUnavailable, "回滚执行器执行失败（回滚未生效）：" + ex.Message);
        }

        string previousVersion;
        try
        {
            previousVersion = await _store.SetActiveAsync(anchor.TenantId, anchor.AppId, request.TargetVersion, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 执行器已切换但活跃指针未落——已登记残留风险（review.md）：装配层启动对账收敛；审计已先行留痕。
            await FailIdempotencyAsync(anchor, request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            return OnlineResult<OnlineHotfixRollbackResult>.Fail(OnlineErrorCode.InternalError, "活跃版本切换失败（状态不一致，须对账）：" + ex.Message);
        }

        var summary = BuildCompatibilitySummary(preparation.Report);
        if (_eventPublisher != null)
        {
            await _eventPublisher.PublishAsync(OnlineHotfixEvents.CreateRolledBack(anchor.TenantId, anchor.AppId, previousVersion, request.TargetVersion, summary), cancellationToken).ConfigureAwait(false);
        }

        var result = new OnlineHotfixRollbackResult
        {
            TargetVersion = request.TargetVersion,
            PreviousVersion = previousVersion,
            IsReplay = false,
            CompatibilitySummary = summary,
        };
        await _idempotencyService.CompleteAsync(anchor, request.IdempotencyKey, EncodeReplayToken(previousVersion, request.TargetVersion, summary), false, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineHotfixRollbackResult>.Ok(result);
    }

    /// <summary>
    /// 读取并守卫目标 / 当前清单（目标未登记 4002、无活跃版本 5001；供预检与回滚守卫链共用）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="targetVersion">目标版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>守卫通过返回准备物；否则返回对应错误。</returns>
    private async Task<OnlineResult<RollbackPreparation>> PrepareTargetAndCurrentAsync(long tenantId, long appId, string targetVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetVersion))
        {
            return OnlineResult<RollbackPreparation>.Fail(OnlineErrorCode.ParameterInvalid, "目标版本号必填");
        }

        var target = await _store.FindAsync(tenantId, appId, targetVersion, cancellationToken).ConfigureAwait(false);
        if (target == null)
        {
            return OnlineResult<RollbackPreparation>.Fail(OnlineErrorCode.ResourceNotFound, "目标版本未登记清单：" + targetVersion);
        }

        var current = await _store.FindActiveAsync(tenantId, appId, cancellationToken).ConfigureAwait(false);
        if (current == null)
        {
            return OnlineResult<RollbackPreparation>.Fail(OnlineErrorCode.StateNotReady, "当前无活跃版本，无从回滚（先经 ActivateAsync 激活基线版本）");
        }

        return OnlineResult<RollbackPreparation>.Ok(new RollbackPreparation { Target = target, Current = current });
    }

    /// <summary>
    /// 处理幂等判定的不可执行分支（Replay 透传首次结果；Conflict/Busy/InvalidKey 映射协议错误码）。
    /// </summary>
    /// <param name="request">回滚命令请求。</param>
    /// <param name="outcome">幂等判定结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>回放结果或错误结果。</returns>
    private async Task<OnlineResult<OnlineHotfixRollbackResult>> ResolveNonExecuteOutcomeAsync(OnlineHotfixRollbackRequest request, OnlineIdempotencyOutcome outcome, CancellationToken cancellationToken)
    {
        if (outcome.Kind == OnlineIdempotencyOutcomeKind.Replay)
        {
            return await BuildReplayResultAsync(outcome, cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<OnlineHotfixRollbackResult>.Fail(outcome.ErrorCode, "回滚命令幂等判定拒绝（" + outcome.Kind + "）：键 " + request.IdempotencyKey);
    }

    /// <summary>
    /// 构造幂等回放结果（从回放令牌还原首次回滚回执——IsReplay = true 且不重复执行 / 不重复落审计）。
    /// </summary>
    /// <param name="outcome">幂等判定（Replay）。</param>
    /// <param name="cancellationToken">取消令牌（回放路径无异步等待，令牌仅对齐签名）。</param>
    /// <returns>与首次结果同构的回放回执。</returns>
    private static Task<OnlineResult<OnlineHotfixRollbackResult>> BuildReplayResultAsync(OnlineIdempotencyOutcome outcome, CancellationToken cancellationToken)
    {
        var token = Encoding.UTF8.GetString(outcome.FirstResponse.ToArray());
        var separatorIndex = token.LastIndexOf('|');
        if (separatorIndex <= 0 || separatorIndex >= token.Length - 1)
        {
            return Task.FromResult(OnlineResult<OnlineHotfixRollbackResult>.Fail(OnlineErrorCode.InternalError, "幂等回放令牌损坏"));
        }

        // 令牌 = previous|target|summary；previous 与 target 均经「版本号禁含竖线」校验，从右侧切出 summary 后其余即版本段。
        var summaryIndex = token.LastIndexOf('|', separatorIndex - 1);
        if (summaryIndex <= 0)
        {
            return Task.FromResult(OnlineResult<OnlineHotfixRollbackResult>.Fail(OnlineErrorCode.InternalError, "幂等回放令牌损坏"));
        }

        var result = new OnlineHotfixRollbackResult
        {
            PreviousVersion = token.Substring(0, summaryIndex),
            TargetVersion = token.Substring(summaryIndex + 1, separatorIndex - summaryIndex - 1),
            IsReplay = true,
            CompatibilitySummary = token.Substring(separatorIndex + 1),
        };
        return Task.FromResult(OnlineResult<OnlineHotfixRollbackResult>.Ok(result));
    }

    /// <summary>
    /// 审计先行：把回滚命令落统一审计（Domain = Operation；审计失败即拒绝执行——「无审计不执行」）。
    /// </summary>
    /// <param name="anchor">两键锚定作用域。</param>
    /// <param name="request">回滚命令请求。</param>
    /// <param name="preparation">守卫链产物。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>审计失败时返回 1001 错误结果；成功返回 <see langword="null"/>。</returns>
    private async Task<OnlineResult<OnlineHotfixRollbackResult>> IngestRollbackAuditAsync(OnlineScope anchor, OnlineHotfixRollbackRequest request, RollbackPreparation preparation, CancellationToken cancellationToken)
    {
        var entry = new OnlineAuditEntry
        {
            Domain = OnlineAuditDomain.Operation,
            EventId = "hotfix-rollback-" + request.IdempotencyKey,
            EventType = OnlineHotfixEvents.RolledBack,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TenantId = anchor.TenantId,
            AppId = anchor.AppId,
            ServerId = 0,
            PlayerId = 0,
            OperatorId = request.OperatorId,
            OperatorName = request.OperatorName,
            Reason = request.Reason,
            Source = OnlineHotfixEvents.Source,
            CorrelationId = request.TargetVersion,
            PayloadAuditFields = new Dictionary<string, string>
            {
                { "PreviousVersion", preparation.Current.Version ?? string.Empty },
                { "TargetVersion", request.TargetVersion },
                { "RemovedCount", preparation.Report.RemovedCount.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { "ChangedCount", preparation.Report.ChangedCount.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { "AddedCount", preparation.Report.AddedCount.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            },
        };

        try
        {
            var ingest = await _auditService.IngestAsync(entry, cancellationToken).ConfigureAwait(false);
            if (!ingest.IsSuccess)
            {
                return OnlineResult<OnlineHotfixRollbackResult>.Fail(OnlineErrorCode.InternalError, "回滚审计接入被拒（无审计不执行）：" + ingest.Message);
            }

            return null;
        }
        catch (Exception ex)
        {
            return OnlineResult<OnlineHotfixRollbackResult>.Fail(OnlineErrorCode.InternalError, "回滚审计接入异常（无审计不执行）：" + ex.Message);
        }
    }

    /// <summary>
    /// 把幂等键落定失败（失败路径专用；落定失败不掩盖原始错误，静默吞并）。
    /// </summary>
    /// <param name="anchor">两键锚定作用域。</param>
    /// <param name="idempotencyKey">命令幂等键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task FailIdempotencyAsync(OnlineScope anchor, string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            await _idempotencyService.FailAsync(anchor, idempotencyKey, false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 失败路径的幂等落定失败不得掩盖原始业务错误；占位未释放由 Foundation 失败策略与超时收敛。
        }
    }

    /// <summary>
    /// 校验回滚命令请求（操作者 / 原因 / 目标版本 / 幂等键必填与格式，4001 宁拒毋缺）。
    /// </summary>
    /// <param name="request">回滚命令请求。</param>
    /// <returns>非法时返回失败结果；合法返回 <see langword="null"/>。</returns>
    private static OnlineResult<OnlineHotfixRollbackResult> ValidateRollbackRequest(OnlineHotfixRollbackRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Scope == null)
        {
            return OnlineResult<OnlineHotfixRollbackResult>.Fail(OnlineErrorCode.ParameterInvalid, "回滚命令必须携带作用域（TenantId / AppId 两键锚定）");
        }

        if (request.Scope.TenantId <= 0 || request.Scope.AppId <= 0)
        {
            return OnlineResult<OnlineHotfixRollbackResult>.Fail(OnlineErrorCode.ParameterInvalid, "回滚命令必须锚定租户与 App（TenantId / AppId 大于 0）");
        }

        if (string.IsNullOrWhiteSpace(request.TargetVersion) || request.TargetVersion.IndexOf('|') >= 0)
        {
            return OnlineResult<OnlineHotfixRollbackResult>.Fail(OnlineErrorCode.ParameterInvalid, "目标版本号必填且禁止包含竖线（回放令牌分隔符）");
        }

        if (string.IsNullOrWhiteSpace(request.OperatorId))
        {
            return OnlineResult<OnlineHotfixRollbackResult>.Fail(OnlineErrorCode.ParameterInvalid, "操作者（OperatorId）必填——受控操作审计完整性要求");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return OnlineResult<OnlineHotfixRollbackResult>.Fail(OnlineErrorCode.ParameterInvalid, "操作原因（Reason）必填——受控操作审计完整性要求");
        }

        if (!OnlineRequestContextValidator.IsIdempotencyKeyValid(request.IdempotencyKey))
        {
            return OnlineResult<OnlineHotfixRollbackResult>.Fail(OnlineErrorCode.ParameterInvalid, "幂等键必填且格式合法（字母数字下划线连字符，长度 1～128）");
        }

        return null;
    }

    /// <summary>
    /// 校验登记清单（版本号 / 作用域 / 契约行名与号的唯一性，4001 宁拒毋缺）。
    /// </summary>
    /// <param name="manifest">待登记清单。</param>
    /// <returns>非法时返回失败结果；合法返回 <see langword="null"/>。</returns>
    private static OnlineResult<bool> ValidateManifest(OnlineHotfixProtocolManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.IndexOf('|') >= 0)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "清单版本号必填且禁止包含竖线（回放令牌分隔符）");
        }

        if (manifest.TenantId <= 0 || manifest.AppId <= 0)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "清单必须锚定租户与 App（TenantId / AppId 大于 0）");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<int>();
        foreach (var message in manifest.Messages ?? Array.Empty<OnlineHotfixProtocolMessage>())
        {
            if (message == null || string.IsNullOrWhiteSpace(message.MessageName))
            {
                return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "协议消息契约行的消息名必填（版本 " + manifest.Version + "）");
            }

            if (message.MessageId <= 0)
            {
                return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "协议消息 " + message.MessageName + " 的消息号必须大于 0");
            }

            if (!names.Add(message.MessageName))
            {
                return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "清单内消息名重复：" + message.MessageName + "（版本 " + manifest.Version + "）");
            }

            if (!ids.Add(message.MessageId))
            {
                return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "清单内消息号重复：" + message.MessageId + "（版本 " + manifest.Version + "）");
            }
        }

        return null;
    }

    /// <summary>
    /// 校验两键作用域（预检 / 激活入口共用）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <returns>非法时返回失败结果；合法返回 <see langword="null"/>。</returns>
    private static OnlineResult<bool> ValidateTwoKeyScope(OnlineScope scope)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (scope.TenantId <= 0 || scope.AppId <= 0)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "必须锚定租户与 App（TenantId / AppId 大于 0；清单与活跃指针是 App 级资产）");
        }

        return null;
    }

    /// <summary>
    /// 构造回滚命令的规范化请求文本（幂等意图摘要——同键不同意图判冲突；目标版本 + 操作者为意图主体）。
    /// </summary>
    /// <param name="anchor">两键锚定作用域。</param>
    /// <param name="request">回滚命令请求。</param>
    /// <returns>规范化请求文本。</returns>
    private static string BuildCanonicalRequestText(OnlineScope anchor, OnlineHotfixRollbackRequest request)
    {
        return "hotfix-rollback|v1|" + anchor.TenantId + ":" + anchor.AppId + "|" + request.TargetVersion + "|" + request.OperatorId;
    }

    /// <summary>
    /// 构造协议兼容报告摘要（Removed/Changed/Added 计数文本；不含竖线，可入回放令牌）。
    /// </summary>
    /// <param name="report">兼容检查报告。</param>
    /// <returns>摘要文本。</returns>
    private static string BuildCompatibilitySummary(OnlineProtocolCompatibilityReport report)
    {
        return "Removed=" + report.RemovedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
               + ",Changed=" + report.ChangedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
               + ",Added=" + report.AddedCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 编码回放令牌（previous|target|summary；版本段经「禁含竖线」校验保证可逆解析）。
    /// </summary>
    /// <param name="previousVersion">回滚前活跃版本号。</param>
    /// <param name="targetVersion">目标版本号。</param>
    /// <param name="summary">兼容摘要。</param>
    /// <returns>令牌字节。</returns>
    private static ReadOnlyMemory<byte> EncodeReplayToken(string previousVersion, string targetVersion, string summary)
    {
        return Encoding.UTF8.GetBytes(previousVersion + "|" + targetVersion + "|" + summary);
    }

    /// <summary>
    /// 回滚守卫链产物（目标清单 + 当前清单 + 兼容报告）。
    /// </summary>
    private sealed class RollbackPreparation
    {
        /// <summary>
        /// 获取或设置目标（回滚到）清单。
        /// </summary>
        public OnlineHotfixProtocolManifest Target
        {
            get;
            set;
        }

        /// <summary>
        /// 获取或设置当前（活跃）清单。
        /// </summary>
        public OnlineHotfixProtocolManifest Current
        {
            get;
            set;
        }

        /// <summary>
        /// 获取或设置协议兼容检查报告（守卫链后段填充）。
        /// </summary>
        public OnlineProtocolCompatibilityReport Report
        {
            get;
            set;
        }
    }
}
