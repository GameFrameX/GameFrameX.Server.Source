// ==========================================================================================
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
//   Any disputes or liabilities arising from secondary development based on this project
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
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Audit;

/// <summary>
/// 统一审计链路服务（vault:C9 S8.2：Admin 既有支付/奖励/邮件/兑换码/远程配置/处罚审计与
/// S8.1 受控操作审计接入统一链路 + 跨域检索；VC-8.3/VC-8.4 审计半边、VC-8.16 服务端半边）。
/// <para>
/// 维护约束（红线）：
/// ① **只写审计存储**——本服务不持有任何其它域服务/存储，不触碰 Online Actor 与资产状态（X3；
/// 「不得直写」由类型系统结构性保证，对齐 C101「只依赖读取面」在写入侧的镜像）；
/// ② **审计完整性**（VC-8.3 半边）：接入条目缺 操作者/原因/标识/类型/租户/App 或域未知 → 拒绝
/// 4001（宁拒毋缺——「关键操作无审计次数 = 0」的服务端结构性保障：不完整审计进不了链路）；
/// ③ **幂等**（VC-8.4 半边）：EventId 全局唯一，判重与落档在存储临界区内完成，重复接入回执
/// <c>IsDuplicate=true</c>、无副作用；
/// ④ **脱敏**（VC-8.16 半边）：落档前经 C93 <see cref="OnlineEventSanitizer"/> 生成 SanitizedFields，
/// 存储与检索自落档起结构性无明文敏感值；
/// ⑤ **检索锚定**：QueryAsync 的作用域只锚定 TenantId/AppId 两键——审计是跨区服检索域
/// （支付/远程配置等 App 级审计 ServerId=0），区服/玩家/操作者等维度走 <see cref="OnlineAuditQuery"/>
/// 可选过滤；scope.ServerId/scope.PlayerId 不参与审计过滤，防止误把跨域联查切割成单区服视角。
/// </para>
/// </summary>
public sealed class OnlineAuditService
{
    /// <summary>未指定页大小时使用的默认页大小。</summary>
    private const int DefaultPageSize = 50;

    /// <summary>页大小上限；超过即拒绝，防止消费方一次拉走全量审计。</summary>
    private const int MaxPageSize = 200;

    /// <summary>游标内部的字段分隔符（游标对消费方不透明，此分隔符属实现细节）。</summary>
    private const char CursorSeparator = '|';

    /// <summary>审计存储（唯一写入面）。</summary>
    private readonly IOnlineAuditStore _store;

    /// <summary>审计脱敏器（复用 C93 脱敏规则与敏感键集合）。</summary>
    private readonly OnlineEventSanitizer _sanitizer;

    /// <summary>
    /// 初始化 <see cref="OnlineAuditService"/>。
    /// </summary>
    /// <param name="store">审计存储。</param>
    /// <param name="sanitizer">审计脱敏器；传 <see langword="null"/> 时使用 C93 默认敏感键集合。</param>
    public OnlineAuditService(IOnlineAuditStore store, OnlineEventSanitizer sanitizer = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sanitizer = sanitizer ?? new OnlineEventSanitizer();
    }

    /// <summary>
    /// 接入一条审计条目（校验 → 脱敏 → 幂等落档）。
    /// </summary>
    /// <param name="entry">接入条目。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>接入回执（重复接入返回 <c>IsDuplicate=true</c>，与首次接入同构成功）；
    /// 完整性校验失败返回 <see cref="OnlineErrorCode.ParameterInvalid"/>。</returns>
    public async Task<OnlineResult<OnlineAuditIngestOutcome>> IngestAsync(OnlineAuditEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        var failure = ValidateEntry(entry);
        if (failure != null)
        {
            return failure;
        }

        // 脱敏桥接：一次性构造 C93 信封调用既有脱敏器，取脱敏字段投影（零 C93 公开面改动，VC-8.16）。
        var sanitizedView = _sanitizer.CreateAuditView(ToEnvelope(entry));
        var record = new OnlineAuditRecord
        {
            EventId = entry.EventId,
            Domain = entry.Domain,
            EventType = entry.EventType,
            OccurredTime = entry.OccurredTime,
            TenantId = entry.TenantId,
            AppId = entry.AppId,
            ServerId = entry.ServerId,
            PlayerId = entry.PlayerId,
            OperatorId = entry.OperatorId,
            OperatorName = entry.OperatorName,
            Reason = entry.Reason,
            Source = entry.Source,
            CorrelationId = entry.CorrelationId,
            SanitizedFields = sanitizedView.SanitizedFields,
        };

        var isNew = await _store.AppendAsync(record, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineAuditIngestOutcome>.Ok(new OnlineAuditIngestOutcome(entry.EventId, !isNew));
    }

    /// <summary>
    /// 跨域检索统一审计（只读；按作用域两键锚定 + 可选维度过滤 + keyset 全序分页）。
    /// </summary>
    /// <param name="scope">生效作用域（只锚定 TenantId/AppId 两键，须均大于 0；区服/玩家走查询条件）。</param>
    /// <param name="query">查询条件；传 <see langword="null"/> 等价于默认条件（全部域、首页、默认页大小）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>审计分页结果；作用域两键缺失、页大小越界、游标非法返回 <see cref="OnlineErrorCode.ParameterInvalid"/>。
    /// 无任何数据时返回**空行集**（与跨作用域读取同构，不是错误）。</returns>
    public async Task<OnlineResult<OnlineAuditPage>> QueryAsync(OnlineScope scope, OnlineAuditQuery query = null, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (scope.TenantId <= 0 || scope.AppId <= 0)
        {
            return OnlineResult<OnlineAuditPage>.Fail(OnlineErrorCode.ParameterInvalid, "统一审计检索必须锚定租户与 App（TenantId / AppId 大于 0；区服与玩家走查询条件可选过滤）");
        }

        var request = query ?? new OnlineAuditQuery();

        var pageSize = request.PageSize <= 0 ? DefaultPageSize : request.PageSize;
        if (pageSize > MaxPageSize)
        {
            return OnlineResult<OnlineAuditPage>.Fail(OnlineErrorCode.ParameterInvalid, "页大小必须在 1～200 之间");
        }

        var hasCursor = !string.IsNullOrEmpty(request.Cursor);
        long cursorOccurredTime = 0;
        string cursorEventId = null;
        if (hasCursor && !TryDecodeCursor(request.Cursor, out cursorOccurredTime, out cursorEventId))
        {
            return OnlineResult<OnlineAuditPage>.Fail(OnlineErrorCode.ParameterInvalid, "游标格式非法（不透明令牌只回传不构造）");
        }

        // 反预言同构：存储按 (Tenant, App) 索引结构性隔离，跨作用域查询与「无数据」都得到空行集。
        var records = await _store.ListAsync(scope.TenantId, scope.AppId, cancellationToken).ConfigureAwait(false);
        var selected = SelectRecords(records, request, hasCursor, cursorOccurredTime, cursorEventId);
        selected.Sort(CompareRecords);

        return OnlineResult<OnlineAuditPage>.Ok(BuildPage(selected, pageSize));
    }

    /// <summary>
    /// 选出落在全部可选过滤内且位于游标之后的记录（过滤在前、游标筛选在全序上生效）。
    /// <para>
    /// 返回结果未排序，全序排序由调用方执行。
    /// </para>
    /// </summary>
    /// <param name="records">全量候选记录（存储半边可能返回 null，等价于空集）。</param>
    /// <param name="request">查询条件。</param>
    /// <param name="hasCursor">是否携带游标。</param>
    /// <param name="cursorOccurredTime">游标行的发生时刻（UTC 毫秒）。</param>
    /// <param name="cursorEventId">游标行的审计标识。</param>
    /// <returns>命中记录集合（未排序）。</returns>
    private static List<OnlineAuditRecord> SelectRecords(IReadOnlyList<OnlineAuditRecord> records, OnlineAuditQuery request, bool hasCursor, long cursorOccurredTime, string cursorEventId)
    {
        var selected = new List<OnlineAuditRecord>();
        if (records == null)
        {
            return selected;
        }

        foreach (var record in records)
        {
            if (record == null || !MatchesFilter(record, request))
            {
                continue;
            }

            if (hasCursor && !IsAfterCursor(record, cursorOccurredTime, cursorEventId))
            {
                continue;
            }

            selected.Add(record);
        }

        return selected;
    }

    /// <summary>
    /// 按全序切页并组装分页结果：取前 <paramref name="pageSize"/> 行，有更多行时以本页末行编码下一页游标。
    /// </summary>
    /// <param name="selected">已按全序排序的候选记录。</param>
    /// <param name="pageSize">页大小（已校验在 1～<see cref="MaxPageSize"/> 之间）。</param>
    /// <returns>组装好的审计分页结果。</returns>
    private static OnlineAuditPage BuildPage(List<OnlineAuditRecord> selected, int pageSize)
    {
        var page = new List<OnlineAuditRecord>(pageSize);
        foreach (var record in selected)
        {
            if (page.Count >= pageSize)
            {
                break;
            }

            page.Add(record);
        }

        var hasMore = selected.Count > page.Count;
        var nextCursor = hasMore ? EncodeCursor(page[page.Count - 1]) : string.Empty;

        return new OnlineAuditPage(page, new OnlinePageCursor(nextCursor, hasMore));
    }

    /// <summary>
    /// 校验接入条目的审计完整性（VC-8.3 半边：缺任一必填维度拒绝——不完整的审计进不了链路）。
    /// </summary>
    /// <param name="entry">接入条目。</param>
    /// <returns>非法时返回失败结果；合法返回 <see langword="null"/>。</returns>
    private static OnlineResult<OnlineAuditIngestOutcome> ValidateEntry(OnlineAuditEntry entry)
    {
        if (!OnlineAuditDomain.IsKnown(entry.Domain))
        {
            return OnlineResult<OnlineAuditIngestOutcome>.Fail(OnlineErrorCode.ParameterInvalid, "审计域必须在统一审计域词表内（Payment/Reward/Mail/RedeemCode/RemoteConfig/Penalty/Operation）");
        }

        if (string.IsNullOrEmpty(entry.EventId))
        {
            return OnlineResult<OnlineAuditIngestOutcome>.Fail(OnlineErrorCode.ParameterInvalid, "审计标识（EventId）必填——它是幂等去重键，缺失无法保证重复接入不重复落档");
        }

        if (string.IsNullOrEmpty(entry.EventType))
        {
            return OnlineResult<OnlineAuditIngestOutcome>.Fail(OnlineErrorCode.ParameterInvalid, "事件/操作类型（EventType）必填");
        }

        if (string.IsNullOrEmpty(entry.OperatorId))
        {
            return OnlineResult<OnlineAuditIngestOutcome>.Fail(OnlineErrorCode.ParameterInvalid, "操作者（OperatorId）必填——审计完整性要求含操作者（VC-8.3）");
        }

        if (string.IsNullOrEmpty(entry.Reason))
        {
            return OnlineResult<OnlineAuditIngestOutcome>.Fail(OnlineErrorCode.ParameterInvalid, "操作原因（Reason）必填——审计完整性要求含原因（VC-8.3）");
        }

        if (entry.TenantId <= 0 || entry.AppId <= 0)
        {
            return OnlineResult<OnlineAuditIngestOutcome>.Fail(OnlineErrorCode.ParameterInvalid, "审计条目必须携带租户与 App（TenantId / AppId 大于 0）");
        }

        if (entry.ServerId < 0 || entry.PlayerId < 0)
        {
            return OnlineResult<OnlineAuditIngestOutcome>.Fail(OnlineErrorCode.ParameterInvalid, "区服与玩家标识不可为负（0 分别表示 App 级操作与系统级操作）");
        }

        return null;
    }

    /// <summary>
    /// 把接入条目桥接为 C93 事件信封（仅为复用 <see cref="OnlineEventSanitizer.CreateAuditView"/> 的脱敏判定，
    /// 信封不发布、不传输；作用域字段与语义投影原样承载）。
    /// </summary>
    /// <param name="entry">接入条目。</param>
    /// <returns>一次性信封。</returns>
    private static OnlineEvent ToEnvelope(OnlineAuditEntry entry)
    {
        return new OnlineEvent
        {
            EventId = entry.EventId,
            EventType = entry.EventType,
            OccurredTime = entry.OccurredTime,
            SchemaVersion = 1,
            TenantId = entry.TenantId,
            AppId = entry.AppId,
            ServerId = entry.ServerId,
            PlayerId = entry.PlayerId,
            Source = entry.Source,
            CorrelationId = entry.CorrelationId,
            Payload = ReadOnlyMemory<byte>.Empty,
            PayloadAuditFields = entry.PayloadAuditFields,
        };
    }

    /// <summary>
    /// 判断记录是否落在全部可选过滤内（合取；域集合为析取——跨域联查语义）。
    /// </summary>
    /// <param name="record">待判定记录。</param>
    /// <param name="query">查询条件。</param>
    /// <returns>命中过滤返回 <c>true</c>。</returns>
    private static bool MatchesFilter(OnlineAuditRecord record, OnlineAuditQuery query)
    {
        if (!MatchesDomainFilter(record, query.Domains))
        {
            return false;
        }

        if (!MatchesDimensionFilter(record, query))
        {
            return false;
        }

        return MatchesTimeRangeFilter(record, query);
    }

    /// <summary>
    /// 判定域集合过滤是否命中（析取——跨域联查语义；集合为空即不限域）。
    /// </summary>
    /// <param name="record">待判定记录。</param>
    /// <param name="domains">域过滤集合。</param>
    /// <returns>命中返回 <c>true</c>。</returns>
    private static bool MatchesDomainFilter(OnlineAuditRecord record, IReadOnlyList<string> domains)
    {
        if (domains == null || domains.Count == 0)
        {
            return true;
        }

        foreach (var domain in domains)
        {
            if (string.Equals(record.Domain, domain, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判定玩家 / 操作者 / 事件类型 / 关联标识 / 区服五个维度过滤是否命中（合取；各维度为空表示不过滤）。
    /// </summary>
    /// <param name="record">待判定记录。</param>
    /// <param name="query">查询条件。</param>
    /// <returns>命中返回 <c>true</c>。</returns>
    private static bool MatchesDimensionFilter(OnlineAuditRecord record, OnlineAuditQuery query)
    {
        if (query.PlayerId.HasValue && record.PlayerId != query.PlayerId.Value)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(query.OperatorId) && !string.Equals(record.OperatorId, query.OperatorId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(query.EventType) && !string.Equals(record.EventType, query.EventType, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(query.CorrelationId) && !string.Equals(record.CorrelationId, query.CorrelationId, StringComparison.Ordinal))
        {
            return false;
        }

        if (query.ServerId.HasValue && record.ServerId != query.ServerId.Value)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 判定发生时刻时间窗过滤是否命中（合取闭区间 <c>[StartTime, EndTime]</c>；两端为空表示不过滤）。
    /// </summary>
    /// <param name="record">待判定记录。</param>
    /// <param name="query">查询条件。</param>
    /// <returns>命中返回 <c>true</c>。</returns>
    private static bool MatchesTimeRangeFilter(OnlineAuditRecord record, OnlineAuditQuery query)
    {
        if (query.StartTime.HasValue && record.OccurredTime < query.StartTime.Value)
        {
            return false;
        }

        if (query.EndTime.HasValue && record.OccurredTime > query.EndTime.Value)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 判断记录是否严格位于游标之后（全序：(发生时刻倒序, 审计标识序数升序)）。
    /// </summary>
    /// <param name="record">待判定记录。</param>
    /// <param name="cursorOccurredTime">游标行的发生时刻（UTC 毫秒）。</param>
    /// <param name="cursorEventId">游标行的审计标识。</param>
    /// <returns>位于游标之后返回 <c>true</c>。</returns>
    private static bool IsAfterCursor(OnlineAuditRecord record, long cursorOccurredTime, string cursorEventId)
    {
        if (record.OccurredTime != cursorOccurredTime)
        {
            return record.OccurredTime < cursorOccurredTime;
        }

        return string.CompareOrdinal(record.EventId, cursorEventId) > 0;
    }

    /// <summary>
    /// 全序比较：发生时刻倒序，同一毫秒内按审计标识序数升序（保证分页稳定）。
    /// </summary>
    /// <param name="left">左值。</param>
    /// <param name="right">右值。</param>
    /// <returns>比较结果。</returns>
    private static int CompareRecords(OnlineAuditRecord left, OnlineAuditRecord right)
    {
        if (left.OccurredTime != right.OccurredTime)
        {
            return right.OccurredTime.CompareTo(left.OccurredTime);
        }

        return string.CompareOrdinal(left.EventId, right.EventId);
    }

    /// <summary>
    /// 编码游标（不透明令牌：消费方只回传不构造）。
    /// </summary>
    /// <param name="record">本页最后一条记录。</param>
    /// <returns>游标令牌。</returns>
    private static string EncodeCursor(OnlineAuditRecord record)
    {
        return record.OccurredTime.ToString(CultureInfo.InvariantCulture) + CursorSeparator + record.EventId;
    }

    /// <summary>
    /// 解析游标；格式非法（缺分隔符 / 时刻非数 / 审计标识为空）返回 <c>false</c>，由调用方映射为参数非法。
    /// </summary>
    /// <param name="cursor">游标令牌。</param>
    /// <param name="occurredTime">解析出的发生时刻（UTC 毫秒）。</param>
    /// <param name="eventId">解析出的审计标识。</param>
    /// <returns>解析成功返回 <c>true</c>。</returns>
    private static bool TryDecodeCursor(string cursor, out long occurredTime, out string eventId)
    {
        occurredTime = 0;
        eventId = string.Empty;

        var separator = cursor.IndexOf(CursorSeparator);
        if (separator <= 0 || separator == cursor.Length - 1)
        {
            return false;
        }

        if (!long.TryParse(cursor.Substring(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out occurredTime))
        {
            return false;
        }

        eventId = cursor.Substring(separator + 1);
        return true;
    }
}
