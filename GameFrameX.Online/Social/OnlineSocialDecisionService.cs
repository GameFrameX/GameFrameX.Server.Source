//  ==========================================================================================
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

namespace GameFrameX.Online.Social;

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;

/// <summary>
/// Block / Mute / Report / 处罚的**唯一判定入口**（vault:C7 S6.3，方案复审 P0-1 / P0-2 收口点）。
/// <para>
/// 维护约束（为什么必须唯一）：vault:C7 风险表首条是「三通路各自实现 Block 判定，行为不一致出现绕过」。
/// 因此 Chat 私聊、Party 邀请、Matchmaker 成组三个通路都只能经本类型裁决——
/// 跨域通路经 <see cref="IOnlineSocialGate"/>（本类型实现），同域 Chat 经
/// <see cref="EvaluateSendAsync"/>。**任何通路内自建屏蔽/处罚判定都视为回归**。
/// </para>
/// <para>
/// 裁决力度矩阵（唯一权威，改这里就是改全部通路）：
/// <list type="table">
/// <item><term>屏蔽</term><description>双向生效，拒绝全部三种互动目的；不暴露方向（屏蔽是单向私密事实）。</description></item>
/// <item><term>禁言</term><description>只拒绝发言（<see cref="OnlineSocialInteractionPurpose.DirectMessage"/> 与
/// <see cref="EvaluateSendAsync"/>）；**不影响**组队邀请与匹配——禁言不剥夺组队能力。</description></item>
/// <item><term>封禁</term><description>拒绝被处罚玩家**自己发起**的全部互动，映射 <c>AccountBanned</c>。</description></item>
/// <item><term>静音</term><description>**不参与任何拒绝裁决**（展示层偏好，见 <see cref="OnlineMuteEntry"/>）。</description></item>
/// </list>
/// </para>
/// <para>
/// 维护约束（封禁的边界）：封禁冻结的是**被处罚玩家自己发起的互动**；「当前连接」的处置
/// （踢线 / 拒绝登录）归运行时与鉴权层，不在本服务内。要让封禁同时冻结他人对其发起的互动，
/// 只能在本入口补充对端检查——禁止改到各通路里分别加，那会立刻退化成上面说的绕过问题。
/// </para>
/// </summary>
public sealed class OnlineSocialDecisionService : IOnlineSocialGate
{
    /// <summary>社交关系图谱存储（屏蔽 / 静音 / 处罚）。</summary>
    private readonly IOnlineSocialGraphStore _graphStore;

    /// <summary>举报案件存储。</summary>
    private readonly IOnlineReportStore _reportStore;

    /// <summary>事件发布出口。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>
    /// 初始化 <see cref="OnlineSocialDecisionService"/>。
    /// </summary>
    /// <param name="graphStore">社交关系图谱存储。</param>
    /// <param name="reportStore">举报案件存储。</param>
    /// <param name="eventPublisher">事件发布出口。</param>
    public OnlineSocialDecisionService(IOnlineSocialGraphStore graphStore, IOnlineReportStore reportStore, IOnlineEventPublisher eventPublisher)
    {
        _graphStore = graphStore ?? throw new ArgumentNullException(nameof(graphStore));
        _reportStore = reportStore ?? throw new ArgumentNullException(nameof(reportStore));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    /// <summary>
    /// 裁决一次定向社交互动（<see cref="IOnlineSocialGate"/> 的实现：Party / Matchmaking 的唯一入口）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="fromPlayerId">发起互动的玩家。</param>
    /// <param name="toPlayerId">互动的目标玩家。</param>
    /// <param name="purpose">互动目的（决定裁决力度）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>裁决结果。</returns>
    public async Task<OnlineSocialDecision> EvaluateAsync(long tenantId, long appId, long fromPlayerId, long toPlayerId, OnlineSocialInteractionPurpose purpose, CancellationToken cancellationToken = default)
    {
        if (fromPlayerId <= 0 || toPlayerId <= 0)
        {
            return OnlineSocialDecision.Deny(OnlineErrorCode.ParameterInvalid, "互动双方标识必须有效");
        }

        if (fromPlayerId == toPlayerId)
        {
            return OnlineSocialDecision.Deny(OnlineErrorCode.ParameterInvalid, "不能与自己发生定向互动");
        }

        var selfDecision = await EvaluatePunishmentsAsync(tenantId, appId, fromPlayerId, purpose, cancellationToken).ConfigureAwait(false);
        if (!selfDecision.Allowed)
        {
            return selfDecision;
        }

        var blocked = await _graphStore.IsBlockedEitherWayAsync(tenantId, appId, fromPlayerId, toPlayerId, cancellationToken).ConfigureAwait(false);
        if (blocked)
        {
            // 双向生效、不指明方向：屏蔽是单向私密事实，泄露方向等于把「谁屏蔽了谁」透给被屏蔽方。
            return OnlineSocialDecision.Deny(OnlineErrorCode.RiskControlRejected, "互动双方存在屏蔽关系");
        }

        return OnlineSocialDecision.Allow();
    }

    /// <summary>
    /// 裁决某玩家在**无对端**场景下能否发言（同域入口：Global / Party / Group 频道发送与内容发布）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>裁决结果；放行表示既未封禁也未禁言。</returns>
    public Task<OnlineSocialDecision> EvaluateSendAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        if (scope == null || scope.PlayerId <= 0)
        {
            return Task.FromResult(OnlineSocialDecision.Deny(OnlineErrorCode.ParameterInvalid, "缺少玩家主体位"));
        }

        return EvaluatePunishmentsAsync(scope.TenantId, scope.AppId, scope.PlayerId, OnlineSocialInteractionPurpose.DirectMessage, cancellationToken);
    }

    /// <summary>
    /// 施加屏蔽（重复屏蔽幂等：返回既有记录，不报错、不刷新时间）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="targetPlayerId">被屏蔽玩家。</param>
    /// <param name="reason">屏蔽原因（可空）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效的屏蔽记录。</returns>
    public async Task<OnlineResult<OnlineBlockEntry>> BlockAsync(OnlineScope scope, long targetPlayerId, string reason = null, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineBlockEntry>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (targetPlayerId <= 0 || targetPlayerId == scope.PlayerId)
        {
            return OnlineResult<OnlineBlockEntry>.Fail(OnlineErrorCode.ParameterInvalid, "被屏蔽玩家无效");
        }

        var existing = await _graphStore.FindBlockAsync(scope.TenantId, scope.AppId, scope.PlayerId, targetPlayerId, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            return OnlineResult<OnlineBlockEntry>.Ok(existing);
        }

        var now = Now();
        var prototype = new OnlineBlockEntry
        {
            EntryId = "blk-" + Guid.NewGuid().ToString("N"),
            OwnerId = scope.PlayerId,
            BlockedPlayerId = targetPlayerId,
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            ServerId = scope.ServerId,
            Reason = reason ?? string.Empty,
            CreatedAtTime = now,
        };
        var effective = await _graphStore.SaveBlockIfAbsentAsync(prototype, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(effective.EntryId, prototype.EntryId, StringComparison.Ordinal))
        {
            // 输给了并发写入者：关系已存在，事件已由赢家发出，不重复发。
            // 判据是每次调用新铸的记录标识，不是「创建时刻是否等于本地时钟」——同一毫秒内的两次调用
            // 会让双方都判定成赢家，各发一次 BlockAdded。
            return OnlineResult<OnlineBlockEntry>.Ok(effective);
        }

        await _eventPublisher.PublishAsync(OnlineSocialEvents.CreateBlockChanged(effective, OnlineSocialEvents.BlockAdded, correlationId), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineBlockEntry>.Ok(effective);
    }

    /// <summary>
    /// 解除屏蔽（仅屏蔽发起人本人可解除；重复解除幂等成功）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="targetPlayerId">被屏蔽玩家。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功负载表示本次是否真的移除了记录。</returns>
    public async Task<OnlineResult<bool>> UnblockAsync(OnlineScope scope, long targetPlayerId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<bool>(scope);
        if (failure != null)
        {
            return failure;
        }

        var entry = await _graphStore.FindBlockAsync(scope.TenantId, scope.AppId, scope.PlayerId, targetPlayerId, cancellationToken).ConfigureAwait(false);
        var removed = await _graphStore.RemoveBlockAsync(scope.TenantId, scope.AppId, scope.PlayerId, targetPlayerId, cancellationToken).ConfigureAwait(false);
        if (removed && entry != null)
        {
            await _eventPublisher.PublishAsync(OnlineSocialEvents.CreateBlockChanged(entry, OnlineSocialEvents.BlockRemoved, correlationId), cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<bool>.Ok(removed);
    }

    /// <summary>
    /// 列出本人发起的全部屏蔽。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>屏蔽记录列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineBlockEntry>>> ListBlockedAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<IReadOnlyList<OnlineBlockEntry>>(scope);
        if (failure != null)
        {
            return failure;
        }

        var entries = await _graphStore.ListBlocksAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<IReadOnlyList<OnlineBlockEntry>>.Ok(entries);
    }

    /// <summary>
    /// 静音某玩家（重复静音幂等：返回既有记录；静音**不影响**任何裁决，只影响本人展示）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="targetPlayerId">被静音玩家。</param>
    /// <param name="expiresAtTime">静音失效时刻（UTC 毫秒；<c>0</c> 表示永久）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效的静音记录。</returns>
    public async Task<OnlineResult<OnlineMuteEntry>> MuteAsync(OnlineScope scope, long targetPlayerId, long expiresAtTime = 0, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineMuteEntry>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (targetPlayerId <= 0 || targetPlayerId == scope.PlayerId)
        {
            return OnlineResult<OnlineMuteEntry>.Fail(OnlineErrorCode.ParameterInvalid, "被静音玩家无效");
        }

        var existing = await _graphStore.FindMuteAsync(scope.TenantId, scope.AppId, scope.PlayerId, targetPlayerId, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            return OnlineResult<OnlineMuteEntry>.Ok(existing);
        }

        var now = Now();
        var prototype = new OnlineMuteEntry
        {
            EntryId = "mut-" + Guid.NewGuid().ToString("N"),
            OwnerId = scope.PlayerId,
            MutedPlayerId = targetPlayerId,
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            ServerId = scope.ServerId,
            ExpiresAtTime = expiresAtTime,
            CreatedAtTime = now,
        };
        var effective = await _graphStore.SaveMuteIfAbsentAsync(prototype, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(effective.EntryId, prototype.EntryId, StringComparison.Ordinal))
        {
            // 输给了并发写入者：关系已存在，事件已由赢家发出，不重复发（判据同 BlockAsync）。
            return OnlineResult<OnlineMuteEntry>.Ok(effective);
        }

        await _eventPublisher.PublishAsync(OnlineSocialEvents.CreateMuteChanged(effective, OnlineSocialEvents.MuteAdded, correlationId), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineMuteEntry>.Ok(effective);
    }

    /// <summary>
    /// 解除静音（重复解除幂等成功）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="targetPlayerId">被静音玩家。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功负载表示本次是否真的移除了记录。</returns>
    public async Task<OnlineResult<bool>> UnmuteAsync(OnlineScope scope, long targetPlayerId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<bool>(scope);
        if (failure != null)
        {
            return failure;
        }

        var entry = await _graphStore.FindMuteAsync(scope.TenantId, scope.AppId, scope.PlayerId, targetPlayerId, cancellationToken).ConfigureAwait(false);
        var removed = await _graphStore.RemoveMuteAsync(scope.TenantId, scope.AppId, scope.PlayerId, targetPlayerId, cancellationToken).ConfigureAwait(false);
        if (removed && entry != null)
        {
            await _eventPublisher.PublishAsync(OnlineSocialEvents.CreateMuteChanged(entry, OnlineSocialEvents.MuteRemoved, correlationId), cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<bool>.Ok(removed);
    }

    /// <summary>
    /// 列出本人发起的全部静音。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>静音记录列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineMuteEntry>>> ListMutedAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<IReadOnlyList<OnlineMuteEntry>>(scope);
        if (failure != null)
        {
            return failure;
        }

        var entries = await _graphStore.ListMutesAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<IReadOnlyList<OnlineMuteEntry>>.Ok(entries);
    }

    /// <summary>
    /// 判定本人是否静音了某玩家（展示层渲染输入；**不得**用于任何拒绝决策）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="targetPlayerId">目标玩家。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>仍在静音期返回 <c>true</c>。</returns>
    public async Task<OnlineResult<bool>> IsMutedAsync(OnlineScope scope, long targetPlayerId, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<bool>(scope);
        if (failure != null)
        {
            return failure;
        }

        var entry = await _graphStore.FindMuteAsync(scope.TenantId, scope.AppId, scope.PlayerId, targetPlayerId, cancellationToken).ConfigureAwait(false);
        if (entry == null)
        {
            return OnlineResult<bool>.Ok(false);
        }

        return OnlineResult<bool>.Ok(entry.IsEffectiveAt(Now()));
    }

    /// <summary>
    /// 提交举报（证据字段照单全收，VC-6.16 证据链的写入点）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="reportedPlayerId">被举报人。</param>
    /// <param name="scene">举报场景。</param>
    /// <param name="reason">举报原因。</param>
    /// <param name="matchId">对局标识（可空）。</param>
    /// <param name="chatMessageId">被举报消息标识（可空）。</param>
    /// <param name="channelId">频道标识（可空；聊天场景必填）。</param>
    /// <param name="evidence">补充说明（可空；原因选「其他」时必填）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已提交的案件。</returns>
    public async Task<OnlineResult<OnlineReportCase>> SubmitReportAsync(OnlineScope scope, long reportedPlayerId, OnlineReportScene scene, OnlineReportReason reason, string matchId = null, string chatMessageId = null, string channelId = null, string evidence = null, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineReportCase>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (reportedPlayerId <= 0 || reportedPlayerId == scope.PlayerId)
        {
            return OnlineResult<OnlineReportCase>.Fail(OnlineErrorCode.ParameterInvalid, "被举报人无效");
        }

        if (scene == OnlineReportScene.Chat && string.IsNullOrEmpty(channelId))
        {
            // 没有频道标识就没有可复现的取证路径，Admin 无法核实 —— 证据链在入口就要完整。
            return OnlineResult<OnlineReportCase>.Fail(OnlineErrorCode.ParameterInvalid, "聊天场景举报必须提供频道标识");
        }

        if (reason == OnlineReportReason.Other && string.IsNullOrWhiteSpace(evidence))
        {
            return OnlineResult<OnlineReportCase>.Fail(OnlineErrorCode.ParameterInvalid, "举报原因为「其他」时必须填写补充说明");
        }

        var now = Now();
        var prototype = new OnlineReportCase
        {
            ReportId = "rpt-" + Guid.NewGuid().ToString("N"),
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            ReporterId = scope.PlayerId,
            ReportedPlayerId = reportedPlayerId,
            Scene = scene,
            Reason = reason,
            MatchId = matchId ?? string.Empty,
            ChatMessageId = chatMessageId ?? string.Empty,
            ChannelId = channelId ?? string.Empty,
            Evidence = evidence ?? string.Empty,
            State = OnlineReportState.Submitted,
            Resolution = OnlineReportResolution.None,
            HandlerAdminId = 0,
            AdminCaseId = string.Empty,
            CreatedAtTime = now,
            UpdatedAtTime = now,
            ClosedAtTime = 0,
        };

        var effective = await _reportStore.SaveIfAbsentAsync(prototype, cancellationToken).ConfigureAwait(false);
        if (string.Equals(effective.ReportId, prototype.ReportId, StringComparison.Ordinal))
        {
            await _eventPublisher.PublishAsync(OnlineSocialEvents.CreateReportChanged(effective, correlationId), cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<OnlineReportCase>.Ok(effective);
    }

    /// <summary>
    /// 撤回举报（仅举报人本人、且案件仍处于「已提交」未受理时）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="reportId">案件标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>撤回后的案件。</returns>
    public async Task<OnlineResult<OnlineReportCase>> WithdrawReportAsync(OnlineScope scope, string reportId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineReportCase>(scope);
        if (failure != null)
        {
            return failure;
        }

        var reportCase = await _reportStore.FindAsync(scope.TenantId, scope.AppId, reportId, cancellationToken).ConfigureAwait(false);
        if (reportCase == null || reportCase.ReporterId != scope.PlayerId)
        {
            // 反预言：非举报人看不到这条案件的存在。
            return OnlineResult<OnlineReportCase>.Fail(OnlineErrorCode.ResourceNotFound, "举报案件不存在");
        }

        return await ApplyReportTransitionAsync(reportCase, OnlineReportState.Withdrawn, OnlineReportResolution.None, 0, null, correlationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 列出本人提交的全部举报。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>案件列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineReportCase>>> ListMyReportsAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<IReadOnlyList<OnlineReportCase>>(scope);
        if (failure != null)
        {
            return failure;
        }

        var cases = await _reportStore.ListByReporterAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<IReadOnlyList<OnlineReportCase>>.Ok(cases);
    }

    /// <summary>
    /// 受理 / 裁决举报案件（Admin 命令面的服务端半边；S6.10 页面归 Admin 仓）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="reportId">案件标识。</param>
    /// <param name="targetState">目标状态。</param>
    /// <param name="resolution">处置结果（进入「已处置」须非 <see cref="OnlineReportResolution.None"/>）。</param>
    /// <param name="handlerAdminId">受理 Admin 标识。</param>
    /// <param name="adminCaseId">Admin 侧案件关联键（可空）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>裁决后的案件。</returns>
    public async Task<OnlineResult<OnlineReportCase>> TransitionReportAsync(long tenantId, long appId, string reportId, OnlineReportState targetState, OnlineReportResolution resolution, long handlerAdminId, string adminCaseId = null, string correlationId = null, CancellationToken cancellationToken = default)
    {
        if (IsScopeLocatorInvalid(tenantId, appId) || string.IsNullOrEmpty(reportId))
        {
            return OnlineResult<OnlineReportCase>.Fail(OnlineErrorCode.ParameterInvalid, "案件定位参数无效");
        }

        var reportCase = await _reportStore.FindAsync(tenantId, appId, reportId, cancellationToken).ConfigureAwait(false);
        if (reportCase == null)
        {
            return OnlineResult<OnlineReportCase>.Fail(OnlineErrorCode.ResourceNotFound, "举报案件不存在");
        }

        return await ApplyReportTransitionAsync(reportCase, targetState, resolution, handlerAdminId, adminCaseId, correlationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 按状态列出案件（Admin 待办队列输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="state">案件状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>案件列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineReportCase>>> ListReportsByStateAsync(long tenantId, long appId, OnlineReportState state, CancellationToken cancellationToken = default)
    {
        if (IsScopeLocatorInvalid(tenantId, appId))
        {
            // 与同族的 TransitionReportAsync 同口径：Admin 面的入口先校验定位参数，不留「读接口不校验」的口子。
            return OnlineResult<IReadOnlyList<OnlineReportCase>>.Fail(OnlineErrorCode.ParameterInvalid, "案件定位参数无效");
        }

        var cases = await _reportStore.ListByStateAsync(tenantId, appId, state, cancellationToken).ConfigureAwait(false);
        return OnlineResult<IReadOnlyList<OnlineReportCase>>.Ok(cases);
    }

    /// <summary>
    /// 按玩家当前实际生效的处罚裁决（封禁拒绝全部目的，禁言只拒绝发言，未命中放行）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">被裁决玩家。</param>
    /// <param name="purpose">互动目的。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>裁决结果。</returns>
    private async Task<OnlineSocialDecision> EvaluatePunishmentsAsync(long tenantId, long appId, long playerId, OnlineSocialInteractionPurpose purpose, CancellationToken cancellationToken)
    {
        var active = await _graphStore.ListActivePunishmentsAsync(tenantId, appId, playerId, Now(), cancellationToken).ConfigureAwait(false);
        foreach (var punishment in active)
        {
            if (punishment.Kind == OnlinePunishmentKind.Ban)
            {
                return OnlineSocialDecision.Deny(OnlineErrorCode.AccountBanned, "账号处于封禁期");
            }

            if (punishment.Kind == OnlinePunishmentKind.Mute && purpose == OnlineSocialInteractionPurpose.DirectMessage)
            {
                return OnlineSocialDecision.Deny(OnlineErrorCode.RiskControlRejected, "账号处于禁言期");
            }
        }

        return OnlineSocialDecision.Allow();
    }

    /// <summary>
    /// 举报案件状态迁移的唯一实现点（玩家撤回与 Admin 裁决共用；先过状态机，再过处置结果正交守卫）。
    /// </summary>
    /// <param name="reportCase">当前案件快照。</param>
    /// <param name="targetState">目标状态。</param>
    /// <param name="resolution">处置结果。</param>
    /// <param name="handlerAdminId">受理 Admin 标识（玩家撤回传 <c>0</c>）。</param>
    /// <param name="adminCaseId">Admin 侧案件关联键（可空）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>迁移后的案件。</returns>
    private async Task<OnlineResult<OnlineReportCase>> ApplyReportTransitionAsync(OnlineReportCase reportCase, OnlineReportState targetState, OnlineReportResolution resolution, long handlerAdminId, string adminCaseId, string correlationId, CancellationToken cancellationToken)
    {
        if (reportCase.State == targetState)
        {
            // 幂等：重复受理 / 重复撤回返回既有状态，不打回。
            return OnlineResult<OnlineReportCase>.Ok(reportCase);
        }

        if (!OnlineReportStateMachine.TryTransition(reportCase.State, targetState))
        {
            return OnlineResult<OnlineReportCase>.Fail(OnlineErrorCode.StateOperationForbidden, "案件当前状态不允许迁移到 " + targetState);
        }

        if (targetState == OnlineReportState.Actioned && resolution == OnlineReportResolution.None)
        {
            return OnlineResult<OnlineReportCase>.Fail(OnlineErrorCode.ParameterInvalid, "裁决为已处置时必须给出处置结果");
        }

        if (targetState == OnlineReportState.Rejected && resolution != OnlineReportResolution.NoViolation)
        {
            return OnlineResult<OnlineReportCase>.Fail(OnlineErrorCode.ParameterInvalid, "驳回案件时处置结果必须为「无违规」");
        }

        var now = Now();
        var updated = reportCase.Copy();
        updated.State = targetState;
        updated.Resolution = resolution;
        updated.UpdatedAtTime = now;
        if (handlerAdminId > 0)
        {
            updated.HandlerAdminId = handlerAdminId;
        }

        if (!string.IsNullOrEmpty(adminCaseId))
        {
            updated.AdminCaseId = adminCaseId;
        }

        if (OnlineReportStateMachine.IsTerminal(targetState))
        {
            updated.ClosedAtTime = now;
        }

        var effective = await _reportStore.ReplaceAsync(updated, reportCase.State, cancellationToken).ConfigureAwait(false);
        if (effective == null)
        {
            // CAS 失败 = 已被并发裁决 / 撤回抢先；重读后按当前事实返回，保证收敛。
            var current = await _reportStore.FindAsync(reportCase.TenantId, reportCase.AppId, reportCase.ReportId, cancellationToken).ConfigureAwait(false);
            if (current == null)
            {
                return OnlineResult<OnlineReportCase>.Fail(OnlineErrorCode.ResourceNotFound, "举报案件不存在");
            }

            return OnlineResult<OnlineReportCase>.Ok(current);
        }

        await _eventPublisher.PublishAsync(OnlineSocialEvents.CreateReportChanged(effective, correlationId), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineReportCase>.Ok(effective);
    }

    /// <summary>
    /// 校验作用域与玩家主体位（泛型形态）。
    /// </summary>
    /// <typeparam name="TData">接口成功负载类型。</typeparam>
    /// <param name="scope">生效作用域。</param>
    /// <returns>成功返回 null，失败返回错误结果。</returns>
    private static OnlineResult<TData> ValidateScope<TData>(OnlineScope scope)
    {
        if (scope == null)
        {
            return OnlineResult<TData>.Fail(OnlineErrorCode.ParameterInvalid, "缺少作用域");
        }

        if (scope.PlayerId <= 0)
        {
            return OnlineResult<TData>.Fail(OnlineErrorCode.ParameterInvalid, "社交操作必须具备玩家主体位");
        }

        return null;
    }

    /// <summary>
    /// 判定租户与 App 定位是否无效（Admin 命令面无玩家主体位，只能靠作用域二元组定位）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <returns>无效返回 <c>true</c>。</returns>
    private static bool IsScopeLocatorInvalid(long tenantId, long appId)
    {
        return tenantId <= 0 || appId <= 0;
    }

    /// <summary>取当前 UTC 毫秒时刻。</summary>
    /// <returns>UTC 毫秒时间戳。</returns>
    private static long Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
