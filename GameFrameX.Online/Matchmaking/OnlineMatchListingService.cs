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

namespace GameFrameX.Online.Matchmaking;

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Scope;

/// <summary>
/// 对局列表项服务（vault:C5 S4.4：发布、发现、按 JoinPolicy 加入）。
/// <para>
/// 维护约束（红线）：
/// ① JoinPolicy 四策略的判定在本类的 <c>ResolveJoinPolicy</c> 单点收敛，四个分支互斥且各有拒绝用例
/// （VC-4.14）；未登记的策略值一律拒绝，不做「未知即放行」的兜底。
/// ② 对外输出（查询结果、事件、审计）不携带 <see cref="OnlineMatchListing.Password"/> 与
/// <see cref="OnlineMatchListing.InvitedPlayerIds"/>——两者只参与判定（脱敏红线）。
/// ③ 容量是唯一事实源：<see cref="OnlineMatchListingState.Full"/> 由容量推导，不接受调用方手工置位。
/// </para>
/// </summary>
public sealed class OnlineMatchListingService
{
    /// <summary>默认返回条数上限。</summary>
    private const int DefaultQueryLimit = 50;

    /// <summary>列表项存储。</summary>
    private readonly IOnlineMatchListingStore _store;

    /// <summary>
    /// 初始化 <see cref="OnlineMatchListingService"/>。
    /// </summary>
    /// <param name="store">列表项存储。</param>
    public OnlineMatchListingService(IOnlineMatchListingStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// 发布对局列表项。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="request">发布请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>发布后的列表项。</returns>
    public async Task<OnlineResult<OnlineMatchListing>> PublishAsync(OnlineScope scope, OnlineMatchListingPublishRequest request, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        if (request == null)
        {
            return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.ParameterInvalid, "缺少发布请求");
        }

        if (request.Capacity <= 0)
        {
            return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.ParameterInvalid, "容量必须为正数");
        }

        if (request.JoinPolicy == OnlineJoinPolicy.Password && string.IsNullOrEmpty(request.Password))
        {
            return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.ParameterInvalid, "密码策略必须携带密码");
        }

        if (request.JoinPolicy == OnlineJoinPolicy.InviteOnly && (request.InvitedPlayerIds == null || request.InvitedPlayerIds.Count == 0))
        {
            return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.ParameterInvalid, "邀请策略必须登记邀请名单");
        }

        var listing = new OnlineMatchListing
        {
            ListingId = "lst-" + Guid.NewGuid().ToString("N"),
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            ServerId = scope.ServerId,
            PartyId = request.PartyId ?? string.Empty,
            OwnerPlayerId = scope.PlayerId,
            Name = request.Name ?? string.Empty,
            Mode = request.Mode,
            Region = request.Region,
            JoinPolicy = request.JoinPolicy,
            Password = request.Password ?? string.Empty,
            InvitedPlayerIds = request.InvitedPlayerIds == null ? new List<long>() : new List<long>(request.InvitedPlayerIds),
            Tags = request.Tags == null ? new List<string>() : new List<string>(request.Tags),
            Capacity = request.Capacity,
            JoinedCount = 0,
            State = OnlineMatchListingState.Open,
            CreatedAtTime = Now(),
        };
        await _store.SaveAsync(listing, cancellationToken);
        return OnlineResult<OnlineMatchListing>.Ok(Sanitize(listing));
    }

    /// <summary>
    /// 发现对局列表项（按模式/区域/策略/标签过滤，结果封顶）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="query">查询条件（可空表示默认只返回可加入项、上限 50）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>列表项列表（已剔除敏感字段）。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineMatchListing>>> QueryAsync(OnlineScope scope, OnlineMatchListingQuery query = null, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            return OnlineResult<IReadOnlyList<OnlineMatchListing>>.Fail(OnlineErrorCode.ParameterInvalid, "缺少作用域");
        }

        var filter = query ?? new OnlineMatchListingQuery();
        var limit = filter.Limit > 0 ? filter.Limit : DefaultQueryLimit;

        var all = await _store.ListAsync(scope.TenantId, scope.AppId, cancellationToken);
        var result = new List<OnlineMatchListing>();
        foreach (var listing in all)
        {
            if (result.Count >= limit)
            {
                break;
            }

            if (MatchesFilters(listing, filter))
            {
                result.Add(Sanitize(listing));
            }
        }

        return OnlineResult<IReadOnlyList<OnlineMatchListing>>.Ok(result);
    }

    /// <summary>
    /// 判定列表项是否命中查询过滤（仅由 <see cref="QueryAsync"/> 调用）。
    /// <para>裁决顺序与语义固定：可用性剔除 → 模式 → 区域 → 策略 → 标签，任一不中即落选；
    /// 各过滤条件为空（HasValue = false / 标签集合为空）表示该维度不过滤。</para>
    /// </summary>
    /// <param name="listing">列表项。</param>
    /// <param name="filter">查询条件。</param>
    /// <returns>命中返回 <c>true</c>。</returns>
    private static bool MatchesFilters(OnlineMatchListing listing, OnlineMatchListingQuery filter)
    {
        if (!filter.IncludeUnavailable && listing.State != OnlineMatchListingState.Open)
        {
            return false;
        }

        if (filter.Mode.HasValue && listing.Mode != filter.Mode.Value)
        {
            return false;
        }

        if (filter.Region.HasValue && listing.Region != filter.Region.Value)
        {
            return false;
        }

        if (filter.JoinPolicy.HasValue && listing.JoinPolicy != filter.JoinPolicy.Value)
        {
            return false;
        }

        return MatchesAnyTag(listing, filter.Tags);
    }

    /// <summary>
    /// 查询单个列表项。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="listingId">列表项标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>列表项（已剔除敏感字段）。</returns>
    public async Task<OnlineResult<OnlineMatchListing>> GetAsync(OnlineScope scope, string listingId, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.ParameterInvalid, "缺少作用域");
        }

        var listing = await _store.FindAsync(scope.TenantId, scope.AppId, listingId, cancellationToken);
        if (listing == null)
        {
            return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.ResourceNotFound, "列表项不存在");
        }

        return OnlineResult<OnlineMatchListing>.Ok(Sanitize(listing));
    }

    /// <summary>
    /// 按 JoinPolicy 加入列表项（VC-4.14）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="listingId">列表项标识。</param>
    /// <param name="request">加入请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>加入后的列表项（已剔除敏感字段）。</returns>
    public async Task<OnlineResult<OnlineMatchListing>> JoinAsync(OnlineScope scope, string listingId, OnlineMatchListingJoinRequest request, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        if (request == null || request.PlayerId != scope.PlayerId)
        {
            return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.ParameterInvalid, "请求方标识与作用域主体位不一致");
        }

        var listing = await _store.FindAsync(scope.TenantId, scope.AppId, listingId, cancellationToken);
        if (listing == null)
        {
            return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.ResourceNotFound, "列表项不存在");
        }

        if (listing.State == OnlineMatchListingState.Closed)
        {
            return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.StateEnded, "列表项已关闭");
        }

        var members = ResolveMembers(request);
        var policyFailure = ResolveJoinPolicy(listing, members, request.Password);
        if (policyFailure != null)
        {
            return policyFailure;
        }

        if (listing.JoinedCount + members.Count > listing.Capacity)
        {
            return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.StateNotReady, "剩余容量不足");
        }

        listing.JoinedCount += members.Count;
        listing.State = listing.JoinedCount >= listing.Capacity ? OnlineMatchListingState.Full : OnlineMatchListingState.Open;
        await _store.SaveAsync(listing, cancellationToken);
        return OnlineResult<OnlineMatchListing>.Ok(Sanitize(listing));
    }

    /// <summary>
    /// 关闭列表项（仅发布者可关闭；重复关闭返回既有终态）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="listingId">列表项标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>关闭后的列表项（已剔除敏感字段）。</returns>
    public async Task<OnlineResult<OnlineMatchListing>> CloseAsync(OnlineScope scope, string listingId, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var listing = await _store.FindAsync(scope.TenantId, scope.AppId, listingId, cancellationToken);
        if (listing == null || listing.OwnerPlayerId != scope.PlayerId)
        {
            return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.ResourceNotFound, "列表项不存在");
        }

        if (listing.State != OnlineMatchListingState.Closed)
        {
            listing.State = OnlineMatchListingState.Closed;
            await _store.SaveAsync(listing, cancellationToken);
        }

        return OnlineResult<OnlineMatchListing>.Ok(Sanitize(listing));
    }

    /// <summary>
    /// 策略裁决（VC-4.14 唯一判定点）。
    /// </summary>
    /// <param name="listing">列表项。</param>
    /// <param name="members">本次加入的有效成员集合（含请求方）。</param>
    /// <param name="password">请求携带的密码。</param>
    /// <returns>通过返回 null，拒绝返回错误结果。</returns>
    private static OnlineResult<OnlineMatchListing> ResolveJoinPolicy(OnlineMatchListing listing, List<long> members, string password)
    {
        switch (listing.JoinPolicy)
        {
            case OnlineJoinPolicy.Public:
            {
                return null;
            }

            case OnlineJoinPolicy.PartyOnly:
            {
                if (members.Count < 2)
                {
                    return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.StateOperationForbidden, "该列表项仅接受整队加入");
                }

                return null;
            }

            case OnlineJoinPolicy.InviteOnly:
            {
                foreach (var memberId in members)
                {
                    if (!listing.InvitedPlayerIds.Contains(memberId))
                    {
                        return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.StateOperationForbidden, "加入名单未包含全部成员");
                    }
                }

                return null;
            }

            case OnlineJoinPolicy.Password:
            {
                if (string.IsNullOrEmpty(password) || password != listing.Password)
                {
                    return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.ParameterInvalid, "加入密码不正确");
                }

                return null;
            }

            default:
            {
                // 未登记策略一律拒绝，且与其余三条拒绝分支同码：这是列表项自身的配置问题，
                // 不是调用方参数问题——用 4xxx 会让客户端误以为改请求就能通过。
                return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.StateOperationForbidden, "未知的加入策略");
            }
        }
    }

    /// <summary>
    /// 合并请求方与随行成员并去重（请求方恒在集合内）。
    /// </summary>
    /// <param name="request">加入请求。</param>
    /// <returns>去重后的成员集合。</returns>
    private static List<long> ResolveMembers(OnlineMatchListingJoinRequest request)
    {
        var members = new List<long> { request.PlayerId };
        if (request.MemberPlayerIds == null)
        {
            return members;
        }

        foreach (var memberId in request.MemberPlayerIds)
        {
            if (memberId > 0 && !members.Contains(memberId))
            {
                members.Add(memberId);
            }
        }

        return members;
    }

    /// <summary>
    /// 判定标签过滤是否命中（过滤为空即命中）。
    /// </summary>
    /// <param name="listing">列表项。</param>
    /// <param name="tags">过滤标签集合。</param>
    /// <returns>命中返回 <c>true</c>。</returns>
    private static bool MatchesAnyTag(OnlineMatchListing listing, List<string> tags)
    {
        if (tags == null || tags.Count == 0)
        {
            return true;
        }

        foreach (var tag in tags)
        {
            if (listing.Tags.Contains(tag))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 剔除敏感字段（密码与邀请名单不出对外）。
    /// </summary>
    /// <param name="listing">列表项。</param>
    /// <returns>可对外输出的副本。</returns>
    private static OnlineMatchListing Sanitize(OnlineMatchListing listing)
    {
        var copy = listing.Copy();
        copy.Password = string.Empty;
        copy.InvitedPlayerIds = new List<long>();
        return copy;
    }

    /// <summary>
    /// 校验作用域与玩家主体位。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <returns>成功返回 null，失败返回错误结果。</returns>
    private static OnlineResult<OnlineMatchListing> ValidateScope(OnlineScope scope)
    {
        if (scope == null)
        {
            return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.ParameterInvalid, "缺少作用域");
        }

        if (scope.PlayerId <= 0)
        {
            return OnlineResult<OnlineMatchListing>.Fail(OnlineErrorCode.ParameterInvalid, "列表项操作必须具备玩家主体位");
        }

        return null;
    }

    /// <summary>取当前 UTC 毫秒时刻。</summary>
    /// <returns>UTC 毫秒时间戳。</returns>
    private static long Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
