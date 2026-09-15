// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

using System.Collections.Generic;

namespace GameFrameX.Online.GameEvents;

/// <summary>
/// 统一 Game Event Schema（vault:C8 S7.5，Server 半边）：17 个标准事件名的登记表 + 版本策略。
/// <para>
/// 维护约束（红线）：
/// (1) **登记表是唯一事实来源**——分类、版本与必需字段只在本表声明，校验器与投影器都从本表读取，
/// 三处各写一份必然漂移；<see cref="All"/> 与 <see cref="OnlineGameEventName"/> 的常量集合必须一一对应
/// （守护测试锁定，漏登记 = 该事件名投递时被判未知）；
/// (2) **版本策略**：版本号只增不减；投递版本 <c>1..CurrentVersion</c> 均受理（新旧并存期），
/// 超出范围即拒绝；破坏性变更走「升版本 + 登记表保留旧版本声明」，非破坏性变更不升版本；
/// (3) 本表只登记，**不定义第二套信封**——事件一律走 C93 <see cref="Events.OnlineEvent"/>；
/// (4) 本 change 只登记 17 个事件名的 Schema，投影只覆盖六类中的 Server 半边可得事件
/// （Install / Register / MailOpen 由客户端 SDK 与邮件域投递，本 change 不投影）。
/// </para>
/// </summary>
public static class OnlineGameEventSchema
{
    /// <summary>统一游戏事件的事件来源标识（与 <c>Online.Season.*</c> / <c>Online.Tournament.*</c> 域事件区分）。</summary>
    public const string Source = "online-game-event";

    /// <summary>登记表（事件名 → 登记项；<see cref="Dictionary{TKey,TValue}"/> 只读语义，进程内构造一次）。</summary>
    private static readonly Dictionary<string, OnlineGameEventDescriptor> Registry = BuildRegistry();

    /// <summary>登记表的有序投影（按事件名序，供遍历与守护测试比对）。</summary>
    private static readonly List<OnlineGameEventDescriptor> OrderedDescriptors = BuildOrderedDescriptors();

    /// <summary>
    /// 获取全部登记项（有序；顺序稳定，供指标与守护测试遍历）。
    /// </summary>
    public static IReadOnlyList<OnlineGameEventDescriptor> All
    {
        get { return OrderedDescriptors; }
    }

    /// <summary>
    /// 按事件名查找登记项。
    /// </summary>
    /// <param name="eventName">事件名。</param>
    /// <returns>登记项；未登记返回 null。</returns>
    public static OnlineGameEventDescriptor Find(string eventName)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            return null;
        }

        return Registry.TryGetValue(eventName, out var descriptor) ? descriptor : null;
    }

    /// <summary>
    /// 判定事件名是否已登记。
    /// </summary>
    /// <param name="eventName">事件名。</param>
    /// <returns>已登记返回 <c>true</c>。</returns>
    public static bool Contains(string eventName)
    {
        return Find(eventName) != null;
    }

    /// <summary>
    /// 构造登记表（17 个标准事件的分类 / 版本 / 必需字段；全部当前版本 = 1）。
    /// </summary>
    /// <returns>登记表。</returns>
    private static Dictionary<string, OnlineGameEventDescriptor> BuildRegistry()
    {
        var descriptors = new Dictionary<string, OnlineGameEventDescriptor>
        {
            { OnlineGameEventName.Install, new OnlineGameEventDescriptor(OnlineGameEventName.Install, OnlineGameEventCategory.Login, 1, "DeviceId") },
            { OnlineGameEventName.Register, new OnlineGameEventDescriptor(OnlineGameEventName.Register, OnlineGameEventCategory.Login, 1, "PlayerId") },
            { OnlineGameEventName.Login, new OnlineGameEventDescriptor(OnlineGameEventName.Login, OnlineGameEventCategory.Login, 1, "PlayerId", "DeviceId") },
            { OnlineGameEventName.SessionStart, new OnlineGameEventDescriptor(OnlineGameEventName.SessionStart, OnlineGameEventCategory.Login, 1, "SessionId", "PlayerId") },
            { OnlineGameEventName.SessionEnd, new OnlineGameEventDescriptor(OnlineGameEventName.SessionEnd, OnlineGameEventCategory.Login, 1, "SessionId", "PlayerId", "EndReason") },
            { OnlineGameEventName.MatchQueue, new OnlineGameEventDescriptor(OnlineGameEventName.MatchQueue, OnlineGameEventCategory.Matchmaking, 1, "TicketId", "Mode", "PlayerCount") },
            { OnlineGameEventName.MatchStart, new OnlineGameEventDescriptor(OnlineGameEventName.MatchStart, OnlineGameEventCategory.Match, 1, "MatchId", "Mode") },
            { OnlineGameEventName.MatchEnd, new OnlineGameEventDescriptor(OnlineGameEventName.MatchEnd, OnlineGameEventCategory.Match, 1, "MatchId", "MatchResultId", "Outcome") },
            { OnlineGameEventName.Win, new OnlineGameEventDescriptor(OnlineGameEventName.Win, OnlineGameEventCategory.Match, 1, "MatchId", "PlayerId") },
            { OnlineGameEventName.Lose, new OnlineGameEventDescriptor(OnlineGameEventName.Lose, OnlineGameEventCategory.Match, 1, "MatchId", "PlayerId") },
            { OnlineGameEventName.Abandon, new OnlineGameEventDescriptor(OnlineGameEventName.Abandon, OnlineGameEventCategory.Match, 1, "MatchId", "PlayerId") },
            { OnlineGameEventName.RewardGrant, new OnlineGameEventDescriptor(OnlineGameEventName.RewardGrant, OnlineGameEventCategory.Reward, 1, "TransactionId", "PlayerId", "ChangeSource") },
            { OnlineGameEventName.CurrencySpend, new OnlineGameEventDescriptor(OnlineGameEventName.CurrencySpend, OnlineGameEventCategory.Reward, 1, "TransactionId", "PlayerId", "ChangeSource") },
            { OnlineGameEventName.Purchase, new OnlineGameEventDescriptor(OnlineGameEventName.Purchase, OnlineGameEventCategory.Payment, 1, "TransactionId", "PlayerId", "BusinessOrderId") },
            { OnlineGameEventName.MailOpen, new OnlineGameEventDescriptor(OnlineGameEventName.MailOpen, OnlineGameEventCategory.Reward, 1, "PlayerId", "MailId") },
            { OnlineGameEventName.ChatReport, new OnlineGameEventDescriptor(OnlineGameEventName.ChatReport, OnlineGameEventCategory.Report, 1, "ReportId", "ReporterId", "ReportedPlayerId") },
            { OnlineGameEventName.Penalty, new OnlineGameEventDescriptor(OnlineGameEventName.Penalty, OnlineGameEventCategory.Report, 1, "PunishmentId", "PlayerId", "Kind") },
        };
        return descriptors;
    }

    /// <summary>
    /// 构造登记表的有序投影（按事件名序）。
    /// </summary>
    /// <returns>有序登记项列表。</returns>
    private static List<OnlineGameEventDescriptor> BuildOrderedDescriptors()
    {
        var descriptors = new List<OnlineGameEventDescriptor>();
        var names = new List<string>(Registry.Keys);
        names.Sort(System.StringComparer.Ordinal);
        foreach (var name in names)
        {
            descriptors.Add(Registry[name]);
        }

        return descriptors;
    }
}
