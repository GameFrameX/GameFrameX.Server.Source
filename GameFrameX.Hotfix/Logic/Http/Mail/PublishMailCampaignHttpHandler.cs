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
using System.Collections.Generic;
using GameFrameX.Apps.Player.Mail;
using GameFrameX.Apps.Player.Mail.Entity;
using GameFrameX.Hotfix.Logic.Player.Mail;

namespace GameFrameX.Hotfix.Logic.Http.Mail
{
    /// <summary>
    /// Admin 发布运营邮件 Campaign。
    /// 路由：POST /api/mailCampaign/publish。
    /// 校验通过后写入不可变 <see cref="MailCampaignState"/> 快照（B1：发布后主体字段不可修改）。
    /// </summary>
    [HttpMessageMapping(typeof(PublishMailCampaignHttpHandler), typeof(PublishMailCampaignRequest), typeof(PublishMailCampaignResponse))]
    [RequireHttpSignature]
    [Description("Admin 发布运营邮件 Campaign")]
    public sealed class PublishMailCampaignHttpHandler : BaseHttpHandler
    {
        /// <summary>
        /// 处理发布运营邮件 Campaign 的 HTTP 请求。校验通过后调用发布 / 覆盖草稿并返回 CampaignId、发布版本与发布时间；Campaign 已发布或已撤回导致主体字段不可修改（B1，InvalidOperationException）以及参数非法时返回对应错误码的 JSON 响应。
        /// </summary>
        /// <remarks>
        /// Handles the HTTP request for publishing a mail campaign. After validation passes, publishes or overwrites the draft and returns the campaign id, publish version, and publish time; returns a JSON response with the corresponding error code when the campaign is already published or revoked so its main fields cannot be modified (B1, InvalidOperationException) or a parameter is invalid.
        /// </remarks>
        /// <param name="context">HTTP 处理管线统一请求载荷 / Unified request payload for the HTTP handling pipeline</param>
        /// <returns>处理结果的 JSON 字符串 / JSON string of the processing result</returns>
        public override async Task<string> ActionAsync(HttpActionContext context)
        {
            var publishRequest = (PublishMailCampaignRequest)context.Request;
            var response = new PublishMailCampaignResponse();

            var state = BuildCampaignState(publishRequest);
            var code = MailCampaignRegistry.Validate(state);
            if (code != MailCampaignErrorCode.Ok)
            {
                response.Code = code;
                response.Message = "Campaign 参数校验失败";
                return HttpJsonResultData<string>.SuccessString(JsonHelper.Serialize(response));
            }

            try
            {
                var published = MailCampaignRegistry.PublishOrUpdate(state, publishRequest.Operator, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                response.Code = MailCampaignErrorCode.Ok;
                response.CampaignId = published.CampaignId;
                response.Version = published.PublishVersion;
                response.PublishedAt = published.PublishedAt;
                response.Message = "发布成功";
                return HttpJsonResultData<string>.SuccessString(JsonHelper.Serialize(response));
            }
            catch (InvalidOperationException)
            {
                response.Code = MailCampaignErrorCode.HasExist;
                response.Message = "Campaign 已发布或已撤回，主体字段不可修改（B1）";
                return HttpJsonResultData<string>.SuccessString(JsonHelper.Serialize(response));
            }
            catch (ArgumentException)
            {
                response.Code = MailCampaignErrorCode.InvalidCampaignParameter;
                response.Message = "Campaign 参数非法";
                return HttpJsonResultData<string>.SuccessString(JsonHelper.Serialize(response));
            }
        }

        private static MailCampaignState BuildCampaignState(PublishMailCampaignRequest request)
        {
            var state = new MailCampaignState
            {
                CampaignId = request.CampaignId,
                TemplateId = request.TemplateId,
                MailType = request.MailType,
                MinLevel = request.MinLevel,
                MaxLevel = request.MaxLevel,
                ExpireAttachmentPolicy = request.ExpireAttachmentPolicy,
                ExpireAt = request.ExpireAt,
                Titles = request.Titles ?? new List<MailLocalizedContent>(),
                Contents = request.Contents ?? new List<MailLocalizedContent>(),
                SenderNames = request.SenderNames ?? new List<MailLocalizedContent>(),
                Attachments = request.Attachments ?? new List<MailAttachmentState>(),
                ServerIds = request.ServerIds ?? new List<int>(),
                ChannelIds = request.ChannelIds ?? new List<int>(),
            };
            return state;
        }
    }
}
