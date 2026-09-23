// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Collections.Generic;
using GameFrameX.Apps.Player.Mail;
using GameFrameX.Apps.Player.Mail.Entity;
using GameFrameX.Hotfix.Logic.Player.Mail;

namespace GameFrameX.Hotfix.Logic.Http.Mail
{
    /// <summary>
    /// Admin 查询运营邮件 Campaign 发布状态。
    /// 路由：GET /api/mailCampaign/query。
    /// <see cref="QueryMailCampaignRequest.CampaignId"/> 大于 0 时按 ID 精确查询单条；否则按过滤条件列表查询。
    /// </summary>
    [HttpMessageMapping(typeof(QueryMailCampaignHttpHandler))]
    [HttpMessageRequest(typeof(QueryMailCampaignRequest))]
    [HttpMessageResponse(typeof(QueryMailCampaignResponse))]
    [Description("Admin 查询运营邮件 Campaign 发布状态")]
    public sealed class QueryMailCampaignHttpHandler : BaseHttpHandler
    {
        /// <summary>
        /// 处理查询运营邮件 Campaign 发布状态的 HTTP 请求。CampaignId 大于 0 时按 ID 精确查询单条，不存在返回 CampaignNotFound；否则按状态、邮件类型、服务器、渠道、等级与创建时间等 AND 过滤条件列表查询并返回 Campaign 列表与总数。
        /// </summary>
        /// <remarks>
        /// Handles the HTTP request for querying mail campaign publish status. When CampaignId is greater than 0, queries a single campaign by id and returns CampaignNotFound when missing; otherwise lists campaigns with AND-combined filters (status, mail type, server, channel, level, creation time) and returns the campaign list with the total count.
        /// </remarks>
        /// <param name="context">HTTP 处理管线统一请求载荷 / Unified request payload for the HTTP handling pipeline</param>
        /// <returns>处理结果的 JSON 字符串 / JSON string of the processing result</returns>
        public override async Task<string> Action(HttpActionContext context)
        {
            var queryRequest = (QueryMailCampaignRequest)context.Request;
            var response = new QueryMailCampaignResponse { Code = MailCampaignErrorCode.Ok };

            if (queryRequest.CampaignId > 0)
            {
                if (MailCampaignRegistry.TryQuery(queryRequest.CampaignId, out var single))
                {
                    response.Campaigns.Add(single);
                    response.Total = 1;
                }
                else
                {
                    response.Code = MailCampaignErrorCode.CampaignNotFound;
                    response.Message = "Campaign 不存在";
                }

                return HttpJsonResultData<string>.SuccessString(JsonHelper.Serialize(response));
            }

            var list = MailCampaignRegistry.QueryAll(
                status: queryRequest.Status,
                mailType: queryRequest.MailType,
                serverId: queryRequest.ServerId,
                channelId: queryRequest.ChannelId,
                minLevel: queryRequest.MinLevel,
                createdFromUnixSeconds: queryRequest.CreatedFrom,
                createdToUnixSeconds: queryRequest.CreatedTo,
                limit: queryRequest.Limit);

            response.Campaigns = list;
            response.Total = list.Count;
            response.Message = "查询成功";
            return HttpJsonResultData<string>.SuccessString(JsonHelper.Serialize(response));
        }
    }

    /// <summary>
    /// 查询 Campaign 请求体。所有过滤条件为 AND 关系，空 / 0 表示不限。
    /// </summary>
    public sealed class QueryMailCampaignRequest : HttpMessageRequestBase
    {
        /// <summary>Campaign ID。大于 0 时精确查询单条，忽略其余过滤条件。</summary>
        [Description("Campaign ID")]
        public long CampaignId { get; set; }

        /// <summary>状态过滤；null 表示不限。</summary>
        [Description("状态过滤")]
        public MailCampaignStatus? Status { get; set; }

        /// <summary>类型过滤；null 表示不限。</summary>
        [Description("类型过滤")]
        public MailType? MailType { get; set; }

        /// <summary>服务器 ID 过滤；≤ 0 表示不限。</summary>
        [Description("服务器 ID")]
        public int ServerId { get; set; }

        /// <summary>渠道 ID 过滤；≤ 0 表示不限。</summary>
        [Description("渠道 ID")]
        public int ChannelId { get; set; }

        /// <summary>最低等级过滤；≤ 0 表示不限。</summary>
        [Description("最低等级")]
        public int MinLevel { get; set; }

        /// <summary>创建时间下限（Unix 秒，UTC）；≤ 0 表示不限。</summary>
        [Description("创建时间下限")]
        public long CreatedFrom { get; set; }

        /// <summary>创建时间上限（Unix 秒，UTC）；≤ 0 表示不限。</summary>
        [Description("创建时间上限")]
        public long CreatedTo { get; set; }

        /// <summary>返回条数上限；≤ 0 表示不限。</summary>
        [Description("返回条数上限")]
        public int Limit { get; set; }
    }

    /// <summary>
    /// 查询 Campaign 响应体。
    /// </summary>
    public sealed class QueryMailCampaignResponse : HttpMessageResponseBase
    {
        /// <summary>业务码。</summary>
        [Description("业务码")]
        public MailCampaignErrorCode Code { get; set; }

        /// <summary>命中 Campaign 列表。</summary>
        [Description("Campaign 列表")]
        public List<MailCampaignState> Campaigns { get; set; } = new List<MailCampaignState>();

        /// <summary>命中总数。</summary>
        [Description("命中总数")]
        public int Total { get; set; }

        /// <summary>提示信息。</summary>
        [Description("提示信息")]
        public string Message { get; set; }
    }
}
