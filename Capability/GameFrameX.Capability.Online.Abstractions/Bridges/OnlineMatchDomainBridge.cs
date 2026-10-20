//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes and liabilities arising from secondary development,
//   本组织不承担任何责任，开发者须自行承担。
//   The organization shall not be responsible for any disputes; developers shall bear the responsibility.
//  ==========================================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Abstractions.Bridges;

/// <summary>
/// 匹配域桥租约（C246，沿 C244 好友域桥 / C245 对局桥形态）：组合根发布给进程内消费方
/// （Hotfix 薄适配器）的吊销式访问句柄——持有域契约面与授权作用域三元组，撤销后任何旧引用调用
/// 一律显式 <see cref="OnlineErrorCode.DependencyUnavailable"/> 失败。
/// <para>
/// 维护约束：撤销是一次性闸门（<see cref="Deactivate"/> 幂等）；每次调用前复查活跃位——宿主停止 /
/// 重启 / 重新发布后，先前捕获的 lease 引用不得继续读写旧匹配数据；作用域三元组来自组合根授权上下文，
/// 玩家位由调用方按请求主体填充，lease 不缓存任何请求态。
/// </para>
/// </summary>
public sealed class OnlineMatchDomainBridgeLease : IOnlineMatchDomain
{
    /// <summary>契约面（撤销后不再可达）。</summary>
    private readonly IOnlineMatchDomain domain;

    /// <summary>活跃位（volatile：撤销后旧引用立即显式失败）。</summary>
    private volatile bool active = true;

    /// <summary>
    /// 初始化 <see cref="OnlineMatchDomainBridgeLease"/>。
    /// </summary>
    /// <param name="domain">域契约面。</param>
    /// <param name="authorizedScope">授权作用域（三元组；玩家位为 0）。</param>
    internal OnlineMatchDomainBridgeLease(IOnlineMatchDomain domain, OnlineScope authorizedScope)
    {
        this.domain = domain ?? throw new ArgumentNullException(nameof(domain));
        AuthorizedScope = authorizedScope ?? throw new ArgumentNullException(nameof(authorizedScope));
    }

    /// <summary>
    /// 获取授权作用域三元组（租户 / App / 区服；玩家位为 0，由调用方按请求主体填充）。
    /// </summary>
    public OnlineScope AuthorizedScope { get; }

    /// <summary>
    /// 获取租约是否仍然活跃（撤销后为 <c>false</c>；活跃位不保证后续调用成功，仅保证撤销可见）。
    /// </summary>
    public bool IsActive
    {
        get { return active; }
    }

    /// <summary>
    /// 撤销租约（幂等；由 <see cref="OnlineMatchDomainBridgeHolder.Retract"/> 驱动）。
    /// </summary>
    internal void Deactivate()
    {
        active = false;
    }

    /// <summary>
    /// 经租约入队：活跃位复查后委托契约面；已撤销返回显式 <see cref="OnlineErrorCode.DependencyUnavailable"/> 失败。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="mode">匹配模式。</param>
    /// <param name="rating">玩家评分。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>入队回执。</returns>
    public Task<OnlineResult<OnlineMatchDomainEnqueue>> EnqueueAsync(OnlineScope scope, int mode, int rating, CancellationToken cancellationToken = default)
    {
        if (!active)
        {
            return Task.FromResult(OnlineResult<OnlineMatchDomainEnqueue>.Fail(
                OnlineErrorCode.DependencyUnavailable, "匹配域桥已撤销（宿主未运行或已停止）"));
        }

        return domain.EnqueueAsync(scope, mode, rating, cancellationToken);
    }

    /// <summary>
    /// 经租约取消：活跃位复查后委托契约面；已撤销返回显式失败。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>取消回执。</returns>
    public Task<OnlineResult<OnlineMatchDomainCancel>> CancelAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        if (!active)
        {
            return Task.FromResult(OnlineResult<OnlineMatchDomainCancel>.Fail(
                OnlineErrorCode.DependencyUnavailable, "匹配域桥已撤销（宿主未运行或已停止）"));
        }

        return domain.CancelAsync(scope, cancellationToken);
    }

    /// <summary>
    /// 经租约查询状态：活跃位复查后委托契约面；已撤销返回显式失败。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>状态回执。</returns>
    public Task<OnlineResult<OnlineMatchDomainStatus>> GetStatusAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        if (!active)
        {
            return Task.FromResult(OnlineResult<OnlineMatchDomainStatus>.Fail(
                OnlineErrorCode.DependencyUnavailable, "匹配域桥已撤销（宿主未运行或已停止）"));
        }

        return domain.GetStatusAsync(scope, cancellationToken);
    }

    /// <summary>
    /// 经租约入场：活跃位复查后委托契约面；已撤销返回显式失败。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="matchId">协议数值对局标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>入场回执。</returns>
    public Task<OnlineResult<OnlineMatchDomainEntry>> EnterAsync(OnlineScope scope, long matchId, CancellationToken cancellationToken = default)
    {
        if (!active)
        {
            return Task.FromResult(OnlineResult<OnlineMatchDomainEntry>.Fail(
                OnlineErrorCode.DependencyUnavailable, "匹配域桥已撤销（宿主未运行或已停止）"));
        }

        return domain.EnterAsync(scope, matchId, cancellationToken);
    }

    /// <summary>
    /// 经租约解析分配：活跃位复查后委托契约面；已撤销返回显式失败。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="assignmentId">分配标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分配解析视图。</returns>
    public Task<OnlineResult<OnlineMatchDomainAssignment>> ResolveAssignmentAsync(long tenantId, long appId, string assignmentId, CancellationToken cancellationToken = default)
    {
        if (!active)
        {
            return Task.FromResult(OnlineResult<OnlineMatchDomainAssignment>.Fail(
                OnlineErrorCode.DependencyUnavailable, "匹配域桥已撤销（宿主未运行或已停止）"));
        }

        return domain.ResolveAssignmentAsync(tenantId, appId, assignmentId, cancellationToken);
    }

    /// <summary>
    /// 经租约会话中介断线通知：活跃位复查后委托契约面；已撤销返回显式失败。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>断线标记回执。</returns>
    public Task<OnlineResult<OnlineMatchDomainDisconnect>> NotifyPlayerDisconnectedAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        if (!active)
        {
            return Task.FromResult(OnlineResult<OnlineMatchDomainDisconnect>.Fail(
                OnlineErrorCode.DependencyUnavailable, "匹配域桥已撤销（宿主未运行或已停止）"));
        }

        return domain.NotifyPlayerDisconnectedAsync(scope, cancellationToken);
    }

    /// <summary>
    /// 经租约会话中介恢复：活跃位复查后委托契约面；已撤销返回显式失败。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="lastAcknowledgedSequence">客户端最后确认的服务器序号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>恢复回执。</returns>
    public Task<OnlineResult<OnlineMatchDomainResume>> TryResumeForPlayerAsync(OnlineScope scope, long lastAcknowledgedSequence, CancellationToken cancellationToken = default)
    {
        if (!active)
        {
            return Task.FromResult(OnlineResult<OnlineMatchDomainResume>.Fail(
                OnlineErrorCode.DependencyUnavailable, "匹配域桥已撤销（宿主未运行或已停止）"));
        }

        return domain.TryResumeForPlayerAsync(scope, lastAcknowledgedSequence, cancellationToken);
    }
}
