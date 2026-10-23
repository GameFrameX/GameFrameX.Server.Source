// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers shall bear the responsibility themselves.
// ==========================================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Match.AdminApi;

/// <summary>
/// 对局域结算编排 admin 查询（query_match_settlement）：
/// 结算中对局清单 + 待重试发奖明细——Capability 包零 LogHelper 惯例下的可观测面。
/// <para>
/// 维护约束：只读 action（幂等包裹 = <c>false</c>，与 query_* 族只读先例一致）；无请求参数。
/// </para>
/// </summary>
/// <remarks>
/// The match-domain settlement orchestration admin query (query_match_settlement): the settling
/// match list plus the pending grant-retry details — the observability face under the capability
/// package's zero-LogHelper convention. Read-only action (idempotency wrap = false, consistent with
/// the read-only query_* precedent family); no request parameters.
/// </remarks>
public static class OnlineAdminMatchSettlementHandlers
{
    /// <summary>
    /// 注册结算编排查询 action（只读）。
    /// </summary>
    /// <param name="registry">动作注册表。</param>
    /// <param name="pipeline">结算管线（Register 期构造）。</param>
    public static void RegisterMatchSettlementQuery(AdminActionRegistry registry, OnlineMatchSettlementPipeline pipeline)
    {
        registry.Register("query_match_settlement", OnlineAdminApiAdapters.Wrap((request, scope, cancellationToken) => QueryMatchSettlementAsync(pipeline, cancellationToken)), false);
    }

    /// <summary>
    /// query_match_settlement：结算中清单 + 待重试发奖明细（快照投影，无状态变更）。
    /// </summary>
    /// <param name="pipeline">结算管线。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>查询响应。</returns>
    private static Task<object> QueryMatchSettlementAsync(OnlineMatchSettlementPipeline pipeline, CancellationToken cancellationToken)
    {
        if (pipeline == null)
        {
            return Task.FromResult<object>(new MatchSettlementQueryResponse());
        }

        var (settlingMatchIds, retries) = pipeline.QueryStatus();
        var items = new List<MatchSettlementRetryResponse>();
        foreach (var retry in retries)
        {
            items.Add(new MatchSettlementRetryResponse
            {
                MatchResultId = retry.MatchResultId,
                MatchId = retry.MatchId,
                FailedPlayerIds = retry.FailedPlayerIds == null ? new List<long>() : new List<long>(retry.FailedPlayerIds),
                Attempts = retry.Attempts,
            });
        }

        return Task.FromResult<object>(new MatchSettlementQueryResponse
        {
            SettlingMatchIds = settlingMatchIds == null ? new List<string>() : new List<string>(settlingMatchIds),
            RetryCount = items.Count,
            Retries = items,
        });
    }

    /// <summary>
    /// 结算编排查询响应（JSON 投影）。
    /// </summary>
    public sealed class MatchSettlementQueryResponse
    {
        /// <summary>
        /// 获取或设置结算中对局标识清单。
        /// </summary>
        public List<string> SettlingMatchIds { get; set; } = new List<string>();

        /// <summary>
        /// 获取或设置待重试发奖条数。
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// 获取或设置待重试发奖明细。
        /// </summary>
        public List<MatchSettlementRetryResponse> Retries { get; set; } = new List<MatchSettlementRetryResponse>();
    }

    /// <summary>
    /// 待重试发奖明细条目（JSON 投影）。
    /// </summary>
    public sealed class MatchSettlementRetryResponse
    {
        /// <summary>
        /// 获取或设置结算结果标识。
        /// </summary>
        public string MatchResultId { get; set; }

        /// <summary>
        /// 获取或设置对局标识。
        /// </summary>
        public string MatchId { get; set; }

        /// <summary>
        /// 获取或设置发奖失败玩家列表。
        /// </summary>
        public List<long> FailedPlayerIds { get; set; } = new List<long>();

        /// <summary>
        /// 获取或设置累计投递尝试次数。
        /// </summary>
        public int Attempts { get; set; }
    }
}
