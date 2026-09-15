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

namespace GameFrameX.Online.Matchmaking;

/// <summary>
/// 对局列表项（vault:C5 S4.4：可被发现和加入的容器 / 对外展示的房间与对局摘要）。
/// <para>
/// 维护约束（红线）：本类型只承载「展示与加入」语义，**不**承担 Party 的成员关系，也**不**承担
/// Match 的对局状态（vault:C5 概念边界表：Room 不直接承担 Party、Matchmaker 和 Match 的全部职责）。
/// <see cref="Password"/> 属敏感字段：只参与加入判定，绝不出现在查询结果、事件载荷或审计字段中
/// （沿用 C93 脱敏要求）。
/// </para>
/// </summary>
public sealed class OnlineMatchListing
{
    /// <summary>
    /// 获取或设置列表项标识。
    /// </summary>
    public string ListingId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识（作用域隔离键）。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识（作用域隔离键）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置区服标识（作用域隔离键）。
    /// </summary>
    public long ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置来源队伍标识（非队伍来源为空字符串）。
    /// </summary>
    public string PartyId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置归属玩家标识（发布者）。
    /// </summary>
    public long OwnerPlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置展示名称。
    /// </summary>
    public string Name
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置玩法模式。
    /// </summary>
    public int Mode
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置区域。
    /// </summary>
    public int Region
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置加入策略。
    /// </summary>
    public OnlineJoinPolicy JoinPolicy
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置加入密码（仅 <see cref="OnlineJoinPolicy.Password"/> 有效；不对外输出）。
    /// </summary>
    public string Password
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置邀请白名单（仅 <see cref="OnlineJoinPolicy.InviteOnly"/> 有效；发布时登记，不对外输出）。
    /// </summary>
    public List<long> InvitedPlayerIds
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置标签集合（发现过滤用）。
    /// </summary>
    public List<string> Tags
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置容量上限。
    /// </summary>
    public int Capacity
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置已加入人数。
    /// </summary>
    public int JoinedCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置列表项状态。
    /// </summary>
    public OnlineMatchListingState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置创建时刻（UTC 毫秒）。
    /// </summary>
    public long CreatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 复制列表项（存储层防御性深拷贝使用；标签集合逐项复制）。
    /// </summary>
    /// <returns>列表项副本。</returns>
    public OnlineMatchListing Copy()
    {
        var tags = new List<string>();
        if (Tags != null)
        {
            tags.AddRange(Tags);
        }

        var invited = new List<long>();
        if (InvitedPlayerIds != null)
        {
            invited.AddRange(InvitedPlayerIds);
        }

        return new OnlineMatchListing
        {
            ListingId = ListingId,
            TenantId = TenantId,
            AppId = AppId,
            ServerId = ServerId,
            PartyId = PartyId,
            OwnerPlayerId = OwnerPlayerId,
            Name = Name,
            Mode = Mode,
            Region = Region,
            JoinPolicy = JoinPolicy,
            Password = Password,
            InvitedPlayerIds = invited,
            Tags = tags,
            Capacity = Capacity,
            JoinedCount = JoinedCount,
            State = State,
            CreatedAtTime = CreatedAtTime,
        };
    }
}
