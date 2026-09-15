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
/// 频道已读位点（vault:C7 S6.5/S6.6）。
/// <para>
/// 维护约束：位点记为**游标对**（<see cref="LastReadSentAtTime"/> + <see cref="LastReadSequence"/>）
/// 而非「已读的第 N 条」——序号偏移在消息撤回、清理、补拉之后会整体错位，而
/// <c>(SentAtTime, Sequence)</c> 是单调的，永远不会因后续动作而改变含义。
/// <see cref="LastReadMessageId"/> 只作展示与排障锚点，**不参与计算**。
/// </para>
/// </summary>
public sealed class OnlineChatReadMark
{
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
    /// 获取或设置玩家标识。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置频道标识。
    /// </summary>
    public string ChannelId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最后已读消息标识（展示与排障锚点；不参与排序比较）。
    /// </summary>
    public string LastReadMessageId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最后已读位置的发送时刻（UTC 毫秒；未读任何消息为 <c>0</c>）。
    /// </summary>
    public long LastReadSentAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最后已读位置的频道内序号（未读任何消息为 <c>0</c>）。
    /// </summary>
    public long LastReadSequence
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置位点更新时刻（UTC 毫秒）。
    /// </summary>
    public long UpdatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 判定给定消息位置是否**不晚于**本位点（位点比较口径的唯一实现点，服务层与存储层共用）。
    /// <para>
    /// 比较键与历史游标同源：先比 <c>SentAtTime</c>，同毫秒再比频道内 <c>Sequence</c>。
    /// 「不晚于」即「推进位点会被这条已读请求推回去」，调用方据此拒绝回落。
    /// </para>
    /// </summary>
    /// <param name="sentAtTime">消息发送时刻（UTC 毫秒）。</param>
    /// <param name="sequence">消息频道内序号。</param>
    /// <returns>不晚于本位点返回 <c>true</c>。</returns>
    public bool IsNotAfterPosition(long sentAtTime, long sequence)
    {
        if (sentAtTime != LastReadSentAtTime)
        {
            return sentAtTime < LastReadSentAtTime;
        }

        return sequence <= LastReadSequence;
    }

    /// <summary>
    /// 构造深拷贝（存储层与调用方不得共享同一实例）。
    /// </summary>
    /// <returns>字段级拷贝。</returns>
    public OnlineChatReadMark Copy()
    {
        return new OnlineChatReadMark
        {
            TenantId = TenantId,
            AppId = AppId,
            PlayerId = PlayerId,
            ChannelId = ChannelId,
            LastReadMessageId = LastReadMessageId,
            LastReadSentAtTime = LastReadSentAtTime,
            LastReadSequence = LastReadSequence,
            UpdatedAtTime = UpdatedAtTime,
        };
    }
}
