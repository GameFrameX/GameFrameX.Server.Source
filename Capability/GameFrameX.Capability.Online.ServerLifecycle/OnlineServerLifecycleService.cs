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
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
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

namespace GameFrameX.Capability.Online.ServerLifecycle;

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Events;

/// <summary>
/// 区服生命周期服务（<see cref="IOnlineServerCalendar"/> 实现：锚点登记幂等 + 首次登记发布开服事件）。
/// </summary>
public sealed class OnlineServerLifecycleService : IOnlineServerCalendar
{
    /// <summary>
    /// 一天的毫秒数（开服天数推导常数）。
    /// </summary>
    public const long MillisecondsPerDay = 86400000L;

    /// <summary>
    /// 开服锚点存储。
    /// </summary>
    private readonly IOnlineServerCalendarStore _store;

    /// <summary>
    /// 能力事件发布器（可为 null：无总线环境下的离线工具场景静默跳过发布）。
    /// </summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>
    /// 初始化服务。
    /// </summary>
    /// <param name="store">开服锚点存储。</param>
    /// <param name="eventPublisher">能力事件发布器（可空）。</param>
    public OnlineServerLifecycleService(IOnlineServerCalendarStore store, IOnlineEventPublisher eventPublisher = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher;
    }

    /// <inheritdoc />
    public async Task<(OnlineServerAnchor Anchor, bool Registered)> TryRegisterOpenAsync(long serverId, long openedAtUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        if (serverId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(serverId), "ServerId must be positive.");
        }

        if (openedAtUnixMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(openedAtUnixMilliseconds), "OpenedAtUnixMilliseconds must be positive.");
        }

        var outcome = await _store.TryRegisterAsync(new OnlineServerAnchor
        {
            ServerId = serverId,
            OpenedAtUnixMilliseconds = openedAtUnixMilliseconds,
        }, cancellationToken).ConfigureAwait(false);

        if (outcome.Registered && _eventPublisher != null)
        {
            await _eventPublisher.PublishAsync(OnlineServerLifecycleEvents.CreateServerOpened(serverId, outcome.Anchor.OpenedAtUnixMilliseconds), cancellationToken).ConfigureAwait(false);
        }

        return outcome;
    }

    /// <inheritdoc />
    public Task<OnlineServerAnchor> GetAnchorAsync(long serverId, CancellationToken cancellationToken = default)
    {
        return _store.FindAsync(serverId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> GetOpenServerDayAsync(long serverId, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        var anchor = await _store.FindAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (anchor == null)
        {
            return 0;
        }

        if (nowUnixMilliseconds < anchor.OpenedAtUnixMilliseconds)
        {
            return 1;
        }

        var elapsedDays = (nowUnixMilliseconds - anchor.OpenedAtUnixMilliseconds) / MillisecondsPerDay;
        return checked((int)(elapsedDays + 1));
    }
}
