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

using System.Collections.Generic;

namespace GameFrameX.Online.Match;

/// <summary>
/// 对局快照（vault:C6 S5.2「重连成功先发送完整快照，再发送客户端缺失的增量」）。
/// <para>
/// 维护约束（红线）：快照是**服务端权威状态在某个服务器序号上的完整切片**——
/// <see cref="ServerSequence"/> 标识该切片，客户端必须用它作为后续增量的起点；
/// 快照只含必要状态（成员表 + 玩法不透明载荷），不放事件日志（vault 风险缓解：快照大小需受控）。
/// </para>
/// </summary>
public sealed class OnlineMatchSnapshot
{
    /// <summary>
    /// 获取或设置对局标识。
    /// </summary>
    public string MatchId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置对局状态。
    /// </summary>
    public OnlineMatchState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置快照对应的服务器序号。
    /// </summary>
    public long ServerSequence
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
    /// 获取或设置创建时刻（UTC 毫秒）。
    /// </summary>
    public long CreatedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置快照生成时刻（UTC 毫秒）。
    /// </summary>
    public long UpdatedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置成员列表（含断线成员——断线不立即等于退出）。
    /// </summary>
    public List<OnlineMatchMember> Members
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置玩法私有状态载荷（不透明字节；可为空）。
    /// </summary>
    public byte[] GameState
    {
        get;
        set;
    }

    /// <summary>
    /// 复制快照（成员逐项深拷贝，载荷逐字节拷贝）。
    /// </summary>
    /// <returns>快照副本。</returns>
    public OnlineMatchSnapshot Copy()
    {
        var members = new List<OnlineMatchMember>();
        if (Members != null)
        {
            foreach (var member in Members)
            {
                members.Add(member == null ? null : member.Copy());
            }
        }

        return new OnlineMatchSnapshot
        {
            MatchId = MatchId,
            State = State,
            ServerSequence = ServerSequence,
            Mode = Mode,
            Region = Region,
            CreatedTime = CreatedTime,
            UpdatedTime = UpdatedTime,
            Members = members,
            GameState = GameState == null ? null : (byte[])GameState.Clone(),
        };
    }
}
