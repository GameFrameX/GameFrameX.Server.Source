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

using System;
using System.Collections.Generic;
using System.Linq;
using GameFrameX.Apps.ServerRole.Mail.Entity;

namespace GameFrameX.Hotfix.Logic.ServerRole.Mail;

/// <summary>
/// 邮件业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 承载与存储无关的跨服投递规则：标题合法性、过期判定、状态机迁移、失败重试上限、待投递选择。
/// Agent 只做状态编排，规则判定一律走本类（<c>MailCampaignFilter</c> 先例）。
/// </remarks>
internal static class MailRules
{
    /// <summary>
    /// 邮件标题长度上限（字符）。
    /// </summary>
    public const int MaxTitleLength = 64;

    /// <summary>
    /// 投递失败重试次数上限：计数超过该值（即第 4 次失败）进入 Dead 终态。
    /// </summary>
    public const int MaxRetryCount = 3;

    /// <summary>
    /// 排队待投递状态数值（协议层 <see cref="MailDeliveryStatus"/>.Queued）。
    /// </summary>
    public const int StatusQueued = (int)MailDeliveryStatus.Queued;

    /// <summary>
    /// 已投递终态数值（协议层 <see cref="MailDeliveryStatus"/>.Delivered）。
    /// </summary>
    public const int StatusDelivered = (int)MailDeliveryStatus.Delivered;

    /// <summary>
    /// 重试超限死亡终态数值（协议层 <see cref="MailDeliveryStatus"/>.Dead）。
    /// </summary>
    public const int StatusDead = (int)MailDeliveryStatus.Dead;

    /// <summary>
    /// 已过期终态数值（协议层 <see cref="MailDeliveryStatus"/>.Expired）。
    /// </summary>
    public const int StatusExpired = (int)MailDeliveryStatus.Expired;

    /// <summary>
    /// 单次查询条数上限。
    /// </summary>
    public const int MaxQueryLimit = 100;

    /// <summary>
    /// 判定邮件标题是否合法：非空白且不超过长度上限。
    /// </summary>
    /// <param name="title">邮件标题。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsTitleValid(string title)
    {
        return !string.IsNullOrWhiteSpace(title) && title.Length <= MaxTitleLength;
    }

    /// <summary>
    /// 判定邮件是否已过期：设置了过期时间（&gt;0）且已到过期时刻。
    /// </summary>
    /// <param name="mail">邮件状态。</param>
    /// <param name="now">当前 Unix 秒（由调用方注入）。</param>
    /// <returns>已过期返回 true。</returns>
    public static bool IsExpired(DeliveryMailState mail, long now)
    {
        return mail.ExpireUnixTime > 0 && now >= mail.ExpireUnixTime;
    }

    /// <summary>
    /// 判定邮件状态是否允许继续流转：仅排队中（Queued）可被投递 / 标记失败 / 过期。
    /// </summary>
    /// <param name="status">当前状态数值。</param>
    /// <returns>可流转返回 true。</returns>
    public static bool CanTransit(int status)
    {
        return status == StatusQueued;
    }

    /// <summary>
    /// 登记一次投递失败：重试计数 +1，超过上限进入 Dead 终态。
    /// </summary>
    /// <param name="mail">邮件状态（就地更新）。</param>
    /// <returns>登记后的重试计数。</returns>
    public static int ApplyFailure(DeliveryMailState mail)
    {
        mail.RetryCount++;
        if (mail.RetryCount > MaxRetryCount)
        {
            mail.Status = StatusDead;
        }

        return mail.RetryCount;
    }

    /// <summary>
    /// 归一化查询条数：&lt;=0 或超上限按上限处理。
    /// </summary>
    /// <param name="limit">请求条数。</param>
    /// <returns>归一化后的条数。</returns>
    public static int ClampQueryLimit(int limit)
    {
        return limit <= 0 ? MaxQueryLimit : Math.Min(limit, MaxQueryLimit);
    }

    /// <summary>
    /// 选择收件人的待投递邮件：惰性将过期邮件置 Expired（不返回），其余排队邮件按邮件ID升序至多 limit 条。
    /// </summary>
    /// <param name="mails">全部邮件状态（就地更新过期终态）。</param>
    /// <param name="toPlayerId">收件玩家ID。</param>
    /// <param name="now">当前 Unix 秒（由调用方注入）。</param>
    /// <param name="limit">条数上限（调用前先归一化）。</param>
    /// <returns>待投递邮件列表。</returns>
    public static List<DeliveryMailState> SelectPending(Dictionary<long, DeliveryMailState> mails, long toPlayerId, long now, int limit)
    {
        var pending = new List<DeliveryMailState>();
        foreach (var mail in mails.Values)
        {
            if (mail.ToPlayerId != toPlayerId)
            {
                continue;
            }

            if (mail.Status != StatusQueued)
            {
                continue;
            }

            if (IsExpired(mail, now))
            {
                mail.Status = StatusExpired;
                continue;
            }

            pending.Add(mail);
        }

        pending.Sort((left, right) => left.MailId.CompareTo(right.MailId));
        return pending.Take(limit).ToList();
    }
}
