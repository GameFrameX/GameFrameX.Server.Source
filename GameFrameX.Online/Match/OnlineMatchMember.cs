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

namespace GameFrameX.Online.Match;

/// <summary>
/// 对局成员（vault:C6 S5.3 成员流程：加入 / 退出 / 踢出 / 准备 / 开始）。
/// <para>
/// 维护约束（红线）：<see cref="LastAckSequence"/> 与 <see cref="LastClientSequence"/> 是**重连补齐的唯一依据**——
/// 重连时服务端据此计算需要补发的增量区间（VC-5.6「序号连续」）；两者只能由服务端在成功接受输入后推进，
/// 客户端上报的序号不直接写入本字段。断线成员保留在成员表中直至窗口超时，故成员表长度不等于在线人数。
/// </para>
/// </summary>
public sealed class OnlineMatchMember
{
    /// <summary>
    /// 获取或设置玩家标识。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置成员状态。
    /// </summary>
    public OnlineMatchMemberState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置加入时刻（UTC 毫秒）。
    /// </summary>
    public long JoinedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近一次状态变更时刻（UTC 毫秒）。
    /// </summary>
    public long StateChangedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置断线时刻（UTC 毫秒；未断线为 0）。
    /// </summary>
    public long DisconnectedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置重连截止时刻（UTC 毫秒；超过该时刻转为退出/托管）。
    /// </summary>
    public long ReconnectDeadlineTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置服务端已确认到的最大服务器序号（重连增量补发的下界）。
    /// </summary>
    public long LastAckSequence
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置服务端已接受的最大客户端序号（重复包判定的依据，VC-5.4）。
    /// </summary>
    public long LastClientSequence
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置当前重连令牌（未断线为 null 或空）。
    /// </summary>
    public string ReconnectToken
    {
        get;
        set;
    }

    /// <summary>
    /// 复制成员（存储与快照层防御性深拷贝使用）。
    /// </summary>
    /// <returns>成员副本。</returns>
    public OnlineMatchMember Copy()
    {
        return new OnlineMatchMember
        {
            PlayerId = PlayerId,
            State = State,
            JoinedTime = JoinedTime,
            StateChangedTime = StateChangedTime,
            DisconnectedTime = DisconnectedTime,
            ReconnectDeadlineTime = ReconnectDeadlineTime,
            LastAckSequence = LastAckSequence,
            LastClientSequence = LastClientSequence,
            ReconnectToken = ReconnectToken,
        };
    }
}
