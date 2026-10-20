//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and other related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws and regulations of the People's Republic of China and international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file at the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations.

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Abstractions.Bridges;

/// <summary>
/// 会话域桥租约（C204，沿 C246 <see cref="OnlineMatchDomainBridgeLease"/> 形态）：组合根发布给进程内
/// 消费方（Hotfix 薄适配器）的吊销式访问句柄——持有会话恢复契约面与授权作用域三元组，
/// 撤销后任何旧引用调用一律显式 <see cref="OnlineErrorCode.DependencyUnavailable"/> 失败。
/// <para>
/// 维护约束：撤销是一次性闸门（幂等）；每次调用前复查活跃位——宿主停止 / 重启 / 重新发布后，
/// 先前捕获的 lease 引用不得继续读写旧会话数据；作用域三元组来自组合根授权上下文，
/// 玩家位由调用方按请求主体填充，lease 不缓存任何请求态。
/// </para>
/// </summary>
public sealed class OnlineSessionDomainBridgeLease : IOnlineSessionReconnectContract
{
    /// <summary>契约面（撤销后不再可达）。</summary>
    private readonly IOnlineSessionReconnectContract contract;

    /// <summary>活跃位（volatile：撤销后旧引用立即显式失败）。</summary>
    private volatile bool active = true;

    /// <summary>
    /// 初始化 <see cref="OnlineSessionDomainBridgeLease"/>。
    /// </summary>
    /// <param name="contract">会话恢复契约面。</param>
    /// <param name="authorizedScope">授权作用域（三元组；玩家位为 0）。</param>
    internal OnlineSessionDomainBridgeLease(IOnlineSessionReconnectContract contract, OnlineScope authorizedScope)
    {
        this.contract = contract ?? throw new ArgumentNullException(nameof(contract));
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
    /// 撤销租约（幂等；由 <see cref="OnlineSessionDomainBridgeHolder.Retract"/> 驱动）。
    /// </summary>
    internal void Deactivate()
    {
        active = false;
    }

    /// <summary>
    /// 经租约登录签发：活跃位复查后委托契约面；已撤销返回显式失败。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="connectionId">当前物理连接标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>签发结果。</returns>
    public Task<OnlineResult<OnlineSessionLoginCredential>> IssueForLoginAsync(OnlineScope scope, string connectionId, CancellationToken cancellationToken = default)
    {
        if (!active)
        {
            return Task.FromResult(OnlineResult<OnlineSessionLoginCredential>.Fail(
                OnlineErrorCode.DependencyUnavailable, "会话域桥已撤销（宿主未运行或已停止）"));
        }

        return contract.IssueForLoginAsync(scope, connectionId, cancellationToken);
    }

    /// <summary>
    /// 经租约恢复：活跃位复查后委托契约面；已撤销返回显式失败。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="token">Token 明文。</param>
    /// <param name="connectionId">新物理连接标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>恢复结果。</returns>
    public Task<OnlineResult<OnlineSessionResumeCredential>> ResumeAsync(OnlineScope scope, string token, string connectionId, CancellationToken cancellationToken = default)
    {
        if (!active)
        {
            return Task.FromResult(OnlineResult<OnlineSessionResumeCredential>.Fail(
                OnlineErrorCode.DependencyUnavailable, "会话域桥已撤销（宿主未运行或已停止）"));
        }

        return contract.ResumeAsync(scope, token, connectionId, cancellationToken);
    }

    /// <summary>
    /// 经租约断线通知：活跃位复查后委托契约面；已撤销返回显式失败。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="reconnectWindowSeconds">重连窗口（秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public Task<OnlineResult<bool>> NotifyPlayerDisconnectedAsync(OnlineScope scope, long reconnectWindowSeconds, CancellationToken cancellationToken = default)
    {
        if (!active)
        {
            return Task.FromResult(OnlineResult<bool>.Fail(
                OnlineErrorCode.DependencyUnavailable, "会话域桥已撤销（宿主未运行或已停止）"));
        }

        return contract.NotifyPlayerDisconnectedAsync(scope, reconnectWindowSeconds, cancellationToken);
    }
}
