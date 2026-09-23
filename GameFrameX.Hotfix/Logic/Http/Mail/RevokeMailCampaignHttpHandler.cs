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


using System;
using GameFrameX.Apps.Player.Mail;
using GameFrameX.Hotfix.Logic.Player.Mail;

namespace GameFrameX.Hotfix.Logic.Http.Mail
{
    /// <summary>
    /// Admin 撤回运营邮件 Campaign。
    /// 路由：POST /api/mailCampaign/revoke。
    /// 仅置 <see cref="MailCampaignState.Status"/> = Revoked 与时间戳（B3：撤回不回滚已发放资产）。
    /// </summary>
    [HttpMessageMapping(typeof(RevokeMailCampaignHttpHandler))]
    [HttpMessageRequest(typeof(RevokeMailCampaignRequest))]
    [HttpMessageResponse(typeof(RevokeMailCampaignResponse))]
    [Description("Admin 撤回运营邮件 Campaign")]
    public sealed class RevokeMailCampaignHttpHandler : BaseHttpHandler
    {
        /// <summary>
        /// 处理撤回运营邮件 Campaign 的 HTTP 请求。调用注册表撤回将状态置为 Revoked 并返回撤回时间（B3：已发放资产不回滚）；Campaign 不存在、已撤回不可重复撤回或其他失败时返回对应错误码的 JSON 响应。
        /// </summary>
        /// <remarks>
        /// Handles the HTTP request for revoking a mail campaign. Invokes the registry revoke to mark the campaign as Revoked and returns the revoked time (B3: granted assets are not rolled back); returns a JSON response with the corresponding error code when the campaign is missing, already revoked, or fails otherwise.
        /// </remarks>
        /// <param name="context">HTTP 处理管线统一请求载荷 / Unified request payload for the HTTP handling pipeline</param>
        /// <returns>处理结果的 JSON 字符串 / JSON string of the processing result</returns>
        public override async Task<string> Action(HttpActionContext context)
        {
            var revokeRequest = (RevokeMailCampaignRequest)context.Request;
            var response = new RevokeMailCampaignResponse { CampaignId = revokeRequest.CampaignId };

            var code = MailCampaignRegistry.Revoke(revokeRequest.CampaignId, revokeRequest.Operator, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            response.Code = code;
            switch (code)
            {
                case MailCampaignErrorCode.Ok:
                    response.Message = "撤回成功（已发放资产不回滚 B3）";
                    if (MailCampaignRegistry.TryQuery(revokeRequest.CampaignId, out var campaign))
                    {
                        response.RevokedAt = campaign.RevokedAt;
                    }

                    break;
                case MailCampaignErrorCode.CampaignNotFound:
                    response.Message = "Campaign 不存在";
                    break;
                case MailCampaignErrorCode.CampaignAlreadyRevoked:
                    response.Message = "Campaign 已撤回，不可重复撤回";
                    break;
                default:
                    response.Message = "撤回失败";
                    break;
            }

            return HttpJsonResultData<string>.SuccessString(JsonHelper.Serialize(response));
        }
    }

    /// <summary>
    /// 撤回 Campaign 请求体。
    /// </summary>
    public sealed class RevokeMailCampaignRequest : HttpMessageRequestBase
    {
        /// <summary>Campaign ID。</summary>
        [Required]
        [Range(1, long.MaxValue)]
        [Description("Campaign ID")]
        public long CampaignId { get; set; }

        /// <summary>撤回操作人（Admin 账号）。</summary>
        [Description("撤回操作人")]
        public string Operator { get; set; }
    }

    /// <summary>
    /// 撤回 Campaign 响应体。
    /// </summary>
    public sealed class RevokeMailCampaignResponse : HttpMessageResponseBase
    {
        /// <summary>业务码。</summary>
        [Description("业务码")]
        public MailCampaignErrorCode Code { get; set; }

        /// <summary>Campaign ID。</summary>
        [Description("CampaignId")]
        public long CampaignId { get; set; }

        /// <summary>撤回时间（Unix 秒，UTC）。撤回失败时为 0。</summary>
        [Description("撤回时间")]
        public long RevokedAt { get; set; }

        /// <summary>提示信息。</summary>
        [Description("提示信息")]
        public string Message { get; set; }
    }
}
