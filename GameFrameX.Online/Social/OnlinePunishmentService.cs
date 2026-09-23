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

/// <summary>
/// 处罚施加 / 撤销 / 查询服务（vault:C7 S6.3，方案复审 P0-2 收口点：Admin 命令面的服务端半边）。
/// <para>
/// 维护约束（归属边界）：本服务只负责**记录处罚事实**；处罚的运行时强制在
/// <see cref="OnlineSocialDecisionService"/>（Chat / Party / Matchmaker 三处同源裁决）。
/// 二者不可合并——记录是 Admin 的持久化事实，强制是每帧的高频读路径，混在一起会让
/// 一次处罚写入拖住整条裁决链。**禁止**在任何业务通路里直接读处罚存储做判定，
/// 一律经裁决服务（否则又是「三通路各自实现」的老问题）。
/// </para>
/// <para>
/// 维护约束（当前连接）：施加处罚会发出 <see cref="OnlineSocialEvents.PunishmentApplied"/> 事件，
/// 运行时 / 网关消费该事件处置被处罚玩家的当前连接（踢线）。本服务**不直接断连**——
/// 连接归运行时，服务端社交域不该持有会话句柄。
/// </para>
/// <para>
/// 维护约束（增删改不可逆性）：撤销是**原位标记**（保留记录、写撤销人与时刻），不是删除；
/// 处罚原因必填，无原因的封禁不可复查，等于把申诉通道关掉。
/// </para>
/// </summary>
public sealed class OnlinePunishmentService
{
    /// <summary>社交关系图谱存储（处罚记录落点）。</summary>
    private readonly IOnlineSocialGraphStore _graphStore;

    /// <summary>事件发布出口。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>
    /// 初始化 <see cref="OnlinePunishmentService"/>。
    /// </summary>
    /// <param name="graphStore">社交关系图谱存储。</param>
    /// <param name="eventPublisher">事件发布出口。</param>
    public OnlinePunishmentService(IOnlineSocialGraphStore graphStore, IOnlineEventPublisher eventPublisher)
    {
        _graphStore = graphStore ?? throw new ArgumentNullException(nameof(graphStore));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    /// <summary>
    /// 施加处罚（禁言 / 封禁）。
    /// </summary>
    /// <param name="request">处罚施加请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效的处罚记录。</returns>
    public async Task<OnlineResult<OnlinePunishment>> ApplyAsync(OnlinePunishmentRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var tenantId = request.TenantId;
        var appId = request.AppId;
        var playerId = request.PlayerId;
        var kind = request.Kind;
        var reason = request.Reason;
        var effectiveAtTime = request.EffectiveAtTime;
        var expiresAtTime = request.ExpiresAtTime;
        var adminId = request.AdminId;
        var adminCaseId = request.AdminCaseId;
        var correlationId = request.CorrelationId;
        if (tenantId <= 0 || appId <= 0 || playerId <= 0)
        {
            return OnlineResult<OnlinePunishment>.Fail(OnlineErrorCode.ParameterInvalid, "处罚定位参数无效");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return OnlineResult<OnlinePunishment>.Fail(OnlineErrorCode.ParameterInvalid, "处罚原因不能为空");
        }

        if (adminId <= 0)
        {
            return OnlineResult<OnlinePunishment>.Fail(OnlineErrorCode.ParameterInvalid, "缺少施加处罚的管理员标识");
        }

        var now = Now();
        var effective = effectiveAtTime > 0 ? effectiveAtTime : now;
        if (expiresAtTime > 0 && expiresAtTime <= effective)
        {
            // 结束早于开始 = 处罚自始无效，写库只会污染裁决路径。
            return OnlineResult<OnlinePunishment>.Fail(OnlineErrorCode.ParameterInvalid, "处罚失效时刻必须晚于生效时刻");
        }

        var prototype = new OnlinePunishment
        {
            PunishmentId = "pun-" + Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            AppId = appId,
            PlayerId = playerId,
            Kind = kind,
            Reason = reason.Trim(),
            EffectiveAtTime = effective,
            ExpiresAtTime = expiresAtTime,
            Revoked = false,
            RevokedAtTime = 0,
            RevokedByAdminId = 0,
            CreatedByAdminId = adminId,
            AdminCaseId = adminCaseId ?? string.Empty,
            CreatedAtTime = now,
            UpdatedAtTime = now,
            Revision = 0,
        };

        var stored = await _graphStore.SavePunishmentIfAbsentAsync(prototype, cancellationToken).ConfigureAwait(false);
        if (string.Equals(stored.PunishmentId, prototype.PunishmentId, StringComparison.Ordinal))
        {
            await _eventPublisher.PublishAsync(OnlineSocialEvents.CreatePunishmentChanged(stored, OnlineSocialEvents.PunishmentApplied, correlationId), cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<OnlinePunishment>.Ok(stored);
    }

    /// <summary>
    /// 撤销处罚（重复撤销幂等成功）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="punishmentId">处罚标识。</param>
    /// <param name="adminId">执行撤销的 Admin 标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>撤销后的处罚记录。</returns>
    public async Task<OnlineResult<OnlinePunishment>> RevokeAsync(long tenantId, long appId, string punishmentId, long adminId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0 || appId <= 0 || string.IsNullOrEmpty(punishmentId))
        {
            return OnlineResult<OnlinePunishment>.Fail(OnlineErrorCode.ParameterInvalid, "处罚定位参数无效");
        }

        if (adminId <= 0)
        {
            return OnlineResult<OnlinePunishment>.Fail(OnlineErrorCode.ParameterInvalid, "缺少执行撤销的管理员标识");
        }

        var punishment = await _graphStore.FindPunishmentAsync(tenantId, appId, punishmentId, cancellationToken).ConfigureAwait(false);
        if (punishment == null)
        {
            return OnlineResult<OnlinePunishment>.Fail(OnlineErrorCode.ResourceNotFound, "处罚记录不存在");
        }

        if (punishment.Revoked)
        {
            // 幂等：已撤销返回既有快照。
            return OnlineResult<OnlinePunishment>.Ok(punishment);
        }

        var now = Now();
        var updated = punishment.Copy();
        updated.Revoked = true;
        updated.RevokedAtTime = now;
        updated.RevokedByAdminId = adminId;
        updated.UpdatedAtTime = now;

        var effective = await _graphStore.ReplacePunishmentAsync(updated, punishment.Revision, cancellationToken).ConfigureAwait(false);
        if (effective == null)
        {
            // CAS 失败 = 已被并发撤销 / 改写；重读后按当前事实返回，保证收敛。
            var current = await _graphStore.FindPunishmentAsync(tenantId, appId, punishmentId, cancellationToken).ConfigureAwait(false);
            if (current == null)
            {
                return OnlineResult<OnlinePunishment>.Fail(OnlineErrorCode.ResourceNotFound, "处罚记录不存在");
            }

            return OnlineResult<OnlinePunishment>.Ok(current);
        }

        await _eventPublisher.PublishAsync(OnlineSocialEvents.CreatePunishmentChanged(effective, OnlineSocialEvents.PunishmentRevoked, correlationId), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlinePunishment>.Ok(effective);
    }

    /// <summary>
    /// 查询某玩家在给定时刻**确实生效**的处罚（Admin 复查与申诉核对的输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">被处罚玩家。</param>
    /// <param name="nowUnixMilliseconds">判定时刻（UTC 毫秒；<c>0</c> 取系统时钟）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效中的处罚列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlinePunishment>>> ListActiveAsync(long tenantId, long appId, long playerId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0 || appId <= 0 || playerId <= 0)
        {
            // 与 ApplyAsync / RevokeAsync 同口径：定位参数先校验，读接口不留免检口子。
            return OnlineResult<IReadOnlyList<OnlinePunishment>>.Fail(OnlineErrorCode.ParameterInvalid, "处罚定位参数无效");
        }

        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : Now();
        var active = await _graphStore.ListActivePunishmentsAsync(tenantId, appId, new ActivePunishmentQuery { PlayerId = playerId, NowUnixMilliseconds = now }, cancellationToken).ConfigureAwait(false);
        return OnlineResult<IReadOnlyList<OnlinePunishment>>.Ok(active);
    }

    /// <summary>
    /// 查询某玩家的全部处罚历史（含已撤销与已失效）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">被处罚玩家。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>处罚记录列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlinePunishment>>> ListHistoryAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0 || appId <= 0 || playerId <= 0)
        {
            // 与 ApplyAsync / RevokeAsync 同口径：定位参数先校验，读接口不留免检口子。
            return OnlineResult<IReadOnlyList<OnlinePunishment>>.Fail(OnlineErrorCode.ParameterInvalid, "处罚定位参数无效");
        }

        var all = await _graphStore.ListPunishmentsByPlayerAsync(tenantId, appId, playerId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<IReadOnlyList<OnlinePunishment>>.Ok(all);
    }

    /// <summary>取当前 UTC 毫秒时刻。</summary>
    /// <returns>UTC 毫秒时间戳。</returns>
    private static long Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
