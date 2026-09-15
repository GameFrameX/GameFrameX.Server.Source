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

namespace GameFrameX.Online.Presence;

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;

/// <summary>
/// 在线状态服务（vault:C3 S2.5：Presence 事实源的唯一写者——所有状态写入必须经本服务并过状态机判定）。
/// <para>
/// 维护约束（红线）：玩家主体位必须有效（PlayerId &lt;= 0 拒绝——Admin 管理员连接结构性不进入 Presence，X6/VC-2.6）；
/// 状态变更唯一合法性判据为 <see cref="OnlinePresenceStateMachine.TryTransition"/>，表外转换映射
/// <see cref="OnlineErrorCode.StateOperationForbidden"/>；每次变更必带原因码并发布
/// <see cref="OnlinePresenceEvents.PresenceChanged"/> 事件（VC-2.16）；无永久 Reconnecting——超窗清理
/// 经 <see cref="SweepTimeoutsAsync"/>（VC-2.15）；Offline = 无记录（下线即移除）。
/// </para>
/// </summary>
public sealed class OnlinePresenceService
{
    /// <summary>在线状态存储。</summary>
    private readonly IOnlinePresenceStore _store;

    /// <summary>事件发布出口。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>空闲判定阈值（秒；Online 态超过该时长无心跳转 Idle）。</summary>
    private readonly long _idleTimeoutSeconds;

    /// <summary>
    /// 初始化 <see cref="OnlinePresenceService"/>。
    /// </summary>
    /// <param name="store">在线状态存储。</param>
    /// <param name="eventPublisher">事件发布出口。</param>
    /// <param name="idleTimeoutSeconds">空闲判定阈值（秒；默认 300）。</param>
    public OnlinePresenceService(IOnlinePresenceStore store, IOnlineEventPublisher eventPublisher, long idleTimeoutSeconds = 300)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _idleTimeoutSeconds = idleTimeoutSeconds;
    }

    /// <summary>
    /// 上线登记（Offline → Online；同会话重复登记幂等刷新心跳，新会话直接接管）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="sessionId">关联会话标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登记后的在线状态。</returns>
    public async Task<OnlineResult<OnlinePresenceState>> SetOnlineAsync(OnlineScope scope, string sessionId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var now = Now();
        var existing = await _store.FindAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        if (existing != null && existing.State == OnlinePresenceState.Online && existing.SessionId == sessionId)
        {
            existing.LastHeartbeatAtTime = now;
            await _store.SetAsync(existing, cancellationToken);
            return OnlineResult<OnlinePresenceState>.Ok(OnlinePresenceState.Online);
        }

        var fromState = existing != null ? existing.State : OnlinePresenceState.Offline;
        if (!OnlinePresenceStateMachine.TryTransition(fromState, OnlinePresenceState.Online))
        {
            return OnlineResult<OnlinePresenceState>.Fail(OnlineErrorCode.StateOperationForbidden, "当前在线状态不允许直接上线：" + fromState);
        }

        var record = new OnlinePresenceRecord
        {
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            ServerId = scope.ServerId,
            PlayerId = scope.PlayerId,
            SessionId = sessionId ?? string.Empty,
            State = OnlinePresenceState.Online,
            ChangeReason = OnlinePresenceChangeReason.Connected,
            ChangedAtTime = now,
            LastHeartbeatAtTime = now,
            ReconnectDeadlineAtTime = 0,
        };
        await _store.SetAsync(record, cancellationToken);
        await PublishChangeAsync(record, fromState, OnlinePresenceState.Online, OnlinePresenceChangeReason.Connected, correlationId, cancellationToken);
        return OnlineResult<OnlinePresenceState>.Ok(OnlinePresenceState.Online);
    }

    /// <summary>
    /// 心跳（刷新最近活跃时刻；Idle 态心跳视为操作恢复——Idle → Online）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>心跳后的在线状态。</returns>
    public async Task<OnlineResult<OnlinePresenceState>> HeartbeatAsync(OnlineScope scope, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var record = await _store.FindAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        if (record == null)
        {
            return OnlineResult<OnlinePresenceState>.Fail(OnlineErrorCode.StateNotReady, "玩家不在线，无心跳语义");
        }

        var now = Now();
        record.LastHeartbeatAtTime = now;
        if (record.State == OnlinePresenceState.Idle)
        {
            return await TransitionAsync(record, OnlinePresenceState.Online, OnlinePresenceChangeReason.ActivityResumed, correlationId, cancellationToken);
        }

        await _store.SetAsync(record, cancellationToken);
        return OnlineResult<OnlinePresenceState>.Ok(record.State);
    }

    /// <summary>
    /// 断线登记（活跃态 → Reconnecting，开启重连窗口；窗口内重复断线刷新窗口）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="reconnectWindowSeconds">重连窗口（秒）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登记后的在线状态。</returns>
    public async Task<OnlineResult<OnlinePresenceState>> MarkDisconnectedAsync(OnlineScope scope, long reconnectWindowSeconds, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        if (reconnectWindowSeconds <= 0)
        {
            return OnlineResult<OnlinePresenceState>.Fail(OnlineErrorCode.ParameterInvalid, "重连窗口必须为正数（秒）");
        }

        var record = await _store.FindAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        if (record == null)
        {
            return OnlineResult<OnlinePresenceState>.Fail(OnlineErrorCode.StateNotReady, "玩家不在线，无断线语义");
        }

        var now = Now();
        if (record.State == OnlinePresenceState.Reconnecting)
        {
            record.ReconnectDeadlineAtTime = now + reconnectWindowSeconds * 1000L;
            await _store.SetAsync(record, cancellationToken);
            return OnlineResult<OnlinePresenceState>.Ok(record.State);
        }

        return await TransitionAsync(record, OnlinePresenceState.Reconnecting, OnlinePresenceChangeReason.Disconnected, correlationId, cancellationToken, now + reconnectWindowSeconds * 1000L);
    }

    /// <summary>
    /// 重连成功（Reconnecting → Online 或 InMatch；对局中断线重连由阶段 5 以 InMatch 目标调用）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="targetState">重连落点（仅允许 Online / InMatch）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重连后的在线状态。</returns>
    public async Task<OnlineResult<OnlinePresenceState>> MarkReconnectedAsync(OnlineScope scope, OnlinePresenceState targetState, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        if (targetState != OnlinePresenceState.Online && targetState != OnlinePresenceState.InMatch)
        {
            return OnlineResult<OnlinePresenceState>.Fail(OnlineErrorCode.ParameterInvalid, "重连落点仅允许 Online / InMatch");
        }

        var record = await _store.FindAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        if (record == null)
        {
            return OnlineResult<OnlinePresenceState>.Fail(OnlineErrorCode.StateNotReady, "玩家不在线，无重连语义");
        }

        var now = Now();
        record.LastHeartbeatAtTime = now;
        return await TransitionAsync(record, targetState, OnlinePresenceChangeReason.ReconnectSucceeded, correlationId, cancellationToken);
    }

    /// <summary>
    /// 会话关闭下线（活跃态移除记录即 Offline；无记录幂等成功；Blocked 不受会话关闭影响）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<OnlineResult<bool>> MarkClosedAsync(OnlineScope scope, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return OnlineResult<bool>.Fail(failure.Code, failure.Message);
        }

        var record = await _store.FindAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        if (record == null)
        {
            return OnlineResult<bool>.Ok(true);
        }

        if (record.State == OnlinePresenceState.Blocked)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.StateOperationForbidden, "风控限制中的玩家不受会话关闭影响，须先解除限制");
        }

        await _store.RemoveAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        await PublishChangeAsync(record, record.State, OnlinePresenceState.Offline, OnlinePresenceChangeReason.Closed, correlationId, cancellationToken);
        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 下游玩法驱动的状态迁移（Matching/InParty/InMatch 相关转换；阶段 4/5 调用，原因码 StateDriven）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="targetState">目标状态。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>迁移后的在线状态。</returns>
    public async Task<OnlineResult<OnlinePresenceState>> SetActivityStateAsync(OnlineScope scope, OnlinePresenceState targetState, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var record = await _store.FindAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        if (record == null)
        {
            return OnlineResult<OnlinePresenceState>.Fail(OnlineErrorCode.StateNotReady, "玩家不在线，须先上线登记");
        }

        return await TransitionAsync(record, targetState, OnlinePresenceChangeReason.StateDriven, correlationId, cancellationToken);
    }

    /// <summary>
    /// 风控限制（任意在线态 → Blocked；离线玩家可直接落 Blocked 记录）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<OnlineResult<bool>> MarkBlockedAsync(OnlineScope scope, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return OnlineResult<bool>.Fail(failure.Code, failure.Message);
        }

        var now = Now();
        var record = await _store.FindAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        if (record == null)
        {
            var blockedRecord = new OnlinePresenceRecord
            {
                TenantId = scope.TenantId,
                AppId = scope.AppId,
                ServerId = scope.ServerId,
                PlayerId = scope.PlayerId,
                SessionId = string.Empty,
                State = OnlinePresenceState.Blocked,
                ChangeReason = OnlinePresenceChangeReason.RiskBlocked,
                ChangedAtTime = now,
                LastHeartbeatAtTime = now,
                ReconnectDeadlineAtTime = 0,
            };
            await _store.SetAsync(blockedRecord, cancellationToken);
            await PublishChangeAsync(blockedRecord, OnlinePresenceState.Offline, OnlinePresenceState.Blocked, OnlinePresenceChangeReason.RiskBlocked, correlationId, cancellationToken);
            return OnlineResult<bool>.Ok(true);
        }

        var outcome = await TransitionAsync(record, OnlinePresenceState.Blocked, OnlinePresenceChangeReason.RiskBlocked, correlationId, cancellationToken);
        return outcome.IsSuccess ? OnlineResult<bool>.Ok(true) : OnlineResult<bool>.Fail(outcome.Code, outcome.Message);
    }

    /// <summary>
    /// 解除风控限制（Blocked → Offline；无记录或非 Blocked 中的无记录视为幂等成功）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<OnlineResult<bool>> ReleaseBlockedAsync(OnlineScope scope, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return OnlineResult<bool>.Fail(failure.Code, failure.Message);
        }

        var record = await _store.FindAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        if (record == null)
        {
            return OnlineResult<bool>.Ok(true);
        }

        if (record.State != OnlinePresenceState.Blocked)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.StateOperationForbidden, "玩家未处于风控限制中");
        }

        await _store.RemoveAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        await PublishChangeAsync(record, record.State, OnlinePresenceState.Offline, OnlinePresenceChangeReason.RiskReleased, correlationId, cancellationToken);
        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 清理超时（VC-2.15：Reconnecting 超窗转 Offline 且不留永久 Reconnecting；Online 超过空闲阈值转 Idle）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="nowUnixMilliseconds">判定基准时刻（Unix 毫秒；0 = 当前时刻，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次清理的记录数。</returns>
    public async Task<int> SweepTimeoutsAsync(long tenantId, long appId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : Now();
        var swept = 0;
        var records = await _store.ListByAppAsync(tenantId, appId, cancellationToken);
        foreach (var record in records)
        {
            if (record.State == OnlinePresenceState.Reconnecting && record.ReconnectDeadlineAtTime > 0 && now > record.ReconnectDeadlineAtTime)
            {
                await _store.RemoveAsync(record.TenantId, record.AppId, record.PlayerId, cancellationToken);
                await PublishChangeAsync(record, record.State, OnlinePresenceState.Offline, OnlinePresenceChangeReason.ReconnectWindowExpired, null, cancellationToken);
                swept++;
                continue;
            }

            if (record.State == OnlinePresenceState.Online && now - record.LastHeartbeatAtTime > _idleTimeoutSeconds * 1000L)
            {
                var outcome = await TransitionAsync(record, OnlinePresenceState.Idle, OnlinePresenceChangeReason.IdleTimeout, null, cancellationToken);
                if (outcome.IsSuccess)
                {
                    swept++;
                }
            }
        }

        return swept;
    }

    /// <summary>
    /// 查询玩家在线状态（无记录 = Offline）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>在线状态。</returns>
    public async Task<OnlineResult<OnlinePresenceState>> GetAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var record = await _store.FindAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        return OnlineResult<OnlinePresenceState>.Ok(record != null ? record.State : OnlinePresenceState.Offline);
    }

    /// <summary>
    /// 统计 (TenantId, AppId) 在线人数（Blocked 不计入在线；Admin 查询口径）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>在线人数（不含 Blocked）。</returns>
    public async Task<int> CountOnlineAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        var records = await _store.ListByAppAsync(tenantId, appId, cancellationToken);
        var count = 0;
        foreach (var record in records)
        {
            if (record.State != OnlinePresenceState.Blocked)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 列出 (TenantId, AppId) 全部在线状态记录（Admin 查询输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>在线状态记录列表。</returns>
    public Task<IReadOnlyList<OnlinePresenceRecord>> ListByAppAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        return _store.ListByAppAsync(tenantId, appId, cancellationToken);
    }

    /// <summary>
    /// 执行状态机判定的状态迁移（表外转换拒绝；成功落库并发布变更事件）。
    /// </summary>
    /// <param name="record">目标记录。</param>
    /// <param name="targetState">目标状态。</param>
    /// <param name="reason">变更原因码。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="reconnectDeadlineAtTime">重连窗口截止时刻（仅 Disconnected 迁移传入；其余忽略）。</param>
    /// <returns>迁移后的在线状态。</returns>
    private async Task<OnlineResult<OnlinePresenceState>> TransitionAsync(OnlinePresenceRecord record, OnlinePresenceState targetState, OnlinePresenceChangeReason reason, string correlationId, CancellationToken cancellationToken, long reconnectDeadlineAtTime = 0)
    {
        var fromState = record.State;
        if (fromState == targetState)
        {
            return OnlineResult<OnlinePresenceState>.Fail(OnlineErrorCode.StateOperationForbidden, "目标状态与当前状态相同：" + targetState);
        }

        if (!OnlinePresenceStateMachine.TryTransition(fromState, targetState))
        {
            return OnlineResult<OnlinePresenceState>.Fail(OnlineErrorCode.StateOperationForbidden, "非法状态转换：" + fromState + " → " + targetState);
        }

        record.State = targetState;
        record.ChangeReason = reason;
        record.ChangedAtTime = Now();
        if (reason == OnlinePresenceChangeReason.Disconnected)
        {
            record.ReconnectDeadlineAtTime = reconnectDeadlineAtTime;
        }
        else if (targetState != OnlinePresenceState.Reconnecting)
        {
            record.ReconnectDeadlineAtTime = 0;
        }

        if (targetState != OnlinePresenceState.Reconnecting)
        {
            record.LastHeartbeatAtTime = record.ChangedAtTime;
        }

        await _store.SetAsync(record, cancellationToken);
        await PublishChangeAsync(record, fromState, targetState, reason, correlationId, cancellationToken);
        return OnlineResult<OnlinePresenceState>.Ok(targetState);
    }

    /// <summary>
    /// 发布在线状态变更事件。
    /// </summary>
    /// <param name="record">目标记录（透出作用域与会话归属）。</param>
    /// <param name="fromState">源状态。</param>
    /// <param name="toState">目标状态。</param>
    /// <param name="reason">变更原因码。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private Task PublishChangeAsync(OnlinePresenceRecord record, OnlinePresenceState fromState, OnlinePresenceState toState, OnlinePresenceChangeReason reason, string correlationId, CancellationToken cancellationToken)
    {
        var changeEvent = OnlinePresenceEvents.Create(record.TenantId, record.AppId, record.ServerId, record.PlayerId, record.SessionId, fromState, toState, reason.ToString(), correlationId);
        return _eventPublisher.PublishAsync(changeEvent, cancellationToken);
    }

    /// <summary>
    /// 校验作用域含有效玩家主体位。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <returns>失败结果；合法返回 null。</returns>
    private static OnlineResult<OnlinePresenceState> ValidateScope(OnlineScope scope)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (scope.PlayerId <= 0)
        {
            return OnlineResult<OnlinePresenceState>.Fail(OnlineErrorCode.ParameterInvalid, "在线状态仅面向玩家会话（PlayerId 必须有效）");
        }

        return null;
    }

    /// <summary>
    /// 获取当前时刻（Unix 毫秒）。
    /// </summary>
    /// <returns>当前时刻。</returns>
    private static long Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
