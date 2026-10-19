// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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

namespace GameFrameX.Capability.Online.Social;

/// <summary>
/// 好友查询契约面实现（C243）：把 <see cref="IOnlineFriendQuery"/> 桥接契约薄映射到
/// <see cref="OnlineFriendService"/>（域语义唯一属主），由社交能力模块注册回共享服务面。
/// <para>
/// 维护约束：本类只做字段映射与转发，不新增语义、不缓存——作用域校验、隔离、
/// 披露态（目录缺席回退 / 探针缺席恒离线）全部由域服务保证。
/// </para>
/// </summary>
/// <remarks>
/// The friend-query contract-face implementation (C243): a thin field-mapping adapter from the
/// <see cref="IOnlineFriendQuery"/> bridge contract to <see cref="OnlineFriendService"/> (the sole
/// owner of the domain semantics), registered back into the shared service face by the social
/// capability module. It adds no semantics and caches nothing — scope validation, isolation and the
/// disclosed absences (name fallback / offline probe) all remain guaranteed by the domain service.
/// </remarks>
public sealed class OnlineFriendQueryFace : IOnlineFriendQuery
{
    /// <summary>好友域服务。</summary>
    private readonly OnlineFriendService friends;

    /// <summary>
    /// 初始化 <see cref="OnlineFriendQueryFace"/>。
    /// </summary>
    /// <param name="friends">好友域服务。</param>
    public OnlineFriendQueryFace(OnlineFriendService friends)
    {
        this.friends = friends ?? throw new ArgumentNullException(nameof(friends));
    }

    /// <summary>
    /// 列出好友列表（委托域服务并映射为契约 DTO）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>好友列表条目。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineFriendEntry>>> ListFriendsAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var result = await friends.ListFriendsAsync(scope, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return OnlineResult<IReadOnlyList<OnlineFriendEntry>>.Fail(result.Code, result.Message);
        }

        var entries = new List<OnlineFriendEntry>(result.Data.Count);
        foreach (var summary in result.Data)
        {
            entries.Add(new OnlineFriendEntry
            {
                PlayerId = summary.PlayerId,
                Name = summary.Name,
                ServerId = summary.ServerId,
                Online = summary.Online,
                EstablishedAtTime = summary.EstablishedAtTime,
            });
        }

        return OnlineResult<IReadOnlyList<OnlineFriendEntry>>.Ok(entries);
    }
}
