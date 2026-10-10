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

using GameFrameX.Apps.ServerRole.Mail.Entity;
using GameFrameX.Proto.Proto;
using GameFrameX.Hotfix.Logic.ServerRole.Mail;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Mail 业务纯规则测试（C197 Phase 2）。
/// </summary>
/// <remarks>
/// 覆盖标题合法性边界、过期判定、状态机终态流转、失败重试上限与待投递选择语义。
/// </remarks>
public class MailRulesTests
{
    /// <summary>
    /// 标题合法性：空白与超长拒绝、边界长度接受。
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("跨服奖励", true)]
    public void IsTitleValid_BoundaryCases(string title, bool expected)
    {
        Assert.Equal(expected, MailRules.IsTitleValid(title));
    }

    /// <summary>
    /// 标题达到上限长度（64）仍合法，超限（65）非法。
    /// </summary>
    [Fact]
    public void IsTitleValid_LengthLimit()
    {
        Assert.True(MailRules.IsTitleValid(new string('a', MailRules.MaxTitleLength)));
        Assert.False(MailRules.IsTitleValid(new string('a', MailRules.MaxTitleLength + 1)));
    }

    /// <summary>
    /// 过期判定：未设置过期时间（0=永久）不过期；到过期时刻（now == expire）即过期。
    /// </summary>
    [Fact]
    public void IsExpired_ZeroExpireMeansForever()
    {
        var forever = new DeliveryMailState { ExpireUnixTime = 0 };
        Assert.False(MailRules.IsExpired(forever, 999999));

        var expiring = new DeliveryMailState { ExpireUnixTime = 1000 };
        Assert.False(MailRules.IsExpired(expiring, 999));
        Assert.True(MailRules.IsExpired(expiring, 1000));
    }

    /// <summary>
    /// 状态机：仅排队中可流转，全部终态不可再操作。
    /// </summary>
    [Theory]
    [InlineData(MailDeliveryStatus.Queued, true)]
    [InlineData(MailDeliveryStatus.Delivered, false)]
    [InlineData(MailDeliveryStatus.Dead, false)]
    [InlineData(MailDeliveryStatus.Expired, false)]
    public void CanTransit_OnlyQueued(MailDeliveryStatus status, bool expected)
    {
        Assert.Equal(expected, MailRules.CanTransit((int)status));
    }

    /// <summary>
    /// 失败重试：计数递增，第 4 次（超过上限 3）进入 Dead 终态。
    /// </summary>
    [Fact]
    public void ApplyFailure_DeadBeyondRetryLimit()
    {
        var mail = new DeliveryMailState { Status = MailRules.StatusQueued };
        for (var attempt = 1; attempt <= MailRules.MaxRetryCount; attempt++)
        {
            Assert.Equal(attempt, MailRules.ApplyFailure(mail));
            Assert.Equal(MailRules.StatusQueued, mail.Status);
        }

        Assert.Equal(MailRules.MaxRetryCount + 1, MailRules.ApplyFailure(mail));
        Assert.Equal(MailRules.StatusDead, mail.Status);
    }

    /// <summary>
    /// 待投递选择：过期邮件惰性置 Expired 且不返回，其余排队邮件按 ID 升序且受条数上限约束。
    /// </summary>
    [Fact]
    public void SelectPending_LazilyExpiresAndFilters()
    {
        var mails = new Dictionary<long, DeliveryMailState>();
        AddMail(mails, 100001, toPlayerId: 7, status: MailRules.StatusQueued, expireUnixTime: 500);
        AddMail(mails, 100002, toPlayerId: 7, status: MailRules.StatusQueued, expireUnixTime: 0);
        AddMail(mails, 100003, toPlayerId: 7, status: MailRules.StatusQueued, expireUnixTime: 0);
        AddMail(mails, 100004, toPlayerId: 7, status: MailRules.StatusDelivered, expireUnixTime: 0);
        AddMail(mails, 100005, toPlayerId: 8, status: MailRules.StatusQueued, expireUnixTime: 0);

        var pending = MailRules.SelectPending(mails, 7, now: 1000, limit: 100);
        Assert.Equal(new long[] { 100002, 100003 }, pending.Select(mail => mail.MailId));
        Assert.Equal(MailRules.StatusExpired, mails[100001].Status);
    }

    /// <summary>
    /// 查询条数归一化：&lt;=0 与超上限都按上限处理。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MailRules.MaxQueryLimit + 1)]
    public void ClampQueryLimit_NormalizesToMax(int limit)
    {
        Assert.Equal(MailRules.MaxQueryLimit, MailRules.ClampQueryLimit(limit));
    }

    /// <summary>
    /// 构造测试邮件并登记入字典。
    /// </summary>
    private static void AddMail(Dictionary<long, DeliveryMailState> mails, long mailId, long toPlayerId, int status, long expireUnixTime)
    {
        var mail = new DeliveryMailState
        {
            MailId = mailId,
            ToPlayerId = toPlayerId,
            Status = status,
            ExpireUnixTime = expireUnixTime,
        };
        mails[mailId] = mail;
    }
}
