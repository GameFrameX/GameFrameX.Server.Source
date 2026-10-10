// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
// 
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
// 
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
// 
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
// 
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using GameFrameX.Apps.ServerRole.Mail.Component;
using GameFrameX.Apps.ServerRole.Mail.Entity;
using GameFrameX.Foundation.Utility;

namespace GameFrameX.Hotfix.Logic.ServerRole.Mail;

/// <summary>
/// 邮件服务器（Mail Role，跨服投递队列，域号 650）业务组件代理。
/// </summary>
/// <remarks>
/// Mail Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点；与玩家邮件系统（域号 500）无关。
/// 业务规则判定一律委托 <see cref="MailRules"/>（纯函数，可单测）；
/// 本类只做状态编排：入队取号、待投递惰性过期过滤、投递成功 / 失败状态机流转。
/// </remarks>
public class MailComponentAgent : StateComponentAgent<MailComponent, MailState>
{
    /// <summary>
    /// 邮件入队：发件人为请求者（需登录）；标题校验后取号落库为排队状态。
    /// </summary>
    /// <param name="playerId">发件玩家ID。</param>
    /// <param name="request">入队请求。</param>
    /// <param name="response">入队响应。</param>
    public Task OnEnqueueAsync(long playerId, ReqMailEnqueue request, RespMailEnqueue response)
    {
        if (!MailRules.IsTitleValid(request.Title))
        {
            response.ErrorCode = (int)MailDeliveryErrorCode.TitleInvalid;
            return Task.CompletedTask;
        }

        var mail = new DeliveryMailState
        {
            MailId = State.NextMailId++,
            FromPlayerId = playerId,
            ToPlayerId = request.ToPlayerId,
            Title = request.Title,
            Body = request.Body,
            Status = MailRules.StatusQueued,
            RetryCount = 0,
            CreatedUnixTime = TimerHelper.UnixTimeSeconds(),
            ExpireUnixTime = request.ExpireUnixTime,
        };
        State.Mails[mail.MailId] = mail;
        response.MailId = mail.MailId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询收件人待投递邮件（参数键控，无需登录语义）：排队中且未过期的邮件摘要；过期邮件惰性置 Expired 且不返回。
    /// </summary>
    /// <param name="request">查询请求。</param>
    /// <param name="response">查询响应。</param>
    public Task OnQueryPendingAsync(ReqMailQueryPending request, RespMailQueryPending response)
    {
        var limit = MailRules.ClampQueryLimit(request.Limit);
        foreach (var mail in MailRules.SelectPending(State.Mails, request.ToPlayerId, TimerHelper.UnixTimeSeconds(), limit))
        {
            response.Mails.Add(ToDeliveryInfo(mail));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 标记邮件已投递（参数键控，无需登录语义）：仅排队中可流转，终态再操作拒绝。
    /// </summary>
    /// <param name="request">标记请求。</param>
    /// <param name="response">标记响应。</param>
    public Task OnMarkDeliveredAsync(ReqMailMarkDelivered request, RespMailMarkDelivered response)
    {
        if (!TryGetMail(request.MailId, out var mail))
        {
            response.ErrorCode = (int)MailDeliveryErrorCode.MailNotFound;
            return Task.CompletedTask;
        }

        if (!MailRules.CanTransit(mail.Status))
        {
            response.ErrorCode = (int)MailDeliveryErrorCode.NotDeliverable;
            return Task.CompletedTask;
        }

        mail.Status = MailRules.StatusDelivered;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 登记一次投递失败（参数键控，无需登录语义）：仅排队中可流转；重试计数 +1，超过上限进入 Dead 终态。
    /// </summary>
    /// <param name="request">登记请求。</param>
    /// <param name="response">登记响应。</param>
    public Task OnMarkFailedAsync(ReqMailMarkFailed request, RespMailMarkFailed response)
    {
        if (!TryGetMail(request.MailId, out var mail))
        {
            response.ErrorCode = (int)MailDeliveryErrorCode.MailNotFound;
            return Task.CompletedTask;
        }

        if (!MailRules.CanTransit(mail.Status))
        {
            response.ErrorCode = (int)MailDeliveryErrorCode.NotDeliverable;
            return Task.CompletedTask;
        }

        response.RetryCount = MailRules.ApplyFailure(mail);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询邮件状态。
    /// </summary>
    /// <param name="mailId">邮件ID。</param>
    /// <param name="mail">邮件状态。</param>
    /// <returns>存在返回 true。</returns>
    private bool TryGetMail(long mailId, out DeliveryMailState mail)
    {
        return State.Mails.TryGetValue(mailId, out mail);
    }

    /// <summary>
    /// 状态邮件 → 协议摘要载荷。
    /// </summary>
    /// <param name="mail">邮件状态。</param>
    /// <returns>协议摘要载荷。</returns>
    private static MailDeliveryInfo ToDeliveryInfo(DeliveryMailState mail)
    {
        return new MailDeliveryInfo
        {
            MailId = mail.MailId,
            FromPlayerId = mail.FromPlayerId,
            ToPlayerId = mail.ToPlayerId,
            Title = mail.Title,
            Status = (MailDeliveryStatus)mail.Status,
        };
    }
}
