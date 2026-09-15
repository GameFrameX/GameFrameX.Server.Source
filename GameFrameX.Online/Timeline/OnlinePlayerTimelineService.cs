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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Identity;
using GameFrameX.Online.Match;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Session;
using GameFrameX.Online.Social;

namespace GameFrameX.Online.Timeline;

/// <summary>
/// 玩家时间线只读查询服务（vault:C9 S8.1：Admin 玩家时间线的服务端半边）。
/// <para>
/// 维护约束（红线）：
/// ① **只读**——本服务只依赖各域读取面：存储读接口（<see cref="IOnlineIdentityStore"/> / <see cref="IOnlineSessionStore"/> /
/// <see cref="IOnlineMatchActorStore"/> / <see cref="IOnlineSocialGraphStore"/>）、只读查询服务（C95 <see cref="OnlineAssetQueryService"/>）
/// 与可空探针（<see cref="IOnlineConfigHitProbe"/>）。不持有任何写入型服务，调用本服务不产生状态副作用与事件
/// ——「只读视图不直写各域状态」（X3）由类型系统结构性保证，对齐 C97 <c>OnlineMatchQueueObserver</c> 先例；
/// ② 数据源唯一 = 各权威域自身记录，**不拼接、不推断、不补齐**：缺数据源（配置命中未装配探针）即空槽；
/// ③ 全部行按作用域三键 (TenantId, AppId, ServerId) + 玩家主体位过滤，跨作用域读取与「无数据」**同构**（反预言）：
/// 查询先以**档案作用域门**判定「该玩家是否存在于本作用域」，不存在即直接返回空行集、不触及其余各腿
/// （处罚记录没有区服维度，若逐腿各自过滤，跨作用域查询会返回「有处罚但无身份」的半截视图而泄露存在性）。
/// </para>
/// <para>
/// 五条腿（每条腿直读权威域，逐行保留来源域标识与关联标识，供按行定位原始记录）：
/// <list type="bullet">
/// <item><description>身份腿 → 分组 <see cref="OnlinePlayerTimelineGroup.Session"/>：玩家档案、账号、绑定身份、玩家侧非终态会话。</description></item>
/// <item><description>资产腿 → 分组 <see cref="OnlinePlayerTimelineGroup.Asset"/>：账本流水（经 C95 只读查询服务读取）。</description></item>
/// <item><description>对局腿 → 分组 <see cref="OnlinePlayerTimelineGroup.Match"/>：该玩家作为成员的对局。</description></item>
/// <item><description>处罚腿 → 分组 <see cref="OnlinePlayerTimelineGroup.Penalty"/>：历史上全部处罚及其撤销。</description></item>
/// <item><description>配置命中腿 → 分组 <see cref="OnlinePlayerTimelineGroup.LiveOps"/>：探针数据源，未装配即空槽。</description></item>
/// </list>
/// 社交分组（<see cref="OnlinePlayerTimelineGroup.Social"/>）为消费方词表保留值，本服务当前不落腿——按该分组过滤得到空列表。
/// </para>
/// <para>
/// 排序与分页：合并后按「发生时刻（Unix 秒）倒序 → 行标识序数升序」构成**全序**（同秒多行由行标识打破平局），
/// 游标是该全序上的不透明令牌（只回传不构造），翻页期间新写入的行不重不漏。
/// 时间窗与分组过滤在对齐全序前生效；游标筛选在全序上生效（等价于「跳过游标之前的所有行」）。
/// </para>
/// <para>
/// 天花板（ponytail）：各腿均为按玩家 / 作用域过滤后的内存聚合，规模有限；账本腿受
/// <see cref="LedgerScanCeiling"/> 限制（见该常量注释），对局腿按 App 级列表全扫后按玩家过滤。
/// </para>
/// </summary>
public sealed class OnlinePlayerTimelineService
{
    /// <summary>未指定页大小时使用的默认页大小。</summary>
    private const int DefaultPageSize = 50;

    /// <summary>页大小上限；超过即拒绝，防止消费方一次拉走全量时间线。</summary>
    private const int MaxPageSize = 200;

    /// <summary>
    /// 账本腿扫描上限（条）。
    /// <para>
    /// 天花板（ponytail）：取 C95 账本读取面单次允许的最大条数（1～100）。账本读取面按账本序**正序**分页且无倒序读取，
    /// 因此本腿覆盖的是**最早的** <see cref="LedgerScanCeiling"/> 条流水，更晚的流水被截断（截断发生在最新端）。
    /// 升级路径：存储层提供按时间 / 倒序索引读取后，本腿改为按时间窗从最新端读取，上限改为窗口内条数。
    /// </para>
    /// </summary>
    private const int LedgerScanCeiling = 100;

    /// <summary>游标内部的字段分隔符（游标对消费方不透明，此分隔符属实现细节）。</summary>
    private const char CursorSeparator = '|';

    /// <summary>身份存储（档案 / 账号 / 绑定身份读取面）。</summary>
    private readonly IOnlineIdentityStore _identityStore;

    /// <summary>会话存储（玩家侧会话读取面）。</summary>
    private readonly IOnlineSessionStore _sessionStore;

    /// <summary>资产只读查询服务（账本流水读取面）。</summary>
    private readonly OnlineAssetQueryService _assetQueryService;

    /// <summary>对局存储（对局读取面）。</summary>
    private readonly IOnlineMatchActorStore _matchStore;

    /// <summary>社交关系存储（处罚读取面）。</summary>
    private readonly IOnlineSocialGraphStore _socialGraphStore;

    /// <summary>配置命中探针（可空；未装配时该腿为空槽）。</summary>
    private readonly IOnlineConfigHitProbe _configHitProbe;

    /// <summary>
    /// 初始化 <see cref="OnlinePlayerTimelineService"/>。
    /// </summary>
    /// <param name="identityStore">身份存储。</param>
    /// <param name="sessionStore">会话存储。</param>
    /// <param name="assetQueryService">资产只读查询服务。</param>
    /// <param name="matchStore">对局存储。</param>
    /// <param name="socialGraphStore">社交关系存储（处罚读取面）。</param>
    /// <param name="configHitProbe">配置命中探针；未装配传 <see langword="null"/>，该腿返回空槽。</param>
    public OnlinePlayerTimelineService(
        IOnlineIdentityStore identityStore,
        IOnlineSessionStore sessionStore,
        OnlineAssetQueryService assetQueryService,
        IOnlineMatchActorStore matchStore,
        IOnlineSocialGraphStore socialGraphStore,
        IOnlineConfigHitProbe configHitProbe = null)
    {
        _identityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _assetQueryService = assetQueryService ?? throw new ArgumentNullException(nameof(assetQueryService));
        _matchStore = matchStore ?? throw new ArgumentNullException(nameof(matchStore));
        _socialGraphStore = socialGraphStore ?? throw new ArgumentNullException(nameof(socialGraphStore));
        _configHitProbe = configHitProbe;
    }

    /// <summary>
    /// 查询指定玩家的跨域时间线（只读；不产生任何状态变更）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含完整三键与玩家主体位）。</param>
    /// <param name="query">查询条件；传 <see langword="null"/> 等价于默认条件（全部行、首页、默认页大小）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>时间线分页结果；作用域 / 玩家位缺失、页大小越界、游标非法返回 <see cref="OnlineErrorCode.ParameterInvalid"/>。
    /// 无任何数据时返回**空行集**（与跨作用域读取同构，不是错误）。</returns>
    public async Task<OnlineResult<OnlinePlayerTimelinePage>> QueryAsync(OnlineScope scope, OnlinePlayerTimelineQuery query = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlinePlayerTimelinePage>(scope);
        if (failure != null)
        {
            return failure;
        }

        var request = query ?? new OnlinePlayerTimelineQuery();

        var pageSize = request.PageSize <= 0 ? DefaultPageSize : request.PageSize;
        if (pageSize > MaxPageSize)
        {
            return OnlineResult<OnlinePlayerTimelinePage>.Fail(OnlineErrorCode.ParameterInvalid, "页大小必须在 1～200 之间");
        }

        var hasCursor = !string.IsNullOrEmpty(request.Cursor);
        long cursorOccurredAt = 0;
        string cursorEventId = null;
        if (hasCursor && !TryDecodeCursor(request.Cursor, out cursorOccurredAt, out cursorEventId))
        {
            return OnlineResult<OnlinePlayerTimelinePage>.Fail(OnlineErrorCode.ParameterInvalid, "游标格式非法（不透明令牌只回传不构造）");
        }

        var profile = await LoadInScopeProfileAsync(scope, cancellationToken).ConfigureAwait(false);
        if (profile == null)
        {
            // 作用域门（反预言）：本作用域内不存在该玩家，与「该玩家无任何记录」同构返回空行集，不触及其余各腿。
            return OnlineResult<OnlinePlayerTimelinePage>.Ok(new OnlinePlayerTimelinePage(new List<OnlinePlayerTimelineEntry>(), new OnlinePageCursor(string.Empty, false)));
        }

        var rows = new List<OnlinePlayerTimelineEntry>();
        await CollectIdentityRowsAsync(scope, profile, rows, cancellationToken).ConfigureAwait(false);
        await CollectSessionRowsAsync(scope, rows, cancellationToken).ConfigureAwait(false);

        var assetFailure = await CollectAssetRowsAsync(scope, rows, cancellationToken).ConfigureAwait(false);
        if (assetFailure != null)
        {
            return assetFailure;
        }

        await CollectMatchRowsAsync(scope, rows, cancellationToken).ConfigureAwait(false);
        await CollectPenaltyRowsAsync(scope, rows, cancellationToken).ConfigureAwait(false);
        await CollectConfigHitRowsAsync(scope, rows, cancellationToken).ConfigureAwait(false);

        var selected = SelectRows(rows, request, hasCursor, cursorOccurredAt, cursorEventId);
        selected.Sort(CompareEntries);

        return OnlineResult<OnlinePlayerTimelinePage>.Ok(BuildPage(selected, pageSize));
    }

    /// <summary>
    /// 选出落在分组 / 时间窗过滤内且位于游标之后的行（过滤在前、游标筛选在全序上生效）。
    /// <para>
    /// 返回结果未排序，全序排序由调用方执行。
    /// </para>
    /// </summary>
    /// <param name="rows">全量候选行。</param>
    /// <param name="request">查询条件。</param>
    /// <param name="hasCursor">是否携带游标。</param>
    /// <param name="cursorOccurredAt">游标行的发生时刻（Unix 秒）。</param>
    /// <param name="cursorEventId">游标行的行标识。</param>
    /// <returns>命中行集合（未排序）。</returns>
    private static List<OnlinePlayerTimelineEntry> SelectRows(List<OnlinePlayerTimelineEntry> rows, OnlinePlayerTimelineQuery request, bool hasCursor, long cursorOccurredAt, string cursorEventId)
    {
        var selected = new List<OnlinePlayerTimelineEntry>();
        foreach (var row in rows)
        {
            if (!MatchesFilter(row, request))
            {
                continue;
            }

            if (hasCursor && !IsAfterCursor(row, cursorOccurredAt, cursorEventId))
            {
                continue;
            }

            selected.Add(row);
        }

        return selected;
    }

    /// <summary>
    /// 按全序切页并组装分页结果：取前 <paramref name="pageSize"/> 行，有更多行时以本页末行编码下一页游标。
    /// </summary>
    /// <param name="selected">已按全序排序的候选行。</param>
    /// <param name="pageSize">页大小（已校验在 1～<see cref="MaxPageSize"/> 之间）。</param>
    /// <returns>组装好的分页结果。</returns>
    private static OnlinePlayerTimelinePage BuildPage(List<OnlinePlayerTimelineEntry> selected, int pageSize)
    {
        var page = new List<OnlinePlayerTimelineEntry>(pageSize);
        foreach (var row in selected)
        {
            if (page.Count >= pageSize)
            {
                break;
            }

            page.Add(row);
        }

        var hasMore = selected.Count > page.Count;
        var nextCursor = hasMore ? EncodeCursor(page[page.Count - 1]) : string.Empty;

        return new OnlinePlayerTimelinePage(page, new OnlinePageCursor(nextCursor, hasMore));
    }

    /// <summary>
    /// 按玩家标识加载档案并判定其是否落在本作用域（作用域门的唯一实现处）。
    /// <para>
    /// 档案读取面按玩家标识单查、不带作用域参数，故此处显式做三键比对：跨作用域玩家与不存在**同构**（都返回 <see langword="null"/>）。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>作用域内的档案；不存在或属于其它作用域时返回 <see langword="null"/>。</returns>
    private async Task<OnlinePlayerProfile> LoadInScopeProfileAsync(OnlineScope scope, CancellationToken cancellationToken)
    {
        var profile = await _identityStore.FindPlayerAsync(scope.PlayerId, cancellationToken).ConfigureAwait(false);
        if (profile == null || !IsInScope(profile.TenantId, profile.AppId, profile.ServerId, scope))
        {
            return null;
        }

        return profile;
    }

    /// <summary>
    /// 收集身份腿行（分组 Session）：玩家档案、账号、绑定身份。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="profile">已通过作用域门的玩家档案。</param>
    /// <param name="rows">行累加容器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task CollectIdentityRowsAsync(OnlineScope scope, OnlinePlayerProfile profile, List<OnlinePlayerTimelineEntry> rows, CancellationToken cancellationToken)
    {
        rows.Add(new OnlinePlayerTimelineEntry
        {
            EventId = "profile:" + profile.Id.ToString(CultureInfo.InvariantCulture),
            EventType = OnlinePlayerTimelineEventTypes.PlayerProfileCreated,
            Group = OnlinePlayerTimelineGroup.Session,
            OccurredAt = ToSeconds(profile.CreatedAtTime),
            Source = OnlinePlayerTimelineEventTypes.SourceIdentity,
            CorrelationId = profile.Id.ToString(CultureInfo.InvariantCulture),
            PayloadSummary = "玩家档案：" + profile.Name,
        });

        var account = await _identityStore.FindAccountAsync(profile.GameAccountId, cancellationToken).ConfigureAwait(false);
        if (account != null && account.TenantId == scope.TenantId && account.AppId == scope.AppId)
        {
            rows.Add(new OnlinePlayerTimelineEntry
            {
                EventId = "account:" + account.Id.ToString(CultureInfo.InvariantCulture),
                EventType = OnlinePlayerTimelineEventTypes.AccountRegistered,
                Group = OnlinePlayerTimelineGroup.Session,
                OccurredAt = ToSeconds(account.CreatedAtTime),
                Source = OnlinePlayerTimelineEventTypes.SourceIdentity,
                CorrelationId = account.Id.ToString(CultureInfo.InvariantCulture),
                PayloadSummary = "账号状态：" + account.Status,
            });
        }

        var identities = await _identityStore.ListIdentitiesAsync(profile.GameAccountId, cancellationToken).ConfigureAwait(false);
        if (identities == null)
        {
            return;
        }

        foreach (var identity in identities)
        {
            if (identity == null || identity.TenantId != scope.TenantId || identity.AppId != scope.AppId)
            {
                continue;
            }

            var unbound = identity.UnboundAtTime > 0;
            rows.Add(new OnlinePlayerTimelineEntry
            {
                EventId = "identity:" + identity.Id + (unbound ? ":unbound" : ":bound"),
                EventType = unbound ? OnlinePlayerTimelineEventTypes.IdentityUnbound : OnlinePlayerTimelineEventTypes.IdentityBound,
                Group = OnlinePlayerTimelineGroup.Session,
                OccurredAt = ToSeconds(unbound ? identity.UnboundAtTime : identity.CreatedAtTime),
                Source = OnlinePlayerTimelineEventTypes.SourceIdentity,
                CorrelationId = identity.Id,
                // 脱敏：只落身份类别（用户名/邮箱/设备/渠道/三方），不落标识字面量。
                PayloadSummary = (unbound ? "解绑身份：" : "绑定身份：") + identity.Kind,
            });
        }
    }

    /// <summary>
    /// 收集会话腿行（分组 Session）：玩家侧非终态会话及其当前状态。
    /// <para>
    /// 每行是「会话记录当前状态」的投影，事件类型复用该状态对应的既有事件常量（不另造词表）；
    /// 故同一会话状态变化后行标识随之变化（<c>session:&lt;id&gt;:&lt;状态&gt;</c>），不会与旧状态行冲突。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="rows">行累加容器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task CollectSessionRowsAsync(OnlineScope scope, List<OnlinePlayerTimelineEntry> rows, CancellationToken cancellationToken)
    {
        var sessions = await _sessionStore.ListActiveByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        if (sessions == null)
        {
            return;
        }

        foreach (var session in sessions)
        {
            if (session == null || session.ServerId != scope.ServerId)
            {
                continue;
            }

            rows.Add(new OnlinePlayerTimelineEntry
            {
                EventId = "session:" + session.Id + ":" + session.State,
                EventType = ResolveSessionEventType(session.State),
                Group = OnlinePlayerTimelineGroup.Session,
                OccurredAt = ToSeconds(ResolveSessionOccurredTime(session)),
                Source = OnlineSessionEvents.Source,
                CorrelationId = session.Id,
                // 脱敏：只落会话标识与状态，不落设备标识、连接标识与令牌散列。
                PayloadSummary = "会话 " + session.Id + " 状态 " + session.State,
            });
        }
    }

    /// <summary>
    /// 收集资产腿行（分组 Asset）：账本流水。
    /// <para>
    /// 只读经 C95 <see cref="OnlineAssetQueryService"/>（不直连存储，保持依赖面为「只读查询服务」）；
    /// 账本键为 (租户, App, 玩家)、**无区服维度**（<c>HomeServerId</c> 只表资产归属/运行位置，不做数据隔离），
    /// 故此处不按归属服过滤——否则「归属服与玩家所在服不一致」的流水会在**任何作用域**都取不到（静默缺行，VC-8.1-c）。
    /// 作用域边界由档案作用域门承担（他服玩家的档案过不了门，根本走不到本腿）。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="rows">行累加容器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>失败结果（账本腿失败不做降级）；成功返回 <see langword="null"/>。</returns>
    private async Task<OnlineResult<OnlinePlayerTimelinePage>> CollectAssetRowsAsync(OnlineScope scope, List<OnlinePlayerTimelineEntry> rows, CancellationToken cancellationToken)
    {
        var ledger = await _assetQueryService.GetLedgerPageAsync(scope, string.Empty, LedgerScanCeiling, cancellationToken).ConfigureAwait(false);
        if (!ledger.IsSuccess)
        {
            // 账本腿失败**不降级为空槽**：空槽语义只用于「数据源未装配」，把失败伪装成空数据会让运维时间线静默缺行。
            return OnlineResult<OnlinePlayerTimelinePage>.Fail(ledger.Code, ledger.Message);
        }

        if (ledger.Data == null || ledger.Data.Entries == null)
        {
            return null;
        }

        foreach (var entry in ledger.Data.Entries)
        {
            if (entry == null)
            {
                continue;
            }

            rows.Add(new OnlinePlayerTimelineEntry
            {
                EventId = "ledger:" + entry.EntryId,
                EventType = ResolveAssetEventType(entry.Operation),
                Group = OnlinePlayerTimelineGroup.Asset,
                OccurredAt = ToSeconds(entry.OccurredTime),
                Source = OnlineAssetEvents.Source,
                CorrelationId = entry.TransactionId,
                PayloadSummary = "资产 " + entry.AssetKind + " " + entry.AssetId + " " + FormatDelta(entry.Delta) + " → " + entry.AmountAfter.ToString(CultureInfo.InvariantCulture) + BuildReasonSuffix(entry.Reason),
            });
        }

        return null;
    }

    /// <summary>
    /// 收集对局腿行（分组 Match）：该玩家作为成员的对局。
    /// <para>
    /// 对局读取面按 (租户, App) 列表返回（无按玩家查询面），故此处全扫后按成员关系过滤——玩家的成员关系以
    /// <c>OnlineMatch.FindMember</c> 为准，不从票据 / 分配记录推断。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="rows">行累加容器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task CollectMatchRowsAsync(OnlineScope scope, List<OnlinePlayerTimelineEntry> rows, CancellationToken cancellationToken)
    {
        var matches = await _matchStore.ListAsync(scope.TenantId, scope.AppId, cancellationToken).ConfigureAwait(false);
        if (matches == null)
        {
            return;
        }

        foreach (var match in matches)
        {
            if (match == null || match.ServerId != scope.ServerId || match.FindMember(scope.PlayerId) == null)
            {
                continue;
            }

            var settled = match.EndedTime > 0;
            rows.Add(new OnlinePlayerTimelineEntry
            {
                EventId = "match:" + match.MatchId + (settled ? ":settled" : ":state"),
                EventType = settled ? OnlineMatchRuntimeEvents.MatchSettled : OnlineMatchRuntimeEvents.MatchStateChanged,
                Group = OnlinePlayerTimelineGroup.Match,
                OccurredAt = ToSeconds(settled ? match.EndedTime : FirstNonZero(match.StateChangedTime, match.CreatedTime)),
                Source = OnlineMatchRuntimeEvents.Source,
                CorrelationId = match.MatchId,
                PayloadSummary = "对局 " + match.MatchId + "：" + match.State + "，模式 " + match.Mode.ToString(CultureInfo.InvariantCulture) + "/区域 " + match.Region.ToString(CultureInfo.InvariantCulture),
            });
        }
    }

    /// <summary>
    /// 收集处罚腿行（分组 Penalty）：处罚记录在下发时刻落一行，已撤销的处罚额外在撤销时刻落一行。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="rows">行累加容器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task CollectPenaltyRowsAsync(OnlineScope scope, List<OnlinePlayerTimelineEntry> rows, CancellationToken cancellationToken)
    {
        var punishments = await _socialGraphStore.ListPunishmentsByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        if (punishments == null)
        {
            return;
        }

        foreach (var punishment in punishments)
        {
            if (punishment == null)
            {
                continue;
            }

            rows.Add(new OnlinePlayerTimelineEntry
            {
                EventId = "punishment:" + punishment.PunishmentId + ":applied",
                EventType = OnlineSocialEvents.PunishmentChanged,
                Group = OnlinePlayerTimelineGroup.Penalty,
                OccurredAt = ToSeconds(punishment.CreatedAtTime),
                Source = OnlineSocialEvents.Source,
                CorrelationId = punishment.PunishmentId,
                PayloadSummary = "处罚 " + punishment.Kind + BuildReasonSuffix(punishment.Reason),
            });

            if (punishment.Revoked && punishment.RevokedAtTime > 0)
            {
                rows.Add(new OnlinePlayerTimelineEntry
                {
                    EventId = "punishment:" + punishment.PunishmentId + ":revoked",
                    EventType = OnlineSocialEvents.PunishmentChanged,
                    Group = OnlinePlayerTimelineGroup.Penalty,
                    OccurredAt = ToSeconds(punishment.RevokedAtTime),
                    Source = OnlineSocialEvents.Source,
                    CorrelationId = punishment.PunishmentId,
                    PayloadSummary = "解除处罚 " + punishment.Kind,
                });
            }
        }
    }

    /// <summary>
    /// 收集配置命中腿行（分组 LiveOps）。
    /// <para>
    /// 探针未装配（<see langword="null"/>）即返回**空槽**——不从其它域推断补齐（推断值与真实配置域必然漂移且无法追溯）。
    /// 行标识以探针给出的原值为主：缺失时用关联标识合成（<c>config:&lt;关联标识&gt;</c>），
    /// 因为空行标识一旦成为某页末行，游标（<c>&lt;秒&gt;|&lt;行标识&gt;</c>）就不可解码，翻页会整页报参数非法；
    /// 行标识与关联标识皆缺的行不可定位（VC-8.5）故整行跳过。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="rows">行累加容器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task CollectConfigHitRowsAsync(OnlineScope scope, List<OnlinePlayerTimelineEntry> rows, CancellationToken cancellationToken)
    {
        if (_configHitProbe == null)
        {
            return;
        }

        var hits = await _configHitProbe.ListHitsAsync(scope.TenantId, scope.AppId, scope.ServerId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        if (hits == null)
        {
            return;
        }

        foreach (var hit in hits)
        {
            if (hit == null)
            {
                continue;
            }

            var eventId = hit.EventId;
            if (string.IsNullOrEmpty(eventId) && !string.IsNullOrEmpty(hit.CorrelationId))
            {
                eventId = "config:" + hit.CorrelationId;
            }

            if (string.IsNullOrEmpty(eventId))
            {
                continue;
            }

            rows.Add(new OnlinePlayerTimelineEntry
            {
                EventId = eventId,
                EventType = hit.EventType,
                Group = OnlinePlayerTimelineGroup.LiveOps,
                OccurredAt = hit.OccurredAt,
                Source = string.IsNullOrEmpty(hit.Source) ? OnlinePlayerTimelineEventTypes.SourceConfig : hit.Source,
                CorrelationId = hit.CorrelationId,
                PayloadSummary = hit.PayloadSummary,
            });
        }
    }

    /// <summary>
    /// 判断行是否落在分组与时间窗过滤内。
    /// </summary>
    /// <param name="entry">待判定行。</param>
    /// <param name="query">查询条件。</param>
    /// <returns>命中过滤返回 <c>true</c>。</returns>
    private static bool MatchesFilter(OnlinePlayerTimelineEntry entry, OnlinePlayerTimelineQuery query)
    {
        if (!string.IsNullOrEmpty(query.Group) && !string.Equals(entry.Group, query.Group, StringComparison.Ordinal))
        {
            return false;
        }

        if (query.StartTime.HasValue && entry.OccurredAt < query.StartTime.Value)
        {
            return false;
        }

        if (query.EndTime.HasValue && entry.OccurredAt > query.EndTime.Value)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 判断行是否严格位于游标之后（全序：(发生时刻倒序, 行标识序数升序)）。
    /// </summary>
    /// <param name="entry">待判定行。</param>
    /// <param name="cursorOccurredAt">游标行的发生时刻（Unix 秒）。</param>
    /// <param name="cursorEventId">游标行的行标识。</param>
    /// <returns>位于游标之后返回 <c>true</c>。</returns>
    private static bool IsAfterCursor(OnlinePlayerTimelineEntry entry, long cursorOccurredAt, string cursorEventId)
    {
        if (entry.OccurredAt != cursorOccurredAt)
        {
            return entry.OccurredAt < cursorOccurredAt;
        }

        return string.CompareOrdinal(entry.EventId, cursorEventId) > 0;
    }

    /// <summary>
    /// 全序比较：发生时刻倒序，同一秒内按行标识序数升序（保证分页稳定）。
    /// </summary>
    /// <param name="left">左值。</param>
    /// <param name="right">右值。</param>
    /// <returns>比较结果。</returns>
    private static int CompareEntries(OnlinePlayerTimelineEntry left, OnlinePlayerTimelineEntry right)
    {
        if (left.OccurredAt != right.OccurredAt)
        {
            return right.OccurredAt.CompareTo(left.OccurredAt);
        }

        return string.CompareOrdinal(left.EventId, right.EventId);
    }

    /// <summary>
    /// 编码游标（不透明令牌：消费方只回传不构造）。
    /// </summary>
    /// <param name="entry">本页最后一行。</param>
    /// <returns>游标令牌。</returns>
    private static string EncodeCursor(OnlinePlayerTimelineEntry entry)
    {
        return entry.OccurredAt.ToString(CultureInfo.InvariantCulture) + CursorSeparator + entry.EventId;
    }

    /// <summary>
    /// 解析游标；格式非法（缺分隔符 / 时刻非数 / 行标识为空）返回 <c>false</c>，由调用方映射为参数非法。
    /// </summary>
    /// <param name="cursor">游标令牌。</param>
    /// <param name="occurredAt">解析出的发生时刻（Unix 秒）。</param>
    /// <param name="eventId">解析出的行标识。</param>
    /// <returns>解析成功返回 <c>true</c>。</returns>
    private static bool TryDecodeCursor(string cursor, out long occurredAt, out string eventId)
    {
        occurredAt = 0;
        eventId = string.Empty;

        var separator = cursor.IndexOf(CursorSeparator);
        if (separator <= 0 || separator == cursor.Length - 1)
        {
            return false;
        }

        if (!long.TryParse(cursor.Substring(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out occurredAt))
        {
            return false;
        }

        eventId = cursor.Substring(separator + 1);
        return true;
    }

    /// <summary>
    /// 会话状态到既有会话事件常量的映射（不新造词表）。
    /// </summary>
    /// <param name="state">会话状态。</param>
    /// <returns>该状态对应的事件类型常量。</returns>
    private static string ResolveSessionEventType(OnlineSessionState state)
    {
        switch (state)
        {
            case OnlineSessionState.Created:
                return OnlineSessionEvents.SessionCreated;
            case OnlineSessionState.Authenticated:
                return OnlineSessionEvents.SessionAuthenticated;
            case OnlineSessionState.Connected:
                return OnlineSessionEvents.SessionConnected;
            case OnlineSessionState.Active:
                return OnlineSessionEvents.SessionActive;
            case OnlineSessionState.Reconnecting:
                return OnlineSessionEvents.SessionReconnecting;
            case OnlineSessionState.Closed:
                return OnlineSessionEvents.SessionClosed;
            case OnlineSessionState.Kicked:
                return OnlineSessionEvents.SessionKicked;
            case OnlineSessionState.Expired:
                return OnlineSessionEvents.SessionExpired;
            default:
                // 状态机穷举（枚举 8 值），default 仅为编译完备性保留。
                return OnlineSessionEvents.SessionCreated;
        }
    }

    /// <summary>
    /// 取会话当前状态对应的时间戳（该状态没有专属时间戳时回退到建档时刻）。
    /// </summary>
    /// <param name="session">会话记录。</param>
    /// <returns>Unix 毫秒时间戳。</returns>
    private static long ResolveSessionOccurredTime(OnlineSession session)
    {
        switch (session.State)
        {
            case OnlineSessionState.Authenticated:
                return FirstNonZero(session.AuthenticatedAtTime, session.CreatedAtTime);
            case OnlineSessionState.Connected:
            case OnlineSessionState.Active:
                return FirstNonZero(session.ConnectedAtTime, session.CreatedAtTime);
            case OnlineSessionState.Reconnecting:
                return FirstNonZero(session.DisconnectAtTime, session.ConnectedAtTime, session.CreatedAtTime);
            case OnlineSessionState.Closed:
            case OnlineSessionState.Kicked:
            case OnlineSessionState.Expired:
                return FirstNonZero(session.ClosedAtTime, session.CreatedAtTime);
            default:
                return session.CreatedAtTime;
        }
    }

    /// <summary>
    /// 账本操作类型到既有资产事件常量的映射。
    /// <para>
    /// 词表只区分发放 / 扣除 / 调整三类（既有常量全集），故撤销与补发一并归入「调整」——
    /// 消费方按行摘要中的操作类型原值可分辨具体操作；为它们新增事件常量属跨仓词表变更，不在此处擅自扩展。
    /// </para>
    /// </summary>
    /// <param name="operation">账本操作类型。</param>
    /// <returns>对应的事件类型常量。</returns>
    private static string ResolveAssetEventType(OnlineGrantOperation operation)
    {
        switch (operation)
        {
            case OnlineGrantOperation.Grant:
                return OnlineAssetEvents.AssetGranted;
            case OnlineGrantOperation.Deduct:
                return OnlineAssetEvents.AssetDeducted;
            default:
                return OnlineAssetEvents.AssetAdjusted;
        }
    }

    /// <summary>
    /// 格式化带符号资产数额（正数补正号，便于运维一眼分辨增减）。
    /// </summary>
    /// <param name="delta">带符号变更数额。</param>
    /// <returns>格式化文本。</returns>
    private static string FormatDelta(long delta)
    {
        return delta > 0 ? "+" + delta.ToString(CultureInfo.InvariantCulture) : delta.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 拼装原因后缀（原因为空时不产生空括号）。
    /// </summary>
    /// <param name="reason">业务原因。</param>
    /// <returns>后缀文本。</returns>
    private static string BuildReasonSuffix(string reason)
    {
        return string.IsNullOrEmpty(reason) ? string.Empty : "（" + reason + "）";
    }

    /// <summary>
    /// 取第一个非零时间戳（用于「该状态时间戳缺失时回退」）。
    /// </summary>
    /// <param name="values">候选时间戳（按优先级）。</param>
    /// <returns>第一个非零值；全为零时返回 0。</returns>
    private static long FirstNonZero(params long[] values)
    {
        foreach (var value in values)
        {
            if (value > 0)
            {
                return value;
            }
        }

        return 0;
    }

    /// <summary>
    /// 毫秒时间戳换算为时间线行契约使用的 Unix 秒（非正值视为缺失，返回 0）。
    /// </summary>
    /// <param name="unixMilliseconds">Unix 毫秒时间戳。</param>
    /// <returns>Unix 秒。</returns>
    private static long ToSeconds(long unixMilliseconds)
    {
        return unixMilliseconds <= 0 ? 0 : unixMilliseconds / 1000L;
    }

    /// <summary>
    /// 判断来源记录是否落在观测作用域内（三键全等）。
    /// </summary>
    /// <param name="tenantId">记录租户标识。</param>
    /// <param name="appId">记录 App 标识。</param>
    /// <param name="serverId">记录区服标识。</param>
    /// <param name="scope">观测作用域。</param>
    /// <returns>在作用域内返回 <c>true</c>。</returns>
    private static bool IsInScope(long tenantId, long appId, long serverId, OnlineScope scope)
    {
        return tenantId == scope.TenantId && appId == scope.AppId && serverId == scope.ServerId;
    }

    /// <summary>
    /// 校验作用域（三键 + 玩家主体位；玩家标识只取自作用域，不从查询体重复读取）。
    /// </summary>
    /// <typeparam name="TData">结果负载类型。</typeparam>
    /// <param name="scope">生效作用域。</param>
    /// <returns>非法时返回失败结果；合法返回 <see langword="null"/>。</returns>
    private static OnlineResult<TData> ValidateScope<TData>(OnlineScope scope)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (scope.TenantId <= 0 || scope.AppId <= 0 || scope.ServerId <= 0)
        {
            return OnlineResult<TData>.Fail(OnlineErrorCode.ParameterInvalid, "玩家时间线查询必须携带完整作用域三键（TenantId / AppId / ServerId）");
        }

        if (scope.PlayerId <= 0)
        {
            return OnlineResult<TData>.Fail(OnlineErrorCode.ParameterInvalid, "玩家时间线查询必须绑定玩家主体位");
        }

        return null;
    }
}
