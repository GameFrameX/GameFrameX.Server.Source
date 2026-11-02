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
//   侵犯玩家合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   侵犯他人合法权益等法律法规所禁止的行为！
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 开服锚点内存存储（默认实现；部署侧可覆盖为持久化实现）。
/// </summary>
public sealed class InMemoryOnlineServerCalendarStore : IOnlineServerCalendarStore
{
    /// <summary>
    /// 锚点表守卫锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 锚点表（键 = ServerId）。
    /// </summary>
    private readonly Dictionary<long, OnlineServerAnchor> _anchors = new();

    /// <inheritdoc />
    public Task<(OnlineServerAnchor Anchor, bool Registered)> TryRegisterAsync(OnlineServerAnchor anchor, CancellationToken cancellationToken = default)
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

        lock (_lock)
        {
            if (_anchors.TryGetValue(anchor.ServerId, out var existing))
            {
                return Task.FromResult((existing, false));
            }

            var stored = new OnlineServerAnchor
            {
                ServerId = anchor.ServerId,
                OpenedAtUnixMilliseconds = anchor.OpenedAtUnixMilliseconds,
                RegisteredAtUnixMilliseconds = anchor.RegisteredAtUnixMilliseconds > 0 ? anchor.RegisteredAtUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MergedIntoServerId = 0,
            };
            _anchors[stored.ServerId] = stored;
            return Task.FromResult((stored, true));
        }
    }

    /// <inheritdoc />
    public Task<OnlineServerAnchor> FindAsync(long serverId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _anchors.TryGetValue(serverId, out var anchor);
            return Task.FromResult(anchor);
        }
    }

    /// <inheritdoc />
    public Task MarkMergedAsync(long serverId, long targetServerId, CancellationToken cancellationToken = default)
    {
        if (targetServerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetServerId), "TargetServerId must be positive.");
        }

        lock (_lock)
        {
            if (!_anchors.TryGetValue(serverId, out var anchor))
            {
                throw new InvalidOperationException("Server anchor not registered: " + serverId);
            }

            if (anchor.MergedIntoServerId == targetServerId)
            {
                return Task.CompletedTask;
            }

            if (anchor.MergedIntoServerId != 0)
            {
                throw new InvalidOperationException("Server " + serverId + " already merged into " + anchor.MergedIntoServerId + "; cannot retarget to " + targetServerId + ".");
            }

            anchor.MergedIntoServerId = targetServerId;
            return Task.CompletedTask;
        }
    }
}
