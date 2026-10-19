// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织及其贡献者不承担任何责任，开发者应自行承担。
//   The organization and contributors assume no responsibility; developers shall bear the responsibility themselves.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:    https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Social;

/// <summary>
/// 好友域桥契约面实现（C244）：把 <see cref="IOnlineFriendDomain"/> 桥接契约编排到
/// <see cref="OnlineFriendService"/>（域语义唯一属主），由社交能力模块注册回共享服务面。
/// <para>
/// 维护约束：本类承载「玩家对定位 → 关系标识编排」的翻译与玩家视角分类投影
///（<see cref="Classify"/>），不新增存储、不缓存——作用域校验、幂等收敛、CAS、
/// 好友上限与披露态全部由域服务保证；<see cref="EstablishAsync"/> 是旧 120 域「直接添加」
/// 语义（T2 裁定 R3：申请 + 立即接受；对方有待答申请时等价于本人接受）的唯一实现点。
/// </para>
/// </summary>
/// <remarks>
/// The friend-domain bridge contract-face implementation (C244): orchestrates the
/// <see cref="IOnlineFriendDomain"/> bridge contract onto <see cref="OnlineFriendService"/> (the sole
/// owner of the domain semantics), registered back into the shared service face by the social
/// capability module. This class carries only the "player-pair locating → friendship-id
/// orchestration" translation and the player-view classification projection
/// (<see cref="Classify"/>) — it adds no storage and caches nothing; scope validation, idempotent
/// convergence, CAS, the friend limit and the disclosed absences all remain guaranteed by the domain
/// service. <see cref="EstablishAsync"/> is the single implementation point of the legacy
/// domain-120 "direct add" semantics (ruling R3: request + immediate accept; converging to
/// "accepting their pending request" when one exists).
/// </remarks>
public sealed class OnlineFriendDomainFace : IOnlineFriendDomain
{
    /// <summary>好友域服务。</summary>
    private readonly OnlineFriendService friends;

    /// <summary>
    /// 初始化 <see cref="OnlineFriendDomainFace"/>。
    /// </summary>
    /// <param name="friends">好友域服务。</param>
    public OnlineFriendDomainFace(OnlineFriendService friends)
    {
        this.friends = friends ?? throw new ArgumentNullException(nameof(friends));
    }

    /// <summary>
    /// 列出好友列表（委托域服务并映射为契约条目）。
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

    /// <summary>
    /// 列出全量关系（委托域服务 <see cref="OnlineFriendService.ListRelationsAsync"/> 并投影为玩家视角分类）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>关系条目列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineFriendRelation>>> ListRelationsAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var result = await friends.ListRelationsAsync(scope, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return OnlineResult<IReadOnlyList<OnlineFriendRelation>>.Fail(result.Code, result.Message);
        }

        var relations = new List<OnlineFriendRelation>(result.Data.Count);
        foreach (var friendship in result.Data)
        {
            var other = friendship.OtherOf(scope.PlayerId);
            if (other <= 0)
            {
                continue;
            }

            relations.Add(new OnlineFriendRelation
            {
                OtherPlayerId = other,
                Kind = Classify(friendship, scope.PlayerId),
            });
        }

        return OnlineResult<IReadOnlyList<OnlineFriendRelation>>.Ok(relations);
    }

    /// <summary>
    /// 发起好友申请：先读既有关系定型 <c>Before</c>，再委托域服务（幂等收敛 / 静止态重发翻方向）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位 = 发起方）。</param>
    /// <param name="targetPlayerId">目标玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作前后的关系分类。</returns>
    public async Task<OnlineResult<OnlineFriendRelationChange>> RequestAsync(OnlineScope scope, long targetPlayerId, CancellationToken cancellationToken = default)
    {
        if (targetPlayerId <= 0)
        {
            return OnlineResult<OnlineFriendRelationChange>.Fail(OnlineErrorCode.ParameterInvalid, "目标玩家标识无效");
        }

        var before = await ClassifyPairAsync(scope, targetPlayerId, cancellationToken).ConfigureAwait(false);
        var result = await friends.RequestAsync(scope, targetPlayerId, null, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return OnlineResult<OnlineFriendRelationChange>.Fail(result.Code, result.Message);
        }

        return Ok(before, Classify(result.Data, scope.PlayerId));
    }

    /// <summary>
    /// 接受好友申请：前置判定「存在对方发来的待答申请」，否则显式
    /// <see cref="OnlineErrorCode.ResourceNotFound"/>（重复答复 / 非被请求方与旧协议「申请不存在」对齐）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位 = 被请求方）。</param>
    /// <param name="fromPlayerId">申请来源玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作前后的关系分类。</returns>
    public async Task<OnlineResult<OnlineFriendRelationChange>> AcceptAsync(OnlineScope scope, long fromPlayerId, CancellationToken cancellationToken = default)
    {
        var pending = await RequireIncomingRequestAsync(scope, fromPlayerId, cancellationToken).ConfigureAwait(false);
        if (pending == null)
        {
            return NotFound();
        }

        var result = await friends.AcceptAsync(scope, pending.FriendshipId, null, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return OnlineResult<OnlineFriendRelationChange>.Fail(result.Code, result.Message);
        }

        return Ok(OnlineFriendRelationKind.IncomingRequest, Classify(result.Data, scope.PlayerId));
    }

    /// <summary>
    /// 拒绝好友申请：前置判定同接受；成功后关系投影为 <see cref="OnlineFriendRelationKind.None"/>（静止态）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位 = 被请求方）。</param>
    /// <param name="fromPlayerId">申请来源玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作前后的关系分类。</returns>
    public async Task<OnlineResult<OnlineFriendRelationChange>> RejectAsync(OnlineScope scope, long fromPlayerId, CancellationToken cancellationToken = default)
    {
        var pending = await RequireIncomingRequestAsync(scope, fromPlayerId, cancellationToken).ConfigureAwait(false);
        if (pending == null)
        {
            return NotFound();
        }

        var result = await friends.RejectAsync(scope, pending.FriendshipId, null, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return OnlineResult<OnlineFriendRelationChange>.Fail(result.Code, result.Message);
        }

        return Ok(OnlineFriendRelationKind.IncomingRequest, Classify(result.Data, scope.PlayerId));
    }

    /// <summary>
    /// 解除好友关系：前置判定「双方确为好友」，否则显式 <see cref="OnlineErrorCode.ResourceNotFound"/>
    ///（重复解除 / 非好友与旧协议「关系不存在」对齐；幂等语义由前置判定承担）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位 = 解除发起方）。</param>
    /// <param name="targetPlayerId">对方玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作前后的关系分类。</returns>
    public async Task<OnlineResult<OnlineFriendRelationChange>> RemoveAsync(OnlineScope scope, long targetPlayerId, CancellationToken cancellationToken = default)
    {
        var failure = ValidatePair(scope, targetPlayerId);
        if (failure != null)
        {
            return failure;
        }

        var existing = await FindPairAsync(scope, targetPlayerId, cancellationToken).ConfigureAwait(false);
        if (existing == null || Classify(existing, scope.PlayerId) != OnlineFriendRelationKind.Friend)
        {
            return OnlineResult<OnlineFriendRelationChange>.Fail(OnlineErrorCode.ResourceNotFound, "好友关系不存在");
        }

        var result = await friends.RemoveAsync(scope, targetPlayerId, null, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return OnlineResult<OnlineFriendRelationChange>.Fail(result.Code, result.Message);
        }

        return Ok(OnlineFriendRelationKind.Friend, Classify(result.Data, scope.PlayerId));
    }

    /// <summary>
    /// 直接建立好友关系（T2 裁定 R3 的唯一实现点）：按既有关系分类分派——
    /// 已是好友 → 幂等返回；对方申请待答 → 等价于本人接受；
    /// 本方申请待答 → 以被请求方身份补接受；无关系 / 静止态 → 申请后立即以被请求方身份接受。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位 = 发起方）。</param>
    /// <param name="targetPlayerId">目标玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作前后的关系分类（成功后 <c>After = Friend</c>）。</returns>
    public async Task<OnlineResult<OnlineFriendRelationChange>> EstablishAsync(OnlineScope scope, long targetPlayerId, CancellationToken cancellationToken = default)
    {
        var failure = ValidatePair(scope, targetPlayerId);
        if (failure != null)
        {
            return failure;
        }

        var before = OnlineFriendRelationKind.None;
        var existing = await FindPairAsync(scope, targetPlayerId, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            before = Classify(existing, scope.PlayerId);
        }

        OnlineFriendship toAccept;
        OnlineScope acceptScope;
        switch (before)
        {
            case OnlineFriendRelationKind.Friend:
                // 幂等：已是好友，直接返回既有事实（适配层据 Before 映射「已是好友」错误码）。
                return Ok(OnlineFriendRelationKind.Friend, OnlineFriendRelationKind.Friend);
            case OnlineFriendRelationKind.IncomingRequest:
                // 对方先申请：直接添加 = 本人接受对方申请。
                toAccept = existing;
                acceptScope = scope;
                break;
            case OnlineFriendRelationKind.OutgoingRequest:
                // 本方申请待答：直接添加 = 以被请求方身份补接受（双方同意由直接添加语义成立）。
                toAccept = existing;
                acceptScope = WithPlayer(scope, targetPlayerId);
                break;
            default:
                // 无关系 / 静止态：先申请（幂等收敛到一条记录），再以被请求方身份接受。
                var requested = await friends.RequestAsync(scope, targetPlayerId, null, cancellationToken).ConfigureAwait(false);
                if (!requested.IsSuccess)
                {
                    return OnlineResult<OnlineFriendRelationChange>.Fail(requested.Code, requested.Message);
                }

                if (Classify(requested.Data, scope.PlayerId) == OnlineFriendRelationKind.Friend)
                {
                    // 并发窗口内已被建立（如对方同时接受）：收敛返回好友事实。
                    return Ok(OnlineFriendRelationKind.None, OnlineFriendRelationKind.Friend);
                }

                toAccept = requested.Data;
                acceptScope = WithPlayer(scope, requested.Data.AddresseeId);
                break;
        }

        var accepted = await friends.AcceptAsync(acceptScope, toAccept.FriendshipId, null, cancellationToken).ConfigureAwait(false);
        if (!accepted.IsSuccess)
        {
            return OnlineResult<OnlineFriendRelationChange>.Fail(accepted.Code, accepted.Message);
        }

        return Ok(before, Classify(accepted.Data, scope.PlayerId));
    }

    /// <summary>
    /// 玩家视角分类投影：已建立 → 好友；待答按方向（被请求方 / 发起方）；静止态 → 无关系。
    /// </summary>
    /// <param name="friendship">关系记录。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>关系分类。</returns>
    internal static OnlineFriendRelationKind Classify(OnlineFriendship friendship, long playerId)
    {
        if (friendship == null)
        {
            return OnlineFriendRelationKind.None;
        }

        if (friendship.State == OnlineFriendshipState.Accepted)
        {
            return OnlineFriendRelationKind.Friend;
        }

        if (friendship.State == OnlineFriendshipState.Requested)
        {
            return friendship.AddresseeId == playerId ? OnlineFriendRelationKind.IncomingRequest : OnlineFriendRelationKind.OutgoingRequest;
        }

        return OnlineFriendRelationKind.None;
    }

    /// <summary>
    /// 校验玩家对（作用域玩家位 + 目标标识；加自己 / 非法标识为参数错误）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="targetPlayerId">目标玩家标识。</param>
    /// <returns>失败返回错误结果；成功返回 null。</returns>
    private static OnlineResult<OnlineFriendRelationChange> ValidatePair(OnlineScope scope, long targetPlayerId)
    {
        if (targetPlayerId <= 0)
        {
            return OnlineResult<OnlineFriendRelationChange>.Fail(OnlineErrorCode.ParameterInvalid, "目标玩家标识无效");
        }

        if (targetPlayerId == scope.PlayerId)
        {
            return OnlineResult<OnlineFriendRelationChange>.Fail(OnlineErrorCode.ParameterInvalid, "不能添加自己为好友");
        }

        return null;
    }

    /// <summary>
    /// 构造成功的关系变更结果。
    /// </summary>
    /// <param name="before">操作前分类。</param>
    /// <param name="after">操作后分类。</param>
    /// <returns>成功结果。</returns>
    private static OnlineResult<OnlineFriendRelationChange> Ok(OnlineFriendRelationKind before, OnlineFriendRelationKind after)
    {
        return OnlineResult<OnlineFriendRelationChange>.Ok(new OnlineFriendRelationChange { Before = before, After = after });
    }

    /// <summary>
    /// 构造「申请 / 关系不存在」失败结果。
    /// </summary>
    /// <returns>失败结果。</returns>
    private static OnlineResult<OnlineFriendRelationChange> NotFound()
    {
        return OnlineResult<OnlineFriendRelationChange>.Fail(OnlineErrorCode.ResourceNotFound, "好友请求不存在");
    }

    /// <summary>
    /// 以同三元组 + 指定玩家位构造作用域（直接添加语义以被请求方身份补接受时使用）。
    /// </summary>
    /// <param name="scope">源作用域。</param>
    /// <param name="playerId">玩家位。</param>
    /// <returns>新作用域。</returns>
    private static OnlineScope WithPlayer(OnlineScope scope, long playerId)
    {
        return new OnlineScope(scope.TenantId, scope.AppId, scope.ServerId, playerId);
    }

    /// <summary>
    /// 按玩家对查找既有关系。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="targetPlayerId">对方玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>关系副本；不存在返回 null。</returns>
    private async Task<OnlineFriendship> FindPairAsync(OnlineScope scope, long targetPlayerId, CancellationToken cancellationToken)
    {
        var result = await friends.FindAsync(scope, targetPlayerId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result.Data : null;
    }

    /// <summary>
    /// 按玩家对定型当前分类。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="targetPlayerId">对方玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>关系分类（不存在为 None）。</returns>
    private async Task<OnlineFriendRelationKind> ClassifyPairAsync(OnlineScope scope, long targetPlayerId, CancellationToken cancellationToken)
    {
        var existing = await FindPairAsync(scope, targetPlayerId, cancellationToken).ConfigureAwait(false);
        return Classify(existing, scope.PlayerId);
    }

    /// <summary>
    /// 答复前置判定：存在对方发来的待答申请才返回该关系，否则 null。
    /// </summary>
    /// <param name="scope">生效作用域（玩家位 = 答复方）。</param>
    /// <param name="fromPlayerId">申请来源玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待答关系；不满足前置返回 null。</returns>
    private async Task<OnlineFriendship> RequireIncomingRequestAsync(OnlineScope scope, long fromPlayerId, CancellationToken cancellationToken)
    {
        if (fromPlayerId <= 0)
        {
            return null;
        }

        var existing = await FindPairAsync(scope, fromPlayerId, cancellationToken).ConfigureAwait(false);
        return Classify(existing, scope.PlayerId) == OnlineFriendRelationKind.IncomingRequest ? existing : null;
    }
}
