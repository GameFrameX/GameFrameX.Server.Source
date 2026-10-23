// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers shall bear the responsibility themselves.
//  ==========================================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Abstractions.Bridges;

/// <summary>
/// 对局样例桥租约：组合根发布给进程内
/// 消费方（Hotfix 薄适配器）的吊销式访问句柄——持有契约面与授权作用域三元组，撤销后任何旧引用
/// 调用一律显式 <see cref="OnlineErrorCode.DependencyUnavailable"/> 失败。
/// <para>
/// 维护约束：撤销是一次性闸门（<see cref="Deactivate"/> 幂等）；每次调用前复查活跃位——宿主停止 /
/// 重启 / 重新发布后，先前捕获的 lease 引用不得继续读写旧对局数据；作用域三元组来自组合根授权
/// 上下文，玩家位由调用方按请求主体填充，lease 不缓存任何请求态。
/// </para>
/// </summary>
/// <remarks>
/// The sample match bridge lease:
/// the revocable access handle published by the composition root for in-process consumers (the Hotfix
/// thin adapter) — it carries the contract face and the authorized scope triple, and once revoked every
/// call through a stale reference fails explicitly with <see cref="OnlineErrorCode.DependencyUnavailable"/>.
/// Revocation is a one-way idempotent gate re-checked before each call, so references captured before a
/// host stop / restart / republish can never keep reading or writing stale match data; the scope triple
/// comes from the composition root's authorized context and the player position is filled per request by
/// the caller — the lease itself caches no request state.
/// </remarks>
public sealed class OnlineMatchGameBridgeLease : IOnlineMatchGameBridge
{
    /// <summary>契约面（撤销后不再可达）。</summary>
    private readonly IOnlineMatchGameBridge bridge;

    /// <summary>活跃位（volatile：撤销后旧引用立即显式失败）。</summary>
    private volatile bool active = true;

    /// <summary>
    /// 初始化 <see cref="OnlineMatchGameBridgeLease"/>。
    /// </summary>
    /// <param name="bridge">契约面。</param>
    /// <param name="authorizedScope">授权作用域（三元组；玩家位为 0）。</param>
    internal OnlineMatchGameBridgeLease(IOnlineMatchGameBridge bridge, OnlineScope authorizedScope)
    {
        this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
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
    /// 撤销租约（幂等；由 <see cref="OnlineMatchGameBridgeHolder.Retract"/> 驱动）。
    /// </summary>
    internal void Deactivate()
    {
        active = false;
    }

    /// <summary>
    /// 经租约开启一局平台对局：活跃位复查后委托契约面；已撤销返回显式
    /// <see cref="OnlineErrorCode.DependencyUnavailable"/> 失败（不回旧数据、不抛未分类异常）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="roomId">房间标识。</param>
    /// <param name="round">局号。</param>
    /// <param name="playerIds">本局玩家集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>开局后的对局视图。</returns>
    public Task<OnlineResult<OnlineMatchBridgeGameView>> StartRoomGameAsync(OnlineScope scope, long roomId, int round, IReadOnlyList<long> playerIds, CancellationToken cancellationToken = default)
    {
        if (!active)
        {
            return Task.FromResult(OnlineResult<OnlineMatchBridgeGameView>.Fail(
                OnlineErrorCode.DependencyUnavailable, "对局桥已撤销（宿主未运行或已停止）"));
        }

        return bridge.StartRoomGameAsync(scope, roomId, round, playerIds, cancellationToken);
    }

    /// <summary>
    /// 经租约提交一次出拳意图：活跃位复查后委托契约面；已撤销返回显式
    /// <see cref="OnlineErrorCode.DependencyUnavailable"/> 失败。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="roomId">房间标识。</param>
    /// <param name="round">局号。</param>
    /// <param name="gesture">出拳值（1 = 石头，2 = 剪刀，3 = 布）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提交后的对局视图。</returns>
    public Task<OnlineResult<OnlineMatchBridgeGameView>> SubmitGestureAsync(OnlineScope scope, long roomId, int round, int gesture, CancellationToken cancellationToken = default)
    {
        if (!active)
        {
            return Task.FromResult(OnlineResult<OnlineMatchBridgeGameView>.Fail(
                OnlineErrorCode.DependencyUnavailable, "对局桥已撤销（宿主未运行或已停止）"));
        }

        return bridge.SubmitGestureAsync(scope, roomId, round, gesture, cancellationToken);
    }

    /// <summary>
    /// 经租约查询对局视图：活跃位复查后委托契约面；已撤销返回显式
    /// <see cref="OnlineErrorCode.DependencyUnavailable"/> 失败。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="roomId">房间标识。</param>
    /// <param name="round">局号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对局视图。</returns>
    public Task<OnlineResult<OnlineMatchBridgeGameView>> GetGameInfoAsync(OnlineScope scope, long roomId, int round, CancellationToken cancellationToken = default)
    {
        if (!active)
        {
            return Task.FromResult(OnlineResult<OnlineMatchBridgeGameView>.Fail(
                OnlineErrorCode.DependencyUnavailable, "对局桥已撤销（宿主未运行或已停止）"));
        }

        return bridge.GetGameInfoAsync(scope, roomId, round, cancellationToken);
    }
}
