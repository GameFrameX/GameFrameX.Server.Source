// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Persistence;

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.ServerLifecycle;
using GameFrameX.DataBase;

/// <summary>
/// 开服锚点存储 GameDb 持久化实现（<see cref="OnlineServerAnchorState"/> 行；跨进程 / 跨运行生命周期可回读——
/// CLI 一次进程登记锚点、另一次进程合服执行的前置；部署侧亦可作为在线宿主的锚点存储绑定）。
/// <para>
/// 语义与内存实现一致：登记幂等（已登记返回既有锚点，不改写首登开服时刻）；标记归并幂等
/// （同目标重复标记 = no-op）；未登记 / 已合入其他目标的标记请求抛 <see cref="InvalidOperationException"/>
///（与 <see cref="InMemoryOnlineServerCalendarStore"/> 同口径，合服参与者的前置校验依赖该失败面）。
/// 标记归并走 CAS 条件写（读-改-写窗口不覆盖并发更新）。
/// </para>
/// </summary>
public sealed class GameDbOnlineServerCalendarStore : IOnlineServerCalendarStore
{
    /// <summary>
    /// 行 Id 共享序列名（与既有持久化行同口径，按类型隔离计数）。
    /// </summary>
    private const string RowIdSequence = "row-id";

    /// <summary>
    /// 单行 CAS 条件写失配后的最大重试次数（重读最新档后再试）。
    /// </summary>
    private const int MarkMergedMaxAttempts = 3;

    /// <summary>
    /// 初始化存储（GameDb 须已由宿主初始化）。
    /// </summary>
    public GameDbOnlineServerCalendarStore()
    {
    }

    /// <inheritdoc />
    public async Task<(OnlineServerAnchor Anchor, bool Registered)> TryRegisterAsync(OnlineServerAnchor anchor, CancellationToken cancellationToken = default)
    {
        if (anchor == null)
        {
            throw new ArgumentNullException(nameof(anchor));
        }

        if (anchor.ServerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(anchor), "ServerId must be positive.");
        }

        if (anchor.OpenedAtUnixMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(anchor), "OpenedAtUnixMilliseconds must be positive.");
        }

        var existing = await FindStateAsync(anchor.ServerId, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            return (ToAnchor(existing), false);
        }

        var state = new OnlineServerAnchorState
        {
            ServerId = anchor.ServerId,
            OpenedAtUnixMilliseconds = anchor.OpenedAtUnixMilliseconds,
            RegisteredAtUnixMilliseconds = anchor.RegisteredAtUnixMilliseconds > 0 ? anchor.RegisteredAtUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MergedIntoServerId = 0,
        };
        state.Id = await GameDb.NextSequenceAsync<OnlineServerAnchorState>(RowIdSequence, cancellationToken).ConfigureAwait(false);
        if (await GameDb.TryAddUniqueAsync(state, candidate => candidate.ServerId == anchor.ServerId, cancellationToken).ConfigureAwait(false))
        {
            return (ToAnchor(state), true);
        }

        // 唯一插入冲突（并发窗口落者）：回读胜者行——登记幂等，保留首登开服时刻。
        var winner = await FindStateAsync(anchor.ServerId, cancellationToken).ConfigureAwait(false);
        if (winner == null)
        {
            throw new InvalidOperationException("Server anchor unique-insert conflict but no existing row found: " + anchor.ServerId);
        }

        return (ToAnchor(winner), false);
    }

    /// <inheritdoc />
    public async Task<OnlineServerAnchor> FindAsync(long serverId, CancellationToken cancellationToken = default)
    {
        var state = await FindStateAsync(serverId, cancellationToken).ConfigureAwait(false);
        return state == null ? null : ToAnchor(state);
    }

    /// <inheritdoc />
    public async Task MarkMergedAsync(long serverId, long targetServerId, CancellationToken cancellationToken = default)
    {
        if (targetServerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetServerId), "TargetServerId must be positive.");
        }

        for (var attempt = 0; attempt < MarkMergedMaxAttempts; attempt++)
        {
            var state = await FindStateAsync(serverId, cancellationToken).ConfigureAwait(false);
            if (state == null)
            {
                throw new InvalidOperationException("Server anchor not registered: " + serverId);
            }

            if (state.MergedIntoServerId == targetServerId)
            {
                return;
            }

            if (state.MergedIntoServerId != 0)
            {
                throw new InvalidOperationException("Server " + serverId + " already merged into " + state.MergedIntoServerId + "; cannot retarget to " + targetServerId + ".");
            }

            state.MergedIntoServerId = targetServerId;
            var written = state.RowVersion == null
                ? await GameDb.UpdateIfRowVersionIsNullAsync(state.Id, state, cancellationToken).ConfigureAwait(false)
                : await GameDb.UpdateIfRowVersionAsync(state.Id, state.RowVersion.Value, state, cancellationToken).ConfigureAwait(false);
            if (written)
            {
                return;
            }
        }

        // 持续 CAS 失配（并发写者高频竞争）：按已合入他服的失败面收口，调用方重试即重新校验。
        throw new InvalidOperationException("Server anchor " + serverId + " mark-merged lost too many row-version races; retry the merge.");
    }

    /// <summary>
    /// 按 ServerId 查行（不存在返回 null）。
    /// </summary>
    private static Task<OnlineServerAnchorState> FindStateAsync(long serverId, CancellationToken cancellationToken)
    {
        return GameDb.FindAsync<OnlineServerAnchorState>(state => state.ServerId == serverId, isCreateIfNotExists: false, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 行 → 锚点实体。
    /// </summary>
    private static OnlineServerAnchor ToAnchor(OnlineServerAnchorState state)
    {
        return new OnlineServerAnchor
        {
            ServerId = state.ServerId,
            OpenedAtUnixMilliseconds = state.OpenedAtUnixMilliseconds,
            RegisteredAtUnixMilliseconds = state.RegisteredAtUnixMilliseconds,
            MergedIntoServerId = state.MergedIntoServerId,
        };
    }
}
