//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://github.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://github.com/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Foundation.Presence;

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;

/// <summary>
/// 队伍在线事实探针（<see cref="IOnlinePartyPresenceProbe"/> 的地基域实现：把成员在线判定委托给
/// <see cref="OnlinePresenceService"/> 事实源，不镜像、不缓存）。
/// <para>
/// 维护约束（红线）：Presence 是唯一在线事实源，本探针只读；在线判定口径 = 记录存在且状态非
/// <see cref="OnlinePresenceState.Offline"/> / <see cref="OnlinePresenceState.Blocked"/>
///（对齐 <see cref="OnlinePresenceService.CountOnlineAsync"/> 的统计口径）；查询失败按离线处理，
/// 队伍侧不得据此直接清理成员（离线判定见契约降级约定）。
/// </para>
/// </summary>
public sealed class OnlinePartyPresenceProbe : IOnlinePartyPresenceProbe
{
    /// <summary>
    /// 在线状态事实源。
    /// </summary>
    private readonly OnlinePresenceService _presence;

    /// <summary>
    /// 初始化 <see cref="OnlinePartyPresenceProbe"/>。
    /// </summary>
    /// <param name="presence">在线状态事实源。</param>
    public OnlinePartyPresenceProbe(OnlinePresenceService presence)
    {
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));
    }

    /// <inheritdoc />
    public async Task<bool> IsOnlineAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        var scope = new OnlineScope(tenantId, appId, 0, playerId);
        var result = await _presence.GetAsync(scope, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            && result.Data != OnlinePresenceState.Offline
            && result.Data != OnlinePresenceState.Blocked;
    }
}
