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

namespace GameFrameX.Online.Social;

/// <summary>
/// 聊天频道（vault:C7 S6.5：频道 = 消息的归属与权限边界）。
/// <para>
/// 维护约束（频道标识是**确定性派生**的，不是随机生成）：标识由「作用域 + 类型 + 绑定主体」派生
/// （见 <see cref="OnlineChatService.BuildChannelId"/>），因此 A→B 与 B→A 天然命中同一频道，
/// 重复打开同一频道也天然幂等。若改成随机标识，「两人各开一个私聊频道」会让消息历史裂成两半，
/// 且没有任何错误会暴露这个 bug——只在玩家投诉「看不到对方的消息」时才发现。
/// </para>
/// <para>
/// 维护约束：<see cref="Participants"/> 只对 <see cref="OnlineChatChannelKind.Direct"/> 是权威的
/// （双方由频道自带）；Party / Group 的成员随队伍与群组的成员变动而变，**不落在这里**
/// （落下来就会有陈旧成员读到已退出后的消息），而是每次读取时经
/// <see cref="IOnlineChannelMembershipProbe"/> 向归属域取当前事实。
/// </para>
/// </summary>
public sealed class OnlineChatChannel
{
    /// <summary>
    /// 获取或设置频道标识（确定性派生，全局唯一）。
    /// </summary>
    public string ChannelId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置频道类型。
    /// </summary>
    public OnlineChatChannelKind Kind
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置绑定主体标识（Party 绑 PartyId、Group 绑 GroupId；Direct 与 Global 为空字符串）。
    /// </summary>
    public string BoundId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置参与玩家集合（仅 Direct 频道权威；按大小排序的规范化对）。
    /// </summary>
    public List<long> Participants
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置频道创建时刻（UTC 毫秒）。
    /// </summary>
    public long CreatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 判定玩家是否在频道内（仅 Direct 频道有权威结论；其余类型返回 <c>false</c>，
    /// 调用方必须改走 <see cref="IOnlineChannelMembershipProbe"/>）。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>在频道内返回 <c>true</c>。</returns>
    public bool Contains(long playerId)
    {
        if (Participants == null)
        {
            return false;
        }

        foreach (var participant in Participants)
        {
            if (participant == playerId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 取定向私聊频道的对端玩家（仅 <see cref="OnlineChatChannelKind.Direct"/> 有结论）。
    /// </summary>
    /// <param name="playerId">本方玩家标识。</param>
    /// <returns>对端玩家标识；非参与方或非私聊频道返回 <c>0</c>。</returns>
    public long OtherOf(long playerId)
    {
        if (!Contains(playerId))
        {
            return 0;
        }

        foreach (var participant in Participants)
        {
            if (participant != playerId)
            {
                return participant;
            }
        }

        return 0;
    }

    /// <summary>
    /// 构造深拷贝（存储层与调用方不得共享同一实例）。
    /// </summary>
    /// <returns>字段级拷贝（参与者集合为新实例）。</returns>
    public OnlineChatChannel Copy()
    {
        var copy = new OnlineChatChannel
        {
            ChannelId = ChannelId,
            TenantId = TenantId,
            AppId = AppId,
            Kind = Kind,
            BoundId = BoundId,
            CreatedAtTime = CreatedAtTime,
            Participants = new List<long>(),
        };
        if (Participants != null)
        {
            foreach (var participant in Participants)
            {
                copy.Participants.Add(participant);
            }
        }

        return copy;
    }
}
