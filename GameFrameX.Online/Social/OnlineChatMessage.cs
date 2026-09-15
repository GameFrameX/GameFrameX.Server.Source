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
/// 聊天消息（vault:C7 S6.5/S6.6）。
/// <para>
/// 维护约束（分页稳定性的地基，VC-6.10）：排序键是 <c>(SentAtTime, Sequence)</c>，其中
/// <see cref="Sequence"/> 由存储层按频道单调递增分配。**为什么不能只按时间排序**：
/// 同一毫秒内的多条消息时间相等，若再按消息标识（随机 GUID）打破平局，
/// 「翻页期间新到达的同毫秒消息」可能排到游标之前而被永久跳过。Sequence 严格递增，
/// 保证新消息永远排在既有消息之后——这是「翻页不重复、不漏项」的实现依据。
/// </para>
/// </summary>
public sealed class OnlineChatMessage
{
    /// <summary>
    /// 获取或设置消息标识。
    /// </summary>
    public string MessageId
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
    /// 获取或设置所属频道标识。
    /// </summary>
    public string ChannelId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置所属频道类型（冗余落库：历史补拉与审计不必回查频道记录）。
    /// </summary>
    public OnlineChatChannelKind ChannelKind
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置发送者。
    /// </summary>
    public long SenderId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置消息内容（已撤回消息的内容不再下发，但字段本身保留以便审计）。
    /// </summary>
    public string Content
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置发送时刻（UTC 毫秒；**由存储层在追加时落定并在频道内保证单调不减**，
    /// 落库后任何调用方都不得改写——改写会让既有的已读位点与游标失去含义）。
    /// </summary>
    public long SentAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置频道内单调递增的序号（**由存储层在追加时分配**；排序平局判定与游标偏移的唯一依据）。
    /// </summary>
    public long Sequence
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置消息状态。
    /// </summary>
    public OnlineChatMessageState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置撤回时刻（UTC 毫秒；未撤回为 <c>0</c>）。
    /// </summary>
    public long RecalledAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置撤回人（未撤回为 <c>0</c>；只有发送者本人可撤回，故非零时必等于发送者）。
    /// </summary>
    public long RecalledByPlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置发送方提供的去重键（可空；非空时存储层按频道内唯一约束做重发幂等）。
    /// </summary>
    public string DedupeKey
    {
        get;
        set;
    }

    /// <summary>
    /// 构造深拷贝（存储层与调用方不得共享同一实例）。
    /// </summary>
    /// <returns>字段级拷贝。</returns>
    public OnlineChatMessage Copy()
    {
        return new OnlineChatMessage
        {
            MessageId = MessageId,
            TenantId = TenantId,
            AppId = AppId,
            ChannelId = ChannelId,
            ChannelKind = ChannelKind,
            SenderId = SenderId,
            Content = Content,
            State = State,
            RecalledAtTime = RecalledAtTime,
            RecalledByPlayerId = RecalledByPlayerId,
            DedupeKey = DedupeKey,
            SentAtTime = SentAtTime,
            Sequence = Sequence,
        };
    }
}
