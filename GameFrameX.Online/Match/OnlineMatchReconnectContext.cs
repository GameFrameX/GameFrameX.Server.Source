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
/// 重连上下文（vault:C6「重连上下文至少保存 MatchId、PlayerId、ReconnectToken、最后确认序号、
/// 当前快照、未确认输入和重连截止时间」）。
/// <para>
/// 维护约束（红线）：<see cref="LastAcknowledgedSequence"/> 是**客户端自称**的进度，
/// 服务端只用它计算补发区间——它不参与任何权威判定，也不能让客户端「跳过」未确认的服务器事件：
/// 若该序号早于事件日志下界，服务端退回全量快照而非假装增量完整（VC-5.6 的序号连续性由此保证）。
/// </para>
/// </summary>
public sealed class OnlineMatchReconnectContext
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
    /// 获取或设置玩家标识。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置服务端签发的重连令牌。
    /// </summary>
    public string ReconnectToken
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置客户端最后确认的服务器序号。
    /// </summary>
    public long LastAcknowledgedSequence
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置客户端未获确认的输入（重连后由客户端按需重放；服务端按重复包规则幂等处理）。
    /// </summary>
    public List<OnlineMatchInput> UnacknowledgedInputs
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置重连截止时刻（UTC 毫秒；服务端权威值以成员表为准，此处仅供客户端对照）。
    /// </summary>
    public long ReconnectDeadlineTime
    {
        get;
        set;
    }
}
